import { Link } from "wouter";
import { useListVerificationOutcomes } from "@workspace/api-client-react";
import { SbuBadge } from "@/components/sbu-badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useCurrentUser } from "@/hooks/use-auth";
import { money } from "@/lib/finance";

/** GF-08: the outcome of each closed count per SBU, with values (NBV), and the missing assets. */
export function VerificationOutcomes() {
  const user = useCurrentUser();
  const { data } = useListVerificationOutcomes(undefined, { query: { enabled: !!user, queryKey: ["verification-outcomes"] } });
  return (
    <div className="space-y-4">
      <div className="overflow-x-auto rounded-md border bg-card">
        <Table>
          <TableHeader><TableRow>
            <TableHead>Count</TableHead><TableHead>SBU</TableHead><TableHead>Location</TableHead><TableHead>Closed</TableHead>
            <TableHead className="text-right">Found</TableHead><TableHead className="text-right">Missing</TableHead><TableHead className="text-right">Unrecorded</TableHead>
          </TableRow></TableHeader>
          <TableBody>
            {data?.map((o) => (
              <TableRow key={o.sessionId}>
                <TableCell className="font-mono">#{o.sessionId}</TableCell>
                <TableCell>{o.sbuCode && <SbuBadge code={o.sbuCode} />}</TableCell>
                <TableCell>{o.locationName}</TableCell>
                <TableCell className="text-xs">{o.closedAt ? new Date(o.closedAt).toLocaleDateString() : "—"} · {o.status}</TableCell>
                <TableCell className="text-right font-mono">{o.found} <span className="text-xs text-muted-foreground">{money(o.foundValue)}</span></TableCell>
                <TableCell className="text-right font-mono text-red-600">{o.missing} <span className="text-xs">{money(o.missingValue)}</span></TableCell>
                <TableCell className="text-right font-mono text-amber-600">{o.unrecorded} <span className="text-xs">{money(o.unrecordedValue)}</span></TableCell>
              </TableRow>
            ))}
            {data?.length === 0 && <TableRow><TableCell colSpan={7} className="text-center text-sm text-muted-foreground">No closed counts yet.</TableCell></TableRow>}
          </TableBody>
        </Table>
      </div>
      {data?.some((o) => o.missingAssets.length > 0) && (
        <section className="space-y-2">
          <h2 className="font-semibold">Missing assets</h2>
          <div className="overflow-x-auto rounded-md border bg-card">
            <Table>
              <TableHeader><TableRow><TableHead>Count</TableHead><TableHead>Asset</TableHead><TableHead>Last known location</TableHead><TableHead>Custodian</TableHead><TableHead>Last seen</TableHead><TableHead className="text-right">NBV</TableHead><TableHead>Resolution</TableHead></TableRow></TableHeader>
              <TableBody>
                {data.flatMap((o) => o.missingAssets.map((m) => (
                  <TableRow key={`${o.sessionId}-${m.assetId}`}>
                    <TableCell className="font-mono">#{o.sessionId}</TableCell>
                    <TableCell><Link href={`/assets/${m.assetId}/card`} className="text-primary hover:underline">{m.description}</Link><div className="font-mono text-xs text-muted-foreground">{m.assetNumber}</div></TableCell>
                    <TableCell>{m.lastLocation ?? "—"}</TableCell>
                    <TableCell>{m.custodian ?? "—"}</TableCell>
                    <TableCell className="text-xs">{m.lastSeen ? new Date(m.lastSeen).toLocaleDateString() : "—"}</TableCell>
                    <TableCell className="text-right font-mono">{money(m.netBookValue)}</TableCell>
                    <TableCell className="text-sm">{m.resolution?.replace("_", " ") ?? "open"}</TableCell>
                  </TableRow>
                )))}
              </TableBody>
            </Table>
          </div>
        </section>
      )}
    </div>
  );
}
