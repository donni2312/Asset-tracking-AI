import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useGetSecurityChecklist, useRecordSecurityAction, type SecurityActionInputAction } from "@workspace/api-client-react";
import { CheckCircle2, Circle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";

const LABEL: Record<string, string> = { remote_wipe: "Remote wipe", account_disable: "Accounts disabled", police_report: "Police report" };

/** GI-07 on the asset card of a lost or stolen item: the incident and the security actions taken. */
export function SecurityChecklist({ assetId }: { assetId: number }) {
  const { toast } = useToast();
  const qc = useQueryClient();
  const { data } = useGetSecurityChecklist(assetId);
  const record = useRecordSecurityAction();
  const [reference, setReference] = useState<Record<string, string>>({});
  if (!data) return null;
  return (
    <section className="space-y-3 rounded-md border border-red-500/40 bg-card p-4">
      <div>
        <h2 className="font-semibold text-red-600">Lost / stolen — security actions</h2>
        <p className="text-xs text-muted-foreground">{data.lostDate} · reported by {data.reportedBy} · {data.incidentNotes}</p>
      </div>
      <ul className="space-y-2 text-sm">
        {data.actions.map((a) => (
          <li key={a.action} className="flex flex-wrap items-center gap-2">
            {a.done ? <CheckCircle2 className="h-4 w-4 text-green-600" /> : <Circle className="h-4 w-4 text-muted-foreground" />}
            <span className="w-40 font-medium">{LABEL[a.action]}</span>
            {a.done ? (
              <span className="text-xs text-muted-foreground">{a.reference ? `${a.reference} · ` : ""}{a.doneBy}, {a.doneAt ? new Date(a.doneAt).toLocaleString() : ""}</span>
            ) : data.canRecord && (
              <span className="flex gap-1">
                {a.action === "police_report" && (
                  <Input className="h-8 w-44" value={reference[a.action] ?? ""} onChange={(e) => setReference({ ...reference, [a.action]: e.target.value })} placeholder="Case number" />
                )}
                <Button size="sm" variant="outline" disabled={a.action === "police_report" && !reference[a.action]}
                  onClick={() => record.mutate({ id: assetId, data: { action: a.action as SecurityActionInputAction, reference: reference[a.action] || undefined } }, {
                    onSuccess: () => { toast({ title: `${LABEL[a.action]} recorded` }); qc.invalidateQueries(); },
                    onError: (err) => toast({ title: "Not recorded", description: apiErrorMessage(err), variant: "destructive" }),
                  })}>Mark done</Button>
              </span>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}
