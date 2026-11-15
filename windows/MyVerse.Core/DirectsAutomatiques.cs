using System.Text.Json.Serialization;

namespace MyVerse;

/// <summary>
/// MyChurch 1.6.4 : un direct automatique. Chaque semaine, les jours choisis à l'heure dite (chaque dimanche à 10:00, chaque vendredi
/// à 17:40…), MyChurch démarre le direct tout seul, avec ses réglages (titre, description, confidentialité, catégorie, « pour les
/// enfants ? », latence, DVR, miniature, qualité, ce que montre MyStream), et l'arrête après la durée choisie.
/// MyChurch doit être ouvert sur l'ordinateur : il le garde éveillé avant le direct et le réveille s'il dort (si Windows le permet).
/// </summary>
public sealed class DirectProgramme
{
    [JsonPropertyName("id")] public string Id { get; set; } = NouvelId();
    [JsonPropertyName("actif")] public bool Actif { get; set; } = true;
    /// <summary>Les jours de la semaine (0 = dimanche … 6 = samedi, comme <see cref="DayOfWeek"/>).</summary>
    [JsonPropertyName("jours")] public List<int> Jours { get; set; } = new() { 0 };
    /// <summary>L'heure du début, « HH:mm » (l'heure de l'ordinateur).</summary>
    [JsonPropertyName("heure")] public string Heure { get; set; } = "10:00";
    /// <summary>Arrêter le direct tout seul après tant de minutes ; 0 : à la main (« Arrêter le direct »).</summary>
    [JsonPropertyName("duree")] public int Duree { get; set; }
    /// <summary>« 720p », « 1080p » ou « 2160p » ; vide : la qualité choisie dans « Image et son du direct ».</summary>
    [JsonPropertyName("qualite")] public string Qualite { get; set; } = "";
    /// <summary>Ce que montre MyStream au début (id d'une source Display, qui devient la source principale) ; vide : la source principale du moment.</summary>
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    /// <summary>Titre, description… « {date} » dans le titre ou la description devient la date du direct (« dimanche 15 novembre 2026 »).</summary>
    [JsonPropertyName("modele")] public ModeleDirect Modele { get; set; } = new();
    /// <summary>Le dernier début traité (démarré, passé parce qu'un direct tournait déjà, ou abandonné) : il ne repart pas.</summary>
    [JsonPropertyName("dernier")] public DateTime? Dernier { get; set; }

    /// <summary>Un début se lance jusqu'à 15 minutes après l'heure (MyChurch ouvert en retard, ordinateur qui se réveille…).</summary>
    public static readonly TimeSpan Marge = TimeSpan.FromMinutes(15);

    public static string NouvelId() => Guid.NewGuid().ToString("N")[..12];

    public void Normaliser()
    {
        if (string.IsNullOrWhiteSpace(Id)) Id = NouvelId();
        Jours = (Jours ?? new()).Where(j => j is >= 0 and <= 6).Distinct().OrderBy(j => j).ToList();
        Heure = LireHeure(Heure) is { } h ? $"{h.Hours:00}:{h.Minutes:00}" : "10:00";
        Duree = Math.Clamp(Duree, 0, 12 * 60);
        if (Qualite is not ("" or "720p" or "1080p" or "2160p")) Qualite = "";
        Source ??= "";
        Modele ??= new ModeleDirect();
        Modele.Normaliser();
    }

    /// <summary>« 10:00 », « 10h », « 10 h 30 », « 17h40 », « 9:05 » : l'heure ; null si ce n'en est pas une.</summary>
    public static TimeSpan? LireHeure(string? texte)
    {
        var t = (texte ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace('h', ':').Replace('.', ':');
        if (t.Length == 0) return null;
        if (t.EndsWith(':')) t += "00";
        if (!t.Contains(':')) t += ":00";
        var p = t.Split(':');
        if (p.Length != 2 || p[0].Length is 0 or > 2 || p[1].Length is 0 or > 2 || !p[0].All(char.IsDigit) || !p[1].All(char.IsDigit)) return null;
        int hh = int.Parse(p[0]), mm = int.Parse(p[1]);
        if (hh > 23 || mm > 59) return null;
        return new TimeSpan(hh, mm, 0);
    }

    /// <summary>Le prochain début à partir de <paramref name="depuis"/> (compris), ou null (aucun jour choisi).</summary>
    public DateTime? Prochain(DateTime depuis)
    {
        if (Jours.Count == 0 || LireHeure(Heure) is not { } h) return null;
        for (int i = 0; i <= 7; i++)
        {
            var jour = depuis.Date.AddDays(i);
            if (!Jours.Contains((int)jour.DayOfWeek)) continue;
            var t = jour + h;
            if (t >= depuis) return t;
        }
        return null;
    }

    /// <summary>Le dernier début passé avant <paramref name="maintenant"/> (compris), au plus une semaine avant, ou null.</summary>
    public DateTime? Precedent(DateTime maintenant)
    {
        if (Jours.Count == 0 || LireHeure(Heure) is not { } h) return null;
        for (int i = 0; i <= 7; i++)
        {
            var jour = maintenant.Date.AddDays(-i);
            if (!Jours.Contains((int)jour.DayOfWeek)) continue;
            var t = jour + h;
            if (t <= maintenant) return t;
        }
        return null;
    }

    /// <summary>Le début à lancer maintenant : arrivé depuis moins de <see cref="Marge"/> et pas encore traité ; null sinon.</summary>
    public DateTime? AFaire(DateTime maintenant)
    {
        if (!Actif || Precedent(maintenant) is not { } t) return null;
        if (maintenant - t > Marge) return null;
        if (Dernier is { } d && d >= t) return null;
        return t;
    }

    static readonly string[] NomsJours = { "dimanche", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi" };
    static readonly string[] NomsMois = { "janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre" };
    /// <summary>Les jours dans l'ordre de la semaine en français : lundi d'abord, dimanche à la fin.</summary>
    static int Rang(int j) => j == 0 ? 7 : j;

    public static string NomJour(int j) => NomsJours[((j % 7) + 7) % 7];

    /// <summary>« dimanche 15 novembre 2026 » (sans dépendre de la langue de Windows).</summary>
    public static string DateLongue(DateTime d) => $"{NomsJours[(int)d.DayOfWeek]} {d.Day} {NomsMois[d.Month - 1]} {d.Year}";

    /// <summary>« dimanche 22 novembre à 10:00 » ; « aujourd'hui à 10:00 », « demain à 10:00 » quand c'est proche.</summary>
    public static string Moment(DateTime quand, DateTime maintenant)
    {
        string jour = quand.Date == maintenant.Date ? "aujourd'hui"
            : quand.Date == maintenant.Date.AddDays(1) ? "demain"
            : $"{NomsJours[(int)quand.DayOfWeek]} {quand.Day} {NomsMois[quand.Month - 1]}";
        return $"{jour} à {quand:HH:mm}";
    }

    /// <summary>« Chaque dimanche à 10:00 », « Chaque vendredi et dimanche à 17:40 », « Tous les jours à 9:00 », « Aucun jour choisi ».</summary>
    public string Quand
    {
        get
        {
            if (Jours.Count == 0) return "Aucun jour choisi";
            string h = LireHeure(Heure) is { } t ? $"{t.Hours:00}:{t.Minutes:00}" : Heure;
            if (Jours.Count == 7) return $"Tous les jours à {h}";
            var noms = Jours.OrderBy(Rang).Select(NomJour).ToList();
            string liste = noms.Count == 1 ? noms[0] : string.Join(", ", noms.Take(noms.Count - 1)) + " et " + noms[^1];
            return $"Chaque {liste} à {h}";
        }
    }

    /// <summary>« arrêt après 2 h 30 » ou « arrêt à la main ».</summary>
    public string Fin => Duree <= 0 ? "arrêt à la main" : "arrêt après " + DureeTexte(Duree);

    /// <summary>90 → « 1 h 30 », 120 → « 2 h », 45 → « 45 min ».</summary>
    public static string DureeTexte(int minutes) =>
        minutes < 60 ? $"{minutes} min" : minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60:00}";

    /// <summary>« {date} » → « dimanche 15 novembre 2026 », « {jour} » → « dimanche », « {heure} » → « 10:00 ».</summary>
    public static string Remplir(string texte, DateTime quand) =>
        (texte ?? "").Replace("{date}", DateLongue(quand), StringComparison.OrdinalIgnoreCase)
            .Replace("{jour}", NomsJours[(int)quand.DayOfWeek], StringComparison.OrdinalIgnoreCase)
            .Replace("{heure}", quand.ToString("HH:mm"), StringComparison.OrdinalIgnoreCase);

    /// <summary>Les réglages du direct de ce début : « {date} » remplacé dans le titre et la description (le titre : 100 caractères au plus).</summary>
    public ModeleDirect ModelePour(DateTime quand)
    {
        var m = new ModeleDirect
        {
            Titre = Remplir(Modele.Titre, quand).Trim(), Description = Remplir(Modele.Description, quand), Confidentialite = Modele.Confidentialite,
            Categorie = Modele.Categorie, Enfants = Modele.Enfants, Latence = Modele.Latence, Dvr = Modele.Dvr, Video360 = Modele.Video360, Miniature = Modele.Miniature,
        };
        if (m.Titre.Length == 0) m.Titre = "Culte du " + DateLongue(quand);
        if (m.Titre.Length > 100) m.Titre = m.Titre[..100].TrimEnd();
        if (m.Description.Length > 5000) m.Description = m.Description[..5000];
        m.Normaliser();
        return m;
    }

    /// <summary>Un nouveau direct automatique : chaque dimanche à 10:00, « Culte du {date} », public, pas pour les enfants, arrêt à la main.</summary>
    public static DirectProgramme Nouveau(ModeleDirect? base_ = null)
    {
        var p = new DirectProgramme();
        if (base_ != null)
        {
            p.Modele = new ModeleDirect
            {
                Description = base_.Description, Confidentialite = base_.Confidentialite, Categorie = base_.Categorie, Enfants = base_.Enfants ?? false,
                Latence = base_.Latence, Dvr = base_.Dvr, Video360 = base_.Video360, Miniature = base_.Miniature,
            };
        }
        p.Modele.Titre = "Culte du {date}";
        p.Normaliser();
        return p;
    }
}
