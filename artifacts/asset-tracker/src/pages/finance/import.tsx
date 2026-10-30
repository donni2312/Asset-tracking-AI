import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { downloadImportTemplateCsv, downloadImportTemplateExcel, useListCategories, type ImportReport } from "@workspace/api-client-react";
import { CheckCircle2, Upload, XCircle } from "lucide-react";
import { Layout } from "@/components/layout";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { currentToken, useCurrentUser } from "@/hooks/use-auth";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS } from "@/lib/constants";
import { saveBlob } from "@/lib/finance";

async function send(path: string, file: File): Promise<{ status: number; report: ImportReport | null; error: string | null }> {
  const form = new FormData();
  form.append("file", file);
  const token = currentToken();
  const res = await fetch(`/api${path}`, { method: "POST", body: form, headers: token ? { Authorization: `Bearer ${token}` } : {} });
  const body = await res.json().catch(() => null);
  return { status: res.status, report: body && "rows" in body ? (body as ImportReport) : null, error: body?.error ?? (res.ok ? null : `Failed (${res.status})`) };
}

/**
 * SF-10: download the template for a category, upload the filled sheet, read the validation report, then import.
 * Nothing is saved while any row has an error; the import is all rows or none.
 */
export default function Import() {
  const user = useCurrentUser();
  const { toast } = useToast();
  const qc = useQueryClient();
  const { data: categories } = useListCategories(undefined, { query: { enabled: !!user, queryKey: ["categories"] } });
  const [categoryId, setCategoryId] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [report, setReport] = useState<ImportReport | null>(null);
  const [busy, setBusy] = useState(false);

  async function template(kind: "csv" | "xlsx") {
    try {
      const params = { categoryId: Number(categoryId) };
      const blob = kind === "csv" ? await downloadImportTemplateCsv(params) : await downloadImportTemplateExcel(params);
      saveBlob(blob, `asset-import-template.${kind}`);
    } catch (e) {
      toast({ title: "No template", description: apiErrorMessage(e), variant: "destructive" });
    }
  }

  async function run(commit: boolean) {
    if (!file) return;
    setBusy(true);
    try {
      const result = await send(commit ? "/imports/assets" : "/imports/assets/validate", file);
      if (result.report) setReport(result.report);
      if (result.error && !result.report) toast({ title: "Not checked", description: result.error, variant: "destructive" });
      else if (commit && result.status === 201) {
        toast({ title: `${result.report?.imported} assets imported`, description: "They are waiting for their Business Central asset numbers." });
        qc.invalidateQueries();
      } else if (commit) toast({ title: "Nothing imported", description: "Fix the rows listed below and upload again.", variant: "destructive" });
    } finally {
      setBusy(false);
    }
  }

  return (
    <Layout>
      <div className="max-w-5xl space-y-5">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Import assets</h1>
          <p className="text-sm text-muted-foreground">Load an existing register from Excel or CSV. Every row is checked as a registration would be; duplicates by serial, MAC or tag are refused.</p>
        </div>
        <section className="space-y-2 rounded-md border bg-card p-4">
          <h2 className="font-semibold">1. Template</h2>
          <div className="flex flex-wrap gap-2">
            <select className={SELECT_CLASS} value={categoryId} onChange={(e) => setCategoryId(e.target.value)} aria-label="Category">
              <option value="">Category…</option>
              {categories?.filter((c) => c.active).map((c) => <option key={c.id} value={c.id}>{c.code} · {c.name}</option>)}
            </select>
            <Button variant="outline" size="sm" disabled={!categoryId} onClick={() => template("xlsx")}>Excel template</Button>
            <Button variant="outline" size="sm" disabled={!categoryId} onClick={() => template("csv")}>CSV template</Button>
          </div>
          <p className="text-xs text-muted-foreground">One file may mix categories: the columns after "notes" are the category's fields (make, model, serial_number …).</p>
        </section>
        <section className="space-y-2 rounded-md border bg-card p-4">
          <h2 className="font-semibold">2. Check, then import</h2>
          <div className="flex flex-wrap items-center gap-2">
            <input type="file" accept=".csv,.xlsx" onChange={(e) => { setFile(e.target.files?.[0] ?? null); setReport(null); }} aria-label="File" />
            <Button size="sm" variant="secondary" disabled={!file || busy} onClick={() => run(false)}><Upload className="mr-1 h-4 w-4" /> Check file</Button>
            <Button size="sm" disabled={!file || busy || !report?.canImport || report.imported > 0} onClick={() => run(true)}>Import {report?.rowCount ?? ""} rows</Button>
          </div>
        </section>
        {report && (
          <section className="space-y-3 rounded-md border bg-card p-4">
            <div className={`flex items-center gap-2 rounded-md p-3 text-white ${report.canImport ? "bg-green-600" : "bg-red-600"}`}>
              {report.canImport ? <CheckCircle2 className="h-6 w-6" /> : <XCircle className="h-6 w-6" />}
              <span className="font-medium">
                {report.imported > 0 ? `${report.imported} assets imported` : report.canImport ? `All ${report.rowCount} rows are valid — ready to import` : `${report.errorRowCount} of ${report.rowCount} rows have errors — nothing will be imported`}
              </span>
            </div>
            <Table>
              <TableHeader><TableRow><TableHead className="w-16">Row</TableHead><TableHead>Asset</TableHead><TableHead>Problems</TableHead></TableRow></TableHeader>
              <TableBody>
                {report.rows.map((r) => (
                  <TableRow key={r.row} className={r.errors.length ? "bg-red-500/5" : ""}>
                    <TableCell className="font-mono">{r.row}</TableCell>
                    <TableCell>{r.description ?? "—"}</TableCell>
                    <TableCell className="text-sm">{r.errors.length ? <ul className="list-disc pl-4 text-red-700">{r.errors.map((e) => <li key={e}>{e}</li>)}</ul> : <span className="text-green-700">OK</span>}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </section>
        )}
      </div>
    </Layout>
  );
}
