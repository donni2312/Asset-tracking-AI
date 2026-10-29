import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import {
  useCreatePurchaseOrder, useIssueEquipment, useListAssets, useListCategories, useListEmployees, useListEquipmentRequests, useListPurchaseOrders, useListStock,
  useReceiveGoods, useReceiveReturn, useRequestEquipment, type PurchaseOrder, type ReturnInputCondition,
} from "@workspace/api-client-react";
import { Layout } from "@/components/layout";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useCurrentUser } from "@/hooks/use-auth";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS } from "@/lib/constants";
import { inGroup } from "@/lib/roles";

function useNotify() {
  const { toast } = useToast();
  const qc = useQueryClient();
  return {
    ok: (title: string) => { toast({ title }); qc.invalidateQueries(); },
    fail: (title: string) => (e: unknown) => toast({ title, description: apiErrorMessage(e), variant: "destructive" }),
  };
}

/**
 * Stores & Receiving (Phase 4b, ST-01 … ST-05): purchase orders (entered by SBU Finance until Business Central's feed),
 * deliveries counted against them, stock in store, equipment requests and issue against an approved one, returns.
 */
export default function Stores() {
  const user = useCurrentUser();
  const requester = inGroup(user, "everyone") && ["group_it", "sbu_it", "hod", "sbu_hr"].includes(user?.role ?? "");
  return (
    <Layout>
      <div className="space-y-5">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Stores &amp; Receiving</h1>
          <p className="text-sm text-muted-foreground">Receive against a PO, register and tag, issue against an approved request, take returns back.</p>
        </div>
        <Tabs defaultValue={user?.role === "stores" ? "orders" : "requests"}>
          <TabsList className="flex-wrap">
            <TabsTrigger value="orders">Purchase orders</TabsTrigger>
            <TabsTrigger value="stock">Stock</TabsTrigger>
            <TabsTrigger value="requests">Equipment requests</TabsTrigger>
            {user?.role === "stores" && <TabsTrigger value="returns">Returns</TabsTrigger>}
          </TabsList>
          <TabsContent value="orders"><Orders /></TabsContent>
          <TabsContent value="stock"><Stock /></TabsContent>
          <TabsContent value="requests"><Requests canRequest={requester} /></TabsContent>
          {user?.role === "stores" && <TabsContent value="returns"><Returns /></TabsContent>}
        </Tabs>
      </div>
    </Layout>
  );
}

function Orders() {
  const user = useCurrentUser();
  const { data } = useListPurchaseOrders();
  const finance = user?.role === "sbu_finance" || user?.role === "group_finance";
  return (
    <div className="space-y-4">
      {finance && <NewOrder />}
      {(data ?? []).map((po) => <OrderCard key={po.id} po={po} canReceive={user?.role === "stores"} />)}
      {data?.length === 0 && <p className="text-sm text-muted-foreground">No purchase orders yet.</p>}
    </div>
  );
}

function NewOrder() {
  const { data: categories } = useListCategories();
  const create = useCreatePurchaseOrder();
  const n = useNotify();
  const [form, setForm] = useState({ poNo: "", supplier: "", categoryId: "", description: "", quantity: "1", unitCost: "" });
  return (
    <form className="flex flex-wrap items-end gap-2 rounded-md border bg-card p-3" onSubmit={(e) => {
      e.preventDefault();
      create.mutate({ data: { poNo: form.poNo, supplier: form.supplier, lines: [{ categoryId: Number(form.categoryId), description: form.description, quantity: Number(form.quantity), unitCost: form.unitCost ? Number(form.unitCost) : undefined }] } },
        { onSuccess: () => n.ok("Purchase order entered"), onError: n.fail("Not entered") });
    }}>
      <Input className="w-40" placeholder="PO number" value={form.poNo} onChange={(e) => setForm({ ...form, poNo: e.target.value })} required />
      <Input className="w-44" placeholder="Supplier" value={form.supplier} onChange={(e) => setForm({ ...form, supplier: e.target.value })} required />
      <select className={SELECT_CLASS + " w-48"} value={form.categoryId} onChange={(e) => setForm({ ...form, categoryId: e.target.value })} required aria-label="Category">
        <option value="">Category…</option>
        {(categories ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
      </select>
      <Input className="w-48" placeholder="Item" value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} required />
      <Input className="w-20" type="number" min={1} value={form.quantity} onChange={(e) => setForm({ ...form, quantity: e.target.value })} aria-label="Quantity" />
      <Input className="w-28" type="number" step="0.01" placeholder="Unit cost" value={form.unitCost} onChange={(e) => setForm({ ...form, unitCost: e.target.value })} />
      <Button type="submit" disabled={create.isPending}>Enter PO</Button>
    </form>
  );
}

function OrderCard({ po, canReceive }: { po: PurchaseOrder; canReceive: boolean }) {
  const receive = useReceiveGoods();
  const n = useNotify();
  const [counts, setCounts] = useState<Record<number, { received: string; damaged: string; serials: string }>>({});
  const open = po.status === "open" || po.status === "partly_received";
  return (
    <section className="rounded-md border bg-card p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-semibold">{po.poNo} <span className="text-sm font-normal text-muted-foreground">· {po.supplier} · {po.sbuCode}</span></h2>
        <span className="text-xs text-muted-foreground">{po.status.replace("_", " ")}</span>
      </div>
      <Table>
        <TableHeader><TableRow><TableHead>Item</TableHead><TableHead className="text-right">Ordered</TableHead><TableHead className="text-right">Received</TableHead>
          {canReceive && open && <><TableHead>Received now</TableHead><TableHead>Damaged</TableHead><TableHead>Serials (comma-separated)</TableHead></>}</TableRow></TableHeader>
        <TableBody>
          {po.lines.map((l) => (
            <TableRow key={l.id}>
              <TableCell>{l.description} <span className="text-xs text-muted-foreground">· {l.categoryName}</span></TableCell>
              <TableCell className="text-right">{l.quantity}</TableCell>
              <TableCell className="text-right">{l.receivedQuantity}</TableCell>
              {canReceive && open && (
                <>
                  <TableCell><Input className="w-20" type="number" min={0} max={l.quantity - l.receivedQuantity} value={counts[l.id]?.received ?? ""} onChange={(e) => setCounts({ ...counts, [l.id]: { ...counts[l.id], received: e.target.value } })} /></TableCell>
                  <TableCell><Input className="w-20" type="number" min={0} value={counts[l.id]?.damaged ?? ""} onChange={(e) => setCounts({ ...counts, [l.id]: { ...counts[l.id], damaged: e.target.value } })} /></TableCell>
                  <TableCell><Input className="w-56" value={counts[l.id]?.serials ?? ""} onChange={(e) => setCounts({ ...counts, [l.id]: { ...counts[l.id], serials: e.target.value } })} /></TableCell>
                </>
              )}
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {canReceive && open && (
        <Button className="mt-2" size="sm" disabled={receive.isPending} onClick={() => receive.mutate({
          id: po.id,
          data: { lines: Object.entries(counts).filter(([, c]) => Number(c.received) > 0).map(([id, c]) => ({
            purchaseOrderLineId: Number(id), quantityReceived: Number(c.received), quantityDamaged: Number(c.damaged || 0),
            serials: (c.serials ?? "").split(",").map((s) => s.trim()).filter(Boolean),
          })) },
        }, { onSuccess: () => { n.ok("Delivery recorded"); setCounts({}); }, onError: n.fail("Not recorded") })}>Record delivery</Button>
      )}
      {po.receipts.map((r) => (
        <p key={r.id} className={`mt-2 text-xs ${r.flagged ? "text-amber-600" : "text-muted-foreground"}`}>
          {new Date(r.receivedAt).toLocaleString("en-GB")} · {r.receivedBy}{r.flagged ? ` · flagged: ${r.flagReason}` : ""}
          {r.lines.map((l) => ` · ${l.description}: ${l.registered}/${l.quantityReceived - l.quantityDamaged} registered (receipt line ${l.id})`).join("")}
        </p>
      ))}
    </section>
  );
}

function Stock() {
  const { data } = useListStock();
  return (
    <Table>
      <TableHeader><TableRow><TableHead>SBU</TableHead><TableHead>Category</TableHead><TableHead>Type</TableHead><TableHead>Location</TableHead><TableHead className="text-right">In store</TableHead><TableHead className="text-right">Untagged</TableHead></TableRow></TableHeader>
      <TableBody>
        {(data ?? []).map((s, i) => (
          <TableRow key={i}><TableCell>{s.sbuCode}</TableCell><TableCell>{s.category}</TableCell><TableCell>{s.subCategory ?? "—"}</TableCell><TableCell>{s.location ?? "—"}</TableCell>
            <TableCell className="text-right">{s.count}</TableCell><TableCell className="text-right">{s.untagged}</TableCell></TableRow>
        ))}
        {data?.length === 0 && <TableRow><TableCell colSpan={6} className="text-center text-sm text-muted-foreground">Nothing in store in your scope.</TableCell></TableRow>}
      </TableBody>
    </Table>
  );
}

function Requests({ canRequest }: { canRequest: boolean }) {
  const user = useCurrentUser();
  const { data } = useListEquipmentRequests();
  const issuer = inGroup(user, "lifecycleWriters");
  return (
    <div className="space-y-4">
      {canRequest && <NewRequest />}
      <Table>
        <TableHeader><TableRow><TableHead>Request</TableHead><TableHead>For</TableHead><TableHead>Status</TableHead><TableHead>Issued</TableHead><TableHead /></TableRow></TableHeader>
        <TableBody>
          {(data ?? []).map((r) => (
            <TableRow key={r.id}>
              <TableCell>{r.quantity} × {r.itemType} <span className="text-xs text-muted-foreground">· {r.categoryName} · {r.reason}</span></TableCell>
              <TableCell>{r.employeeName ?? r.departmentName ?? "—"}</TableCell>
              <TableCell>{r.status}</TableCell>
              <TableCell>{r.issuedQuantity}/{r.quantity}</TableCell>
              <TableCell>{issuer && r.status === "approved" && <IssueForm requestId={r.id} />}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

function NewRequest() {
  const { data: categories } = useListCategories();
  const { data: employees } = useListEmployees();
  const request = useRequestEquipment();
  const n = useNotify();
  const [form, setForm] = useState({ employeeId: "", categoryId: "", itemType: "", quantity: "1", reason: "", urgency: "normal" });
  return (
    <form className="flex flex-wrap items-end gap-2 rounded-md border bg-card p-3" onSubmit={(e) => {
      e.preventDefault();
      request.mutate({ data: { employeeId: form.employeeId ? Number(form.employeeId) : undefined, categoryId: Number(form.categoryId), itemType: form.itemType, quantity: Number(form.quantity), reason: form.reason, urgency: form.urgency as "normal" } },
        { onSuccess: () => n.ok("Request sent for approval"), onError: n.fail("Not sent") });
    }}>
      <select className={SELECT_CLASS + " w-48"} value={form.employeeId} onChange={(e) => setForm({ ...form, employeeId: e.target.value })} aria-label="For employee">
        <option value="">For the department</option>
        {(employees ?? []).map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
      </select>
      <select className={SELECT_CLASS + " w-44"} value={form.categoryId} onChange={(e) => setForm({ ...form, categoryId: e.target.value })} required aria-label="Category">
        <option value="">Category…</option>
        {(categories ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
      </select>
      <Input className="w-40" placeholder="Item" value={form.itemType} onChange={(e) => setForm({ ...form, itemType: e.target.value })} required />
      <Input className="w-16" type="number" min={1} value={form.quantity} onChange={(e) => setForm({ ...form, quantity: e.target.value })} aria-label="Quantity" />
      <Input className="w-56" placeholder="Reason" value={form.reason} onChange={(e) => setForm({ ...form, reason: e.target.value })} required minLength={3} />
      <select className={SELECT_CLASS + " w-28"} value={form.urgency} onChange={(e) => setForm({ ...form, urgency: e.target.value })} aria-label="Urgency">
        <option value="low">Low</option><option value="normal">Normal</option><option value="high">High</option>
      </select>
      <Button type="submit" disabled={request.isPending}>Request</Button>
    </form>
  );
}

function IssueForm({ requestId }: { requestId: number }) {
  const { data: assets } = useListAssets({ status: "in_store" }, { query: { queryKey: ["assets", "in_store"] } });
  const issue = useIssueEquipment();
  const n = useNotify();
  const [assetId, setAssetId] = useState("");
  return (
    <form className="flex gap-2" onSubmit={(e) => { e.preventDefault(); issue.mutate({ id: requestId, data: { assetId: Number(assetId) } }, { onSuccess: () => n.ok("Issued; the employee is asked to acknowledge it"), onError: n.fail("Not issued") }); }}>
      <select className={SELECT_CLASS + " w-56"} value={assetId} onChange={(e) => setAssetId(e.target.value)} required aria-label="Item in store">
        <option value="">Item in store…</option>
        {(assets ?? []).map((a) => <option key={a.id} value={a.id}>{a.assetNumber ?? "pending"} · {a.name}{a.rfidTag ? "" : " (untagged)"}</option>)}
      </select>
      <Button size="sm" type="submit" disabled={issue.isPending}>Issue</Button>
    </form>
  );
}

function Returns() {
  const { data: assets } = useListAssets();
  const take = useReceiveReturn();
  const n = useNotify();
  const [form, setForm] = useState({ assetId: "", condition: "good", notes: "" });
  return (
    <form className="flex flex-wrap items-end gap-2" onSubmit={(e) => {
      e.preventDefault();
      take.mutate({ id: Number(form.assetId), data: { condition: form.condition as ReturnInputCondition, notes: form.notes || undefined } }, { onSuccess: () => n.ok("Return recorded"), onError: n.fail("Not recorded") });
    }}>
      <select className={SELECT_CLASS + " w-72"} value={form.assetId} onChange={(e) => setForm({ ...form, assetId: e.target.value })} required aria-label="Returned item">
        <option value="">Returned item…</option>
        {(assets ?? []).map((a) => <option key={a.id} value={a.id}>{a.assetNumber ?? "pending"} · {a.name}</option>)}
      </select>
      <select className={SELECT_CLASS + " w-36"} value={form.condition} onChange={(e) => setForm({ ...form, condition: e.target.value })} aria-label="Condition">
        <option value="good">Good (back to stock)</option><option value="repair">Needs repair</option><option value="scrap">Scrap</option>
      </select>
      <Input className="w-64" placeholder="Notes" value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
      <Button type="submit" disabled={take.isPending}>Record return</Button>
      <p className="w-full text-xs text-muted-foreground">IT items are taken back only once SBU IT has recommended them for disposal (ST-05); other IT returns go to SBU IT.</p>
    </form>
  );
}
