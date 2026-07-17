import { Layout } from "@/components/layout";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { useGetAsset, useGetAssetHistory, getGetAssetQueryKey, getGetAssetHistoryQueryKey } from "@workspace/api-client-react";
import { useParams } from "wouter";
import { formatCurrency, formatDate } from "@/lib/utils";
import {
  Activity, MapPin, HardDrive, History, User, Radio, ArrowRight,
  UserCheck, CornerDownLeft, Wrench, CheckCircle, ArrowRightLeft, MessageSquare, Building2,
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
      className="absolute -left-[9px] top-1 w-4 h-4 rounded-full border-2 flex-shrink-0"
      style={{ backgroundColor: "white", borderColor: color }}
    />
  );
}

function HistoryEvent({ event }: { event: any }) {
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

  // Lifecycle events
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
  const { data: asset, isLoading: loadingAsset } = useGetAsset(assetId, { query: { enabled: !!assetId, queryKey: getGetAssetQueryKey(assetId) } });
  const { data: history, isLoading: loadingHistory } = useGetAssetHistory(assetId, { query: { enabled: !!assetId, queryKey: getGetAssetHistoryQueryKey(assetId) } });

  if (loadingAsset) return <Layout><div className="animate-pulse h-64 bg-muted rounded-lg" /></Layout>;
  if (!asset) return <Layout><p className="text-muted-foreground">Asset not found.</p></Layout>;

  const statusColor = { active: "default", missing: "destructive", maintenance: "secondary", retired: "outline" }[asset.status] as any;

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
              {asset.sbuCode && (
                <SbuBadge code={asset.sbuCode} name={asset.sbuName} />
              )}
            </div>
          </div>
          <Badge variant={statusColor} className="text-sm flex-shrink-0">
            {asset.status.toUpperCase()}
          </Badge>
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
                  {asset.purchaseDate && (
                    <div>
                      <dt className="text-muted-foreground font-medium">Purchase Date</dt>
                      <dd className="font-mono mt-1">{asset.purchaseDate as string}</dd>
                    </div>
                  )}
                  {asset.notes && (
                    <div className="sm:col-span-2">
                      <dt className="text-muted-foreground font-medium">Notes</dt>
                      <dd className="mt-1 text-sm">{asset.notes}</dd>
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
                    {[1,2,3].map(i => <div key={i} className="h-12 bg-muted rounded" />)}
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
                  {asset.currentLocationName || <span className="italic text-muted-foreground text-base">Unknown Location</span>}
                </div>
                {asset.currentLocationId && (
                  <div className="mt-2 text-xs text-muted-foreground font-mono">LOC_ID: {asset.currentLocationId}</div>
                )}
              </CardContent>
            </Card>

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
                  <div className="mt-0.5">{asset.sbuCode ?? "—"} {asset.sbuName ? `(${asset.sbuName})` : ""}</div>
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
    </Layout>
  );
}
