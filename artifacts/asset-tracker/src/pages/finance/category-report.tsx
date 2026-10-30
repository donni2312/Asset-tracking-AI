import { useState } from "react";
import { Link, useLocation, useSearch } from "wouter";
import { downloadCategoryReportExcel, downloadStandardReportExcel, downloadStandardReportPdf, type DownloadStandardReportExcelKind, downloadCategoryReportPdf, useGetCategoryReport, type Totals } from "@workspace/api-client-react";
import { Layout } from "@/components/layout";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ReportFilters, filtersFromSearch, searchOf, toParams, type ReportFilterValues } from "@/components/report-filters";
import { useCurrentUser } from "@/hooks/use-auth";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { currentPeriod, money, periodLabel, saveBlob } from "@/lib/finance";

const COLUMNS: Array<[keyof Totals, string]> = [
  ["originalCost", "Original cost"], ["upgradesInPeriod", "Upgrades in period"], ["totalCost", "Total cost"],
  ["depreciationMonth", "Depr. this month"], ["depreciationYtd", "Depr. YTD"], ["accumulatedDepreciation", "Accumulated"], ["netBookValue", "NBV"],
];

/**
 * Assets by category (AU-09): subtotals per category and a grand total that equals the register for the
 * same filters. A category drills down to its assets in the register, and from there to the asset card.
 */
export default function CategoryReport() {
  const user = useCurrentUser();
  const search = useSearch();
  const [, navigate] = useLocation();
  const { toast } = useToast();
  const [filters, setFilters] = useState<ReportFilterValues>(() => filtersFromSearch(search, currentPeriod()));
  const { data, isLoading, error } = useGetCategoryReport(toParams(filters), { query: { enabled: !!user, queryKey: ["category-report", filters] } });

  function change(f: ReportFilterValues) {
    setFilters(f);
    navigate(`/reports/category${searchOf(f)}`, { replace: true });
  }

  async function exportAs(kind: "xlsx" | "pdf") {
    try {
      const params = toParams(filters);
      const blob = kind === "xlsx" ? await downloadCategoryReportExcel(params) : await downloadCategoryReportPdf(params);
      saveBlob(blob, `category-report-${filters.period}.${kind}`);
    } catch (err) {
      toast({ title: "Export failed", description: apiErrorMessage(err), variant: "destructive" });
    }
  }

  /** GF-06: the standard reports for the same filters. */
  async function standard(kind: DownloadStandardReportExcelKind, format: "xlsx" | "pdf") {
    try {
      const params = { ...toParams(filters), kind };
      const blob = format === "xlsx" ? await downloadStandardReportExcel(params) : await downloadStandardReportPdf(params);
      saveBlob(blob, `${kind}-${filters.period}.${format}`);
    } catch (err) {
      toast({ title: "Export failed", description: apiErrorMessage(err), variant: "destructive" });
    }
  }

  if (!user) return <Layout><p className="text-sm text-muted-foreground">Sign in to see the category report.</p></Layout>;

  return (
    <Layout>
      <div className="space-y-5">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Assets by category — {periodLabel(filters.period)}</h1>
            <p className="text-sm text-muted-foreground">Upgrades in period = capitalised from January · amounts in USD · <Link href="/reports" className="text-primary hover:underline">CSV downloads</Link></p>
          </div>
          <div className="flex gap-2">
            <Button variant="outline" size="sm" onClick={() => exportAs("xlsx")}>Excel</Button>
            <Button variant="outline" size="sm" onClick={() => exportAs("pdf")}>PDF</Button>
          </div>
        </div>
        <ReportFilters value={filters} onChange={change} />
        <section className="flex flex-wrap items-center gap-2 rounded-md border bg-card p-3 text-sm">
          <span className="font-medium">Standard reports (GF-06):</span>
          {(["register", "additions", "disposals", "depreciation", "movements"] as const).map((k) => (
            <span key={k} className="flex items-center gap-1 rounded border px-2 py-1">
              <span className="capitalize">{k === "register" ? "Fixed asset register" : k}</span>
              <button type="button" className="text-primary hover:underline" onClick={() => standard(k, "xlsx")}>Excel</button>
              <span className="text-muted-foreground">·</span>
              <button type="button" className="text-primary hover:underline" onClick={() => standard(k, "pdf")}>PDF</button>
            </span>
          ))}
        </section>
        <section className="rounded-md border bg-card">
          <div className="overflow-x-auto">
            <Table>
              <TableHeader><TableRow>
                <TableHead>Category</TableHead><TableHead className="text-right">Assets</TableHead>
                {COLUMNS.map(([k, label]) => <TableHead key={k} className="text-right">{label}</TableHead>)}
              </TableRow></TableHeader>
              <TableBody>
                {isLoading && <TableRow><TableCell colSpan={9} className="text-center text-sm text-muted-foreground">Loading…</TableCell></TableRow>}
                {error && <TableRow><TableCell colSpan={9} className="text-center text-sm text-destructive">{apiErrorMessage(error)}</TableCell></TableRow>}
                {data?.categories.map((c) => (
                  <TableRow key={c.categoryId}>
                    <TableCell><Link href={`/register${searchOf({ ...filters, categoryId: String(c.categoryId) })}`} className="text-primary hover:underline">{c.categoryName}</Link></TableCell>
                    <TableCell className="text-right font-mono">{c.totals.count}</TableCell>
                    {COLUMNS.map(([k]) => <TableCell key={k} className="text-right font-mono">{money(c.totals[k] as number)}</TableCell>)}
                  </TableRow>
                ))}
                {data && (
                  <TableRow className="font-bold">
                    <TableCell>Total</TableCell>
                    <TableCell className="text-right font-mono">{data.totals.count}</TableCell>
                    {COLUMNS.map(([k]) => <TableCell key={k} className="text-right font-mono">{money(data.totals[k] as number)}</TableCell>)}
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
