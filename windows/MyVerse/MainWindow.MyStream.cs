using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MyVerse.Ui.Audio;

namespace MyVerse.Ui;

/// <summary>
/// MyChurch 1.6.0 : les deux écrans de la colonne de droite. L'église (ce que montre le projecteur) et MyStream (ce qui part sur Zoom
/// et sur YouTube). MyStream a sa source principale (choisie avec « Source »), toujours là comme l'écran de repos de l'église ;
/// « Source principale » (touche M) y ramène MyStream. En haut, la pastille YouTube ouvre le direct.
/// </summary>
public partial class MainWindow
{
    /// <summary>Le lecteur qui donne l'image vivante de MyStream : celui de l'église, de l'écran de repos, ou celui de la source principale.</summary>
    SourcesDisplay? lecteurStream;
    /// <summary>MyStream montre le contenu épinglé (pas un contenu « Dans l'église »).</summary>
    bool streamDuo = true;
    EtatEcran etatStream = new();

    void InitMyStream()
    {
        StreamRetour.Click += (_, _) => RetourStream();
        StreamChoisir.Click += (_, _) => Ouvrir(MenuStream(), StreamChoisir, PlacementMode.Bottom);
        DirectBtn.Click += (_, _) => OuvrirDirect();
        App.SourcesStream.Change += () =>
        {
            if (lecteurStream == App.SourcesStream) ImageStream();
            MajInfoStream();
        };
        App.Direct.Change += MajDirect;
        minuterieHorloge.Tick += (_, _) => { if (App.Direct.EnDirect) MajDirect(); };
        MajDirect();
    }

    /// <summary>
    /// Le lecteur de l'image vivante de MyStream. La source principale reste captée tant qu'elle est choisie : MyStream ne clignote pas
    /// quand l'église la montre puis la retire (une caméra n'est jamais ouverte deux fois : la capture est partagée).
    /// </summary>
    void ChoisirLecteurStream(EtatEcran stream)
    {
        var principale = R.SourcePrincipaleStream?.Source;
        App.SourcesStream.ChoisirSource(principale);
        var m = stream.Media;
        lecteurStream = m == null ? null
            : m == principale ? App.SourcesStream
            : App.Sources.Source == m ? App.Sources
            : App.SourcesDefaut.Source == m ? App.SourcesDefaut
            : null;
    }

    /// <summary>L'image vivante de MyStream, vers son aperçu et vers Zoom et YouTube.</summary>
    void ImageStream()
    {
        var b = lecteurStream?.Image;
        StreamRendu.ImageDisplay(b);
        App.MyStream?.Media(b);
    }

    /// <summary>Après chaque changement de l'écran : l'aperçu de MyStream, son moteur, et les mots sous l'aperçu.</summary>
    void MajStream(EtatEcran stream)
    {
        etatStream = stream;
        streamDuo = stream.Duo != null;
        App.MyStream?.Afficher(stream);
        StreamRendu.Afficher(stream);
        ImageStream();
        StreamApercu.Background = stream.Vide && stream.Duo == null ? Pinceau("Damier") : Brushes.Black;
        MajInfoStream();
    }

    /// <summary>« Source principale : MyCam Samsung · paroles en bandeau ».</summary>
    void MajInfoStream()
    {
        var p = R.SourcePrincipaleStream;
        StreamRetour.IsEnabled = p != null;
        if (p == null)
        {
            StreamInfo.Text = R.SourcesStreamPossibles.Count == 0
                ? "Pas de source principale : MyStream montre comme l'église. Ajoutez une caméra ou MyCam (Bibliothèque › Médias), puis « Source »."
                : "Pas de source principale : MyStream montre comme l'église. « Source » pour en choisir une.";
            return;
        }
        string statut = App.SourcesStream.Statut is { Length: > 0 } s && App.SourcesStream.Image == null ? " (" + s.TrimEnd('.') + ")" : "";
        StreamInfo.Text = $"Source principale : {p.Titre}{statut} · paroles {(R.TexteStream == "plein" ? "en plein écran" : "en bandeau")}"
            + (R.StreamSurPrincipale ? " · sur la source principale" : "");
    }

    /// <summary>Touche M ou « Source principale » : MyStream revient sur sa source principale, l'église garde ce qu'elle montre.</summary>
    void RetourStream()
    {
        if (R.SourcePrincipaleStream == null)
        {
            Ouvrir(MenuStream(), StreamChoisir, PlacementMode.Bottom);
            return;
        }
        if (R.StreamPrincipale()) Toast($"MyStream : « {R.SourcePrincipaleStream?.Titre} ». L'église garde ce qu'elle montre ; la prochaine diapositive repart aussi sur MyStream.");
    }

    /// <summary>Le menu « Source » : la source principale, les paroles en bandeau ou en plein écran, le direct.</summary>
    ContextMenu MenuStream()
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Source principale de MyStream", IsEnabled = false, FontWeight = FontWeights.Bold });
        var actuelle = R.SourcePrincipaleStream?.Id;
        var possibles = R.SourcesStreamPossibles;
        foreach (var a in possibles)
        {
            var id = a.Id;
            var item = Article(a.Titre, () => { R.ChoisirSourceStream(id); Toast($"Source principale de MyStream : « {a.Titre} »."); });
            item.IsCheckable = true;
            item.IsChecked = id == actuelle;
            item.ToolTip = a.Description;
            menu.Items.Add(item);
        }
        if (possibles.Count == 0) menu.Items.Add(new MenuItem { Header = "Aucune caméra : ajoutez-en une dans Bibliothèque › Médias", IsEnabled = false });
        var aucune = Article("Aucune (MyStream montre comme l'église)", () => R.ChoisirSourceStream(null));
        aucune.IsCheckable = true;
        aucune.IsChecked = actuelle == null;
        menu.Items.Add(aucune);
        menu.Items.Add(new Separator());
        var bandeau = Article("Paroles et versets en bandeau, sur la source principale", () => R.ChoisirTexteStream("bandeau"));
        bandeau.IsCheckable = true; bandeau.IsChecked = R.TexteStream == "bandeau";
        var plein = Article("Paroles et versets en plein écran, comme l'église", () => R.ChoisirTexteStream("plein"));
        plein.IsCheckable = true; plein.IsChecked = R.TexteStream == "plein";
        menu.Items.Add(bandeau);
        menu.Items.Add(plein);
        menu.Items.Add(new Separator());
        menu.Items.Add(Article("Le direct sur YouTube…", OuvrirDirect));
        return menu;
    }

    // ------------------------------------------------------------ le direct (pastille en haut, fenêtre)
    DirectWindow? fenetreDirect;

    void OuvrirDirect()
    {
        if (fenetreDirect is { IsLoaded: true }) { fenetreDirect.Activate(); return; }
        fenetreDirect = new DirectWindow { Owner = this };
        fenetreDirect.Closed += (_, _) => fenetreDirect = null;
        fenetreDirect.Show();
    }

    /// <summary>La pastille « YouTube » : grise à l'arrêt, rouge en direct (avec la durée), ambre pendant une reconnexion.</summary>
    void MajDirect()
    {
        var d = App.Direct;
        var duree = d.Duree;
        string temps = $"{(int)duree.TotalHours}:{duree.Minutes:00}:{duree.Seconds:00}";
        switch (d.Etat)
        {
            case EtatDirect.EnDirect:
                DirectTexte.Text = "En direct " + temps;
                DirectBtn.Background = Pinceau("Direct"); DirectBtn.BorderBrush = Pinceau("Direct");
                DirectTexte.Foreground = Brushes.White; DirectPoint.Fill = Brushes.White;
                StreamPastilleTexte.Text = "● En direct";
                StreamPastille.Background = Pinceau("Direct"); StreamPastilleTexte.Foreground = Brushes.White;
                break;
            case EtatDirect.Reconnexion:
                DirectTexte.Text = "Reconnexion…";
                DirectBtn.Background = Pinceau("Ensuite"); DirectBtn.BorderBrush = Pinceau("Ensuite");
                DirectTexte.Foreground = Pinceau("SurEnsuite"); DirectPoint.Fill = Pinceau("SurEnsuite");
                StreamPastilleTexte.Text = "Reconnexion…";
                StreamPastille.Background = Pinceau("Ensuite"); StreamPastilleTexte.Foreground = Pinceau("SurEnsuite");
                break;
            default:
                DirectTexte.Text = d.Etat is EtatDirect.Preparation ? "Démarrage…" : d.Etat is EtatDirect.Arret ? "Arrêt…" : "YouTube";
                DirectBtn.ClearValue(BackgroundProperty); DirectBtn.ClearValue(BorderBrushProperty);
                DirectTexte.ClearValue(TextBlock.ForegroundProperty);
                DirectPoint.Fill = Pinceau(d.Etat == EtatDirect.Erreur ? "Danger" : "Brume2");
                StreamPastilleTexte.Text = "Zoom · YouTube";
                StreamPastille.Background = Pinceau("StreamLeger"); StreamPastilleTexte.Foreground = Pinceau("Stream");
                break;
        }
        DirectBtn.ToolTip = d.Message.Length > 0 ? d.Message : "Le direct sur YouTube : MyStream et le son partent sur Internet";
    }

    // ------------------------------------------------------------ pour le test automatique
    public EtatEcran TestEtatStream => etatStream;
    public string TestInfoStream => StreamInfo.Text;
    public string TestDirectTexte => DirectTexte.Text;
    public void TestRetourStream() => RetourStream();
    public void TestOuvrirDirect() => OuvrirDirect();
    public DirectWindow? TestFenetreDirect => fenetreDirect;
    public int TestMenuStream() => MenuStream().Items.Count;
}
