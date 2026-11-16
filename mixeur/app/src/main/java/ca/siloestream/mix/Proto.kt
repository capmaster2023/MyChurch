package ca.siloestream.mix

import java.io.ByteArrayOutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.zip.DataFormatException
import java.util.zip.Inflater

/**
 * Le réseau des consoles PreSonus StudioLive (Series III et AI) : « UCNet ».
 *
 * Chaque message : « UC » 00 01, longueur (u16 LE), code de 2 lettres, 4 octets d'identité, puis les données.
 * Codes utiles : JM (JSON), PV (valeur d'un paramètre), PS / PC (texte), ZB (état complet compressé),
 * CK (état complet en morceaux), MS (positions des faders, vu-mètres), KA (maintien), UM (bonjour des vu-mètres).
 *
 * Les paramètres ont un chemin (« line/ch1/volume », « aux/ch2/mute », « line/ch3/aux2 ») et une valeur
 * normalisée entre 0 et 1 (1 = activé pour les boutons).
 */
object Uc {
    val ENTETE = byteArrayOf(0x55, 0x43, 0x00, 0x01)
    const val PORT_TCP = 53000
    const val PORT_DECOUVERTE = 47809

    fun paquet(code: String, data: ByteArray = ByteArray(0), a: Int = 0x68, b: Int = 0x65): ByteArray {
        val n = 2 + 4 + data.size
        val bb = ByteBuffer.allocate(6 + n).order(ByteOrder.LITTLE_ENDIAN)
        bb.put(ENTETE)
        bb.putShort(n.toShort())
        bb.put(code.toByteArray(Charsets.US_ASCII))
        bb.put(a.toByte()).put(0).put(b.toByte()).put(0)
        bb.put(data)
        return bb.array()
    }

    fun json(texte: String): ByteArray {
        val j = texte.toByteArray(Charsets.UTF_8)
        val bb = ByteBuffer.allocate(4 + j.size).order(ByteOrder.LITTLE_ENDIAN)
        bb.putInt(j.size)
        bb.put(j)
        return paquet("JM", bb.array())
    }

    /** PV : « chemin » 00 00 00 puis la valeur (float LE). */
    fun valeur(cle: String, v: Float): ByteArray {
        val k = cle.toByteArray(Charsets.UTF_8)
        val bb = ByteBuffer.allocate(k.size + 3 + 4).order(ByteOrder.LITTLE_ENDIAN)
        bb.put(k).put(0).put(0).put(0)
        bb.putFloat(v)
        return paquet("PV", bb.array())
    }

    /** Bonjour des vu-mètres : la console envoie les niveaux en UDP vers ce port. */
    fun bonjourMetres(portUdp: Int): ByteArray {
        val bb = ByteBuffer.allocate(2).order(ByteOrder.LITTLE_ENDIAN)
        bb.putShort(portUdp.toShort())
        return paquet("UM", bb.array(), a = 0x00)
    }

    fun maintien(): ByteArray = paquet("KA")

    /** Petite demande de fichier : la console y répond, ce qui prouve que la liaison est vivante. */
    fun sonde(id: Int): ByteArray {
        val bb = ByteBuffer.allocate(2 + 4 + 2).order(ByteOrder.BIG_ENDIAN)
        bb.putShort(id.toShort())
        bb.put(byteArrayOf(0x46, 0x74, 0x62, 0x72, 0, 0))
        return paquet("FR", bb.array())
    }

    /** Abonnement : la console répond « SubscriptionReply » puis envoie son état complet (ZB). Texte ASCII seulement. */
    fun abonnement(nomAppareil: String, identifiant: String): ByteArray {
        fun t(x: String) = "\"" + x.filter { it.code in 32..126 && it != '"' && it != '\\' } + "\""
        val j = "{\n" +
            " \"id\": \"Subscribe\",\n" +
            " \"clientName\": \"UC-Surface\",\n" +
            " \"clientInternalName\": \"ucremoteapp\",\n" +
            " \"clientType\": \"StudioLive API\",\n" +
            " \"clientDescription\": ${t(nomAppareil)},\n" +
            " \"clientIdentifier\": ${t(identifiant)},\n" +
            " \"clientOptions\": \"perm users levl redu rtan\",\n" +
            " \"clientEncoding\": 23106\n" +
            "}"
        return json(j)
    }

    fun texteJson(d: ByteArray): String = if (d.size > 4) String(d, 4, d.size - 4, Charsets.UTF_8) else ""

    /** Un message reçu : code et données (après les 12 octets d'en-tête). */
    class Message(val code: String, val data: ByteArray)

    /** Découpe le flux TCP en messages (un message peut arriver en plusieurs morceaux, ou plusieurs ensemble). */
    class Decoupeur {
        private var tampon = ByteArray(0)

        fun ajouter(octets: ByteArray, n: Int, sortie: (Message) -> Unit) {
            tampon = if (tampon.isEmpty()) octets.copyOf(n) else tampon + octets.copyOf(n)
            var i = 0
            while (true) {
                if (tampon.size - i < 6) break
                if (!enteteA(tampon, i)) {
                    // désynchronisé : on cherche le prochain « UC 00 01 »
                    val j = chercherEntete(tampon, i + 1)
                    if (j < 0) {
                        i = maxOf(i, tampon.size - 3)
                        break
                    }
                    i = j
                    continue
                }
                val longueur = (tampon[i + 4].toInt() and 0xff) or ((tampon[i + 5].toInt() and 0xff) shl 8)
                if (tampon.size - i < 6 + longueur) break
                if (longueur >= 6) {
                    val code = String(tampon, i + 6, 2, Charsets.US_ASCII)
                    val data = tampon.copyOfRange(i + 12, i + 6 + longueur)
                    sortie(Message(code, data))
                }
                i += 6 + longueur
            }
            tampon = if (i >= tampon.size) ByteArray(0) else tampon.copyOfRange(i, tampon.size)
        }
    }

    fun enteteA(b: ByteArray, i: Int) = b.size >= i + 4 && b[i] == 0x55.toByte() && b[i + 1] == 0x43.toByte() && b[i + 2] == 0.toByte() && b[i + 3] == 1.toByte()

    fun chercherEntete(b: ByteArray, depuis: Int): Int {
        for (i in depuis..b.size - 4) if (enteteA(b, i)) return i
        return -1
    }

    /** Chemin terminé par 00, puis 2 octets, puis la donnée. */
    private fun cle(d: ByteArray): Pair<String, Int>? {
        val z = d.indexOf(0)
        if (z <= 0) return null
        return String(d, 0, z, Charsets.UTF_8) to z
    }

    fun lirePV(d: ByteArray): Pair<String, Float>? {
        val (k, z) = cle(d) ?: return null
        val debut = z + 3
        if (d.size < debut + 4) return null
        return k to ByteBuffer.wrap(d, debut, 4).order(ByteOrder.LITTLE_ENDIAN).float
    }

    fun lirePS(d: ByteArray): Pair<String, String>? {
        val (k, z) = cle(d) ?: return null
        val debut = z + 3
        if (d.size < debut) return k to ""
        var fin = d.size
        while (fin > debut && d[fin - 1] == 0.toByte()) fin--
        return k to String(d, debut, fin - debut, Charsets.UTF_8)
    }

    fun lirePC(d: ByteArray): Pair<String, ByteArray>? {
        val (k, z) = cle(d) ?: return null
        val debut = z + 3
        if (d.size < debut) return null
        return k to d.copyOfRange(debut, d.size)
    }

    /** Décompresse (zlib, ou deflate brut au besoin). */
    fun decompresser(b: ByteArray): ByteArray? {
        for (brut in listOf(false, true)) {
            val inf = Inflater(brut)
            try {
                inf.setInput(b)
                val out = ByteArrayOutputStream(b.size * 6)
                val buf = ByteArray(64 * 1024)
                while (!inf.finished()) {
                    val n = inf.inflate(buf)
                    if (n == 0 && (inf.needsInput() || inf.needsDictionary())) break
                    out.write(buf, 0, n)
                }
                if (out.size() > 0) return out.toByteArray()
            } catch (e: DataFormatException) {
                // on essaie l'autre format
            } finally {
                inf.end()
            }
        }
        return null
    }

    /** État complet (ZB) : 4 octets, puis zlib. */
    fun lireEtat(d: ByteArray): Map<String, Any>? {
        val brut = (if (d.size > 4) decompresser(d.copyOfRange(4, d.size)) else null) ?: decompresser(d) ?: return null
        return aplatirArbre(brut)
    }

    fun aplatirArbre(ubjson: ByteArray): Map<String, Any>? {
        val racine = try {
            Ubjson(ubjson).lire()
        } catch (e: Exception) {
            null
        } as? Map<*, *> ?: return null
        val sortie = HashMap<String, Any>(8192)
        aplatir(racine, "", sortie)
        return sortie
    }

    private fun aplatir(n: Map<*, *>, prefixe: String, sortie: MutableMap<String, Any>) {
        fun chemin(k: Any?) = if (prefixe.isEmpty()) k.toString() else "$prefixe/$k"
        (n["values"] as? Map<*, *>)?.forEach { (k, v) ->
            val c = chemin(k)
            when (v) {
                is String -> sortie[c] = v
                is Boolean -> sortie[c] = if (v) 1f else 0f
                is Long -> sortie[c] = if (c.endsWith("color")) couleurHex(v) else v.toFloat()
                is Number -> sortie[c] = v.toFloat()
            }
        }
        (n["children"] as? Map<*, *>)?.forEach { (k, v) ->
            if (v is Map<*, *>) aplatir(v, chemin(k), sortie)
        }
    }

    /** Couleur d'une voie : les 4 octets de poids faible, dans l'ordre de la mémoire (R V B A). */
    fun couleurHex(v: Long): String {
        val b = ByteArray(4) { i -> ((v shr (8 * i)) and 0xff).toByte() }
        return b.joinToString("") { "%02x".format(it.toInt() and 0xff) }
    }

    /** MS « fdrs » : positions des faders (u16, 0 à 65535) par groupe de voies. */
    private val GROUPES_MS = mapOf(0 to "line", 1 to "return", 2 to "fxreturn", 3 to "talkback", 4 to "aux", 5 to "fxbus", 6 to "sub", 7 to "main", 8 to "mono", 11 to "master")

    fun lireFaders(d: ByteArray): Map<String, Float>? {
        if (d.size < 8 || String(d, 0, 4, Charsets.US_ASCII) != "fdrs") return null
        val bb = ByteBuffer.wrap(d).order(ByteOrder.LITTLE_ENDIAN)
        val n = bb.getShort(6).toInt() and 0xffff
        if (d.size < 8 + n * 2 + 1) return null
        val v = FloatArray(n) { i -> (bb.getShort(8 + i * 2).toInt() and 0xffff) / 65535f }
        var p = 8 + n * 2
        val groupes = d[p].toInt() and 0xff
        p++
        val sortie = HashMap<String, Float>()
        for (g in 0 until groupes) {
            if (d.size < p + 6) break
            val num = bb.getShort(p).toInt() and 0xffff
            val debut = bb.getShort(p + 2).toInt() and 0xffff
            val nb = bb.getShort(p + 4).toInt() and 0xffff
            p += 6
            val nom = GROUPES_MS[num] ?: continue
            for (i in 0 until nb) if (debut + i < n) sortie["$nom/ch${i + 1}/volume"] = v[debut + i]
        }
        return sortie
    }

    /**
     * Vu-mètres (UDP, MS « levl ») : valeurs u16 puis des groupes (numéro u16 BE, début, nombre).
     * Rend : numéro de groupe → niveaux (0 à 1).
     */
    fun lireNiveaux(p: ByteArray, n: Int): Map<Int, FloatArray>? {
        if (n < 21 || !enteteA(p, 0) || p[6] != 'M'.code.toByte() || p[7] != 'S'.code.toByte()) return null
        if (String(p, 12, 4, Charsets.US_ASCII) != "levl") return null
        val bb = ByteBuffer.wrap(p, 0, n).order(ByteOrder.LITTLE_ENDIAN)
        val nb = bb.getShort(18).toInt() and 0xffff
        if (n < 21 + nb * 2) return null
        val v = FloatArray(nb) { i -> (bb.getShort(20 + i * 2).toInt() and 0xffff) / 65535f }
        val groupes = p[20 + nb * 2].toInt() and 0xff
        val sortie = HashMap<Int, FloatArray>()
        for (g in 0 until groupes) {
            val o = 21 + nb * 2 + g * 6
            if (n < o + 6) break
            val num = ((p[o].toInt() and 0xff) shl 8) or (p[o + 1].toInt() and 0xff)
            val debut = bb.getShort(o + 2).toInt() and 0xffff
            val compte = bb.getShort(o + 4).toInt() and 0xffff
            if (debut + compte <= nb) sortie[num] = v.copyOfRange(debut, debut + compte)
        }
        return sortie
    }

    /** Annonce de découverte (UDP 47809) : modèle, numéro de série, nom. */
    class Annonce(val modele: String, val serie: String, val nom: String)

    fun lireAnnonce(p: ByteArray, n: Int): Annonce? {
        if (n < 32 || !enteteA(p, 0)) return null
        val morceaux = ArrayList<String>()
        var i = 32
        while (i < n) {
            var j = i
            while (j < n && p[j] != 0.toByte()) j++
            morceaux.add(String(p, i, j - i, Charsets.UTF_8))
            i = j + 1
        }
        val modele = morceaux.getOrNull(0).orEmpty()
        val serie = morceaux.getOrNull(2).orEmpty()
        if (serie.isBlank() && modele.isBlank()) return null
        return Annonce(modele, serie, morceaux.getOrNull(3).orEmpty())
    }
}

/** UBJSON (format binaire de l'état complet), lu de façon tolérante. */
class Ubjson(private val b: ByteArray) {
    private var i = 0

    fun lire(): Any? = valeur(b[i++].toInt().toChar())

    private fun u8() = b[i++].toInt() and 0xff

    private fun bb(n: Int): ByteBuffer {
        val r = ByteBuffer.wrap(b, i, n).order(ByteOrder.BIG_ENDIAN)
        i += n
        return r
    }

    private fun entier(t: Char): Long = when (t) {
        'i' -> b[i++].toLong()
        'U' -> u8().toLong()
        'I' -> bb(2).short.toLong()
        'l' -> bb(4).int.toLong()
        'L' -> bb(8).long
        else -> throw IllegalStateException("longueur de type $t")
    }

    private fun texte(): String {
        val n = entier(b[i++].toInt().toChar()).toInt()
        val s = String(b, i, n, Charsets.UTF_8)
        i += n
        return s
    }

    private fun valeur(t: Char): Any? = when (t) {
        'Z' -> null
        'N' -> lire()
        'T' -> true
        'F' -> false
        'i', 'U', 'I', 'l', 'L' -> entier(t)
        'd' -> bb(4).float
        'D' -> bb(8).double
        'C' -> b[i++].toInt().toChar().toString()
        'S', 'H' -> texte()
        '[' -> tableau()
        '{' -> objet()
        else -> throw IllegalStateException("type inconnu $t à ${i - 1}")
    }

    /** Conteneurs « optimisés » : $ type et # nombre. */
    private fun entete(): Pair<Char?, Int> {
        var type: Char? = null
        var nombre = -1
        if (i < b.size && b[i] == '$'.code.toByte()) {
            type = b[i + 1].toInt().toChar()
            i += 2
        }
        if (i < b.size && b[i] == '#'.code.toByte()) {
            i++
            nombre = entier(b[i++].toInt().toChar()).toInt()
        }
        return type to nombre
    }

    private fun objet(): Map<String, Any?> {
        val m = LinkedHashMap<String, Any?>()
        val (type, nombre) = entete()
        var k = 0
        while (i < b.size) {
            if (nombre >= 0) {
                if (k++ >= nombre) break
            } else if (b[i] == '}'.code.toByte()) {
                i++
                break
            }
            val cle = texte()
            m[cle] = if (type != null) valeur(type) else lire()
        }
        return m
    }

    private fun tableau(): List<Any?> {
        val l = ArrayList<Any?>()
        val (type, nombre) = entete()
        var k = 0
        while (i < b.size) {
            if (nombre >= 0) {
                if (k++ >= nombre) break
            } else if (b[i] == ']'.code.toByte()) {
                i++
                break
            }
            l.add(if (type != null) valeur(type) else lire())
        }
        return l
    }
}

/** Courbe des faders PreSonus (-84 dB à +10 dB) : position (0 à 1) ↔ dB. */
object Courbe {
    private fun position(db: Double): Double =
        (72.5204177782 + 2.473473992 * db + 0.026567557 * db * db + 0.0000880866 * db * db * db) / 100.0

    /** null : -∞ (fader en bas). */
    fun db(p: Float): Float? {
        if (p <= 0.002f) return null
        if (p >= 1f) return 10f
        var bas = -73.3
        var haut = 10.0
        repeat(40) {
            val m = (bas + haut) / 2
            if (position(m) < p) bas = m else haut = m
        }
        val r = ((bas + haut) / 2).toFloat()
        return if (r < -72f) null else r
    }

    fun positionDe(db: Float): Float = position(db.toDouble().coerceIn(-73.3, 10.0)).toFloat().coerceIn(0f, 1f)

    fun texte(p: Float): String {
        val d = db(p) ?: return "-∞"
        return when {
            d > -0.05f && d < 0.05f -> "0.0"
            d > 0 -> "+%.1f".format(d)
            d > -10 -> "%.1f".format(d)
            else -> "%.0f".format(d)
        }
    }

    /** Niveau d'un vu-mètre (amplitude 0 à 1) → hauteur de la barre (-60 dB à 0 dB). */
    fun barre(a: Float): Float {
        if (a <= 0.00001f) return 0f
        val d = 20.0 * Math.log10(a.toDouble())
        return ((d + 60.0) / 60.0).toFloat().coerceIn(0f, 1f)
    }
}
