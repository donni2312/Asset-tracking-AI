import { useState } from "react";
import { Link, useParams } from "wouter";
import { useQueryClient } from "@tanstack/react-query";
import {
  exportDepartmentHoldings, useAcceptAcknowledgement, useAcceptAcknowledgementLink, useCompleteExitClearance, useGetDepartmentHoldings, useGetExitClearance,
  useGetToolbox, useListAcknowledgements, useRecordExitClearanceItem, useRecordExitRecovery, useStartExitClearance, type RecoveryInputOutcome, type ExitClearanceInputReason, type ExitClearanceItemInputOutcome,
} from "@workspace/api-client-react";
import { Layout } from "@/components/layout";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useCurrentUser } from "@/hooks/use-auth";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS } from "@/lib/constants";
import { saveBlob, STATUS_LABEL } from "@/lib/finance";

function useNotify() {
  const { toast } = useToast();
  const qc = useQueryClient();
  return {
    ok: (title: string) => { toast({ title }); qc.invalidateQueries(); },
    fail: (title: string) => (e: unknown) => toast({ title, description: apiErrorMessage(e), variant: "destructive" }),
  };
}

const date = (v?: string | null) => (v ? new Date(v).toLocaleDateString("en-GB") : "—");

/** HR-01 / HR-04: an employee's toolbox, what they acknowledged, past items, and their exit clearance. */
export function ToolboxPage() {
  const { id } = useParams();
  const employeeId = Number(id);
  const user = useCurrentUser();
  const { data: toolbox } = useGetToolbox(employeeId);
  const start = useStartExitClearance();
  const n = useNotify();
  const [reason, setReason] = useState("resignation");
  if (!toolbox) return <Layout><div className="h-32 animate-pulse rounded-md bg-muted" /></Layout>;
  return (
    <Layout>
      <div className="space-y-5">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">{toolbox.name} <span className="font-mono text-sm text-muted-foreground">{toolbox.employeeNo}</span></h1>
          <p className="text-sm text-muted-foreground">{toolbox.jobRole ?? "—"} · {toolbox.active ? "employed" : "left"} · {toolbox.items.length} items held</p>
        </div>
        <section className="rounded-md border bg-card">
          <Table>
            <TableHeader><TableRow><TableHead>Item</TableHead><TableHead>Tag</TableHead><TableHead>Condition</TableHead><TableHead>Issued</TableHead><TableHead>Acknowledged</TableHead></TableRow></TableHeader>
            <TableBody>
              {toolbox.items.map((i) => (
                <TableRow key={i.assetId}>
                  <TableCell><Link href={`/assets/${i.assetId}`} className="text-primary hover:underline">{i.assetNumber ?? "pending"}</Link> · {i.description}</TableCell>
                  <TableCell className="font-mono text-xs">{i.tag ?? "—"}</TableCell>
                  <TableCell>{i.condition ?? "—"}</TableCell>
                  <TableCell>{date(i.issuedAt)}</TableCell>
                  <TableCell className={i.acknowledgedAt ? "" : "font-semibold text-amber-600"}>{i.acknowledgedAt ? date(i.acknowledgedAt) : i.acknowledgementId ? "Waiting (HR-03)" : "—"}</TableCell>
                </TableRow>
              ))}
              {toolbox.items.length === 0 && <TableRow><TableCell colSpan={5} className="text-center text-sm text-muted-foreground">Holds nothing.</TableCell></TableRow>}
            </TableBody>
          </Table>
        </section>
        {toolbox.history.length > 0 && (
          <section className="rounded-md border bg-card p-4">
            <h2 className="mb-2 font-semibold">Past items</h2>
            <ul className="text-sm text-muted-foreground">{toolbox.history.map((h) => <li key={h.assetId}>{h.assetNumber} · {h.description} · returned {date(h.returnedAt)}</li>)}</ul>
          </section>
        )}
        {toolbox.openExitClearanceId ? <ExitClearance id={toolbox.openExitClearanceId} />
          : user?.role === "sbu_hr" && toolbox.active && (
            <form className="flex gap-2" onSubmit={(e) => { e.preventDefault(); start.mutate({ id: employeeId, data: { reason: reason as ExitClearanceInputReason } }, { onSuccess: () => n.ok("Exit clearance started"), onError: n.fail("Not started") }); }}>
              <select className={SELECT_CLASS + " w-44"} value={reason} onChange={(e) => setReason(e.target.value)} aria-label="Reason">
                <option value="resignation">Resignation</option><option value="termination">Termination</option><option value="retirement">Retirement</option><option value="transfer">Transfer</option>
              </select>
              <Button type="submit" variant="outline">Start exit clearance (HR-04)</Button>
            </form>
          )}
      </div>
    </Layout>
  );
}

function ExitClearance({ id }: { id: number }) {
  const user = useCurrentUser();
  const { data } = useGetExitClearance(id);
  const record = useRecordExitClearanceItem();
  const recovery = useRecordExitRecovery();
  const [approver, setApprover] = useState("");
  const complete = useCompleteExitClearance();
  const n = useNotify();
  const [exception, setException] = useState({ approvedBy: "", reason: "" });
  if (!data) return null;
  const outstanding = data.items.filter((i) => i.outcome === "outstanding").length;
  return (
    <section className="space-y-3 rounded-md border border-amber-500/40 bg-card p-4">
      <h2 className="font-semibold">Exit clearance · {data.reason} · {data.status}</h2>
      <ul className="space-y-1 text-sm">
        {data.items.map((i) => (
          <li key={i.id} className="flex flex-wrap items-center justify-between gap-2">
            <span>{i.assetNumber} · {i.description} <span className="text-xs text-muted-foreground">· {i.isIt ? "IT (back to SBU IT)" : "non-IT (back to Stores)"}</span></span>
            {i.outcome === "outstanding" && data.status === "open"
              ? <span className="flex gap-1">{(["returned", "damaged", "missing"] as const).map((o) => (
                  <Button key={o} size="sm" variant="outline" onClick={() => record.mutate({ id, itemId: i.id, data: { outcome: o as ExitClearanceItemInputOutcome } }, { onSuccess: () => n.ok(`Recorded ${o}`), onError: n.fail("Not recorded") })}>{o}</Button>
                ))}</span>
              : <span className="flex flex-wrap items-center gap-2 text-xs">
                  {i.outcome}{i.recordedBy ? ` · ${i.recordedBy}` : ""}
                  {i.netBookValue != null && <span className="font-mono">NBV {i.netBookValue.toFixed(2)}</span>}
                  {i.recoveryOutcome && <span className="rounded bg-muted px-1.5">{i.recoveryOutcome.replace("_", " ")} · approved by {i.recoveryApprovedBy}</span>}
                  {/* HR-05: the recovery outcome of a missing or damaged item, with its approver. */}
                  {!i.recoveryOutcome && (i.outcome === "missing" || i.outcome === "damaged") && user?.role === "sbu_hr" && (
                    <span className="flex gap-1">
                      <input className="h-7 w-32 rounded border bg-background px-1" value={approver} onChange={(e) => setApprover(e.target.value)} placeholder="Approved by" aria-label="Approved by" />
                      {(["recovered", "written_off", "cost_recovery"] as const).map((o) => (
                        <Button key={o} size="sm" variant="outline" disabled={approver.trim().length < 2}
                          onClick={() => recovery.mutate({ id, itemId: i.id, data: { outcome: o as RecoveryInputOutcome, approvedBy: approver } }, { onSuccess: () => n.ok("Outcome recorded; SBU Finance notified"), onError: n.fail("Not recorded") })}>
                          {o.replace("_", " ")}
                        </Button>
                      ))}
                    </span>
                  )}
                </span>}
          </li>
        ))}
      </ul>
      {user?.role === "sbu_hr" && data.status === "open" && (
        <form className="flex flex-wrap items-end gap-2" onSubmit={(e) => {
          e.preventDefault();
          complete.mutate({ id, data: outstanding > 0 ? { exceptionApprovedBy: exception.approvedBy, exceptionReason: exception.reason } : {} }, { onSuccess: () => n.ok("Exit clearance complete"), onError: n.fail("Not complete") });
        }}>
          {outstanding > 0 && (
            <>
              <Input className="w-48" placeholder="Exception approved by" value={exception.approvedBy} onChange={(e) => setException({ ...exception, approvedBy: e.target.value })} required />
              <Input className="w-64" placeholder="Why (approved exception)" value={exception.reason} onChange={(e) => setException({ ...exception, reason: e.target.value })} required />
            </>
          )}
          <Button type="submit">{outstanding > 0 ? `Complete with exception (${outstanding} outstanding)` : "Complete clearance"}</Button>
        </form>
      )}
    </section>
  );
}

/** HR-03: the employee's items to acknowledge. */
export function MyItems() {
  const { data } = useListAcknowledgements({});
  const accept = useAcceptAcknowledgement();
  const n = useNotify();
  return (
    <Layout>
      <div className="space-y-5">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">My items</h1>
          <p className="text-sm text-muted-foreground">Confirm each item you received and accept the company asset policy before using it (HR-03).</p>
        </div>
        <ul className="space-y-2">
          {(data ?? []).map((a) => (
            <li key={a.id} className="flex flex-wrap items-center justify-between gap-2 rounded-md border bg-card p-3">
              <span>{a.assetNumber} · {a.assetDescription} <span className="text-xs text-muted-foreground">· issued by {a.issuedBy} on {date(a.issuedAt)}</span></span>
              {a.acknowledgedAt ? <span className="text-xs text-green-600">Acknowledged {date(a.acknowledgedAt)}</span>
                : <Button size="sm" onClick={() => accept.mutate({ id: a.id }, { onSuccess: () => n.ok("Thank you, acknowledged"), onError: n.fail("Not recorded") })}>I received it and accept the policy</Button>}
            </li>
          ))}
          {data?.length === 0 && <li className="text-sm text-muted-foreground">Nothing has been issued to you.</li>}
        </ul>
      </div>
    </Layout>
  );
}

/** HR-03 by the emailed link: no sign-in. */
export function AcknowledgeLink() {
  const { token } = useParams();
  const accept = useAcceptAcknowledgementLink();
  return (
    <div className="flex min-h-[100dvh] items-center justify-center bg-background p-4 text-foreground">
      <div className="w-full max-w-sm space-y-3 rounded-lg border bg-card p-6">
        <h1 className="text-lg font-bold">Acknowledge an item issued to you</h1>
        <p className="text-sm text-muted-foreground">By confirming you received the item and accept the TSL company asset policy.</p>
        {accept.isSuccess ? <p className="text-sm text-green-600">Thank you. Your acknowledgement is recorded.</p>
          : <Button onClick={() => accept.mutate({ data: { token: token ?? "" } })} disabled={accept.isPending}>I received it and accept the policy</Button>}
        {accept.isError && <p className="text-sm text-destructive">{apiErrorMessage(accept.error)}</p>}
      </div>
    </div>
  );
}

/** LM-01 / LM-02: what the department holds, and per person; Excel export. */
export function DepartmentHoldingsPage() {
  const { id } = useParams();
  const departmentId = Number(id);
  const { data } = useGetDepartmentHoldings(departmentId);
  const { toast } = useToast();
  if (!data) return <Layout><div className="h-32 animate-pulse rounded-md bg-muted" /></Layout>;
  return (
    <Layout>
      <div className="space-y-5">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h1 className="text-2xl font-bold tracking-tight">{data.departmentName}</h1>
          <Button variant="outline" size="sm" onClick={async () => {
            try { saveBlob(await exportDepartmentHoldings(departmentId), `department-${departmentId}.xlsx`); }
            catch (e) { toast({ title: "Export failed", description: apiErrorMessage(e), variant: "destructive" }); }
          }}>Export Excel</Button>
        </div>
        <div className="grid gap-3 sm:grid-cols-3">
          {data.employees.map((e) => (
            <Link key={e.employeeId} href={`/people/${e.employeeId}`} className="rounded-md border bg-card p-3 hover:border-primary">
              <div className="font-semibold">{e.name}</div><div className="text-xs text-muted-foreground">{e.jobRole ?? "—"} · {e.items} item{e.items === 1 ? "" : "s"}</div>
            </Link>
          ))}
        </div>
        <Table>
          <TableHeader><TableRow><TableHead>Item</TableHead><TableHead>Category</TableHead><TableHead>Holder</TableHead><TableHead>Status</TableHead><TableHead>Location</TableHead><TableHead>Condition</TableHead></TableRow></TableHeader>
          <TableBody>
            {data.items.map((i) => (
              <TableRow key={i.assetId}>
                <TableCell>{i.assetNumber ?? "pending"} · {i.description}</TableCell><TableCell>{i.category}</TableCell><TableCell>{i.holder ?? "—"}</TableCell>
                <TableCell>{STATUS_LABEL[i.status] ?? i.status}</TableCell><TableCell>{i.location ?? "—"}</TableCell><TableCell>{i.condition ?? "—"}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </Layout>
  );
}
