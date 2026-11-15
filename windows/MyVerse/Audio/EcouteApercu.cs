using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MyVerse.Ui.Audio;

/// <summary>
/// L'écoute de l'assistant « Ajouter un son » : Siloé Stream écoute chaque micro et chaque carte son (en partage, comme Windows),
/// et mesure chaque entrée à part. « Parlez dans le micro : la barre qui bouge, c'est le bon. » Rien n'est enregistré ni envoyé.
/// </summary>
public sealed class EcouteApercu : IDisposable
{
    static readonly Guid FloatIeee = new("00000003-0000-0010-8000-00aa00389b71");
    static readonly Guid Pcm = new("00000001-0000-0010-8000-00aa00389b71");

    sealed class Ecoute
    {
        public WasapiCapture? Capture;
        public MMDevice? Appareil;
        /// <summary>Crête de chaque entrée depuis le dernier relevé (0 à 1), et celle du dernier relevé.</summary>
        public float[] Cretes = Array.Empty<float>(), Releve = Array.Empty<float>();
        public int Canaux;
        public string Erreur = "";
    }

    readonly Dictionary<string, Ecoute> ecoutes = new();
    readonly object verrou = new();
    bool fini;

    /// <summary>Commence à écouter un appareil (sans effet s'il est déjà écouté). Une erreur est gardée et dite par <see cref="Erreur"/>.</summary>
    public void Ecouter(string id)
    {
        lock (verrou) { if (fini || ecoutes.ContainsKey(id)) return; ecoutes[id] = new Ecoute(); }
        var e = ecoutes[id];
        try
        {
            using var enumerateur = new MMDeviceEnumerator();
            e.Appareil = id == "default" ? enumerateur.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console) : enumerateur.GetDevice(id);
            var capture = new WasapiCapture(e.Appareil, false, 50);
            e.Canaux = capture.WaveFormat.Channels;
            e.Cretes = new float[Math.Max(1, e.Canaux)];
            e.Releve = new float[e.Cretes.Length];
            capture.DataAvailable += (_, a) => Mesurer(e, capture.WaveFormat, a.Buffer, a.BytesRecorded);
            capture.RecordingStopped += (_, a) => { if (a.Exception != null) e.Erreur = a.Exception.Message; };
            e.Capture = capture;
            capture.StartRecording();
        }
        catch (Exception x)
        {
            e.Erreur = x.Message;
            Journal.Info("écoute de l'assistant : " + id + " : " + x.Message);
        }
    }

    static void Mesurer(Ecoute e, WaveFormat f, byte[] buffer, int octets)
    {
        try
        {
            int canaux = f.Channels, taille = f.BitsPerSample / 8;
            if (canaux <= 0 || taille <= 0 || octets <= 0) return;
            bool flottant = f.Encoding == WaveFormatEncoding.IeeeFloat || (f is WaveFormatExtensible x && x.SubFormat == FloatIeee);
            bool entier = f.Encoding == WaveFormatEncoding.Pcm || (f is WaveFormatExtensible y && y.SubFormat == Pcm);
            if (!flottant && !entier) return;
            int frames = octets / (canaux * taille);
            var cretes = e.Cretes;
            for (int k = 0; k < canaux && k < cretes.Length; k++)
            {
                float m = 0;
                for (int i = 0; i < frames; i++)
                {
                    int o = (i * canaux + k) * taille;
                    float v = flottant ? (taille == 4 ? BitConverter.ToSingle(buffer, o) : (float)BitConverter.ToDouble(buffer, o)) : taille switch
                    {
                        2 => BitConverter.ToInt16(buffer, o) / 32768f,
                        3 => ((buffer[o] << 8 | buffer[o + 1] << 16 | buffer[o + 2] << 24) >> 8) / 8388608f,
                        4 => BitConverter.ToInt32(buffer, o) / 2147483648f,
                        _ => 0,
                    };
                    v = Math.Abs(v);
                    if (v > m) m = v;
                }
                if (m > cretes[k]) cretes[k] = m;
            }
        }
        catch (Exception) { }
    }

    /// <summary>Un relevé (une fois par rafraîchissement de l'écran) : les crêtes depuis le relevé précédent.</summary>
    public void Relever()
    {
        List<Ecoute> toutes;
        lock (verrou) toutes = ecoutes.Values.ToList();
        foreach (var e in toutes)
            for (int k = 0; k < e.Cretes.Length && k < e.Releve.Length; k++) { e.Releve[k] = e.Cretes[k]; e.Cretes[k] = 0; }
    }

    /// <summary>La crête d'une entrée au dernier relevé, en dBFS (−∞ = silence) ; <paramref name="entree"/> 0 = la plus forte de toutes.</summary>
    public double Crete(string id, int entree)
    {
        Ecoute? e;
        lock (verrou) ecoutes.TryGetValue(id, out e);
        if (e == null || e.Releve.Length == 0) return double.NegativeInfinity;
        float m = 0;
        if (entree <= 0) foreach (float v in e.Releve) m = Math.Max(m, v);
        else if (entree <= e.Releve.Length) m = e.Releve[entree - 1];
        return m <= 1e-6f ? double.NegativeInfinity : 20 * Math.Log10(m);
    }

    /// <summary>Le nombre d'entrées de l'appareil écouté (0 s'il ne s'ouvre pas).</summary>
    public int Entrees(string id) { lock (verrou) return ecoutes.TryGetValue(id, out var e) ? e.Canaux : 0; }
    public string Erreur(string id) { lock (verrou) return ecoutes.TryGetValue(id, out var e) ? e.Erreur : ""; }

    public void Dispose()
    {
        List<Ecoute> toutes;
        lock (verrou) { fini = true; toutes = ecoutes.Values.ToList(); ecoutes.Clear(); }
        foreach (var e in toutes)
        {
            try { e.Capture?.StopRecording(); } catch (Exception) { }
            try { e.Capture?.Dispose(); } catch (Exception) { }
            try { e.Appareil?.Dispose(); } catch (Exception) { }
        }
    }
}
