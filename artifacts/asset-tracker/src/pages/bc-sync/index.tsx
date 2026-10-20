import { Layout } from "@/components/layout";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { useGetBcSyncStatus, useTriggerBcSync, getGetBcSyncStatusQueryKey } from "@workspace/api-client-react";
import { HardDrive, RefreshCw, CheckCircle2, AlertCircle, Clock, Hand, Info } from "lucide-react";
import { formatDate } from "@/lib/utils";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";

/**
 * Business Central integration — MVP decision: MANUAL SYNC ONLY against a mock
 * connection. Movements and gate-pass decisions since the last sync are shown
 * as "pending changes": exactly what an automatic Phase 2 sync would push.
 */
export default function BcSync() {
  const { data: status, refetch } = useGetBcSyncStatus({
    query: { refetchInterval: (query) => (query.state.data?.status === "syncing" ? 1000 : 5000), queryKey: getGetBcSyncStatusQueryKey() },
  });
  const triggerSync = useTriggerBcSync();
  const { toast } = useToast();

  const handleSync = () => {
    triggerSync.mutate(undefined, {
      onSuccess: () => {
        toast({ title: "Sync started", description: "Pushing pending changes to Business Central (mock)." });
        refetch();
      },
      onError: (err) => {
        toast({ title: "Failed to start sync", description: apiErrorMessage(err), variant: "destructive" });
      },
    });
  };

  const isSyncing = status?.status === "syncing" || triggerSync.isPending;
  const manual = (status?.mode ?? "manual") === "manual";

  return (
    <Layout>
      <div className="max-w-3xl mx-auto space-y-6">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">ERP Integration</h1>
        </div>

        <div className="flex items-start gap-3 rounded-lg border border-blue-200 dark:border-blue-900 bg-blue-50 dark:bg-blue-950/40 p-4 text-sm text-blue-900 dark:text-blue-100">
          <Info className="w-4 h-4 mt-0.5 flex-shrink-0" />
          <p>
            <strong>MVP scope:</strong> the Business Central connection is simulated and syncs are <strong>manual</strong>.
            Automatic sync on every movement needs the real BC API (OAuth2) and is planned for Phase 2 — the
            pending-changes count below is the queue such a sync would push.
          </p>
        </div>

        <Card>
          <CardHeader className="border-b bg-muted/20">
            <div className="flex justify-between items-start">
              <div className="space-y-1">
                <CardTitle className="flex items-center gap-2">
                  <HardDrive className="w-5 h-5 text-primary" />
                  Connection Status
                </CardTitle>
                <CardDescription>AssetIQ syncs registry records, locations and gate-pass transfers to Business Central.</CardDescription>
              </div>
              <Badge variant={status?.connected ? "success" : "destructive"}>
                {status?.connected ? "CONNECTED (MOCK)" : "DISCONNECTED"}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="p-6">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-8">
              <div className="space-y-6">
                <div>
                  <h3 className="text-sm font-medium text-muted-foreground mb-1 uppercase tracking-wider">Sync State</h3>
                  <div className="flex items-center gap-2">
                    {status?.status === "success" && <CheckCircle2 className="w-5 h-5 text-emerald-500" />}
                    {status?.status === "error" && <AlertCircle className="w-5 h-5 text-destructive" />}
                    {status?.status === "syncing" && <RefreshCw className="w-5 h-5 text-blue-500 animate-spin" />}
                    {status?.status === "idle" && <Clock className="w-5 h-5 text-muted-foreground" />}
                    <span className="font-semibold capitalize text-lg">{status?.status || "Unknown"}</span>
                  </div>
                  {status?.errorMessage && (
                    <p className="text-sm text-destructive mt-2 bg-destructive/10 p-2 rounded-md border border-destructive/20 font-mono">
                      {status.errorMessage}
                    </p>
                  )}
                </div>

                <div>
                  <h3 className="text-sm font-medium text-muted-foreground mb-1 uppercase tracking-wider">Pending Changes</h3>
                  <div className="text-3xl font-bold">{status?.pendingChanges ?? 0}</div>
                  <p className="text-sm text-muted-foreground">Movements and gate-pass decisions since the last sync</p>
                </div>

                <div>
                  <h3 className="text-sm font-medium text-muted-foreground mb-1 uppercase tracking-wider">Last Sync Volume</h3>
                  <div className="text-xl font-semibold">{status?.syncedAssets || 0}</div>
                  <p className="text-sm text-muted-foreground">Assets synchronized</p>
                </div>
              </div>

              <div className="space-y-6 md:border-l md:pl-8">
                <div>
                  <h3 className="text-sm font-medium text-muted-foreground mb-1 uppercase tracking-wider">Last Sync</h3>
                  <div className="font-mono text-base">
                    {status?.lastSyncAt ? formatDate(status.lastSyncAt) : "Never"}
                  </div>
                </div>
                <div>
                  <h3 className="text-sm font-medium text-muted-foreground mb-1 uppercase tracking-wider">Sync Mode</h3>
                  <div className="flex items-center gap-2 text-base">
                    <Hand className="w-4 h-4 text-muted-foreground" />
                    {manual ? "Manual only (MVP decision)" : "Automatic"}
                  </div>
                  <p className="text-xs text-muted-foreground mt-1">
                    {status?.nextSyncAt ? `Next scheduled: ${formatDate(status.nextSyncAt)}` : "No scheduled syncs — use the button below."}
                  </p>
                </div>
              </div>
            </div>

            <div className="mt-8 pt-6 border-t flex justify-end">
              <Button onClick={handleSync} disabled={!status?.connected || isSyncing} className="w-full sm:w-auto min-w-[200px]">
                {isSyncing ? (
                  <>
                    <RefreshCw className="w-4 h-4 mr-2 animate-spin" /> Sync in progress...
                  </>
                ) : (
                  <>
                    <RefreshCw className="w-4 h-4 mr-2" /> Sync Now
                  </>
                )}
              </Button>
            </div>
          </CardContent>
        </Card>
      </div>
    </Layout>
  );
}
