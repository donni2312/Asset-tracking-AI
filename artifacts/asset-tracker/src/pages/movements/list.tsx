import { Layout } from "@/components/layout";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Badge } from "@/components/ui/badge";
import { useListMovements, type MovementEvent } from "@workspace/api-client-react";
import { formatDate, cn } from "@/lib/utils";
import { Activity, ArrowRight, LogOut, Plus, Radio, ShieldCheck, UserRound } from "lucide-react";
import { Link } from "wouter";

const TRIGGERS: Record<string, { label: string; className: string; icon: typeof Radio }> = {
  rfid: { label: "RFID", className: "border-blue-200 bg-blue-50 text-blue-700", icon: Radio },
  manual: { label: "Manual", className: "border-slate-200 bg-slate-50 text-slate-700", icon: UserRound },
  gate_pass: { label: "Gate pass", className: "border-purple-200 bg-purple-50 text-purple-700", icon: ShieldCheck },
};

function Destination({ event }: { event: MovementEvent }) {
  if (event.toLocationName) return <>{event.toLocationName}</>;
  if (event.triggeredBy === "gate_pass") {
    return (
      <span className="inline-flex items-center gap-1 text-purple-700">
        <LogOut className="w-3 h-3" /> Off-premises
      </span>
    );
  }
  return <>Unknown</>;
}

export default function MovementList() {
  const { data: movements, isLoading } = useListMovements({ limit: 100 });

  return (
    <Layout>
      <div className="space-y-6">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Movement Log</h1>
            <p className="text-muted-foreground text-sm">Audit trail of every asset movement — RFID scans, manual moves and gate-pass exits.</p>
          </div>
          <Link href="/movements/new" className="inline-flex items-center justify-center whitespace-nowrap rounded-md text-sm font-medium h-9 px-4 py-2 bg-primary text-primary-foreground shadow hover:bg-primary/90">
            <Plus className="w-4 h-4 mr-2" />Record Movement
          </Link>
        </div>

        <Card>
          <CardContent className="p-0">
            {isLoading ? (
              <div className="p-8 text-center text-muted-foreground animate-pulse">Loading movements...</div>
            ) : movements?.length === 0 ? (
              <div className="p-16 text-center">
                <Activity className="w-12 h-12 text-muted-foreground/30 mx-auto mb-4" />
                <h3 className="text-lg font-medium">No movements recorded</h3>
              </div>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Timestamp</TableHead>
                    <TableHead>Asset</TableHead>
                    <TableHead>Movement Route</TableHead>
                    <TableHead>Trigger</TableHead>
                    <TableHead>Actor</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {movements?.map((event) => {
                    const trigger = TRIGGERS[event.triggeredBy] ?? TRIGGERS.manual;
                    const TriggerIcon = trigger.icon;
                    return (
                      <TableRow key={event.id}>
                        <TableCell className="font-mono text-xs whitespace-nowrap">
                          {formatDate(event.timestamp)}
                        </TableCell>
                        <TableCell>
                          <Link href={`/assets/${event.assetId}`} className="font-medium hover:underline text-primary">
                            {event.assetName}
                          </Link>
                          {event.rfidTag && <div className="text-[10px] text-muted-foreground font-mono mt-0.5">{event.rfidTag}</div>}
                        </TableCell>
                        <TableCell>
                          <div className="flex items-center gap-2 text-sm">
                            <span className="text-muted-foreground max-w-[160px] truncate" title={event.fromLocationName || "Off-premises / unknown"}>
                              {event.fromLocationName || "Off-premises / unknown"}
                            </span>
                            <ArrowRight className="w-3 h-3 text-muted-foreground shrink-0" />
                            <span className="font-medium max-w-[160px] truncate">
                              <Destination event={event} />
                            </span>
                          </div>
                          {event.notes && (
                            <div className="text-[11px] text-muted-foreground mt-0.5 max-w-[360px] truncate" title={event.notes}>{event.notes}</div>
                          )}
                        </TableCell>
                        <TableCell>
                          <div className="flex flex-col items-start gap-1">
                            <Badge variant="outline" className={cn("text-[10px] uppercase font-mono tracking-wider gap-1", trigger.className)}>
                              <TriggerIcon className="w-3 h-3" />{trigger.label}
                            </Badge>
                            {event.gatePassId != null && (
                              <Link href="/gate-passes" className="text-[11px] text-primary hover:underline">Gate pass #{event.gatePassId}</Link>
                            )}
                          </div>
                        </TableCell>
                        <TableCell className="text-sm text-muted-foreground">
                          {event.movedBy || (event.triggeredBy === "rfid" ? "System (RFID)" : "—")}
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>
      </div>
    </Layout>
  );
}
