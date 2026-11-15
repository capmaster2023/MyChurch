namespace MyVerse;

/// <summary>
/// MyStream (MyChurch 1.6.0) : l'écran envoyé sur Internet. Zoom (par MyVirtualCam) et YouTube (direct de MyChurch) montrent la même image.
/// <para>
/// L'église et MyStream sont deux écrans. MyStream a sa source principale (une caméra, MyCam, une fenêtre, une vidéo, une image),
/// toujours là, comme l'écran de repos de l'église : quand l'église ne montre rien, ou son écran de repos, MyStream montre la source
/// principale. Les paroles et les versets passent par-dessus, en bandeau (ou en plein écran, au choix) ; une image, une vidéo ou
/// une caméra montrées à l'église passent en grand. « Source principale » (touche M) ramène MyStream sur la source principale ;
/// l'église garde ce qu'elle montre, et le prochain contenu montré repart aussi sur MyStream.
/// </para>
/// <para>Sans source principale, MyStream montre ce que voyait Zoom jusqu'à la 1.5 (<see cref="Regie.EtatCamera"/>).</para>
/// </summary>
public sealed partial class Regie
{
    /// <summary>Les sources qui peuvent être la source principale de MyStream (une image vivante ou fixe, pas un texte).</summary>
    public static bool SourceStreamPossible(SourceDisplay? s) => s?.Type is "camera" or "mycam" or "fenetre" or "video" or "image";

    /// <summary>La source principale de MyStream (une source Display), ou null : MyStream montre comme l'église.</summary>
    public Annonce? SourcePrincipaleStream =>
        donnees.Reglages.MyStreamSource is { Length: > 0 } id && donnees.Annonces.FirstOrDefault(a => a.Id == id) is { } a && SourceStreamPossible(a.Source) ? a : null;

    /// <summary>Les sources Display qui peuvent être la source principale, caméras d'abord.</summary>
    public IReadOnlyList<Annonce> SourcesStreamPossibles =>
        donnees.Annonces.Where(a => SourceStreamPossible(a.Source))
            .OrderBy(a => a.Source!.Type switch { "mycam" => 0, "camera" => 1, "fenetre" => 2, "video" => 3, _ => 4 })
            .ThenBy(a => a.Titre, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Les paroles et les versets sur MyStream : « bandeau » (par-dessus la source principale) ou « plein » (comme l'église).</summary>
    public string TexteStream => donnees.Reglages.MyStreamTexte == "plein" ? "plein" : "bandeau";

    /// <summary>Choisit la source principale de MyStream (null ou vide : aucune).</summary>
    public void ChoisirSourceStream(string? id)
    {
        id ??= "";
        if (id.Length > 0 && !SourceStreamPossible(donnees.Annonces.FirstOrDefault(a => a.Id == id)?.Source)) return;
        if (donnees.Reglages.MyStreamSource == id) return;
        donnees.Reglages.MyStreamSource = id;
        donnees.SauverReglages();
        Signaler();
    }

    /// <summary>Les paroles et versets sur MyStream : « bandeau » ou « plein ».</summary>
    public void ChoisirTexteStream(string mode)
    {
        mode = mode == "plein" ? "plein" : "bandeau";
        if (donnees.Reglages.MyStreamTexte == mode) return;
        donnees.Reglages.MyStreamTexte = mode;
        donnees.SauverReglages();
        Signaler();
    }

    /// <summary>Ce qui était à l'écran de l'église quand on a demandé la source principale (touche M).</summary>
    sealed record RetourStream(Cible? Cible, IReadOnlyList<string> Lignes, SourceDisplay? Media, string Mode);
    RetourStream? retourStream;

    /// <summary>MyStream est revenu sur sa source principale (touche M) et l'église n'a rien montré de nouveau depuis.</summary>
    public bool StreamSurPrincipale =>
        retourStream is { } r && SourcePrincipaleStream != null && ReferenceEquals(r.Cible, Cible) && ReferenceEquals(r.Lignes, Etat.Lignes)
        && ReferenceEquals(r.Media, Etat.Media) && r.Mode == Etat.Mode;

    /// <summary>
    /// « Source principale » (touche M) : MyStream revient sur sa source principale, l'église garde ce qu'elle montre.
    /// Le prochain contenu montré (Suivant, un clic, un verset) repart aussi sur MyStream. Faux sans source principale.
    /// </summary>
    public bool StreamPrincipale()
    {
        if (SourcePrincipaleStream == null) { Dire("Choisis d'abord la source principale de MyStream (à droite, sous MyStream)."); return false; }
        retourStream = new RetourStream(Cible, Etat.Lignes, Etat.Media, Etat.Mode);
        Signaler();
        return true;
    }

    /// <summary>
    /// Ce que montre MyStream (Zoom et YouTube) pour l'état <paramref name="e"/> de l'église (écran noir compris).
    /// Le texte posé sur la source principale garde le mode du texte (« chant », « verset ») avec <see cref="EtatEcran.Media"/> = la source :
    /// le dessin montre l'image dessous et le texte en bandeau par-dessus.
    /// </summary>
    public EtatEcran EtatStream(EtatEcran e)
    {
        var p = SourcePrincipaleStream?.Source;
        if (p == null) return EtatCamera(e);
        // un contenu épinglé « Dans l'église » ne part pas sur Internet
        var r = e.Duo?.Etat.Media is { EgliseSeulement: true } ? e with { Duo = null } : e;
        var seule = r with { Mode = "media", Media = p, Lignes = Array.Empty<string>(), Ref = "", Second = null, Duo = null, Presentation = "plein" };
        if (StreamSurPrincipale) return seule;
        // rien pour Internet (écran vide, noir, fond seul, l'écran de repos de l'église, une source « Dans l'église ») :
        // la source principale, l'écran de repos de MyStream
        if (Cible is CibleDefaut || r.Mode is "vide" or "fond" || r is { Mode: "media", Media.EgliseSeulement: true })
            return r.Duo != null ? seule with { Duo = r.Duo } : seule;
        if (r.Mode is "verset" or "chant" && TexteStream == "bandeau") return r with { Media = p, Presentation = "bandeau" };
        return r;
    }

    /// <summary>Le texte posé sur une image (MyStream : paroles en bandeau sur la source principale).</summary>
    public static bool TexteSurImage(EtatEcran e) => e.Media != null && e.Mode is "verset" or "chant";
}
