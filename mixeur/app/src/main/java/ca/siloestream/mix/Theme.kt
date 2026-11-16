package ca.siloestream.mix

import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.ExperimentalTextApi
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontVariation
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.sp
import androidx.compose.ui.text.font.Font

/** Régie dans la pénombre : bleu nuit, curseurs crème, l'or de Siloé pour ce qui est actif, le rouge pour « coupé ». */
object K {
    val Fond = Color(0xFF11151E)
    val Panneau = Color(0xFF181E2A)
    val Panneau2 = Color(0xFF212938)
    val Trait = Color(0xFF2C3547)
    val Trait2 = Color(0xFF3A4559)
    val Texte = Color(0xFFEDF0F5)
    val Texte2 = Color(0xFF98A3B8)
    val Texte3 = Color(0xFF667189)
    val Or = Color(0xFFE9B54A)
    val OrSombre = Color(0xFF3A2E14)
    val Creme = Color(0xFFF4EBD6)
    val Rouge = Color(0xFFE5484D)
    val RougeSombre = Color(0xFF4A1E23)
    val Vert = Color(0xFF3CCB7F)
    val Ambre = Color(0xFFF5A524)
}

@OptIn(ExperimentalTextApi::class)
private fun manrope(w: Int) = Font(R.font.manrope, FontWeight(w), variationSettings = FontVariation.Settings(FontVariation.weight(w)))

val Manrope = FontFamily(manrope(400), manrope(500), manrope(600), manrope(700), manrope(800))

@Composable
fun T(
    texte: String,
    modifier: Modifier = Modifier,
    taille: TextUnit = 14.sp,
    poids: FontWeight = FontWeight.SemiBold,
    couleur: Color = K.Texte,
    lignes: Int = Int.MAX_VALUE,
    centre: Boolean = false,
    chiffres: Boolean = false,
    interligne: TextUnit = TextUnit.Unspecified,
) {
    Text(
        texte,
        modifier = modifier,
        maxLines = lignes,
        overflow = TextOverflow.Ellipsis,
        textAlign = if (centre) TextAlign.Center else null,
        style = TextStyle(
            fontFamily = Manrope,
            fontSize = taille,
            fontWeight = poids,
            color = couleur,
            lineHeight = interligne,
            fontFeatureSettings = if (chiffres) "tnum" else null,
        ),
    )
}
