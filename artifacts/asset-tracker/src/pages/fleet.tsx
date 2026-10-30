import { useState } from "react";
import { Link, useParams } from "wouter";
import { useQueryClient } from "@tanstack/react-query";
import {
  getGetFleetItemQueryKey,
  useAddVehicleDocument,
  useGetFleetItem,
  useListEmployees,
  useListFleet,
  useLogService,
  useSetServiceCost,
  useSetServiceSchedule,
  useUpdateFleetItem,
  type FleetItem,
  type FleetUpdateInputFleetStatus,
  type VehicleDocumentInputKind,
} from "@workspace/api-client-react";
import { Layout } from "@/components/layout";
import { SbuBadge } from "@/components/sbu-badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useCurrentUser } from "@/hooks/use-auth";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS } from "@/lib/constants";
import { money } from "@/lib/finance";

const FLEET_STATUS: Record<string, string> = { active: "Active", in_service: "In service", off_road: "Off road" };
const DOC_LABEL: Record<string, string> = { licence: "Licence", insurance: "Insurance", fitness: "Fitness", zinara: "ZINARA" };
const DOC_CLASS: Record<string, string> = {
  ok: "bg-green-500/10 text-green-700", due_30: "bg-amber-500/10 text-amber-700", due_7: "bg-orange-500/20 text-orange-700", expired: "bg-red-600 text-white",
};
const DUE_CLASS: Record<string, string> = { ok: "text-muted-foreground", due_soon: "text-amber-600", overdue: "font-semibold text-red-600" };

function useNotice() {
  const { toast } = useToast();
  const qc = useQueryClient();
  return {
    ok: (title: string) => { toast({ title }); qc.invalidateQueries(); },
    fail: (title: string) => (e: unknown) => toast({ title, description: apiErrorMessage(e), variant: "destructive" }),
  };
}

function Documents({ item }: { item: FleetItem }) {
  if (item.documents.length === 0) return <span className="text-xs text-muted-foreground">none recorded</span>;
  return (
    <div className="flex flex-wrap gap-1">
      {item.documents.map((d) => (
        <span key={d.id} className={`rounded px-1.5 py-0.5 text-xs ${DOC_CLASS[d.state]}`} title={`${d.reference ?? ""} expires ${d.expiresOn}`}>
          {DOC_LABEL[d.kind]} {d.state === "expired" ? "expired" : d.state === "ok" ? "" : `${d.daysLeft}d`}
        </span>
      ))}
    </div>
  );
}

function Due({ item }: { item: FleetItem }) {
  const due = item.nextService;
  if (!due) return <span className="text-xs text-muted-foreground">no schedule</span>;
  const parts = [due.dueReading != null ? `${due.dueReading.toLocaleString()} ${item.readingUnit}` : null, due.dueDate].filter(Boolean);
  return <span className={`text-sm ${DUE_CLASS[due.state]}`}>{parts.join(" or ")}{due.state === "overdue" ? " — overdue" : ""}</span>;
}

/** AM-02: the SBU's vehicles (and generators) with driver, depot, status and mileage; red when a document has expired. */
export default function Fleet() {
  const user = useCurrentUser();
  const [kind, setKind] = useState<"vehicles" | "generators" | "all">("vehicles");
  const { data, isLoading, error } = useListFleet({ kind }, { query: { enabled: !!user, queryKey: ["fleet", kind] } });
  const expired = data?.filter((v) => v.documentsExpired).length ?? 0;
  const overdue = data?.filter((v) => v.nextService?.state === "overdue").length ?? 0;

  return (
    <Layout>
      <div className="space-y-5">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Fleet &amp; Non-IT</h1>
            <p className="text-sm text-muted-foreground">
              {user?.role === "sbu_admin" ? "Your SBU's vehicles and generators" : "Read-only roll-up across the SBUs you may see"} · {expired} with expired documents · {overdue} service overdue
            </p>
          </div>
          <select className={SELECT_CLASS} value={kind} onChange={(e) => setKind(e.target.value as typeof kind)} aria-label="Show">
            <option value="vehicles">Vehicles</option>
            <option value="generators">Generators &amp; plant</option>
            <option value="all">All</option>
          </select>
        </div>
        {error && <p className="text-sm text-red-600">{apiErrorMessage(error)}</p>}
        <section className="overflow-x-auto rounded-md border bg-card">
          <Table>
            <TableHeader><TableRow>
              <TableHead>Reg / asset</TableHead><TableHead>SBU</TableHead><TableHead>Driver</TableHead><TableHead>Depot</TableHead><TableHead>Status</TableHead>
              <TableHead className="text-right">Reading</TableHead><TableHead>Documents</TableHead><TableHead>Next service</TableHead>
            </TableRow></TableHeader>
            <TableBody>
              {data?.map((v) => (
                <TableRow key={v.assetId} className={v.documentsExpired ? "bg-red-500/10" : ""}>
                  <TableCell className="min-w-56">
                    <Link href={`/fleet/${v.assetId}`} className="font-medium text-primary hover:underline">{v.registrationNumber ?? v.assetNumber ?? v.name}</Link>
                    <div className="text-xs text-muted-foreground">{v.makeModel || v.name}</div>
                  </TableCell>
                  <TableCell>{v.sbuCode && <SbuBadge code={v.sbuCode} />}</TableCell>
                  <TableCell>{v.driver ?? "—"}</TableCell>
                  <TableCell>{v.depot ?? "off premises"}</TableCell>
                  <TableCell>{FLEET_STATUS[v.fleetStatus]}</TableCell>
                  <TableCell className="text-right font-mono">{v.reading != null ? `${v.reading.toLocaleString()} ${v.readingUnit}` : "—"}</TableCell>
                  <TableCell><Documents item={v} /></TableCell>
                  <TableCell><Due item={v} /></TableCell>
                </TableRow>
              ))}
              {!isLoading && data?.length === 0 && <TableRow><TableCell colSpan={8} className="text-center text-sm text-muted-foreground">Nothing in the fleet register.</TableCell></TableRow>}
            </TableBody>
          </Table>
        </section>
      </div>
    </Layout>
  );
}

/** One vehicle: status and driver, documents, service schedule and history (AM-02 … AM-04). */
export function FleetItemPage() {
  const { id } = useParams<{ id: string }>();
  const assetId = Number(id);
  const user = useCurrentUser();
  const n = useNotice();
  const { data: v, error } = useGetFleetItem(assetId, { query: { enabled: !!user, queryKey: getGetFleetItemQueryKey(assetId) } });
  const { data: employees } = useListEmployees(undefined, { query: { enabled: !!v?.canEdit, queryKey: ["employees"] } });
  const update = useUpdateFleetItem();
  const addDoc = useAddVehicleDocument();
  const schedule = useSetServiceSchedule();
  const log = useLogService();
  const cost = useSetServiceCost();
  const [doc, setDoc] = useState({ kind: "licence", reference: "", expiresOn: "" });
  const [plan, setPlan] = useState({ intervalReading: "", intervalDays: "" });
  const [service, setService] = useState({ serviceDate: "", reading: "", garage: "", workDone: "" });
  const [reading, setReading] = useState("");
  const [costs, setCosts] = useState<Record<number, string>>({});
  const finance = user?.role === "sbu_finance" || user?.role === "system_admin";

  if (error) return <Layout><p className="text-sm text-red-600">{apiErrorMessage(error)}</p></Layout>;
  if (!v) return <Layout><p className="text-sm text-muted-foreground">Loading…</p></Layout>;
  const ok = (title: string) => () => n.ok(title);

  return (
    <Layout>
      <div className="space-y-5">
        <div>
          <Link href="/fleet" className="text-sm text-primary hover:underline">← Fleet</Link>
          <h1 className="text-2xl font-bold tracking-tight">{v.registrationNumber ?? v.name}</h1>
          <p className="text-sm text-muted-foreground">
            {v.name} · <Link href={`/assets/${v.assetId}/card`} className="text-primary hover:underline">{v.assetNumber ?? "asset card"}</Link> · {v.depot ?? "off premises"}
          </p>
          {v.documentsExpired && <p className="mt-2 rounded-md bg-red-600 p-2 text-sm font-medium text-white">A document has expired: this vehicle must not be driven until it is renewed.</p>}
        </div>

        <section className="grid gap-4 rounded-md border bg-card p-4 md:grid-cols-3">
          <div>
            <div className="text-xs text-muted-foreground">Status</div>
            {v.canEdit ? (
              <select className={SELECT_CLASS} value={v.fleetStatus} onChange={(e) => update.mutate({ assetId, data: { fleetStatus: e.target.value as FleetUpdateInputFleetStatus } }, { onSuccess: ok("Status saved"), onError: n.fail("Not saved") })}>
                {Object.entries(FLEET_STATUS).map(([k, label]) => <option key={k} value={k}>{label}</option>)}
              </select>
            ) : <div className="font-medium">{FLEET_STATUS[v.fleetStatus]}</div>}
          </div>
          <div>
            <div className="text-xs text-muted-foreground">Driver</div>
            {v.canEdit ? (
              <select className={SELECT_CLASS} value={v.driverEmployeeId ?? ""} onChange={(e) => update.mutate({ assetId, data: { driverEmployeeId: e.target.value ? Number(e.target.value) : null } }, { onSuccess: ok("Driver saved"), onError: n.fail("Not saved") })}>
                <option value="">— none —</option>
                {employees?.filter((x) => x.sbuId === v.sbuId && x.active).map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
              </select>
            ) : <div className="font-medium">{v.driver ?? "—"}</div>}
          </div>
          <div>
            <div className="text-xs text-muted-foreground">{v.readingUnit === "km" ? "Mileage" : "Hours run"}</div>
            <div className="flex gap-2">
              <span className="font-mono font-medium">{v.reading?.toLocaleString() ?? "—"} {v.readingUnit}</span>
              {v.canEdit && (
                <form className="flex gap-1" onSubmit={(e) => { e.preventDefault(); update.mutate({ assetId, data: { reading: Number(reading) } }, { onSuccess: () => { setReading(""); n.ok("Reading saved"); }, onError: n.fail("Not saved") }); }}>
                  <Input className="h-8 w-28" type="number" value={reading} onChange={(e) => setReading(e.target.value)} placeholder="New reading" />
                  <Button size="sm" type="submit" disabled={!reading}>Save</Button>
                </form>
              )}
            </div>
          </div>
        </section>

        {v.categoryCode === "MV" && (
          <section className="space-y-3 rounded-md border bg-card p-4">
            <h2 className="font-semibold">Documents (AM-03)</h2>
            <Documents item={v} />
            {v.canEdit && (
              <form className="flex flex-wrap gap-2" onSubmit={(e) => {
                e.preventDefault();
                addDoc.mutate({ assetId, data: { kind: doc.kind as VehicleDocumentInputKind, reference: doc.reference || undefined, expiresOn: doc.expiresOn } },
                  { onSuccess: () => { setDoc({ ...doc, reference: "", expiresOn: "" }); n.ok("Document recorded"); }, onError: n.fail("Not recorded") });
              }}>
                <select className={SELECT_CLASS} value={doc.kind} onChange={(e) => setDoc({ ...doc, kind: e.target.value })}>
                  {Object.entries(DOC_LABEL).map(([k, label]) => <option key={k} value={k}>{label}</option>)}
                </select>
                <Input className="w-40" value={doc.reference} onChange={(e) => setDoc({ ...doc, reference: e.target.value })} placeholder="Reference" />
                <Input className="w-40" type="date" value={doc.expiresOn} onChange={(e) => setDoc({ ...doc, expiresOn: e.target.value })} required />
                <Button type="submit" size="sm" disabled={!doc.expiresOn}>Record / renew</Button>
              </form>
            )}
          </section>
        )}

        <section className="space-y-3 rounded-md border bg-card p-4">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 className="font-semibold">Service (AM-04)</h2>
            <div>Next due: <Due item={v} /></div>
          </div>
          {v.canEdit && (
            <form className="flex flex-wrap items-end gap-2" onSubmit={(e) => {
              e.preventDefault();
              schedule.mutate({ assetId, data: { intervalReading: plan.intervalReading ? Number(plan.intervalReading) : undefined, intervalDays: plan.intervalDays ? Number(plan.intervalDays) : undefined } },
                { onSuccess: ok("Schedule saved"), onError: n.fail("Not saved") });
            }}>
              <Input className="w-36" type="number" value={plan.intervalReading} onChange={(e) => setPlan({ ...plan, intervalReading: e.target.value })} placeholder={`Every … ${v.readingUnit}`} />
              <Input className="w-36" type="number" value={plan.intervalDays} onChange={(e) => setPlan({ ...plan, intervalDays: e.target.value })} placeholder="or every … days" />
              <Button type="submit" size="sm" variant="secondary" disabled={!plan.intervalReading && !plan.intervalDays}>Set schedule</Button>
              <span className="text-xs text-muted-foreground">{v.nextService ? `Now: ${v.nextService.intervalReading ?? "—"} ${v.readingUnit} / ${v.nextService.intervalDays ?? "—"} days` : ""}</span>
            </form>
          )}
          {v.canEdit && (
            <form className="flex flex-wrap gap-2" onSubmit={(e) => {
              e.preventDefault();
              log.mutate({ assetId, data: { serviceDate: service.serviceDate, reading: service.reading ? Number(service.reading) : undefined, garage: service.garage, workDone: service.workDone } },
                { onSuccess: () => { setService({ serviceDate: "", reading: "", garage: "", workDone: "" }); n.ok("Service logged; SBU Finance adds the cost"); }, onError: n.fail("Not logged") });
            }}>
              <Input className="w-40" type="date" value={service.serviceDate} onChange={(e) => setService({ ...service, serviceDate: e.target.value })} required />
              <Input className="w-32" type="number" value={service.reading} onChange={(e) => setService({ ...service, reading: e.target.value })} placeholder={v.readingUnit} />
              <Input className="w-44" value={service.garage} onChange={(e) => setService({ ...service, garage: e.target.value })} placeholder="Garage" required />
              <Input className="min-w-56 flex-1" value={service.workDone} onChange={(e) => setService({ ...service, workDone: e.target.value })} placeholder="Work done" required />
              <Button type="submit" size="sm">Log service</Button>
            </form>
          )}
          <Table>
            <TableHeader><TableRow><TableHead>Date</TableHead><TableHead>Reading</TableHead><TableHead>Garage</TableHead><TableHead>Work done</TableHead><TableHead>By</TableHead>{finance || v.services.some((s) => s.cost != null) ? <TableHead className="text-right">Cost</TableHead> : null}</TableRow></TableHeader>
            <TableBody>
              {v.services.map((s) => (
                <TableRow key={s.id}>
                  <TableCell>{s.serviceDate}</TableCell>
                  <TableCell className="font-mono">{s.reading?.toLocaleString() ?? "—"}</TableCell>
                  <TableCell>{s.garage}</TableCell>
                  <TableCell>{s.workDone}</TableCell>
                  <TableCell className="text-xs">{s.loggedBy}</TableCell>
                  {finance ? (
                    <TableCell className="text-right">
                      <form className="flex justify-end gap-1" onSubmit={(e) => { e.preventDefault(); cost.mutate({ id: s.id, data: { cost: Number(costs[s.id]), currency: "USD" } }, { onSuccess: ok("Cost saved"), onError: n.fail("Not saved") }); }}>
                        <Input className="h-8 w-28 text-right" type="number" step="0.01" value={costs[s.id] ?? s.cost?.toString() ?? ""} onChange={(e) => setCosts({ ...costs, [s.id]: e.target.value })} placeholder="USD" />
                        <Button size="sm" type="submit" variant="outline" disabled={!costs[s.id]}>Save</Button>
                      </form>
                    </TableCell>
                  ) : s.cost != null ? <TableCell className="text-right font-mono">{money(s.cost)}</TableCell> : null}
                </TableRow>
              ))}
              {v.services.length === 0 && <TableRow><TableCell colSpan={6} className="text-center text-sm text-muted-foreground">No services logged.</TableCell></TableRow>}
            </TableBody>
          </Table>
        </section>
      </div>
    </Layout>
  );
}
