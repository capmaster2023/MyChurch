using System.ComponentModel;
using System.Windows.Media;

namespace MyVerse.Ui;

/// <summary>
/// Les couleurs de l'interface : thème clair par défaut (MyChurch 1.5.0 : blanc chaud et bordeaux #930000), thème sombre (noyer) au choix (Réglages › Apparence ou barre de gauche).
/// Chaque pinceau de Theme.xaml est lié à une couleur d'ici : changer de thème met à jour d'un coup toute l'interface,
/// fenêtres ouvertes et éléments construits en C# compris (ils utilisent les mêmes pinceaux).
/// Les noms viennent des versions précédentes : « Blanc » est le texte, « Encre » et « Nuit » le fond des cartes, « Or » l'accent.
/// </summary>
public sealed class Palette : INotifyPropertyChanged
{
    /// <summary>Clé → (clair, sombre). Déclaré avant <see cref="Actuelle"/> : les champs statiques s'initialisent dans l'ordre.</summary>
    static readonly Dictionary<string, (string Clair, string Sombre)> Couleurs = new()
    {
        // fonds (1.5.0 : blanc chaud en clair ; noyer très foncé en sombre)
        ["Page"] = ("#FAF7F7", "#15110E"),
        ["Rail"] = ("#F3EEEE", "#100D0A"),
        ["RailActif"] = ("#FBE9E9", "#2C1D1A"),
        ["Encre"] = ("#FFFFFF", "#1E1915"),
        ["Nuit"] = ("#FFFFFF", "#1E1915"),
        ["Nuit2"] = ("#F6F1F1", "#28211B"),
        ["Nuit3"] = ("#E8E0E0", "#3A2F27"),
        ["Trait"] = ("#EFE8E8", "#2C241E"),
        ["Trait2"] = ("#D6CACA", "#4D4137"),
        // textes
        ["Blanc"] = ("#1E1818", "#F4EADB"),
        ["Encre2"] = ("#463D3D", "#D2C4B2"),
        ["Brume"] = ("#6E6262", "#A8998A"),
        ["Brume2"] = ("#857979", "#8E8072"),
        // accent (1.5.0) : bordeaux #930000
        ["Or"] = ("#930000", "#F07A6E"),
        ["Or2"] = ("#6E0000", "#FF9D93"),
        ["OrVif"] = ("#930000", "#D8463B"),
        ["OrLeger"] = ("#FBEDED", "#2E1715"),
        ["OrBord"] = ("#EBC4C4", "#5C2A25"),
        ["SurOr"] = ("#FFFFFF", "#FFFFFF"),
        ["OrDegrade"] = ("#930000", "#930000"),
        ["OrDegradeClair"] = ("#B01E1E", "#B3261E"),
        // bouton principal (Suivant, Importer…) : bordeaux #930000 dans les deux thèmes
        ["Primaire"] = ("#930000", "#930000"),
        ["PrimaireSurvol"] = ("#AD1616", "#B01818"),
        ["SurPrimaire"] = ("#FFFFFF", "#FFFFFF"),
        // ce qui vient avec Suivant (1.5.0) : or, à côté du rouge du direct
        ["Ensuite"] = ("#D9981E", "#F2B33D"),
        ["SurEnsuite"] = ("#241804", "#231804"),
        // en direct, sourdine, erreurs
        ["Rouge"] = ("#E0312B", "#FF4D40"),
        ["Direct"] = ("#E0312B", "#FF4D40"),
        ["RougeLeger"] = ("#FFF1F0", "#2D1614"),
        ["RougeBord"] = ("#F6C4C0", "#6A2620"),
        ["RougeTexte"] = ("#B3261E", "#FF8A7F"),
        ["Danger"] = ("#B3261E", "#FF8A7F"),
        // projection et moteur actifs
        ["Vert"] = ("#15803D", "#4ADE80"),
        ["VertLeger"] = ("#E8F6EE", "#12261A"),
        // types d'éléments du programme (icônes des lignes, groupes de vignettes)
        ["TypeAccueil"] = ("#64748B", "#A3B1C6"),
        ["TypeChant"] = ("#2563EB", "#7AA7FF"),
        ["TypeLecture"] = ("#7C3AED", "#B794F6"),
        ["TypeDisplay"] = ("#DB2777", "#F58FB6"),
        ["TypeDuo"] = ("#0F766E", "#4FD1C5"),
        ["TypePrincipal"] = ("#A16207", "#E3B762"),
        // divers
        ["BleuFonce"] = ("#152C91", "#9DB0FF"),
        ["Marque"] = ("#FBE3E3", "#3A1F1C"),
        ["Surligne"] = ("#FFE9A8", "#4A3A12"),
        ["TuileFond"] = ("#17130E", "#0E0C09"),
        ["TuileSource"] = ("#2A241C", "#26211A"),
        ["TuileTexte"] = ("#D9CFC0", "#A99F92"),
        ["VuFond"] = ("#EEE7E7", "#2B241E"),
        ["VuCrete"] = ("#F9DCDA", "#3F2427"),
        ["DamierA"] = ("#F5F1F1", "#1C1814"),
        ["DamierB"] = ("#E9E2E2", "#26201B"),
    };

    public static Palette Actuelle { get; } = new();

    readonly Dictionary<string, Color> actuelles = new();

    Palette() => Remplir(false);

    public bool Sombre { get; private set; }
    /// <summary>« clair » ou « sombre » (Reglages.Apparence).</summary>
    public string Nom => Sombre ? "sombre" : "clair";
    /// <summary>Toutes les clés de couleur (pour le test automatique).</summary>
    public static IReadOnlyCollection<string> Cles => Couleurs.Keys;
    /// <summary>La couleur d'une clé dans un thème donné (aperçus des Réglages, test automatique).</summary>
    public static Color De(string cle, bool sombre) => Lire(sombre ? Couleurs[cle].Sombre : Couleurs[cle].Clair);

    /// <summary>Le thème a changé (après la mise à jour des pinceaux).</summary>
    public static event Action? Change;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Applique « clair » ou « sombre » (toute autre valeur : clair). Sans effet si c'est déjà le thème.</summary>
    public void Appliquer(string? nom)
    {
        bool sombre = nom == "sombre";
        if (sombre == Sombre) return;
        Remplir(sombre);
        // nom vide : toutes les couleurs ont changé
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        Change?.Invoke();
    }

    void Remplir(bool sombre)
    {
        Sombre = sombre;
        foreach (var (cle, (clair, fonce)) in Couleurs) actuelles[cle] = Lire(sombre ? fonce : clair);
    }

    static Color Lire(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    Color C(string cle) => actuelles[cle];

    public Color Page => C(nameof(Page));
    public Color Rail => C(nameof(Rail));
    public Color RailActif => C(nameof(RailActif));
    public Color Encre => C(nameof(Encre));
    public Color Nuit => C(nameof(Nuit));
    public Color Nuit2 => C(nameof(Nuit2));
    public Color Nuit3 => C(nameof(Nuit3));
    public Color Trait => C(nameof(Trait));
    public Color Trait2 => C(nameof(Trait2));
    public Color Blanc => C(nameof(Blanc));
    public Color Encre2 => C(nameof(Encre2));
    public Color Brume => C(nameof(Brume));
    public Color Brume2 => C(nameof(Brume2));
    public Color Or => C(nameof(Or));
    public Color Or2 => C(nameof(Or2));
    public Color OrVif => C(nameof(OrVif));
    public Color OrLeger => C(nameof(OrLeger));
    public Color OrBord => C(nameof(OrBord));
    public Color Primaire => C(nameof(Primaire));
    public Color PrimaireSurvol => C(nameof(PrimaireSurvol));
    public Color SurPrimaire => C(nameof(SurPrimaire));
    public Color Ensuite => C(nameof(Ensuite));
    public Color SurEnsuite => C(nameof(SurEnsuite));
    public Color SurOr => C(nameof(SurOr));
    public Color OrDegrade => C(nameof(OrDegrade));
    public Color OrDegradeClair => C(nameof(OrDegradeClair));
    public Color Rouge => C(nameof(Rouge));
    public Color Direct => C(nameof(Direct));
    public Color RougeLeger => C(nameof(RougeLeger));
    public Color RougeBord => C(nameof(RougeBord));
    public Color RougeTexte => C(nameof(RougeTexte));
    public Color Danger => C(nameof(Danger));
    public Color Vert => C(nameof(Vert));
    public Color VertLeger => C(nameof(VertLeger));
    public Color TypeAccueil => C(nameof(TypeAccueil));
    public Color TypeChant => C(nameof(TypeChant));
    public Color TypeLecture => C(nameof(TypeLecture));
    public Color TypeDisplay => C(nameof(TypeDisplay));
    public Color TypeDuo => C(nameof(TypeDuo));
    public Color TypePrincipal => C(nameof(TypePrincipal));
    public Color BleuFonce => C(nameof(BleuFonce));
    public Color Marque => C(nameof(Marque));
    public Color Surligne => C(nameof(Surligne));
    public Color TuileFond => C(nameof(TuileFond));
    public Color TuileSource => C(nameof(TuileSource));
    public Color TuileTexte => C(nameof(TuileTexte));
    public Color VuFond => C(nameof(VuFond));
    public Color VuCrete => C(nameof(VuCrete));
    public Color DamierA => C(nameof(DamierA));
    public Color DamierB => C(nameof(DamierB));
}
