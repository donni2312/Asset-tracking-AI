import { Layout } from "@/components/layout";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogDescription,
} from "@/components/ui/dialog";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent,
  AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import {
  useGetAsset, useGetAssetHistory, useUpdateAsset, useCreateAssetLifecycleEvent,
  getGetAssetQueryKey, getGetAssetHistoryQueryKey, useListInsights, useDismissInsight,
} from "@workspace/api-client-react";
import { Link, useParams } from "wouter";
import { formatCurrency, formatDate } from "@/lib/utils";
import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useToast } from "@/hooks/use-toast";
import { GatePassRequestDialog } from "@/components/gate-pass-request-dialog";
import {
  Activity, MapPin, HardDrive, History, User, Radio, ArrowRight,
  UserCheck, CornerDownLeft, Wrench, CheckCircle, ArrowRightLeft, MessageSquare,
  Building2, Trash2, UserPlus, LogOut, ShieldPlus, AlertTriangle, Info, Check, Move,
} from "lucide-react";

function SbuBadge({ code, name, color }: { code: string; name?: string | null; color?: string }) {
  const c = color ?? "#6B7280";
  return (
    <span
      className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-bold border"
      style={{ color: c, borderColor: c, backgroundColor: `${c}18` }}
    >
      <Building2 className="w-3 h-3" />
      {code}{name ? ` — ${name}` : ""}
    </span>
  );
}

function HistoryDot({ color }: { color: string }) {
  return (
    <span
      className="absolute -left-[9px] top-1 w-4 h-4 rounded-full border-2 bg-background flex-shrink-0"
      style={{ borderColor: color }}
    />
  );
}

function HistoryEvent({ event }: { event: any }) {
  if (event.kind === "movement" && event.triggeredBy === "gate_pass") {
    return (
      <div className="relative pl-6">
        <HistoryDot color="#9333ea" />
        <div className="flex flex-col sm:flex-row sm:justify-between sm:items-start gap-1">
          <div>
            <div className="flex items-center gap-2">
              <LogOut className="w-3.5 h-3.5 text-purple-600" />
              <p className="text-sm font-medium">Left the premises{event.gatePassId ? ` under gate pass #${event.gatePassId}` : ""}</p>
              <Badge className="text-[10px] bg-purple-100 text-purple-700 border-purple-200 border">GATE PASS</Badge>
            </div>
            {event.fromLocationName && <p className="text-xs text-muted-foreground mt-0.5 ml-5">Via: {event.fromLocationName}</p>}
            {event.movedBy && <p className="text-xs text-muted-foreground mt-0.5 ml-5">Carried by {event.movedBy}</p>}
            {event.notes && <p className="text-xs text-muted-foreground italic mt-0.5 ml-5">{event.notes}</p>}
          </div>
          <span className="text-xs font-mono text-muted-foreground sm:ml-4 flex-shrink-0">{formatDate(event.timestamp)}</span>
        </div>
      </div>
    );
  }

  if (event.kind === "movement") {
    const isRfid = event.triggeredBy === "rfid";
    return (
      <div className="relative pl-6">
        <HistoryDot color="#3b82f6" />
        <div className="flex flex-col sm:flex-row sm:justify-between sm:items-start gap-1">
          <div>
            <div className="flex items-center gap-2">
              {isRfid ? <Radio className="w-3.5 h-3.5 text-blue-500" /> : <ArrowRight className="w-3.5 h-3.5 text-blue-400" />}
              <p className="text-sm font-medium">
                {isRfid ? "RFID scan at" : "Moved to"} {event.toLocationName || "Unknown"}
              </p>
            </div>
            {event.fromLocationName && (
              <p className="text-xs text-muted-foreground mt-0.5 ml-5">From: {event.fromLocationName}</p>
            )}
            <div className="flex items-center gap-2 mt-1.5 ml-5">
              <Badge variant="secondary" className="text-[10px] uppercase">{event.triggeredBy}</Badge>
              {event.movedBy && <span className="text-xs text-muted-foreground">by {event.movedBy}</span>}
              {event.notes && <span className="text-xs text-muted-foreground italic truncate max-w-xs">{event.notes}</span>}
            </div>
          </div>
          <span className="text-xs font-mono text-muted-foreground sm:ml-4 flex-shrink-0">{formatDate(event.timestamp)}</span>
        </div>
      </div>
    );
  }

  const et = event.eventType;

  if (et === "deployment") {
    return (
      <div className="relative pl-6">
        <HistoryDot color="#22c55e" />
        <div className="flex flex-col sm:flex-row sm:justify-between sm:items-start gap-1">
          <div>
            <div className="flex items-center gap-2">
              <UserCheck className="w-3.5 h-3.5 text-green-500" />
              <p className="text-sm font-medium">Deployed to {event.assignedTo || "user"}</p>
              <Badge className="text-[10px] bg-green-100 text-green-700 border-green-200 border">DEPLOYED</Badge>
            </div>
            {event.toSbuName && <p className="text-xs text-muted-foreground mt-0.5 ml-5">SBU: {event.toSbuName}</p>}
            <p className="text-xs text-muted-foreground mt-0.5 ml-5">Performed by {event.performedBy}</p>
            {event.notes && <p className="text-xs text-muted-foreground italic mt-0.5 ml-5">{event.notes}</p>}
          </div>
          <span className="text-xs font-mono text-muted-foreground sm:ml-4 flex-shrink-0">{formatDate(event.timestamp)}</span>
        </div>
      </div>
    );
  }

  if (et === "return_to_it") {
    return (
      <div className="relative pl-6">
        <HistoryDot color="#3b82f6" />
        <div className="flex flex-col sm:flex-row sm:justify-between sm:items-start gap-1">
          <div>
            <div className="flex items-center gap-2">
              <CornerDownLeft className="w-3.5 h-3.5 text-blue-500" />
              <p className="text-sm font-medium">Returned to IT</p>
              <Badge className="text-[10px] bg-blue-100 text-blue-700 border-blue-200 border">RETURNED</Badge>
            </div>
            <p className="text-xs text-muted-foreground mt-0.5 ml-5">Performed by {event.performedBy}</p>
            {event.conditionBefore && <p className="text-xs text-muted-foreground mt-0.5 ml-5">Condition: {event.conditionBefore}</p>}
            {event.notes && <p className="text-xs text-muted-foreground italic mt-0.5 ml-5">{event.notes}</p>}
          </div>
          <span className="text-xs font-mono text-muted-foreground sm:ml-4 flex-shrink-0">{formatDate(event.timestamp)}</span>
        </div>
      </div>
    );
  }

  if (et === "repair_sent") {
    return (
      <div className="relative pl-6">
        <HistoryDot color="#f59e0b" />
        <div className="flex flex-col sm:flex-row sm:justify-between sm:items-start gap-1">
          <div>
            <div className="flex items-center gap-2">
              <Wrench className="w-3.5 h-3.5 text-amber-500" />
              <p className="text-sm font-medium">Sent for repair{event.vendor ? ` — ${event.vendor}` : ""}</p>
              <Badge className="text-[10px] bg-amber-100 text-amber-700 border-amber-200 border">REPAIR</Badge>
            </div>
            {event.ticketRef && <p className="text-xs text-muted-foreground mt-0.5 ml-5 font-mono">Ticket: {event.ticketRef}</p>}
            {event.conditionBefore && <p className="text-xs text-muted-foreground mt-0.5 ml-5">Condition: {event.conditionBefore}</p>}
            <p className="text-xs text-muted-foreground mt-0.5 ml-5">Initiated by {event.performedBy}</p>
            {event.notes && <p className="text-xs text-muted-foreground italic mt-0.5 ml-5">{event.notes}</p>}
          </div>
          <span className="text-xs font-mono text-muted-foreground sm:ml-4 flex-shrink-0">{formatDate(event.timestamp)}</span>
        </div>
      </div>
    );
  }

  if (et === "repair_returned") {
    return (
      <div className="relative pl-6">
        <HistoryDot color="#22c55e" />
        <div className="flex flex-col sm:flex-row sm:justify-between sm:items-start gap-1">
          <div>
            <div className="flex items-center gap-2">
              <CheckCircle className="w-3.5 h-3.5 text-green-500" />
              <p className="text-sm font-medium">Returned from repair{event.vendor ? ` (${event.vendor})` : ""}</p>
              <Badge className="text-[10px] bg-green-100 text-green-700 border-green-200 border">REPAIRED</Badge>
            </div>
            {event.conditionAfter && <p className="text-xs text-muted-foreground mt-0.5 ml-5">Condition after: {event.conditionAfter}</p>}
            <p className="text-xs text-muted-foreground mt-0.5 ml-5">Received by {event.performedBy}</p>
          </div>
          <span className="text-xs font-mono text-muted-foreground sm:ml-4 flex-shrink-0">{formatDate(event.timestamp)}</span>
        </div>
      </div>
    );
  }

  if (et === "sbu_transfer") {
    return (
      <div className="relative pl-6">
        <HistoryDot color="#7c3aed" />
        <div className="flex flex-col sm:flex-row sm:justify-between sm:items-start gap-1">
          <div>
            <div className="flex items-center gap-2 flex-wrap">
              <ArrowRightLeft className="w-3.5 h-3.5 text-purple-500" />
              <p className="text-sm font-medium">SBU Transfer</p>
              <Badge className="text-[10px] bg-purple-100 text-purple-700 border-purple-200 border">SBU TRANSFER</Badge>
            </div>
            <p className="text-xs text-muted-foreground mt-0.5 ml-5">
              {event.fromSbuName || "?"} → {event.toSbuName || "?"}
            </p>
            <p className="text-xs text-muted-foreground mt-0.5 ml-5">Authorised by {event.performedBy}</p>
            {event.notes && <p className="text-xs text-muted-foreground italic mt-0.5 ml-5">{event.notes}</p>}
          </div>
          <span className="text-xs font-mono text-muted-foreground sm:ml-4 flex-shrink-0">{formatDate(event.timestamp)}</span>
        </div>
      </div>
    );
  }

  if (et === "note") {
    return (
      <div className="relative pl-6">
        <HistoryDot color="#9ca3af" />
        <div className="flex flex-col sm:flex-row sm:justify-between sm:items-start gap-1">
          <div>
            <div className="flex items-center gap-2">
              <MessageSquare className="w-3.5 h-3.5 text-gray-400" />
              <p className="text-sm font-medium text-muted-foreground">Note</p>
            </div>
            {event.notes && (
              <div className="ml-5 mt-1 p-2 bg-muted/60 rounded text-xs text-foreground">{event.notes}</div>
            )}
            <p className="text-xs text-muted-foreground mt-0.5 ml-5">by {event.performedBy}</p>
          </div>
          <span className="text-xs font-mono text-muted-foreground sm:ml-4 flex-shrink-0">{formatDate(event.timestamp)}</span>
        </div>
      </div>
    );
  }

  return null;
}

export default function AssetDetail() {
  const { id } = useParams();
  const assetId = Number(id);
  const qc = useQueryClient();
  const { toast } = useToast();

  const { data: asset, isLoading: loadingAsset } = useGetAsset(assetId, {
    query: { enabled: !!assetId, queryKey: getGetAssetQueryKey(assetId) },
  });
  const { data: history, isLoading: loadingHistory } = useGetAssetHistory(assetId, {
    query: { enabled: !!assetId, queryKey: getGetAssetHistoryQueryKey(assetId) },
  });

  const updateAsset = useUpdateAsset();
  const createLifecycle = useCreateAssetLifecycleEvent();
  const { data: insights } = useListInsights();
  const dismissInsight = useDismissInsight();
  const [gatePassOpen, setGatePassOpen] = useState(false);
  const activeAlerts = (insights ?? []).filter((i) => i.assetId === assetId && !i.dismissed);

  // Assign dialog state
  const [assignOpen, setAssignOpen] = useState(false);
  const [employeeName, setEmployeeName] = useState("");
  const [performedBy, setPerformedBy] = useState("");
  const [assignNotes, setAssignNotes] = useState("");

  // Dispose dialog state
  const [disposeOpen, setDisposeOpen] = useState(false);
  const [disposeBy, setDisposeBy] = useState("");
  const [disposeReason, setDisposeReason] = useState("");

  function invalidate() {
    qc.invalidateQueries({ queryKey: getGetAssetQueryKey(assetId) });
    qc.invalidateQueries({ queryKey: getGetAssetHistoryQueryKey(assetId) });
  }

  function handleAssign() {
    if (!employeeName.trim() || !performedBy.trim()) return;
    updateAsset.mutate(
      { id: assetId, data: { assignedTo: employeeName.trim(), status: "active" } },
      {
        onSuccess: () => {
          createLifecycle.mutate(
            {
              id: assetId,
              data: {
                eventType: "deployment",
                performedBy: performedBy.trim(),
                assignedTo: employeeName.trim(),
                notes: assignNotes.trim() || undefined,
              },
            },
            {
              onSuccess: () => {
                toast({ title: "Asset assigned", description: `Assigned to ${employeeName.trim()}.` });
                setAssignOpen(false);
                setEmployeeName("");
                setPerformedBy("");
                setAssignNotes("");
                invalidate();
              },
            }
          );
        },
        onError: () => toast({ title: "Assignment failed", variant: "destructive" }),
      }
    );
  }

  function handleDispose() {
    if (!disposeBy.trim()) return;
    updateAsset.mutate(
      { id: assetId, data: { status: "retired", assignedTo: null } },
      {
        onSuccess: () => {
          createLifecycle.mutate(
            {
              id: assetId,
              data: {
                eventType: "note",
                performedBy: disposeBy.trim(),
                notes: `Asset disposed.${disposeReason.trim() ? ` Reason: ${disposeReason.trim()}` : ""}`,
              },
            },
            {
              onSuccess: () => {
                toast({ title: "Asset disposed", description: "Status set to Retired." });
                setDisposeOpen(false);
                setDisposeBy("");
                setDisposeReason("");
                invalidate();
              },
            }
          );
        },
        onError: () => toast({ title: "Disposal failed", variant: "destructive" }),
      }
    );
  }

  if (loadingAsset) return <Layout><div className="animate-pulse h-64 bg-muted rounded-lg" /></Layout>;
  if (!asset) return <Layout><p className="text-muted-foreground">Asset not found.</p></Layout>;

  const isRetired = asset.status === "retired";
  const statusColor = { active: "default", missing: "destructive", maintenance: "secondary", retired: "outline" }[asset.status] as any;
  const latestMovement = history?.find((e: any) => e.kind === "movement") as any;
  const offPremises = !asset.currentLocationId && latestMovement?.triggeredBy === "gate_pass";

  return (
    <Layout>
      <div className="space-y-6">
        {/* Header */}
        <div className="flex items-start justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">{asset.name}</h1>
            <div className="flex items-center gap-2 mt-1.5 flex-wrap">
              <span className="text-muted-foreground text-sm flex items-center gap-1">
                <HardDrive className="w-4 h-4" />{asset.category}
              </span>
              {(asset as any).sbuCode && (
                <SbuBadge code={(asset as any).sbuCode} name={(asset as any).sbuName} />
              )}
            </div>
          </div>
          <div className="flex items-center gap-2 flex-shrink-0">
            <Badge variant={statusColor} className="text-sm">{asset.status.toUpperCase()}</Badge>
            {!isRetired && (
              <>
                <Link
                  href={`/movements/new?assetId=${asset.id}`}
                  className="inline-flex items-center gap-2 h-8 rounded-md border border-input bg-background px-3 text-xs font-medium shadow-sm hover:bg-accent"
                >
                  <Move className="w-4 h-4" />
                  Move
                </Link>
                <Button size="sm" variant="outline" className="gap-2" onClick={() => setGatePassOpen(true)}>
                  <ShieldPlus className="w-4 h-4" />
                  Request Gate Pass
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  className="gap-2"
                  onClick={() => setAssignOpen(true)}
                >
                  <UserPlus className="w-4 h-4" />
                  Assign
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  className="gap-2 text-destructive border-destructive/40 hover:bg-destructive/10"
                  onClick={() => setDisposeOpen(true)}
                >
                  <Trash2 className="w-4 h-4" />
                  Dispose
                </Button>
              </>
            )}
          </div>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
          {/* Left: Identity + History */}
          <div className="md:col-span-2 space-y-6">
            <Card>
              <CardHeader>
                <CardTitle className="text-lg">Identity & Details</CardTitle>
              </CardHeader>
              <CardContent>
                <dl className="grid grid-cols-1 sm:grid-cols-2 gap-4 text-sm">
                  <div>
                    <dt className="text-muted-foreground font-medium">Serial Number</dt>
                    <dd className="font-mono mt-1">{asset.serialNumber}</dd>
                  </div>
                  <div>
                    <dt className="text-muted-foreground font-medium">RFID Tag</dt>
                    <dd className="font-mono mt-1">{asset.rfidTag}</dd>
                  </div>
                  <div>
                    <dt className="text-muted-foreground font-medium">Purchase Value</dt>
                    <dd className="font-mono mt-1">{formatCurrency(asset.purchaseValue)}</dd>
                  </div>
                  <div>
                    <dt className="text-muted-foreground font-medium">Assigned To</dt>
                    <dd className="mt-1 flex items-center gap-2">
                      <User className="w-4 h-4 text-muted-foreground" />
                      {asset.assignedTo || <span className="italic text-muted-foreground">Unassigned</span>}
                    </dd>
                  </div>
                  {(asset as any).purchaseDate && (
                    <div>
                      <dt className="text-muted-foreground font-medium">Purchase Date</dt>
                      <dd className="font-mono mt-1">{(asset as any).purchaseDate as string}</dd>
                    </div>
                  )}
                  {(asset as any).notes && (
                    <div className="sm:col-span-2">
                      <dt className="text-muted-foreground font-medium">Notes</dt>
                      <dd className="mt-1 text-sm">{(asset as any).notes}</dd>
                    </div>
                  )}
                </dl>
              </CardContent>
            </Card>

            {/* Combined History Timeline */}
            <Card>
              <CardHeader>
                <CardTitle className="text-lg flex items-center gap-2">
                  <History className="w-5 h-5" />
                  Asset History
                  {history && history.length > 0 && (
                    <Badge variant="secondary" className="ml-auto">{history.length} events</Badge>
                  )}
                </CardTitle>
              </CardHeader>
              <CardContent>
                {loadingHistory ? (
                  <div className="animate-pulse space-y-4">
                    {[1, 2, 3].map(i => <div key={i} className="h-12 bg-muted rounded" />)}
                  </div>
                ) : !history || history.length === 0 ? (
                  <p className="text-sm text-muted-foreground">No history recorded yet.</p>
                ) : (
                  <div className="relative border-l-2 border-muted ml-3 space-y-5 pb-4">
                    {history.map((event) => (
                      <HistoryEvent key={(event as any).id} event={event} />
                    ))}
                  </div>
                )}
              </CardContent>
            </Card>
          </div>

          {/* Right: Location + System data */}
          <div className="space-y-6">
            <Card className="bg-primary/5 border-primary/20">
              <CardHeader>
                <CardTitle className="text-lg flex items-center gap-2">
                  <MapPin className="w-5 h-5 text-primary" />
                  Current Location
                </CardTitle>
              </CardHeader>
              <CardContent>
                <div className="text-xl font-semibold">
                  {asset.currentLocationName ? (
                    asset.currentLocationName
                  ) : offPremises ? (
                    <span className="flex items-center gap-2 text-purple-700"><LogOut className="w-5 h-5" /> Off-premises</span>
                  ) : (
                    <span className="italic text-muted-foreground text-base">Unknown Location</span>
                  )}
                </div>
                {offPremises && latestMovement && (
                  <div className="mt-2 text-xs text-muted-foreground">
                    Left {formatDate(latestMovement.timestamp)}{latestMovement.gatePassId ? ` under gate pass #${latestMovement.gatePassId}` : ""}
                  </div>
                )}
                {asset.currentLocationId && (
                  <div className="mt-2 text-xs text-muted-foreground font-mono">LOC_ID: {asset.currentLocationId}</div>
                )}
              </CardContent>
            </Card>

            {activeAlerts.length > 0 && (
              <Card className="border-destructive/30">
                <CardHeader className="pb-3">
                  <CardTitle className="text-lg flex items-center gap-2">
                    <AlertTriangle className="w-5 h-5 text-destructive" />
                    Active Alerts
                    <Badge variant="secondary" className="ml-auto">{activeAlerts.length}</Badge>
                  </CardTitle>
                </CardHeader>
                <CardContent className="space-y-2">
                  {activeAlerts.map((alert) => (
                    <div
                      key={alert.id}
                      className={`rounded-md border p-2.5 text-xs flex items-start gap-2 ${
                        alert.severity === "critical" ? "border-destructive/40 bg-destructive/5" :
                        alert.severity === "warning" ? "border-amber-400/50 bg-amber-50" : "border-blue-300/50 bg-blue-50"
                      }`}
                    >
                      {alert.severity === "info"
                        ? <Info className="w-3.5 h-3.5 mt-0.5 text-blue-600 flex-shrink-0" />
                        : <AlertTriangle className={`w-3.5 h-3.5 mt-0.5 flex-shrink-0 ${alert.severity === "critical" ? "text-destructive" : "text-amber-600"}`} />}
                      <div className="flex-1 min-w-0">
                        <div className="font-semibold uppercase tracking-wide text-[10px] mb-0.5">{alert.severity} · {alert.type.replace(/_/g, " ")}</div>
                        <div>{alert.message}</div>
                        <div className="text-muted-foreground mt-0.5">{formatDate(alert.createdAt)}</div>
                      </div>
                      <button
                        type="button"
                        className="p-1 rounded hover:bg-background/80 text-muted-foreground"
                        title="Dismiss alert"
                        aria-label="Dismiss alert"
                        onClick={() => dismissInsight.mutate({ id: alert.id }, { onSuccess: () => qc.invalidateQueries() })}
                      >
                        <Check className="w-3.5 h-3.5" />
                      </button>
                    </div>
                  ))}
                  <Link href="/insights" className="text-xs text-primary hover:underline">All AI insights →</Link>
                </CardContent>
              </Card>
            )}

            <Card>
              <CardHeader>
                <CardTitle className="text-lg">System Data</CardTitle>
              </CardHeader>
              <CardContent className="space-y-3 text-sm">
                <div>
                  <div className="text-xs text-muted-foreground font-medium">Asset ID</div>
                  <div className="font-mono mt-0.5">#{asset.id}</div>
                </div>
                <div>
                  <div className="text-xs text-muted-foreground font-medium">SBU</div>
                  <div className="mt-0.5">{(asset as any).sbuCode ?? "—"} {(asset as any).sbuName ? `(${(asset as any).sbuName})` : ""}</div>
                </div>
                <div>
                  <div className="text-xs text-muted-foreground font-medium">Registered</div>
                  <div className="font-mono mt-0.5 text-xs">{formatDate(asset.createdAt)}</div>
                </div>
                {asset.updatedAt && (
                  <div>
                    <div className="text-xs text-muted-foreground font-medium">Last Updated</div>
                    <div className="font-mono mt-0.5 text-xs">{formatDate(asset.updatedAt)}</div>
                  </div>
                )}
              </CardContent>
            </Card>
          </div>
        </div>
      </div>

      <GatePassRequestDialog open={gatePassOpen} onOpenChange={setGatePassOpen} assetId={asset.id} />

      {/* ── Assign to Employee Dialog ── */}
      <Dialog open={assignOpen} onOpenChange={setAssignOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <UserPlus className="w-5 h-5 text-green-600" />
              Assign Asset to Employee
            </DialogTitle>
            <DialogDescription>
              This will update the asset's assigned employee and record a deployment event in the history.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4 py-2">
            <div className="space-y-1.5">
              <Label htmlFor="employeeName">Employee Name <span className="text-destructive">*</span></Label>
              <Input
                id="employeeName"
                placeholder="e.g. Tendai Moyo"
                value={employeeName}
                onChange={e => setEmployeeName(e.target.value)}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="performedBy">Authorised By <span className="text-destructive">*</span></Label>
              <Input
                id="performedBy"
                placeholder="e.g. IT Manager name"
                value={performedBy}
                onChange={e => setPerformedBy(e.target.value)}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="assignNotes">Notes (optional)</Label>
              <Input
                id="assignNotes"
                placeholder="e.g. Issued for remote work"
                value={assignNotes}
                onChange={e => setAssignNotes(e.target.value)}
              />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setAssignOpen(false)}>Cancel</Button>
            <Button
              onClick={handleAssign}
              disabled={!employeeName.trim() || !performedBy.trim() || updateAsset.isPending || createLifecycle.isPending}
              className="gap-2"
            >
              <UserCheck className="w-4 h-4" />
              {updateAsset.isPending || createLifecycle.isPending ? "Assigning…" : "Assign Asset"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* ── Dispose Asset Alert Dialog ── */}
      <AlertDialog open={disposeOpen} onOpenChange={setDisposeOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle className="flex items-center gap-2 text-destructive">
              <Trash2 className="w-5 h-5" />
              Dispose Asset
            </AlertDialogTitle>
            <AlertDialogDescription>
              This will permanently mark <strong>{asset.name}</strong> as <strong>Retired</strong> and remove it from active inventory. A disposal record will be added to the asset history. This action cannot be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <div className="space-y-4 py-2">
            <div className="space-y-1.5">
              <Label htmlFor="disposeBy">Authorised By <span className="text-destructive">*</span></Label>
              <Input
                id="disposeBy"
                placeholder="e.g. IT Manager name"
                value={disposeBy}
                onChange={e => setDisposeBy(e.target.value)}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="disposeReason">Reason (optional)</Label>
              <Input
                id="disposeReason"
                placeholder="e.g. End of life, damaged beyond repair"
                value={disposeReason}
                onChange={e => setDisposeReason(e.target.value)}
              />
            </div>
          </div>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
              onClick={handleDispose}
              disabled={!disposeBy.trim() || updateAsset.isPending || createLifecycle.isPending}
            >
              {updateAsset.isPending || createLifecycle.isPending ? "Disposing…" : "Confirm Disposal"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Layout>
  );
}
