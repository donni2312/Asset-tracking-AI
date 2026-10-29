import { useState } from "react";
import { Link } from "wouter";
import { useQueryClient } from "@tanstack/react-query";
import {
  useCreateDelegation, useDecideApprovalRequest, useListApprovals, useListAssetUpgrades, useListDelegationCandidates, useListDelegations,
  type ApprovalDecisionInputMethod, type ApprovalItem,
} from "@workspace/api-client-react";
import { Layout } from "@/components/layout";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { useCurrentUser } from "@/hooks/use-auth";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS } from "@/lib/constants";
import { money } from "@/lib/finance";
import { inGroup } from "@/lib/roles";
import { UpgradeDecisionButtons } from "@/pages/assets/card";

const KIND: Record<string, { label: string; tone: string }> = {
  upgrade: { label: "UPGRADE · AU-07", tone: "text-violet-400" },
  no_serial: { label: "NO SERIAL · AU-02", tone: "text-amber-500" },
  repair: { label: "REPAIR · SI-05", tone: "text-sky-400" },
  disposal: { label: "DISPOSAL · SF-04", tone: "text-orange-400" },
  transfer: { label: "TRANSFER · GI-05", tone: "text-emerald-400" },
  gate_pass: { label: "GATE PASS · SI-04", tone: "text-rose-400" },
};

/**
 * One inbox (Phase 4): upgrades and "No serial" reasons for SBU Finance, and every approval step the signed-in
 * user may decide (repairs, disposals, transfers, gate passes), including a colleague's while they are on leave.
 */
export default function Approvals() {
  const user = useCurrentUser();
  const { data, isLoading } = useListApprovals({ query: { enabled: !!user, queryKey: ["approvals"] } });
  return (
    <Layout>
      <div className="space-y-5">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Approvals</h1>
          <p className="text-sm text-muted-foreground">Everything waiting on you, routed by role and threshold. Every decision and comment goes to the audit trail.</p>
        </div>
        {isLoading && <div className="h-32 animate-pulse rounded-md bg-muted" />}
        <div className="space-y-3">
          {(data ?? []).map((item) => <ApprovalRow key={item.id} item={item} />)}
          {data && data.length === 0 && <p className="text-sm text-muted-foreground">Nothing is waiting on you.</p>}
        </div>
        {inGroup(user, "gatePassApprovers") || user?.role === "group_finance" ? <OnLeave /> : null}
      </div>
    </Layout>
  );
}

function ApprovalRow({ item }: { item: ApprovalItem }) {
  const { data: upgrades } = useListAssetUpgrades(item.assetId ?? 0, { query: { enabled: item.type === "upgrade" && item.assetId != null, queryKey: ["upgrades", item.assetId] } });
  const upgrade = upgrades?.find((u) => u.id === item.upgradeId);
  const kind = KIND[item.type] ?? { label: item.type, tone: "" };
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 rounded-md border bg-card p-4">
      <div className="min-w-0">
        <span className={`text-[11px] font-bold ${kind.tone}`}>{kind.label}</span>
        {item.stepLabel && <span className="ml-2 text-[11px] text-muted-foreground">· {item.stepLabel}</span>}
        {item.escalated && <span className="ml-2 rounded bg-red-500/15 px-1.5 text-[10px] font-semibold text-red-500">ESCALATED</span>}
        {item.onBehalfOf && <span className="ml-2 rounded bg-sky-500/15 px-1.5 text-[10px] font-semibold text-sky-500">for {item.onBehalfOf} (on leave)</span>}
        <div className="mt-1 font-semibold">{item.summary}</div>
        <div className="text-xs text-muted-foreground">
          {item.assetId != null ? <><Link href={`/assets/${item.assetId}/card`} className="font-mono text-primary hover:underline">{item.assetNumber ?? "pending"}</Link> · </> : null}{item.assetDescription}
          {upgrade?.cost != null && ` · ${money(upgrade.cost)}`}{item.raisedBy && ` · raised by ${item.raisedBy}`} · {new Date(item.raisedAt).toLocaleDateString("en-GB")}
        </div>
      </div>
      {item.requestId ? <DecideButtons item={item} />
        : upgrade ? <UpgradeDecisionButtons upgrade={upgrade} assetId={item.assetId ?? 0} />
        : <Link href={`/assets/${item.assetId}/card`} className="text-sm font-semibold text-amber-500">Review on the card →</Link>}
    </div>
  );
}

/** Approve or reject this step, adding what the step needs (Finance's disposal details, the data-wipe note, a repair estimate). */
function DecideButtons({ item }: { item: ApprovalItem }) {
  const decide = useDecideApprovalRequest();
  const qc = useQueryClient();
  const { toast } = useToast();
  const [open, setOpen] = useState<null | "approve" | "reject">(null);
  const [form, setForm] = useState({ comment: "", method: "scrap", proceeds: "", buyer: "", disposalDate: new Date().toISOString().slice(0, 10), dataWipeNote: "", cost: "" });
  const needs = item.needs ?? [];
  const repairFinance = item.type === "repair";

  function submit(e: React.FormEvent) {
    e.preventDefault();
    const approve = open === "approve";
    decide.mutate(
      {
        id: item.requestId!,
        data: {
          decision: approve ? "approve" : "reject",
          comment: form.comment || undefined,
          ...(approve && needs.includes("method") ? { method: form.method as ApprovalDecisionInputMethod, disposalDate: form.disposalDate, proceeds: Number(form.proceeds || 0), buyer: form.buyer || undefined } : {}),
          ...(approve && needs.includes("dataWipeNote") ? { dataWipeNote: form.dataWipeNote } : {}),
          ...(approve && repairFinance && form.cost ? { cost: Number(form.cost) } : {}),
        },
      },
      {
        onSuccess: () => { toast({ title: approve ? "Approved" : "Rejected" }); setOpen(null); qc.invalidateQueries(); },
        onError: (err) => toast({ title: "Not recorded", description: apiErrorMessage(err), variant: "destructive" }),
      },
    );
  }

  return (
    <div className="flex gap-2">
      <Button size="sm" onClick={() => setOpen("approve")}>Approve</Button>
      <Button size="sm" variant="outline" onClick={() => setOpen("reject")}>Reject</Button>
      <Dialog open={!!open} onOpenChange={(o) => !o && setOpen(null)}>
        <DialogContent>
          <DialogHeader><DialogTitle>{open === "approve" ? "Approve" : "Reject"}: {item.stepLabel}</DialogTitle></DialogHeader>
          <form onSubmit={submit} className="space-y-3">
            <p className="text-sm text-muted-foreground">{item.summary}</p>
            {open === "approve" && needs.includes("method") && (
              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-1">
                  <Label className="text-xs">Method *</Label>
                  <select className={SELECT_CLASS} value={form.method} onChange={(e) => setForm({ ...form, method: e.target.value })}>
                    <option value="sale">Sale</option><option value="scrap">Scrap</option><option value="donation">Donation</option><option value="write_off">Write-off</option>
                  </select>
                </div>
                <div className="space-y-1"><Label className="text-xs">Disposal date *</Label><Input type="date" value={form.disposalDate} onChange={(e) => setForm({ ...form, disposalDate: e.target.value })} required /></div>
                <div className="space-y-1"><Label className="text-xs">Proceeds (USD)</Label><Input type="number" step="0.01" min="0" value={form.proceeds} onChange={(e) => setForm({ ...form, proceeds: e.target.value })} required={form.method === "sale"} /></div>
                <div className="space-y-1"><Label className="text-xs">Buyer</Label><Input value={form.buyer} onChange={(e) => setForm({ ...form, buyer: e.target.value })} required={form.method === "sale"} /></div>
                <p className="col-span-2 text-xs text-muted-foreground">Gain / loss = proceeds − NBV at disposal. Above the threshold Group Finance signs off too (GF-04).</p>
              </div>
            )}
            {open === "approve" && needs.includes("dataWipeNote") && (
              <div className="space-y-1"><Label className="text-xs">Data-wipe certificate or note * (GI-10)</Label><Textarea value={form.dataWipeNote} onChange={(e) => setForm({ ...form, dataWipeNote: e.target.value })} required /></div>
            )}
            {open === "approve" && repairFinance && (
              <div className="space-y-1"><Label className="text-xs">Estimated repair cost (USD)</Label><Input type="number" step="0.01" min="0" value={form.cost} onChange={(e) => setForm({ ...form, cost: e.target.value })} /></div>
            )}
            <div className="space-y-1">
              <Label className="text-xs">Comment{open === "reject" ? " *" : ""}</Label>
              <Textarea value={form.comment} onChange={(e) => setForm({ ...form, comment: e.target.value })} required={open === "reject"} />
            </div>
            <DialogFooter><Button type="submit" disabled={decide.isPending}>{open === "approve" ? "Approve" : "Reject"}</Button></DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  );
}

/** LM-03: record leave and who decides meanwhile. */
function OnLeave() {
  const { data: delegations } = useListDelegations();
  const { data: colleagues } = useListDelegationCandidates();
  const create = useCreateDelegation();
  const qc = useQueryClient();
  const { toast } = useToast();
  const [form, setForm] = useState({ endsAt: "", delegateUserId: "", reason: "" });
  return (
    <section className="space-y-3 rounded-md border bg-card p-4">
      <h2 className="font-semibold">On leave</h2>
      <form className="flex flex-wrap items-end gap-2" onSubmit={(e) => {
        e.preventDefault();
        create.mutate({ data: { endsAt: new Date(form.endsAt + "T23:59:00").toISOString(), delegateUserId: form.delegateUserId ? Number(form.delegateUserId) : undefined, reason: form.reason || undefined } },
          { onSuccess: () => { toast({ title: "Leave recorded" }); qc.invalidateQueries(); }, onError: (err) => toast({ title: "Not recorded", description: apiErrorMessage(err), variant: "destructive" }) });
      }}>
        <label className="flex flex-col text-xs text-muted-foreground">Away until<Input type="date" value={form.endsAt} onChange={(e) => setForm({ ...form, endsAt: e.target.value })} required /></label>
        <select className={SELECT_CLASS + " w-56"} value={form.delegateUserId} onChange={(e) => setForm({ ...form, delegateUserId: e.target.value })} aria-label="Delegate">
          <option value="">No delegate (the next approver stands in)</option>
          {(colleagues ?? []).map((c) => <option key={c.id} value={c.id}>{c.name} · {c.role}</option>)}
        </select>
        <Input className="w-48" placeholder="Reason" value={form.reason} onChange={(e) => setForm({ ...form, reason: e.target.value })} />
        <Button type="submit" variant="outline" disabled={create.isPending}>Record leave</Button>
      </form>
      <ul className="text-sm">
        {(delegations ?? []).map((d) => (
          <li key={d.id} className="text-muted-foreground">
            {d.userName} away {new Date(d.startsAt).toLocaleDateString("en-GB")} – {new Date(d.endsAt).toLocaleDateString("en-GB")}{d.delegateName ? ` · ${d.delegateName} decides` : ""}{d.reason ? ` · ${d.reason}` : ""}
          </li>
        ))}
      </ul>
    </section>
  );
}
