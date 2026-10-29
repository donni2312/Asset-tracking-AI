import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import {
  getGetVerificationSessionQueryKey,
  useAddVerificationScans,
  useCloseVerificationSession,
  useGetVerificationSession,
  useListLocations,
  useListVerificationSessions,
  useResolveVerificationResult,
  useStartVerificationSession,
  type VerificationResolveInputResolution,
  type VerificationResult,
} from "@workspace/api-client-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { SbuBadge } from "@/components/sbu-badge";
import { useCurrentUser } from "@/hooks/use-auth";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS } from "@/lib/constants";

const RUNNERS = ["group_it", "sbu_it", "stores", "sbu_admin", "sbu_finance", "group_finance", "system_admin"];
const SCOPE_LABEL: Record<string, string> = { it: "IT", non_it: "Non-IT", all: "All items" };
const RESULT_CLASS: Record<string, string> = { found: "text-green-600", missing: "text-red-600", unrecorded: "text-amber-600" };
const RESOLUTIONS: Record<string, VerificationResolveInputResolution[]> = {
  missing: ["adjust", "write_off", "investigate"],
  unrecorded_known: ["adjust", "investigate"],
  unrecorded_unknown: ["register", "investigate"],
};
const RESOLUTION_LABEL: Record<string, string> = { adjust: "Adjust the register", write_off: "Write off (lost)", investigate: "Investigate", register: "Register the item" };

function useNotice() {
  const { toast } = useToast();
  const qc = useQueryClient();
  return {
    ok: (title: string) => { toast({ title }); qc.invalidateQueries(); },
    fail: (title: string) => (e: unknown) => toast({ title, description: apiErrorMessage(e), variant: "destructive" }),
  };
}

/** SF-06: SBU Finance resolves one difference, with a comment. */
function Resolve({ sessionId, result }: { sessionId: number; result: VerificationResult }) {
  const n = useNotice();
  const resolve = useResolveVerificationResult();
  const options = result.result === "missing" ? RESOLUTIONS.missing : result.assetId ? RESOLUTIONS.unrecorded_known : RESOLUTIONS.unrecorded_unknown;
  const [resolution, setResolution] = useState(options[0]);
  const [comment, setComment] = useState("");
  return (
    <form className="flex flex-wrap gap-2" onSubmit={(e) => {
      e.preventDefault();
      resolve.mutate({ id: sessionId, resultId: result.id, data: { resolution, comment } }, { onSuccess: () => n.ok("Resolved"), onError: n.fail("Not resolved") });
    }}>
      <select className={SELECT_CLASS} value={resolution} onChange={(e) => setResolution(e.target.value as VerificationResolveInputResolution)} aria-label="Resolution">
        {options.map((o) => <option key={o} value={o}>{RESOLUTION_LABEL[o]}</option>)}
      </select>
      <Input className="min-w-48 flex-1" value={comment} onChange={(e) => setComment(e.target.value)} placeholder="Comment (required)" />
      <Button size="sm" type="submit" disabled={comment.trim().length < 3 || resolve.isPending}>Resolve</Button>
    </form>
  );
}

function Session({ id }: { id: number }) {
  const n = useNotice();
  const { data } = useGetVerificationSession(id, { query: { queryKey: getGetVerificationSessionQueryKey(id) } });
  const scan = useAddVerificationScans();
  const close = useCloseVerificationSession();
  const [codes, setCodes] = useState("");
  if (!data) return null;
  return (
    <section className="space-y-4 rounded-md border bg-card p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="font-semibold">Count #{data.id}: {data.locationName} <span className="text-sm font-normal text-muted-foreground">({SCOPE_LABEL[data.scope]}, {data.status})</span></h2>
          <p className="text-xs text-muted-foreground">Started by {data.startedBy}, {new Date(data.startedAt).toLocaleString()}</p>
        </div>
        <div className="flex gap-4 font-mono text-sm">
          <span>expected {data.expectedCount}</span><span>scanned {data.scanCount}</span>
          {data.status !== "open" && <><span className="text-green-600">found {data.foundCount}</span><span className="text-red-600">missing {data.missingCount}</span><span className="text-amber-600">unrecorded {data.unrecordedCount}</span></>}
        </div>
      </div>

      {data.canScan && (
        <form className="flex flex-wrap gap-2" onSubmit={(e) => {
          e.preventDefault();
          const list = codes.split(/[\s,]+/).map((c) => c.trim()).filter(Boolean);
          scan.mutate({ id, data: { codes: list } }, { onSuccess: () => { setCodes(""); n.ok(`${list.length} code(s) recorded`); }, onError: n.fail("Scans not recorded") });
        }}>
          <Input className="min-w-56 flex-1 font-mono" value={codes} onChange={(e) => setCodes(e.target.value)} placeholder="Scan tags (one after another, or paste a list)" autoFocus />
          <Button type="submit" disabled={!codes.trim() || scan.isPending}>Add</Button>
          <Button type="button" variant="secondary" disabled={close.isPending}
            onClick={() => close.mutate({ id }, { onSuccess: () => n.ok("Count closed"), onError: n.fail("Not closed") })}>Close count</Button>
        </form>
      )}

      {data.status === "open" ? (
        <ul className="max-h-60 divide-y overflow-auto rounded-md border text-sm">
          {data.scans.map((s) => <li key={s.code} className="flex justify-between p-2"><span className="font-mono">{s.code}</span><span className="text-muted-foreground">{s.assetName ?? "not in the register"}</span></li>)}
          {data.scans.length === 0 && <li className="p-2 text-muted-foreground">Nothing scanned yet.</li>}
        </ul>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <TableHeader><TableRow><TableHead>Result</TableHead><TableHead>Item</TableHead><TableHead>Register says</TableHead><TableHead>Resolution</TableHead></TableRow></TableHeader>
            <TableBody>
              {data.results.map((r) => (
                <TableRow key={r.id}>
                  <TableCell className={`font-medium ${RESULT_CLASS[r.result]}`}>{r.result}</TableCell>
                  <TableCell>{r.assetName ?? "Unknown item"}<div className="font-mono text-xs text-muted-foreground">{r.assetNumber ?? r.rfidTag ?? r.code}</div></TableCell>
                  <TableCell className="text-sm">{r.recordedLocationName ?? "—"}</TableCell>
                  <TableCell className="min-w-72 text-sm">
                    {r.result === "found" ? "—" : r.resolution
                      ? <span>{RESOLUTION_LABEL[r.resolution]}: {r.comment} <span className="text-xs text-muted-foreground">({r.resolvedBy})</span></span>
                      : data.canResolve ? <Resolve sessionId={id} result={r} /> : <span className="text-muted-foreground">Waiting for SBU Finance</span>}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </section>
  );
}

/** SI-07 / SF-06: counts of a location; open one, scan, close, then SBU Finance reconciles the differences. */
export function VerificationCounts() {
  const user = useCurrentUser();
  const n = useNotice();
  const { data: sessions } = useListVerificationSessions();
  const { data: locations } = useListLocations(user?.sbuId ? { sbuId: user.sbuId } : undefined);
  const start = useStartVerificationSession();
  const [locationId, setLocationId] = useState("");
  const [selected, setSelected] = useState<number | null>(null);
  const canStart = !!user && RUNNERS.includes(user.role);

  return (
    <div className="space-y-4">
      {canStart && (
        <form className="flex flex-wrap gap-2" onSubmit={(e) => {
          e.preventDefault();
          start.mutate({ data: { locationId: Number(locationId) } }, { onSuccess: (s) => { setSelected(s.id); n.ok("Count started"); }, onError: n.fail("Not started") });
        }}>
          <select className={SELECT_CLASS} value={locationId} onChange={(e) => setLocationId(e.target.value)} aria-label="Location">
            <option value="">Location to count…</option>
            {locations?.filter((l) => l.type !== "gate").map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
          </select>
          <Button type="submit" disabled={!locationId || start.isPending}>Start count</Button>
        </form>
      )}
      <div className="overflow-x-auto rounded-md border bg-card">
        <Table>
          <TableHeader><TableRow><TableHead>#</TableHead><TableHead>SBU</TableHead><TableHead>Location</TableHead><TableHead>Scope</TableHead><TableHead>Status</TableHead><TableHead>Differences</TableHead><TableHead /></TableRow></TableHeader>
          <TableBody>
            {sessions?.map((s) => (
              <TableRow key={s.id} className={selected === s.id ? "bg-muted/50" : ""}>
                <TableCell className="font-mono">{s.id}</TableCell>
                <TableCell><SbuBadge code={s.sbuCode} /></TableCell>
                <TableCell>{s.locationName}</TableCell>
                <TableCell>{SCOPE_LABEL[s.scope]}</TableCell>
                <TableCell>{s.status}</TableCell>
                <TableCell className="font-mono text-sm">{s.status === "open" ? `${s.scanCount} scanned` : `${s.missingCount + s.unrecordedCount} (${s.unresolvedCount} open)`}</TableCell>
                <TableCell><Button size="sm" variant="ghost" onClick={() => setSelected(s.id)}>Open</Button></TableCell>
              </TableRow>
            ))}
            {sessions?.length === 0 && <TableRow><TableCell colSpan={7} className="text-center text-sm text-muted-foreground">No counts yet.</TableCell></TableRow>}
          </TableBody>
        </Table>
      </div>
      {selected && <Session key={selected} id={selected} />}
    </div>
  );
}
