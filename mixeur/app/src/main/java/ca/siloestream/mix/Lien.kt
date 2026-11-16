package ca.siloestream.mix

import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.net.Socket
import java.net.SocketException
import java.net.SocketTimeoutException
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledExecutorService
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

enum class EtatLien { ARRET, CONNEXION, SYNCHRO, CONNECTE, PERDU }

/**
 * La liaison avec une console : connexion TCP (port 53000), abonnement, état complet, mises à jour,
 * envoi des réglages (regroupés : seule la dernière valeur d'un fader part), maintien et reconnexion automatique.
 * Aucune dépendance Android : testé sur ordinateur contre une console simulée.
 */
class Lien(
    private val hote: String,
    /** Ports essayés à tour de rôle tant qu'aucun ne répond (celui annoncé par la console, puis 53000). */
    private val ports: List<Int>,
    private val nomAppareil: String,
    private val identifiant: String,
    private val ecouteur: Ecouteur,
    /** Android : attacher la prise au Wi-Fi (sinon elle peut partir par les données mobiles). */
    private val preparer: (Socket) -> Unit = {},
    private val preparerUdp: (DatagramSocket) -> Unit = {},
    private val vuMetres: Boolean = true,
) {
    interface Ecouteur {
        fun etat(e: EtatLien, detail: String)
        fun etatComplet(valeurs: Map<String, Any>)
        fun valeurs(maj: Map<String, Any>)
        fun niveaux(n: Map<Int, FloatArray>)
        fun journal(ligne: String)
    }

    private val actif = AtomicBoolean(false)
    private var fil: Thread? = null
    @Volatile private var prise: Socket? = null
    @Volatile private var sortie: OutputStream? = null
    @Volatile private var udp: DatagramSocket? = null
    @Volatile private var dernierRecu = 0L
    @Volatile private var etatActuel = EtatLien.ARRET
    private val verrouEcriture = Any()
    private var envoyeur: ScheduledExecutorService? = null
    private val attente = ConcurrentHashMap<String, Float>()
    private val planifie = AtomicBoolean(false)
    /** Réglages faits ici : on ignore l'écho de la console pendant un instant (le fader ne « saute » pas). */
    private val touches = ConcurrentHashMap<String, Long>()
    private var morceaux: java.io.ByteArrayOutputStream? = null
    private var indexPort = 0
    private var sondes = 0

    fun demarrer() {
        if (!actif.compareAndSet(false, true)) return
        envoyeur = Executors.newSingleThreadScheduledExecutor { r -> Thread(r, "mix-envoi").apply { isDaemon = true } }
        envoyeur?.scheduleWithFixedDelay({ maintien() }, 1, 1, TimeUnit.SECONDS)
        fil = Thread({ boucle() }, "mix-lien").apply {
            isDaemon = true
            start()
        }
    }

    fun arreter() {
        if (!actif.compareAndSet(true, false)) return
        try { ecrire(Uc.json("{\"id\": \"Unsubscribe\"}")) } catch (e: Exception) {}
        fermer()
        envoyeur?.shutdownNow()
        envoyeur = null
        fil?.interrupt()
        changer(EtatLien.ARRET, "")
    }

    /** Régler un paramètre (0 à 1). Appelable de n'importe quel fil. */
    fun regler(cle: String, v: Float) {
        touches[cle] = System.currentTimeMillis()
        attente[cle] = v
        if (planifie.compareAndSet(false, true)) {
            try {
                envoyeur?.schedule({
                    planifie.set(false)
                    vider()
                }, 12, TimeUnit.MILLISECONDS) ?: planifie.set(false)
            } catch (e: Exception) {
                planifie.set(false)
            }
        }
    }

    private fun vider() {
        for (k in attente.keys.toList()) {
            val v = attente.remove(k) ?: continue
            ecrire(Uc.valeur(k, v))
        }
    }

    private fun ecrire(b: ByteArray) {
        val o = sortie ?: return
        try {
            synchronized(verrouEcriture) {
                o.write(b)
                o.flush()
            }
        } catch (e: IOException) {
            journal("envoi impossible : ${e.message}")
            fermer()
        }
    }

    private fun maintien() {
        if (sortie == null) return
        ecrire(Uc.maintien())
        if (++sondes % 2 == 0) ecrire(Uc.sonde(0x5100 + (sondes / 2) % 0xff))
        if (dernierRecu > 0 && System.currentTimeMillis() - dernierRecu > 8000) {
            journal("plus de nouvelles de la console depuis 8 s : reconnexion")
            fermer()
        }
    }

    private fun changer(e: EtatLien, detail: String) {
        etatActuel = e
        ecouteur.etat(e, detail)
    }

    private fun journal(l: String) = ecouteur.journal(l)

    private fun fermer() {
        val s = prise
        prise = null
        sortie = null
        try { s?.close() } catch (e: IOException) {}
        val u = udp
        udp = null
        u?.close()
    }

    private fun boucle() {
        var essais = 0
        while (actif.get()) {
            changer(if (essais == 0) EtatLien.CONNEXION else EtatLien.PERDU, if (essais == 0) "" else "reconnexion…")
            val port = ports[indexPort % ports.size]
            var relie = false
            try {
                val s = Socket()
                preparer(s)
                s.tcpNoDelay = true
                s.connect(InetSocketAddress(hote, port), 4000)
                relie = true
                s.soTimeout = 500
                prise = s
                sortie = s.getOutputStream()
                dernierRecu = System.currentTimeMillis()
                morceaux = null
                journal("connecté à $hote:$port")
                changer(EtatLien.SYNCHRO, "")
                ecrire(Uc.abonnement(nomAppareil, identifiant))
                if (vuMetres) demarrerMetres()
                essais = 0
                lire(s.getInputStream())
            } catch (e: IOException) {
                if (actif.get()) journal("liaison ($hote:$port) : ${e.javaClass.simpleName} ${e.message ?: ""}".trim())
                if (!relie) indexPort++
            } finally {
                fermer()
            }
            if (!actif.get()) break
            essais++
            changer(EtatLien.PERDU, if (essais > 2) "Console injoignable : vérifiez le Wi-Fi et l'adresse." else "reconnexion…")
            try {
                Thread.sleep(minOf(500L * essais, 4000L))
            } catch (e: InterruptedException) {
                break
            }
        }
    }

    private fun lire(entree: InputStream) {
        val buf = ByteArray(64 * 1024)
        val decoupeur = Uc.Decoupeur()
        val lot = HashMap<String, Any>()
        while (actif.get() && prise != null) {
            val n = try {
                entree.read(buf)
            } catch (e: SocketTimeoutException) {
                continue
            }
            if (n < 0) throw SocketException("fermée par la console")
            dernierRecu = System.currentTimeMillis()
            decoupeur.ajouter(buf, n) { m -> traiter(m, lot) }
            if (lot.isNotEmpty()) {
                ecouteur.valeurs(HashMap(lot))
                lot.clear()
            }
        }
    }

    private fun recent(cle: String): Boolean {
        val t = touches[cle] ?: return false
        return System.currentTimeMillis() - t < 700
    }

    private fun traiter(m: Uc.Message, lot: MutableMap<String, Any>) {
        when (m.code) {
            "JM" -> {
                val t = Uc.texteJson(m.data)
                val id = Regex("\"id\"\\s*:\\s*\"([^\"]+)\"").find(t)?.groupValues?.get(1) ?: "?"
                journal("JSON : $id")
                if (id == "SubscriptionReply" && etatActuel == EtatLien.SYNCHRO) changer(EtatLien.SYNCHRO, "réception de l'état de la console…")
            }
            "ZB" -> {
                val v = Uc.lireEtat(m.data)
                if (v == null) journal("ZB illisible, début : " + m.data.take(24).joinToString(" ") { "%02x".format(it.toInt() and 0xff) })
                etatRecu(v, m.data.size)
            }
            "CK" -> morceau(m.data)
            "PV" -> Uc.lirePV(m.data)?.let { (k, v) -> if (!recent(k)) lot[k] = v }
            "PS" -> Uc.lirePS(m.data)?.let { (k, v) -> lot[k] = v }
            "PC" -> Uc.lirePC(m.data)?.let { (k, v) ->
                lot[k] = if (k.endsWith("color")) v.take(4).joinToString("") { "%02x".format(it.toInt() and 0xff) } else String(v, Charsets.UTF_8).trimEnd('\u0000')
            }
            "MS" -> Uc.lireFaders(m.data)?.forEach { (k, v) -> if (!recent(k)) lot[k] = v }
            else -> {}
        }
        if (etatActuel == EtatLien.SYNCHRO && (m.code == "PV" || m.code == "PS") && lot.size > 20) {
            // console sans état compressé : les valeurs arrivent une à une
            changer(EtatLien.CONNECTE, "")
        }
    }

    /** CK : état complet en morceaux (4 octets, début, total, taille, données). */
    private fun morceau(d: ByteArray) {
        if (d.size < 16) return
        val bb = java.nio.ByteBuffer.wrap(d).order(java.nio.ByteOrder.LITTLE_ENDIAN)
        val debut = bb.getInt(4)
        val total = bb.getInt(8)
        val taille = bb.getInt(12)
        if (debut == 0 || morceaux == null) morceaux = java.io.ByteArrayOutputStream(maxOf(total, 0))
        morceaux?.write(d, 16, d.size - 16)
        if (debut + taille >= total) {
            val tout = morceaux?.toByteArray()
            morceaux = null
            if (tout != null) etatRecu(Uc.decompresser(tout)?.let { Uc.aplatirArbre(it) }, tout.size)
        }
    }

    private fun etatRecu(v: Map<String, Any>?, taille: Int) {
        if (v == null) {
            journal("état complet illisible ($taille octets)")
            return
        }
        journal("état complet : ${v.size} paramètres")
        ecouteur.etatComplet(v)
        changer(EtatLien.CONNECTE, "")
    }

    private fun demarrerMetres() {
        try {
            val u = DatagramSocket(null)
            u.reuseAddress = true
            preparerUdp(u)
            u.bind(InetSocketAddress(0))
            u.soTimeout = 1000
            udp = u
            ecrire(Uc.bonjourMetres(u.localPort))
            Thread({
                val p = ByteArray(8192)
                val paquet = DatagramPacket(p, p.size)
                var dernier = 0L
                while (actif.get() && udp === u) {
                    try {
                        u.receive(paquet)
                    } catch (e: SocketTimeoutException) {
                        continue
                    } catch (e: IOException) {
                        break
                    }
                    val maintenant = System.currentTimeMillis()
                    if (maintenant - dernier < 45) continue
                    val n = Uc.lireNiveaux(p, paquet.length) ?: continue
                    dernier = maintenant
                    ecouteur.niveaux(n)
                }
            }, "mix-metres").apply {
                isDaemon = true
                start()
            }
        } catch (e: IOException) {
            journal("vu-mètres indisponibles : ${e.message}")
        }
    }
}
