using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Un vumètre de −60 à 0 dBFS, horizontal ou vertical : vert, puis jaune vers −20 dB, rouge près de 0 dB (comme OBS),
/// avec une retombée douce et un repère de crête tenu 1,5 s. Grisé quand la source est coupée (le signal reste visible, comme dans OBS).
/// </summary>
internal sealed class Vumetre : Grid
{
    static Brush? couleursH, couleursV;
    readonly bool vertical;
    readonly Shapes.Rectangle barre;
    readonly Border crete;
    readonly RectangleGeometry coupe = new(new Rect(0, 0, 0, 0));
    double affiche = double.NegativeInfinity, pic = double.NegativeInfinity;
    long picDepuis, dernier;
    bool gris;

    /// <summary>Vitesse de retombée (dB par seconde), celle d'OBS par défaut (« rapide »).</summary>
    internal const double Retombee = 23.53;

    public Vumetre(double epaisseur, bool vertical = false)
    {
        this.vertical = vertical;
        if (vertical) Width = epaisseur; else Height = epaisseur;
        SnapsToDevicePixels = true;
        Children.Add(Zones(vertical, epaisseur));
        barre = new Shapes.Rectangle { Fill = vertical ? (couleursV ??= Degrade(true)) : (couleursH ??= Degrade(false)), RadiusX = epaisseur / 2, RadiusY = epaisseur / 2, Clip = coupe };
        Children.Add(barre);
        crete = new Border
        {
            Background = MainWindow.Pinceau("Blanc"), Visibility = Visibility.Collapsed, Opacity = 0.75,
            HorizontalAlignment = vertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            VerticalAlignment = vertical ? VerticalAlignment.Top : VerticalAlignment.Stretch,
        };
        if (vertical) crete.Height = 2; else crete.Width = 2;
        Children.Add(crete);
    }

    public bool Gris
    {
        get => gris;
        set { gris = value; barre.Opacity = value ? 0.32 : 1; crete.Opacity = value ? 0.25 : 0.75; }
    }

    /// <summary>Nouveau niveau en dBFS (−∞ = silence).</summary>
    public void Niveau(double dbfs)
    {
        if (double.IsNaN(dbfs) || dbfs < -100) dbfs = -100;
        // montée immédiate, retombée à la vitesse d'OBS ; la crête reste 1,5 s puis redescend
        long maintenant = Environment.TickCount64;
        double dt = dernier == 0 ? 0.05 : Math.Clamp((maintenant - dernier) / 1000.0, 0, 0.5);
        dernier = maintenant;
        affiche = dbfs >= affiche ? dbfs : Math.Max(dbfs, affiche - Retombee * dt);
        if (dbfs >= pic) { pic = dbfs; picDepuis = maintenant; }
        else if (maintenant - picDepuis > 1500) pic = Math.Max(affiche, pic - 1.5);
        double f = Math.Clamp((affiche + 60) / 60, 0, 1), fp = Math.Clamp((pic + 60) / 60, 0, 1);
        double w = Math.Max(0, ActualWidth), h = Math.Max(0, ActualHeight);
        if (vertical)
        {
            coupe.Rect = new Rect(0, h * (1 - f), w, h * f);
            crete.Margin = new Thickness(0, Math.Max(0, h * (1 - fp) - 1), 0, 0);
        }
        else
        {
            coupe.Rect = new Rect(0, 0, w * f, h);
            crete.Margin = new Thickness(Math.Max(0, w * fp - 1), 0, 0, 0);
        }
        crete.Visibility = fp > 0.001 ? Visibility.Visible : Visibility.Collapsed;
    }

    static Brush Degrade(bool vertical)
    {
        var b = vertical ? new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) } : new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0x2F, 0x9E, 0x5B), 0));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0x2F, 0x9E, 0x5B), 0.64)); // −21,6 dB
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xE3, 0xB0, 0x2E), 0.8));  // −12 dB
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xE3, 0xB0, 0x2E), 0.86));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xD9, 0x2D, 0x20), 0.96));  // −2,4 dB
        b.Freeze();
        return b;
    }

    /// <summary>Le fond du vumètre : gris, et rosé dans les derniers dB (près de 0), aux couleurs du thème.</summary>
    static UIElement Zones(bool vertical, double epaisseur)
    {
        var g = new Grid { ClipToBounds = true };
        var fond = new Border { CornerRadius = new CornerRadius(epaisseur / 2) };
        fond.SetResourceReference(Border.BackgroundProperty, "VuFond");
        var crete = new Border();
        crete.SetResourceReference(Border.BackgroundProperty, "VuCrete");
        if (vertical)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(86, GridUnitType.Star) });
            Grid.SetRowSpan(fond, 2);
            crete.CornerRadius = new CornerRadius(epaisseur / 2, epaisseur / 2, 0, 0);
        }
        else
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14, GridUnitType.Star) });
            Grid.SetColumnSpan(fond, 2);
            Grid.SetColumn(crete, 1);
            crete.CornerRadius = new CornerRadius(0, epaisseur / 2, epaisseur / 2, 0);
        }
        g.Children.Add(fond);
        g.Children.Add(crete);
        return g;
    }
}
