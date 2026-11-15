using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MyVerse.Ui.Audio;

namespace MyVerse.Ui;

/// <summary>
/// Siloé Stream 1.6.4 : « Directs automatiques », dans la fenêtre du direct. La liste (chaque dimanche à 10:00, chaque vendredi à 17:40…),
/// chacun activé ou non, « Modifier », « × » ; « + Programmer un direct automatique » ; « Ouvrir Siloé Stream au démarrage de Windows ».
/// </summary>
public sealed partial class DirectWindow
{
    readonly StackPanel listeAuto = new();
    readonly TextBlock noteAuto = new();
    CheckBox? demarrageWindows;

    StackPanel ConstruireAutomatiques()
    {
        var bloc = new StackPanel();
        bloc.Children.Add(Libelle("Directs automatiques"));
        bloc.Children.Add(Note("Chaque semaine, à l'heure dite, le direct démarre tout seul avec ses réglages, et s'arrête après la durée choisie. "
            + "Siloé Stream doit être ouvert sur cet ordinateur : il le garde éveillé avant le direct, et le réveille s'il est en veille.", 0));
        noteAuto.FontSize = 12.5; noteAuto.TextWrapping = TextWrapping.Wrap; noteAuto.Foreground = Pinceau("OrVif"); noteAuto.Margin = new Thickness(0, 6, 0, 0);
        bloc.Children.Add(noteAuto);
        listeAuto.Margin = new Thickness(0, 10, 0, 0);
        bloc.Children.Add(listeAuto);
        var ajouter = new Button { Style = Style_("BtnPetitOr"), Content = "+ Programmer un direct automatique", Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) };
        ajouter.Click += (_, _) => ModifierAutomatique(null);
        bloc.Children.Add(ajouter);
        demarrageWindows = new CheckBox
        {
            Content = "Ouvrir Siloé Stream au démarrage de Windows (pour ne pas manquer un direct automatique)", IsChecked = App.AutoTest == null && Natif.DemarrageWindows,
            FontSize = 14, Margin = new Thickness(0, 12, 0, 0),
        };
        demarrageWindows.Click += (_, _) =>
        {
            if (App.AutoTest != null) return;
            if (!Natif.ChangerDemarrageWindows(demarrageWindows.IsChecked == true)) Dire("Windows n'a pas accepté ce changement (voir Réglages › Journal).", true);
            demarrageWindows.IsChecked = Natif.DemarrageWindows;
        };
        bloc.Children.Add(demarrageWindows);
        App.Planificateur.Change += RemplirAutomatiques;
        Closed += (_, _) => App.Planificateur.Change -= RemplirAutomatiques;
        RemplirAutomatiques();
        return bloc;
    }

    /// <summary>La liste des directs automatiques : quand, quel titre, la qualité, la fin, le prochain.</summary>
    void RemplirAutomatiques()
    {
        listeAuto.Children.Clear();
        var maintenant = DateTime.Now;
        noteAuto.Text = R.Programmes.Count == 0 ? "" : R.Mode switch
        {
            ReglagesDirect.Cle => "Avec la clé de stream, Siloé Stream démarre seulement l'envoi : le titre et les réglages se font dans YouTube Studio.",
            ReglagesDirect.Autre => "Avec « Autre service », Siloé Stream démarre seulement l'envoi vers l'adresse choisie (le titre et les réglages ne servent pas).",
            _ => D.Compte.Connecte ? "" : "Connectez le compte YouTube : sans lui, le direct automatique ne peut pas démarrer.",
        };
        noteAuto.Visibility = noteAuto.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (R.Programmes.Count == 0)
        {
            listeAuto.Children.Add(Note("Aucun direct automatique pour l'instant (par exemple : chaque dimanche à 10:00).", 0));
            return;
        }
        foreach (var p in R.Programmes.ToList())
        {
            bool enCours = App.Planificateur.EnCours == p;
            var carte = new Border
            {
                Background = Pinceau(enCours ? "RougeLeger" : "Nuit2"), BorderBrush = Pinceau(enCours ? "Direct" : "Nuit3"), BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8),
            };
            var dock = new DockPanel();
            var boutons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            var modifier = new Button { Style = Style_("BtnPetit"), Content = "Modifier", Padding = new Thickness(12, 4, 12, 4) };
            modifier.Click += (_, _) => ModifierAutomatique(p);
            var supprimer = new Button { Style = Style_("BtnDanger"), Content = "×", Padding = new Thickness(11, 4, 11, 4), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Supprimer ce direct automatique" };
            supprimer.Click += (_, _) =>
            {
                if (!Dialogue.Confirmer(this, $"Supprimer le direct automatique « {p.Quand} » ?", "Supprimer")) return;
                R.Programmes.Remove(p);
                App.Donnees.SauverReglages();
                App.Planificateur.Modifie();
                Dire("Direct automatique supprimé.");
            };
            boutons.Children.Add(modifier);
            boutons.Children.Add(supprimer);
            DockPanel.SetDock(boutons, Dock.Right);
            dock.Children.Add(boutons);
            var textes = new StackPanel();
            var actif = new CheckBox
            {
                IsChecked = p.Actif, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Activé : il démarre tout seul",
                Content = new TextBlock { Text = p.Quand, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
            };
            actif.Click += (_, _) =>
            {
                p.Actif = actif.IsChecked == true;
                // réactivé après l'heure du jour : il ne repart pas en retard pour un début déjà passé
                if (p.Actif && p.Precedent(DateTime.Now) is { } t && (p.Dernier == null || p.Dernier < t)) p.Dernier = t;
                App.Donnees.SauverReglages();
                App.Planificateur.Modifie();
            };
            textes.Children.Add(actif);
            var details = new List<string>();
            if (R.Mode == ReglagesDirect.Compte)
            {
                details.Add("« " + p.Modele.Titre + " »");
                details.Add(p.Modele.Confidentialite switch { "unlisted" => "non répertorié", "private" => "privé", _ => "public" });
            }
            details.Add(p.Qualite switch { "2160p" => "4K", "1080p" => "1080p", "720p" => "720p", _ => "qualité du direct (" + (R.Qualite == "2160p" ? "4K" : R.Qualite) + ")" });
            details.Add(p.Fin);
            if (p.Source.Length > 0) details.Add("MyStream : " + (App.Donnees.Annonces.FirstOrDefault(a => a.Id == p.Source)?.Titre ?? "source supprimée"));
            textes.Children.Add(new TextBlock { Text = string.Join(" · ", details), FontSize = 12.5, Foreground = Pinceau("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(28, 3, 0, 0) });
            string prochain = enCours ? "En direct maintenant" + (App.Planificateur.ArretPrevu is { } f ? $" · arrêt à {f:HH:mm}" : "")
                : !p.Actif ? "Désactivé"
                : p.Prochain(maintenant) is { } q ? "Prochain : " + DirectProgramme.Moment(q, maintenant)
                : "Aucun jour choisi";
            textes.Children.Add(new TextBlock
            {
                Text = prochain, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(28, 2, 0, 0),
                Foreground = Pinceau(enCours ? "Direct" : p.Actif && p.Jours.Count > 0 ? "Vert" : "Brume"),
            });
            dock.Children.Add(textes);
            carte.Child = dock;
            listeAuto.Children.Add(carte);
        }
    }

    /// <summary>« + Programmer un direct automatique » (null) ou « Modifier ».</summary>
    void ModifierAutomatique(DirectProgramme? p)
    {
        var w = new ProgrammeDirectWindow(p) { Owner = this };
        if (w.ShowDialog() != true) return;
        bool premier = R.Programmes.Count == 0;
        var r = w.Resultat;
        if (p == null) R.Programmes.Add(r);
        // un début déjà passé aujourd'hui (programmé à 10:00, enregistré à 10:05) ne démarre pas en retard : le prochain compte
        if (r.Precedent(DateTime.Now) is { } t && DateTime.Now - t <= DirectProgramme.Marge && (r.Dernier == null || r.Dernier < t)) r.Dernier = t;
        App.Donnees.SauverReglages();
        App.Planificateur.Modifie();
        string message = r.Actif && r.Prochain(DateTime.Now) is { } q ? $"Direct automatique enregistré : {r.Quand.ToLower(CultureInfo.GetCultureInfo("fr-CA"))}. Prochain : {DirectProgramme.Moment(q, DateTime.Now)}." : "Direct automatique enregistré.";
        // le premier direct automatique : Siloé Stream s'ouvrira au démarrage de Windows (décochable)
        if (premier && App.AutoTest == null && !Natif.DemarrageWindows && Natif.ChangerDemarrageWindows(true))
        {
            if (demarrageWindows != null) demarrageWindows.IsChecked = true;
            message += " Siloé Stream s'ouvrira aussi au démarrage de Windows (décochez plus bas si vous ne voulez pas).";
        }
        Dire(message);
    }

    // ------------------------------------------------------------ pour le test automatique
    public int TestAutomatiques => listeAuto.Children.OfType<Border>().Count();
    public string TestTexteAutomatiques => string.Join(" | ", listeAuto.Children.OfType<Border>().Select(b => string.Join(" / ",
        ((DockPanel)b.Child).Children.OfType<StackPanel>().Last().Children.OfType<FrameworkElement>()
            .Select(x => x is CheckBox { Content: TextBlock tb } ? tb.Text : x is TextBlock t ? t.Text : ""))));
}

/// <summary>
/// Siloé Stream 1.6.4 : un direct automatique : les jours, l'heure, quand l'arrêter, ce que montre MyStream, la qualité, et les réglages
/// du direct YouTube (titre avec « {date} », description, confidentialité, catégorie, « pour les enfants ? », latence, DVR, miniature).
/// </summary>
public sealed class ProgrammeDirectWindow : FenetreDialogue
{
    /// <summary>Le direct automatique (le même objet en modification, un nouveau sinon), rempli quand « Enregistrer » réussit.</summary>
    public DirectProgramme Resultat { get; }
    readonly CheckBox[] jours = new CheckBox[7];
    readonly TextBox heure, titre, description;
    readonly ComboBox fin = new(), source = new(), qualite = new(), confidentialite = new(), categorie = new(), latence = new();
    readonly RadioButton enfantsNon, enfantsOui;
    readonly CheckBox dvr, actif;
    readonly TextBlock miniatureNom = new(), message = new();
    string miniature;

    public ProgrammeDirectWindow(DirectProgramme? p)
    {
        Resultat = p ?? DirectProgramme.Nouveau(App.Donnees.Reglages.Direct.Modele);
        var m = Resultat.Modele;
        Title = p == null ? "Programmer un direct automatique" : "Modifier le direct automatique";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(520, SystemParameters.WorkArea.Height - 40);
        ResizeMode = ResizeMode.NoResize;
        Background = Pinceau("Page");
        var racine = new DockPanel { Margin = new Thickness(26, 20, 26, 20) };
        var tete = new StackPanel();
        tete.Children.Add(new TextBlock { Text = Title, Style = Style_("Titre2") });
        tete.Children.Add(new TextBlock
        {
            Text = "Chaque semaine, les jours choisis à l'heure dite, Siloé Stream démarre ce direct tout seul, avec ces réglages.",
            Style = Style_("Muet"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 4),
        });
        DockPanel.SetDock(tete, Dock.Top);
        racine.Children.Add(tete);

        // ------------------------------------------------------------ bas
        var pied = new StackPanel();
        message.FontSize = 13.5; message.TextWrapping = TextWrapping.Wrap; message.Foreground = Pinceau("Danger"); message.Margin = new Thickness(0, 10, 0, 0);
        pied.Children.Add(message);
        var bas = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        // IsCancel : la fenêtre se ferme toute seule (ShowDialog rend faux)
        var annuler = new Button { Style = Style_("Btn"), Content = "Annuler", IsCancel = true, MinWidth = 110, Height = 42 };
        DockPanel.SetDock(annuler, Dock.Left);
        bas.Children.Add(annuler);
        var enregistrer = new Button { Style = Style_("BtnOr"), Content = "Enregistrer", IsDefault = true, MinWidth = 150, Height = 42 };
        enregistrer.Click += (_, _) => Enregistrer();
        DockPanel.SetDock(enregistrer, Dock.Right);
        bas.Children.Add(enregistrer);
        pied.Children.Add(bas);
        DockPanel.SetDock(pied, Dock.Bottom);
        racine.Children.Add(pied);

        var corps = new StackPanel();
        // ------------------------------------------------------------ quand
        corps.Children.Add(Petit("Les jours", 10));
        var ligneJours = new WrapPanel();
        foreach (var j in new[] { 0, 1, 2, 3, 4, 5, 6 })
        {
            var nom = DirectProgramme.NomJour(j);
            jours[j] = new CheckBox { Content = char.ToUpper(nom[0]) + nom[1..], IsChecked = Resultat.Jours.Contains(j), FontSize = 14, Margin = new Thickness(0, 0, 16, 6) };
            ligneJours.Children.Add(jours[j]);
        }
        corps.Children.Add(ligneJours);
        var deux = Deux(out var gauche, out var droite);
        gauche.Children.Add(Petit("L'heure (par exemple 10:00 ou 17h40)"));
        heure = new TextBox { Style = Style_("Champ"), FontSize = 15, Text = Resultat.Heure, MaxLength = 8 };
        gauche.Children.Add(heure);
        droite.Children.Add(Petit("Arrêter le direct"));
        var durees = new List<int> { 0, 60, 90, 120, 150, 180, 240, 300 };
        if (!durees.Contains(Resultat.Duree)) { durees.Add(Resultat.Duree); durees.Sort(); }
        foreach (var d in durees) fin.Items.Add(new ComboBoxItem { Content = d == 0 ? "À la main (« Arrêter le direct »)" : "Tout seul après " + DirectProgramme.DureeTexte(d), Tag = d, IsSelected = Resultat.Duree == d });
        fin.MinHeight = 40;
        droite.Children.Add(fin);
        corps.Children.Add(deux);

        // ------------------------------------------------------------ l'image
        var deux2 = Deux(out var gauche2, out var droite2);
        gauche2.Children.Add(Petit("Ce que montre MyStream au début"));
        source.Items.Add(new ComboBoxItem { Content = "La source principale du moment", Tag = "", IsSelected = Resultat.Source.Length == 0 });
        foreach (var a in App.Regie.SourcesStreamPossibles) source.Items.Add(new ComboBoxItem { Content = a.Titre, Tag = a.Id, IsSelected = Resultat.Source == a.Id });
        if (Resultat.Source.Length > 0 && source.SelectedIndex < 0) source.SelectedIndex = 0;
        source.MinHeight = 40;
        gauche2.Children.Add(source);
        droite2.Children.Add(Petit("Qualité"));
        foreach (var (v, nom) in new[] { ("", "Celle du direct (Image et son)"), ("720p", "720p, 4,5 Mb/s"), ("1080p", "1080p, 6 Mb/s"), ("2160p", "4K, 30 Mb/s") })
            qualite.Items.Add(new ComboBoxItem { Content = nom, Tag = v, IsSelected = Resultat.Qualite == v });
        qualite.MinHeight = 40;
        droite2.Children.Add(qualite);
        corps.Children.Add(deux2);

        // ------------------------------------------------------------ sur YouTube
        corps.Children.Add(new Capitales { Libelle = "Sur YouTube", Margin = new Thickness(0, 20, 0, 4) });
        corps.Children.Add(new TextBlock
        {
            Text = "Avec le compte YouTube connecté. « {date} » devient la date du direct : « Culte du {date} » → « Culte du dimanche 22 novembre 2026 ».",
            FontSize = 12.5, Foreground = Pinceau("Brume"), TextWrapping = TextWrapping.Wrap, LineHeight = 18,
        });
        corps.Children.Add(Petit("Titre *"));
        titre = new TextBox { Style = Style_("Champ"), FontSize = 15, MaxLength = 100, Text = m.Titre };
        corps.Children.Add(titre);
        corps.Children.Add(Petit("Description"));
        description = new TextBox { Style = Style_("Zone"), FontSize = 14, Height = 80, Text = m.Description, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 5000 };
        corps.Children.Add(description);
        var deux3 = Deux(out var gauche3, out var droite3);
        gauche3.Children.Add(Petit("Confidentialité"));
        foreach (var (v, nom) in new[] { ("public", "Public"), ("unlisted", "Non répertorié (avec le lien)"), ("private", "Privé") })
            confidentialite.Items.Add(new ComboBoxItem { Content = nom, Tag = v, IsSelected = m.Confidentialite == v });
        confidentialite.MinHeight = 40;
        gauche3.Children.Add(confidentialite);
        droite3.Children.Add(Petit("Catégorie"));
        foreach (var (v, nom) in ModeleDirect.Categories) categorie.Items.Add(new ComboBoxItem { Content = nom, Tag = v, IsSelected = m.Categorie == v });
        if (categorie.SelectedIndex < 0) categorie.SelectedIndex = 0;
        categorie.MinHeight = 40;
        droite3.Children.Add(categorie);
        corps.Children.Add(deux3);
        corps.Children.Add(Petit("Cette vidéo est-elle conçue pour les enfants ? *"));
        var enfants = new StackPanel { Orientation = Orientation.Horizontal };
        enfantsNon = new RadioButton { Content = "Non, elle n'est pas conçue pour les enfants", GroupName = "enfantsAuto", IsChecked = m.Enfants != true, FontSize = 14, Margin = new Thickness(0, 0, 18, 0) };
        enfantsOui = new RadioButton { Content = "Oui", GroupName = "enfantsAuto", IsChecked = m.Enfants == true, FontSize = 14 };
        enfants.Children.Add(enfantsNon);
        enfants.Children.Add(enfantsOui);
        corps.Children.Add(enfants);
        var deux4 = Deux(out var gauche4, out var droite4);
        gauche4.Children.Add(Petit("Latence"));
        foreach (var (v, nom) in new[] { ("normal", "Normale (meilleure qualité)"), ("low", "Faible"), ("ultraLow", "Très faible") })
            latence.Items.Add(new ComboBoxItem { Content = nom, Tag = v, IsSelected = m.Latence == v });
        latence.MinHeight = 40;
        gauche4.Children.Add(latence);
        droite4.Children.Add(Petit("Miniature"));
        var ligneMini = new StackPanel { Orientation = Orientation.Horizontal };
        var choisirMini = new Button { Style = Style_("BtnPetit"), Content = "Choisir…", Padding = new Thickness(12, 4, 12, 4) };
        choisirMini.Click += (_, _) => ChoisirMiniature();
        var retirerMini = new Button { Style = Style_("BtnPetit"), Content = "×", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Sans miniature (celle de YouTube)" };
        retirerMini.Click += (_, _) => { miniature = ""; MajMiniature(); };
        miniatureNom.FontSize = 12.5; miniatureNom.Foreground = Pinceau("Brume"); miniatureNom.VerticalAlignment = VerticalAlignment.Center;
        miniatureNom.Margin = new Thickness(8, 0, 0, 0); miniatureNom.TextTrimming = TextTrimming.CharacterEllipsis; miniatureNom.MaxWidth = 130;
        ligneMini.Children.Add(choisirMini);
        ligneMini.Children.Add(retirerMini);
        ligneMini.Children.Add(miniatureNom);
        droite4.Children.Add(ligneMini);
        corps.Children.Add(deux4);
        miniature = File.Exists(m.Miniature) ? m.Miniature : "";
        MajMiniature();
        dvr = new CheckBox { Content = "DVR : revenir en arrière pendant le direct", IsChecked = m.Dvr, Margin = new Thickness(0, 12, 0, 0), FontSize = 14 };
        corps.Children.Add(dvr);
        actif = new CheckBox { Content = "Activé : ce direct démarre tout seul", IsChecked = Resultat.Actif, Margin = new Thickness(0, 16, 0, 0), FontSize = 14.5, FontWeight = FontWeights.SemiBold };
        corps.Children.Add(actif);

        racine.Children.Add(new ScrollViewer { Content = corps, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 0) });
        Content = racine;
    }

    static Grid Deux(out StackPanel gauche, out StackPanel droite)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        gauche = new StackPanel();
        droite = new StackPanel();
        Grid.SetColumn(droite, 2);
        g.Children.Add(gauche);
        g.Children.Add(droite);
        return g;
    }

    static TextBlock Petit(string texte, double haut = 12) => new() { Text = texte, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = MainWindow.Pinceau("Encre2"), Margin = new Thickness(0, haut, 0, 5), TextWrapping = TextWrapping.Wrap };

    void MajMiniature() => miniatureNom.Text = miniature.Length > 0 ? Path.GetFileName(miniature) : "aucune";

    void ChoisirMiniature()
    {
        var d = new OpenFileDialog { Title = "Miniature du direct (JPEG ou PNG, 2 Mo au plus, 1280 × 720 conseillé)", Filter = "Images|*.jpg;*.jpeg;*.png" };
        if (d.ShowDialog(this) != true) return;
        if (new FileInfo(d.FileName).Length > 2 * 1024 * 1024) { message.Text = "Cette image fait plus de 2 Mo : YouTube la refuserait. Choisissez-en une plus légère."; return; }
        miniature = d.FileName;
        MajMiniature();
    }

    /// <summary>Vérifie et garde : au moins un jour, une heure, un titre (sans &lt; ni &gt;), la réponse « pour les enfants ».</summary>
    void Enregistrer()
    {
        var js = Enumerable.Range(0, 7).Where(j => jours[j].IsChecked == true).ToList();
        if (js.Count == 0) { message.Text = "Choisissez au moins un jour."; return; }
        if (DirectProgramme.LireHeure(heure.Text) is not { } h) { message.Text = "L'heure n'est pas lisible : écrivez par exemple 10:00 ou 17h40."; heure.Focus(); return; }
        var t = titre.Text.Trim();
        if (t.Length == 0) { message.Text = "Donnez un titre au direct (par exemple « Culte du {date} »)."; titre.Focus(); return; }
        if (t.Contains('<') || t.Contains('>')) { message.Text = "Le titre ne peut pas contenir < ni >."; titre.Focus(); return; }
        if (enfantsNon.IsChecked != true && enfantsOui.IsChecked != true) { message.Text = "Répondez à « conçue pour les enfants ? » : YouTube l'exige."; return; }
        var p = Resultat;
        p.Jours = js;
        p.Heure = $"{h.Hours:00}:{h.Minutes:00}";
        p.Duree = (int)((ComboBoxItem)fin.SelectedItem).Tag;
        p.Source = (string)((ComboBoxItem)source.SelectedItem).Tag;
        p.Qualite = (string)((ComboBoxItem)qualite.SelectedItem).Tag;
        p.Actif = actif.IsChecked == true;
        p.Modele = new ModeleDirect
        {
            Titre = t, Description = description.Text, Confidentialite = (string)((ComboBoxItem)confidentialite.SelectedItem).Tag,
            Categorie = (string)((ComboBoxItem)categorie.SelectedItem).Tag, Enfants = enfantsOui.IsChecked == true,
            Latence = (string)((ComboBoxItem)latence.SelectedItem).Tag, Dvr = dvr.IsChecked == true, Miniature = miniature,
        };
        p.Normaliser();
        DialogResult = true;
    }
}
