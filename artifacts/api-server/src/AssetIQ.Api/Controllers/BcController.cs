using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetIQ.Api.Controllers;

/// <summary>Business Central — MVP decision: syncs are manual only (see docs/project-plan.md).</summary>
public sealed class BcController(AssetIqDbContext db, BcSyncService sync) : ApiControllerBase
{
    [HttpGet("bc/sync-status")]
    [RequireRole(Roles.BcViewers)]
    [FailureMessage("Failed to get BC sync status")]
    public Task<BcSyncStatusDto> Status() => sync.GetStatusAsync(db, Aborted);

    [HttpPost("bc/sync")]
    [RequireRole(Roles.BcOperators)]
    [FailureMessage("BC sync failed")]
    public Task<BcSyncStatusDto> Trigger() => sync.TriggerAsync(db, Aborted);
}
