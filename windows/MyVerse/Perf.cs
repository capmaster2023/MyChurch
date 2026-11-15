using System.Diagnostics;

namespace MyVerse.Ui;

/// <summary>
/// MyChurch 1.5.0 : le temps des opérations de l'interface (mise à jour de l'écran, vignettes, recherche, caméra virtuelle).
/// Le test automatique en fait le résumé ; une opération de plus de 300 ms est notée dans le journal (une fois par minute au plus),
/// pour retrouver une lenteur chez un utilisateur.
/// </summary>
public static class Perf
{
    sealed class Stat { public int N; public double Total, Max; public long DernierJournal; }
    static readonly Dictionary<string, Stat> stats = new();

    public static void Ajouter(string nom, double ms)
    {
        bool noter = false;
        lock (stats)
        {
            if (!stats.TryGetValue(nom, out var s)) stats[nom] = s = new Stat();
            s.N++; s.Total += ms; if (ms > s.Max) s.Max = ms;
            if (ms > 300 && Environment.TickCount64 - s.DernierJournal > 60_000) { s.DernierJournal = Environment.TickCount64; noter = true; }
        }
        if (noter) Journal.Info($"lenteur : {nom} a pris {ms:0} ms");
    }

    /// <summary>Mesure le bloc : <c>using (Perf.Chrono("MajEcran")) { … }</c>.</summary>
    public static Mesure Chrono(string nom) => new(nom, Stopwatch.GetTimestamp());

    public readonly struct Mesure : IDisposable
    {
        readonly string nom;
        readonly long debut;
        public Mesure(string nom, long debut) { this.nom = nom; this.debut = debut; }
        public void Dispose() => Ajouter(nom, Stopwatch.GetElapsedTime(debut).TotalMilliseconds);
    }

    public static void Vider() { lock (stats) stats.Clear(); }

    /// <summary>« MajEcran : 42 fois, 12 ms en moyenne, 30 ms au plus », une ligne par opération.</summary>
    public static string Resume()
    {
        lock (stats)
            return string.Join("\n", stats.OrderByDescending(x => x.Value.Total)
                .Select(x => $"  {x.Key} : {x.Value.N} fois, {x.Value.Total / Math.Max(1, x.Value.N):0.0} ms en moyenne, {x.Value.Max:0} ms au plus"));
    }
}
