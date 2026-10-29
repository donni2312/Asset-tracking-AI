import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useCreateLocation, useCreateRfidReader, useListLocations, useListRfidReaders, useListSbus, useUpdateRfidReader } from "@workspace/api-client-react";
import { SbuBadge } from "@/components/sbu-badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS } from "@/lib/constants";

/**
 * Gates and their RFID readers per SBU (System Administrator, product owner 29 Oct 2026). A Gate Officer covers
 * every gate of their SBU; each read is processed by the reader's gate.
 */
export function GatesTab() {
  const { data: gates } = useListLocations({ type: "gate" }, { query: { queryKey: ["locations", "gates"] } });
  const { data: readers } = useListRfidReaders();
  const { data: sbus } = useListSbus();
  const createGate = useCreateLocation();
  const createReader = useCreateRfidReader();
  const updateReader = useUpdateRfidReader();
  const qc = useQueryClient();
  const { toast } = useToast();
  const [gate, setGate] = useState({ name: "", sbuId: "", building: "" });
  const [reader, setReader] = useState({ name: "", locationId: "" });
  const done = (title: string) => { toast({ title }); qc.invalidateQueries(); };
  const fail = (title: string) => (e: unknown) => toast({ title, description: apiErrorMessage(e), variant: "destructive" });

  return (
    <div className="space-y-4">
      <p className="text-xs text-muted-foreground">{gates?.length ?? 0} gates across {new Set((gates ?? []).map((g) => g.sbuId)).size} SBUs. Every change is audit-logged.</p>
      <form className="flex flex-wrap items-end gap-2" onSubmit={(e) => {
        e.preventDefault();
        createGate.mutate({ data: { name: gate.name, type: "gate", building: gate.building, sbuId: Number(gate.sbuId) } },
          { onSuccess: () => { done("Gate added"); setGate({ name: "", sbuId: "", building: "" }); }, onError: fail("Gate not added") });
      }}>
        <Input className="w-48" placeholder="Gate name" value={gate.name} onChange={(e) => setGate({ ...gate, name: e.target.value })} required />
        <select className={SELECT_CLASS + " w-32"} value={gate.sbuId} onChange={(e) => setGate({ ...gate, sbuId: e.target.value })} required aria-label="SBU">
          <option value="">SBU…</option>
          {(sbus ?? []).map((s) => <option key={s.id} value={s.id}>{s.code}</option>)}
        </select>
        <Input className="w-44" placeholder="Building / site" value={gate.building} onChange={(e) => setGate({ ...gate, building: e.target.value })} required />
        <Button type="submit" disabled={createGate.isPending}>Add gate</Button>
      </form>
      <form className="flex flex-wrap items-end gap-2" onSubmit={(e) => {
        e.preventDefault();
        createReader.mutate({ data: { name: reader.name, locationId: Number(reader.locationId) } },
          { onSuccess: () => { done("Reader added"); setReader({ name: "", locationId: "" }); }, onError: fail("Reader not added") });
      }}>
        <Input className="w-48" placeholder="Reader name" value={reader.name} onChange={(e) => setReader({ ...reader, name: e.target.value })} required />
        <select className={SELECT_CLASS + " w-56"} value={reader.locationId} onChange={(e) => setReader({ ...reader, locationId: e.target.value })} required aria-label="Gate">
          <option value="">At gate…</option>
          {(gates ?? []).map((g) => <option key={g.id} value={g.id}>{g.sbuCode} · {g.name}</option>)}
        </select>
        <Button type="submit" variant="outline" disabled={createReader.isPending}>Add reader</Button>
      </form>
      <div className="grid gap-3 sm:grid-cols-2">
        {(gates ?? []).map((g) => (
          <section key={g.id} className="rounded-md border bg-card p-3">
            <div className="flex items-center justify-between gap-2"><span className="font-semibold">{g.name}</span>{g.sbuCode && <SbuBadge code={g.sbuCode} />}</div>
            <div className="text-xs text-muted-foreground">{g.building}</div>
            <ul className="mt-2 space-y-1 text-sm">
              {(readers ?? []).filter((r) => r.locationId === g.id).map((r) => (
                <li key={r.id} className="flex items-center justify-between gap-2">
                  <span>{r.name} <span className="text-xs text-muted-foreground">· {r.status}</span></span>
                  <Button size="sm" variant="ghost" onClick={() => updateReader.mutate({ id: r.id, data: { status: r.status === "active" ? "inactive" : "active" } },
                    { onSuccess: () => done("Reader updated"), onError: fail("Not updated") })}>
                    {r.status === "active" ? "Switch off" : "Switch on"}
                  </Button>
                </li>
              ))}
              {(readers ?? []).every((r) => r.locationId !== g.id) && <li className="text-xs text-muted-foreground">No reader yet</li>}
            </ul>
          </section>
        ))}
      </div>
    </div>
  );
}
