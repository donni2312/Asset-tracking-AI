using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

/// <summary>
/// Business Central integration — MVP decision: MANUAL SYNC ONLY. The BC connection is a mock (no BC
/// tenant / OAuth2 in the MVP), so syncs are triggered from the BC Sync page. Movements and gate-pass
/// decisions recorded since the last sync are reported as pending changes — exactly the set an
/// automatic (Phase 2) sync would push. State is in memory and resets when the API restarts.
/// </summary>
public sealed class BcSyncService(AppClock clock)
{
    public static readonly TimeSpan DefaultSyncDuration = TimeSpan.FromSeconds(2);

    private sealed record SyncState(string Status, DateTime? LastSyncAt, int SyncedAssets, string? ErrorMessage);

    private static readonly SyncState Initial = new("idle", null, 0, null);

    private readonly object _gate = new();
    private SyncState _state = Initial;

    /// <summary>How long a simulated sync takes.</summary>
    public TimeSpan SyncDuration { get; set; } = DefaultSyncDuration;

    public async Task<BcSyncStatusDto> GetStatusAsync(AssetIqDbContext db, CancellationToken ct)
    {
        SyncState state;
        lock (_gate) state = _state;
        return new BcSyncStatusDto
        {
            Connected = true, // mock connection — see the BC Sync page
            Mode = "manual",
            Status = state.Status,
            LastSyncAt = state.LastSyncAt,
            SyncedAssets = state.SyncedAssets,
            ErrorMessage = state.ErrorMessage,
            NextSyncAt = null, // no scheduled syncs in the MVP
            PendingChanges = await CountPendingChangesAsync(db, state.LastSyncAt, ct),
        };
    }

    /// <summary>Start a simulated sync; it completes after <see cref="SyncDuration"/>. 409 while one is running.</summary>
    public async Task<BcSyncStatusDto> TriggerAsync(AssetIqDbContext db, CancellationToken ct)
    {
        SyncState previous;
        lock (_gate)
        {
            if (_state.Status == "syncing") throw new ApiException(StatusCodes.Status409Conflict, "A Business Central sync is already in progress");
            previous = _state;
            _state = _state with { Status = "syncing", ErrorMessage = null };
        }

        int total;
        try
        {
            total = await db.Assets.CountAsync(ct);
        }
        catch
        {
            lock (_gate) _state = previous;
            throw;
        }

        // Changes recorded while the sync is running stay pending for the next sync.
        var snapshotAt = clock.UtcNow;
        _ = CompleteAfterDelayAsync(snapshotAt, total);
        return await GetStatusAsync(db, ct);
    }

    /// <summary>Movements plus gate-pass decisions recorded after <paramref name="since"/> (everything when never synced).</summary>
    public static async Task<int> CountPendingChangesAsync(AssetIqDbContext db, DateTime? since, CancellationToken ct)
    {
        var movements = since is null
            ? await db.Movements.CountAsync(ct)
            : await db.Movements.CountAsync(m => m.Timestamp > since, ct);
        var decisions = since is null
            ? await db.GatePasses.CountAsync(p => p.DecidedAt != null, ct)
            : await db.GatePasses.CountAsync(p => p.DecidedAt != null && p.DecidedAt > since, ct);
        return movements + decisions;
    }

    /// <summary>Testing seam: forget any sync state.</summary>
    public void Reset()
    {
        lock (_gate) _state = Initial;
    }

    private async Task CompleteAfterDelayAsync(DateTime snapshotAt, int total)
    {
        await Task.Delay(SyncDuration, clock.TimeProvider);
        lock (_gate) _state = new SyncState("success", snapshotAt, total, null);
    }
}
