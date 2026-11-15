using System.Windows.Threading;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Siloé Stream 1.6.4 : les directs automatiques (<see cref="DirectProgramme"/>). Toutes les 15 secondes : un direct programmé dont l'heure
/// est arrivée (depuis moins de 15 minutes) démarre tout seul avec ses réglages, et s'arrête après sa durée. Deux minutes avant,
/// Siloé Stream le dit. S'il ne démarre pas (Internet coupé…), Siloé Stream réessaie chaque minute pendant les 15 minutes.
/// Un direct déjà en cours (démarré à la main) continue : le direct automatique de ce jour-là est passé.
/// Siloé Stream garde l'ordinateur éveillé pendant les 3 heures avant un direct automatique, et le réveille 5 minutes avant s'il dort.
/// </summary>
public sealed class PlanificateurDirect : IDisposable
{
    readonly DirectService direct;
    readonly Donnees donnees;
    readonly Regie regie;
    readonly DispatcherTimer minuterie;
    /// <summary>Le direct en cours a été démarré par ce direct automatique (il s'arrête après sa durée).</summary>
    public DirectProgramme? EnCours { get; private set; }
    /// <summary>L'heure où le direct automatique en cours s'arrête tout seul (null : à la main).</summary>
    public DateTime? ArretPrevu { get; private set; }
    /// <summary>Le titre du direct automatique en cours.</summary>
    public string TitreEnCours { get; private set; } = "";
    /// <summary>Un message pour la fenêtre principale (« Le direct automatique démarre à 10:00… »).</summary>
    public event Action<string>? Annonce;
    /// <summary>Quelque chose a changé (prochain direct, direct automatique en cours).</summary>
    public event Action? Change;

    /// <summary>Les débuts annoncés (deux minutes avant) : une fois chacun.</summary>
    readonly HashSet<string> annonces = new();
    /// <summary>Les débuts qui n'ont pas encore marché : le nombre d'essais, le prochain essai, le direct YouTube déjà créé.</summary>
    sealed class Essai { public int N; public DateTime Prochain; public string? Video; }
    readonly Dictionary<string, Essai> essais = new();
    bool occupe;
    DateTime? reveil;
    /// <summary>Le direct automatique en cours : son début prévu, quand il a démarré, son « dernier » d'avant, le direct YouTube.</summary>
    DateTime? debutPrevu, demarreA, dernierAvant;
    string? videoEnCours;

    ReglagesDirect R => donnees.Reglages.Direct;

    public PlanificateurDirect(DirectService direct, Donnees donnees, Regie regie, bool demarrer = true)
    {
        this.direct = direct; this.donnees = donnees; this.regie = regie;
        minuterie = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(15) };
        minuterie.Tick += async (_, _) => await VerifierAsync(DateTime.Now);
        // le direct s'arrête (à la main, ou une erreur) : le direct automatique n'est plus en cours
        direct.Change += () =>
        {
            if (EnCours == null || direct.Actif) return;
            var p = EnCours;
            // refusé dès le départ (Internet pas encore là, YouTube qui refuse) : un nouvel essai dans 1 minute, tant que l'heure n'est pas trop passée
            if (direct.Etat == EtatDirect.Erreur && demarreA is { } d0 && DateTime.Now - d0 < TimeSpan.FromMinutes(2)
                && debutPrevu is { } t0 && DateTime.Now - t0 < DirectProgramme.Marge)
            {
                p.Dernier = dernierAvant;
                donnees.SauverReglages();
                essais[p.Id + "|" + t0.Ticks] = new Essai { N = 1, Prochain = DateTime.Now.AddMinutes(1), Video = videoEnCours };
                Journal.Info("direct automatique coupé au démarrage : " + direct.Message + " ; nouvel essai dans 1 minute");
                Annonce?.Invoke("Le direct automatique s'est coupé au démarrage : " + direct.Message.TrimEnd('.') + ". Nouvel essai dans 1 minute.");
            }
            EnCours = null; ArretPrevu = null; TitreEnCours = ""; debutPrevu = demarreA = dernierAvant = null; videoEnCours = null;
            Change?.Invoke();
        };
        if (demarrer) minuterie.Start();
        Journal.Info($"directs automatiques : {R.Programmes.Count(p => p.Actif)} actif(s)" + (Prochain(DateTime.Now) is { } x ? ", prochain " + DirectProgramme.Moment(x.Quand, DateTime.Now) : ""));
    }

    /// <summary>Le prochain direct automatique (actif, avec au moins un jour), ou null.</summary>
    public (DirectProgramme P, DateTime Quand)? Prochain(DateTime maintenant)
    {
        (DirectProgramme P, DateTime Quand)? mieux = null;
        foreach (var p in R.Programmes)
        {
            if (!p.Actif || p.Prochain(maintenant) is not { } q) continue;
            if (mieux == null || q < mieux.Value.Quand) mieux = (p, q);
        }
        return mieux;
    }

    /// <summary>L'ordinateur reste éveillé : un direct en cours, un direct automatique dans les 3 heures, ou un essai qui attend.</summary>
    public bool GarderEveille =>
        direct.Actif || essais.Count > 0 || (Prochain(DateTime.Now) is { } x && x.Quand - DateTime.Now <= TimeSpan.FromHours(3));

    /// <summary>« Prochain direct automatique : dimanche 22 novembre à 10:00 (« Culte du … ») » ; vide s'il n'y en a pas.</summary>
    public string Resume(DateTime maintenant)
    {
        if (EnCours != null) return $"Direct automatique en cours : « {TitreEnCours} »" + (ArretPrevu is { } f ? $", arrêt à {f:HH:mm}" : "");
        return Prochain(maintenant) is { } x ? $"Prochain direct automatique : {DirectProgramme.Moment(x.Quand, maintenant)}" : "";
    }

    /// <summary>Un tour : arrêter après la durée, annoncer, démarrer ce qui est arrivé, garder l'ordinateur éveillé. Public pour le test (heure simulée).</summary>
    public async Task VerifierAsync(DateTime maintenant)
    {
        if (occupe) return;
        occupe = true;
        try
        {
            await ArreterSiFiniAsync(maintenant);
            AnnoncerAvant(maintenant);
            foreach (var p in R.Programmes.ToList())
            {
                if (p.AFaire(maintenant) is not { } t) continue;
                string cle = p.Id + "|" + t.Ticks;
                if (essais.TryGetValue(cle, out var e) && maintenant < e.Prochain) continue;
                if (direct.Actif)
                {
                    // un direct tourne déjà (démarré à la main, ou par un autre direct automatique) : il continue, celui-ci est passé
                    p.Dernier = t;
                    essais.Remove(cle);
                    donnees.SauverReglages();
                    Journal.Info($"direct automatique ({p.Quand}) : un direct est déjà en cours, rien à démarrer");
                    if (EnCours == null) Annonce?.Invoke($"Direct automatique de {t:HH:mm} : un direct est déjà en cours, il continue.");
                    continue;
                }
                await LancerAsync(p, t, maintenant);
                break;
            }
            MajReveil(maintenant);
        }
        catch (Exception x) { Journal.Erreur("directs automatiques", x); }
        finally
        {
            occupe = false;
            Change?.Invoke();
        }
    }

    /// <summary>Le direct automatique a fini sa durée : il s'arrête (comme « Arrêter le direct »).</summary>
    async Task ArreterSiFiniAsync(DateTime maintenant)
    {
        if (EnCours == null) return;
        if (!direct.Actif) { EnCours = null; ArretPrevu = null; TitreEnCours = ""; debutPrevu = demarreA = dernierAvant = null; videoEnCours = null; return; }
        if (ArretPrevu is not { } fin || maintenant < fin || !direct.EnDirect) return;
        var p = EnCours;
        string titre = TitreEnCours;
        EnCours = null; ArretPrevu = null; TitreEnCours = ""; debutPrevu = demarreA = dernierAvant = null; videoEnCours = null;
        Journal.Info($"direct automatique « {titre} » : fin prévue ({p.Fin})");
        await direct.ArreterAsync();
        Annonce?.Invoke($"Direct automatique « {titre} » terminé ({p.Fin}).");
    }

    /// <summary>Deux minutes avant un direct automatique : un message dans Siloé Stream.</summary>
    void AnnoncerAvant(DateTime maintenant)
    {
        foreach (var p in R.Programmes)
        {
            if (!p.Actif || p.Prochain(maintenant) is not { } q) continue;
            var reste = q - maintenant;
            if (reste <= TimeSpan.Zero || reste > TimeSpan.FromMinutes(2) || (p.Dernier is { } d && d >= q)) continue;
            if (!annonces.Add(p.Id + "|" + q.Ticks)) continue;
            int min = Math.Max(1, (int)Math.Ceiling(reste.TotalMinutes));
            Annonce?.Invoke(direct.Actif
                ? $"Un direct automatique est prévu à {q:HH:mm}, mais un direct est déjà en cours : il continue."
                : $"Le direct automatique « {p.ModelePour(q).Titre} » démarre à {q:HH:mm} (dans {min} minute{(min > 1 ? "s" : "")}).");
        }
    }

    /// <summary>Démarre le direct automatique du début <paramref name="t"/> ; s'il ne démarre pas, un nouvel essai dans 1 minute.</summary>
    async Task LancerAsync(DirectProgramme p, DateTime t, DateTime maintenant)
    {
        string cle = p.Id + "|" + t.Ticks;
        if (!essais.TryGetValue(cle, out var e)) essais[cle] = e = new Essai();
        e.N++;
        var m = p.ModelePour(t);
        string? qualite = p.Qualite.Length > 0 ? p.Qualite : null;
        Journal.Info($"direct automatique ({p.Quand}) : démarrage de « {m.Titre} » (essai {e.N}, {(qualite ?? R.Qualite)}, {p.Fin})");
        // MyStream : la source choisie pour ce direct, sinon la source principale (MyStream ne suit plus l'église)
        if (p.Source.Length > 0 && regie.SourcesStreamPossibles.Any(a => a.Id == p.Source)) regie.ChoisirSourceStream(p.Source);
        else if (regie.SourcePrincipaleStream != null) regie.StreamPrincipale();
        string raison = "";
        if (R.Mode == ReglagesDirect.Compte)
        {
            if (!direct.Compte.Connecte) raison = "le compte YouTube n'est pas connecté (Le direct sur YouTube › Connecter le compte YouTube)";
            else if (e.Video != null)
                // le direct a été créé sur YouTube au premier essai : on le reprend au lieu d'en créer un autre
                await direct.DemarrerPrevuAsync(new DirectPrevu(e.Video, m.Titre, t, m.Confidentialite, "ready", "", true), qualite);
            else e.Video = await direct.CreerAsync(m, null, qualite, garder: false);
        }
        else await direct.DemarrerAvecCleAsync(qualite);
        if (raison.Length == 0 && direct.Actif)
        {
            dernierAvant = p.Dernier;
            debutPrevu = t; demarreA = DateTime.Now; videoEnCours = e.Video;
            p.Dernier = t;
            essais.Remove(cle);
            donnees.SauverReglages();
            EnCours = p;
            TitreEnCours = R.Mode == ReglagesDirect.Compte ? m.Titre : p.Quand;
            ArretPrevu = p.Duree > 0 ? maintenant.AddMinutes(p.Duree) : null;
            Journal.Info($"direct automatique démarré : « {TitreEnCours} »" + (ArretPrevu is { } f0 ? $", arrêt à {f0:HH:mm}" : ""));
            Annonce?.Invoke($"Direct automatique démarré : « {TitreEnCours} »" + (ArretPrevu is { } f ? $" (arrêt à {f:HH:mm})." : ". Arrêtez-le avec la pastille YouTube."));
            return;
        }
        if (raison.Length == 0) raison = direct.Message.Length > 0 ? direct.Message : "le direct n'a pas démarré";
        raison = raison.TrimEnd('.');
        bool fini = e.N >= 5 || maintenant.AddMinutes(1) - t > DirectProgramme.Marge;
        if (fini)
        {
            p.Dernier = t;
            essais.Remove(cle);
            donnees.SauverReglages();
            Journal.Info($"direct automatique ({p.Quand}) abandonné : {raison}");
            Annonce?.Invoke($"Le direct automatique de {t:HH:mm} n'a pas démarré : {raison}. Démarrez-le à la main (pastille YouTube).");
        }
        else
        {
            e.Prochain = maintenant.AddMinutes(1);
            Journal.Info($"direct automatique ({p.Quand}) : essai {e.N} raté ({raison}), nouvel essai dans 1 minute");
            Annonce?.Invoke($"Le direct automatique n'a pas démarré : {raison}. Nouvel essai dans 1 minute.");
        }
    }

    /// <summary>Le réveil de l'ordinateur 5 minutes avant le prochain direct automatique (s'il est en veille).</summary>
    void MajReveil(DateTime maintenant)
    {
        DateTime? voulu = Prochain(maintenant) is { } x ? x.Quand.AddMinutes(-5) : null;
        if (voulu is { } v && v <= maintenant.AddMinutes(1)) voulu = null; // trop proche : l'ordinateur est déjà gardé éveillé
        if (voulu == reveil) return;
        reveil = voulu;
        if (App.AutoTest != null) return;
        bool ok = Natif.Reveil(voulu);
        Journal.Info(voulu is { } r ? $"directs automatiques : réveil de l'ordinateur prévu {DirectProgramme.Moment(r, maintenant)}{(ok ? "" : " (refusé par Windows)")}" : "directs automatiques : pas de réveil prévu");
    }

    /// <summary>Les directs automatiques ont changé (ajout, modification, suppression) : la fenêtre principale et le réveil suivent.</summary>
    public void Modifie()
    {
        // un début modifié pour plus tard dans la journée ne doit pas être pris pour « déjà fait »
        annonces.Clear();
        MajReveil(DateTime.Now);
        Change?.Invoke();
    }

    public void Dispose()
    {
        minuterie.Stop();
        if (App.AutoTest == null && reveil != null) Natif.Reveil(null);
    }
}
