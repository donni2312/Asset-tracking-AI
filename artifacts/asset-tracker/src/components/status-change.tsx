import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import {
  getListAssetTransitionsQueryKey, useChangeAssetStatus, useListAssetTransitions, useListSbus, useRecommendDisposal, useRequestRepair, useRequestTransfer,
  type AssetStatus,
} from "@workspace/api-client-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS } from "@/lib/constants";
import { STATUS_LABEL } from "@/lib/finance";

/**
 * Phase 3: change an asset's status through the state machine. The options come from the API (the changes the
 * table allows from the current status, each allowed for this user or with the reason it is not); the API checks
 * everything again. Lost / stolen asks for the date, the reporter and what happened (GI-07).
 */
export function StatusChangeButton({ assetId, status }: { assetId: number; status: string }) {
  const { data: options } = useListAssetTransitions(assetId, { query: { queryKey: getListAssetTransitionsQueryKey(assetId) } });
  const change = useChangeAssetStatus();
  // Phase 4: repairs, disposals and transfers are requests decided in the approvals inbox.
  const repair = useRequestRepair();
  const disposal = useRecommendDisposal();
  const transfer = useRequestTransfer();
  const { data: sbus } = useListSbus();
  const [toSbuId, setToSbuId] = useState("");
  const qc = useQueryClient();
  const { toast } = useToast();
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState({ to: "", reason: "", lostDate: new Date().toISOString().slice(0, 10), reporter: "", notes: "" });
  const allowed = (options ?? []).filter((o) => o.allowed);
  if (status === "disposed" || (options && options.length === 0)) return null;
  const chosen = options?.find((o) => o.status === form.to);

  function submit(e: React.FormEvent) {
    e.preventDefault();
    const handlers = {
      onSuccess: () => {
        toast({ title: chosen?.workflow ? "Request sent for approval" : `Now ${STATUS_LABEL[form.to] ?? form.to}` });
        setOpen(false);
        setForm({ ...form, to: "", reason: "", notes: "" });
        qc.invalidateQueries();
      },
      onError: (err: unknown) => toast({ title: "Not sent", description: apiErrorMessage(err), variant: "destructive" as const }),
    };
    if (chosen?.workflow === "repair") return repair.mutate({ id: assetId, data: { fault: form.reason } }, handlers);
    if (chosen?.workflow === "disposal") return disposal.mutate({ id: assetId, data: { reason: form.reason } }, handlers);
    if (chosen?.workflow === "transfer") return transfer.mutate({ id: assetId, data: { toSbuId: Number(toSbuId), reason: form.reason } }, handlers);
    change.mutate(
      {
        id: assetId,
        data: {
          status: form.to as AssetStatus, reason: form.reason,
          ...(chosen?.needsIncidentDetails ? { lostDate: form.lostDate, reporter: form.reporter, notes: form.notes } : {}),
        },
      },
      {
        onSuccess: () => {
          toast({ title: `Now ${STATUS_LABEL[form.to] ?? form.to}` });
          setOpen(false);
          setForm({ ...form, to: "", reason: "", notes: "" });
          qc.invalidateQueries();
        },
        onError: (err) => toast({ title: "Status not changed", description: apiErrorMessage(err), variant: "destructive" }),
      },
    );
  }

  return (
    <>
      <Button variant="outline" size="sm" onClick={() => setOpen(true)} disabled={allowed.length === 0}
        title={allowed.length === 0 ? "No status change is open to your role" : undefined}>
        Change status
      </Button>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent>
          <DialogHeader><DialogTitle>Change status · now {STATUS_LABEL[status] ?? status}</DialogTitle></DialogHeader>
          <form onSubmit={submit} className="space-y-3">
            <div className="space-y-1">
              <Label className="text-xs" htmlFor="status-to">New status *</Label>
              <select id="status-to" className={SELECT_CLASS} value={form.to} onChange={(e) => setForm({ ...form, to: e.target.value })} required>
                <option value="">Choose…</option>
                {(options ?? []).map((o) => (
                  <option key={o.status} value={o.status} disabled={!o.allowed}>
                    {o.label} ({o.action}){o.allowed ? "" : ` — ${o.reason}`}
                  </option>
                ))}
              </select>
            </div>
            {chosen?.needsIncidentDetails && (
              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-1"><Label className="text-xs">Date lost / stolen *</Label><Input type="date" value={form.lostDate} onChange={(e) => setForm({ ...form, lostDate: e.target.value })} required /></div>
                <div className="space-y-1"><Label className="text-xs">Reported by *</Label><Input value={form.reporter} onChange={(e) => setForm({ ...form, reporter: e.target.value })} required /></div>
                <div className="col-span-2 space-y-1"><Label className="text-xs">What happened *</Label><Textarea value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} required /></div>
              </div>
            )}
            {chosen?.workflow === "transfer" && (
              <div className="space-y-1">
                <Label className="text-xs">To SBU *</Label>
                <select className={SELECT_CLASS} value={toSbuId} onChange={(e) => setToSbuId(e.target.value)} required>
                  <option value="">Choose…</option>
                  {(sbus ?? []).map((s) => <option key={s.id} value={s.id}>{s.code} · {s.name}</option>)}
                </select>
              </div>
            )}
            {chosen?.workflow && <p className="text-xs text-muted-foreground">This is a request: it is decided in the approvals inbox ({chosen.workflow === "repair" ? "SBU Finance" : chosen.workflow === "disposal" ? "SBU Finance, then Group IT / Group Finance where needed" : "Group IT or the receiving Admin Manager"}).</p>}
            <div className="space-y-1"><Label className="text-xs">{chosen?.workflow === "repair" ? "Fault *" : "Reason (kept in the audit trail) *"}</Label><Input value={form.reason} onChange={(e) => setForm({ ...form, reason: e.target.value })} required minLength={3} /></div>
            <DialogFooter><Button type="submit" disabled={change.isPending || !form.to}>Change status</Button></DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </>
  );
}
