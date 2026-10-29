import { useState } from "react";
import { Link, useLocation, useSearch } from "wouter";
import { downloadAssetRegisterExcel, downloadAssetRegisterPdf, lookupAsset, useGetAssetRegister } from "@workspace/api-client-react";
import { ScanLine } from "lucide-react";
import { Layout } from "@/components/layout";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ReportFilters, filtersFromSearch, searchOf, toParams, type ReportFilterValues } from "@/components/report-filters";
import { SbuBadge } from "@/components/sbu-badge";
import { useCurrentUser } from "@/hooks/use-auth";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { canRegisterSomething, currentPeriod, money, periodLabel, saveBlob, STATUS_LABEL, STATUS_TONE } from "@/lib/finance";
import { cn } from "@/lib/utils";

/**
 * The fixed asset register (SF-02): every asset the signed-in user may see, with cost, depreciation and
 * NBV for the month. Drill-down target of the category report (AU-09); opens the asset card (AU-08),
 * also by asset-number search or tag scan.
 */
export default function AssetRegister() {
  const user = useCurrentUser();
  const search = useSearch();
  const [, navigate] = useLocation();
  const { toast } = useToast();
  const [filters, setFilters] = useState<ReportFilterValues>(() => filtersFromSearch(search, currentPeriod()));
  const [code, setCode] = useState("");
  const { data, isLoading, error } = useGetAssetRegister(toParams(filters), { query: { enabled: !!user, queryKey: ["register", filters] } });

  function change(f: ReportFilterValues) {
    setFilters(f);
    navigate(`/register${searchOf(f)}`, { replace: true });
  }

  async function open(e: React.FormEvent) {
    e.preventDefault();
    try {
      const found = await lookupAsset({ code });
      navigate(`/assets/${found.assetId}/card`);
    } catch (err) {
      toast({ title: "Not found", description: apiErrorMessage(err), variant: "destructive" });
    }
  }

  async function exportAs(kind: "xlsx" | "pdf") {
    try {
      const params = toParams(filters);
      const blob = kind === "xlsx" ? await downloadAssetRegisterExcel(params) : await downloadAssetRegisterPdf(params);
      saveBlob(blob, `asset-register-${filters.period}.${kind}`);
    } catch (err) {
      toast({ title: "Export failed", description: apiErrorMessage(err), variant: "destructive" });
    }
  }

  if (!user) return <Layout><p className="text-sm text-muted-foreground">Sign in to see the asset register.</p></Layout>;

  return (
    <Layout>
      <div className="space-y-5">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Asset Register</h1>
            <p className="text-sm text-muted-foreground">Cost, depreciation and net book value at the end of {periodLabel(filters.period)} · {user.scope}</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" size="sm" onClick={() => exportAs("xlsx")}>Excel</Button>
            <Button variant="outline" size="sm" onClick={() => exportAs("pdf")}>PDF</Button>
            {canRegisterSomething(user) && <Button size="sm" asChild><Link href="/assets/new">+ Register asset</Link></Button>}
          </div>
        </div>

        <div className="flex flex-wrap items-end justify-between gap-4">
          <ReportFilters value={filters} onChange={change} />
          <form onSubmit={open} className="flex gap-2">
            <Input value={code} onChange={(e) => setCode(e.target.value)} placeholder="Asset no., tag or serial" className="w-56 font-mono" aria-label="Open asset card by asset number, tag or serial" />
            <Button type="submit" variant="outline" size="icon" aria-label="Open asset card"><ScanLine className="h-4 w-4" /></Button>
          </form>
        </div>

        <section className="rounded-md border bg-card">
          <div className="overflow-x-auto">
            <Table>
              <TableHeader><TableRow>
                <TableHead>Asset no.</TableHead><TableHead>Description</TableHead><TableHead>SBU</TableHead><TableHead>Location</TableHead><TableHead>Custodian</TableHead><TableHead>Status</TableHead>
                <TableHead className="text-right">Cost</TableHead><TableHead className="text-right">Upgrades</TableHead><TableHead className="text-right">Depr. month</TableHead>
                <TableHead className="text-right">Accumulated</TableHead><TableHead className="text-right">NBV</TableHead>
              </TableRow></TableHeader>
              <TableBody>
                {isLoading && <TableRow><TableCell colSpan={11} className="text-center text-sm text-muted-foreground">Loading…</TableCell></TableRow>}
                {error && <TableRow><TableCell colSpan={11} className="text-center text-sm text-destructive">{apiErrorMessage(error)}</TableCell></TableRow>}
                {data?.rows.map((r) => (
                  <TableRow key={r.assetId} className="cursor-pointer" onClick={() => navigate(`/assets/${r.assetId}/card`)}>
                    <TableCell className="whitespace-nowrap font-mono text-xs">{r.assetNumber ?? <span className="text-amber-600">pending</span>}</TableCell>
                    <TableCell className="min-w-64"><Link href={`/assets/${r.assetId}/card`} className="text-primary hover:underline">{r.description}</Link><div className="text-[11px] text-muted-foreground">{r.categoryName}</div></TableCell>
                    <TableCell>{r.sbuCode && <SbuBadge code={r.sbuCode} />}</TableCell>
                    <TableCell className="text-xs">{r.locationName ?? "—"}</TableCell>
                    <TableCell className="text-xs">{r.custodian ?? "—"}</TableCell>
                    <TableCell><span className={cn("whitespace-nowrap rounded px-1.5 py-0.5 text-[10px] font-semibold", STATUS_TONE[r.status])}>{STATUS_LABEL[r.status] ?? r.status}</span></TableCell>
                    <TableCell className="text-right font-mono text-xs">{r.capitalised ? money(r.originalCost) : <span className="text-muted-foreground">not capitalised</span>}</TableCell>
                    <TableCell className="text-right font-mono text-xs">{money(r.capitalisedUpgrades)}</TableCell>
                    <TableCell className="text-right font-mono text-xs">{money(r.depreciationMonth)}</TableCell>
                    <TableCell className="text-right font-mono text-xs">{money(r.accumulatedDepreciation)}</TableCell>
                    <TableCell className="text-right font-mono text-xs font-semibold">{money(r.netBookValue)}</TableCell>
                  </TableRow>
                ))}
                {data && (
                  <TableRow className="font-bold">
                    <TableCell colSpan={6}>Total · {data.totals.count} assets</TableCell>
                    <TableCell className="text-right font-mono text-xs">{money(data.totals.originalCost)}</TableCell>
                    <TableCell className="text-right font-mono text-xs">{money(data.totals.capitalisedUpgrades)}</TableCell>
                    <TableCell className="text-right font-mono text-xs">{money(data.totals.depreciationMonth)}</TableCell>
                    <TableCell className="text-right font-mono text-xs">{money(data.totals.accumulatedDepreciation)}</TableCell>
                    <TableCell className="text-right font-mono text-xs">{money(data.totals.netBookValue)}</TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </div>
        </section>
      </div>
    </Layout>
  );
}
