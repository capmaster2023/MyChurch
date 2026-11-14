namespace MyVerse.Audio;

/// <summary>
/// Le volume tel qu'on le montre : un curseur de 0 à 125 %, où 100 % est le volume normal (0 dB).
/// La courbe suit l'oreille : à 50 %, le son paraît deux fois moins fort (environ −18 dB), pas presque muet.
/// </summary>
public static class VolumeCurseur
{
    /// <summary>Position du « 100 % » sur le curseur (0 à 1).</summary>
    public const double Normal = 0.8;
    public const double DbMax = 6, DbMin = -60;

    /// <summary>Position du curseur (0 à 1) → volume en dB (−60 = silence, 0 = normal, +6 au bout).</summary>
    public static double Db(double position)
    {
        if (!double.IsFinite(position) || position <= 0.001) return DbMin;
        if (position >= 1) return DbMax;
        if (position > Normal) return (position - Normal) / (1 - Normal) * DbMax;
        return Math.Max(DbMin, 60 * Math.Log10(position / Normal));
    }

    /// <summary>Volume en dB → position du curseur (0 à 1).</summary>
    public static double Position(double db)
    {
        if (!double.IsFinite(db) || db <= DbMin) return 0;
        if (db >= DbMax) return 1;
        if (db > 0) return Normal + db / DbMax * (1 - Normal);
        return Normal * Math.Pow(10, db / 60);
    }

    /// <summary>Le volume en pourcentage affiché (100 = normal), de 0 à 125.</summary>
    public static int Pourcent(double db) => (int)Math.Round(Position(db) / Normal * 100);

    /// <summary>« 80 % », « 100 % », « Muet ».</summary>
    public static string Texte(double db) => db <= DbMin + 0.01 ? "Muet" : Pourcent(db) + " %";
}

/// <summary>Ce que dit la carte d'un son : on n'entend rien, c'est un peu faible, c'est bien, c'est trop fort.</summary>
public enum VerdictNiveau { Silence, Faible, Bon, TropFort }

/// <summary>
/// Suit le niveau d'un son (crêtes en dBFS, une vingtaine de fois par seconde) et en tire un verdict simple, sur les 3 dernières secondes :
/// trop fort dès que le son touche le plafond (il reste « trop fort » 2 s), bon entre −32 et −1 dBFS, faible en dessous, silence sous −50.
/// </summary>
public sealed class SuiviNiveau
{
    public const double Plafond = -1, SeuilBon = -32, SeuilSilence = -50;
    const long Fenetre = 3000, TenueTropFort = 2000;
    readonly Queue<(long T, double Db)> releves = new();
    long dernierTropFort = long.MinValue / 2;

    /// <summary>Un relevé : la crête (dBFS, −∞ = silence) à l'instant <paramref name="ms"/>.</summary>
    public void Ajouter(double dbfs, long ms)
    {
        if (double.IsNaN(dbfs)) dbfs = double.NegativeInfinity;
        releves.Enqueue((ms, dbfs));
        while (releves.Count > 0 && ms - releves.Peek().T > Fenetre) releves.Dequeue();
        if (dbfs >= Plafond) dernierTropFort = ms;
    }

    /// <summary>La plus forte crête des 3 dernières secondes.</summary>
    public double Maximum(long ms)
    {
        double m = double.NegativeInfinity;
        foreach (var (t, db) in releves) if (ms - t <= Fenetre && db > m) m = db;
        return m;
    }

    public VerdictNiveau Verdict(long ms)
    {
        if (ms - dernierTropFort <= TenueTropFort) return VerdictNiveau.TropFort;
        double m = Maximum(ms);
        return m < SeuilSilence ? VerdictNiveau.Silence : m < SeuilBon ? VerdictNiveau.Faible : VerdictNiveau.Bon;
    }

    public void Vider() { releves.Clear(); dernierTropFort = long.MinValue / 2; }
}

/// <summary>
/// La protection anti-saturation de chaque destination (enregistrement, Zoom, casque) : un limiteur doux juste sous le plafond.
/// Plusieurs sons forts additionnés ne « craquent » jamais : les crêtes sont ramenées sous −1 dBFS, puis le gain remonte en douceur.
/// </summary>
public sealed class ProtectionSaturation
{
    readonly float seuil;
    readonly float retour;
    float gain = 1;
    /// <summary>Le son a été retenu depuis le dernier appel à <see cref="Retenu"/> (pour un témoin « Trop fort »).</summary>
    volatile bool retenu;

    public ProtectionSaturation(int frequence = 48000, double seuilDb = -1, double retourMs = 120)
    {
        seuil = (float)Math.Pow(10, seuilDb / 20);
        retour = (float)(1 - Math.Exp(-1.0 / (retourMs / 1000.0 * Math.Max(8000, frequence))));
    }

    /// <summary>Traite un bloc stéréo entrelacé (gauche, droite, gauche…) sur place.</summary>
    public void Traiter(float[] stereo, int echantillons)
    {
        int n = Math.Min(echantillons, stereo.Length) & ~1;
        for (int i = 0; i < n; i += 2)
        {
            float l = stereo[i], r = stereo[i + 1];
            if (float.IsNaN(l) || float.IsInfinity(l)) l = 0;
            if (float.IsNaN(r) || float.IsInfinity(r)) r = 0;
            float crete = Math.Max(Math.Abs(l), Math.Abs(r));
            // le gain remonte doucement vers 1, mais jamais au point de laisser passer une crête au-dessus du seuil
            gain += (1 - gain) * retour;
            if (crete * gain > seuil) { gain = seuil / crete; retenu = true; }
            stereo[i] = Math.Clamp(l * gain, -1f, 1f);
            stereo[i + 1] = Math.Clamp(r * gain, -1f, 1f);
        }
    }

    /// <summary>Vrai si le son a été retenu depuis la dernière question.</summary>
    public bool Retenu() { bool r = retenu; retenu = false; return r; }
}
