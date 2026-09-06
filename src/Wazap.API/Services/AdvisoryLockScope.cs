using Microsoft.EntityFrameworkCore;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>
/// Verrou advisory PostgreSQL de SESSION (clé entière) : permet qu'un seul worker/instance exécute
/// une maintenance sur une fenêtre donnée, même avec plusieurs instances en parallèle
/// (outbox déjà multi-instance-safe via FOR UPDATE SKIP LOCKED ; ici on protège les purges).
/// La transaction maintenue ouverte garantit une connexion persistante (verrou de session).
/// </summary>
public sealed class AdvisoryLockScope : IAsyncDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly long _key;
    private bool _completed;

    private AdvisoryLockScope(ApplicationDbContext db, long key)
    {
        _db = db;
        _key = key;
    }

    public bool Acquired { get; private set; }

    /// <summary>Tente d'acquérir le verrou. Retourne un scope dont <see cref="Acquired"/> est false si occupé.</summary>
    public static async Task<AdvisoryLockScope> TryAcquireAsync(ApplicationDbContext db, long key, CancellationToken ct = default)
    {
        var scope = new AdvisoryLockScope(db, key);
        await db.Database.BeginTransactionAsync(ct);
        var acquired = await db.Database
            .SqlQueryRaw<bool>("SELECT pg_try_advisory_lock({0}) AS \"Value\"", key)
            .FirstAsync(ct);

        if (!acquired)
        {
            await db.Database.RollbackTransactionAsync(ct);
            return scope;
        }

        scope.Acquired = true;
        return scope;
    }

    /// <summary>
    /// Relâche le verrou puis valide la transaction. À appeler UNE FOIS après le travail.
    /// </summary>
    public async Task CompleteAsync(CancellationToken ct = default)
    {
        if (!Acquired || _completed)
            return;

        await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock({0})", _key);
        await _db.Database.CommitTransactionAsync(ct);
        Acquired = false;
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Acquired && !_completed)
        {
            // Annulation : la fermeture de connexion libère de toute façon le verrou de session.
            try { await _db.Database.RollbackTransactionAsync(); } catch { /* best-effort */ }
        }
    }
}
