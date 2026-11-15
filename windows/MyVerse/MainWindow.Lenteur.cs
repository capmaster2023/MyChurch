using System.Diagnostics;
using System.Windows.Threading;

namespace MyVerse.Ui;

/// <summary>
/// MyChurch 1.8.0 : ce qui ralentit l'interface est noté au journal (Réglages › Journal) : une page lente à s'afficher, ou
/// l'interface bloquée plus d'une demi-seconde, avec ce qui tourne à ce moment (caméras, tuiles, mémoire). De quoi trouver la
/// cause chez l'utilisateur, sans outil.
/// </summary>
public partial class MainWindow
{
    long derniereAlerte;

    void DebutPage(string page)
    {
        var chrono = Stopwatch.StartNew();
        int gc2 = GC.CollectionCount(2);
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            long ms = chrono.ElapsedMilliseconds;
            if (ms >= 400) Journal.Info($"lenteur : page « {page} » affichée en {ms} ms ({GC.CollectionCount(2) - gc2} nettoyage(s) de mémoire ; {Activite()})");
        }));
    }

    string Activite() =>
        $"tuiles en direct {lecteursTuiles.Count}, source à l'écran {App.Sources.Source?.Type ?? "aucune"}, MyStream {App.SourcesStream.Source?.Type ?? "aucune"}, "
        + $"mémoire {GC.GetTotalMemory(false) / 1048576} Mo, nettoyages complets {GC.CollectionCount(2)}";

    /// <summary>Un fil discret demande à l'interface de répondre toutes les secondes ; plus d'une demi-seconde d'attente : au journal.</summary>
    void SurveillerInterface()
    {
        var d = Dispatcher;
        var t = new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(1000);
                var chrono = Stopwatch.StartNew();
                try
                {
                    if (!d.InvokeAsync(() => { }, DispatcherPriority.Normal).Task.Wait(15000)) { }
                }
                catch (Exception) { return; }
                long ms = chrono.ElapsedMilliseconds;
                if (ms < 500 || Environment.TickCount64 - Volatile.Read(ref derniereAlerte) < 10000) continue;
                Volatile.Write(ref derniereAlerte, Environment.TickCount64);
                try { d.BeginInvoke(new Action(() => Journal.Info($"lenteur : interface bloquée {ms} ms (page « {onglet} » ; {Activite()})"))); }
                catch (Exception) { return; }
            }
        }) { IsBackground = true, Name = "surveillance de l'interface", Priority = ThreadPriority.BelowNormal };
        t.Start();
    }
}
