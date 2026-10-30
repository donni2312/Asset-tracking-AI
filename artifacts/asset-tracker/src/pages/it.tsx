import { useState } from "react";
import { Link } from "wouter";
import {
  downloadItInventoryExcel,
  downloadItRefreshExcel,
  useGetItInventory,
  useGetItRefresh,
  useGetItStatusBoard,
  useListDepartments,
  useListLocations,
  type ItInventoryRow,
} from "@workspace/api-client-react";
import { Layout } from "@/components/layout";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { SbuBadge } from "@/components/sbu-badge";
import { useCurrentUser } from "@/hooks/use-auth";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS } from "@/lib/constants";
import { saveBlob, STATUS_LABEL } from "@/lib/finance";

function Rows({ rows, refresh }: { rows: ItInventoryRow[] | undefined; refresh?: boolean }) {
  return (
    <div className="overflow-x-auto rounded-md border bg-card">
      <Table>
        <TableHeader><TableRow>
          <TableHead>Asset</TableHead><TableHead>Type</TableHead><TableHead>Make / model</TableHead><TableHead>SBU</TableHead><TableHead>Site</TableHead>
          <TableHead>Assigned to</TableHead><TableHead>Status</TableHead><TableHead>Last seen</TableHead><TableHead className="text-right">Age</TableHead>
          {refresh ? <TableHead>Why</TableHead> : <TableHead>Warranty</TableHead>}
        </TableRow></TableHeader>
        <TableBody>
          {rows?.map((r) => (
            <TableRow key={r.assetId}>
              <TableCell className="min-w-56"><Link href={`/assets/${r.assetId}/card`} className="text-primary hover:underline">{r.description}</Link><div className="font-mono text-xs text-muted-foreground">{r.assetNumber ?? "number pending"}</div></TableCell>
              <TableCell>{r.subCategory}</TableCell>
              <TableCell>{[r.make, r.model].filter(Boolean).join(" ")}</TableCell>
              <TableCell>{r.sbuCode && <SbuBadge code={r.sbuCode} />}</TableCell>
              <TableCell>{r.site ?? "off premises"}</TableCell>
              <TableCell>{r.assignedTo}</TableCell>
              <TableCell>{STATUS_LABEL[r.status as keyof typeof STATUS_LABEL] ?? r.status}</TableCell>
              <TableCell className="text-xs">{r.lastSeen ? new Date(r.lastSeen).toLocaleDateString() : "—"}</TableCell>
              <TableCell className="text-right font-mono">{r.ageYears ?? "—"}</TableCell>
              <TableCell className="text-xs">{refresh ? r.refreshReasons.join("; ") : r.warrantyEnds ?? "—"}</TableCell>
            </TableRow>
          ))}
          {rows?.length === 0 && <TableRow><TableCell colSpan={10} className="text-center text-sm text-muted-foreground">No devices.</TableCell></TableRow>}
        </TableBody>
      </Table>
    </div>
  );
}

/** GI-01 inventory, GI-03 refresh report, SI-06 status board: Group IT sees every SBU, SBU IT its own. */
export default function ItViews() {
  const user = useCurrentUser();
  const { toast } = useToast();
  const [filter, setFilter] = useState({ type: "", make: "", model: "", minAgeYears: "", status: "" });
  const [board, setBoard] = useState({ departmentId: "", locationId: "" });
  const params = {
    type: filter.type || undefined, make: filter.make || undefined, model: filter.model || undefined,
    minAgeYears: filter.minAgeYears ? Number(filter.minAgeYears) : undefined, status: filter.status || undefined,
  };
  const { data: inventory } = useGetItInventory(params, { query: { enabled: !!user, queryKey: ["it-inventory", params] } });
  const { data: refresh } = useGetItRefresh(undefined, { query: { enabled: !!user, queryKey: ["it-refresh"] } });
  const boardParams = { departmentId: board.departmentId ? Number(board.departmentId) : undefined, locationId: board.locationId ? Number(board.locationId) : undefined };
  const { data: status } = useGetItStatusBoard(boardParams, { query: { enabled: !!user, queryKey: ["it-board", boardParams] } });
  const { data: departments } = useListDepartments(undefined, { query: { enabled: !!user, queryKey: ["departments"] } });
  const { data: locations } = useListLocations(user?.sbuId ? { sbuId: user.sbuId } : undefined);
  const set = (k: keyof typeof filter) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setFilter({ ...filter, [k]: e.target.value });

  async function download(which: "inventory" | "refresh") {
    try {
      saveBlob(which === "inventory" ? await downloadItInventoryExcel(params) : await downloadItRefreshExcel(), `it-${which}.xlsx`);
    } catch (e) {
      toast({ title: "Export failed", description: apiErrorMessage(e), variant: "destructive" });
    }
  }

  return (
    <Layout>
      <div className="space-y-5">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">IT Inventory</h1>
          <p className="text-sm text-muted-foreground">Every IT device in your scope, the hardware due for refresh, and what is ready to issue.</p>
        </div>
        <Tabs defaultValue="inventory">
          <TabsList>
            <TabsTrigger value="inventory">Inventory</TabsTrigger>
            <TabsTrigger value="refresh">Refresh {refresh?.length ? `(${refresh.length})` : ""}</TabsTrigger>
            <TabsTrigger value="status">Status board</TabsTrigger>
          </TabsList>
          <TabsContent value="inventory" className="space-y-3">
            <div className="flex flex-wrap gap-2">
              <Input className="w-36" value={filter.type} onChange={set("type")} placeholder="Type (Laptop…)" />
              <Input className="w-36" value={filter.make} onChange={set("make")} placeholder="Make" />
              <Input className="w-36" value={filter.model} onChange={set("model")} placeholder="Model" />
              <Input className="w-36" type="number" value={filter.minAgeYears} onChange={set("minAgeYears")} placeholder="Min. age (years)" />
              <select className={SELECT_CLASS} value={filter.status} onChange={set("status")} aria-label="Status">
                <option value="">All statuses</option>
                {Object.entries(STATUS_LABEL).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
              </select>
              <Button variant="outline" size="sm" onClick={() => download("inventory")}>Excel</Button>
            </div>
            <Rows rows={inventory} />
          </TabsContent>
          <TabsContent value="refresh" className="space-y-3">
            <div className="flex justify-between gap-2">
              <p className="text-sm text-muted-foreground">Past the refresh age set by Group IT (by type), or out of warranty. Warranty alerts go out 60 days ahead.</p>
              <Button variant="outline" size="sm" onClick={() => download("refresh")}>Excel</Button>
            </div>
            <Rows rows={refresh} refresh />
          </TabsContent>
          <TabsContent value="status" className="space-y-3">
            <div className="flex flex-wrap gap-2">
              <select className={SELECT_CLASS} value={board.departmentId} onChange={(e) => setBoard({ ...board, departmentId: e.target.value })} aria-label="Department">
                <option value="">All departments</option>
                {departments?.map((d) => <option key={d.id} value={d.id}>{d.sbuCode} · {d.name}</option>)}
              </select>
              <select className={SELECT_CLASS} value={board.locationId} onChange={(e) => setBoard({ ...board, locationId: e.target.value })} aria-label="Location">
                <option value="">All locations</option>
                {locations?.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
              </select>
            </div>
            <div className="grid grid-cols-2 gap-3 md:grid-cols-4 xl:grid-cols-6">
              {status?.counts.filter((c) => c.count > 0 || ["in_use", "in_store", "in_repair", "out_on_gate_pass"].includes(c.status)).map((c) => (
                <div key={c.status} className="rounded-md border bg-card p-3">
                  <div className="text-xs text-muted-foreground">{STATUS_LABEL[c.status as keyof typeof STATUS_LABEL] ?? c.status}</div>
                  <div className="font-mono text-2xl font-bold">{c.count}</div>
                </div>
              ))}
              <div className="rounded-md border bg-card p-3">
                <div className="text-xs text-muted-foreground">Faulty (poor / damaged)</div>
                <div className="font-mono text-2xl font-bold text-amber-600">{status?.faulty ?? 0}</div>
              </div>
            </div>
            <h2 className="font-semibold">Spare stock ready to issue</h2>
            <Rows rows={status?.spare} />
          </TabsContent>
        </Tabs>
      </div>
    </Layout>
  );
}
