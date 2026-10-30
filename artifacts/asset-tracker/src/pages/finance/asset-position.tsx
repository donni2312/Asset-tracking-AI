import { useState } from "react";
import { Link } from "wouter";
import { downloadCategoryReportExcel, downloadCategoryReportPdf, useGetFinanceDashboard, useListApprovals } from "@workspace/api-client-react";
import { Layout } from "@/components/layout";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ReportFilters, searchOf, type ReportFilterValues } from "@/components/report-filters";
import { SbuBadge } from "@/components/sbu-badge";
import { useCurrentUser } from "@/hooks/use-auth";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { currentPeriod, financialYear, money, moneyWhole, periodLabel, saveBlob } from "@/lib/finance";

/**
 * Management dashboard (AU-10, GF-01): cost, upgrades, depreciation and NBV by category. The figures come
 * from the same calculation as the category report (AU-09), and each category opens that report.
 */
export default function AssetPosition() {
  const user = useCurrentUser();
  const { toast } = useToast();
  const [filters, setFilters] = useState<ReportFilterValues>({ period: currentPeriod(), sbuId: "", locationId: "", categoryId: "" });
  const params = { period: filters.period, sbuId: filters.sbuId ? Number(filters.sbuId) : undefined };
  const { data, error } = useGetFinanceDashboard(params, { query: { enabled: !!user, queryKey: ["finance-dashboard", params] } });
  const { data: approvals } = useListApprovals({ query: { enabled: user?.role === "sbu_finance", queryKey: ["approvals"] } });

  async function exportAs(kind: "xlsx" | "pdf") {
    try {
      const blob = kind === "xlsx" ? await downloadCategoryReportExcel(params) : await downloadCategoryReportPdf(params);
      saveBlob(blob, `category-report-${filters.period}.${kind}`);
    } catch (err) {
      toast({ title: "Export failed", description: apiErrorMessage(err), variant: "destructive" });
    }
  }

  const t = data?.totals;
  const maxNbv = Math.max(1, ...(data?.bySbu ?? []).map((s) => s.netBookValue));
  return (
    <Layout>
      <div className="space-y-6">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Asset Position — {periodLabel(filters.period)}</h1>
            <p className="text-sm text-muted-foreground">Cost, monthly depreciation and net book value · {user?.scope}</p>
          </div>
          <div className="flex flex-wrap items-end gap-3">
            <ReportFilters value={filters} onChange={setFilters} show={["period", "sbu"]} />
            <Button asChild><Link href={`/reports/category${searchOf(filters)}`}>Open category report</Link></Button>
          </div>
        </div>
        {error && <p className="text-sm text-destructive">{apiErrorMessage(error)}</p>}

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <Kpi label="Total cost" value={moneyWhole(t?.totalCost)} sub={t ? `incl. ${moneyWhole(t.capitalisedUpgrades)} capitalised upgrades` : ""} />
          <Kpi label="Depreciation this month" value={moneyWhole(t?.depreciationMonth)} sub={t ? `YTD ${moneyWhole(t.depreciationYtd)}` : ""} />
          <Kpi label="Net book value" value={moneyWhole(t?.netBookValue)} sub={t && t.totalCost > 0 ? `${Math.round((t.netBookValue / t.totalCost) * 100)}% of cost remaining` : ""} />
          <Kpi label="Record completeness" value={data ? `${data.percentComplete}%` : "—"} tone={data && data.percentComplete < 100 ? "text-amber-500" : undefined}
            sub={data ? <Link href="/verification" className="text-primary hover:underline">{data.incompleteAssets} assets with gaps →</Link> : ""} />
        </div>

        <section className="rounded-md border bg-card">
          <div className="flex flex-wrap items-center justify-between gap-3 p-4">
            <div><h2 className="text-lg font-semibold">By asset category</h2><p className="text-xs text-muted-foreground">Click a category to open its report</p></div>
            <div className="flex gap-2"><Button variant="outline" size="sm" onClick={() => exportAs("xlsx")}>Excel</Button><Button variant="outline" size="sm" onClick={() => exportAs("pdf")}>PDF</Button></div>
          </div>
          <div className="overflow-x-auto">
            <Table>
              <TableHeader><TableRow>
                <TableHead>Category</TableHead><TableHead className="text-right">Assets</TableHead><TableHead className="text-right">Cost</TableHead>
                <TableHead className="text-right">Upgrades (YTD)</TableHead><TableHead className="text-right">Depr. this month</TableHead><TableHead className="text-right">Depr. YTD</TableHead>
                <TableHead className="text-right">NBV</TableHead><TableHead className="text-right">Additions {financialYear(filters.period)}</TableHead>
              </TableRow></TableHeader>
              <TableBody>
                {data?.categories.map((c) => (
                  <TableRow key={c.categoryId}>
                    <TableCell><Link href={`/reports/category${searchOf({ ...filters, categoryId: String(c.categoryId) })}`} className="text-primary hover:underline">{c.categoryName}</Link></TableCell>
                    <TableCell className="text-right font-mono">{c.totals.count}</TableCell>
                    <TableCell className="text-right font-mono">{money(c.totals.totalCost)}</TableCell>
                    <TableCell className="text-right font-mono">{money(c.totals.upgradesInPeriod)}</TableCell>
                    <TableCell className="text-right font-mono">{money(c.totals.depreciationMonth)}</TableCell>
                    <TableCell className="text-right font-mono">{money(c.totals.depreciationYtd)}</TableCell>
                    <TableCell className="text-right font-mono">{money(c.totals.netBookValue)}</TableCell>
                    <TableCell className="text-right font-mono">{c.totals.additions}</TableCell>
                  </TableRow>
                ))}
                {t && (
                  <TableRow className="font-bold">
                    <TableCell>Total</TableCell><TableCell className="text-right font-mono">{t.count}</TableCell>
                    <TableCell className="text-right font-mono">{money(t.totalCost)}</TableCell><TableCell className="text-right font-mono">{money(t.upgradesInPeriod)}</TableCell>
                    <TableCell className="text-right font-mono">{money(t.depreciationMonth)}</TableCell><TableCell className="text-right font-mono">{money(t.depreciationYtd)}</TableCell>
                    <TableCell className="text-right font-mono">{money(t.netBookValue)}</TableCell><TableCell className="text-right font-mono">{t.additions}</TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </div>
        </section>

        <div className="grid grid-cols-1 gap-3 lg:grid-cols-3">
          <section className="rounded-md border bg-card p-4 lg:col-span-2">
            <h2 className="mb-3 text-lg font-semibold">NBV by SBU</h2>
            <div className="space-y-3 text-sm">
              {(data?.bySbu ?? []).map((s) => (
                <Link key={s.sbuCode ?? "none"} href={`/register${searchOf({ ...filters, sbuId: s.sbuId ? String(s.sbuId) : "" })}`}
                  className="flex items-center gap-3 rounded hover:bg-muted/50" title="Open the register for this SBU">
                  <span className="w-14">{s.sbuCode && <SbuBadge code={s.sbuCode} color={s.color} />}</span>
                  <div className="h-2.5 flex-1 rounded bg-muted"><div className="h-2.5 rounded" style={{ width: `${(s.netBookValue / maxNbv) * 100}%`, backgroundColor: s.color ?? "#3C83F6" }} /></div>
                  <span className="w-24 text-right font-mono text-primary">{moneyWhole(s.netBookValue)}</span>
                </Link>
              ))}
            </div>
          </section>
          <section className="space-y-2 rounded-md border bg-card p-4">
            <h2 className="text-lg font-semibold">Needs your decision</h2>
            {user?.role !== "sbu_finance" && <p className="text-sm text-muted-foreground">{data?.pendingDecisions ? `${data.pendingDecisions} waiting` : "Nothing waiting on you."}</p>}
            {(approvals ?? []).slice(0, 4).map((x) => (
              <Link key={x.id} href={`/assets/${x.assetId}/card`} className="flex justify-between gap-3 rounded border bg-background p-3 text-sm hover:border-primary">
                <span>{x.summary} — <span className="font-mono">{x.assetNumber ?? "pending"}</span></span><span className="font-semibold text-amber-500">Review</span>
              </Link>
            ))}
            {user?.role === "sbu_finance" && approvals?.length === 0 && <p className="text-sm text-muted-foreground">Nothing waiting on you.</p>}
          </section>
        </div>
      </div>
    </Layout>
  );
}

function Kpi({ label, value, sub, tone }: { label: string; value: string; sub?: React.ReactNode; tone?: string }) {
  return (
    <div className="rounded-md border bg-card p-4">
      <div className="text-sm font-medium text-muted-foreground">{label}</div>
      <div className={`mt-2 font-mono text-2xl font-bold ${tone ?? ""}`}>{value}</div>
      {sub && <div className="mt-1 text-xs text-muted-foreground">{sub}</div>}
    </div>
  );
}
