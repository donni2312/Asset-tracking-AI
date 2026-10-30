import { useState } from "react";
import { RevaluationsPanel } from "@/components/revaluations";
import { SecurityChecklist } from "@/components/security-checklist";
import { Link, useParams } from "wouter";
import { useQueryClient } from "@tanstack/react-query";
import {
  downloadAssetCardPdf, downloadDepreciationSchedule, getGetAssetCardQueryKey, useCapitaliseAsset, useChangeAssetCategory, useDecideNoSerial,
  useDecideUpgrade, useGetAssetAudit, useGetAssetCard, useListCategories, useLogAssetUpgrade, useTagAsset, useUpdateAssetFinancial,
  getGetAssetAuditQueryKey, type AssetCard, type AssetUpgrade, type UpgradeDecisionTreatment,
} from "@workspace/api-client-react";
import { Layout } from "@/components/layout";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { SbuBadge } from "@/components/sbu-badge";
import { AttachmentsPanel } from "@/components/attachments";
import { StatusChangeButton } from "@/components/status-change";
import { TemplateFields, filled, type TemplateDraft } from "@/components/template-fields";
import { useToast } from "@/hooks/use-toast";
import { useCurrentUser } from "@/hooks/use-auth";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS } from "@/lib/constants";
import { money, periodLabel, saveBlob, STATUS_LABEL, STATUS_TONE } from "@/lib/finance";
import { cn } from "@/lib/utils";

const Key = ({ children }: { children: React.ReactNode }) => <div className="text-xs text-muted-foreground">{children}</div>;
const Val = ({ children, mono }: { children: React.ReactNode; mono?: boolean }) => (
  <div className={cn("mt-0.5 text-sm", mono && "font-mono")}>{children}</div>
);

/** The one-page asset card (AU-08): identity, cost & value, purchase details, upgrades, schedule, history. */
export default function AssetCardPage() {
  const { id } = useParams();
  const assetId = Number(id);
  const user = useCurrentUser();
  const { toast } = useToast();
  const { data: card, isLoading, error } = useGetAssetCard(assetId, {
    query: { enabled: !!user && assetId > 0, queryKey: getGetAssetCardQueryKey(assetId) },
  });
  const [dialog, setDialog] = useState<null | "audit" | "upgrade" | "financial" | "category" | "tag">(null);

  if (!user) return <Layout><p className="text-sm text-muted-foreground">Sign in to open the asset card.</p></Layout>;
  if (isLoading) return <Layout><div className="h-64 animate-pulse rounded-lg bg-muted" /></Layout>;
  if (error || !card) return <Layout><p className="text-sm text-muted-foreground">{apiErrorMessage(error, "Asset not found.")}</p></Layout>;

  const a = card.asset;
  const v = card.value;
  const fileId = a.assetNumber ?? String(a.id);

  async function download(kind: "pdf" | "xlsx") {
    try {
      const blob = kind === "pdf" ? await downloadAssetCardPdf(assetId) : await downloadDepreciationSchedule(assetId);
      saveBlob(blob, kind === "pdf" ? `asset-card-${fileId}.pdf` : `depreciation-${fileId}.xlsx`);
    } catch (err) {
      toast({ title: "Download failed", description: apiErrorMessage(err), variant: "destructive" });
    }
  }

  return (
    <Layout>
      <div className="space-y-5">
        <div className="text-sm text-muted-foreground">
          <Link href="/register" className="text-primary hover:underline">Asset Register</Link> / {card.categoryName} / <span className="font-mono">{a.assetNumber ?? "pending"}</span>
        </div>

        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              {a.sbuCode && <SbuBadge code={a.sbuCode} />}
              <span className={cn("rounded px-2 py-0.5 text-[11px] font-semibold", STATUS_TONE[a.status])}>{STATUS_LABEL[a.status] ?? a.status}</span>
              {!card.isIt && <span className="rounded bg-muted px-2 py-0.5 text-[11px]">Non-IT</span>}
            </div>
            <h1 className="mt-2 text-xl font-bold tracking-tight sm:text-2xl">{a.name}</h1>
            <p className="mt-1 text-xs text-muted-foreground">Description built from category, make, model and specification fields (AU-01)</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" size="sm" onClick={() => download("pdf")}>Asset card PDF</Button>
            <Button variant="outline" size="sm" onClick={() => setDialog("audit")}>Audit trail</Button>
            {card.can.editTechnical && <Button variant="outline" size="sm" asChild><Link href={`/assets/${a.id}/edit`}>Edit fields</Link></Button>}
            {card.can.tag && <Button variant="outline" size="sm" onClick={() => setDialog("tag")}>{a.rfidTag ? "Re-link tag" : "Link tag"}</Button>}
            {card.can.changeCategory && <Button variant="outline" size="sm" onClick={() => setDialog("category")}>Change category</Button>}
            <StatusChangeButton assetId={a.id} status={a.status} />
            {card.can.logUpgrade && <Button size="sm" onClick={() => setDialog("upgrade")}>+ Log upgrade</Button>}
          </div>
        </div>

        {card.completenessGaps.length > 0 && (
          <div className="rounded-md border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-800 dark:text-amber-200">
            <span className="font-semibold">Incomplete for audit (AU-11):</span> {card.completenessGaps.join(" · ")}
          </div>
        )}

        <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
          <Tile label="Asset number (Business Central, locked)" value={a.assetNumber ?? "Pending"} sub={a.previousAssetNumber ? `Previous no.: ${a.previousAssetNumber}` : undefined} />
          <Tile label="Serial number (unique across the Group)" value={a.serialNumber ?? "No serial"}
            sub={a.serialNumber ? undefined : `${a.noSerialStatus === "approved" ? "Approved" : a.noSerialStatus === "rejected" ? "Rejected" : "Awaiting SBU Finance"}: ${a.noSerialReason ?? ""}`} />
          <Tile label="RFID / QR tag" value={a.rfidTag ?? "Not tagged"} sub={a.currentLocationName ? `Location: ${a.currentLocationName}` : undefined} />
        </div>

        <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
          {v && (
          <section className="rounded-md border bg-card p-4">
            <div className="mb-3 flex items-center justify-between gap-2">
              <h2 className="font-semibold">Cost &amp; value · {periodLabel(v.period)}</h2>
              {card.can.editFinancial && (
                <div className="flex gap-2">
                  <Button variant="outline" size="sm" onClick={() => setDialog("financial")}>Edit</Button>
                  {!v.capitalised && <CapitaliseButton card={card} />}
                </div>
              )}
            </div>
            <div className="grid grid-cols-2 gap-x-5 gap-y-3">
              <div><Key>Original cost</Key><Val mono>{v.capitalised ? money(v.originalCost) : a.originalCost != null ? `${a.originalCost.toLocaleString()} ${a.currency ?? ""} (not capitalised)` : "—"}</Val></div>
              <div><Key>Capitalised upgrades</Key><Val mono>{money(v.capitalisedUpgrades)}</Val></div>
              <div><Key>Total cost</Key><Val mono><span className="font-bold">{money(v.totalCost)}</span></Val></div>
              <div><Key>Currency / rate used</Key><Val>{v.currency ?? "—"} · {v.exchangeRate ?? "—"}</Val></div>
              <div><Key>Depreciation this month</Key><Val mono>{money(v.depreciationMonth)}</Val></div>
              <div><Key>Accumulated depreciation</Key><Val mono>{money(v.accumulatedDepreciation)}</Val></div>
              <div><Key>Net book value</Key><Val mono><span className="font-bold text-primary">{money(v.netBookValue)}</span></Val></div>
              <div><Key>Method / life</Key><Val>{v.depreciationMethod === "straight_line" ? `Straight-line · ${v.usefulLifeMonths} months` : `Reducing balance · ${((v.annualRate ?? 0) * 100).toFixed(0)}% a year`}</Val></div>
            </div>
          </section>
          )}
          <section className="rounded-md border bg-card p-4">
            <h2 className="mb-3 font-semibold">Purchase details</h2>
            <div className="grid grid-cols-2 gap-x-5 gap-y-3">
              <div><Key>Supplier</Key><Val>{a.supplier ?? "—"}</Val></div>
              <div><Key>Invoice no.</Key><Val mono>{a.invoiceNo ?? "—"}</Val></div>
              <div><Key>PO no.</Key><Val mono>{a.poNo ?? "—"}</Val></div>
              <div><Key>Purchase date</Key><Val>{a.purchaseDate ?? "—"}</Val></div>
              <div><Key>In-use date</Key><Val>{a.inUseDate ?? "—"}</Val></div>
              <div><Key>Cost centre</Key><Val mono>{a.costCentre ?? "—"}</Val></div>
              <div><Key>Warranty ends</Key><Val>{a.warrantyEnds ?? "—"}</Val></div>
              <div><Key>Custodian</Key><Val>{a.assignedTo ?? "—"}</Val></div>
              <div><Key>Condition</Key><Val>{a.condition ?? "—"}</Val></div>
            </div>
          </section>
        </div>

        <section className="rounded-md border bg-card p-4">
          <h2 className="mb-3 font-semibold">Specification</h2>
          <div className="grid grid-cols-2 gap-x-5 gap-y-3 sm:grid-cols-4">
            {card.attributes.map((x) => <div key={x.key}><Key>{x.label}</Key><Val>{x.value}{x.unit ? ` ${x.unit}` : ""}</Val></div>)}
            {card.attributes.length === 0 && <p className="col-span-full text-sm text-muted-foreground">No template values recorded yet.</p>}
          </div>
        </section>

        {a.status === "lost_stolen" && <SecurityChecklist assetId={a.id} />}

        <AttachmentsPanel assetId={a.id} canAttach={card.can.attach} />

        {card.value && card.value.capitalised && <RevaluationsPanel assetId={a.id} canRecord={card.can.editFinancial} />}

        <section className="rounded-md border bg-card">
          <div className="p-4">
            <h2 className="font-semibold">Upgrades</h2>
            <p className="text-xs text-muted-foreground">Work that increased value or capacity. SBU Finance decides capitalise or expense (AU-07).</p>
          </div>
          <div className="overflow-x-auto">
            <Table>
              <TableHeader><TableRow>
                <TableHead>Date</TableHead><TableHead>Upgrade</TableHead><TableHead>Before → after</TableHead><TableHead>Logged by</TableHead>
                <TableHead className="text-right">Cost</TableHead><TableHead>Finance decision</TableHead>
              </TableRow></TableHeader>
              <TableBody>
                {card.upgrades.map((u) => (
                  <TableRow key={u.id}>
                    <TableCell className="whitespace-nowrap">{u.upgradeDate}</TableCell>
                    <TableCell>{u.description}</TableCell>
                    <TableCell>{u.beforeText} → {u.afterText}</TableCell>
                    <TableCell>{u.loggedBy}</TableCell>
                    <TableCell className="text-right font-mono">
                      {money(u.cost)}
                      {u.currency !== "USD" && u.invoiceAmount != null && (
                        <div className="text-[11px] text-muted-foreground">{(u.invoiceAmount ?? 0).toLocaleString(undefined, { minimumFractionDigits: 2 })} {u.currency} × {u.exchangeRate}</div>
                      )}
                    </TableCell>
                    <TableCell>{u.status === "pending" && card.can.decideUpgrades ? <UpgradeDecisionButtons upgrade={u} assetId={a.id} /> : <UpgradeStatus u={u} />}</TableCell>
                  </TableRow>
                ))}
                {card.upgrades.length === 0 && <TableRow><TableCell colSpan={6} className="text-center text-sm text-muted-foreground">No upgrades logged.</TableCell></TableRow>}
              </TableBody>
            </Table>
          </div>
        </section>

        {a.noSerialStatus === "pending" && card.can.editFinancial && <NoSerialDecision assetId={a.id} reason={a.noSerialReason ?? ""} />}

        <div className="grid grid-cols-1 gap-3 lg:grid-cols-5">
          {v && (
          <section className="rounded-md border bg-card lg:col-span-3">
            <div className="flex items-center justify-between gap-2 p-4">
              <h2 className="font-semibold">Monthly depreciation schedule</h2>
              <Button variant="outline" size="sm" onClick={() => download("xlsx")} disabled={card.schedule.length === 0}>Export Excel</Button>
            </div>
            <div className="max-h-96 overflow-auto">
              <Table>
                <TableHeader><TableRow>
                  <TableHead>Month</TableHead><TableHead className="text-right">Opening cost</TableHead><TableHead className="text-right">Charge</TableHead>
                  <TableHead className="text-right">Accumulated</TableHead><TableHead className="text-right">NBV</TableHead>
                </TableRow></TableHeader>
                <TableBody>
                  {card.schedule.map((m) => (
                    <TableRow key={m.period}>
                      <TableCell className="whitespace-nowrap">{periodLabel(m.period)}{m.locked && <span className="ml-1 text-[10px] text-muted-foreground">🔒</span>}</TableCell>
                      <TableCell className="text-right font-mono">{money(m.openingCost)}</TableCell>
                      <TableCell className="text-right font-mono">{money(m.charge)}{m.adjustment !== 0 && <span className="block text-[10px] text-amber-600">adj. {money(m.adjustment)}</span>}</TableCell>
                      <TableCell className="text-right font-mono">{money(m.closingAccumulated)}</TableCell>
                      <TableCell className="text-right font-mono">{money(m.closingNbv)}</TableCell>
                    </TableRow>
                  ))}
                  {card.schedule.length === 0 && <TableRow><TableCell colSpan={5} className="text-center text-sm text-muted-foreground">Starts once SBU Finance capitalises the asset.</TableCell></TableRow>}
                </TableBody>
              </Table>
            </div>
          </section>
          )}
          <section className="rounded-md border bg-card p-4 lg:col-span-2">
            <h2 className="mb-3 font-semibold">History</h2>
            <ol className="max-h-96 space-y-3 overflow-auto text-sm">
              {card.timeline.map((e) => (
                <li key={e.id} className="flex gap-2.5">
                  <span className="mt-1.5 h-2 w-2 flex-none rounded-full bg-primary" />
                  <div className="min-w-0">
                    <div>{e.title}</div>
                    {e.detail && <div className="break-words text-xs text-muted-foreground">{e.detail}</div>}
                    <div className="text-xs text-muted-foreground">{new Date(e.at).toLocaleDateString("en-GB", { day: "2-digit", month: "short", year: "numeric" })}{e.by ? ` · ${e.by}` : ""}</div>
                  </div>
                </li>
              ))}
            </ol>
          </section>
        </div>
      </div>

      <AuditDialog open={dialog === "audit"} onClose={() => setDialog(null)} assetId={a.id} />
      <UpgradeDialog open={dialog === "upgrade"} onClose={() => setDialog(null)} card={card} />
      <FinancialDialog open={dialog === "financial"} onClose={() => setDialog(null)} card={card} />
      <CategoryDialog open={dialog === "category"} onClose={() => setDialog(null)} card={card} />
      <TagDialog open={dialog === "tag"} onClose={() => setDialog(null)} card={card} />
    </Layout>
  );
}

function Tile({ label, value, sub }: { label: string; value: string; sub?: string }) {
  return (
    <div className="rounded-md border bg-card p-4">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="mt-1.5 break-all font-mono text-lg font-bold">{value}</div>
      {sub && <div className="mt-1 text-xs text-muted-foreground">{sub}</div>}
    </div>
  );
}

function UpgradeStatus({ u }: { u: AssetUpgrade }) {
  const tone = u.status === "capitalised" ? "text-green-600 dark:text-green-400" : u.status === "pending" ? "text-amber-600" : "text-muted-foreground";
  return <span className={cn("text-xs font-semibold", tone)}>{u.status === "capitalised" ? `Capitalised · from ${u.capitalisedPeriod}` : u.status === "pending" ? "Awaiting SBU Finance" : u.status[0].toUpperCase() + u.status.slice(1)}</span>;
}

/** Refreshes the card and everything derived from it after a change. */
function useRefresh() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries();
}

function useFail() {
  const { toast } = useToast();
  return (title: string) => (err: unknown) => toast({ title, description: apiErrorMessage(err), variant: "destructive" });
}

export function UpgradeDecisionButtons({ upgrade, assetId: _assetId }: { upgrade: AssetUpgrade; assetId: number }) {
  const decide = useDecideUpgrade();
  const refresh = useRefresh();
  const fail = useFail();
  const [open, setOpen] = useState<UpgradeDecisionTreatment | null>(null);
  const [comment, setComment] = useState("");
  const [life, setLife] = useState("");
  function submit() {
    if (!open) return;
    decide.mutate(
      { id: upgrade.id, data: { treatment: open, comment: comment.trim() || undefined, revisedLifeMonths: open === "capitalise" && life ? Number(life) : undefined } },
      { onSuccess: () => { setOpen(null); refresh(); }, onError: fail("Could not record the decision") },
    );
  }
  return (
    <>
      <span className="inline-flex gap-1.5">
        <Button size="sm" className="h-8 px-2.5 text-xs" onClick={() => setOpen("capitalise")}>Capitalise</Button>
        <Button size="sm" variant="outline" className="h-8 px-2.5 text-xs" onClick={() => setOpen("expense")}>Expense</Button>
        <Button size="sm" variant="ghost" className="h-8 px-2.5 text-xs" onClick={() => setOpen("reject")}>Reject</Button>
      </span>
      <Dialog open={!!open} onOpenChange={(o) => !o && setOpen(null)}>
        <DialogContent>
          <DialogHeader><DialogTitle>{open === "capitalise" ? "Capitalise" : open === "expense" ? "Expense" : "Reject"}: {upgrade.description}</DialogTitle></DialogHeader>
          <p className="text-sm text-muted-foreground">
            {open === "capitalise" ? `${money(upgrade.cost)} is added to cost from this month and depreciated over the remaining (or revised) life.`
              : open === "expense" ? "Ordinary repair or maintenance: kept in the history, not added to cost."
              : "The upgrade is not accepted; nothing changes on the asset."}
          </p>
          {open === "capitalise" && (
            <div className="space-y-1.5">
              <Label htmlFor="life" className="text-xs">Revised remaining life in months (optional)</Label>
              <Input id="life" type="number" min={1} value={life} onChange={(e) => setLife(e.target.value)} />
            </div>
          )}
          <div className="space-y-1.5">
            <Label htmlFor="cmt" className="text-xs">Comment (saved to the audit trail)</Label>
            <Textarea id="cmt" rows={2} value={comment} onChange={(e) => setComment(e.target.value)} />
          </div>
          <DialogFooter><Button onClick={submit} disabled={decide.isPending}>Confirm</Button></DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

function CapitaliseButton({ card }: { card: AssetCard }) {
  const capitalise = useCapitaliseAsset();
  const refresh = useRefresh();
  const fail = useFail();
  const blocked = !card.asset.assetNumber ? "Waiting for the asset number" : card.asset.originalCost == null || !card.asset.inUseDate ? "Record cost and in-use date first" : "";
  return (
    <Button size="sm" disabled={!!blocked || capitalise.isPending} title={blocked}
      onClick={() => capitalise.mutate({ id: card.asset.id, data: {} }, { onSuccess: refresh, onError: fail("Could not capitalise") })}>
      Capitalise
    </Button>
  );
}

function NoSerialDecision({ assetId, reason }: { assetId: number; reason: string }) {
  const decide = useDecideNoSerial();
  const refresh = useRefresh();
  const fail = useFail();
  const act = (approve: boolean) => decide.mutate({ id: assetId, data: { approve } }, { onSuccess: refresh, onError: fail("Could not record the decision") });
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-amber-500/40 bg-amber-500/10 p-3 text-sm">
      <span><span className="font-semibold">"No serial" reason awaiting your approval:</span> {reason}</span>
      <span className="flex gap-2"><Button size="sm" onClick={() => act(true)}>Approve</Button><Button size="sm" variant="outline" onClick={() => act(false)}>Reject</Button></span>
    </div>
  );
}

function AuditDialog({ open, onClose, assetId }: { open: boolean; onClose: () => void; assetId: number }) {
  const { data } = useGetAssetAudit(assetId, { query: { enabled: open, queryKey: getGetAssetAuditQueryKey(assetId) } });
  return (
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-w-3xl">
        <DialogHeader><DialogTitle>Audit trail</DialogTitle></DialogHeader>
        <p className="text-xs text-muted-foreground">Append-only: entries can never be changed or deleted (AU-06, GF-07).</p>
        <div className="max-h-[60vh] overflow-auto">
          <Table>
            <TableHeader><TableRow><TableHead>When</TableHead><TableHead>Who</TableHead><TableHead>What</TableHead><TableHead>Old → new</TableHead><TableHead>Reason</TableHead></TableRow></TableHeader>
            <TableBody>
              {(data ?? []).map((e) => (
                <TableRow key={e.id}>
                  <TableCell className="whitespace-nowrap text-xs">{new Date(e.at).toLocaleString("en-GB")}</TableCell>
                  <TableCell className="text-xs">{e.userName}</TableCell>
                  <TableCell className="text-xs">{e.action.replace(/_/g, " ")}{e.field ? ` · ${e.field}` : ""}</TableCell>
                  <TableCell className="max-w-56 break-words text-xs">{e.oldValue ?? "—"} → {e.newValue ?? "—"}</TableCell>
                  <TableCell className="text-xs">{e.reason ?? ""}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function UpgradeDialog({ open, onClose, card }: { open: boolean; onClose: () => void; card: AssetCard }) {
  const log = useLogAssetUpgrade();
  const refresh = useRefresh();
  const fail = useFail();
  const [form, setForm] = useState({
    upgradeDate: new Date().toISOString().slice(0, 10), description: "", beforeText: "", afterText: "", invoiceAmount: "", currency: "USD", exchangeRate: "1",
    supplier: "", invoiceNo: "", field: "", newValue: "",
  });
  const foreign = form.currency.trim().toUpperCase() !== "USD";
  const usd = Number(form.invoiceAmount) * (foreign ? Number(form.exchangeRate) : 1);
  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setForm({ ...form, [k]: e.target.value });
  function submit(e: React.FormEvent) {
    e.preventDefault();
    log.mutate(
      {
        id: card.asset.id,
        data: {
          upgradeDate: form.upgradeDate, description: form.description, beforeText: form.beforeText, afterText: form.afterText,
          invoiceAmount: Number(form.invoiceAmount), currency: form.currency.trim().toUpperCase(), exchangeRate: foreign ? Number(form.exchangeRate) : 1,
          supplier: form.supplier || undefined, invoiceNo: form.invoiceNo || undefined,
          fieldChanges: form.field && form.newValue ? { [form.field]: form.newValue } : undefined,
        },
      },
      { onSuccess: () => { onClose(); refresh(); }, onError: fail("Could not log the upgrade") },
    );
  }
  return (
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader><DialogTitle>Log upgrade</DialogTitle></DialogHeader>
        <form onSubmit={submit} className="grid grid-cols-2 gap-3">
          <div className="col-span-2 space-y-1"><Label className="text-xs">What was done *</Label><Input value={form.description} onChange={set("description")} required placeholder="RAM upgrade, engine overhaul…" /></div>
          <div className="space-y-1"><Label className="text-xs">Before *</Label><Input value={form.beforeText} onChange={set("beforeText")} required placeholder="8 GB" /></div>
          <div className="space-y-1"><Label className="text-xs">After *</Label><Input value={form.afterText} onChange={set("afterText")} required placeholder="16 GB" /></div>
          <div className="space-y-1"><Label className="text-xs">Date *</Label><Input type="date" value={form.upgradeDate} onChange={set("upgradeDate")} required /></div>
          <div className="space-y-1"><Label className="text-xs">Invoice amount *</Label><Input type="number" step="0.01" min="0.01" value={form.invoiceAmount} onChange={set("invoiceAmount")} required /></div>
          <div className="grid grid-cols-2 gap-2">
            <div className="space-y-1"><Label className="text-xs">Currency *</Label><Input value={form.currency} onChange={set("currency")} maxLength={3} required className="uppercase" /></div>
            <div className="space-y-1"><Label className="text-xs">Rate to USD</Label><Input type="number" step="0.000001" min="0.000001" value={foreign ? form.exchangeRate : "1"} onChange={set("exchangeRate")} disabled={!foreign} required={foreign} /></div>
          </div>
          {foreign && Number.isFinite(usd) && usd > 0 && <p className="col-span-2 -mt-1 text-xs text-muted-foreground">Captured as invoiced; {money(usd)} at this rate. SBU Finance may confirm a different rate when deciding.</p>}
          <div className="space-y-1"><Label className="text-xs">Supplier</Label><Input value={form.supplier} onChange={set("supplier")} /></div>
          <div className="space-y-1"><Label className="text-xs">Invoice</Label><Input value={form.invoiceNo} onChange={set("invoiceNo")} /></div>
          <div className="space-y-1">
            <Label className="text-xs">Specification field it changes</Label>
            <select className={SELECT_CLASS} value={form.field} onChange={set("field")}>
              <option value="">None</option>
              {card.attributes.map((x) => <option key={x.key} value={x.key}>{x.label}</option>)}
            </select>
          </div>
          <div className="space-y-1"><Label className="text-xs">New value</Label><Input value={form.newValue} onChange={set("newValue")} disabled={!form.field} /></div>
          <p className="col-span-2 text-xs text-muted-foreground">SBU Finance decides capitalise or expense; on approval the specification and description update.</p>
          <DialogFooter className="col-span-2"><Button type="submit" disabled={log.isPending}>Log upgrade</Button></DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function FinancialDialog({ open, onClose, card }: { open: boolean; onClose: () => void; card: AssetCard }) {
  const update = useUpdateAssetFinancial();
  const refresh = useRefresh();
  const fail = useFail();
  const a = card.asset;
  const [form, setForm] = useState({
    originalCost: a.originalCost?.toString() ?? "", currency: a.currency ?? "USD", exchangeRate: a.exchangeRate?.toString() ?? "1",
    residualValue: (a.residualValue ?? 0).toString(), supplier: a.supplier ?? "", invoiceNo: a.invoiceNo ?? "", poNo: a.poNo ?? "",
    purchaseDate: a.purchaseDate ?? "", inUseDate: a.inUseDate ?? "", costCentre: a.costCentre ?? "", reason: "",
  });
  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [k]: e.target.value });
  const num = (v: string) => (v.trim() === "" ? null : Number(v));
  const text = (v: string) => (v.trim() === "" ? null : v.trim());
  function submit(e: React.FormEvent) {
    e.preventDefault();
    update.mutate(
      {
        id: a.id,
        data: {
          originalCost: num(form.originalCost), currency: text(form.currency.toUpperCase()), exchangeRate: num(form.exchangeRate),
          residualValue: num(form.residualValue), supplier: text(form.supplier), invoiceNo: text(form.invoiceNo), poNo: text(form.poNo),
          purchaseDate: text(form.purchaseDate), inUseDate: text(form.inUseDate), costCentre: text(form.costCentre), reason: form.reason,
        },
      },
      { onSuccess: () => { onClose(); refresh(); }, onError: fail("Could not save") },
    );
  }
  const usd = Number(form.originalCost) * Number(form.exchangeRate);
  return (
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader><DialogTitle>Cost &amp; purchase details</DialogTitle></DialogHeader>
        <form onSubmit={submit} className="grid grid-cols-2 gap-3">
          <div className="space-y-1"><Label className="text-xs">Original cost</Label><Input type="number" step="0.01" value={form.originalCost} onChange={set("originalCost")} /></div>
          <div className="space-y-1"><Label className="text-xs">Currency</Label><Input value={form.currency} onChange={set("currency")} maxLength={3} /></div>
          <div className="space-y-1"><Label className="text-xs">Rate to USD</Label><Input type="number" step="any" value={form.exchangeRate} onChange={set("exchangeRate")} /></div>
          <div className="space-y-1"><Label className="text-xs">USD equivalent</Label><Input value={Number.isFinite(usd) ? money(Math.round(usd * 100) / 100) : "—"} readOnly disabled /></div>
          <div className="space-y-1"><Label className="text-xs">Invoice no.</Label><Input value={form.invoiceNo} onChange={set("invoiceNo")} /></div>
          <div className="space-y-1"><Label className="text-xs">PO no.</Label><Input value={form.poNo} onChange={set("poNo")} /></div>
          <div className="space-y-1"><Label className="text-xs">Supplier</Label><Input value={form.supplier} onChange={set("supplier")} /></div>
          <div className="space-y-1"><Label className="text-xs">Residual value (USD)</Label><Input type="number" step="0.01" value={form.residualValue} onChange={set("residualValue")} /></div>
          <div className="space-y-1"><Label className="text-xs">Purchase date</Label><Input type="date" value={form.purchaseDate} onChange={set("purchaseDate")} /></div>
          <div className="space-y-1"><Label className="text-xs">In-use date</Label><Input type="date" value={form.inUseDate} onChange={set("inUseDate")} /></div>
          <div className="space-y-1"><Label className="text-xs">Cost centre</Label><Input value={form.costCentre} onChange={set("costCentre")} /></div>
          <div className="col-span-2 space-y-1"><Label className="text-xs">Reason (required, saved to the audit trail)</Label><Input value={form.reason} onChange={set("reason")} required minLength={3} /></div>
          <DialogFooter className="col-span-2"><Button type="submit" disabled={update.isPending}>Save</Button></DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function CategoryDialog({ open, onClose, card }: { open: boolean; onClose: () => void; card: AssetCard }) {
  const { data: categories } = useListCategories();
  const change = useChangeAssetCategory();
  const refresh = useRefresh();
  const fail = useFail();
  const [categoryId, setCategoryId] = useState("");
  const [sub, setSub] = useState("");
  const [values, setValues] = useState<TemplateDraft>({});
  const [reason, setReason] = useState("");
  const category = categories?.find((c) => String(c.id) === categoryId);
  function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!category) return;
    change.mutate(
      { id: card.asset.id, data: { categoryId: category.id, subCategory: sub || undefined, attributes: filled(values), reason } },
      { onSuccess: () => { onClose(); refresh(); }, onError: fail("Could not change the category") },
    );
  }
  return (
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-w-3xl">
        <DialogHeader><DialogTitle>Change category</DialogTitle></DialogHeader>
        <form onSubmit={submit} className="space-y-3">
          <p className="text-xs text-muted-foreground">Fill in the new category's required fields. The change and its reason are audit-logged.</p>
          <div className="grid grid-cols-2 gap-3">
            <select className={SELECT_CLASS} value={categoryId} onChange={(e) => { setCategoryId(e.target.value); setSub(""); setValues({}); }} required>
              <option value="">New category…</option>
              {(categories ?? []).filter((c) => c.id !== card.asset.categoryId).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
            {category && category.subCategories.length > 0 && (
              <select className={SELECT_CLASS} value={sub} onChange={(e) => setSub(e.target.value)} required>
                <option value="">Sub-category…</option>
                {category.subCategories.map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
            )}
          </div>
          {category && <TemplateFields category={category} values={values} onChange={setValues} />}
          <div className="space-y-1"><Label className="text-xs">Reason *</Label><Input value={reason} onChange={(e) => setReason(e.target.value)} required minLength={3} /></div>
          <DialogFooter><Button type="submit" disabled={!category || change.isPending}>Change category</Button></DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function TagDialog({ open, onClose, card }: { open: boolean; onClose: () => void; card: AssetCard }) {
  const tag = useTagAsset();
  const refresh = useRefresh();
  const fail = useFail();
  const [value, setValue] = useState(card.asset.rfidTag ?? "");
  return (
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader><DialogTitle>Link RFID / QR tag</DialogTitle></DialogHeader>
        <form onSubmit={(e) => { e.preventDefault(); tag.mutate({ id: card.asset.id, data: { rfidTag: value } }, { onSuccess: () => { onClose(); refresh(); }, onError: fail("Could not link the tag") }); }} className="space-y-3">
          <Input autoFocus value={value} onChange={(e) => setValue(e.target.value)} className="font-mono" placeholder="Scan the tag" required />
          <DialogFooter><Button type="submit" disabled={tag.isPending}>Link tag</Button></DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
