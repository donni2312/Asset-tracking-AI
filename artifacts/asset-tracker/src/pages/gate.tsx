import { useEffect, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import {
  getGateScanQueryKey,
  useGateScan,
  useListGateAlerts,
  useListLocations,
  useListOverdueReturns,
  useRecordGateAction,
  useRecordGateExit,
  useRecordGateReturn,
  type GateActionInputAction,
  type GateAlert,
  type GatePass,
} from "@workspace/api-client-react";
import { AlertTriangle, CheckCircle2, RotateCcw, ScanLine, XCircle } from "lucide-react";
import { Layout } from "@/components/layout";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { useCurrentUser } from "@/hooks/use-auth";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS } from "@/lib/constants";

const VERDICT = {
  clear: { cls: "bg-green-600", icon: CheckCircle2, title: "Approved to leave" },
  return: { cls: "bg-amber-500", icon: RotateCcw, title: "Expected back" },
  stop: { cls: "bg-red-600", icon: XCircle, title: "Must not leave" },
} as const;

function useNotice() {
  const { toast } = useToast();
  const qc = useQueryClient();
  return {
    ok: (title: string) => { toast({ title }); qc.invalidateQueries(); },
    fail: (title: string) => (e: unknown) => toast({ title, description: apiErrorMessage(e), variant: "destructive" }),
  };
}

/** The items of a pass with a tick box each: exit what has not left, return what is out (some or all). */
function PassItems({ pass, gateId }: { pass: GatePass; gateId: number | null }) {
  const n = useNotice();
  const exit = useRecordGateExit();
  const back = useRecordGateReturn();
  const [picked, setPicked] = useState<number[]>([]);
  const [condition, setCondition] = useState("good");
  const items = pass.items ?? [];
  const waiting = items.filter((i) => !i.exitedAt);
  const outside = items.filter((i) => i.exitedAt && !i.returnedAt);
  const chosen = (list: typeof items) => list.filter((i) => picked.includes(i.assetId)).map((i) => i.assetId);
  const toggle = (id: number) => setPicked((p) => (p.includes(id) ? p.filter((x) => x !== id) : [...p, id]));

  return (
    <div className="space-y-3">
      <ul className="divide-y rounded-md border">
        {items.map((i) => (
          <li key={i.assetId} className="flex items-center gap-3 p-3">
            {(!i.exitedAt || !i.returnedAt) && (
              <input type="checkbox" className="h-5 w-5" checked={picked.includes(i.assetId)} onChange={() => toggle(i.assetId)} aria-label={`Select ${i.assetName}`} />
            )}
            <div className="min-w-0 flex-1">
              <div className="truncate font-medium">{i.assetName}</div>
              <div className="font-mono text-xs text-muted-foreground">{i.assetNumber ?? i.rfidTag}</div>
            </div>
            <span className="text-xs text-muted-foreground">
              {i.returnedAt ? `Back ${new Date(i.returnedAt).toLocaleDateString()}${i.returnCondition ? ` (${i.returnCondition})` : ""}` : i.exitedAt ? "Out" : "Not left"}
            </span>
          </li>
        ))}
      </ul>
      {!gateId && <p className="text-sm text-amber-600">Choose your gate first.</p>}
      <div className="flex flex-wrap gap-2">
        {pass.status === "approved" && waiting.length > 0 && (
          <Button className="h-12 flex-1" disabled={!gateId || exit.isPending}
            onClick={() => exit.mutate({ id: pass.id, data: { gateLocationId: gateId!, assetIds: chosen(waiting).length ? chosen(waiting) : undefined } },
              { onSuccess: () => { setPicked([]); n.ok("Exit recorded"); }, onError: n.fail("Exit not recorded") })}>
            Record exit {chosen(waiting).length ? `(${chosen(waiting).length})` : "(all)"}
          </Button>
        )}
        {outside.length > 0 && (
          <>
            <select className={SELECT_CLASS + " h-12"} value={condition} onChange={(e) => setCondition(e.target.value)} aria-label="Condition on return">
              {["good", "fair", "poor", "damaged"].map((c) => <option key={c} value={c}>{c}</option>)}
            </select>
            <Button className="h-12 flex-1" variant="secondary" disabled={!gateId || back.isPending}
              onClick={() => {
                const ids = chosen(outside).length ? chosen(outside) : outside.map((i) => i.assetId);
                back.mutate({ id: pass.id, data: { gateLocationId: gateId!, items: ids.map((assetId) => ({ assetId, condition: condition as "good" })) } },
                  { onSuccess: () => { setPicked([]); n.ok("Return recorded"); }, onError: n.fail("Return not recorded") });
              }}>
              Record return {chosen(outside).length ? `(${chosen(outside).length})` : "(all out)"}
            </Button>
          </>
        )}
      </div>
    </div>
  );
}

function Alert({ alert, gateId }: { alert: GateAlert; gateId: number | null }) {
  const n = useNotice();
  const act = useRecordGateAction();
  const [notes, setNotes] = useState("");
  const record = (action: GateActionInputAction) =>
    act.mutate({ insightId: alert.insightId, data: { action, notes: notes || undefined, gateLocationId: gateId ?? undefined } },
      { onSuccess: () => { setNotes(""); n.ok("Action recorded"); }, onError: n.fail("Not recorded") });
  return (
    <li className="space-y-2 rounded-md border p-3">
      <div className="flex items-start gap-2">
        <AlertTriangle className={alert.severity === "critical" ? "mt-0.5 h-5 w-5 text-red-600" : "mt-0.5 h-5 w-5 text-amber-500"} />
        <div className="min-w-0">
          <div className="font-medium">{alert.assetName} <span className="font-mono text-xs text-muted-foreground">{alert.assetNumber ?? alert.rfidTag}</span></div>
          <div className="text-sm text-muted-foreground">{alert.message}</div>
          {alert.actions.map((a) => (
            <div key={a.id} className="text-xs text-muted-foreground">{new Date(a.at).toLocaleString()} · {a.officer}: {a.action.replace(/_/g, " ")}{a.notes ? ` — ${a.notes}` : ""}</div>
          ))}
        </div>
      </div>
      <Textarea value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="Reason (required to release or escalate)" rows={2} />
      <div className="grid grid-cols-3 gap-2">
        <Button className="h-11" variant="destructive" disabled={act.isPending} onClick={() => record("stopped")}>Stopped</Button>
        <Button className="h-11" variant="outline" disabled={act.isPending || !notes.trim()} onClick={() => record("released_with_override")}>Release (override)</Button>
        <Button className="h-11" variant="secondary" disabled={act.isPending || !notes.trim()} onClick={() => record("escalated")}>Escalate</Button>
      </div>
    </li>
  );
}

/**
 * The Gate Officer's screen (SG-01 … SG-03, GI-06), built for a phone at the gate: scan the pass QR or the item,
 * see green / amber / red, record the exit or the return item by item, act on alerts, and see what is overdue.
 */
export default function Gate() {
  const user = useCurrentUser();
  const [text, setText] = useState("");
  const [code, setCode] = useState("");
  const [gateId, setGateId] = useState<number | null>(null);
  const input = useRef<HTMLInputElement>(null);
  const { data: gates } = useListLocations({ type: "gate", sbuId: user?.sbuId ?? undefined });
  const { data: scan, error, isFetching } = useGateScan({ code }, { query: { enabled: code.length > 0, queryKey: getGateScanQueryKey({ code }), retry: false } });
  const { data: alerts } = useListGateAlerts();
  const { data: overdue } = useListOverdueReturns();
  useEffect(() => input.current?.focus(), []);
  useEffect(() => { if (gateId === null && gates?.length === 1) setGateId(gates[0].id); }, [gates, gateId]);

  const verdict = scan ? VERDICT[scan.verdict] : null;
  return (
    <Layout>
      <div className="mx-auto max-w-md space-y-5">
        <div className="flex items-center justify-between gap-2">
          <h1 className="text-2xl font-bold tracking-tight">Gate</h1>
          <select className={SELECT_CLASS} value={gateId ?? ""} onChange={(e) => setGateId(e.target.value ? Number(e.target.value) : null)} aria-label="Gate">
            <option value="">Your gate…</option>
            {gates?.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
          </select>
        </div>
        <form className="flex gap-2" onSubmit={(e) => { e.preventDefault(); setCode(text.trim()); }}>
          <Input ref={input} value={text} onChange={(e) => setText(e.target.value)} placeholder="Scan pass QR (GP-…) or item tag" aria-label="Code" className="h-12 font-mono" />
          <Button type="submit" className="h-12" disabled={!text.trim() || isFetching}><ScanLine className="mr-1 h-4 w-4" /> Check</Button>
        </form>
        {error && <p className="rounded-md border border-red-500/40 bg-red-500/10 p-3 text-sm">{apiErrorMessage(error)}</p>}
        {scan && verdict && (
          <section className="space-y-3">
            <div className={`flex items-center gap-3 rounded-md p-4 text-white ${verdict.cls}`}>
              <verdict.icon className="h-10 w-10 shrink-0" />
              <div>
                <div className="text-xl font-bold">{verdict.title}</div>
                <div className="text-sm opacity-90">{scan.message}</div>
              </div>
            </div>
            {scan.gatePass && (
              <div className="space-y-2 rounded-md border bg-card p-3 text-sm">
                <div className="font-medium">Gate pass #{scan.gatePass.id} · {scan.gatePass.status}</div>
                <div className="text-muted-foreground">{scan.gatePass.reason}{scan.gatePass.destination ? ` → ${scan.gatePass.destination}` : ""}</div>
                <div className="text-muted-foreground">Requested by {scan.gatePass.requestedBy}{scan.gatePass.returnDate ? ` · due back ${scan.gatePass.returnDate}` : ""}</div>
                <PassItems pass={scan.gatePass} gateId={gateId} />
              </div>
            )}
          </section>
        )}

        <section className="space-y-2">
          <h2 className="font-semibold">Alerts {alerts?.length ? `(${alerts.length})` : ""}</h2>
          {alerts?.length === 0 && <p className="text-sm text-muted-foreground">No open alerts.</p>}
          <ul className="space-y-2">{alerts?.map((a) => <Alert key={a.insightId} alert={a} gateId={gateId} />)}</ul>
        </section>

        <section className="space-y-2">
          <h2 className="font-semibold">Overdue returns {overdue?.length ? `(${overdue.length})` : ""}</h2>
          {overdue?.length === 0 && <p className="text-sm text-muted-foreground">Nothing overdue.</p>}
          <ul className="divide-y rounded-md border">
            {overdue?.map((o) => (
              <li key={`${o.gatePassId}-${o.assetId}`} className="flex items-center justify-between gap-2 p-3 text-sm">
                <button type="button" className="min-w-0 text-left" onClick={() => { setText(`GP-${o.gatePassId}`); setCode(`GP-${o.gatePassId}`); }}>
                  <div className="truncate font-medium">{o.assetName}</div>
                  <div className="text-xs text-muted-foreground">GP-{o.gatePassId} · {o.requestedBy}{o.destination ? ` · ${o.destination}` : ""}</div>
                </button>
                <span className="shrink-0 font-medium text-red-600">due {o.returnDate}</span>
              </li>
            ))}
          </ul>
        </section>
      </div>
    </Layout>
  );
}
