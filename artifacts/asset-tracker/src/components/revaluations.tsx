import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useListAssetAttachments, useListRevaluations, useRecordRevaluation, type RevaluationInputKind } from "@workspace/api-client-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS } from "@/lib/constants";
import { money } from "@/lib/finance";

/**
 * SF-07 on the asset card (Finance roles only): revaluations and impairments, and the form for SBU Finance.
 * A supporting document must be attached to the asset first.
 */
export function RevaluationsPanel({ assetId, canRecord }: { assetId: number; canRecord: boolean }) {
  const { toast } = useToast();
  const qc = useQueryClient();
  const { data: rows } = useListRevaluations(assetId);
  const { data: attachments } = useListAssetAttachments(assetId, { query: { enabled: canRecord, queryKey: ["attachments", assetId] } });
  const record = useRecordRevaluation();
  const [form, setForm] = useState({ kind: "impairment", amount: "", life: "", reason: "", attachmentId: "" });

  function submit(e: React.FormEvent) {
    e.preventDefault();
    const amount = Number(form.amount);
    record.mutate({
      id: assetId,
      data: {
        kind: form.kind as RevaluationInputKind,
        newValue: form.kind === "revaluation" ? amount : undefined,
        impairmentAmount: form.kind === "impairment" ? amount : undefined,
        revisedLifeMonths: form.life ? Number(form.life) : undefined,
        reason: form.reason,
        attachmentId: Number(form.attachmentId),
      },
    }, {
      onSuccess: () => { toast({ title: form.kind === "impairment" ? "Impairment recorded" : "Revaluation recorded" }); setForm({ ...form, amount: "", reason: "" }); qc.invalidateQueries(); },
      onError: (err) => toast({ title: "Not recorded", description: apiErrorMessage(err), variant: "destructive" }),
    });
  }

  return (
    <section className="space-y-3 rounded-md border bg-card p-4">
      <div>
        <h2 className="font-semibold">Revaluations &amp; impairments</h2>
        <p className="text-xs text-muted-foreground">Change the carrying amount from the current month; depreciation after it is spread over the remaining (or revised) life (SF-07).</p>
      </div>
      {rows && rows.length > 0 && (
        <Table>
          <TableHeader><TableRow><TableHead>From</TableHead><TableHead>Kind</TableHead><TableHead className="text-right">NBV before</TableHead><TableHead className="text-right">Change</TableHead><TableHead className="text-right">NBV after</TableHead><TableHead>Reason</TableHead><TableHead>By</TableHead></TableRow></TableHeader>
          <TableBody>
            {rows.map((r) => (
              <TableRow key={r.id}>
                <TableCell>{r.period}</TableCell>
                <TableCell className="capitalize">{r.kind}</TableCell>
                <TableCell className="text-right font-mono">{money(r.nbvBefore)}</TableCell>
                <TableCell className={`text-right font-mono ${r.amount < 0 ? "text-red-600" : "text-green-600"}`}>{money(r.amount)}</TableCell>
                <TableCell className="text-right font-mono">{money(r.nbvAfter)}</TableCell>
                <TableCell className="text-sm">{r.reason}{r.revisedLifeMonths ? ` · life ${r.revisedLifeMonths} months` : ""}</TableCell>
                <TableCell className="text-xs">{r.recordedBy}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {canRecord && (
        <form className="flex flex-wrap items-end gap-2" onSubmit={submit}>
          <select className={SELECT_CLASS} value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value })} aria-label="Kind">
            <option value="impairment">Impairment (loss)</option>
            <option value="revaluation">Revaluation (new value)</option>
          </select>
          <Input className="w-36" type="number" step="0.01" value={form.amount} onChange={(e) => setForm({ ...form, amount: e.target.value })} placeholder={form.kind === "impairment" ? "Loss (USD)" : "New value (USD)"} required />
          <Input className="w-36" type="number" value={form.life} onChange={(e) => setForm({ ...form, life: e.target.value })} placeholder="Revised life (months)" />
          <Input className="min-w-48 flex-1" value={form.reason} onChange={(e) => setForm({ ...form, reason: e.target.value })} placeholder="Reason" required minLength={3} />
          <select className={SELECT_CLASS} value={form.attachmentId} onChange={(e) => setForm({ ...form, attachmentId: e.target.value })} aria-label="Supporting document" required>
            <option value="">Supporting document…</option>
            {attachments?.map((x) => <option key={x.id} value={x.id}>{x.fileName}</option>)}
          </select>
          <Button type="submit" size="sm" disabled={record.isPending || !form.attachmentId}>Record</Button>
        </form>
      )}
    </section>
  );
}
