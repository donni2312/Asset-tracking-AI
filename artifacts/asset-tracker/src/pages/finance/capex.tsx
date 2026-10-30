import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useGetCapex, useListCategories, useListSbus, useSetCapexBudget } from "@workspace/api-client-react";
import { Layout } from "@/components/layout";
import { SbuBadge } from "@/components/sbu-badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useCurrentUser } from "@/hooks/use-auth";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS } from "@/lib/constants";
import { currentPeriod, financialYear, money } from "@/lib/finance";

function Used({ percent, over }: { percent?: number | null; over: boolean }) {
  if (percent == null) return <span className="text-muted-foreground">no budget</span>;
  const cls = over ? "text-red-600 font-semibold" : percent >= 80 ? "text-amber-600" : "text-green-600";
  return <span className={cls}>{percent.toFixed(1)}%</span>;
}

/**
 * GF-05 / SF-09: CAPEX budget against what has been capitalised, by financial year (1 November – 31 October).
 * SBU Finance enters its SBU's budget per category; Group Finance sees every SBU and may load budgets too.
 */
export default function Capex() {
  const user = useCurrentUser();
  const { toast } = useToast();
  const qc = useQueryClient();
  const current = Number(financialYear(currentPeriod()).slice(2));
  const [year, setYear] = useState(current);
  const { data } = useGetCapex({ financialYear: year }, { query: { enabled: !!user, queryKey: ["capex", year] } });
  const { data: categories } = useListCategories(undefined, { query: { enabled: !!user, queryKey: ["categories"] } });
  const { data: sbus } = useListSbus({ query: { enabled: user?.role === "group_finance", queryKey: ["sbus"] } });
  const save = useSetCapexBudget();
  const [line, setLine] = useState({ sbuId: "", categoryId: "", amount: "" });
  const canEdit = user?.role === "sbu_finance" || user?.role === "group_finance" || user?.role === "system_admin";

  return (
    <Layout>
      <div className="space-y-5">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">CAPEX budget</h1>
            <p className="text-sm text-muted-foreground">{data ? `${data.label}: ${data.startsOn} to ${data.endsOn}` : ""} · spend = capitalised assets and upgrades · alerts at 80% and 100%</p>
          </div>
          <select className={SELECT_CLASS} value={year} onChange={(e) => setYear(Number(e.target.value))} aria-label="Financial year">
            {[current - 1, current, current + 1].map((y) => <option key={y} value={y}>FY{y}</option>)}
          </select>
        </div>

        <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
          {data?.bySbu.map((s) => (
            <div key={s.sbuId} className={`rounded-md border bg-card p-4 ${s.overBudget ? "border-red-500/60" : ""}`}>
              <div className="flex items-center justify-between">{s.sbuCode && <SbuBadge code={s.sbuCode} />}<Used percent={s.usedPercent} over={s.overBudget} /></div>
              <div className="mt-2 font-mono text-lg">{money(s.actual)} <span className="text-sm text-muted-foreground">of {money(s.budget)}</span></div>
              <div className={`text-xs ${s.variance > 0 ? "text-red-600" : "text-muted-foreground"}`}>Variance {money(s.variance)}</div>
            </div>
          ))}
        </div>

        <section className="overflow-x-auto rounded-md border bg-card">
          <Table>
            <TableHeader><TableRow>
              <TableHead>SBU</TableHead><TableHead>Category</TableHead><TableHead className="text-right">Budget</TableHead><TableHead className="text-right">Actual</TableHead>
              <TableHead className="text-right">Variance</TableHead><TableHead className="text-right">Used</TableHead>
            </TableRow></TableHeader>
            <TableBody>
              {data?.lines.map((l) => (
                <TableRow key={`${l.sbuId}-${l.categoryId}`} className={l.overBudget ? "bg-red-500/5" : ""}>
                  <TableCell>{l.sbuCode && <SbuBadge code={l.sbuCode} />}</TableCell>
                  <TableCell>{l.categoryName}</TableCell>
                  <TableCell className="text-right font-mono">{money(l.budget)}</TableCell>
                  <TableCell className="text-right font-mono">{money(l.actual)}</TableCell>
                  <TableCell className="text-right font-mono">{money(l.variance)}</TableCell>
                  <TableCell className="text-right"><Used percent={l.usedPercent} over={l.overBudget} /></TableCell>
                </TableRow>
              ))}
              {data?.lines.length === 0 && <TableRow><TableCell colSpan={6} className="text-center text-sm text-muted-foreground">No budget or spend for FY{year}.</TableCell></TableRow>}
            </TableBody>
          </Table>
        </section>

        {canEdit && (
          <form className="flex flex-wrap items-end gap-2 rounded-md border bg-card p-4" onSubmit={(e) => {
            e.preventDefault();
            save.mutate({ data: { sbuId: line.sbuId ? Number(line.sbuId) : undefined, financialYear: year, lines: [{ categoryId: Number(line.categoryId), amount: Number(line.amount) }] } }, {
              onSuccess: () => { toast({ title: "Budget saved" }); setLine({ ...line, amount: "" }); qc.invalidateQueries(); },
              onError: (err) => toast({ title: "Not saved", description: apiErrorMessage(err), variant: "destructive" }),
            });
          }}>
            <span className="text-sm font-medium">Budget for FY{year}:</span>
            {user?.role === "group_finance" && (
              <select className={SELECT_CLASS} value={line.sbuId} onChange={(e) => setLine({ ...line, sbuId: e.target.value })} required aria-label="SBU">
                <option value="">SBU…</option>
                {sbus?.map((s) => <option key={s.id} value={s.id}>{s.code}</option>)}
              </select>
            )}
            <select className={SELECT_CLASS} value={line.categoryId} onChange={(e) => setLine({ ...line, categoryId: e.target.value })} required aria-label="Category">
              <option value="">Category…</option>
              {categories?.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
            <Input className="w-40" type="number" step="0.01" min="0" value={line.amount} onChange={(e) => setLine({ ...line, amount: e.target.value })} placeholder="USD" required />
            <Button type="submit" size="sm" disabled={save.isPending}>Save</Button>
          </form>
        )}
      </div>
    </Layout>
  );
}
