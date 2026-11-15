using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MyVerse.Ui;

/// <summary>
/// « Capture de fenêtre » (Siloé Stream 1.3.1), comme les propriétés de la capture de fenêtre d'OBS : l'image de la fenêtre en direct,
/// la liste « [Zoom.exe] : titre », la zone client, le nom dans Display, et où va la fenêtre : dans l'église, en streaming ou les deux.
/// Sert à l'ajout (+ Ajouter une source) et à la modification (clic droit › Modifier).
/// </summary>
public sealed class CaptureFenetreWindow : FenetreDialogue
{
    /// <summary>Programmes d'appel vidéo : leur fenêtre va « Dans l'église » (renvoyée dans l'appel par MyVirtualCam, elle s'y verrait à l'infini).</summary>
    static readonly string[] Appels = { "zoom", "teams", "ms-teams", "webex", "ciscocollabhost", "skype", "discord", "whatsapp" };

    readonly SourceDisplay? existante;
    /// <summary>La fenêtre d'une source qu'on modifie, fermée en ce moment : gardée en tête de liste, telle quelle.</summary>
    readonly SourcesDisplay.Fenetre? fermee;
    readonly ComboBox liste = new() { FontSize = 14.5, MinHeight = 40, VerticalContentAlignment = VerticalAlignment.Center };
    readonly Image apercu = new() { Stretch = Stretch.Uniform };
    readonly TextBlock apercuVide;
    readonly CheckBox zoneClient;
    readonly TextBox nom;
    readonly RadioButton eglise, streaming, lesDeux;
    readonly TextBlock conseil, erreur;
    readonly DispatcherTimer minuterie = new() { Interval = TimeSpan.FromMilliseconds(125) };
    string? derniereListe;
    bool captureEnCours, nomTouche, diffusionTouchee, remplissage;

    /// <summary>La source réglée (après « Ajouter la source » ou « Enregistrer »).</summary>
    public SourceDisplay? Source { get; private set; }
    /// <summary>Son nom dans Display.</summary>
    public string Nom { get; private set; } = "";

    /// <param name="existante">La source à modifier ; null pour en ajouter une.</param>
    /// <param name="titre">Son nom dans Display (vide pour une nouvelle source : le titre de la fenêtre choisie).</param>
    public CaptureFenetreWindow(SourceDisplay? existante, string titre)
    {
        this.existante = existante;
        Title = existante == null ? "Capture de fenêtre" : $"Propriétés de « {titre} »";
        Width = 780;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(520, SystemParameters.WorkArea.Height - 40);
        ResizeMode = ResizeMode.NoResize;
        Background = Pinceau("Page");

        var corps = new StackPanel();
        // l'image en direct, en haut, comme dans OBS
        apercuVide = new TextBlock
        {
            FontSize = 14, Foreground = Pinceau("TuileTexte"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(30, 0, 30, 0),
        };
        var vue = new Grid();
        vue.Children.Add(apercuVide);
        vue.Children.Add(apercu);
        corps.Children.Add(new Border { Height = 280, CornerRadius = new CornerRadius(10), Background = Pinceau("TuileFond"), ClipToBounds = true, Padding = new Thickness(6), Child = vue });

        corps.Children.Add(Libelle("Fenêtre"));
        var ligne = new DockPanel();
        var actualiser = new Button { Style = Style_("Btn"), Content = "Actualiser", MinHeight = 40, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(10, 0, 0, 0), ToolTip = "Relire les fenêtres ouvertes" };
        actualiser.Click += (_, _) => Remplir();
        DockPanel.SetDock(actualiser, Dock.Right);
        ligne.Children.Add(actualiser);
        ligne.Children.Add(liste);
        corps.Children.Add(ligne);
        corps.Children.Add(Note("Gardez-la ouverte et non réduite. Fermée puis rouverte, Siloé Stream la retrouve tout seul : même programme et même titre, sinon même type de fenêtre."));
        zoneClient = new CheckBox { Content = "Zone client seulement (sans la barre de titre ni les bords)", IsChecked = existante?.ZoneClient ?? true, FontSize = 14, Margin = new Thickness(0, 14, 0, 0) };
        corps.Children.Add(zoneClient);

        corps.Children.Add(Libelle("Nom dans Display"));
        nom = new TextBox { Style = Style_("Champ"), FontSize = 15, Text = titre };
        corps.Children.Add(nom);

        corps.Children.Add(Libelle("Où va cette fenêtre ?"));
        eglise = Option("Dans l'église", "Seulement sur l'écran de projection. Zoom (MyVirtualCam) voit l'écran principal à la place, OBS rien : pour projeter Zoom dans l'église, sans effet miroir.", existante?.EgliseSeulement == true);
        streaming = Option("Sur MyStream", "Seulement sur MyStream (Zoom, YouTube) et OBS. Sur l'écran de projection, l'écran de repos reste affiché.", existante?.StreamingSeulement == true);
        lesDeux = Option("Les deux", "Sur l'écran de projection et sur MyStream, comme les autres sources.", existante != null && existante.Diffusion == null);
        foreach (var o in new[] { eglise, streaming, lesDeux })
        {
            o.Checked += (_, _) => { if (!remplissage) diffusionTouchee = true; MajConseil(); };
            corps.Children.Add(o);
        }
        conseil = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 19, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 0) };
        corps.Children.Add(conseil);

        // en bas : Annuler, puis « Ajouter la source » (ou « Enregistrer »)
        erreur = new TextBlock { Foreground = Pinceau("Danger"), FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
        var bas = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
        var ok = new Button { Style = Style_("BtnOr"), Content = existante == null ? "Ajouter la source" : "Enregistrer", IsDefault = true, MinWidth = 150, Height = 42, Margin = new Thickness(10, 0, 0, 0) };
        ok.Click += (_, _) => Valider();
        DockPanel.SetDock(ok, Dock.Right);
        bas.Children.Add(ok);
        var annuler = new Button { Style = Style_("Btn"), Content = "Annuler", IsCancel = true, MinWidth = 110, Height = 42 };
        annuler.Click += (_, _) => Close();
        DockPanel.SetDock(annuler, Dock.Right);
        bas.Children.Add(annuler);
        var pied = new StackPanel();
        pied.Children.Add(erreur);
        pied.Children.Add(bas);

        var tete = new StackPanel();
        tete.Children.Add(new TextBlock { Text = existante == null ? "Capture de fenêtre" : "Propriétés de la capture", Style = Style_("Titre2") });
        tete.Children.Add(new TextBlock
        {
            Text = "Choisissez la fenêtre : son image s'affiche ici en direct, comme dans OBS.", Style = Style_("Muet"), FontSize = 14,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12),
        });
        var racine = new DockPanel { Margin = new Thickness(28, 22, 28, 22) };
        DockPanel.SetDock(tete, Dock.Top);
        racine.Children.Add(tete);
        DockPanel.SetDock(pied, Dock.Bottom);
        racine.Children.Add(pied);
        racine.Children.Add(new ScrollViewer { Content = corps, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 0) });
        Content = racine;

        // la fenêtre d'une source qu'on modifie : la même, sinon retrouvée (même programme et titre, sinon même type) ; fermée, elle reste en tête
        if (existante != null)
        {
            var ouvertes = SourcesDisplay.Fenetres();
            bool la = ouvertes.Any(f => f.Handle == existante.Fenetre && f.Processus == existante.Processus)
                || (SourcesDisplay.Retrouver(existante) is { } r && ouvertes.Any(f => f.Handle == r.Handle));
            if (!la) fermee = new SourcesDisplay.Fenetre(existante.Fenetre, existante.Processus, existante.Titre ?? titre, existante.Exe ?? "", existante.Classe ?? "");
        }

        liste.SelectionChanged += (_, _) => Choisie();
        zoneClient.Click += (_, _) => Apercu();
        nom.TextChanged += (_, _) => { if (!remplissage) nomTouche = true; };
        minuterie.Tick += (_, _) => Apercu();
        Loaded += (_, _) => { minuterie.Start(); Apercu(); };
        Closed += (_, _) => minuterie.Stop();
        // de retour dans cette fenêtre (après avoir ouvert Zoom, par exemple) : la liste est relue
        Activated += (_, _) => { if (IsLoaded) Remplir(); };
        Remplir();
    }

    static TextBlock Libelle(string texte) => new Capitales { Libelle = texte, Margin = new Thickness(0, 20, 0, 8) };
    static TextBlock Note(string texte) => new() { Text = texte, FontSize = 12.5, Foreground = Pinceau("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), LineHeight = 18 };

    /// <summary>Un grand choix à cliquer : un titre et une phrase.</summary>
    static RadioButton Option(string titre, string detail, bool coche)
    {
        // le bouton radio d'origine pose son contenu dans une pile horizontale (largeur sans limite) : sans largeur maximale,
        // l'explication ne passe pas à la ligne et se coupe à droite (1.3.1)
        var p = new StackPanel { Margin = new Thickness(4, 0, 0, 0), MaxWidth = 640 };
        p.Children.Add(new TextBlock { Text = titre, FontSize = 15, FontWeight = FontWeights.SemiBold });
        p.Children.Add(new TextBlock { Text = detail, FontSize = 12.5, Foreground = Pinceau("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        return new RadioButton { Content = p, IsChecked = coche, GroupName = "diffusion", Margin = new Thickness(0, 0, 0, 10), VerticalContentAlignment = VerticalAlignment.Top };
    }

    SourcesDisplay.Fenetre? Choix => (liste.SelectedItem as ComboBoxItem)?.Tag as SourcesDisplay.Fenetre;

    /// <summary>Relit les fenêtres ouvertes ; la fenêtre choisie le reste si elle est toujours là.</summary>
    void Remplir()
    {
        var fenetres = SourcesDisplay.Fenetres();
        string signature = string.Join("\n", fenetres.Select(f => f.Handle + "|" + f.Processus + "|" + f.Libelle));
        if (signature == derniereListe) return;
        derniereListe = signature;
        var avant = Choix;
        if (avant == null && existante != null && fermee == null)
            avant = fenetres.FirstOrDefault(f => f.Handle == existante.Fenetre && f.Processus == existante.Processus)
                ?? (SourcesDisplay.Retrouver(existante) is { } r ? fenetres.FirstOrDefault(f => f.Handle == r.Handle) : null);
        remplissage = true;
        try
        {
            liste.Items.Clear();
            if (fermee != null) liste.Items.Add(new ComboBoxItem { Content = "Fermée · " + fermee.Libelle, Tag = fermee });
            foreach (var f in fenetres) liste.Items.Add(new ComboBoxItem { Content = f.Libelle, Tag = f });
            var garder = avant == null ? null : liste.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is SourcesDisplay.Fenetre f && f.Handle == avant.Handle && f.Processus == avant.Processus);
            liste.SelectedItem = garder ?? (liste.Items.Count > 0 ? liste.Items[0] : null);
        }
        finally { remplissage = false; }
        Choisie();
    }

    /// <summary>Une autre fenêtre est choisie : son nom, le choix conseillé pour un appel vidéo, et son image tout de suite.</summary>
    void Choisie()
    {
        if (remplissage) return;
        var f = Choix;
        remplissage = true;
        try
        {
            if (f != null && existante == null && !nomTouche) nom.Text = f.Titre;
            // une fenêtre d'appel vidéo (Zoom, Teams…) : « Dans l'église » d'office, tant qu'on n'a pas choisi soi-même
            if (existante == null && !diffusionTouchee)
            {
                if (f != null && AppelVideo(f)) eglise.IsChecked = true;
                else lesDeux.IsChecked = true;
            }
        }
        finally { remplissage = false; }
        MajConseil();
        Montrer(null, "");
        Apercu();
    }

    /// <summary>Zoom, Teams, Webex… (le programme, ou le titre d'une réunion dans un navigateur).</summary>
    static bool AppelVideo(SourcesDisplay.Fenetre f)
    {
        string exe = Path.GetFileNameWithoutExtension(f.Exe).ToLowerInvariant();
        return Appels.Contains(exe)
            || f.Titre.Contains("Zoom Meeting", StringComparison.OrdinalIgnoreCase) || f.Titre.Contains("Réunion Zoom", StringComparison.OrdinalIgnoreCase)
            || f.Titre.Contains("Microsoft Teams", StringComparison.OrdinalIgnoreCase) || f.Titre.Contains("Google Meet", StringComparison.OrdinalIgnoreCase)
            || f.Titre.StartsWith("Meet - ", StringComparison.OrdinalIgnoreCase);
    }

    static string NomProgramme(SourcesDisplay.Fenetre f) => f.Exe.Length > 0 ? Path.GetFileNameWithoutExtension(f.Exe) : "Ce programme";

    /// <summary>Pour une fenêtre d'appel vidéo : pourquoi « Dans l'église » (en rouge si un autre choix ferait l'effet miroir).</summary>
    void MajConseil()
    {
        if (conseil == null) return;
        var f = Choix;
        bool appel = f != null && AppelVideo(f);
        bool risque = appel && eglise.IsChecked != true;
        conseil.Text = !appel ? ""
            : risque ? $"{NomProgramme(f!)} reçoit sans doute l'image de MyVirtualCam : sur MyStream, il recevrait sa propre fenêtre (effet miroir à l'infini). Choisissez « Dans l'église »."
            : $"{NomProgramme(f!)} reste dans l'église : MyVirtualCam envoie l'écran principal à la place de sa fenêtre, Zoom ne se voit pas lui-même.";
        conseil.Foreground = Pinceau(risque ? "Danger" : "Brume");
        conseil.Visibility = appel ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>L'image de la fenêtre choisie, 8 fois par seconde, capturée à côté (la fenêtre ne bloque pas).</summary>
    async void Apercu()
    {
        if (captureEnCours || !IsVisible) return;
        var f = Choix;
        if (f == null)
        {
            Montrer(null, liste.Items.Count == 0 ? "Aucune fenêtre à capturer : ouvrez-la (Zoom, navigateur…), puis cliquez « Actualiser »." : "Choisissez une fenêtre.");
            return;
        }
        captureEnCours = true;
        bool zone = zoneClient.IsChecked == true;
        try
        {
            var (image, message) = await Task.Run<(BitmapSource? Image, string Message)>(() =>
            {
                var h = new IntPtr(f.Handle);
                if (!SourcesDisplay.Ouverte(h, f.Processus)) return (null, "Cette fenêtre est fermée : rouvrez-la, ou choisissez-en une autre.");
                if (SourcesDisplay.Reduite(h)) return (null, "Cette fenêtre est réduite : restaurez-la pour voir son image.");
                try { return (SourcesDisplay.Capturer(h, zone), ""); }
                catch (IOException ex) { return (null, ex.Message); }
            });
            if (ReferenceEquals(Choix, f)) Montrer(image, message);
        }
        catch (Exception ex) { Montrer(null, "Aperçu impossible : " + ex.Message); }
        finally { captureEnCours = false; }
    }

    void Montrer(BitmapSource? image, string message)
    {
        apercu.Source = image;
        apercuVide.Text = message;
        apercuVide.Visibility = image == null ? Visibility.Visible : Visibility.Collapsed;
    }

    void Valider()
    {
        if (Choix is not { } f)
        {
            erreur.Text = "Choisissez la fenêtre à capturer.";
            erreur.Visibility = Visibility.Visible;
            return;
        }
        Source = new SourceDisplay
        {
            Type = "fenetre", Fenetre = f.Handle, Processus = f.Processus,
            Exe = f.Exe.Length > 0 ? f.Exe : null, Classe = f.Classe.Length > 0 ? f.Classe : null, Titre = f.Titre,
            ZoneClient = zoneClient.IsChecked == true,
            Diffusion = eglise.IsChecked == true ? SourceDisplay.Eglise : streaming.IsChecked == true ? SourceDisplay.Streaming : null,
        };
        Nom = nom.Text.Trim().Length > 0 ? nom.Text.Trim() : f.Titre;
        DialogResult = true;
    }

    // ------------------------------------------------------------ pour le test automatique
    public int TestFenetres => liste.Items.Count;
    public string TestDiffusion => eglise.IsChecked == true ? SourceDisplay.Eglise : streaming.IsChecked == true ? SourceDisplay.Streaming : "les deux";
}
