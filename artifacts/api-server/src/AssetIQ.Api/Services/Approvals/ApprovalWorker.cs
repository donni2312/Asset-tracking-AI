namespace AssetIQ.Api.Services.Approvals;

/// <summary>Every five minutes: reminders, then escalation, for approval steps nobody has answered (Phase 4); overdue gate returns (4c); fleet reminders (4d).</summary>
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
                // Phase 4d (AM-03, AM-04): document expiry at 30 / 7 days and overdue services.
                var fleet = await scope.ServiceProvider.GetRequiredService<FleetService>().SendRemindersAsync(stoppingToken);
                if (fleet > 0) logger.LogInformation("Fleet reminders sent: {Count}", fleet);
                // GI-03: warranty expiry alerts.
                var warranty = await scope.ServiceProvider.GetRequiredService<ItReportService>().WarrantyAlertsAsync(stoppingToken);
                if (warranty > 0) logger.LogInformation("Warranty alerts raised: {Count}", warranty);
                // Phase 5a (SF-09): CAPEX budget thresholds.
                var capex = await scope.ServiceProvider.GetRequiredService<FinancePlanningService>().CapexAlertsAsync(stoppingToken);
                if (capex > 0) logger.LogInformation("CAPEX alerts sent: {Count}", capex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Approval reminders failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
