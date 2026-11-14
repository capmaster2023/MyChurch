using System.Windows;
using System.Windows.Controls;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

/// <summary>
/// « Propriétés pour « Capture audio (sortie) » », comme dans OBS : le périphérique (« Par défaut » ou un périphérique de Windows)
/// et « Utiliser l'horodatage du périphérique ». Pour une capture audio (entrée) d'une carte son à plusieurs entrées : l'entrée écoutée.
/// Pour un média : le fichier local et « Lire en boucle ». Rien ne change avant « OK » ; « Valeurs par défaut » remet ceux d'OBS.
/// </summary>
public sealed class ProprietesSourceWindow : FenetreDialogue
{
    static ChurchAudioService A => App.Audio;
    const string NonConnecte = " (non connecté)";

    readonly AudioSource source;
    readonly bool nouvelle;
    readonly Grid formulaire = new();
    readonly TextBox nom = new();
    readonly ComboBox peripherique = new() { MinHeight = 38 };
    readonly ComboBox entree = new() { MinHeight = 38 };
    readonly CheckBox horodatage = new() { Content = "Utiliser l'horodatage du périphérique", FontSize = 14.5 };
    readonly TextBox fichier = new() { IsReadOnly = true };
    readonly CheckBox boucle = new() { Content = "Lire en boucle", FontSize = 14.5 };
    readonly TextBlock erreur = new() { FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
    readonly List<UIElement> ligneEntree = new();
    // capture ASIO, comme le plugin ASIO d'OBS : Device, Format, OBS Channel 1 / 2, Control Panel
    readonly ComboBox piloteAsio = new() { MinHeight = 38 };
    readonly ComboBox formatAsio = new() { MinHeight = 38 };
    readonly ComboBox[] canalAsio = { new() { MinHeight = 38 }, new() { MinHeight = 38 } };
    readonly List<UIElement> ligneCanal2 = new();
    List<string> nomsEntrees = new();
    bool remplissage;

    /// <summary>L'utilisateur a cliqué « OK » : la source a reçu les nouveaux réglages.</summary>
    public bool Valide { get; private set; }

    /// <param name="nouvelle">Source pas encore dans le mélangeur : son nom se choisit ici, comme dans la fenêtre « Créer une source » d'OBS.</param>
    public ProprietesSourceWindow(AudioSource s, bool nouvelle)
    {
        source = s;
        this.nouvelle = nouvelle;
        Title = $"Propriétés pour « {s.Name} »";
        Width = 660;
        Height = s.Kind == AudioInputKind.Media ? 330 : s.Kind == AudioInputKind.Asio ? 480 : 400;
        MinWidth = 520;
        MinHeight = 280;
        ResizeMode = ResizeMode.CanResize;
        Background = Pinceau("Page");

        formulaire.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        formulaire.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nom.Style = Style_("Champ");
        nom.FontSize = 15;
        nom.Text = s.Name;
        fichier.Style = Style_("Champ");
        fichier.FontSize = 14;
        erreur.Foreground = Pinceau("Danger");

        if (nouvelle) Ligne("Nom", nom);
        if (s.Kind == AudioInputKind.Media)
        {
            if (s.FollowProjection) Ligne("", new TextBlock { Text = "Aucune propriété disponible : cette source joue le son de la vidéo projetée depuis Display.", Style = Style_("Muet"), FontSize = 14, TextWrapping = TextWrapping.Wrap });
            else
            {
                var parcourir = new Button { Style = Style_("Btn"), Content = "Parcourir…", Height = 40, Padding = new Thickness(14, 0, 14, 0), FontSize = 14, Margin = new Thickness(8, 0, 0, 0) };
                parcourir.Click += (_, _) => Parcourir();
                var ligne = new DockPanel();
                DockPanel.SetDock(parcourir, Dock.Right);
                ligne.Children.Add(parcourir);
                ligne.Children.Add(fichier);
                fichier.Text = s.MediaPath;
                Ligne("Fichier local", ligne);
                boucle.IsChecked = s.Loop;
                Ligne("", boucle);
            }
        }
        else if (s.Kind == AudioInputKind.Asio)
        {
            Ligne("Périphérique", piloteAsio);
            piloteAsio.ToolTip = "Le pilote ASIO de la carte son (Focusrite USB ASIO, UMC ASIO Driver…)";
            formatAsio.Items.Add(new ComboBoxItem { Content = "Mono", Tag = 1 });
            formatAsio.Items.Add(new ComboBoxItem { Content = "Stéréo", Tag = 2 });
            Ligne("Format", formatAsio);
            Ligne("Canal OBS 1", canalAsio[0]);
            ligneCanal2.AddRange(Ligne("Canal OBS 2", canalAsio[1]));
            var panneau = new Button { Style = Style_("Btn"), Content = "Panneau de configuration", Height = 40, Padding = new Thickness(16, 0, 16, 0), HorizontalAlignment = HorizontalAlignment.Left };
            panneau.ToolTip = "Le panneau du fabricant : fréquence, taille du tampon (latence)…";
            panneau.Click += (_, _) =>
            {
                try { A.PanneauAsio(PiloteAsioChoisi); }
                catch (Exception x) { Erreur("Le panneau du pilote ne s'ouvre pas : " + x.Message); }
            };
            Ligne("", panneau);
            RemplirPilotes(s.DeviceId);
            formatAsio.SelectedIndex = s.AsioCanaux.Count > 1 ? 1 : 0;
            RemplirCanaux(s.AsioCanaux);
            piloteAsio.SelectionChanged += (_, _) => { if (!remplissage) RemplirCanaux(new List<int> { 0, 1 }.Take(FormatAsio).ToList()); };
            formatAsio.SelectionChanged += (_, _) => { if (!remplissage) MajFormat(); };
            MajFormat();
        }
        else
        {
            Ligne("Périphérique", peripherique);
            if (s.Kind == AudioInputKind.Microphone) ligneEntree.AddRange(Ligne("Entrée", entree));
            entree.ToolTip = "Une carte son à plusieurs entrées (Behringer, Focusrite…) : chaque micro peut être sa propre source";
            horodatage.IsChecked = s.Horodatage;
            Ligne("", horodatage);
            RemplirPeripheriques(s.DeviceId, s.DeviceName);
            RemplirEntrees(s.Channel);
            peripherique.SelectionChanged += (_, _) => { if (!remplissage) RemplirEntrees(0); };
            entree.SelectionChanged += (_, _) => { if (!remplissage) MajHorodatage(); };
            MajHorodatage();
        }

        // en bas, comme OBS : « Valeurs par défaut » à gauche, « OK » et « Annuler » à droite
        var bas = new DockPanel { Margin = new Thickness(24, 0, 24, 20), LastChildFill = false };
        var defauts = new Button { Style = Style_("Btn"), Content = "Valeurs par défaut", Height = 40, Padding = new Thickness(16, 0, 16, 0) };
        defauts.Click += (_, _) => Defauts();
        DockPanel.SetDock(defauts, Dock.Left);
        bas.Children.Add(defauts);
        var annuler = new Button { Style = Style_("Btn"), Content = "Annuler", IsCancel = true, MinWidth = 110, Height = 40, Margin = new Thickness(10, 0, 0, 0) };
        annuler.Click += (_, _) => Close();
        DockPanel.SetDock(annuler, Dock.Right);
        bas.Children.Add(annuler);
        var ok = new Button { Style = Style_("BtnOr"), Content = "OK", IsDefault = true, MinWidth = 110, Height = 40 };
        ok.Click += (_, _) => Valider();
        DockPanel.SetDock(ok, Dock.Right);
        bas.Children.Add(ok);

        var racine = new DockPanel();
        var pied = new StackPanel();
        erreur.Margin = new Thickness(24, 0, 24, 12);
        pied.Children.Add(erreur);
        pied.Children.Add(bas);
        DockPanel.SetDock(pied, Dock.Bottom);
        racine.Children.Add(pied);
        racine.Children.Add(new ScrollViewer { Content = formulaire, Padding = new Thickness(24, 22, 24, 12), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = racine;
        Loaded += (_, _) =>
        {
            if (nouvelle) { nom.Focus(); nom.SelectAll(); }
            else if (s.Kind == AudioInputKind.Asio) piloteAsio.Focus();
            else if (s.Kind != AudioInputKind.Media) peripherique.Focus();
        };
    }

    /// <summary>Une ligne du formulaire : le libellé à droite de sa colonne, comme les propriétés d'OBS, puis le réglage.</summary>
    List<UIElement> Ligne(string libelle, FrameworkElement controle)
    {
        int r = formulaire.RowDefinitions.Count;
        formulaire.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var t = new TextBlock { Text = libelle, FontSize = 14.5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 14, 12) };
        controle.Margin = new Thickness(0, 0, 0, 12);
        Grid.SetRow(t, r);
        Grid.SetRow(controle, r);
        Grid.SetColumn(controle, 1);
        formulaire.Children.Add(t);
        formulaire.Children.Add(controle);
        return new List<UIElement> { t, controle };
    }

    // ------------------------------------------------------------ capture ASIO
    string PiloteAsioChoisi => (piloteAsio.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    int FormatAsio => (formatAsio.SelectedItem as ComboBoxItem)?.Tag is int f ? f : 1;
    List<int> CanauxAsio => canalAsio.Take(FormatAsio).Select(c => (c.SelectedItem as ComboBoxItem)?.Tag is int k ? k : -1).ToList();

    /// <summary>Les pilotes ASIO installés ; un pilote choisi mais absent reste affiché.</summary>
    void RemplirPilotes(string choisi)
    {
        remplissage = true;
        try
        {
            piloteAsio.Items.Clear();
            var liste = ChurchAudioService.PilotesAsio();
            foreach (var p in liste) piloteAsio.Items.Add(new ComboBoxItem { Content = p, Tag = p });
            if (choisi is not ("" or "default") && !liste.Contains(choisi)) piloteAsio.Items.Add(new ComboBoxItem { Content = choisi + NonConnecte, Tag = choisi });
            piloteAsio.SelectedItem = piloteAsio.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag! == choisi) ?? (piloteAsio.Items.Count > 0 ? piloteAsio.Items[0] : null);
            if (piloteAsio.Items.Count == 0) Erreur("Aucun pilote ASIO installé. Installez le pilote de la carte son (Focusrite Control, Behringer UMC…), puis rouvrez cette fenêtre.");
        }
        finally { remplissage = false; }
    }

    /// <summary>« Muet », puis les entrées du pilote (« Input 1 », « Input 2 »…), pour chaque canal OBS.</summary>
    void RemplirCanaux(List<int> choisis)
    {
        remplissage = true;
        try
        {
            nomsEntrees = PiloteAsioChoisi.Length > 0 ? A.EntreesAsio(PiloteAsioChoisi) : new();
            if (PiloteAsioChoisi.Length > 0 && nomsEntrees.Count == 0)
            {
                string st = A.StatutAsio(PiloteAsioChoisi);
                Erreur(st.Length > 0 ? st : "Ce pilote ASIO ne s'ouvre pas (carte son débranchée, ou utilisée par OBS ?).");
            }
            else erreur.Visibility = Visibility.Collapsed;
            for (int c = 0; c < canalAsio.Length; c++)
            {
                var combo = canalAsio[c];
                int voulu = c < choisis.Count ? choisis[c] : Math.Min(c, Math.Max(0, nomsEntrees.Count - 1));
                combo.Items.Clear();
                combo.Items.Add(new ComboBoxItem { Content = "Muet", Tag = -1 });
                for (int k = 0; k < Math.Max(nomsEntrees.Count, voulu + 1); k++)
                    combo.Items.Add(new ComboBoxItem { Content = k < nomsEntrees.Count ? nomsEntrees[k] : $"Input {k + 1}" + NonConnecte, Tag = k });
                combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag! == voulu) ?? combo.Items[0];
            }
        }
        finally { remplissage = false; }
    }

    /// <summary>Mono : un canal OBS ; Stéréo : deux (gauche et droite).</summary>
    void MajFormat()
    {
        foreach (var e in ligneCanal2) e.Visibility = FormatAsio > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (FormatAsio > 1 && (canalAsio[1].SelectedItem as ComboBoxItem)?.Tag is -1 && canalAsio[1].Items.Count > 2) canalAsio[1].SelectedIndex = 2;
    }

    /// <summary>« Par défaut » puis les périphériques de Windows ; un périphérique choisi mais débranché reste affiché.</summary>
    void RemplirPeripheriques(string choisi, string nomChoisi)
    {
        remplissage = true;
        try
        {
            peripherique.Items.Clear();
            peripherique.Items.Add(new ComboBoxItem { Content = "Par défaut", Tag = new AudioDevice("default", "") });
            var liste = source.Kind == AudioInputKind.Sortie ? A.Outputs() : A.Microphones();
            foreach (var d in liste) peripherique.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d });
            if (choisi != "default" && choisi.Length > 0 && liste.All(d => d.Id != choisi))
                peripherique.Items.Add(new ComboBoxItem { Content = (nomChoisi.Length > 0 ? nomChoisi : choisi) + NonConnecte, Tag = new AudioDevice(choisi, nomChoisi) });
            peripherique.SelectedItem = peripherique.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is AudioDevice d && d.Id == choisi) ?? peripherique.Items[0];
        }
        finally { remplissage = false; }
    }

    AudioDevice PeripheriqueChoisi => (peripherique.SelectedItem as ComboBoxItem)?.Tag as AudioDevice ?? new AudioDevice("default", "");
    int EntreeChoisie => source.Kind == AudioInputKind.Microphone && (entree.SelectedItem as ComboBoxItem)?.Tag is int k ? k : 0;

    /// <summary>Les entrées du micro choisi : « Toutes les entrées », puis « Entrée 1 », « Entrée 2 »… (seulement s'il en a plusieurs).</summary>
    void RemplirEntrees(int choisie)
    {
        if (source.Kind != AudioInputKind.Microphone) return;
        remplissage = true;
        try
        {
            int n = ChurchAudioService.Entrees(PeripheriqueChoisi.Id);
            entree.Items.Clear();
            entree.Items.Add(new ComboBoxItem { Content = "Toutes les entrées (stéréo)", Tag = 0 });
            for (int k = 1; k <= Math.Min(Math.Max(n, choisie), AudioConfiguration.MaxEntrees); k++)
                entree.Items.Add(new ComboBoxItem { Content = $"Entrée {k}" + (k > n ? NonConnecte : ""), Tag = k });
            entree.SelectedItem = entree.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag! == choisie) ?? entree.Items[0];
            // une seule entrée (ou un micro stéréo ordinaire) : rien à choisir
            bool montrer = n >= 2 || choisie > 0;
            foreach (var e in ligneEntree) e.Visibility = montrer ? Visibility.Visible : Visibility.Collapsed;
            entree.IsEnabled = A.EntreesSeparees || choisie > 0;
            if (!A.EntreesSeparees) entree.ToolTip = "Le moteur audio OBS doit être démarré pour écouter une entrée seule.";
        }
        finally { remplissage = false; }
        MajHorodatage();
    }

    /// <summary>L'horodatage du périphérique ne concerne que la capture du périphérique entier (une entrée seule est horodatée par MyChurch).</summary>
    void MajHorodatage() => horodatage.Visibility = EntreeChoisie == 0 ? Visibility.Visible : Visibility.Collapsed;

    void Parcourir()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Fichier audio ou vidéo pour le mélangeur",
            Filter = "Audio et vidéo|*.mp3;*.wav;*.flac;*.ogg;*.opus;*.m4a;*.aac;*.wma;*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm|Tous les fichiers|*.*",
        };
        if (File.Exists(fichier.Text)) dialog.InitialDirectory = Path.GetDirectoryName(fichier.Text);
        if (dialog.ShowDialog(this) == true) fichier.Text = dialog.FileName;
    }

    /// <summary>Les valeurs par défaut d'OBS : « Par défaut », toutes les entrées, horodatage selon le type ; un média ne boucle pas.</summary>
    void Defauts()
    {
        if (source.Kind == AudioInputKind.Media) { boucle.IsChecked = false; return; }
        if (source.Kind == AudioInputKind.Asio)
        {
            if (piloteAsio.Items.Count > 0) piloteAsio.SelectedIndex = 0;
            remplissage = true; formatAsio.SelectedIndex = 0; remplissage = false;
            RemplirCanaux(new List<int> { 0 });
            MajFormat();
            return;
        }
        peripherique.SelectedIndex = 0;
        RemplirEntrees(0);
        horodatage.IsChecked = AudioConfiguration.HorodatageParDefaut(source.Kind);
    }

    void Valider()
    {
        string n = nom.Text.Trim();
        if (nouvelle && n.Length == 0) { Erreur("Donnez un nom à la source."); nom.Focus(); return; }
        if (nouvelle && App.Audio.Config.Sources.Any(x => x != source && x.Name == n)) { Erreur($"Une autre source s'appelle déjà « {n} »."); nom.Focus(); return; }
        if (source.Kind == AudioInputKind.Media)
        {
            if (!source.FollowProjection)
            {
                if (fichier.Text.Trim().Length == 0) { Erreur("Choisissez un fichier audio ou vidéo."); return; }
                source.MediaPath = fichier.Text.Trim();
                source.Loop = boucle.IsChecked == true;
            }
        }
        else if (source.Kind == AudioInputKind.Asio)
        {
            if (PiloteAsioChoisi.Length == 0) { Erreur("Choisissez un pilote ASIO."); return; }
            source.DeviceId = PiloteAsioChoisi;
            source.DeviceName = PiloteAsioChoisi;
            source.AsioCanaux = CanauxAsio;
            source.Channel = 0;
            source.UseDeviceTiming = null;
        }
        else
        {
            var d = PeripheriqueChoisi;
            source.DeviceId = d.Id;
            source.DeviceName = d.Id == "default" ? "" : d.Name;
            source.Channel = EntreeChoisie;
            bool h = horodatage.IsChecked == true;
            source.UseDeviceTiming = h == AudioConfiguration.HorodatageParDefaut(source.Kind) ? null : h;
        }
        if (nouvelle) source.Name = n;
        Valide = true;
        Close();
    }

    void Erreur(string message)
    {
        erreur.Text = message;
        erreur.Visibility = Visibility.Visible;
    }
}
