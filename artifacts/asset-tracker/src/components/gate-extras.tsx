import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useCheckOutVisitorItem, useGetGateShift, useListGateIncidents, useListVisitorItems, type ShiftPass } from "@workspace/api-client-react";
import { Phone } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { currentToken } from "@/hooks/use-auth";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS } from "@/lib/constants";

async function postForm(path: string, fields: Record<string, string | undefined>, photo: File | null): Promise<void> {
  const form = new FormData();
  for (const [k, v] of Object.entries(fields)) if (v) form.append(k, v);
  if (photo) form.append("photo", photo);
  const token = currentToken();
  const res = await fetch(`/api${path}`, { method: "POST", body: form, headers: token ? { Authorization: `Bearer ${token}` } : {} });
  if (!res.ok) throw new Error((await res.json().catch(() => null))?.error ?? `Failed (${res.status})`);
}

function PassRow({ p, tone }: { p: ShiftPass; tone?: string }) {
  return (
    <li className="flex items-center justify-between gap-2 p-3 text-sm">
      <div className="min-w-0">
        <div className="truncate font-medium">{p.assetName}</div>
        <div className="text-xs text-muted-foreground">GP-{p.gatePassId} · {p.holder ?? "—"}{p.destination ? ` · ${p.destination}` : ""}</div>
      </div>
      <div className="flex shrink-0 items-center gap-2">
        {p.returnDate && <span className={`text-xs ${tone ?? ""}`}>due {p.returnDate}</span>}
        {p.phone && <a href={`tel:${p.phone.replace(/\s/g, "")}`} className="flex items-center gap-1 rounded border px-2 py-1 text-xs text-primary"><Phone className="h-3 w-3" />{p.phone}</a>}
      </div>
    </li>
  );
}

/** SG-04: what to look out for on this shift. */
export function ShiftPanel() {
  const { data } = useGetGateShift();
  return (
    <section className="space-y-2">
      <h2 className="font-semibold">This shift</h2>
      <div className="text-xs text-muted-foreground">Approved to go out today ({data?.dueOut.length ?? 0})</div>
      <ul className="divide-y rounded-md border">
        {data?.dueOut.map((p) => <PassRow key={`d${p.gatePassId}`} p={p} />)}
        {data?.dueOut.length === 0 && <li className="p-3 text-sm text-muted-foreground">None.</li>}
      </ul>
      <div className="text-xs text-muted-foreground">Overdue returns ({data?.overdue.length ?? 0})</div>
      <ul className="divide-y rounded-md border">
        {data?.overdue.map((p) => <PassRow key={`o${p.gatePassId}-${p.assetId}`} p={p} tone="font-medium text-red-600" />)}
        {data?.overdue.length === 0 && <li className="p-3 text-sm text-muted-foreground">Nothing overdue.</li>}
      </ul>
    </section>
  );
}

/** SG-05: visitors' and contractors' own equipment, logged on entry and checked out on exit. */
export function VisitorPanel({ gateId }: { gateId: number | null }) {
  const { toast } = useToast();
  const qc = useQueryClient();
  const { data } = useListVisitorItems();
  const checkOut = useCheckOutVisitorItem();
  const [form, setForm] = useState({ ownerName: "", ownerCompany: "", item: "", serialNumber: "" });
  const [photo, setPhoto] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [k]: e.target.value });

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    try {
      await postForm("/gate/visitor-items", { gateLocationId: String(gateId), ...form }, photo);
      toast({ title: "Logged on entry" });
      setForm({ ownerName: "", ownerCompany: "", item: "", serialNumber: "" });
      setPhoto(null);
      qc.invalidateQueries();
    } catch (err) {
      toast({ title: "Not logged", description: apiErrorMessage(err), variant: "destructive" });
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="space-y-2">
      <h2 className="font-semibold">Visitor equipment</h2>
      <form className="grid grid-cols-2 gap-2" onSubmit={submit}>
        <Input value={form.ownerName} onChange={set("ownerName")} placeholder="Owner" required />
        <Input value={form.ownerCompany} onChange={set("ownerCompany")} placeholder="Company" />
        <Input value={form.item} onChange={set("item")} placeholder="Item (e.g. Lenovo laptop)" required />
        <Input value={form.serialNumber} onChange={set("serialNumber")} placeholder="Serial" className="font-mono" />
        <input type="file" accept="image/*" capture="environment" className="col-span-2 text-sm" onChange={(e) => setPhoto(e.target.files?.[0] ?? null)} aria-label="Photo" />
        <Button type="submit" className="col-span-2 h-11" disabled={!gateId || busy}>Log on entry</Button>
      </form>
      <ul className="divide-y rounded-md border">
        {data?.map((v) => (
          <li key={v.id} className="flex items-center justify-between gap-2 p-3 text-sm">
            <div className="min-w-0">
              <div className="truncate font-medium">{v.item}</div>
              <div className="text-xs text-muted-foreground">{v.ownerName}{v.ownerCompany ? ` (${v.ownerCompany})` : ""} · <span className="font-mono">{v.serialNumber ?? "no serial"}</span> · in {new Date(v.checkedInAt).toLocaleTimeString()}</div>
            </div>
            <Button size="sm" variant="outline" disabled={!gateId || checkOut.isPending}
              onClick={() => checkOut.mutate({ id: v.id, data: { gateLocationId: gateId! } }, {
                onSuccess: () => { toast({ title: "Checked out" }); qc.invalidateQueries(); },
                onError: (err) => toast({ title: "Not checked out", description: apiErrorMessage(err), variant: "destructive" }),
              })}>Check out</Button>
          </li>
        ))}
        {data?.length === 0 && <li className="p-3 text-sm text-muted-foreground">No visitor equipment on site.</li>}
      </ul>
    </section>
  );
}

const KINDS: Record<string, string> = { forged_pass: "Forged pass", unknown_tag: "Unknown tag", refused_search: "Refused search", other: "Other" };

/** SG-06: one-tap escalation to the security supervisor (the SBU Admin Manager) and the asset owner's SBU IT. */
export function IncidentPanel({ gateId, code }: { gateId: number | null; code: string }) {
  const { toast } = useToast();
  const qc = useQueryClient();
  const { data } = useListGateIncidents();
  const [kind, setKind] = useState("unknown_tag");
  const [notes, setNotes] = useState("");
  const [photo, setPhoto] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);

  async function escalate() {
    setBusy(true);
    try {
      await postForm("/gate/incidents", { kind, gateLocationId: gateId ? String(gateId) : undefined, code: code || undefined, notes }, photo);
      toast({ title: "Escalated", description: "The Admin Manager and the asset's SBU IT have been told." });
      setNotes("");
      setPhoto(null);
      qc.invalidateQueries();
    } catch (err) {
      toast({ title: "Not escalated", description: apiErrorMessage(err), variant: "destructive" });
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="space-y-2">
      <h2 className="font-semibold">Escalate an incident</h2>
      <div className="grid grid-cols-2 gap-2">
        <select className={SELECT_CLASS + " h-11"} value={kind} onChange={(e) => setKind(e.target.value)} aria-label="Kind">
          {Object.entries(KINDS).map(([k, label]) => <option key={k} value={k}>{label}</option>)}
        </select>
        <input type="file" accept="image/*" capture="environment" className="text-sm" onChange={(e) => setPhoto(e.target.files?.[0] ?? null)} aria-label="Photo" />
        <Textarea className="col-span-2" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} placeholder={code ? `What happened (scan: ${code})` : "What happened"} />
        <Button variant="destructive" className="col-span-2 h-12" disabled={busy || notes.trim().length < 3} onClick={escalate}>Escalate now</Button>
      </div>
      {data && data.length > 0 && (
        <ul className="divide-y rounded-md border text-sm">
          {data.slice(0, 5).map((i) => (
            <li key={i.id} className="p-3">
              <div className="font-medium">{KINDS[i.kind]}{i.assetName ? ` · ${i.assetName}` : i.code ? ` · ${i.code}` : ""}</div>
              <div className="text-xs text-muted-foreground">{new Date(i.createdAt).toLocaleString()} · {i.officer}: {i.notes}</div>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
