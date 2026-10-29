namespace AssetIQ.Api.Services.Approvals;

/// <summary>Every five minutes: reminders, then escalation, for approval steps nobody has answered (Phase 4); overdue gate returns (4c).</summary>
public sealed class ApprovalWorker(IServiceScopeFactory scopes, ILogger<ApprovalWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var sent = await scope.ServiceProvider.GetRequiredService<ApprovalService>().ProcessDueAsync(stoppingToken);
                if (sent > 0) logger.LogInformation("Approval reminders / escalations sent: {Count}", sent);
                // Phase 4c (GI-06): items not back through the gate by their return date.
                var overdue = await scope.ServiceProvider.GetRequiredService<GateService>().FlagOverdueAsync(stoppingToken);
                if (overdue > 0) logger.LogInformation("Overdue gate returns flagged: {Count}", overdue);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Approval reminders failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
