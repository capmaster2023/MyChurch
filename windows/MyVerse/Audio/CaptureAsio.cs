using MyVerse.Audio;
using NAudio.Wave;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Capture ASIO (MyChurch 1.1), comme le plugin ASIO d'OBS : un pilote (Focusrite USB ASIO, UMC ASIO Driver…), un format
/// (Mono ou Stéréo) et, pour chaque canal OBS, l'entrée du pilote qu'il reçoit (ou Muet). MyChurch ouvre le pilote une seule fois,
/// toutes entrées, et donne à chaque source ses canaux ; volume, filtres, écoute, vumètres et pistes restent ceux d'OBS.
/// Windows ne charge qu'un pilote ASIO à la fois : toutes les sources ASIO doivent utiliser le même.
/// </summary>
public sealed class CaptureAsio : IDisposable
{
    readonly Func<string, float[], float[]?, int, int, bool> pousser;
    readonly object verrou = new();
    AsioOut? asio;
    string pilote = "";
    int entreesPilote, frequence;
    volatile (string Id, int[] Canaux)[] bandes = Array.Empty<(string, int[])>();
    float[] entrelace = Array.Empty<float>();
    float[][] canaux = Array.Empty<float[]>();
    float[] silence = Array.Empty<float>();
    string statut = "";
    bool reinitialiser;

    /// <param name="pousser">Envoie le son d'une source à OBS : (source, gauche, droite ou null, nombre, fréquence).</param>
    public CaptureAsio(Func<string, float[], float[]?, int, int, bool> pousser) => this.pousser = pousser;

    /// <summary>Les pilotes ASIO installés (liste « Device » du plugin ASIO d'OBS).</summary>
    public static List<string> Pilotes()
    {
        // relu au plus toutes les 2 s (la liste sert aussi à « Actif / Inactif » à chaque rafraîchissement)
        if (Environment.TickCount64 - lu > 2000)
        {
            try { cache = AsioOut.GetDriverNames().Where(n => !string.IsNullOrWhiteSpace(n)).ToList(); }
            catch (Exception) { cache = new(); }
            lu = Environment.TickCount64;
        }
        return cache.ToList();
    }
    static List<string> cache = new();
    static long lu = long.MinValue / 2;

    /// <summary>Le pilote ouvert par MyChurch (vide si aucun).</summary>
    public string PiloteOuvert { get { lock (verrou) return asio != null ? pilote : ""; } }

    /// <summary>Problème du pilote d'une source (vide si tout va bien).</summary>
    public string Statut(string nom)
    {
        lock (verrou)
        {
            if (asio != null && pilote != nom) return $"Un seul pilote ASIO à la fois : « {pilote} » est déjà utilisé.";
            return pilote == nom ? statut : "";
        }
    }

    /// <summary>Les entrées du pilote : leurs noms (« Input 1 », « Mic/Inst 1 »…). Liste vide si le pilote ne s'ouvre pas.</summary>
    public List<string> Entrees(string nom)
    {
        var noms = new List<string>();
        if (string.IsNullOrEmpty(nom)) return noms;
        lock (verrou)
        {
            if (asio != null && pilote == nom) return Noms(asio);
            if (asio != null) return noms; // un autre pilote est ouvert : Windows n'en charge qu'un
        }
        try
        {
            using var temporaire = new AsioOut(nom);
            return Noms(temporaire);
        }
        catch (Exception x) { Journal.Erreur("ASIO : entrées de " + nom, x); return noms; }
    }

    static List<string> Noms(AsioOut a)
    {
        var noms = new List<string>();
        int n = Math.Min(a.DriverInputChannelCount, AudioConfiguration.MaxEntrees);
        for (int i = 0; i < n; i++)
        {
            string s;
            try { s = a.AsioInputChannelName(i); } catch (Exception) { s = ""; }
            noms.Add(string.IsNullOrWhiteSpace(s) ? $"Input {i + 1}" : s.Trim());
        }
        return noms;
    }

    /// <summary>« Control Panel » du plugin ASIO d'OBS : le panneau du fabricant (fréquence, taille du tampon…).</summary>
    public void PanneauConfiguration(string nom)
    {
        if (string.IsNullOrEmpty(nom)) return;
        lock (verrou)
            if (asio != null && pilote == nom) { asio.ShowControlPanel(); return; }
        using var temporaire = new AsioOut(nom);
        temporaire.ShowControlPanel();
    }

    /// <summary>Ouvre, met à jour ou ferme le pilote d'après les sources ASIO du mélangeur. Sur le fil de l'interface.</summary>
    public void Apply(IEnumerable<AudioSource> sources)
    {
        var asios = sources.Where(s => s.Kind == AudioInputKind.Asio && !string.IsNullOrEmpty(s.DeviceId) && s.DeviceId != "default").ToList();
        // le pilote déjà ouvert reste s'il sert encore, sinon celui de la première source ASIO
        string voulu = asios.Any(s => s.DeviceId == PiloteOuvert) ? PiloteOuvert : asios.FirstOrDefault()?.DeviceId ?? "";
        bandes = asios.Where(s => s.DeviceId == voulu).Select(s => (s.Id, s.AsioChannels.ToArray())).ToArray();
        lock (verrou)
        {
            if (asio != null && pilote == voulu && !reinitialiser) return;
            Fermer();
            if (voulu.Length == 0) return;
            pilote = voulu;
            reinitialiser = false;
            try
            {
                var a = new AsioOut(voulu);
                entreesPilote = a.DriverInputChannelCount;
                if (entreesPilote <= 0) { a.Dispose(); statut = "Ce pilote ASIO n'a aucune entrée."; return; }
                // la fréquence du moteur (48 kHz, comme OBS) si le pilote l'accepte ; OBS rééchantillonne sinon
                frequence = new[] { 48000, 44100, 96000, 88200 }.FirstOrDefault(f => { try { return a.IsSampleRateSupported(f); } catch (Exception) { return false; } });
                if (frequence == 0) frequence = 48000;
                a.InputChannelOffset = 0;
                a.InitRecordAndPlayback(null, entreesPilote, frequence);
                a.AudioAvailable += Recevoir;
                a.DriverResetRequest += (_, _) => Reinitialiser();
                a.Play();
                asio = a;
                statut = "";
                Journal.Info($"ASIO : {voulu}, {entreesPilote} entrées à {frequence} Hz");
            }
            catch (Exception x)
            {
                Journal.Erreur("ASIO : ouverture de " + voulu, x);
                Fermer();
                pilote = voulu;
                statut = "Pilote ASIO indisponible : " + x.Message + " (déjà utilisé par OBS ou un autre logiciel ?)";
            }
        }
    }

    /// <summary>Le pilote demande à être rouvert (fréquence ou tampon changés dans son panneau) : on le rouvre sur le fil de l'interface.</summary>
    void Reinitialiser()
    {
        lock (verrou) reinitialiser = true;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { try { App.Audio.Apply(false); } catch (Exception e) { Journal.Erreur("ASIO : réouverture", e); } });
    }

    /// <summary>Un tampon du pilote, sur son fil : chaque source reçoit ses canaux (gauche, et droite en stéréo).</summary>
    void Recevoir(object? _, AsioAudioAvailableEventArgs e)
    {
        try
        {
            int n = e.SamplesPerBuffer, c = e.InputBuffers.Length;
            if (n <= 0 || c <= 0) return;
            if (entrelace.Length < n * c) entrelace = new float[n * c];
            if (canaux.Length < c || canaux[0].Length < n) canaux = Enumerable.Range(0, c).Select(_ => new float[n]).ToArray();
            if (silence.Length < n) silence = new float[n];
            e.GetAsInterleavedSamples(entrelace);
            var liste = bandes;
            var faits = new bool[c];
            foreach (var (id, map) in liste)
            {
                float[] Canal(int k)
                {
                    if (k < 0 || k >= c) return silence;
                    if (!faits[k])
                    {
                        var d = canaux[k];
                        for (int i = 0; i < n; i++) d[i] = entrelace[i * c + k];
                        faits[k] = true;
                    }
                    return canaux[k];
                }
                var gauche = Canal(map.Length > 0 ? map[0] : -1);
                var droite = map.Length > 1 ? Canal(map[1]) : null;
                pousser(id, gauche, droite, n, frequence);
            }
        }
        catch (Exception) { }
    }

    void Fermer()
    {
        var a = asio;
        asio = null;
        pilote = "";
        statut = "";
        if (a == null) return;
        try { a.AudioAvailable -= Recevoir; a.Stop(); } catch (Exception) { }
        try { a.Dispose(); } catch (Exception x) { Journal.Erreur("ASIO : fermeture", x); }
    }

    public void Dispose() { lock (verrou) Fermer(); }
}
