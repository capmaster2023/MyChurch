using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MyVerse.Ui.Audio;

namespace MyVerse.Ui;

/// <summary>
/// MyChurch 1.6.0 : les deux écrans de la colonne de droite. L'église (ce que montre le projecteur) et MyStream (ce qui part sur Zoom
/// et sur YouTube). 1.6.4 : MyStream montre sa source principale (choisie avec « Source »), quoi que montre l'église ;
/// « Envoyer dans le stream » (touche S, sous l'église) : MyStream montre aussi ce que montre l'église et le suit ;
/// « Source principale » (touche M) le ramène sur sa source principale. En haut, la pastille YouTube ouvre le direct.
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
        EnvoyerStream.Click += (_, _) => EnvoyerDansStream();
        ToolTipService.SetShowOnDisabled(EnvoyerStream, true);
        StreamChoisir.Click += (_, _) => Ouvrir(MenuStream(), StreamChoisir, PlacementMode.Bottom);
        DirectBtn.Click += (_, _) => OuvrirDirect();
        App.SourcesStream.Change += () =>
        {
            if (lecteurStream == App.SourcesStream) ImageStream();
            MajInfoStream();
        };
        App.Direct.Change += () => { MajDirect(); MajVeille(); };
        minuterieHorloge.Tick += (_, _) => { if (App.Direct.EnDirect) MajDirect(); };
        // 1.6.4 : les directs automatiques le disent dans MyChurch (deux minutes avant, au démarrage, à la fin)
        App.Planificateur.Annonce += Toast;
        App.Planificateur.Change += () => { MajDirect(); MajVeille(); };
        MajDirect();
        MajVeille();
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

    /// <summary>
    /// Sous MyStream : ce qu'il montre (« Caméra · source principale » ou « suit l'église ») ; les boutons « Envoyer dans le stream »
    /// (allumé quand MyStream suit l'église) et « Source principale ».
    /// </summary>
    void MajInfoStream()
    {
        var p = R.SourcePrincipaleStream;
        bool suit = p != null && R.StreamSuitEglise;
        StreamRetour.IsEnabled = p != null;
        EnvoyerStream.IsEnabled = p != null;
        if (suit)
        {
            EnvoyerStream.Background = Pinceau("StreamLeger");
            EnvoyerStream.BorderBrush = Pinceau("Stream");
            EnvoyerStreamTexte.Text = "MyStream suit l'église";
            EnvoyerStreamTexte.Foreground = Pinceau("Stream");
            EnvoyerStream.ToolTip = "MyStream montre ce que montre l'église et le suit. « Source principale » (M) pour revenir sur " + p!.Titre + ".";
            StreamRetour.BorderBrush = Pinceau("Stream");
        }
        else
        {
            EnvoyerStream.ClearValue(BackgroundProperty);
            EnvoyerStream.ClearValue(BorderBrushProperty);
            EnvoyerStreamTexte.Text = "Envoyer dans le stream";
            EnvoyerStreamTexte.ClearValue(TextBlock.ForegroundProperty);
            EnvoyerStream.ToolTip = p == null
                ? "Sans source principale, MyStream montre déjà ce que montre l'église. Choisissez une source avec « Source »."
                : "Touche S : MyStream montre aussi ce que montre l'église (paroles, versets, images…) et le suit, jusqu'à « Source principale » (M)";
            StreamRetour.ClearValue(BorderBrushProperty);
        }
        if (p == null)
        {
            StreamInfo.Text = R.SourcesStreamPossibles.Count == 0
                ? "Pas de source principale : MyStream montre comme l'église. Ajoutez une caméra ou MyCam (Bibliothèque › Médias), puis « Source »."
                : "Pas de source principale : MyStream montre comme l'église. « Source » pour en choisir une.";
            return;
        }
        string statut = App.SourcesStream.Statut is { Length: > 0 } s && App.SourcesStream.Image == null ? " (" + s.TrimEnd('.') + ")" : "";
        StreamInfo.Text = suit
            ? $"MyStream suit l'église : ce qu'elle montre part sur Zoom et YouTube. M : revenir sur {p.Titre}."
            : etatStream is { Mode: "media" } e && e.Media != null && e.Media != p.Source
                ? $"MyStream montre une source « MyStream seulement ». Source principale : {p.Titre}{statut}."
                : $"MyStream montre {p.Titre}{statut}, quoi que montre l'église.";
    }

    /// <summary>Touche M ou « Source principale » : MyStream revient sur sa source principale, l'église garde ce qu'elle montre.</summary>
    void RetourStream()
    {
        if (R.SourcePrincipaleStream == null)
        {
            Ouvrir(MenuStream(), StreamChoisir, PlacementMode.Bottom);
            return;
        }
        bool suivait = R.StreamSuitEglise;
        if (R.StreamPrincipale())
            Toast(suivait
                ? $"MyStream : « {R.SourcePrincipaleStream?.Titre} ». L'église garde ce qu'elle montre ; MyStream ne la suit plus."
                : $"MyStream montre déjà « {R.SourcePrincipaleStream?.Titre} ».");
    }

    /// <summary>
    /// Touche S ou « Envoyer dans le stream » : ce que montre l'église part aussi sur MyStream, et la suite aussi, jusqu'à « Source principale ».
    /// </summary>
    void EnvoyerDansStream()
    {
        if (R.SourcePrincipaleStream == null)
        {
            Toast("Sans source principale, MyStream montre déjà ce que montre l'église. « Source » pour en choisir une.");
            return;
        }
        if (R.StreamSuitEglise) { Toast("MyStream suit déjà l'église. « Source principale » (M) pour revenir sur " + R.SourcePrincipaleStream.Titre + "."); return; }
        if (R.EnvoyerDansStream())
            Toast(R.Etat is { Mode: "vide" or "fond" } || R.Cible is CibleDefaut
                ? "MyStream suit l'église : ce que vous montrerez partira aussi sur Zoom et YouTube. M pour revenir sur la source principale."
                : "Envoyé dans le stream : MyStream montre ce que montre l'église, et la suit. M pour revenir sur la source principale.");
    }

    /// <summary>Le menu « Source » : la source principale de MyStream (ce qu'il montre), aucune, le direct.</summary>
    ContextMenu MenuStream()
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Source principale de MyStream", IsEnabled = false, FontWeight = FontWeights.Bold });
        var actuelle = R.SourcePrincipaleStream?.Id;
        var possibles = R.SourcesStreamPossibles;
        foreach (var a in possibles)
        {
            var id = a.Id;
            var item = Article(a.Titre, () => { R.ChoisirSourceStream(id); Toast($"MyStream montre « {a.Titre} », quoi que montre l'église."); });
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
        var prochain = App.Planificateur?.Resume(DateTime.Now) ?? "";
        DirectBtn.ToolTip = (d.Message.Length > 0 ? d.Message : "Le direct sur YouTube : MyStream et le son partent sur Internet")
            + (prochain.Length > 0 ? "\n" + prochain : "");
    }

    // ------------------------------------------------------------ pour le test automatique
    public EtatEcran TestEtatStream => etatStream;
    public string TestInfoStream => StreamInfo.Text;
    public string TestDirectTexte => DirectTexte.Text;
    public void TestRetourStream() => RetourStream();
    public void TestEnvoyerStream() => EnvoyerDansStream();
    public string TestEnvoyerTexte => EnvoyerStreamTexte.Text;
    public void TestOuvrirDirect() => OuvrirDirect();
    public DirectWindow? TestFenetreDirect => fenetreDirect;
    public int TestMenuStream() => MenuStream().Items.Count;
}
