using Microsoft.EntityFrameworkCore;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>
/// Verrou advisory PostgreSQL de TRANSACTION : permet qu'un seul worker exécute une
/// maintenance donnée, même avec plusieurs instances en parallèle (l'outbox est déjà
/// multi-instance-safe via <c>FOR UPDATE SKIP LOCKED</c> ; ici on protège les purges, la
/// réconciliation et l'onboarding).
/// <para>
/// Le verrou est pris avec <c>pg_try_advisory_xact_lock</c>, donc <b>libéré automatiquement</b>
/// par PostgreSQL à la validation ou à l'annulation de la transaction. C'est ce qui remplace
/// l'ancien couple <c>pg_advisory_lock</c> / <c>pg_advisory_unlock</c>, qui présentait deux
/// défauts : le verrou était relâché <b>avant</b> le commit (une autre instance pouvait
/// démarrer la même maintenance sans voir les écritures non encore validées) et, sur erreur,
/// il n'était jamais relâché explicitement — un verrou de session rendu au pool de connexions
/// pouvait rester actif et bloquer le worker <b>définitivement</b>, en silence.
/// </para>
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
    public static async Task<AdvisoryLockScope> TryAcquireAsync(
        ApplicationDbContext db, long key, CancellationToken ct = default)
    {
        var scope = new AdvisoryLockScope(db, key);
        await db.Database.BeginTransactionAsync(ct);
        var acquired = await db.Database
            .SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock({0}) AS \"Value\"", key)
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
    /// Valide la transaction, ce qui LIBÈRE le verrou (il est de portée transactionnelle).
    /// À appeler UNE FOIS après le travail.
    /// </summary>
    public async Task CompleteAsync(CancellationToken ct = default)
    {
        if (!Acquired || _completed)
            return;

        await _db.Database.CommitTransactionAsync(ct);
        Acquired = false;
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Acquired && !_completed)
        {
            // L'annulation (rollback) libère le verrou côté PostgreSQL : aucune fuite possible,
            // même si la connexion retourne ensuite au pool.
            try { await _db.Database.RollbackTransactionAsync(); } catch { /* best-effort */ }
        }
    }
}
