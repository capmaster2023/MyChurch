using System.Windows.Threading;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

/// <summary>Ce que dit la carte d'un son : un mot simple, sa couleur (clé de la palette) et le verdict.</summary>
public sealed record EtatSon(VerdictNiveau Verdict, string Texte, string Couleur, bool Actif);

/// <summary>
/// Le son de MyChurch (1.3). Garde les réglages (les sons, et où va le son), prépare le plan du moteur et le donne à OBS (libobs,
/// livrée dans le dossier « obs » à côté de MyChurch.exe). Suit aussi les niveaux (« Bon niveau », « Trop fort ! »), les appareils
/// branchés et débranchés, les musiques (Lire, Pause), les envois vers Zoom ou OBS et le casque de la régie.
/// L'utilisateur ne voit jamais de piste, de monitoring ni de filtre : <see cref="AudioConfiguration.Planifier"/> les décide.
/// </summary>
public sealed class ChurchAudioService : IDisposable
{
    readonly ObsMixer mixer = new();
    readonly CaptureEntrees entrees;
    readonly CaptureAsio asio;
    readonly DispatcherTimer sauvegarde = new() { Interval = TimeSpan.FromMilliseconds(600) };
    readonly DispatcherTimer veille = new() { Interval = TimeSpan.FromMilliseconds(500) };
    /// <summary>Relevé des appareils toutes les 2 s : un son dont l'appareil est débranché devient « Débranché », puis revient.</summary>
    readonly DispatcherTimer releve = new() { Interval = TimeSpan.FromSeconds(2) };
    /// <summary>Les niveaux de chaque son, 20 fois par seconde : ils donnent « Bon niveau », « Trop fort ! »…</summary>
    readonly DispatcherTimer niveaux = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
    readonly Dictionary<string, SuiviNiveau> suivis = new();
    List<AudioDevice> micros = new(), sortiesAudio = new();
    bool releveFait, releveEnCours, erreurReleve;
    string videoProjetee = "";

    public AudioConfiguration Config { get; }
    public ChurchRecordingService Recorder { get; }
    /// <summary>Les envois (Zoom, OBS…) et le casque de la régie.</summary>
    public SortiesVirtuelles Sorties { get; }
    /// <summary>Le dernier plan donné au moteur (pistes des destinations, sources d'OBS).</summary>
    public PlanAudio Plan { get; private set; } = new();
    public string Diagnostic { get; private set; } = "Le moteur du son démarre…";
    /// <summary>Le dossier « obs » manque ou est incomplet : la page Son propose de réparer (réinstaller MyChurch).</summary>
    public bool ObsAbsent { get; private set; }
    /// <summary>ffmpeg (MP3 et vidéo MKV), livré avec MyChurch.</summary>
    public string Ffmpeg => FfmpegInclus;
    public static string FfmpegInclus => Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
    public static string DossierObs => Path.Combine(AppContext.BaseDirectory, "obs");
    public bool Running => mixer.Running;
    public bool TracksActive => mixer.TracksActive;
    public string Version => mixer.Version;
    public event Action? Changed;
    /// <summary>Un appareil a été branché ou débranché.</summary>
    public event Action? PeripheriquesChange;

    public ChurchAudioService(AudioConfiguration config)
    {
        Config = config;
        Config.Normalize();
        entrees = new CaptureEntrees(mixer.Pousser);
        asio = new CaptureAsio(mixer.Pousser);
        Recorder = new(this);
        Sorties = new(this);
        releve.Tick += (_, _) => Relever();
        sauvegarde.Tick += (_, _) => { sauvegarde.Stop(); try { App.Donnees.SauverReglages(); } catch (Exception e) { Journal.Erreur("réglages du son", e); } };
        veille.Tick += async (_, _) => { try { await Recorder.PollAsync(); } catch (Exception e) { Journal.Erreur("enregistrement", e); } };
        veille.Start();
        niveaux.Tick += (_, _) => SuivreNiveaux();
        App.Regie.Change += ProjectionChange;
        ObsMixer.Log += (niveau, texte) =>
        {
            if (niveau <= 100) Journal.Info("OBS (erreur) : " + texte.Trim());
            else if (niveau <= 200) Journal.Info("OBS : " + texte.Trim());
        };
    }

    /// <summary>Démarre le moteur puis recrée les sons enregistrés. Sans le moteur, le reste de MyChurch fonctionne normalement.</summary>
    public void Start()
    {
        if (mixer.Running) return;
        if (App.AutoTest == null)
        {
            Relever();
            releve.Start();
        }
        // MyChurch n'est jamais baissé par Windows pendant un appel (Zoom, Teams) : le son envoyé à Zoom reste au même niveau
        if (!AttenuationWindows.Desactiver(true)) Journal.Info("son : atténuation de Windows non désactivée (aucune sortie audio ?)");
        var chemins = ObsPaths.Windows(DossierObs);
        if (!chemins.Present)
        {
            ObsAbsent = true;
            Diagnostic = "Le moteur du son est absent (dossier « obs » de MyChurch manquant ou incomplet). « Réparer » réinstalle MyChurch : vos chants et réglages sont gardés.";
            Journal.Info("son : fichier absent " + chemins.Library);
            Changed?.Invoke();
            return;
        }
        ObsAbsent = false;
        try
        {
            mixer.Start(chemins);
            Apply(false);
            Diagnostic = mixer.TracksActive ? "Le moteur du son est prêt." : "Le moteur du son est prêt, mais cet ordinateur ne peut pas mélanger pour l'enregistrement et Zoom (pilote de la carte graphique).";
            Journal.Info($"son : libobs {mixer.Version}, modules {string.Join(", ", mixer.Modules)}, pistes {(mixer.TracksActive ? "actives" : "inactives")}");
            niveaux.Start();
        }
        catch (Exception e)
        {
            Diagnostic = "Le moteur du son ne démarre pas : " + (e is DllNotFoundException || e.Message.Contains("0x8007007E")
                ? "un fichier du dossier « obs » ne se charge pas (manquant ou bloqué par l'antivirus). « Réparer » réinstalle MyChurch."
                : e.Message);
            ObsAbsent = e is DllNotFoundException or BadImageFormatException;
            Journal.Erreur("moteur du son (OBS)", e);
        }
        Changed?.Invoke();
    }

    /// <summary>Le son des vidéos suit ce qui part à l'écran depuis Display.</summary>
    void ProjectionChange()
    {
        var e = App.Regie.Etat;
        string chemin = e.Mode == "media" && e.Media?.Type == "video" ? e.Media.Chemin ?? "" : "";
        if (chemin == videoProjetee) return;
        videoProjetee = chemin;
        Apply(false);
    }

    /// <summary>La vidéo de Display à l'écran (vide s'il n'y en a pas).</summary>
    public string VideoProjetee => videoProjetee;

    /// <summary>Donne les réglages au moteur tout de suite ; l'écriture sur disque suit un instant plus tard.</summary>
    public void Apply(bool sauverMaintenant = true)
    {
        Config.Normalize();
        sauvegarde.Stop(); sauvegarde.Start();
        try { Plan = Config.Planifier(videoProjetee, Recorder.Reserves.ToHashSet()); }
        catch (Exception e) { Journal.Erreur("plan du son", e); }
        try { mixer.Apply(Plan.Moteur, Config.Eglise.Appareil, Config.Eglise.NomAppareil); }
        catch (Exception e) { Diagnostic = "Moteur du son : " + e.Message; Journal.Erreur("réglages du son (OBS)", e); }
        Sorties.Apply(Config, Plan);
        // une entrée seule d'une carte son : MyChurch écoute la carte et donne à chaque son son entrée
        try { entrees.Apply(mixer.EntreesSeparees ? Plan.Moteur : Enumerable.Empty<AudioSource>()); }
        catch (Exception e) { Journal.Erreur("entrées séparées", e); }
        // carte son professionnelle (ASIO) : MyChurch ouvre le pilote et donne à chaque son ses entrées
        try { asio.Apply(mixer.EntreesSeparees ? Plan.Moteur : Enumerable.Empty<AudioSource>()); }
        catch (Exception e) { Journal.Erreur("ASIO", e); }
        Recorder.SuivrePistes();
        foreach (var id in suivis.Keys.Where(id => Config.Sons.All(s => s.Id != id)).ToList()) suivis.Remove(id);
        Changed?.Invoke();
    }

    /// <summary>La piste d'OBS d'une destination (enregistrement, envoi, casque), d'après le dernier plan.</summary>
    public int PisteDe(string destination) => Plan.PisteDe(destination);

    // ------------------------------------------------------------ niveaux et mots simples
    void SuivreNiveaux()
    {
        long maintenant = Environment.TickCount64;
        foreach (var s in Config.Sons)
        {
            if (!suivis.TryGetValue(s.Id, out var suivi)) suivis[s.Id] = suivi = new SuiviNiveau();
            var (l, r) = mixer.Level(s.Id);
            suivi.Ajouter(Math.Max(l, r), maintenant);
        }
    }

    /// <summary>Crêtes gauche et droite d'un son en dBFS (−∞ = silence), après son volume et ses aides.</summary>
    public (float L, float R) Level(string id) => mixer.Level(id);
    public float TrackLevel(int piste) => mixer.TrackLevel(piste);

    /// <summary>« Bon niveau », « Trop fort ! », « Coupé », « Débranché »… : ce que dit la carte d'un son.</summary>
    public EtatSon Etat(Son s)
    {
        if (!Running) return new(VerdictNiveau.Silence, ObsAbsent ? "Moteur du son absent" : "Moteur du son arrêté", "Brume2", false);
        if (s.Coupe) return new(VerdictNiveau.Silence, "Coupé", "RougeTexte", false);
        if (Debranche(s)) return new(VerdictNiveau.Silence, s.Genre == GenreSon.Fichier ? "Fichier introuvable" : "Débranché", "Brume2", false);
        if (s.Genre == GenreSon.Video && videoProjetee.Length == 0) return new(VerdictNiveau.Silence, "Pas de vidéo à l'écran", "Brume2", true);
        if (s.Genre == GenreSon.Fichier)
        {
            var (etat, _, _) = mixer.Lecture(s.Id);
            if (etat == EtatLecture.Pause) return new(VerdictNiveau.Silence, "En pause", "Brume2", true);
            if (etat is EtatLecture.Arret or EtatLecture.Aucun) return new(VerdictNiveau.Silence, "Prête : cliquez Lire", "Brume2", true);
            if (etat == EtatLecture.Fin) return new(VerdictNiveau.Silence, "Terminée", "Brume2", true);
            if (etat == EtatLecture.Erreur) return new(VerdictNiveau.Silence, "Fichier illisible", "RougeTexte", false);
        }
        if (s.Capture == AudioInputKind.Asio && asio.Statut(s.Appareil) is { Length: > 0 }) return new(VerdictNiveau.Silence, "Carte son occupée", "RougeTexte", false);
        var v = suivis.TryGetValue(s.Id, out var suivi) ? suivi.Verdict(Environment.TickCount64) : VerdictNiveau.Silence;
        return v switch
        {
            VerdictNiveau.TropFort => new(v, "Trop fort !", "Direct", true),
            VerdictNiveau.Bon => new(v, "Bon niveau", "Vert", true),
            VerdictNiveau.Faible => new(v, "Un peu faible", "Or", true),
            _ => new(v, "Rien pour l'instant", "Brume2", true),
        };
    }

    /// <summary>Pourquoi un son ne marche pas, en une phrase (vide si tout va bien) : pour les info-bulles.</summary>
    public string Probleme(Son s)
    {
        if (Debranche(s)) return s.Genre == GenreSon.Fichier ? "Le fichier n'existe plus : choisissez-le de nouveau dans Régler." : "L'appareil est débranché : le son revient tout seul quand il est rebranché.";
        if (s.Capture == AudioInputKind.Asio) return asio.Statut(s.Appareil);
        if (s.Capture == AudioInputKind.Microphone && s.Entree > 0) return entrees.Statut(s.Appareil);
        return "";
    }

    // ------------------------------------------------------------ musique (fichier)
    public void Jouer(Son s) { mixer.Jouer(s.Id); Changed?.Invoke(); }
    public void Pause(Son s) { mixer.Pause(s.Id); Changed?.Invoke(); }
    public void Arreter(Son s) { mixer.Arreter(s.Id); Changed?.Invoke(); }
    public (EtatLecture Etat, long Position, long Duree) Lecture(Son s) => mixer.Lecture(s.Id);

    // ------------------------------------------------------------ appareils branchés et débranchés
    /// <summary>Micros et cartes son branchés (le dernier relevé de Windows, sinon la liste d'OBS).</summary>
    public List<AudioDevice> Microphones()
    {
        if (releveFait) return micros.ToList();
        try { return mixer.Microphones(); } catch (Exception e) { Journal.Erreur("liste des micros", e); return new(); }
    }
    /// <summary>Sorties de l'ordinateur (haut-parleurs, casques, câbles virtuels).</summary>
    public List<AudioDevice> Outputs()
    {
        if (releveFait) return sortiesAudio.ToList();
        try { return mixer.Outputs(); } catch (Exception e) { Journal.Erreur("liste des sorties", e); return new(); }
    }

    /// <summary>Débranché : son appareil n'est plus là (ou il n'y en a aucun), ou son fichier manque. Tant qu'aucun relevé n'est fait, rien n'est débranché.</summary>
    public bool Debranche(Son s)
    {
        if (s.Genre == GenreSon.Fichier) return s.Fichier.Length == 0 || !File.Exists(s.Fichier);
        if (!releveFait || s.Genre == GenreSon.Video) return false;
        return s.Capture switch
        {
            AudioInputKind.Microphone => s.Appareil == "default" ? micros.Count == 0 : micros.All(d => d.Id != s.Appareil),
            AudioInputKind.Asio => !CaptureAsio.Pilotes().Contains(s.Appareil),
            AudioInputKind.Sortie => s.Appareil == "default" ? sortiesAudio.Count == 0 : sortiesAudio.All(d => d.Id != s.Appareil),
            _ => false,
        };
    }

    async void Relever()
    {
        if (releveEnCours) return;
        releveEnCours = true;
        try
        {
            var (e, s) = await Task.Run(() => PeripheriquesAudio.Lister());
            Traiter(e, s);
            erreurReleve = false;
        }
        catch (Exception x)
        {
            if (!erreurReleve) Journal.Erreur("relevé des appareils audio", x);
            erreurReleve = true;
        }
        finally { releveEnCours = false; }
    }

    /// <summary>Un relevé des appareils : les sons d'un appareil débranché deviennent « Débranché » et reviennent quand il est rebranché.
    /// Rien n'est ajouté tout seul : on ajoute un son avec « + Ajouter un son ».</summary>
    internal void Traiter(List<AudioDevice> entreesTrouvees, List<AudioDevice> sortiesTrouvees)
    {
        bool premier = !releveFait;
        bool change = premier || !PeripheriquesAudio.Memes(entreesTrouvees, micros) || !PeripheriquesAudio.Memes(sortiesTrouvees, sortiesAudio);
        micros = entreesTrouvees;
        sortiesAudio = sortiesTrouvees;
        releveFait = true;
        // un son branché sur une entrée seule retrouve sa carte son rebranchée
        if (change && !premier) Apply(false);
        if (change) PeripheriquesChange?.Invoke();
    }

    /// <summary>Oublie le dernier relevé (test automatique, après un relevé simulé) : plus rien n'est débranché jusqu'au prochain.</summary>
    internal void OublierReleve()
    {
        releveFait = false;
        micros = new();
        sortiesAudio = new();
        PeripheriquesChange?.Invoke();
    }

    /// <summary>Le son est une source du moteur (test automatique).</summary>
    public bool InputReady(string id) => mixer.Has(id);
    /// <summary>Une entrée seule d'une carte son est possible (moteur démarré, type « entrée » accepté par OBS).</summary>
    public bool EntreesSeparees => mixer.EntreesSeparees;
    /// <summary>Ce son écoute une entrée seule (son poussé par MyChurch) : test automatique.</summary>
    public bool EstEntree(string id) => mixer.EstEntree(id);
    /// <summary>Nombre d'entrées d'un micro ou d'une carte son (0 s'il est absent).</summary>
    public static int Entrees(string deviceId) => CaptureEntrees.Entrees(deviceId);
    /// <summary>Pilotes ASIO installés (cartes son professionnelles).</summary>
    public static List<string> PilotesAsio() => CaptureAsio.Pilotes();
    /// <summary>Noms des entrées d'un pilote ASIO (vide s'il ne s'ouvre pas).</summary>
    public List<string> EntreesAsio(string pilote) => asio.Entrees(pilote);
    /// <summary>Le panneau de réglage du fabricant de la carte son.</summary>
    public void PanneauAsio(string pilote) => asio.PanneauConfiguration(pilote);
    public string StatutAsio(string pilote) => asio.Statut(pilote);
    internal void AddTrackConsumer(int piste, Action<float[], long> c) => mixer.AddTrackConsumer(piste, c);
    internal void RemoveTrackConsumer(int piste, Action<float[], long> c) => mixer.RemoveTrackConsumer(piste, c);

    public void Dispose()
    {
        sauvegarde.Stop(); veille.Stop(); releve.Stop(); niveaux.Stop();
        try { Sorties.Dispose(); } catch (Exception e) { Journal.Erreur("envois du son", e); }
        try { Recorder.Dispose(); } catch (Exception e) { Journal.Erreur("fin d'enregistrement", e); }
        try { asio.Dispose(); } catch (Exception e) { Journal.Erreur("ASIO", e); }
        try { entrees.Dispose(); } catch (Exception e) { Journal.Erreur("entrées séparées", e); }
        try { mixer.Dispose(); } catch (Exception e) { Journal.Erreur("arrêt du moteur du son", e); }
    }
}
