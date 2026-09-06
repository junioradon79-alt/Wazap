using System.Collections.Concurrent;

namespace Wazap.API.Health;

/// <summary>
/// Registre en mémoire des battements de cœur des workers hébergés (par processus).
/// Chaque worker appelle <see cref="Beat"/> à la fin de chaque cycle réussi et
/// <see cref="Fail"/> quand un cycle échoue. Le health check « workers » compare
/// l'ancienneté du dernier battement au délai maximal attendu pour détecter un
/// worker bloqué, mort ou en boucle d'erreurs.
/// </summary>
public static class WorkerHeartbeats
{
    private sealed record Entry(DateTime LastBeatUtc, string? LastError);

    private static readonly ConcurrentDictionary<string, Entry> Entries = new();

    /// <summary>Enregistre un battement de cœur réussi pour le worker <paramref name="name"/>.</summary>
    public static void Beat(string name)
        => Entries[name] = new Entry(DateTime.UtcNow, LastError: null);

    /// <summary>Enregistre un cycle en échec (le message est exposé dans /health).</summary>
    public static void Fail(string name, string? error)
        => Entries[name] = new Entry(DateTime.UtcNow, LastError: error);

    /// <summary>Instantané lisible par le health check (nom, dernier battement, dernière erreur).</summary>
    public static IReadOnlyList<WorkerBeat> Snapshot()
        => Entries
            .Select(kv => new WorkerBeat(kv.Key, kv.Value.LastBeatUtc, kv.Value.LastError))
            .OrderBy(b => b.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>Purge le registre (tests uniquement).</summary>
    public static void Reset() => Entries.Clear();
}

/// <summary>État observé d'un worker (instantané immuable).</summary>
public sealed record WorkerBeat(string Name, DateTime LastBeatUtc, string? LastError);
