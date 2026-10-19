import { Layout } from "@/components/layout";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { useListGatePasses, type GatePass } from "@workspace/api-client-react";
import { formatDate, cn } from "@/lib/utils";
import { ShieldAlert, CheckCircle, XCircle, Radio, ShieldPlus, LogOut, Clock, Flag } from "lucide-react";
import { useState } from "react";
import { Link } from "wouter";
import { GatePassRequestDialog } from "@/components/gate-pass-request-dialog";
import { GatePassDecisionDialog, type GatePassAction } from "@/components/gate-pass-decision-dialog";

type Status = "pending" | "approved" | "rejected";

function OriginBadge({ pass }: { pass: GatePass }) {
  if (pass.source === "rfid_gate") {
    return (
      <div className="space-y-0.5">
        <Badge variant="outline" className="gap-1 text-[10px] border-blue-200 bg-blue-50 text-blue-700">
          <Radio className="w-3 h-3" /> AUTO · GATE READER
        </Badge>
        <div className="text-xs text-muted-foreground">Detected at {pass.gateLocationName ?? "a gate"}</div>
      </div>
    );
  }
  return (
    <div className="space-y-0.5">
      <Badge variant="outline" className="text-[10px]">REQUEST</Badge>
      <div className="text-xs font-medium">{pass.requestedBy}</div>
    </div>
  );
}

function DecisionCell({ pass }: { pass: GatePass }) {
  if (pass.status === "approved") {
    return (
      <div className="space-y-1 text-xs">
        <div className="flex items-center gap-1.5 font-medium text-emerald-700">
          <CheckCircle className="w-3.5 h-3.5" /> Approved by {pass.approvedBy}
        </div>
        <div className="text-muted-foreground">{formatDate(pass.decidedAt ?? pass.updatedAt)}</div>
        {pass.exitedAt ? (
          <div className="flex items-center gap-1 text-muted-foreground"><LogOut className="w-3 h-3" /> Left site {formatDate(pass.exitedAt)}</div>
        ) : (
          <div className="flex items-center gap-1 text-muted-foreground"><Clock className="w-3 h-3" /> Awaiting exit at a gate</div>
        )}
        {pass.decisionNote && <div className="italic text-muted-foreground max-w-[220px] truncate" title={pass.decisionNote}>“{pass.decisionNote}”</div>}
      </div>
    );
  }
  return (
    <div className="space-y-1 text-xs">
      <div className="flex items-center gap-1.5 font-medium text-destructive">
        <XCircle className="w-3.5 h-3.5" /> Rejected by {pass.approvedBy}
      </div>
      <div className="text-muted-foreground">{formatDate(pass.decidedAt ?? pass.updatedAt)}</div>
      <div className="flex items-center gap-1 text-destructive/80"><Flag className="w-3 h-3" /> Asset flagged</div>
      {pass.decisionNote && <div className="italic text-muted-foreground max-w-[220px] truncate" title={pass.decisionNote}>“{pass.decisionNote}”</div>}
    </div>
  );
}

export default function GatePassList() {
  const [filter, setFilter] = useState<Status>("pending");
  const [requestOpen, setRequestOpen] = useState(false);
  const [decision, setDecision] = useState<{ pass: GatePass; action: GatePassAction } | null>(null);
  const { data: allPasses, isLoading } = useListGatePasses();

  const counts = {
    pending: allPasses?.filter((p) => p.status === "pending").length ?? 0,
    approved: allPasses?.filter((p) => p.status === "approved").length ?? 0,
    rejected: allPasses?.filter((p) => p.status === "rejected").length ?? 0,
  };
  const passes = allPasses?.filter((p) => p.status === filter) ?? [];

  return (
    <Layout>
      <div className="space-y-6">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Gate Passes</h1>
            <p className="text-muted-foreground text-sm">
              Authorisation for assets leaving the premises. Gate readers raise a pass automatically when an asset tries to leave without one.
            </p>
          </div>
          <Button onClick={() => setRequestOpen(true)} className="gap-2 flex-shrink-0">
            <ShieldPlus className="w-4 h-4" /> Request Gate Pass
          </Button>
        </div>

        <div className="flex gap-2 bg-muted p-1 rounded-lg w-fit">
          {(["pending", "approved", "rejected"] as const).map((status) => (
            <button
              key={status}
              onClick={() => setFilter(status)}
              className={cn(
                "px-4 py-1.5 text-sm font-medium rounded-md capitalize transition-colors flex items-center gap-2",
                filter === status ? "bg-background shadow-sm text-foreground" : "text-muted-foreground hover:text-foreground",
              )}
            >
              {status}
              <span
                className={cn(
                  "text-[10px] font-bold rounded-full px-1.5 min-w-5 text-center",
                  status === "pending" && counts.pending > 0 ? "bg-amber-500 text-slate-900" : "bg-muted-foreground/15",
                )}
              >
                {counts[status]}
              </span>
            </button>
          ))}
        </div>

        <Card>
          <CardContent className="p-0">
            {isLoading ? (
              <div className="p-8 text-center text-muted-foreground animate-pulse">Loading gate passes...</div>
            ) : passes.length === 0 ? (
              <div className="p-16 text-center">
                <ShieldAlert className="w-12 h-12 text-muted-foreground/30 mx-auto mb-4" />
                <h3 className="text-lg font-medium">No {filter} gate passes</h3>
                {filter === "pending" && (
                  <p className="text-sm text-muted-foreground mt-1">
                    Request one above, or scan an asset at a gate reader in the RFID Simulator.
                  </p>
                )}
              </div>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Pass</TableHead>
                    <TableHead>Asset</TableHead>
                    <TableHead>Origin</TableHead>
                    <TableHead>Destination & Reason</TableHead>
                    <TableHead>Return By</TableHead>
                    <TableHead className="text-right">{filter === "pending" ? "Actions" : "Decision"}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {passes.map((gp) => (
                    <TableRow key={gp.id} className={gp.status === "pending" && gp.source === "rfid_gate" ? "bg-amber-50/50" : ""}>
                      <TableCell className="whitespace-nowrap">
                        <div className="font-mono text-sm font-semibold">#{gp.id}</div>
                        <div className="text-[11px] text-muted-foreground">{formatDate(gp.createdAt)}</div>
                      </TableCell>
                      <TableCell>
                        <Link href={`/assets/${gp.assetId}`} className="font-medium hover:underline text-primary">
                          {gp.assetName}
                        </Link>
                        <div className="text-[10px] text-muted-foreground font-mono mt-0.5">{gp.rfidTag}</div>
                      </TableCell>
                      <TableCell><OriginBadge pass={gp} /></TableCell>
                      <TableCell>
                        <div className="text-sm font-medium">{gp.destination || "Unspecified"}</div>
                        <div className="text-xs text-muted-foreground max-w-[240px] truncate" title={gp.reason}>{gp.reason}</div>
                      </TableCell>
                      <TableCell className="text-sm font-mono whitespace-nowrap">
                        {gp.returnDate ? gp.returnDate : <span className="text-muted-foreground font-sans text-xs">Open-ended</span>}
                      </TableCell>
                      <TableCell className="text-right">
                        {gp.status === "pending" ? (
                          <div className="flex justify-end gap-2">
                            <Button
                              size="sm"
                              variant="outline"
                              className="h-8 text-destructive hover:text-destructive hover:bg-destructive/10"
                              onClick={() => setDecision({ pass: gp, action: "reject" })}
                            >
                              <XCircle className="w-4 h-4 mr-1" /> Reject
                            </Button>
                            <Button
                              size="sm"
                              className="h-8 bg-emerald-600 hover:bg-emerald-700"
                              onClick={() => setDecision({ pass: gp, action: "approve" })}
                            >
                              <CheckCircle className="w-4 h-4 mr-1" /> Approve
                            </Button>
                          </div>
                        ) : (
                          <div className="flex justify-end text-left"><DecisionCell pass={gp} /></div>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>
      </div>

      <GatePassRequestDialog open={requestOpen} onOpenChange={setRequestOpen} />
      <GatePassDecisionDialog
        pass={decision?.pass ?? null}
        action={decision?.action ?? "approve"}
        onOpenChange={(open) => !open && setDecision(null)}
      />
    </Layout>
  );
}
