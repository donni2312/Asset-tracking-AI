import { useState } from "react";
import { Link } from "wouter";
import { useGetCompletenessReport } from "@workspace/api-client-react";
import { Layout } from "@/components/layout";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ReportFilters, toParams, type ReportFilterValues } from "@/components/report-filters";
import { SbuBadge } from "@/components/sbu-badge";
import { useCurrentUser } from "@/hooks/use-auth";
import { currentPeriod } from "@/lib/finance";

/**
 * Record gaps before audit (AU-11): every asset missing a Priority 1 item, who fixes it, and % complete
 * per SBU. Scan sessions and reconciliation (SI-07, SF-06) are added to this screen in Phase 4.
 */
export default function Verification() {
  const user = useCurrentUser();
  const [filters, setFilters] = useState<ReportFilterValues>({ period: currentPeriod(), sbuId: "", locationId: "", categoryId: "" });
  const { sbuId, categoryId } = toParams(filters);
  const { data } = useGetCompletenessReport({ sbuId, categoryId }, { query: { enabled: !!user, queryKey: ["completeness", sbuId, categoryId] } });

  return (
    <Layout>
      <div className="space-y-5">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Verification</h1>
          <p className="text-sm text-muted-foreground">Fix record gaps before the audit; target zero gaps by year-end. Room-by-room scan sessions arrive in Phase 4.</p>
        </div>
        <ReportFilters value={filters} onChange={setFilters} show={["sbu", "category"]} />
        <div className="grid grid-cols-2 gap-3 md:grid-cols-4 xl:grid-cols-6">
          {data && (
            <div className="rounded-md border bg-card p-4">
              <div className="text-sm text-muted-foreground">In scope</div>
              <div className="mt-2 font-mono text-2xl font-bold">{data.percentComplete}%</div>
              <div className="text-xs text-muted-foreground">{data.complete} of {data.assets} complete</div>
            </div>
          )}
          {data?.sbus.map((s) => (
            <div key={s.sbuCode ?? "none"} className="rounded-md border bg-card p-4">
              <div>{s.sbuCode ? <SbuBadge code={s.sbuCode} /> : "No SBU"}</div>
              <div className={`mt-2 font-mono text-2xl font-bold ${s.percentComplete < 100 ? "text-amber-500" : "text-green-500"}`}>{s.percentComplete}%</div>
              <div className="text-xs text-muted-foreground">{s.complete} of {s.assets} complete</div>
            </div>
          ))}
        </div>
        <section className="rounded-md border bg-card">
          <div className="p-4"><h2 className="font-semibold">Record gaps before audit (AU-11)</h2></div>
          <div className="overflow-x-auto">
            <Table>
              <TableHeader><TableRow><TableHead>Asset no.</TableHead><TableHead>Description</TableHead><TableHead>SBU</TableHead><TableHead>Missing</TableHead><TableHead>Owner to fix</TableHead><TableHead /></TableRow></TableHeader>
              <TableBody>
                {data?.incomplete.map((r) => (
                  <TableRow key={r.assetId}>
                    <TableCell className="whitespace-nowrap font-mono text-xs">{r.assetNumber ?? "pending"}</TableCell>
                    <TableCell className="min-w-56">{r.description}<div className="text-[11px] text-muted-foreground">{r.categoryName}</div></TableCell>
                    <TableCell>{r.sbuCode && <SbuBadge code={r.sbuCode} />}</TableCell>
                    <TableCell className="text-xs">{r.gaps.map((g) => <div key={g.field}>{g.message}</div>)}</TableCell>
                    <TableCell className="text-xs">{[...new Set(r.gaps.map((g) => g.owner))].join(", ")}</TableCell>
                    <TableCell><Link href={`/assets/${r.assetId}/card`} className="text-sm text-primary hover:underline">Fix</Link></TableCell>
                  </TableRow>
                ))}
                {data && data.incomplete.length === 0 && <TableRow><TableCell colSpan={6} className="text-center text-sm text-muted-foreground">No gaps: every asset in scope is complete.</TableCell></TableRow>}
              </TableBody>
            </Table>
          </div>
        </section>
      </div>
    </Layout>
  );
}
