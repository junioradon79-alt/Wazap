using System.Diagnostics;

namespace Wazap.API.Health;

/// <summary>
/// Politique de surveillance des workers « cœur métier » : nom attendu + délai maximal sans
/// battement avant de considérer le worker comme bloqué/mort. Utilisé par le watchdog
/// (<see cref="Services.OutboxBackgroundWorker"/>) et les métriques.
/// </summary>
public static class WorkerLagPolicy
{
    public static readonly (string Name, TimeSpan MaxIdle)[] MandatoryWorkers =
    {
        ("OutboxBackgroundWorker", TimeSpan.FromMinutes(2)),
        ("DeliveryOfferWorker", TimeSpan.FromMinutes(2)),
        ("LocationPurgeWorker", TimeSpan.FromHours(3))
    };

    /// <summary>
    /// Retourne la liste des workers obligatoires dont le dernier battement est trop ancien
    /// (ou absent après le délai de grâce du démarrage).
    /// </summary>
    public static IReadOnlyList<string> EvaluateStale(DateTime nowUtc, IReadOnlyList<WorkerBeat> beats)
    {
        var byName = beats.ToDictionary(b => b.Name, StringComparer.Ordinal);
        var processStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        var stale = new List<string>();

        foreach (var (name, maxIdle) in MandatoryWorkers)
        {
            if (byName.TryGetValue(name, out var beat))
            {
                if (nowUtc - beat.LastBeatUtc > maxIdle)
                    stale.Add($"{name} (pas de cycle depuis {(nowUtc - beat.LastBeatUtc).TotalSeconds:0}s)");
            }
            else if (nowUtc - processStart > maxIdle)
            {
                stale.Add($"{name} (aucun battement depuis le démarrage)");
            }
        }

        return stale;
    }
}
