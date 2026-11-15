using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MyVerse.Ui;

/// <summary>
/// Always publishes the program output while MyChurch is open. The registered filter stays available when it is closed.
/// MyChurch 1.5.0 : le fil de l'interface ne fait presque plus rien pour la caméra virtuelle :
/// - une image vivante plein écran (fenêtre Zoom, MyCam, caméra, vidéo, sans Duo) est mise à 1280 × 720 à côté, sans dessin WPF ;
/// - le reste (paroles, versets, Duo) est dessiné une fois quand il change (pas plus de 15 fois par seconde),
///   puis converti et écrit à côté ;
/// - l'image n'est écrite dans la mémoire partagée que quand elle change ; entre deux, seule l'heure est rafraîchie
///   (le filtre montre du noir si elle a plus de 2 secondes).
/// </summary>
public sealed class MyVirtualCam : IDisposable
{
    public const string Clsid = "{A9D47A18-C751-4BDE-AB6B-A3D13F090501}";
    public const int Width = 1280, Height = 720, Stride = Width * 3, Bytes = Stride * Height;
    readonly Rendu rendu = new() { Transitions = false, FondFlou = false, Width = Width, Height = Height, Background = Brushes.Black };
    readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
    readonly MemoryMappedFile fichier;
    readonly MemoryMappedViewAccessor vue;
    readonly Mutex verrou = new(false, @"Local\MyChurch.MyVirtualCam.lock.v1");
    /// <summary>Un seul écrivain à la fois dans la mémoire partagée (le fil de l'interface ou le travail à côté).</summary>
    readonly object verrouVue = new();
    readonly RenderTargetBitmap image = new(Width, Height, 96, 96, PixelFormats.Pbgra32);
    readonly byte[] bgra = new byte[Width * Height * 4];
    readonly byte[] trame = new byte[Bytes];
    byte[] lignes = Array.Empty<byte>();
    bool sale = true, dispose;
    int seq;
    long dernierEntete, dernierDessin;
    EtatEcran? etat;
    /// <summary>L'image vivante qui attend d'être convertie (la plus récente seulement) ; 1 quand le travail à côté tourne.</summary>
    BitmapSource? aConvertir;
    int conversion;
    /// <summary>Le chemin rapide a échoué une fois : on reste au dessin WPF.</summary>
    volatile bool rapideEnPanne;
    public static bool Installee => Registry.ClassesRoot.OpenSubKey(@"CLSID\" + Clsid + @"\InprocServer32") is { } k && Fermer(k);
    static bool Fermer(RegistryKey k) { k.Dispose(); return true; }

    public MyVirtualCam()
    {
        fichier = MemoryMappedFile.CreateOrOpen(@"Local\MyChurch.MyVirtualCam.frames.v1", 64 + Bytes, MemoryMappedFileAccess.ReadWrite);
        vue = fichier.CreateViewAccessor();
        timer.Tick += (_, _) => Publier();
        timer.Start();
    }

    /// <summary>Une image vivante seule à l'écran : elle part sans dessin WPF.</summary>
    bool Rapide => !rapideEnPanne && etat is { Mode: "media", Duo: null };

    public void Afficher(EtatEcran e) { etat = e; rendu.Afficher(e); sale = true; }
    public void Media(BitmapSource? b)
    {
        rendu.ImageDisplay(b);
        if (Rapide && (b == null || b.IsFrozen)) { Convertir(b); return; }
        sale = true;
    }
    /// <summary>Image vivante du contenu épinglé (Duo).</summary>
    public void MediaDuo(BitmapSource? b) { rendu.ImageDuo(b); sale = true; }

    // ------------------------------------------------------------ chemin rapide : l'image vivante, mise à 1280 × 720 à côté
    static readonly BitmapSource Vide = Noir();
    static BitmapSource Noir() { var b = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr24, null, new byte[12], 6); b.Freeze(); return b; }

    void Convertir(BitmapSource? b)
    {
        Interlocked.Exchange(ref aConvertir, b ?? Vide);
        if (Interlocked.CompareExchange(ref conversion, 1, 0) != 0) return; // le travail en cours prendra la plus récente
        _ = Task.Run(Travailler);
    }

    void Travailler()
    {
        while (true)
        {
            try
            {
                while (Interlocked.Exchange(ref aConvertir, null) is { } img)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    lock (verrouVue)
                    {
                        if (ReferenceEquals(img, Vide)) Array.Clear(trame);
                        else Cadrer(img, trame);
                        Ecrire(trame, 5);
                    }
                    Perf.Ajouter("MyVirtualCam : image vivante (à côté)", Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                }
            }
            catch (Exception x)
            {
                // on revient au dessin WPF, comme avant 1.5.0
                rapideEnPanne = true;
                Journal.Erreur("MyVirtualCam (image vivante)", x);
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => sale = true));
            }
            Volatile.Write(ref conversion, 0);
            if (Volatile.Read(ref aConvertir) == null || Interlocked.CompareExchange(ref conversion, 1, 0) != 0) return;
        }
    }

    /// <summary>L'image entière dans 1280 × 720, au milieu, bandes noires autour (comme le dessin : Stretch Uniform sur fond noir), lignes de bas en haut.</summary>
    void Cadrer(BitmapSource b, byte[] dest)
    {
        int sw = b.PixelWidth, sh = b.PixelHeight;
        Array.Clear(dest);
        if (sw <= 0 || sh <= 0) return;
        double s = Math.Min((double)Width / sw, (double)Height / sh);
        BitmapSource src = b;
        if (Math.Abs(s - 1) > 0.002) src = new TransformedBitmap(b, new ScaleTransform(s, s));
        if (src.Format != PixelFormats.Bgr24) src = new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0);
        int w = Math.Min(Width, src.PixelWidth), h = Math.Min(Height, src.PixelHeight);
        if (w <= 0 || h <= 0) return;
        int pas = w * 3;
        if (lignes.Length < pas * h) lignes = new byte[pas * h];
        src.CopyPixels(new Int32Rect(0, 0, w, h), lignes, pas, 0);
        int x0 = (Width - w) / 2, y0 = (Height - h) / 2;
        for (int y = 0; y < h; y++) Buffer.BlockCopy(lignes, y * pas, dest, (Height - 1 - (y0 + y)) * Stride + x0 * 3, pas);
    }

    // ------------------------------------------------------------ chemin normal : le dessin WPF quand l'écran change
    void Publier()
    {
        try
        {
            long maintenant = Environment.TickCount64;
            // une image vivante seule : le chemin rapide s'en charge, rien à dessiner ici
            if (sale && Rapide) sale = false;
            // pas plus de 15 dessins par seconde (une caméra dans un Duo envoie 30 images par seconde)
            if (sale && maintenant - dernierDessin >= 66)
            {
                long t0 = Stopwatch.GetTimestamp();
                dernierDessin = maintenant;
                sale = false;
                rendu.Measure(new Size(Width, Height)); rendu.Arrange(new Rect(0, 0, Width, Height)); rendu.UpdateLayout();
                image.Clear(); image.Render(rendu);
                image.CopyPixels(bgra, Width * 4, 0);
                Perf.Ajouter("MyVirtualCam : dessin (fil de l'interface)", Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                // la conversion BGRA → BGR (lignes de bas en haut) et l'écriture se font à côté
                var copie = (byte[])bgra.Clone();
                _ = Task.Run(() =>
                {
                    try
                    {
                        lock (verrouVue)
                        {
                            for (int y = 0; y < Height; y++)
                            {
                                int s = y * Width * 4, d = (Height - 1 - y) * Stride;
                                for (int x = 0; x < Width; x++, s += 4, d += 3) { trame[d] = copie[s]; trame[d + 1] = copie[s + 1]; trame[d + 2] = copie[s + 2]; }
                            }
                            Ecrire(trame, 5);
                        }
                    }
                    catch (Exception x) { Journal.Erreur("MyVirtualCam (écriture)", x); }
                });
                dernierEntete = maintenant;
                return;
            }
            // rien de neuf : l'heure seulement, deux fois par seconde (le filtre attend moins de 2 secondes)
            if (maintenant - dernierEntete < 500) return;
            dernierEntete = maintenant;
            if (!Monitor.TryEnter(verrouVue)) return;
            try { Ecrire(null, 0); }
            finally { Monitor.Exit(verrouVue); }
        }
        catch (Exception e) { Journal.Erreur("MyVirtualCam", e); timer.Stop(); }
    }

    /// <summary>Écrit l'en-tête (et l'image s'il y en a une) sous le verrou partagé avec le filtre DirectShow.</summary>
    void Ecrire(byte[]? trameNeuve, int attenteMs)
    {
        bool acquis;
        try { acquis = verrou.WaitOne(attenteMs); } catch (AbandonedMutexException) { acquis = true; }
        if (!acquis) return;
        try
        {
            vue.Write(0, 0x4D43414D); vue.Write(4, 1);
            vue.Write(8, Environment.TickCount64); vue.Write(16, Width); vue.Write(20, Height); vue.Write(24, Stride);
            if (trameNeuve != null) { vue.WriteArray(64, trameNeuve, 0, Bytes); vue.Write(28, ++seq); }
            else vue.Write(28, seq);
        }
        finally { verrou.ReleaseMutex(); }
    }

    public static void Installer(Window owner)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--virtualcam-install") { UseShellExecute = true, Verb = "runas" });
        }
        catch (Exception e) { Dialogue.Informer(owner, "Installation interrompue : " + e.Message); }
    }
    public static int Enregistrer(bool installer)
    {
        // Register both architectures; consumers load the matching in-process DirectShow source.
        int resultat = 0;
        foreach (var (exe, dll) in new[] {
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "regsvr32.exe"), "MyVirtualCam.dll"),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64", "regsvr32.exe"), "MyVirtualCam32.dll") })
        {
            var path = Path.Combine(AppContext.BaseDirectory, dll);
            if (!File.Exists(path)) { resultat = 2; continue; }
            using var p = Process.Start(new ProcessStartInfo(exe, (installer ? "/s " : "/s /u ") + "\"" + path + "\"") { UseShellExecute = false });
            if (p == null || !p.WaitForExit(30000) || p.ExitCode != 0) resultat = 1;
        }
        return resultat;
    }
    public void Dispose()
    {
        if (dispose) return; dispose = true; timer.Stop();
        lock (verrouVue)
        {
            try { vue.Write(8, 0L); } catch (Exception) { }
            vue.Dispose(); fichier.Dispose(); verrou.Dispose();
        }
    }
}
