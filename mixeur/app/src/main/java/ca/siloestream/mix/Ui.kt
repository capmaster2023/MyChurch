package ca.siloestream.mix

import android.app.Activity
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.MutableState
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay

// ------------------------------------------------------------------------------------------------ vues

/** Ce que montrent les faders : le mix principal, un mix de retour (aux), un envoi d'effet, ou les sorties. */
sealed interface Vue
data object VuePrincipal : Vue
data class VueMix(val m: Int) : Vue
data class VueFx(val k: Int) : Vue
data object VueSorties : Vue

/** Une tranche : la voie, le paramètre du fader, le bouton mute (inversé dans les mix : « envoyé » = audible). */
class Piste(
    val voie: Voie,
    val cleFader: String,
    val cleMute: String?,
    val muteInverse: Boolean = false,
    val metres: List<Pair<Int, Int>> = emptyList(),
)

private fun lettre(k: Int) = 'A' + (k - 1)

fun nomParDefaut(v: Voie): String = when (v.groupe) {
    "line" -> "Ch ${v.n}"
    "return" -> "Retour ${v.n}"
    "fxreturn" -> "Retour FX ${lettre(v.n)}"
    "talkback" -> "Talkback"
    "aux" -> "Mix ${v.n}"
    "fxbus" -> "FX ${lettre(v.n)}"
    "sub" -> "Sub ${v.n}"
    "main" -> "Principal"
    else -> v.base
}

fun numero(v: Voie): String = when (v.groupe) {
    "line" -> "${v.n}"
    "return" -> "R${v.n}"
    "fxreturn" -> "FX${lettre(v.n)}"
    "talkback" -> "TB"
    "aux" -> "M${v.n}"
    "fxbus" -> "FX${lettre(v.n)}"
    "sub" -> "S${v.n}"
    "main" -> "L/R"
    else -> ""
}

/** Vu-mètres : entrées (groupe 0x0000), mix (0x0400), principal (0x0700). */
private fun metreDe(v: Voie): List<Pair<Int, Int>> = when (v.groupe) {
    "line" -> listOf(0x0000 to v.n - 1)
    "aux" -> listOf(0x0400 to v.n - 1)
    "main" -> listOf(0x0700 to 0, 0x0700 to 1)
    else -> emptyList()
}

fun pistes(vue: Vue, s: Structure, c: Console): List<Piste> = when (vue) {
    VuePrincipal -> s.entrees.map { v -> Piste(v, "${v.base}/volume", "${v.base}/mute", false, metreDe(v)) }
    is VueMix -> s.entrees.filter { c.existe("${it.base}/aux${vue.m}") }.map { v ->
        Piste(v, "${v.base}/aux${vue.m}", "${v.base}/assign_aux${vue.m}".takeIf { c.existe(it) }, true, metreDe(v))
    }
    is VueFx -> s.entrees.filter { c.existe("${it.base}/FX${lettre(vue.k)}") }.map { v ->
        Piste(v, "${v.base}/FX${lettre(vue.k)}", "${v.base}/assign_fx${vue.k}".takeIf { c.existe(it) }, true, metreDe(v))
    }
    VueSorties -> ((1..s.aux).map { Voie("aux", it) } + (1..s.fx).map { Voie("fxbus", it) } + (1..s.subs).map { Voie("sub", it) })
        .map { v -> Piste(v, "${v.base}/volume", "${v.base}/mute", false, metreDe(v)) }
}

fun maitre(vue: Vue): Piste = when (vue) {
    is VueMix -> Voie("aux", vue.m).let { Piste(it, "${it.base}/volume", "${it.base}/mute", false, metreDe(it)) }
    is VueFx -> Voie("fxbus", vue.k).let { Piste(it, "${it.base}/volume", "${it.base}/mute", false, metreDe(it)) }
    else -> Voie("main", 1).let { Piste(it, "${it.base}/volume", "${it.base}/mute", false, metreDe(it)) }
}

/** Couleur d'une voie (« rrvvbbaa ») ; null : pas de couleur. */
fun couleurDe(hex: String): Color? {
    if (hex.length < 8) return null
    val r = hex.substring(0, 2).toIntOrNull(16) ?: return null
    val g = hex.substring(2, 4).toIntOrNull(16) ?: return null
    val b = hex.substring(4, 6).toIntOrNull(16) ?: return null
    val a = hex.substring(6, 8).toIntOrNull(16) ?: return null
    if (a == 0 || r + g + b == 0) return null
    return Color(r, g, b)
}

private fun Console.nomDe(v: Voie): String = t("${v.base}/username").ifBlank { t("${v.base}/name") }.ifBlank { nomParDefaut(v) }

// ------------------------------------------------------------------------------------------------ racine

@Composable
fun MixApp(console: Console, decouverte: Decouverte) {
    val etat by console.etat.collectAsState()
    Box(Modifier.fillMaxSize().background(K.Fond).windowInsetsPadding(WindowInsets.safeDrawing)) {
        if (etat == EtatLien.ARRET) EcranConnexion(console, decouverte) else EcranConsole(console)
    }
}

// ------------------------------------------------------------------------------------------------ connexion

@Composable
fun EcranConnexion(console: Console, decouverte: Decouverte) {
    DisposableEffect(Unit) {
        decouverte.demarrer()
        onDispose { decouverte.arreter() }
    }
    val trouvees by decouverte.trouvees.collectAsState()
    val erreur by decouverte.erreur.collectAsState()
    var ip by rememberSaveable { mutableStateOf(console.derniereHote) }
    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 22.dp, vertical = 26.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
            Logo(Modifier.size(52.dp))
            Column {
                T("Siloé Mix", taille = 30.sp, poids = FontWeight.ExtraBold)
                T("La console PreSonus StudioLive, dans votre main", taille = 13.5.sp, couleur = K.Texte2)
            }
        }
        Spacer(Modifier.height(4.dp))
        T("Sur ce Wi-Fi", taille = 13.sp, poids = FontWeight.Bold, couleur = K.Texte2)
        if (trouvees.isEmpty()) {
            Carte {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    Pouls()
                    Column(Modifier.weight(1f)) {
                        T("Recherche des consoles…", poids = FontWeight.Bold)
                        T(
                            erreur.ifBlank { "Le téléphone et la console doivent être sur le même réseau. La console s'annonce toute seule." },
                            taille = 12.5.sp, couleur = K.Texte2, interligne = 17.sp,
                        )
                    }
                }
            }
        }
        trouvees.forEach { c ->
            Carte(onClick = { console.connecter(c.ip, c.nom, c.port, c.brut) }) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        T(c.nom.ifBlank { "Console StudioLive" }, taille = 16.sp, poids = FontWeight.ExtraBold, lignes = 1)
                        T(listOf(c.modele.takeIf { it != c.nom }, c.ip).filterNotNull().filter { it.isNotBlank() }.joinToString(" · "), taille = 12.5.sp, couleur = K.Texte2, lignes = 1)
                    }
                    Bouton("Connecter", plein = true) { console.connecter(c.ip, c.nom, c.port, c.brut) }
                }
            }
        }
        val derniere = console.derniereHote
        if (derniere.isNotBlank() && trouvees.none { it.ip == derniere }) {
            T("Dernière console", taille = 13.sp, poids = FontWeight.Bold, couleur = K.Texte2)
            Carte(onClick = { console.connecter(derniere, console.dernierNom, console.dernierPort) }) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        T(console.dernierNom.ifBlank { derniere }, taille = 16.sp, poids = FontWeight.ExtraBold, lignes = 1)
                        T(derniere, taille = 12.5.sp, couleur = K.Texte2)
                    }
                    Bouton("Reconnecter") { console.connecter(derniere, console.dernierNom, console.dernierPort) }
                }
            }
        }
        T("Par adresse IP", taille = 13.sp, poids = FontWeight.Bold, couleur = K.Texte2)
        Carte {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                BasicTextField(
                    value = ip,
                    onValueChange = { ip = it.filter { ch -> ch.isDigit() || ch == '.' || ch.isLetter() || ch == '-' || ch == ':' }.take(64) },
                    singleLine = true,
                    textStyle = TextStyle(fontFamily = Manrope, fontSize = 17.sp, fontWeight = FontWeight.Bold, color = K.Texte),
                    cursorBrush = SolidColor(K.Or),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Go),
                    keyboardActions = KeyboardActions(onGo = { console.connecter(ip, "") }),
                    modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(K.Fond)
                        .border(1.dp, K.Trait2, RoundedCornerShape(12.dp)).padding(horizontal = 14.dp, vertical = 13.dp),
                    decorationBox = { champ ->
                        Box {
                            if (ip.isEmpty()) T("ex. 192.168.1.50", taille = 17.sp, couleur = K.Texte3)
                            champ()
                        }
                    },
                )
                Bouton("Connecter", plein = true, modifier = Modifier.fillMaxWidth(), actif = ip.isNotBlank()) { console.connecter(ip, "") }
                T("L'adresse de la console est dans ses réglages réseau (ou dans UC Surface).", taille = 12.sp, couleur = K.Texte3, interligne = 16.sp)
            }
        }
    }
}

// ------------------------------------------------------------------------------------------------ console

@Composable
fun EcranConsole(console: Console) {
    val etat by console.etat.collectAsState()
    val detail by console.detail.collectAsState()
    val nom by console.nom.collectAsState()
    val hote by console.hote.collectAsState()
    val s by console.structure.collectAsState()
    val context = LocalContext.current
    var vue by remember { mutableStateOf<Vue>(VuePrincipal) }
    var verrou by rememberSaveable { mutableStateOf(false) }
    var avis by remember { mutableStateOf("") }
    var menu by remember { mutableStateOf(false) }
    var journal by remember { mutableStateOf(false) }
    LaunchedEffect(avis) {
        if (avis.isNotEmpty()) {
            delay(2200)
            avis = ""
        }
    }
    // la vue choisie n'existe plus (autre console) : retour au principal
    LaunchedEffect(s) {
        val v = vue
        if ((v is VueMix && v.m > s.aux) || (v is VueFx && v.k > s.fx)) vue = VuePrincipal
    }
    // « retour » ne coupe pas la liaison en plein culte : l'app passe en arrière-plan
    BackHandler { (context as? Activity)?.moveTaskToBack(true) }
    val bloque: () -> Unit = { avis = "Réglages verrouillés : touchez le cadenas pour déverrouiller." }

    Column(Modifier.fillMaxSize()) {
        // en-tête
        Row(Modifier.fillMaxWidth().padding(start = 14.dp, end = 6.dp, top = 6.dp, bottom = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(
                Modifier.size(10.dp).clip(CircleShape).background(
                    when (etat) {
                        EtatLien.CONNECTE -> K.Vert
                        EtatLien.PERDU -> K.Rouge
                        else -> K.Ambre
                    },
                ),
            )
            Spacer(Modifier.width(10.dp))
            Column(Modifier.weight(1f)) {
                T(nom.ifBlank { "Console" }, taille = 16.sp, poids = FontWeight.ExtraBold, lignes = 1)
                T(
                    avis.ifBlank {
                        when (etat) {
                            EtatLien.CONNECTE -> "Connectée · $hote"
                            EtatLien.SYNCHRO -> detail.ifBlank { "Synchronisation…" }
                            EtatLien.CONNEXION -> "Connexion à $hote…"
                            EtatLien.PERDU -> detail.ifBlank { "Reconnexion…" }
                            EtatLien.ARRET -> ""
                        }
                    },
                    taille = 12.sp, couleur = if (avis.isNotEmpty()) K.Or else K.Texte2, lignes = 1,
                )
            }
            Cadenas(verrou) {
                verrou = !verrou
                avis = if (verrou) "Verrouillé : les faders ne bougent plus au toucher." else "Déverrouillé."
            }
            Box {
                Box(
                    Modifier.size(44.dp).clip(CircleShape).clickable { menu = true }.semantics { contentDescription = "Menu" },
                    contentAlignment = Alignment.Center,
                ) { T("•••", taille = 15.sp, poids = FontWeight.ExtraBold, couleur = K.Texte2) }
                DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                    DropdownMenuItem(text = { T("Journal de connexion") }, onClick = {
                        menu = false
                        journal = true
                    })
                    DropdownMenuItem(text = { T("Changer de console") }, onClick = {
                        menu = false
                        console.deconnecter()
                    })
                }
            }
        }

        OngletsMix(console, s, vue) { vue = it }
        if (vue == VuePrincipal && s.groupesMute > 0) GroupesMute(console, s.groupesMute, verrou, bloque)

        Box(Modifier.weight(1f).fillMaxWidth().padding(top = 6.dp)) {
            if (s.entrees.isEmpty() && !s.principal) {
                Column(Modifier.fillMaxSize().padding(24.dp), verticalArrangement = Arrangement.Center, horizontalAlignment = Alignment.CenterHorizontally) {
                    Pouls()
                    Spacer(Modifier.height(14.dp))
                    T(
                        when (etat) {
                            EtatLien.PERDU -> detail.ifBlank { "Reconnexion à la console…" }
                            else -> "Lecture des voies de la console…"
                        },
                        couleur = K.Texte2, centre = true,
                    )
                    Spacer(Modifier.height(18.dp))
                    Bouton("Changer de console") { console.deconnecter() }
                }
            } else {
                val liste = remember(s, vue) { pistes(vue, s, console) }
                Row(Modifier.fillMaxSize()) {
                    if (liste.isEmpty()) {
                        Box(Modifier.weight(1f).fillMaxHeight().padding(24.dp), contentAlignment = Alignment.Center) {
                            T("Aucune voie n'est envoyée dans ce mix.", couleur = K.Texte2, centre = true)
                        }
                    } else {
                        LazyRow(
                            Modifier.weight(1f).fillMaxHeight(),
                            contentPadding = PaddingValues(start = 8.dp, end = 8.dp, bottom = 8.dp),
                            horizontalArrangement = Arrangement.spacedBy(5.dp),
                        ) {
                            items(liste, key = { it.cleFader }) { p -> Tranche(console, p, verrou, bloque) }
                        }
                    }
                    Box(Modifier.width(1.dp).fillMaxHeight().padding(vertical = 10.dp).background(K.Trait))
                    Box(Modifier.padding(start = 6.dp, end = 8.dp, bottom = 8.dp)) {
                        Tranche(console, maitre(vue), verrou, bloque, largeur = 84.dp, principale = true)
                    }
                }
                if (etat == EtatLien.PERDU) {
                    Box(
                        Modifier.align(Alignment.TopCenter).padding(top = 8.dp).clip(RoundedCornerShape(20.dp)).background(K.RougeSombre)
                            .border(1.dp, K.Rouge, RoundedCornerShape(20.dp)).padding(horizontal = 16.dp, vertical = 8.dp),
                    ) { T(detail.ifBlank { "Reconnexion à la console…" }, taille = 13.sp, poids = FontWeight.Bold) }
                }
            }
        }
    }

    if (journal) {
        val lignes by console.journal.collectAsState()
        val presse = LocalClipboardManager.current
        AlertDialog(
            onDismissRequest = { journal = false },
            containerColor = K.Panneau,
            title = { T("Journal de connexion", taille = 17.sp, poids = FontWeight.ExtraBold) },
            text = {
                LazyColumn(Modifier.heightIn(max = 420.dp)) {
                    items(lignes.reversed()) { l -> T(l, taille = 11.5.sp, poids = FontWeight.Medium, couleur = K.Texte2, interligne = 16.sp) }
                }
            },
            confirmButton = { TextButton(onClick = { journal = false }) { Text("Fermer", color = K.Or) } },
            dismissButton = {
                TextButton(onClick = {
                    presse.setText(AnnotatedString(lignes.joinToString("\n")))
                    avis = "Journal copié."
                }) { Text("Copier", color = K.Texte2) }
            },
        )
    }
}

@Composable
private fun OngletsMix(console: Console, s: Structure, vue: Vue, choisir: (Vue) -> Unit) {
    val onglets = buildList {
        add(VuePrincipal)
        for (m in 1..s.aux) add(VueMix(m))
        for (k in 1..s.fx) add(VueFx(k))
        if (s.aux + s.fx + s.subs > 0) add(VueSorties)
    }
    LazyRow(contentPadding = PaddingValues(horizontal = 10.dp), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        items(onglets, key = { it.toString() }) { o ->
            val titre = when (o) {
                VuePrincipal -> "Principal"
                is VueMix -> console.nomDe(Voie("aux", o.m))
                is VueFx -> console.nomDe(Voie("fxbus", o.k))
                VueSorties -> "Sorties"
            }
            val choisi = o == vue
            Box(
                Modifier.heightIn(min = 40.dp).clip(RoundedCornerShape(20.dp))
                    .background(if (choisi) K.Or else K.Panneau2)
                    .clickable { choisir(o) }
                    .padding(horizontal = 15.dp, vertical = 10.dp),
                contentAlignment = Alignment.Center,
            ) { T(titre, taille = 13.5.sp, poids = FontWeight.ExtraBold, couleur = if (choisi) K.Fond else K.Texte, lignes = 1) }
        }
    }
}

@Composable
private fun GroupesMute(console: Console, n: Int, verrou: Boolean, bloque: () -> Unit) {
    LazyRow(
        Modifier.padding(top = 8.dp),
        contentPadding = PaddingValues(horizontal = 10.dp),
        horizontalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        items((1..n).toList()) { g ->
            val cle = "mutegroup/mutegroup$g"
            val actif = (console.f(cle) ?: 0f) >= 0.5f
            val nom = console.t("mutegroup/mutegroup${g}username").ifBlank { "Groupe $g" }
            Box(
                Modifier.heightIn(min = 36.dp).clip(RoundedCornerShape(10.dp))
                    .background(if (actif) K.RougeSombre else K.Panneau)
                    .border(1.dp, if (actif) K.Rouge else K.Trait, RoundedCornerShape(10.dp))
                    .clickable { if (verrou) bloque() else console.basculer(cle) }
                    .padding(horizontal = 12.dp, vertical = 8.dp),
                contentAlignment = Alignment.Center,
            ) { T(nom, taille = 12.5.sp, poids = FontWeight.Bold, couleur = if (actif) K.Texte else K.Texte2, lignes = 1) }
        }
    }
}

// ------------------------------------------------------------------------------------------------ tranche

@Composable
fun Tranche(console: Console, p: Piste, verrou: Boolean, bloque: () -> Unit, largeur: Dp = 66.dp, principale: Boolean = false) {
    val v = p.voie
    val nom = console.nomDe(v)
    val couleur = couleurDe(console.t("${v.base}/color"))
    val valeur = console.f(p.cleFader) ?: 0f
    val brut = p.cleMute?.let { console.f(it) }
    val coupe = when {
        brut == null -> false
        p.muteInverse -> brut < 0.5f
        else -> brut >= 0.5f
    }
    val niveaux = p.metres.map { (g, i) -> console.niveau(g) to i }
    Column(
        Modifier.width(largeur).fillMaxHeight().clip(RoundedCornerShape(14.dp))
            .background(if (principale) K.Panneau2 else K.Panneau)
            .padding(top = 8.dp, bottom = 6.dp, start = 4.dp, end = 4.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Box(Modifier.fillMaxWidth(0.55f).height(3.dp).clip(RoundedCornerShape(2.dp)).background(couleur ?: K.Trait2))
        Spacer(Modifier.height(6.dp))
        Box(Modifier.height(32.dp).fillMaxWidth(), contentAlignment = Alignment.Center) {
            T(nom, taille = 11.5.sp, poids = FontWeight.ExtraBold, lignes = 2, centre = true, interligne = 14.sp)
        }
        T(Courbe.texte(valeur), taille = 12.sp, poids = FontWeight.Bold, couleur = if (coupe) K.Texte3 else K.Or, chiffres = true)
        Fader(
            valeur = valeur,
            coupe = coupe,
            actif = !verrou,
            niveaux = niveaux,
            modifier = Modifier.weight(1f).fillMaxWidth().padding(vertical = 4.dp)
                .semantics { contentDescription = "$nom : ${Courbe.texte(valeur)} dB" },
            bloque = bloque,
        ) { console.regler(p.cleFader, it) }
        val cleMute = p.cleMute
        if (cleMute != null) {
            Box(
                Modifier.fillMaxWidth().height(36.dp).clip(RoundedCornerShape(10.dp))
                    .background(if (coupe) K.Rouge else K.Panneau2)
                    .border(1.dp, if (coupe) K.Rouge else K.Trait2, RoundedCornerShape(10.dp))
                    .clickable {
                        if (verrou) {
                            bloque()
                        } else {
                            // mute : 1 = coupé ; dans un mix, « assign » : 1 = envoyé
                            val nouveau = if (p.muteInverse) (if (coupe) 1f else 0f) else (if (coupe) 0f else 1f)
                            console.regler(cleMute, nouveau)
                        }
                    }
                    .semantics { contentDescription = if (coupe) "$nom coupé" else "Couper $nom" },
                contentAlignment = Alignment.Center,
            ) { T("Mute", taille = 12.5.sp, poids = FontWeight.ExtraBold, couleur = if (coupe) Color.White else K.Texte2) }
        } else {
            Spacer(Modifier.height(36.dp))
        }
        Spacer(Modifier.height(4.dp))
        T(numero(v), taille = 11.sp, poids = FontWeight.Bold, couleur = K.Texte3, lignes = 1)
    }
}

private val MARQUES = listOf(10f, 5f, 0f, -5f, -10f, -20f, -30f, -40f, -60f)

/**
 * Fader vertical : on le fait glisser (le doigt n'a pas besoin d'être sur le curseur, rien ne saute quand on touche).
 * Les vu-mètres sont lus pendant le dessin : seule la barre se redessine.
 */
@Composable
fun Fader(
    valeur: Float,
    coupe: Boolean,
    actif: Boolean,
    niveaux: List<Pair<MutableState<FloatArray?>, Int>>,
    modifier: Modifier = Modifier,
    bloque: () -> Unit,
    changer: (Float) -> Unit,
) {
    var local by remember { mutableStateOf<Float?>(null) }
    val actuel by rememberUpdatedState(local ?: valeur)
    val auChangement by rememberUpdatedState(changer)
    val auBlocage by rememberUpdatedState(bloque)
    val v = local ?: valeur
    Canvas(
        modifier.pointerInput(actif) {
            if (actif) {
                detectVerticalDragGestures(
                    onDragStart = { local = actuel },
                    onDragEnd = { local = null },
                    onDragCancel = { local = null },
                ) { change, dy ->
                    change.consume()
                    val utile = (size.height - 22.dp.toPx()).coerceAtLeast(1f)
                    val n = ((local ?: actuel) - dy / utile).coerceIn(0f, 1f)
                    local = n
                    auChangement(n)
                }
            } else {
                detectVerticalDragGestures(onDragStart = { auBlocage() }) { change, _ -> change.consume() }
            }
        },
    ) {
        val capH = 22.dp.toPx()
        val capW = minOf(size.width * 0.6f, 40.dp.toPx())
        val haut = capH / 2
        val bas = size.height - capH / 2
        val h = (bas - haut).coerceAtLeast(1f)
        val cx = size.width / 2 - (if (niveaux.size > 1) 4.dp.toPx() else 0f)
        val piste = 4.dp.toPx()
        // graduations (0 dB en or)
        for (db in MARQUES) {
            val y = bas - Courbe.positionDe(db) * h
            val zero = db == 0f
            drawLine(
                color = if (zero) K.Or else K.Trait2,
                start = Offset(cx - capW / 2 - (if (zero) 3.dp.toPx() else 0f), y),
                end = Offset(cx - piste, y),
                strokeWidth = if (zero) 1.5.dp.toPx() else 1.dp.toPx(),
            )
        }
        // rail et remplissage
        drawRoundRect(K.Trait, topLeft = Offset(cx - piste / 2, haut), size = Size(piste, h), cornerRadius = CornerRadius(piste / 2))
        val y = bas - v * h
        drawRoundRect(
            if (coupe) K.Texte3 else K.Or,
            topLeft = Offset(cx - piste / 2, y),
            size = Size(piste, (bas - y).coerceAtLeast(0f)),
            cornerRadius = CornerRadius(piste / 2),
        )
        // vu-mètres
        val mw = 4.dp.toPx()
        niveaux.forEachIndexed { j, (etat, i) ->
            val x = cx + capW / 2 + 3.dp.toPx() + j * (mw + 2.dp.toPx())
            if (x + mw > size.width) return@forEachIndexed
            drawRoundRect(K.Panneau2, topLeft = Offset(x, haut), size = Size(mw, h), cornerRadius = CornerRadius(mw / 2))
            val b = Courbe.barre(etat.value?.getOrNull(i) ?: 0f)
            if (b > 0f) {
                val top = bas - b * h
                drawRoundRect(
                    when {
                        b > 0.95f -> K.Rouge
                        b > 0.8f -> K.Ambre
                        else -> K.Vert
                    },
                    topLeft = Offset(x, top),
                    size = Size(mw, bas - top),
                    cornerRadius = CornerRadius(mw / 2),
                )
            }
        }
        // curseur
        drawRoundRect(
            if (coupe) K.Texte2 else K.Creme,
            topLeft = Offset(cx - capW / 2, y - capH / 2),
            size = Size(capW, capH),
            cornerRadius = CornerRadius(6.dp.toPx()),
        )
        drawLine(K.Fond, Offset(cx - capW / 2 + 5.dp.toPx(), y), Offset(cx + capW / 2 - 5.dp.toPx(), y), strokeWidth = 2.dp.toPx())
    }
}

// ------------------------------------------------------------------------------------------------ petits éléments

@Composable
fun Carte(onClick: (() -> Unit)? = null, contenu: @Composable () -> Unit) {
    Box(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(18.dp)).background(K.Panneau)
            .border(1.dp, K.Trait, RoundedCornerShape(18.dp))
            .then(if (onClick != null) Modifier.clickable { onClick() } else Modifier)
            .padding(16.dp),
    ) { contenu() }
}

@Composable
fun Bouton(texte: String, modifier: Modifier = Modifier, plein: Boolean = false, actif: Boolean = true, onClick: () -> Unit) {
    Box(
        modifier.heightIn(min = 44.dp).clip(RoundedCornerShape(12.dp))
            .background(if (plein && actif) K.Or else K.Panneau2)
            .clickable(enabled = actif) { onClick() }
            .padding(horizontal = 16.dp, vertical = 11.dp),
        contentAlignment = Alignment.Center,
    ) { T(texte, poids = FontWeight.ExtraBold, couleur = if (plein && actif) K.Fond else if (actif) K.Texte else K.Texte3, lignes = 1) }
}

/** Cadenas dessiné : fermé = réglages verrouillés. */
@Composable
fun Cadenas(ferme: Boolean, basculer: () -> Unit) {
    Box(
        Modifier.size(44.dp).clip(CircleShape).background(if (ferme) K.OrSombre else Color.Transparent)
            .clickable { basculer() }
            .semantics { contentDescription = if (ferme) "Déverrouiller les réglages" else "Verrouiller les réglages" },
        contentAlignment = Alignment.Center,
    ) {
        Canvas(Modifier.size(20.dp)) {
            val c = if (ferme) K.Or else K.Texte2
            val trait = 2.dp.toPx()
            val corpsH = size.height * 0.5f
            val anse = size.width * 0.56f
            val gauche = (size.width - anse) / 2
            drawArc(
                color = c,
                startAngle = 180f,
                sweepAngle = if (ferme) 180f else 150f,
                useCenter = false,
                topLeft = Offset(gauche, trait),
                size = Size(anse, anse),
                style = Stroke(width = trait),
            )
            if (ferme) drawLine(c, Offset(gauche + anse, trait + anse / 2), Offset(gauche + anse, size.height - corpsH), strokeWidth = trait)
            drawLine(c, Offset(gauche, trait + anse / 2), Offset(gauche, size.height - corpsH), strokeWidth = trait)
            drawRoundRect(c, topLeft = Offset(size.width * 0.12f, size.height - corpsH), size = Size(size.width * 0.76f, corpsH), cornerRadius = CornerRadius(3.dp.toPx()))
        }
    }
}

/** Trois faders : le logo de Siloé Mix. */
@Composable
fun Logo(modifier: Modifier = Modifier) {
    Canvas(modifier.clip(RoundedCornerShape(14.dp)).background(K.Panneau2)) {
        val w = size.width
        val h = size.height
        val xs = listOf(0.3f, 0.5f, 0.7f)
        val ys = listOf(0.42f, 0.32f, 0.62f)
        xs.forEachIndexed { i, x ->
            drawLine(K.Or, Offset(w * x, h * 0.2f), Offset(w * x, h * 0.8f), strokeWidth = w * 0.045f)
            drawRoundRect(K.Creme, topLeft = Offset(w * x - w * 0.08f, h * ys[i] - h * 0.045f), size = Size(w * 0.16f, h * 0.09f), cornerRadius = CornerRadius(w * 0.02f))
        }
    }
}

/** Point qui respire : recherche en cours. */
@Composable
fun Pouls() {
    var fort by remember { mutableStateOf(false) }
    LaunchedEffect(Unit) {
        while (true) {
            delay(700)
            fort = !fort
        }
    }
    Box(Modifier.size(28.dp), contentAlignment = Alignment.Center) {
        Box(Modifier.size(if (fort) 22.dp else 14.dp).clip(CircleShape).background(K.Or.copy(alpha = if (fort) 0.25f else 0.45f)))
        Box(Modifier.size(9.dp).clip(CircleShape).background(K.Or))
    }
}
