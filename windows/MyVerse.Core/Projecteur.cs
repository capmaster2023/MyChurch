using System.Text.RegularExpressions;

namespace MyVerse;

/// <summary>Un écran branché au PC (coordonnées en pixels réels). Peripherique : la sortie de la carte graphique (\\.\DISPLAY2).</summary>
public sealed record Moniteur(string Nom, int X, int Y, int W, int H, bool Principal, string Peripherique);

/// <summary>
/// Retrouver le projecteur parmi les écrans branchés : au démarrage de Siloé Stream (la projection se rouvre sur le dernier écran)
/// et quand Windows signale un changement d'écrans (projecteur débranché puis rebranché, résolution changée).
/// </summary>
public static class Projecteur
{
    /// <summary>Nom donné par Siloé Stream quand Windows ne donne pas le vrai nom de l'écran (« Écran principal », « Écran 2 »).</summary>
    public static bool NomGenerique(string? nom) => string.IsNullOrWhiteSpace(nom) || nom == "Écran principal" || Regex.IsMatch(nom, @"^Écran \d+$");

    static bool MemeSortie(Moniteur e, string peripherique) =>
        peripherique.Length > 0 && string.Equals(e.Peripherique, peripherique, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// L'écran où remettre la projection plein écran choisie, ou null (débranché).
    /// Le vrai nom de l'écran (« EPSON PJ ») d'abord ; sans vrai nom, la même sortie de la carte graphique, puis la même place et la même taille.
    /// Un écran qui a un autre vrai nom (l'écran de scène) n'est jamais pris pour le projecteur.
    /// L'écran principal (celui de la régie) n'est candidat que si <paramref name="principal"/> : la projection que l'opérateur
    /// y a lui-même ouverte. Jamais pour une réouverture automatique : la projection couvrirait la régie.
    /// </summary>
    public static Moniteur? Retrouver(EcranChoisi? choix, IEnumerable<Moniteur> ecrans, bool principal = false)
    {
        if (choix is not { Mode: "plein" } || choix.Principal && !principal) return null;
        EcranChoisi c = choix;
        var candidats = ecrans.Where(e => principal || !e.Principal).ToList();
        int Proche(Moniteur e) => (MemeSortie(e, c.Peripherique) ? 2 : 0) + (e.X == c.X && e.Y == c.Y ? 1 : 0);
        if (!NomGenerique(c.Nom))
            // même vrai nom ; ou même sortie quand Windows n'a pas donné le nom cette fois
            return candidats.Where(e => e.Nom == c.Nom || NomGenerique(e.Nom) && MemeSortie(e, c.Peripherique))
                .OrderByDescending(Proche).FirstOrDefault();
        return candidats.FirstOrDefault(e => MemeSortie(e, c.Peripherique))
            ?? candidats.FirstOrDefault(e => e.X == c.X && e.Y == c.Y && e.W == c.W && e.H == c.H);
    }

    /// <summary>
    /// Au démarrage : rouvrir la projection laissée ouverte ? Plein écran : sur le projecteur (jamais sur l'écran principal) ;
    /// fenêtre : sur l'écran principal. Réglage de Siloé Stream 4 (sans « ouvert ») : oui. Arrêtée par l'opérateur : non.
    /// </summary>
    public static bool ARouvrir(EcranChoisi? choix) =>
        choix is { Mode: "plein" or "fenetre" } && choix.Ouvert != false && !(choix.Mode == "plein" && choix.Principal);

    /// <summary>
    /// Le choix enregistré pour une projection ouverte sur <paramref name="m"/>. Garde le vrai nom de l'écran quand Windows
    /// ne l'a pas donné cette fois (même sortie de la carte graphique).
    /// </summary>
    public static EcranChoisi Choix(Moniteur m, bool plein, EcranChoisi? avant = null)
    {
        var nom = m.Nom;
        if (NomGenerique(nom) && avant != null && !NomGenerique(avant.Nom) && MemeSortie(m, avant.Peripherique)) nom = avant.Nom;
        return new EcranChoisi
        {
            Mode = plein ? "plein" : "fenetre", X = m.X, Y = m.Y, W = m.W, H = m.H,
            Nom = nom, Principal = m.Principal, Peripherique = m.Peripherique, Ouvert = true,
        };
    }
}
