using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace MyVerse.Audio;

/// <summary>Ce qui fournit les images d'un enregistrement vidéo (la composition des sources Display).</summary>
public interface IRecordingFrameProvider
{
    /// <summary>L'image (BGRA) qui montre le monde à l'instant <paramref name="monde"/> (horloge <see cref="Horloge"/>, ns).</summary>
    void CopyFrame(byte[] destination, long monde);
    /// <summary>
    /// Retard des images vivantes sur le monde (ns) : l'image de l'instant T n'est complète qu'à T + Retard
    /// (MyCam : de la prise de vue sur le téléphone à l'image décodée sur le PC). 0 pour du texte ou une image fixe.
    /// </summary>
    long Retard { get; }
}

/// <summary>
/// Un enregistrement : le son d'une piste d'OBS (file bornée : le fil audio d'OBS ne fait que copier un bloc) et, en vidéo,
/// les images de la composition, écrits pour ffmpeg par des fils à eux.
/// MyChurch 1.2 : synchro automatique, comme OBS. Chaque bloc de son porte l'instant où OBS l'a capté ; la n-ième image
/// du fichier est celle du monde à l'instant du son à la même place (origine du son + n / ips), prise quand elle est prête
/// (après le retard de MyCam). Plus de réglage « Synchro du son » : ni le retard du mélange d'OBS ni celui de la caméra
/// ne décalent la voix et l'image.
/// </summary>
public sealed class RecordingSession : IDisposable
{
    // 5 s de son en attente : le temps qu'un encodeur avec retard (x264, carte graphique) sorte ses premières images
    const int Slots = 500;
    readonly float[][] blocks = Enumerable.Range(0, Slots).Select(_ => new float[AudioBlock.Samples]).ToArray();
    readonly object gate = new();
    readonly SemaphoreSlim available = new(0);
    readonly CancellationTokenSource cancel = new();
    int read, count;
    bool finished;
    volatile bool audioDone;
    readonly RecordingProfile profile;
    readonly string executable;
    readonly IRecordingFrameProvider? compositor;
    readonly NamedPipeServerStream? audioPipe, videoPipe;
    Process? ffmpeg;
    Task? worker, stderrWorker;
    Thread? videoWorker;
    readonly TaskCompletionSource videoFin = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Instant du monde (horloge <see cref="Horloge"/>) à la place 0 du fichier : celui du premier bloc de son, moins l'amorce. Inconnu avant le premier bloc.</summary>
    long origine = long.MinValue;
    /// <summary>Instant du premier bloc reçu, et décalages constatés depuis (un saut de l'horloge d'OBS recale la vidéo).</summary>
    long premierBloc, sauts;
    /// <summary>Images écrites pour ffmpeg.</summary>
    long imagesEcrites;
    readonly StringBuilder stderr = new();
    readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Trames de son reçues (pas forcément encore écrites pour ffmpeg) : c'est l'horloge de la vidéo.</summary>
    long accepted;
    /// <summary>Silence d'amorçage (une image) avant le premier bloc reçu.</summary>
    readonly int primeFrames;
    volatile string? error;
    public string? Error => error;
    public long AcceptedFrames => Interlocked.Read(ref accepted);
    /// <summary>Images vidéo écrites.</summary>
    public long VideoFrames => Interlocked.Read(ref imagesEcrites);
    /// <summary>Recalages de la vidéo sur le son (saut de l'horloge du son) : 0 normalement.</summary>
    public long Sauts => Interlocked.Read(ref sauts);
    public long Bytes => !Direct && File.Exists(Path) ? new FileInfo(Path).Length : 0;
    /// <summary>MyChurch 1.6.0 : un direct (YouTube, un autre service) : <see cref="Path"/> est l'adresse RTMP(S), pas un fichier.</summary>
    public bool Direct => profile.Format == RecordingProfile.FormatDirect;
    public string Path { get; }
    public string Encoder { get; }
    public Task Completion => worker ?? Task.CompletedTask;
    public RecordingSession(RecordingProfile profile, string path, string executable, string encoder, IRecordingFrameProvider? compositor)
    {
        this.profile = profile; Path = path; this.executable = executable; Encoder = encoder; this.compositor = compositor;
        if (!(profile.AudioOnly && profile.Format == "wav"))
        {
            audioPipe = NamedPipe(); if (!profile.AudioOnly) videoPipe = NamedPipe();
        }
        primeFrames = videoPipe == null ? 0 : AudioBlock.Rate / profile.Fps;
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NamedPipeServerStream, PipeName> names = new();
    sealed record PipeName(string Value);
    static NamedPipeServerStream NamedPipe()
    {
        string name = "mychurch-" + Guid.NewGuid().ToString("N");
        var pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 65536); names.Add(pipe, new(name)); return pipe;
    }
    /// <summary>Le chemin que ffmpeg ouvre : un tube nommé sous Windows, la prise Unix de .NET ailleurs (essais sous Linux).</summary>
    static string PipePath(NamedPipeServerStream pipe)
    {
        string nom = names.GetValue(pipe, _ => throw new InvalidOperationException()).Value;
        return OperatingSystem.IsWindows() ? @"\\.\pipe\" + nom : "unix:" + System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CoreFxPipe_" + nom);
    }
    public static IReadOnlyList<string> FfmpegArguments(RecordingProfile profile, string audioInput, string? videoInput, string encoder, string output)
    {
        // pas de « -thread_queue_size » sur les entrées : les ffmpeg récents le refusent, et la vidéo suit le son reçu
        var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-nostdin", "-y" };
        if (videoInput != null) args.AddRange(new[] { "-f", "rawvideo", "-pixel_format", "bgra", "-video_size", $"{profile.Width}x{profile.Height}", "-framerate", profile.Fps.ToString(), "-probesize", "32", "-analyzeduration", "0", "-i", videoInput });
        args.AddRange(new[] { "-f", "f32le", "-ar", AudioBlock.Rate.ToString(), "-ac", "2", "-probesize", "32", "-analyzeduration", "0", "-i", audioInput });
        if (videoInput != null && profile.Format == RecordingProfile.FormatDirect)
        {
            // 1.6.0 : le direct (YouTube) : débit constant, une image clé toutes les 2 s (exigé par YouTube), AAC 48 kHz stéréo, FLV vers RTMP(S)
            string k = profile.VideoKbps.ToString(), g = (2 * profile.Fps).ToString();
            args.AddRange(new[] { "-map", "0:v:0", "-map", "1:a:0", "-c:v", encoder, "-b:v", k + "k", "-maxrate", k + "k", "-bufsize", 2 * profile.VideoKbps + "k",
                "-g", g, "-keyint_min", g, "-pix_fmt", "yuv420p" });
            args.AddRange(encoder switch
            {
                "libx264" => new[] { "-preset", "veryfast", "-profile:v", "high", "-sc_threshold", "0" },
                "h264_nvenc" => new[] { "-preset", "p5", "-rc", "cbr", "-profile:v", "high", "-forced-idr", "1" },
                "h264_qsv" => new[] { "-preset", "veryfast", "-profile:v", "high" },
                "h264_amf" => new[] { "-rc", "cbr", "-profile:v", "high" },
                _ => Array.Empty<string>(),
            });
            args.AddRange(new[] { "-c:a", "aac", "-b:a", profile.AudioKbps + "k", "-ar", AudioBlock.Rate.ToString(), "-ac", "2", "-f", "flv", "-flvflags", "no_duration_filesize" });
            args.Add(output); return args;
        }
        if (videoInput != null)
        {
            args.AddRange(new[] { "-map", "0:v:0", "-map", "1:a:0", "-c:v", encoder, "-b:v", profile.VideoKbps + "k", "-pix_fmt", encoder == "mjpeg" ? "yuvj420p" : "yuv420p", "-c:a", "aac", "-b:a", profile.AudioKbps + "k", "-cluster_time_limit", "1000", "-f", "matroska" });
            if (encoder == "libx264") args.AddRange(new[] { "-preset", "veryfast" });
        }
        else args.AddRange(profile.Format switch
        {
            // son seul, léger : MP3 (lisible partout, WhatsApp compris) ; M4A fragmenté si l'encodeur MP3 manque (lisible même après une coupure)
            "mp3" => new[] { "-c:a", "libmp3lame", "-b:a", profile.AudioKbps + "k", "-f", "mp3" },
            "m4a" => new[] { "-c:a", "aac", "-b:a", profile.AudioKbps + "k", "-movflags", "+frag_keyframe+empty_moov+default_base_moof", "-f", "mp4" },
            _ => new[] { "-c:a", "flac", "-f", "flac" },
        });
        args.Add(output); return args;
    }
    public async Task PrepareAsync()
    {
        worker = Task.Run(WriteAsync);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
    /// <summary>
    /// Un bloc de 10 ms (stéréo entrelacée) et l'instant où sa première trame a été captée (horloge <see cref="Horloge"/>, ns) ;
    /// 0 : le bloc suit le précédent sans interruption (le premier est daté de son arrivée).
    /// </summary>
    public bool Accept(ReadOnlySpan<float> audio, long instant = 0)
    {
        if (audio.Length != AudioBlock.Samples) throw new ArgumentException($"Un bloc stéréo de {AudioBlock.Frames} trames est requis.");
        lock (gate)
        {
            if (finished || error != null) return false;
            if (count == Slots) { error = "L’encodeur ne suit plus : tampon d’enregistrement plein. Le fichier est en cours de finalisation."; finished = true; available.Release(); return false; }
            long avant = Interlocked.Read(ref accepted);
            if (Volatile.Read(ref origine) == long.MinValue)
            {
                premierBloc = instant != 0 ? instant : Horloge.Ns();
                Volatile.Write(ref origine, premierBloc - (long)primeFrames * Horloge.Seconde / AudioBlock.Rate);
            }
            else if (instant != 0)
            {
                // le son d'OBS est continu ; un saut de son horloge (plus de 30 ms) recale la vidéo sur le son
                long attendu = premierBloc + avant * Horloge.Seconde / AudioBlock.Rate, saut = instant - attendu;
                if (Math.Abs(saut) > 30 * Horloge.Milliseconde)
                {
                    premierBloc += saut;
                    Volatile.Write(ref origine, origine + saut);
                    Interlocked.Increment(ref sauts);
                }
            }
            audio.CopyTo(blocks[(read + count) % Slots]); count++; Interlocked.Add(ref accepted, audio.Length / 2); available.Release(); return true;
        }
    }
    public void Fail(string message) { error = message; Finish(); }
    public void Finish() { lock (gate) { if (finished) return; finished = true; available.Release(); } }
    public async Task StopAsync()
    {
        Finish();
        if (worker != null)
        {
            try { await worker.WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (TimeoutException) { error = "Finalisation trop longue. Le fichier partiel est conservé."; cancel.Cancel(); Kill(); await worker.WaitAsync(TimeSpan.FromSeconds(5)); }
        }
        if (error != null) throw new IOException(error);
    }
    async Task WriteAsync()
    {
        var block = new float[AudioBlock.Samples]; var bytes = new byte[AudioBlock.Samples * 4]; PcmRecordingWriter? wav = null;
        try
        {
            if (audioPipe == null) { wav = new(Path); ready.TrySetResult(); }
            else
            {
                var args = FfmpegArguments(profile, PipePath(audioPipe), videoPipe == null ? null : PipePath(videoPipe), Encoder, Path);
                var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                foreach (var a in args) info.ArgumentList.Add(a);
                ffmpeg = Process.Start(info) ?? throw new IOException("FFmpeg n’a pas démarré.");
                stderrWorker = Task.Run(async () => { string? line; while ((line = await ffmpeg.StandardError.ReadLineAsync()) != null) lock (stderr) { if (stderr.Length > 12000) stderr.Remove(0, 6000); stderr.AppendLine(line); } });
                if (videoPipe != null)
                {
                    videoWorker = new Thread(EcrireVideo) { IsBackground = true, Name = "MyChurch vidéo", Priority = ThreadPriority.AboveNormal };
                    videoWorker.Start();
                }
                await audioPipe.WaitForConnectionAsync(cancel.Token);
                // A silent priming interval lets FFmpeg open both pipes before the allow-list is activated.
                if (primeFrames > 0) await audioPipe.WriteAsync(new byte[primeFrames * 8], cancel.Token);
                ready.TrySetResult();
            }
            long lastDiskCheck = 0;
            while (true)
            {
                await available.WaitAsync(cancel.Token);
                bool got;
                lock (gate)
                {
                    got = count > 0;
                    if (got) { blocks[read].CopyTo(block, 0); read = (read + 1) % Slots; count--; }
                    else if (finished) break;
                }
                if (!got) continue;
                if (wav != null) wav.Write(block);
                else { Buffer.BlockCopy(block, 0, bytes, 0, bytes.Length); await audioPipe!.WriteAsync(bytes, cancel.Token); }
                if (Environment.TickCount64 - lastDiskCheck > 1000)
                {
                    lastDiskCheck = Environment.TickCount64;
                    if (!Direct && RecordingFiles.FreeSpace(profile.Directory) < RecordingFiles.ReserveBytes) { Fail("Espace disque faible : arrêt et finalisation du fichier."); }
                    if (ffmpeg?.HasExited == true) throw new IOException("FFmpeg s’est arrêté : " + stderr);
                }
            }
            audioDone = true;
            wav?.Dispose(); wav = null;
            audioPipe?.Dispose();
            if (videoWorker != null) await videoFin.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (ffmpeg != null)
            {
                await ffmpeg.WaitForExitAsync(cancel.Token).WaitAsync(TimeSpan.FromSeconds(15));
                if (stderrWorker != null) await stderrWorker;
                if (ffmpeg.ExitCode != 0) throw new IOException("Encodage : " + stderr);
            }
        }
        catch (Exception e) { error ??= e.Message; ready.TrySetException(e); Kill(); }
        finally { audioDone = true; wav?.Dispose(); audioPipe?.Dispose(); videoPipe?.Dispose(); lock (gate) finished = true; }
    }
    /// <summary>
    /// Le fil vidéo : la n-ième image est celle du monde à l'instant « origine + n / ips » (l'instant du son à la même place),
    /// demandée à la composition dès qu'elle est prête (cet instant + le retard des caméras), à cadence exacte.
    /// Les images composées attendent dans une file qu'un autre fil les écrive pour ffmpeg : quand ffmpeg retient la vidéo
    /// le temps que le son (en retard) la rattrape, la composition, elle, reste à l'heure.
    /// </summary>
    void EcrireVideo()
    {
        bool precision = false;
        Thread? ecrivain = null;
        int size = profile.Width * profile.Height * 4;
        // au plus ~256 Mo d'images en attente (2 s en 720p) : au-delà, ffmpeg ne suit plus
        int maxFile = Math.Clamp((int)(256L * 1024 * 1024 / size), 8, profile.Fps * 4);
        try
        {
            videoPipe!.WaitForConnection();
            ecrivain = new Thread(() => EcrireImages(size)) { IsBackground = true, Name = "MyChurch vidéo (ffmpeg)", Priority = ThreadPriority.AboveNormal };
            ecrivain.Start();
            // une première image tout de suite : ffmpeg ouvre alors le tube du son
            Composer(Horloge.Ns() - compositor!.Retard, size);
            while (Volatile.Read(ref origine) == long.MinValue)
            {
                if (cancel.IsCancellationRequested || Volatile.Read(ref finished) || audioDone) return;
                Thread.Sleep(2);
            }
            precision = Minuterie(true);
            long n = 1;
            while (!cancel.IsCancellationRequested)
            {
                // lu avant le compte : une fois l'arrêt vu, plus aucun bloc ne s'ajoute, la fin du son est définitive
                bool fini = Volatile.Read(ref finished) || audioDone;
                long depart = Volatile.Read(ref origine);
                long monde = depart + n * Horloge.Seconde / profile.Fps;
                if (fini && monde >= depart + (primeFrames + Interlocked.Read(ref accepted)) * Horloge.Seconde / AudioBlock.Rate) break;
                long quand = monde + Math.Clamp(compositor.Retard, 0, 2 * Horloge.Seconde);
                if (!fini && Horloge.Ns() < quand) { Attendre(quand); continue; }
                if (!fini && (Horloge.Ns() - quand > 2 * Horloge.Seconde || fileImages.Count > maxFile))
                {
                    Fail("L’encodeur vidéo ne suit plus le son. Arrêt pour préserver la synchronisation.");
                    break;
                }
                Composer(monde, size);
                n++;
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            if (!cancel.IsCancellationRequested && !Volatile.Read(ref finished)) error ??= "Vidéo : " + e.Message;
        }
        catch (Exception e) { error ??= "Vidéo : " + e.Message; }
        finally
        {
            if (precision) Minuterie(false);
            try { fileImages.CompleteAdding(); } catch (ObjectDisposedException) { }
            // l'écrivain finit d'écrire la file, puis ferme le tube (ffmpeg termine alors la vidéo)
            if (ecrivain == null) { try { videoPipe?.Dispose(); } catch (Exception) { } videoFin.TrySetResult(); }
        }
    }

    readonly System.Collections.Concurrent.BlockingCollection<byte[]> fileImages = new();
    readonly System.Collections.Concurrent.ConcurrentBag<byte[]> imagesLibres = new();

    /// <summary>Compose l'image de l'instant <paramref name="monde"/> et la met dans la file de l'écrivain.</summary>
    void Composer(long monde, int size)
    {
        if (!imagesLibres.TryTake(out var image) || image.Length != size) image = new byte[size];
        compositor!.CopyFrame(image, monde);
        fileImages.Add(image);
    }

    /// <summary>L'écrivain : les images de la file, dans l'ordre, vers ffmpeg (il peut attendre ffmpeg sans retarder la composition).</summary>
    void EcrireImages(int size)
    {
        try
        {
            foreach (var image in fileImages.GetConsumingEnumerable())
            {
                videoPipe!.Write(image, 0, size);
                Interlocked.Increment(ref imagesEcrites);
                imagesLibres.Add(image);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            if (!cancel.IsCancellationRequested && !Volatile.Read(ref finished)) error ??= "Vidéo : " + e.Message;
        }
        catch (Exception e) { error ??= "Vidéo : " + e.Message; }
        finally
        {
            try { videoPipe?.Dispose(); } catch (Exception) { }
            videoFin.TrySetResult();
        }
    }

    /// <summary>Attend l'instant voulu (à la milliseconde près), ou l'arrêt de l'enregistrement.</summary>
    void Attendre(long quand)
    {
        while (!cancel.IsCancellationRequested && !Volatile.Read(ref finished) && !audioDone)
        {
            long reste = quand - Horloge.Ns();
            if (reste <= 0) return;
            if (reste > 2 * Horloge.Milliseconde) Thread.Sleep((int)Math.Min(20, (reste - Horloge.Milliseconde) / Horloge.Milliseconde));
            else Thread.Yield();
        }
    }

    [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
    [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);
    /// <summary>Sous Windows, une minuterie à la milliseconde pendant l'enregistrement (sinon Thread.Sleep dure 15 ms et les images sautent).</summary>
    static bool Minuterie(bool active)
    {
        if (!OperatingSystem.IsWindows()) return active;
        try { if (active) timeBeginPeriod(1); else timeEndPeriod(1); return true; }
        catch (Exception) { return false; }
    }
    void Kill() { try { if (ffmpeg is { HasExited: false }) ffmpeg.Kill(true); } catch (Exception) { } }
    public void Dispose() { Finish(); cancel.Cancel(); Kill(); audioPipe?.Dispose(); videoPipe?.Dispose(); ffmpeg?.Dispose(); /* Workers own queue wait lifetime. */ }
}
