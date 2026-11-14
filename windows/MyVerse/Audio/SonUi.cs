using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MyVerse.Audio;
using Shapes = System.Windows.Shapes;

namespace MyVerse.Ui.Audio;

/// <summary>Petits outils de dessin de la page Son : pinceaux, styles, icônes (dessinées en traits, sans symboles Unicode).</summary>
internal static class SonUi
{
    public static ChurchAudioService A => App.Audio;
    public static AudioConfiguration C => App.Audio.Config;
    public static Brush P(string cle) => MainWindow.Pinceau(cle);
    public static Style S(string cle) => MainWindow.Style_(cle);

    // ------------------------------------------------------------ icônes (24 × 24, en traits)
    public static readonly Geometry Micro = G("M12,3 A3,3 0 0 1 15,6 V11 A3,3 0 0 1 9,11 V6 A3,3 0 0 1 12,3 Z M5.5,10.5 A6.5,6.5 0 0 0 18.5,10.5 M12,17 V21 M8.5,21 H15.5");
    public static readonly Geometry MicroCoupe = G("M12,3 A3,3 0 0 1 15,6 V11 A3,3 0 0 1 9,11 V6 A3,3 0 0 1 12,3 Z M5.5,10.5 A6.5,6.5 0 0 0 18.5,10.5 M12,17 V21 M8.5,21 H15.5 M4,3.5 L20,20.5");
    public static readonly Geometry Instrument = G("M3.5,6 H20.5 V18 H3.5 Z M8,6 V13 M12,6 V13 M16,6 V13 M3.5,13 H20.5");
    public static readonly Geometry Table = G("M6,4 V20 M12,4 V20 M18,4 V20 M4,15 H8 M10,8.5 H14 M16,13 H20");
    public static readonly Geometry Ordinateur = G("M4.5,4 H19.5 A2,2 0 0 1 21.5,6 V14.5 A2,2 0 0 1 19.5,16.5 H4.5 A2,2 0 0 1 2.5,14.5 V6 A2,2 0 0 1 4.5,4 Z M8,20.5 H16 M12,16.5 V20.5");
    public static readonly Geometry Video = G("M4,6 H14.5 A2,2 0 0 1 16.5,8 V16 A2,2 0 0 1 14.5,18 H4 A2,2 0 0 1 2,16 V8 A2,2 0 0 1 4,6 Z M16.5,10.5 L21.5,7.5 V16.5 L16.5,13.5");
    public static readonly Geometry Musique = G("M9,18 V5 L20,3 V16 M3,18 A3,3 0 1 0 9,18 A3,3 0 1 0 3,18 M14,16 A3,3 0 1 0 20,16 A3,3 0 1 0 14,16");
    public static readonly Geometry HautParleur = G("M4,9.5 H7.5 L12,5.5 V18.5 L7.5,14.5 H4 Z M15.5,9 A4,4 0 0 1 15.5,15 M18,6.5 A7.5,7.5 0 0 1 18,17.5");
    public static readonly Geometry HautParleurCoupe = G("M4,9.5 H7.5 L12,5.5 V18.5 L7.5,14.5 H4 Z M15.5,9.5 L20.5,14.5 M20.5,9.5 L15.5,14.5");
    public static readonly Geometry Eglise = G("M12,2.5 V6.5 M10,4.5 H14 M5,21 V12.5 L12,7.5 L19,12.5 V21 Z M10,21 V17 A2,2 0 0 1 14,17 V21 M3,21 H21");
    public static readonly Geometry Enregistrer = G("M12,3.5 A8.5,8.5 0 1 1 11.99,3.5 Z");
    public static readonly Geometry EnLigne = G("M10.5,12 A1.5,1.5 0 1 0 13.5,12 A1.5,1.5 0 1 0 10.5,12 Z M8.5,8.5 A5,5 0 0 0 8.5,15.5 M15.5,8.5 A5,5 0 0 1 15.5,15.5 M5.5,5.5 A9.2,9.2 0 0 0 5.5,18.5 M18.5,5.5 A9.2,9.2 0 0 1 18.5,18.5");
    public static readonly Geometry Casque = G("M4,15 V12 A8,8 0 0 1 20,12 V15 M4,14 H7.5 V20 H5.5 A1.5,1.5 0 0 1 4,18.5 Z M20,14 H16.5 V20 H18.5 A1.5,1.5 0 0 0 20,18.5 Z");
    public static readonly Geometry Lire = G("M8,5 L19,12 L8,19 Z");
    public static readonly Geometry PauseG = G("M7,5 H10.5 V19 H7 Z M13.5,5 H17 V19 H13.5 Z");
    public static readonly Geometry Stop = G("M6.5,6.5 H17.5 V17.5 H6.5 Z");
    public static readonly Geometry Plus = G("M12,5 V19 M5,12 H19");
    public static readonly Geometry Dossier = G("M3,7 A2,2 0 0 1 5,5 H9.5 L11.5,7.5 H19 A2,2 0 0 1 21,9.5 V17 A2,2 0 0 1 19,19 H5 A2,2 0 0 1 3,17 Z");
    public static readonly Geometry Fleche = G("M9,6 L15,12 L9,18");

    static Geometry G(string d) { var g = Geometry.Parse(d); g.Freeze(); return g; }

    public static Geometry IconeGenre(GenreSon g) => g switch
    {
        GenreSon.Voix => Micro,
        GenreSon.Instrument => Instrument,
        GenreSon.Table => Table,
        GenreSon.Ordinateur => Ordinateur,
        GenreSon.Video => Video,
        _ => Musique,
    };

    /// <summary>La couleur d'un genre (clé de la palette) : celle des types du déroulé, pour reconnaître un son d'un coup d'œil.</summary>
    public static string CouleurGenre(GenreSon g) => g switch
    {
        GenreSon.Voix => "TypeChant",
        GenreSon.Instrument => "TypeDuo",
        GenreSon.Table => "TypeLecture",
        GenreSon.Ordinateur => "TypeAccueil",
        GenreSon.Video => "TypeDisplay",
        _ => "TypePrincipal",
    };

    /// <summary>Une icône en traits.</summary>
    public static Shapes.Path Icone(Geometry g, Brush couleur, double taille = 20, double epaisseur = 1.8, bool pleine = false) => new()
    {
        Data = g, Stroke = pleine ? null : couleur, Fill = pleine ? couleur : null, StrokeThickness = epaisseur,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        Width = taille, Height = taille, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>L'icône d'un son sur sa pastille arrondie.</summary>
    public static Border Pastille(GenreSon g, double taille = 40)
    {
        var p = new Border
        {
            Width = taille, Height = taille, CornerRadius = new CornerRadius(taille * 0.3), VerticalAlignment = VerticalAlignment.Center,
            Child = Icone(IconeGenre(g), P(CouleurGenre(g)), taille * 0.5),
        };
        p.SetResourceReference(Border.BackgroundProperty, "Nuit2");
        return p;
    }

    /// <summary>Une icône sur une pastille de couleur (destinations).</summary>
    public static Border Pastille(Geometry g, string couleur, double taille = 40, bool pleine = false)
    {
        var p = new Border
        {
            Width = taille, Height = taille, CornerRadius = new CornerRadius(taille * 0.3), VerticalAlignment = VerticalAlignment.Center,
            Child = Icone(g, P(couleur), taille * 0.5, 1.8, pleine),
        };
        p.SetResourceReference(Border.BackgroundProperty, "Nuit2");
        return p;
    }

    public static TextBlock Texte(string texte, double taille = 14, string couleur = "Blanc", FontWeight? poids = null, bool retour = true) => new()
    {
        Text = texte, FontSize = taille, Foreground = P(couleur), FontWeight = poids ?? FontWeights.Normal,
        TextWrapping = retour ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = retour ? TextTrimming.None : TextTrimming.CharacterEllipsis,
    };

    /// <summary>Un bouton avec une icône et un texte.</summary>
    public static Button Bouton(string texte, Geometry? icone, string style = "Btn", double hauteur = 40, string? couleurIcone = null, bool pleine = false)
    {
        var contenu = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (icone != null)
        {
            var i = Icone(icone, P(couleurIcone ?? "Blanc"), 16, 2, pleine);
            i.Margin = new Thickness(0, 0, texte.Length > 0 ? 8 : 0, 0);
            if (couleurIcone == null) i.SetBinding(Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
            contenu.Children.Add(i);
        }
        if (texte.Length > 0) contenu.Children.Add(new TextBlock { Text = texte, VerticalAlignment = VerticalAlignment.Center });
        return new Button { Style = S(style), Content = contenu, Height = hauteur, Padding = new Thickness(14, 0, 14, 0) };
    }

    /// <summary>Un bouton « allumé » : fond plein (rouge pour Coupé), texte clair.</summary>
    public static void Allumer(Button b, bool actif, string fond = "Direct")
    {
        if (actif)
        {
            b.Background = P(fond);
            b.BorderBrush = P(fond);
            b.Foreground = Brushes.White;
        }
        else
        {
            b.ClearValue(Control.BackgroundProperty);
            b.ClearValue(Control.BorderBrushProperty);
            b.ClearValue(Control.ForegroundProperty);
        }
    }

    /// <summary>Un interrupteur avec son titre et une phrase d'explication.</summary>
    public static (DockPanel Ligne, CheckBox Interrupteur) Interrupteur(string titre, string aide, bool coche)
    {
        var cb = new CheckBox { Style = S("Interrupteur"), IsChecked = coche, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        var d = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(cb, Dock.Right);
        d.Children.Add(cb);
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(Texte(titre, 15, "Blanc", FontWeights.SemiBold));
        var a = Texte(aide, 12.5, "Brume");
        a.Margin = new Thickness(0, 2, 0, 0);
        t.Children.Add(a);
        d.Children.Add(t);
        // un clic sur le texte bascule aussi l'interrupteur
        t.Cursor = System.Windows.Input.Cursors.Hand;
        t.MouseLeftButtonUp += (_, _) => { if (cb.IsEnabled) { cb.IsChecked = cb.IsChecked != true; cb.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); } };
        return (d, cb);
    }

    /// <summary>Une liste d'appareils de sortie : « Sortie de Windows (par défaut) », puis ceux trouvés ; le choix enregistré reste visible même débranché.</summary>
    public static ComboBox ListeSorties(string choisi, string nomChoisi, Action<string, string> changer, string defaut = "Sortie de Windows (par défaut)")
    {
        var cb = new ComboBox { MinHeight = 40 };
        cb.Items.Add(new ComboBoxItem { Content = defaut, Tag = "default" });
        var liste = A.Outputs();
        foreach (var d in liste) cb.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d.Id });
        if (choisi != "default" && choisi.Length > 0 && liste.All(d => d.Id != choisi))
            cb.Items.Add(new ComboBoxItem { Content = (nomChoisi.Length > 0 ? nomChoisi : "Appareil choisi") + " (débranché)", Tag = choisi });
        cb.SelectedItem = cb.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == choisi) ?? cb.Items[0];
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem is not ComboBoxItem { Tag: string id } item) return;
            changer(id, id == "default" ? "" : (item.Content as string ?? "").Replace(" (débranché)", ""));
        };
        return cb;
    }

    public static void Ouvrir(string cible)
    {
        try { Process.Start(new ProcessStartInfo(cible) { UseShellExecute = true }); }
        catch (Exception e) { Journal.Erreur("ouverture de " + cible, e); }
    }

    public static void MontrerFichier(string fichier)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fichier}\"") { UseShellExecute = true }); }
        catch (Exception e) { Journal.Erreur("ouverture de " + fichier, e); }
    }

    /// <summary>« 0,4 Mo », « 54 Mo », « 1,2 Go ».</summary>
    public static string Taille(long octets)
    {
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-CA");
        double mo = Math.Max(0, octets / (double)(1 << 20));
        return octets >= 1L << 30 ? (octets / (double)(1L << 30)).ToString("0.0", fr) + " Go" : mo.ToString(mo < 10 ? "0.0" : "0", fr) + " Mo";
    }

    /// <summary>« 1:05 » ou « 1:02:03 ».</summary>
    public static string Duree(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    /// <summary>La fenêtre principale (pour les messages et les fenêtres).</summary>
    public static MainWindow? Principale => Application.Current?.MainWindow as MainWindow;

    public static void Toast(string texte) => Principale?.Toast(texte);

    /// <summary>La police à chasse fixe (chronos, pourcentages).</summary>
    public static FontFamily Mono => (FontFamily)Application.Current.FindResource("Mono");

    /// <summary>Une petite étiquette en capitales (« HAUT-PARLEURS »).</summary>
    public static TextBlock Etiquette(string texte, double haut = 14) => new Capitales { Libelle = texte, Margin = new Thickness(0, haut, 0, 8) };

    /// <summary>Une phrase d'aide grise.</summary>
    public static TextBlock Note(string texte, double haut = 8) => new()
    {
        Text = texte, FontSize = 12.5, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, haut, 0, 0),
    };

    /// <summary>Un lien souligné (« + Autre enregistrement », « Ouvrir le dossier »).</summary>
    public static Button Lien(string texte, Action clic, double taille = 13.5)
    {
        var b = new Button { Style = S("BtnLien"), Content = texte, FontSize = taille, Margin = new Thickness(0, 0, 18, 4) };
        b.Click += (_, _) => clic();
        return b;
    }

    /// <summary>Le bouton Couper : rouge quand le son est coupé (partout : église, enregistrement, Zoom, casque).
    /// <paramref name="compact"/> : l'icône seule (colonne de droite).</summary>
    public static (Button Bouton, Action Maj) BoutonCouper(Son s, double hauteur = 38, bool compact = false)
    {
        var icone = Icone(HautParleur, P("Blanc"), compact ? 16 : 15, 1.9);
        var texte = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var contenu = new StackPanel { Orientation = Orientation.Horizontal };
        contenu.Children.Add(icone);
        if (!compact) contenu.Children.Add(texte);
        var b = new Button { Style = S("Btn"), Height = hauteur, Content = contenu, Padding = compact ? new Thickness(0) : new Thickness(14, 0, 16, 0) };
        if (compact) b.Width = hauteur;
        bool? avant = null;
        void Maj()
        {
            if (avant == s.Coupe) return;
            avant = s.Coupe;
            bool voix = s.Genre == GenreSon.Voix;
            icone.Data = s.Coupe ? (voix ? MicroCoupe : HautParleurCoupe) : (voix ? Micro : HautParleur);
            icone.Stroke = s.Coupe ? Brushes.White : P("Blanc");
            texte.Text = s.Coupe ? "Coupé" : "Couper";
            Allumer(b, s.Coupe, "Direct");
            b.ToolTip = s.Coupe ? $"« {s.Nom} » est coupé partout : cliquez pour le rallumer" : $"Couper « {s.Nom} » partout (église, enregistrement, Zoom)";
        }
        b.Click += (_, _) => { ActionsSon.Couper(s, !s.Coupe); Maj(); };
        Maj();
        return (b, Maj);
    }

    /// <summary>Un point de couleur (état d'un son, d'un envoi).</summary>
    public static Shapes.Ellipse Point(double taille = 9) => new() { Width = taille, Height = taille, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };

    public static void Placer(Window w, DependencyObject? depuis)
    {
        var f = depuis != null ? Window.GetWindow(depuis) : null;
        f ??= Principale;
        if (f is { IsLoaded: true }) w.Owner = f;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }
}

/// <summary>
/// « Ce qu'il reçoit » : tout le son (le cas normal) ou seulement les sons cochés.
/// Sert aux enregistrements et aux envois (Zoom, OBS…).
/// </summary>
internal sealed class ChoixSons : StackPanel
{
    readonly RadioButton tout, choisir;
    readonly WrapPanel cases = new() { Margin = new Thickness(28, 8, 0, 0) };
    readonly List<(CheckBox Case, string Id)> liste = new();

    public ChoixSons(List<string>? choix)
    {
        tout = new RadioButton { Content = "Tout le son (conseillé)", IsChecked = choix == null, FontSize = 14.5, Margin = new Thickness(0, 0, 0, 8), GroupName = "sons" + GetHashCode() };
        choisir = new RadioButton { Content = "Seulement ces sons :", IsChecked = choix != null, FontSize = 14.5, GroupName = "sons" + GetHashCode() };
        Children.Add(tout);
        Children.Add(choisir);
        foreach (var s in SonUi.C.Sons)
        {
            var contenu = new StackPanel { Orientation = Orientation.Horizontal };
            var icone = SonUi.Icone(SonUi.IconeGenre(s.Genre), SonUi.P(SonUi.CouleurGenre(s.Genre)), 16);
            icone.Margin = new Thickness(0, 0, 6, 0);
            contenu.Children.Add(icone);
            contenu.Children.Add(new TextBlock { Text = s.Nom, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            var cb = new CheckBox { Content = contenu, IsChecked = choix?.Contains(s.Id) ?? true, Margin = new Thickness(0, 0, 18, 8) };
            liste.Add((cb, s.Id));
            cases.Children.Add(cb);
        }
        Children.Add(cases);
        tout.Checked += (_, _) => Maj();
        choisir.Checked += (_, _) => Maj();
        Maj();
    }

    void Maj() => cases.IsEnabled = choisir.IsChecked == true;

    /// <summary>Null = tout le son ; sinon les sons cochés.</summary>
    public List<string>? Choix => tout.IsChecked == true ? null : liste.Where(x => x.Case.IsChecked == true).Select(x => x.Id).ToList();
}
