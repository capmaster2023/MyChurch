namespace MyVerse;

/// <summary>
/// MyStream (MyChurch 1.6.0) : l'écran envoyé sur Internet. Zoom (par MyVirtualCam) et YouTube (direct de MyChurch) montrent la même image.
/// <para>
/// 1.6.4 : l'église et MyStream sont deux écrans indépendants. MyStream montre sa source principale (une caméra, MyCam, une fenêtre,
/// une vidéo, une image), quoi que montre l'église : les paroles, les versets, les images de l'église n'y vont pas.
/// « Envoyer dans le stream » (touche S, sous l'écran de l'église) : MyStream montre aussi ce que montre l'église, et le suit
/// (le verset suivant, la strophe suivante…) jusqu'à « Source principale » (touche M), qui le ramène sur sa source principale.
/// Une source « MyStream seulement » montrée part toujours sur MyStream (elle est faite pour Internet).
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

    /// <summary>
    /// Choisit la source principale de MyStream (null ou vide : aucune). MyStream la montre aussitôt
    /// (s'il suivait l'église, il revient sur la nouvelle source).
    /// </summary>
    public void ChoisirSourceStream(string? id)
    {
        id ??= "";
        if (id.Length > 0 && !SourceStreamPossible(donnees.Annonces.FirstOrDefault(a => a.Id == id)?.Source)) return;
        bool suivait = StreamSuitEglise;
        StreamSuitEglise = false;
        if (donnees.Reglages.MyStreamSource == id) { if (suivait) Signaler(); return; }
        donnees.Reglages.MyStreamSource = id;
        donnees.SauverReglages();
        Signaler();
    }

    /// <summary>
    /// « Envoyer dans le stream » (touche S) : MyStream montre aussi ce que montre l'église, et la suit jusqu'à « Source principale ».
    /// Sans source principale, MyStream suit déjà l'église : faux (rien ne change).
    /// </summary>
    public bool StreamSuitEglise { get; private set; }

    /// <summary>MyStream montre sa source principale (une source principale est choisie et MyStream ne suit pas l'église).</summary>
    public bool StreamSurPrincipale => SourcePrincipaleStream != null && !StreamSuitEglise;

    /// <summary>« Envoyer dans le stream » : MyStream montre ce que montre l'église et la suit. Faux sans source principale (il la suit déjà).</summary>
    public bool EnvoyerDansStream()
    {
        if (SourcePrincipaleStream == null) return false;
        if (StreamSuitEglise) return true;
        StreamSuitEglise = true;
        Signaler();
        return true;
    }

    /// <summary>
    /// « Source principale » (touche M) : MyStream revient sur sa source principale et ne suit plus l'église ; l'église garde ce
    /// qu'elle montre. Faux sans source principale.
    /// </summary>
    public bool StreamPrincipale()
    {
        if (SourcePrincipaleStream == null) { Dire("Choisis d'abord la source principale de MyStream (à droite, sous MyStream)."); return false; }
        if (!StreamSuitEglise) return true;
        StreamSuitEglise = false;
        Signaler();
        return true;
    }

    /// <summary>
    /// Ce que montre MyStream (Zoom et YouTube) pour l'état <paramref name="e"/> de l'église (écran noir compris).
    /// Sa source principale, quoi que montre l'église ; après « Envoyer dans le stream », ce que montre l'église (comme Zoom avant),
    /// et la source principale quand l'église n'a rien pour Internet (vide, noir, fond seul, écran de repos, source « Dans l'église »).
    /// </summary>
    public EtatEcran EtatStream(EtatEcran e)
    {
        var p = SourcePrincipaleStream?.Source;
        if (p == null) return EtatCamera(e);
        // un contenu épinglé « Dans l'église » ne part pas sur Internet
        var r = e.Duo?.Etat.Media is { EgliseSeulement: true } ? e with { Duo = null } : e;
        var seule = r with { Mode = "media", Media = p, Lignes = Array.Empty<string>(), Ref = "", Second = null, Duo = null, Presentation = "plein" };
        // une source « MyStream seulement » est faite pour Internet : elle y part toujours (sans ce que l'église garde dans un coin)
        if (r is { Mode: "media", Media.StreamingSeulement: true }) return StreamSuitEglise ? r : r with { Duo = null };
        if (!StreamSuitEglise) return seule;
        // MyStream suit l'église ; quand elle n'a rien pour Internet, la source principale (avec ce qui est gardé dans un coin)
        if (Cible is CibleDefaut || r.Mode is "vide" or "fond" || r is { Mode: "media", Media.EgliseSeulement: true })
            return r.Duo != null ? seule with { Duo = r.Duo } : seule;
        return r;
    }

    /// <summary>Le texte posé sur une image (le dessin le met en bandeau par-dessus l'image).</summary>
    public static bool TexteSurImage(EtatEcran e) => e.Media != null && e.Mode is "verset" or "chant";
}
