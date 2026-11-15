using MyVerse.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Une sortie du son vers un appareil de l'ordinateur : un envoi (vers « CABLE Input » de VB-CABLE, que Zoom, OBS ou Teams
/// reçoivent comme un micro « CABLE Output », ou vers une autre sortie), ou le casque de la régie.
/// Elle recopie la piste d'OBS que Siloé Stream lui donne, protégée contre la saturation. Siloé Stream ne mélange rien ici.
/// </summary>
public sealed class SortieSon : IDisposable
{
    sealed class Lecteur : IWaveProvider
    {
        readonly float[] anneau = new float[AudioBlock.Rate * 2]; // 0,5 s stéréo
        readonly float[] bloc = new float[AudioBlock.Samples];
        readonly ProtectionSaturation protection = new(AudioBlock.Rate);
        int lecture, quantite;
        readonly object verrou = new();
        float[] tampon = new float[4096];
        public float Crete;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(AudioBlock.Rate, 2);
        /// <summary>Un bloc de la piste (partagé avec les autres destinations) : recopié, protégé, puis mis en attente.</summary>
        public void Ecrire(float[] source)
        {
            if (source.Length != bloc.Length) return;
            lock (verrou)
            {
                Array.Copy(source, bloc, bloc.Length);
                protection.Traiter(bloc, bloc.Length);
                foreach (float v in bloc)
                {
                    if (quantite == anneau.Length) { lecture = (lecture + 1) % anneau.Length; quantite--; }
                    anneau[(lecture + quantite) % anneau.Length] = v; quantite++;
                }
                // horloges différentes (OBS et la carte) : au-delà de 200 ms d'avance, on revient à 60 ms
                if (quantite > AudioBlock.Rate * 2 * 2 / 10) { int trop = quantite - AudioBlock.Rate * 2 * 6 / 100; trop -= trop % 2; lecture = (lecture + trop) % anneau.Length; quantite -= trop; }
            }
        }
        public int Read(byte[] buffer, int offset, int count)
        {
            int n = count / 4;
            if (tampon.Length < n) tampon = new float[n];
            float c = 0;
            lock (verrou)
            {
                int dispo = Math.Min(n, quantite);
                for (int i = 0; i < dispo; i++) { tampon[i] = anneau[lecture]; lecture = (lecture + 1) % anneau.Length; }
                quantite -= dispo;
                Array.Clear(tampon, dispo, n - dispo);
            }
            for (int i = 0; i < n; i++) { float a = Math.Abs(tampon[i]); if (a > c) c = a; }
            Crete = Math.Max(Crete * .85f, c);
            Buffer.BlockCopy(tampon, 0, buffer, offset, n * 4);
            return count;
        }
    }

    readonly ChurchAudioService audio;
    readonly Lecteur lecteur = new();
    readonly Action<float[], long> consommateur;
    WasapiOut? sortie;
    MMDevice? peripherique;
    // null : rien d'ouvert ; sinon l'appareil voulu (vide = le câble audio virtuel installé)
    string? signature;
    int piste = -1;
    /// <summary>En mots simples : « Coupé », « En marche », « Il manque le câble audio virtuel »…</summary>
    public string Statut { get; private set; } = "Coupé";
    /// <summary>Le nom de l'appareil où le son sort (vide s'il ne sort pas).</summary>
    public string Appareil { get; private set; } = "";
    public bool Actif => sortie != null;
    /// <summary>Il manque le câble audio virtuel (VB-CABLE) pour cet envoi.</summary>
    public bool SansCable { get; private set; }

    public SortieSon(ChurchAudioService audio)
    {
        this.audio = audio;
        consommateur = (bloc, _) => lecteur.Ecrire(bloc);
    }

    /// <summary>Niveau envoyé en dBFS (−∞ = silence).</summary>
    public float Level => sortie == null || lecteur.Crete <= 1e-6f ? float.NegativeInfinity : (float)AudioMath.Db(lecteur.Crete);

    /// <summary>Force une nouvelle recherche du câble (après l'installation de VB-CABLE).</summary>
    public void Reset() => Fermer();

    /// <param name="appareil">Vide : le câble audio virtuel installé ; « default » : la sortie de Windows ; sinon un appareil.</param>
    public void Apply(bool actif, string appareil, int nouvellePiste)
    {
        string voulu = actif && audio.Running ? appareil ?? "" : "\u0000";
        if (voulu != signature)
        {
            Fermer();
            signature = voulu;
            SansCable = false;
            if (voulu == "\u0000")
            {
                Statut = !actif ? "Coupé" : "En attente du moteur du son";
                return;
            }
            try
            {
                using var enumerateur = new MMDeviceEnumerator();
                if (voulu.Length == 0)
                {
                    // le câble audio virtuel installé (VB-CABLE, Virtual Audio Driver, Virtual Cables)
                    var cable = VbCable.Entree();
                    if (cable == null) { SansCable = true; Statut = "Il manque le câble audio virtuel"; return; }
                    peripherique = enumerateur.GetDevice(cable.Value.Id);
                }
                else peripherique = voulu == "default" ? enumerateur.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerateur.GetDevice(voulu);
                Appareil = peripherique.FriendlyName;
                sortie = new WasapiOut(peripherique, AudioClientShareMode.Shared, true, 50);
                sortie.Init(lecteur);
                sortie.Play();
            }
            catch (Exception e)
            {
                Fermer(); signature = voulu;
                Statut = "Impossible d'ouvrir la sortie : " + e.Message;
                Journal.Erreur("sortie du son", e);
                return;
            }
        }
        if (sortie == null) return;
        if (piste != nouvellePiste - 1)
        {
            if (piste >= 0) audio.RemoveTrackConsumer(piste, consommateur);
            piste = nouvellePiste - 1;
            audio.AddTrackConsumer(piste, consommateur);
        }
        Statut = "En marche";
    }

    void Fermer()
    {
        if (piste >= 0) { audio.RemoveTrackConsumer(piste, consommateur); piste = -1; }
        try { sortie?.Stop(); } catch (Exception) { }
        sortie?.Dispose(); sortie = null;
        peripherique?.Dispose(); peripherique = null;
        signature = null;
        Appareil = "";
    }

    public void Dispose() => Fermer();
}

/// <summary>Les envois (4 au plus, actifs en même temps si besoin) et le casque de la régie.</summary>
public sealed class SortiesVirtuelles : IDisposable
{
    readonly ChurchAudioService audio;
    readonly Dictionary<string, SortieSon> sorties = new();
    public SortiesVirtuelles(ChurchAudioService audio) => this.audio = audio;

    SortieSon De(string id)
    {
        if (!sorties.TryGetValue(id, out var s)) sorties[id] = s = new SortieSon(audio);
        return s;
    }

    public void Apply(AudioConfiguration c, PlanAudio plan)
    {
        var voulues = c.VirtualSounds.Select(v => v.Id).Append(AudioConfiguration.IdCasque).ToHashSet();
        foreach (var id in sorties.Keys.Where(id => !voulues.Contains(id)).ToList())
        {
            sorties[id].Dispose();
            sorties.Remove(id);
        }
        foreach (var v in c.VirtualSounds) De(v.Id).Apply(v.Enabled, v.DeviceId, plan.PisteDe(v.Id));
        De(AudioConfiguration.IdCasque).Apply(c.Casque.Actif, c.Casque.Appareil, plan.PisteDe(AudioConfiguration.IdCasque));
    }

    public float Level(string id) => sorties.TryGetValue(id, out var s) ? s.Level : float.NegativeInfinity;
    public string Status(string id) => sorties.TryGetValue(id, out var s) ? s.Statut : "Coupé";
    public string Appareil(string id) => sorties.TryGetValue(id, out var s) ? s.Appareil : "";
    public bool Actif(string id) => sorties.TryGetValue(id, out var s) && s.Actif;
    public bool SansCable(string id) => sorties.TryGetValue(id, out var s) && s.SansCable;
    /// <summary>Force une nouvelle recherche des câbles (après l'installation de VB-CABLE).</summary>
    public void Reset() { foreach (var s in sorties.Values) s.Reset(); }
    public void Dispose() { foreach (var s in sorties.Values) s.Dispose(); sorties.Clear(); }
}
