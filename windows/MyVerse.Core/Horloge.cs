using System.Diagnostics;

namespace MyVerse;

/// <summary>
/// L'horloge commune du son et de l'image (Siloé Stream 1.1.1), en nanosecondes : le compteur haute précision de Windows
/// (QueryPerformanceCounter), le même que celui d'OBS (os_gettime_ns). Les blocs de son d'OBS, les images MyCam
/// et les images de l'enregistrement sont datés avec elle : c'est ce qui les garde synchrones, comme dans OBS.
/// </summary>
public static class Horloge
{
    static readonly long frequence = Stopwatch.Frequency;

    /// <summary>Maintenant, en nanosecondes (origine quelconque, toujours croissante).</summary>
    public static long Ns() => (long)((Int128)Stopwatch.GetTimestamp() * 1_000_000_000 / frequence);

    public const long Milliseconde = 1_000_000;
    public const long Seconde = 1_000_000_000;
}
