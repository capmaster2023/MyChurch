package ca.siloestream.mix

import android.content.Context
import android.graphics.Color as CouleurAndroid
import android.os.Bundle
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue

/** La console et la recherche vivent tant que l'app vit (une rotation ou un passage en arrière-plan ne coupe rien). */
object Mix {
    private var console: Console? = null
    private var decouverte: Decouverte? = null

    fun console(ctx: Context): Console = console ?: Console(ctx.applicationContext).also { console = it }
    fun decouverte(ctx: Context): Decouverte = decouverte ?: Decouverte(ctx.applicationContext).also { decouverte = it }
}

/**
 * Siloé Mix : télécommande des consoles PreSonus StudioLive (Series III et AI) sur le Wi-Fi de l'église.
 * Faders, mutes, mix de retour, envois d'effets, groupes de mute, vu-mètres.
 */
class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.dark(CouleurAndroid.TRANSPARENT),
            navigationBarStyle = SystemBarStyle.dark(CouleurAndroid.TRANSPARENT),
        )
        super.onCreate(savedInstanceState)
        val console = Mix.console(this)
        val decouverte = Mix.decouverte(this)
        // au lancement : on retourne sur la dernière console
        if (savedInstanceState == null && console.etat.value == EtatLien.ARRET && console.derniereHote.isNotBlank()) {
            console.connecter(console.derniereHote, console.dernierNom, console.dernierPort)
        }
        setContent {
            val etat by console.etat.collectAsState()
            LaunchedEffect(etat) {
                // l'écran reste allumé pendant qu'on mixe
                if (etat != EtatLien.ARRET) window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                else window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
            }
            MaterialTheme(
                colorScheme = darkColorScheme(
                    primary = K.Or, onPrimary = K.Fond, secondary = K.Or, onSecondary = K.Fond,
                    background = K.Fond, onBackground = K.Texte, surface = K.Panneau, onSurface = K.Texte,
                    surfaceVariant = K.Panneau2, onSurfaceVariant = K.Texte2, outline = K.Trait2, error = K.Rouge,
                ),
            ) {
                MixApp(console, decouverte)
            }
        }
    }
}
