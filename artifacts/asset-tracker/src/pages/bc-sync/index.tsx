import { Layout } from "@/components/layout";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { useGetBcSyncStatus, useTriggerBcSync, getGetBcSyncStatusQueryKey } from "@workspace/api-client-react";
import { HardDrive, RefreshCw, CheckCircle2, AlertCircle, Clock } from "lucide-react";
import { formatDate } from "@/lib/utils";
import { useToast } from "@/hooks/use-toast";

export default function BcSync() {
  const { data: status, refetch } = useGetBcSyncStatus({ query: { refetchInterval: 5000, queryKey: getGetBcSyncStatusQueryKey() } });
  const triggerSync = useTriggerBcSync();
  const { toast } = useToast();

  const handleSync = () => {
    triggerSync.mutate({}, {
      onSuccess: () => {
        toast({ title: "Sync triggered", description: "Data synchronization with Business Central has started." });
        refetch();
      },
      onError: () => {
        toast({ title: "Failed to start sync", variant: "destructive" });
      }
    });
  };

  const isSyncing = status?.status === 'syncing' || triggerSync.isPending;

  return (
    <Layout>
      <div className="max-w-3xl mx-auto space-y-6">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">ERP Integration</h1>
          <p className="text-muted-foreground text-sm">Microsoft Dynamics 365 Business Central synchronization.</p>
        </div>

        <Card>
          <CardHeader className="border-b bg-muted/20">
            <div className="flex justify-between items-start">
              <div className="space-y-1">
                <CardTitle className="flex items-center gap-2">
                  <HardDrive className="w-5 h-5 text-primary" />
                  Connection Status
                </CardTitle>
                <CardDescription>AssetIQ is configured to sync financial data and registry records.</CardDescription>
              </div>
              <Badge variant={status?.connected ? 'success' : 'destructive'}>
                {status?.connected ? 'CONNECTED' : 'DISCONNECTED'}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="p-6">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-8">
              <div className="space-y-6">
                <div>
                  <h3 className="text-sm font-medium text-muted-foreground mb-1 uppercase tracking-wider">Sync State</h3>
                  <div className="flex items-center gap-2">
                    {status?.status === 'success' && <CheckCircle2 className="w-5 h-5 text-emerald-500" />}
                    {status?.status === 'error' && <AlertCircle className="w-5 h-5 text-destructive" />}
                    {status?.status === 'syncing' && <RefreshCw className="w-5 h-5 text-blue-500 animate-spin" />}
                    {status?.status === 'idle' && <Clock className="w-5 h-5 text-muted-foreground" />}
                    <span className="font-semibold capitalize text-lg">{status?.status || 'Unknown'}</span>
                  </div>
                  {status?.errorMessage && (
                    <p className="text-sm text-destructive mt-2 bg-destructive/10 p-2 rounded-md border border-destructive/20 font-mono">
                      {status.errorMessage}
                    </p>
                  )}
                </div>
                
                <div>
                  <h3 className="text-sm font-medium text-muted-foreground mb-1 uppercase tracking-wider">Metrics</h3>
                  <div className="text-3xl font-mono font-bold">
                    {status?.syncedAssets || 0}
                  </div>
                  <p className="text-sm text-muted-foreground">Assets synchronized</p>
                </div>
              </div>

              <div className="space-y-6 border-l pl-8">
                <div>
                  <h3 className="text-sm font-medium text-muted-foreground mb-1 uppercase tracking-wider">Last Sync</h3>
                  <div className="font-mono text-base">
                    {status?.lastSyncAt ? formatDate(status.lastSyncAt) : 'Never'}
                  </div>
                </div>
                <div>
                  <h3 className="text-sm font-medium text-muted-foreground mb-1 uppercase tracking-wider">Next Scheduled Sync</h3>
                  <div className="font-mono text-base text-muted-foreground">
                    {status?.nextSyncAt ? formatDate(status.nextSyncAt) : '—'}
                  </div>
                </div>
              </div>
            </div>

            <div className="mt-8 pt-6 border-t flex justify-end">
              <Button 
                onClick={handleSync} 
                disabled={!status?.connected || isSyncing}
                className="w-full sm:w-auto min-w-[200px]"
              >
                {isSyncing ? (
                  <>
                    <RefreshCw className="w-4 h-4 mr-2 animate-spin" /> Sync in progress...
                  </>
                ) : (
                  <>
                    <RefreshCw className="w-4 h-4 mr-2" /> Trigger Manual Sync
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
