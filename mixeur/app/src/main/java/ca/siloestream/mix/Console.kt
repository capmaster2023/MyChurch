package ca.siloestream.mix

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.wifi.WifiManager
import android.os.Build
import android.os.Handler
import android.os.Looper
import androidx.compose.runtime.MutableState
import androidx.compose.runtime.mutableStateOf
import kotlinx.coroutines.flow.MutableStateFlow
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.net.SocketTimeoutException
import java.security.SecureRandom

/** Une voie de la console : « line » 3 → line/ch3. */
data class Voie(val groupe: String, val n: Int) {
    val base get() = "$groupe/ch$n"
}

/** Ce que la console possède (calculé une fois l'état reçu). */
data class Structure(
    val entrees: List<Voie> = emptyList(),
    val aux: Int = 0,
    val fx: Int = 0,
    val subs: Int = 0,
    val principal: Boolean = false,
    val groupesMute: Int = 0,
)

/** La console connectée, vue par l'interface (état Compose, mis à jour sur le fil principal). */
class Console(private val ctx: Context) {
    val etat = MutableStateFlow(EtatLien.ARRET)
    val detail = MutableStateFlow("")
    val structure = MutableStateFlow(Structure())
    val hote = MutableStateFlow("")
    val nom = MutableStateFlow("")
    val journal = MutableStateFlow<List<String>>(emptyList())
    /**
     * Une cellule observable par paramètre (chemin → Float de 0 à 1, ou String) : une voie ne se redessine
     * que si ses propres paramètres changent. Fil principal seulement.
     */
    private val cellules = HashMap<String, MutableState<Any?>>()
    private val connues = HashSet<String>()
    /** groupe de vu-mètres → niveaux (lus pendant le dessin : seuls les vu-mètres se redessinent) */
    private val niveaux = HashMap<Int, MutableState<FloatArray?>>()

    fun cellule(cle: String): MutableState<Any?> = cellules.getOrPut(cle) { mutableStateOf(null) }
    fun niveau(groupe: Int): MutableState<FloatArray?> = niveaux.getOrPut(groupe) { mutableStateOf(null) }

    private fun mettre(k: String, v: Any): Boolean {
        val nouvelle = connues.add(k)
        val c = cellule(k)
        if (c.value != v) c.value = v
        return nouvelle
    }

    private fun toutEffacer() {
        for (c in cellules.values) c.value = null
        for (n in niveaux.values) n.value = null
        connues.clear()
    }

    private val prefs = ctx.getSharedPreferences("mix", Context.MODE_PRIVATE)
    private val principal = Handler(Looper.getMainLooper())
    private var lien: Lien? = null
    /** Chaque connexion a son numéro : les nouvelles d'une ancienne liaison sont ignorées. */
    private var generation = 0

    val derniereHote: String get() = prefs.getString("hote", "") ?: ""
    val dernierNom: String get() = prefs.getString("nom", "") ?: ""

    private fun identifiant(): String {
        prefs.getString("id", null)?.let { return it }
        val b = ByteArray(8).also { SecureRandom().nextBytes(it) }
        val id = b.joinToString("") { "%02x".format(it.toInt() and 0xff) }
        prefs.edit().putString("id", id).apply()
        return id
    }

    private fun ecrireJournal(l: String) {
        val heure = java.text.SimpleDateFormat("HH:mm:ss", java.util.Locale.CANADA_FRENCH).format(java.util.Date())
        journal.value = (journal.value + "$heure  $l").takeLast(300)
    }

    /** Le réseau Wi-Fi (ou Ethernet) : la console n'est pas sur Internet, les données mobiles ne la trouvent pas. */
    fun reseauLocal(): Network? {
        val cm = ctx.getSystemService(ConnectivityManager::class.java) ?: return null
        @Suppress("DEPRECATION")
        return cm.allNetworks.firstOrNull { n ->
            cm.getNetworkCapabilities(n)?.let { it.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) || it.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET) } == true
        }
    }

    fun connecter(adresse: String, nomConsole: String) {
        deconnecter()
        val h = adresse.trim()
        if (h.isEmpty()) return
        toutEffacer()
        structure.value = Structure()
        hote.value = h
        nom.value = nomConsole.ifBlank { h }
        prefs.edit().putString("hote", h).putString("nom", nomConsole).apply()
        ecrireJournal("connexion à $h")
        val g = ++generation
        fun surPrincipal(bloc: () -> Unit) {
            principal.post { if (g == generation) bloc() }
        }
        val ecouteur = object : Lien.Ecouteur {
            override fun etat(e: EtatLien, detail: String) = surPrincipal {
                this@Console.etat.value = e
                this@Console.detail.value = detail
            }

            override fun etatComplet(valeurs: Map<String, Any>) = surPrincipal {
                for ((k, v) in valeurs) mettre(k, v)
                (valeurs["global/mixer_name"] as? String)?.takeIf { it.isNotBlank() }?.let { nom.value = it }
                calculerStructure()
            }

            override fun valeurs(maj: Map<String, Any>) = surPrincipal {
                var nouvelle = false
                for ((k, v) in maj) if (mettre(k, v)) nouvelle = true
                if (nouvelle) calculerStructurePlusTard()
            }

            override fun niveaux(n: Map<Int, FloatArray>) = surPrincipal { for ((g, v) in n) niveau(g).value = v }

            override fun journal(ligne: String) = surPrincipal { ecrireJournal(ligne) }
        }
        lien = Lien(
            hote = h,
            port = Uc.PORT_TCP,
            nomAppareil = "Siloe Mix " + Build.MODEL,
            identifiant = identifiant(),
            ecouteur = ecouteur,
            preparer = { s ->
                try { reseauLocal()?.bindSocket(s) } catch (e: Exception) { }
            },
            preparerUdp = { u ->
                try { reseauLocal()?.bindSocket(u) } catch (e: Exception) { }
            },
        ).also { it.demarrer() }
        etat.value = EtatLien.CONNEXION
    }

    fun deconnecter() {
        val l = lien ?: return
        lien = null
        generation++
        Thread { l.arreter() }.start()
        etat.value = EtatLien.ARRET
        detail.value = ""
        ecrireJournal("déconnecté")
    }

    // ------------------------------------------------------------------ lecture et réglage

    fun f(cle: String): Float? = cellule(cle).value as? Float
    fun t(cle: String): String = cellule(cle).value as? String ?: ""
    /** Non observé : sert à bâtir les listes de voies, recalculées quand la structure change. */
    fun existe(cle: String): Boolean = cle in connues

    fun regler(cle: String, v: Float) {
        mettre(cle, v)
        lien?.regler(cle, v)
    }

    fun basculer(cle: String) {
        regler(cle, if ((f(cle) ?: 0f) >= 0.5f) 0f else 1f)
    }

    private var structureEnAttente = false

    private fun calculerStructurePlusTard() {
        if (structureEnAttente) return
        structureEnAttente = true
        principal.postDelayed({
            structureEnAttente = false
            calculerStructure()
        }, 400)
    }

    private fun calculerStructure() {
        val cles = connues
        fun voies(g: String): List<Voie> {
            val l = ArrayList<Voie>()
            for (n in 1..64) {
                val b = "$g/ch$n"
                if ("$b/volume" in cles || "$b/mute" in cles) l.add(Voie(g, n))
            }
            return l
        }
        var mutes = 0
        for (g in 1..8) if ("mutegroup/mutegroup$g" in cles) mutes = g
        structure.value = Structure(
            entrees = voies("line") + voies("return") + voies("fxreturn") + voies("talkback"),
            aux = voies("aux").size,
            fx = voies("fxbus").size,
            subs = voies("sub").size,
            principal = "main/ch1/volume" in cles,
            groupesMute = mutes,
        )
    }
}

/** Les consoles annoncées sur le Wi-Fi (UDP 47809). */
class Decouverte(private val ctx: Context) {
    data class Trouvee(val nom: String, val modele: String, val serie: String, val ip: String, val vu: Long)

    val trouvees = MutableStateFlow<List<Trouvee>>(emptyList())
    val erreur = MutableStateFlow("")
    @Volatile private var actif = false
    private var fil: Thread? = null
    private val principal = Handler(Looper.getMainLooper())

    fun demarrer() {
        if (actif) return
        actif = true
        fil = Thread({ boucle() }, "mix-decouverte").apply {
            isDaemon = true
            start()
        }
    }

    fun arreter() {
        actif = false
        fil?.interrupt()
    }

    private fun boucle() {
        val wifi = ctx.applicationContext.getSystemService(WifiManager::class.java)
        val verrou = wifi?.createMulticastLock("siloe-mix")?.apply {
            setReferenceCounted(false)
            try { acquire() } catch (e: Exception) { }
        }
        var prise: DatagramSocket? = null
        try {
            prise = DatagramSocket(null).apply {
                reuseAddress = true
                broadcast = true
                soTimeout = 1000
                bind(InetSocketAddress(Uc.PORT_DECOUVERTE))
            }
            principal.post { erreur.value = "" }
            val p = ByteArray(2048)
            val paquet = DatagramPacket(p, p.size)
            while (actif) {
                try {
                    prise.receive(paquet)
                    val a = Uc.lireAnnonce(p, paquet.length)
                    val ip = paquet.address?.hostAddress
                    if (a != null && ip != null) {
                        val t = Trouvee(a.nom.ifBlank { a.modele }, a.modele, a.serie, ip, System.currentTimeMillis())
                        principal.post { ajouter(t) }
                    }
                } catch (e: SocketTimeoutException) {
                    principal.post { nettoyer() }
                }
            }
        } catch (e: Exception) {
            principal.post { erreur.value = "Recherche automatique indisponible (${e.message ?: "port occupé"}) : entrez l'adresse IP de la console." }
        } finally {
            prise?.close()
            try { verrou?.release() } catch (e: Exception) { }
        }
    }

    private fun ajouter(t: Trouvee) {
        trouvees.value = (trouvees.value.filter { it.ip != t.ip && (t.serie.isBlank() || it.serie != t.serie) } + t).sortedBy { it.nom }
    }

    private fun nettoyer() {
        val limite = System.currentTimeMillis() - 12000
        if (trouvees.value.any { it.vu < limite }) trouvees.value = trouvees.value.filter { it.vu >= limite }
    }
}
