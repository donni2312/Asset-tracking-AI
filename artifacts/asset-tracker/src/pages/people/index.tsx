import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useCreateDepartment, useCreateEmployee, useListDepartments, useListEmployees } from "@workspace/api-client-react";
import { Layout } from "@/components/layout";
import { SbuBadge } from "@/components/sbu-badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useCurrentUser } from "@/hooks/use-auth";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS } from "@/lib/constants";

/**
 * Employees (Phase 2): SBU HR keeps its SBU's records; the HOD sees their department; Group roles read every
 * SBU. Toolboxes, acknowledgements and exit clearance (HR-01 … HR-04) build on these records in Phase 4.
 */
export function People() {
  const user = useCurrentUser();
  const [q, setQ] = useState("");
  const [departmentId, setDepartmentId] = useState("");
  const { data: employees, isLoading } = useListEmployees(
    { q: q || undefined, departmentId: departmentId ? Number(departmentId) : undefined },
    { query: { queryKey: ["employees", q, departmentId] } },
  );
  const { data: departments } = useListDepartments();
  const hr = user?.role === "sbu_hr";

  return (
    <Layout>
      <div className="space-y-5">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">People &amp; Toolboxes</h1>
          <p className="text-sm text-muted-foreground">{user?.scope}. Items held counts what you may see of each person's items.</p>
        </div>
        {hr && <NewEmployee departments={departments ?? []} />}
        <div className="flex flex-wrap gap-2">
          <Input className="w-64" placeholder="Search name or employee number" value={q} onChange={(e) => setQ(e.target.value)} aria-label="Search employees" />
          <select className={SELECT_CLASS + " w-56"} value={departmentId} onChange={(e) => setDepartmentId(e.target.value)} aria-label="Department">
            <option value="">All departments</option>
            {(departments ?? []).map((d) => <option key={d.id} value={d.id}>{d.sbuCode} · {d.name}</option>)}
          </select>
        </div>
        <div className="overflow-x-auto rounded-md border bg-card">
          <Table>
            <TableHeader><TableRow>
              <TableHead>Employee</TableHead><TableHead>SBU</TableHead><TableHead>Department</TableHead><TableHead>Job role</TableHead>
              <TableHead>Line manager</TableHead><TableHead className="text-right">Items held</TableHead>
            </TableRow></TableHeader>
            <TableBody>
              {(employees ?? []).map((e) => (
                <TableRow key={e.id}>
                  <TableCell>{e.name} <span className="font-mono text-xs text-muted-foreground">{e.employeeNo}</span></TableCell>
                  <TableCell><SbuBadge code={e.sbuCode} /></TableCell>
                  <TableCell>{e.departmentName ?? "—"}</TableCell>
                  <TableCell>{e.jobRole ?? "—"}</TableCell>
                  <TableCell>{e.lineManagerName ?? "—"}</TableCell>
                  <TableCell className="text-right font-mono">{e.itemsHeld}</TableCell>
                </TableRow>
              ))}
              {!isLoading && employees?.length === 0 && <TableRow><TableCell colSpan={6} className="text-center text-sm text-muted-foreground">No employees in your scope.</TableCell></TableRow>}
            </TableBody>
          </Table>
        </div>
      </div>
    </Layout>
  );
}

function NewEmployee({ departments }: { departments: { id: number; name: string }[] }) {
  const create = useCreateEmployee();
  const qc = useQueryClient();
  const { toast } = useToast();
  const [form, setForm] = useState({ employeeNo: "", name: "", departmentId: "", jobRole: "", email: "" });
  return (
    <form
      className="flex flex-wrap items-end gap-2 rounded-md border bg-card p-3"
      onSubmit={(e) => {
        e.preventDefault();
        create.mutate(
          { data: { employeeNo: form.employeeNo, name: form.name, departmentId: form.departmentId ? Number(form.departmentId) : undefined, jobRole: form.jobRole || undefined, email: form.email || undefined } },
          {
            onSuccess: () => { toast({ title: "Employee added" }); qc.invalidateQueries(); setForm({ employeeNo: "", name: "", departmentId: "", jobRole: "", email: "" }); },
            onError: (err) => toast({ title: "Not added", description: apiErrorMessage(err), variant: "destructive" }),
          },
        );
      }}
    >
      <Input className="w-32" placeholder="Employee no." value={form.employeeNo} onChange={(e) => setForm({ ...form, employeeNo: e.target.value })} required />
      <Input className="w-48" placeholder="Full name" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required />
      <select className={SELECT_CLASS + " w-44"} value={form.departmentId} onChange={(e) => setForm({ ...form, departmentId: e.target.value })} aria-label="Department">
        <option value="">Department…</option>
        {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
      </select>
      <Input className="w-40" placeholder="Job role" value={form.jobRole} onChange={(e) => setForm({ ...form, jobRole: e.target.value })} />
      <Input className="w-52" type="email" placeholder="Email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} />
      <Button type="submit" disabled={create.isPending}>Add employee</Button>
    </form>
  );
}

/** Departments (Phase 2): SBU HR adds them; the department views of LM-01 / LM-02 build on them in Phase 4. */
export function Departments() {
  const user = useCurrentUser();
  const { data } = useListDepartments();
  const create = useCreateDepartment();
  const qc = useQueryClient();
  const { toast } = useToast();
  const [name, setName] = useState("");
  return (
    <Layout>
      <div className="space-y-5">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Departments</h1>
          <p className="text-sm text-muted-foreground">{user?.scope}</p>
        </div>
        {user?.role === "sbu_hr" && (
          <form className="flex gap-2" onSubmit={(e) => {
            e.preventDefault();
            create.mutate({ data: { name } }, {
              onSuccess: () => { toast({ title: "Department added" }); setName(""); qc.invalidateQueries(); },
              onError: (err) => toast({ title: "Not added", description: apiErrorMessage(err), variant: "destructive" }),
            });
          }}>
            <Input className="w-64" placeholder="Department name" value={name} onChange={(e) => setName(e.target.value)} required minLength={2} />
            <Button type="submit">Add department</Button>
          </form>
        )}
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {(data ?? []).map((d) => (
            <div key={d.id} className="rounded-md border bg-card p-4">
              <div className="flex items-center justify-between gap-2"><span className="font-semibold">{d.name}</span><SbuBadge code={d.sbuCode} /></div>
              <div className="mt-1 text-sm text-muted-foreground">{d.employeeCount} active employee{d.employeeCount === 1 ? "" : "s"}</div>
            </div>
          ))}
        </div>
      </div>
    </Layout>
  );
}
