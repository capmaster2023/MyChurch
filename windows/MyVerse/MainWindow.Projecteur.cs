using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace MyVerse.Ui;

/// <summary>
/// MyChurch 1.0 : le projecteur.
/// • Pas de veille tant que la projection est ouverte (45 min de prédication sans toucher la souris) : Windows n'éteint ni l'écran
///   ni le PC, et l'économiseur d'écran ne démarre pas. Pendant un enregistrement, le PC reste éveillé.
/// • Projecteur débranché : la projection se ferme tout de suite (Windows la poserait sinon sur l'écran de la régie) et MyChurch
///   l'attend ; rebranché : elle se rouvre toute seule dessus, à sa nouvelle place et à sa nouvelle résolution.
/// • Au démarrage : la projection laissée ouverte se rouvre sur le dernier écran — jamais en plein écran sur l'écran de la régie.
/// </summary>
public partial class MainWindow
{
    /// <summary>La projection plein écran attend son projecteur (débranché, ou pas encore branché au démarrage).</summary>
    bool attenteProjecteur;
    /// <summary>Fermeture décidée par MyChurch (projecteur débranché) : la projection reste « ouverte » dans les réglages.</summary>
    bool fermetureAuto;
    /// <summary>MyChurch se ferme : la projection se rouvrira au prochain démarrage.</summary>
    bool fermetureApp;
    /// <summary>Veille demandée à Windows : -1 rien encore, sinon 1 = écran (projection), 2 = PC (enregistrement).</summary>
    int veille = -1;
    string signatureEcrans = "";
    readonly DispatcherTimer verifEcrans = new() { Interval = TimeSpan.FromSeconds(1.5) };
    readonly DispatcherTimer surveillanceEcrans = new() { Interval = TimeSpan.FromSeconds(3) };

    void InitProjecteur()
    {
        // Windows envoie plusieurs WM_DISPLAYCHANGE pendant qu'il installe un écran : on regarde 1,5 s après le dernier
        verifEcrans.Tick += (_, _) => { verifEcrans.Stop(); VerifierEcrans(); };
        // filet de sécurité si WM_DISPLAYCHANGE n'arrive pas : un relevé léger des écrans toutes les 3 s
        surveillanceEcrans.Tick += (_, _) =>
        {
            var s = Natif.SignatureEcrans();
            if (s == signatureEcrans) return;
            signatureEcrans = s;
            EcransChanges();
        };
        SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(Messages);
            signatureEcrans = Natif.SignatureEcrans();
            if (App.AutoTest == null) surveillanceEcrans.Start();
        };
        App.Audio.Recorder.Changed += () => Dispatcher.BeginInvoke(new Action(MajVeille));
        Application.Current.SessionEnding += (_, _) => fermetureApp = true;
        Dispatcher.ShutdownStarted += (_, _) => fermetureApp = true;
    }

    IntPtr Messages(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_DISPLAYCHANGE = 0x007E;
        if (msg == WM_DISPLAYCHANGE) EcransChanges();
        // pas d'économiseur d'écran pendant la projection (la régie est au premier plan)
        else if (ecran != null && Natif.EstEconomiseur(msg, wParam)) handled = true;
        return IntPtr.Zero;
    }

    /// <summary>
    /// Les écrans ont changé. Le projecteur de la projection plein écran a disparu : elle se ferme tout de suite.
    /// Le reste (rouvrir, suivre une nouvelle place ou résolution) 1,5 s plus tard, quand Windows a fini.
    /// </summary>
    void EcransChanges()
    {
        if (ecran is { Plein: true } w)
        {
            var m = w.Moniteur;
            bool la = Natif.Ecrans().Any(e => (m.Principal || !e.Principal)
                && (m.Peripherique.Length > 0 ? string.Equals(e.Peripherique, m.Peripherique, StringComparison.OrdinalIgnoreCase) : e.X == m.X && e.Y == m.Y));
            if (!la) FermerProjectionAuto();
        }
        verifEcrans.Stop();
        verifEcrans.Start();
    }

    /// <summary>
    /// Relevé des écrans : la projection plein écran se ferme (projecteur absent), suit son écran (nouvelle place, nouvelle résolution)
    /// ou se rouvre (projecteur rebranché). <paramref name="simule"/> : écrans inventés par le test automatique.
    /// </summary>
    public void VerifierEcrans(List<Moniteur>? simule = null)
    {
        var ecrans = simule ?? Natif.Ecrans();
        var choix = App.Donnees.Reglages.Ecran;
        if (ecran is { Plein: true } w)
        {
            // ouverte par l'opérateur sur l'écran principal : elle peut y rester ; sinon jamais (elle couvrirait la régie)
            var m = Projecteur.Retrouver(choix, ecrans, choix?.Principal == true);
            if (m == null) FermerProjectionAuto();
            else if (m != w.Moniteur)
            {
                w.Deplacer(m);
                App.Donnees.Reglages.Ecran = Projecteur.Choix(m, true, choix);
                App.Donnees.SauverReglages();
                Journal.Info($"projecteur : {m.Nom} {m.W}×{m.H} @ {m.X},{m.Y}, la projection suit");
            }
        }
        else if (ecran == null && attenteProjecteur && Projecteur.Retrouver(choix, ecrans) is { } m)
        {
            Journal.Info($"projecteur rebranché : {m.Nom} {m.W}×{m.H} @ {m.X},{m.Y}");
            OuvrirSur(m, true, $"Projecteur rebranché : projection rouverte sur {m.Nom}.");
        }
        MajPastille();
        MajPageEcrans();
    }

    /// <summary>Réglages › Écrans, s'ils sont à l'écran, suivent les écrans branchés et la projection.</summary>
    void MajPageEcrans()
    {
        if (Reglages is { IsVisible: true } r) r.MajEcrans();
    }

    /// <summary>Le projecteur a disparu : la projection se ferme (sinon Windows la pose sur l'écran de la régie) et MyChurch l'attend.</summary>
    void FermerProjectionAuto()
    {
        var choix = App.Donnees.Reglages.Ecran;
        var nom = choix?.Nom is { Length: > 0 } n ? n : "le projecteur";
        attenteProjecteur = choix is { Mode: "plein", Principal: false };
        fermetureAuto = true;
        try { ecran?.Close(); }
        finally { fermetureAuto = false; }
        Journal.Info("projecteur débranché : projection fermée" + (attenteProjecteur ? ", en attente" : ""));
        Toast(attenteProjecteur
            ? $"Projecteur débranché ({nom}). La projection se rouvrira toute seule dès qu'il sera rebranché."
            : "Écran de projection débranché : projection fermée.");
        MajPastille();
        MajPageEcrans();
    }

    /// <summary>Au démarrage : la projection laissée ouverte se rouvre sur le dernier écran (attendu s'il n'est pas encore branché).</summary>
    public void RouvrirProjection(List<Moniteur>? simule = null)
    {
        var choix = App.Donnees.Reglages.Ecran;
        if (ecran != null || choix == null || !Projecteur.ARouvrir(choix)) return;
        var ecrans = simule ?? Natif.Ecrans();
        if (choix.Mode == "fenetre")
        {
            if ((ecrans.FirstOrDefault(e => e.Principal) ?? ecrans.FirstOrDefault()) is { } p) OuvrirSur(p, false, "Projection rouverte en fenêtre.");
            return;
        }
        if (Projecteur.Retrouver(choix, ecrans) is { } m)
        {
            Journal.Info($"démarrage : projection rouverte sur {m.Nom}");
            OuvrirSur(m, true, $"Projection rouverte sur {m.Nom}.");
            return;
        }
        attenteProjecteur = true;
        var nom = choix.Nom is { Length: > 0 } n ? n : "le projecteur";
        Journal.Info($"démarrage : {nom} n'est pas branché, la projection l'attend");
        Toast($"Projecteur non branché ({nom}) : la projection s'ouvrira toute seule dès qu'il sera branché.");
        MajPastille();
    }

    /// <summary>« Arrêter la projection » : elle se ferme et ne se rouvrira pas toute seule (ni rebranchement, ni démarrage).</summary>
    public void ArreterProjection()
    {
        attenteProjecteur = false;
        if (App.Donnees.Reglages.Ecran is { } c && c.Ouvert != false)
        {
            c.Ouvert = false;
            App.Donnees.SauverReglages();
        }
        ecran?.Close();
        MajPastille();
    }

    /// <summary>La projection attend son projecteur débranché.</summary>
    public bool AttenteProjecteur => ecran == null && attenteProjecteur;

    /// <summary>
    /// Pas de veille tant que la projection est ouverte (l'écran reste allumé, le PC éveillé) ; pendant un enregistrement,
    /// le PC reste éveillé. Sinon, Windows reprend ses réglages d'alimentation.
    /// </summary>
    void MajVeille()
    {
        bool projection = ecran != null, enregistrement = App.Audio?.Recorder.AnyBusy == true;
        int v = (projection ? 1 : 0) | (enregistrement ? 2 : 0);
        if (v == veille) return;
        veille = v;
        if (!Natif.Eveil(projection, enregistrement)) Journal.Erreur("anti-veille : SetThreadExecutionState refusé");
        Journal.Info(projection ? "anti-veille : projection ouverte, l'écran reste allumé" : enregistrement ? "anti-veille : enregistrement, le PC reste éveillé" : "anti-veille : arrêtée");
    }

    // ------------------------------------------------------------ pour le test automatique
    /// <summary>Test : « écran » (projection), « PC » (enregistrement), « écran + PC », « non ».</summary>
    public string TestVeille => veille switch { 1 => "écran", 2 => "PC", 3 => "écran + PC", _ => "non" };
}
