import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import {
  useAddCategoryField, useCreateAuditor, useListAccessLog, useListAuditors, useListCategories, useListDepreciationLocks, useListSbus, useListSettings,
  useLockDepreciationMonth, useRunDepreciation, useUpdateAuditorAccess, useUpdateCategory, useUpdateSetting,
  type AssetCategory, type GroupSetting, type TemplateFieldInputType,
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
import { currentPeriod, money, periodLabel, recentPeriods } from "@/lib/finance";

/**
 * Category templates and depreciation policy (Group Finance; fields by Group IT / SBU Admin Manager),
 * Group settings, the monthly depreciation run and month lock (SBU / Group Finance), auditor access and
 * the access log (AU-12). Every action is checked again by the API and audit-logged.
 */
export default function Administration() {
  const user = useCurrentUser();
  if (!user) return <Layout><p className="text-sm text-muted-foreground">Sign in.</p></Layout>;
  const finance = user.role === "group_finance";
  return (
    <Layout>
      <div className="space-y-5">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Administration</h1>
          <p className="text-sm text-muted-foreground">Signed in as {user.roleName} · {user.scope}</p>
        </div>
        <Tabs defaultValue="categories">
          <TabsList className="flex-wrap">
            <TabsTrigger value="categories">Categories</TabsTrigger>
            <TabsTrigger value="depreciation">Depreciation run</TabsTrigger>
            <TabsTrigger value="settings">Group settings</TabsTrigger>
            {finance && <TabsTrigger value="auditors">Auditor access</TabsTrigger>}
            {finance && <TabsTrigger value="log">Access log</TabsTrigger>}
          </TabsList>
          <TabsContent value="categories"><Categories /></TabsContent>
          <TabsContent value="depreciation"><DepreciationRun /></TabsContent>
          <TabsContent value="settings"><Settings /></TabsContent>
          {finance && <TabsContent value="auditors"><Auditors /></TabsContent>}
          {finance && <TabsContent value="log"><AccessLogTab /></TabsContent>}
        </Tabs>
      </div>
    </Layout>
  );
}

function useNotify() {
  const { toast } = useToast();
  const qc = useQueryClient();
  return {
    ok: (title: string) => { toast({ title }); qc.invalidateQueries(); },
    fail: (title: string) => (err: unknown) => toast({ title, description: apiErrorMessage(err), variant: "destructive" }),
  };
}

function Categories() {
  const { data } = useListCategories({ includeInactive: "true" });
  const user = useCurrentUser();
  return (
    <div className="space-y-3">
      <p className="text-xs text-muted-foreground">Group Finance sets categories and depreciation policy; Group IT the fields of IT categories; the SBU Admin Manager those of non-IT categories.</p>
      {(data ?? []).map((c) => <CategoryCard key={c.id} category={c} canPolicy={user?.role === "group_finance"} canFields={c.isIt ? user?.role === "group_it" : user?.role === "sbu_admin"} />)}
    </div>
  );
}

function CategoryCard({ category: c, canPolicy, canFields }: { category: AssetCategory; canPolicy: boolean; canFields: boolean }) {
  const update = useUpdateCategory();
  const addField = useAddCategoryField();
  const n = useNotify();
  const [life, setLife] = useState(String(c.usefulLifeMonths ?? ""));
  const [rate, setRate] = useState(String(c.annualRate ?? ""));
  const [field, setField] = useState({ key: "", label: "", type: "text" as TemplateFieldInputType, required: false });
  return (
    <section className="rounded-md border bg-card p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-semibold">{c.name} <span className="ml-1 font-mono text-xs text-muted-foreground">{c.code} · {c.isIt ? "IT" : "non-IT"}{c.active ? "" : " · inactive"}</span></h2>
        <span className="text-xs text-muted-foreground">Description: <span className="font-mono">{c.descriptionPattern}</span></span>
      </div>
      <div className="mt-2 flex flex-wrap gap-1.5 text-xs">
        {c.fields.map((f) => <span key={f.id} className="rounded bg-muted px-2 py-0.5">{f.label}{f.required ? " *" : ""}{f.isIdentifier ? " (identifier)" : ""}</span>)}
      </div>
      <div className="mt-3 flex flex-wrap items-end gap-3 text-sm">
        <span className="text-muted-foreground">{c.depreciationMethod === "straight_line" ? `Straight-line, ${c.usefulLifeMonths} months` : `Reducing balance, ${((c.annualRate ?? 0) * 100).toFixed(1)}% a year`}</span>
        {canPolicy && (c.depreciationMethod === "straight_line" ? (
          <form className="flex gap-2" onSubmit={(e) => { e.preventDefault(); update.mutate({ id: c.id, data: { usefulLifeMonths: Number(life), reason: "Group policy" } }, { onSuccess: () => n.ok("Policy updated"), onError: n.fail("Not saved") }); }}>
            <Input className="w-24" type="number" min={1} value={life} onChange={(e) => setLife(e.target.value)} aria-label="Useful life in months" />
            <Button size="sm" variant="outline" type="submit">Set life</Button>
          </form>
        ) : (
          <form className="flex gap-2" onSubmit={(e) => { e.preventDefault(); update.mutate({ id: c.id, data: { annualRate: Number(rate), reason: "Group policy" } }, { onSuccess: () => n.ok("Policy updated"), onError: n.fail("Not saved") }); }}>
            <Input className="w-24" type="number" step="0.01" min={0.01} max={0.99} value={rate} onChange={(e) => setRate(e.target.value)} aria-label="Annual rate (0.25 = 25%)" />
            <Button size="sm" variant="outline" type="submit">Set rate</Button>
          </form>
        ))}
      </div>
      {canFields && (
        <form className="mt-3 flex flex-wrap items-end gap-2" onSubmit={(e) => { e.preventDefault(); addField.mutate({ id: c.id, data: field }, { onSuccess: () => n.ok("Field added"), onError: n.fail("Not added") }); }}>
          <Input className="w-36" placeholder="key (e.g. gpu)" value={field.key} onChange={(e) => setField({ ...field, key: e.target.value })} required />
          <Input className="w-44" placeholder="Label" value={field.label} onChange={(e) => setField({ ...field, label: e.target.value })} required />
          <select className={SELECT_CLASS + " w-28"} value={field.type} onChange={(e) => setField({ ...field, type: e.target.value as TemplateFieldInputType })}>
            {["text", "number", "integer", "date"].map((t) => <option key={t}>{t}</option>)}
          </select>
          <label className="flex items-center gap-1 text-xs"><input type="checkbox" checked={field.required} onChange={(e) => setField({ ...field, required: e.target.checked })} /> required</label>
          <Button size="sm" variant="outline" type="submit">Add field</Button>
        </form>
      )}
    </section>
  );
}

function DepreciationRun() {
  const user = useCurrentUser();
  const { data: sbus } = useListSbus();
  const { data: locks } = useListDepreciationLocks();
  const run = useRunDepreciation();
  const lock = useLockDepreciationMonth();
  const n = useNotify();
  const [period, setPeriod] = useState(currentPeriod());
  const [sbuId, setSbuId] = useState("");
  const allowed = user?.role === "sbu_finance" || user?.role === "group_finance";
  const sbu = user?.role === "sbu_finance" ? user.sbuId ?? undefined : sbuId ? Number(sbuId) : undefined;
  return (
    <div className="space-y-4">
      <p className="text-xs text-muted-foreground">The run stores one row per asset per month (AU-05, SF-03). A locked month is never recalculated: a later change that affects it is posted as an adjustment in the next open month.</p>
      {allowed ? (
        <div className="flex flex-wrap items-end gap-3">
          <select className={SELECT_CLASS + " w-44"} value={period} onChange={(e) => setPeriod(e.target.value)}>{recentPeriods(24).map((p) => <option key={p} value={p}>{periodLabel(p)}</option>)}</select>
          {user?.role === "group_finance" && (
            <select className={SELECT_CLASS + " w-36"} value={sbuId} onChange={(e) => setSbuId(e.target.value)}>
              <option value="">Whole Group</option>{(sbus ?? []).map((s) => <option key={s.id} value={s.id}>{s.code}</option>)}
            </select>
          )}
          <Button onClick={() => run.mutate({ data: { period, sbuId: sbu } }, { onSuccess: (r) => n.ok(`Run ${r.period}: ${r.assets} assets, charge ${money(r.chargeForPeriod)}`), onError: n.fail("Run failed") })}>Run depreciation</Button>
          {user?.role === "sbu_finance" && (
            <Button variant="outline" onClick={() => lock.mutate({ data: { period } }, { onSuccess: () => n.ok(`${period} locked`), onError: n.fail("Not locked") })}>Lock {periodLabel(period)}</Button>
          )}
        </div>
      ) : <p className="text-sm text-muted-foreground">Only SBU Finance (own SBU) and Group Finance run depreciation.</p>}
      <Table>
        <TableHeader><TableRow><TableHead>SBU</TableHead><TableHead>Locked month</TableHead><TableHead>By</TableHead><TableHead>When</TableHead></TableRow></TableHeader>
        <TableBody>
          {(locks ?? []).map((l) => <TableRow key={l.id}><TableCell>{sbus?.find((s) => s.id === l.sbuId)?.code}</TableCell><TableCell>{periodLabel(l.period)}</TableCell><TableCell>{l.lockedBy}</TableCell><TableCell>{new Date(l.lockedAt).toLocaleString("en-GB")}</TableCell></TableRow>)}
        </TableBody>
      </Table>
    </div>
  );
}

function Settings() {
  const { data } = useListSettings();
  return (
    <Table>
      <TableHeader><TableRow><TableHead>Setting</TableHead><TableHead>Value</TableHead><TableHead>Editable by</TableHead><TableHead /></TableRow></TableHeader>
      <TableBody>{(data ?? []).map((s) => <SettingRow key={s.key} setting={s} />)}</TableBody>
    </Table>
  );
}

function SettingRow({ setting }: { setting: GroupSetting }) {
  const user = useCurrentUser();
  const update = useUpdateSetting();
  const n = useNotify();
  const [text, setText] = useState(JSON.stringify(setting.value));
  const can = user?.role === setting.editableBy;
  return (
    <TableRow>
      <TableCell><div className="font-mono text-xs">{setting.key}</div><div className="text-xs text-muted-foreground">{setting.description}</div></TableCell>
      <TableCell><Input className="w-56 font-mono text-xs" value={text} onChange={(e) => setText(e.target.value)} disabled={!can} /></TableCell>
      <TableCell className="text-xs">{setting.editableBy === "group_finance" ? "Group Finance" : "Group IT"}</TableCell>
      <TableCell>{can && <Button size="sm" variant="outline" onClick={() => {
        let value: unknown;
        try { value = JSON.parse(text); } catch { n.fail("Not saved")(new Error("Enter a JSON value, e.g. 1000 or [80,100]")); return; }
        update.mutate({ key: setting.key, data: { value } }, { onSuccess: () => n.ok("Setting saved"), onError: n.fail("Not saved") });
      }}>Save</Button>}</TableCell>
    </TableRow>
  );
}

function Auditors() {
  const { data } = useListAuditors();
  const create = useCreateAuditor();
  const access = useUpdateAuditorAccess();
  const n = useNotify();
  const [form, setForm] = useState({ username: "", name: "", password: "", accessExpiresAt: "" });
  return (
    <div className="space-y-4">
      <form className="flex flex-wrap items-end gap-2" onSubmit={(e) => { e.preventDefault(); create.mutate({ data: { ...form, accessExpiresAt: new Date(form.accessExpiresAt + "T23:59:00").toISOString() } }, { onSuccess: () => n.ok("Auditor account created"), onError: n.fail("Not created") }); }}>
        <Input className="w-40" placeholder="username" value={form.username} onChange={(e) => setForm({ ...form, username: e.target.value })} required />
        <Input className="w-48" placeholder="Name / firm" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required />
        <Input className="w-44" type="password" placeholder="Password (10+)" minLength={10} value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} required />
        <label className="flex flex-col text-xs text-muted-foreground">Access until<Input type="date" value={form.accessExpiresAt} onChange={(e) => setForm({ ...form, accessExpiresAt: e.target.value })} required /></label>
        <Button type="submit">Create read-only login</Button>
      </form>
      <Table>
        <TableHeader><TableRow><TableHead>Auditor</TableHead><TableHead>Access until</TableHead><TableHead>Last sign-in</TableHead><TableHead /></TableRow></TableHeader>
        <TableBody>
          {(data ?? []).map((a) => (
            <TableRow key={a.id}>
              <TableCell>{a.name} <span className="font-mono text-xs text-muted-foreground">{a.username}</span></TableCell>
              <TableCell>{a.accessExpiresAt ? new Date(a.accessExpiresAt).toLocaleDateString("en-GB") : "—"}{!a.active && " (disabled)"}</TableCell>
              <TableCell>{a.lastLoginAt ? new Date(a.lastLoginAt).toLocaleString("en-GB") : "never"}</TableCell>
              <TableCell><Button size="sm" variant="outline" onClick={() => access.mutate({ id: a.id, data: { active: !a.active } }, { onSuccess: () => n.ok("Saved"), onError: n.fail("Not saved") })}>{a.active ? "Disable" : "Enable"}</Button></TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

function AccessLogTab() {
  const [role, setRole] = useState("auditor");
  const { data } = useListAccessLog({ role: role || undefined }, { query: { queryKey: ["access-log", role] } });
  return (
    <div className="space-y-3">
      <select className={SELECT_CLASS + " w-40"} value={role} onChange={(e) => setRole(e.target.value)}><option value="auditor">Auditors</option><option value="">Everyone</option></select>
      <Table>
        <TableHeader><TableRow><TableHead>When</TableHead><TableHead>User</TableHead><TableHead>Action</TableHead><TableHead>Detail</TableHead></TableRow></TableHeader>
        <TableBody>{(data ?? []).map((e) => <TableRow key={e.id}><TableCell className="text-xs">{new Date(e.at).toLocaleString("en-GB")}</TableCell><TableCell className="text-xs">{e.username} ({e.role})</TableCell><TableCell className="text-xs">{e.action}</TableCell><TableCell className="break-all text-xs">{e.detail}</TableCell></TableRow>)}</TableBody>
      </Table>
    </div>
  );
}
