import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useCreateUser, useListEmployees, useListRoles, useListSbus, useListUsers, useUpdateUser, type UserRole } from "@workspace/api-client-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useCurrentUser } from "@/hooks/use-auth";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS } from "@/lib/constants";
import { roleName } from "@/lib/roles";

/**
 * GI-08 / matrix "Manage users": Group IT manages every account except auditors (Group Finance does those);
 * SBU IT manages its own SBU's accounts and gives SBU roles only. The API checks every change again.
 */
export function UsersTab() {
  const me = useCurrentUser();
  const groupIt = me?.role === "group_it";
  const { data: users } = useListUsers();
  const { data: roles } = useListRoles();
  const { data: sbus } = useListSbus();
  const { data: employees } = useListEmployees();
  const create = useCreateUser();
  const update = useUpdateUser();
  const qc = useQueryClient();
  const { toast } = useToast();
  const [form, setForm] = useState({ username: "", name: "", password: "", role: "stores" as UserRole, sbuId: "", employeeId: "" });

  const offered = (roles ?? []).filter((r) => r.role !== "auditor" && (groupIt || r.sbuScoped));
  const chosen = roles?.find((r) => r.role === form.role);
  const done = (title: string) => { toast({ title }); qc.invalidateQueries(); };
  const fail = (title: string) => (e: unknown) => toast({ title, description: apiErrorMessage(e), variant: "destructive" });

  return (
    <div className="space-y-4">
      <p className="text-xs text-muted-foreground">
        {groupIt ? "Group IT manages every account except auditors (Group Finance gives those)." : "You manage the accounts of your own SBU and can give SBU roles only."}
        {" "}Disabled accounts that still carry a retired role (viewer / operator) wait here for Group IT to give them a current role.
      </p>
      <form
        className="flex flex-wrap items-end gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          create.mutate(
            { data: { username: form.username, name: form.name, password: form.password, role: form.role,
              sbuId: form.sbuId ? Number(form.sbuId) : undefined, employeeId: form.employeeId ? Number(form.employeeId) : undefined } },
            { onSuccess: () => { done("Account created"); setForm({ ...form, username: "", name: "", password: "" }); }, onError: fail("Not created") },
          );
        }}
      >
        <Input className="w-36" placeholder="username" value={form.username} onChange={(e) => setForm({ ...form, username: e.target.value })} required />
        <Input className="w-44" placeholder="Full name" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required />
        <Input className="w-40" type="password" placeholder="Password (10+)" minLength={10} value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} required />
        <select className={SELECT_CLASS + " w-52"} value={form.role} onChange={(e) => setForm({ ...form, role: e.target.value as UserRole })} aria-label="Role">
          {offered.map((r) => <option key={r.role} value={r.role}>{r.name}</option>)}
        </select>
        {groupIt && chosen?.sbuScoped && (
          <select className={SELECT_CLASS + " w-36"} value={form.sbuId} onChange={(e) => setForm({ ...form, sbuId: e.target.value })} aria-label="SBU" required>
            <option value="">SBU…</option>
            {(sbus ?? []).map((s) => <option key={s.id} value={s.id}>{s.code}</option>)}
          </select>
        )}
        {chosen?.needsEmployee && (
          <select className={SELECT_CLASS + " w-52"} value={form.employeeId} onChange={(e) => setForm({ ...form, employeeId: e.target.value })} aria-label="Employee" required>
            <option value="">Employee record…</option>
            {(employees ?? []).filter((x) => !form.sbuId || x.sbuId === Number(form.sbuId)).map((x) => <option key={x.id} value={x.id}>{x.name} · {x.sbuCode}</option>)}
          </select>
        )}
        <Button type="submit" disabled={create.isPending}>Create account</Button>
      </form>
      <div className="overflow-x-auto">
        <Table>
          <TableHeader><TableRow><TableHead>User</TableHead><TableHead>Role</TableHead><TableHead>SBU</TableHead><TableHead>Employee</TableHead><TableHead>Last sign-in</TableHead><TableHead /></TableRow></TableHeader>
          <TableBody>
            {(users ?? []).map((u) => (
              <TableRow key={u.id} className={u.active ? "" : "opacity-60"}>
                <TableCell>{u.name} <span className="font-mono text-xs text-muted-foreground">{u.username}</span></TableCell>
                <TableCell>
                  {u.id === me?.id || u.role === "auditor" ? roleName(u.role) : (
                    <select className={SELECT_CLASS + " w-48"} value={u.role} aria-label={`Role of ${u.username}`}
                      onChange={(e) => update.mutate({ id: u.id, data: { role: e.target.value as UserRole, reason: "Role change" } }, { onSuccess: () => done("Role changed"), onError: fail("Not changed") })}>
                      {!offered.some((r) => r.role === u.role) && <option value={u.role}>{roleName(u.role)}</option>}
                      {offered.map((r) => <option key={r.role} value={r.role}>{r.name}</option>)}
                    </select>
                  )}
                </TableCell>
                <TableCell>{u.sbuCode ?? "—"}</TableCell>
                <TableCell>{u.employeeName ?? "—"}</TableCell>
                <TableCell className="text-xs">{u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString("en-GB") : "never"}</TableCell>
                <TableCell>
                  {u.id !== me?.id && u.role !== "auditor" && (
                    <Button size="sm" variant="outline" onClick={() => update.mutate({ id: u.id, data: { active: !u.active } }, { onSuccess: () => done("Saved"), onError: fail("Not saved") })}>
                      {u.active ? "Disable" : "Enable"}
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </div>
  );
}
