import { useEffect, useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useApproveGatePass, useRejectGatePass, type GatePass } from "@workspace/api-client-react";
import { CheckCircle, Radio, ShieldAlert, XCircle } from "lucide-react";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { useToast } from "@/hooks/use-toast";
import { useOperatorName } from "@/hooks/use-operator";
import { apiErrorMessage } from "@/lib/api-error";
import { formatDate, formatDateOnly } from "@/lib/utils";
import { SearchableSelect } from "@/components/searchable-select";
import { personHint, usePeople } from "@/hooks/use-people";

export type GatePassAction = "approve" | "reject";

interface GatePassDecisionDialogProps {
  pass: GatePass | null;
  action: GatePassAction;
  onOpenChange: (open: boolean) => void;
}

/**
 * Approve / reject a gate pass. The approver's name is required and recorded
 * on the pass (replacing the old hard-coded "Admin User").
 */
export function GatePassDecisionDialog({ pass, action, onOpenChange }: GatePassDecisionDialogProps) {
  const qc = useQueryClient();
  const { toast } = useToast();
  const operator = useOperatorName();
  const { people } = usePeople();
  const approve = useApproveGatePass();
  const reject = useRejectGatePass();
  const mutation = action === "approve" ? approve : reject;

  const peopleOptions = useMemo(
    () => people.map((p) => ({ value: p.name, label: p.name, hint: personHint(p) || undefined })),
    [people],
  );

  const [decidedBy, setDecidedBy] = useState("");
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!pass) return;
    setDecidedBy(operator);
    setNote("");
    setError(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pass?.id, action]);

  if (!pass) return null;

  const raisedAtGate = pass.source === "rfid_gate";
  const canSubmit = decidedBy.trim() !== "" && !mutation.isPending;

  function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!pass || !canSubmit) return;
    setError(null);
    mutation.mutate(
      { id: pass.id, data: { decidedBy: decidedBy.trim(), note: note.trim() || undefined } },
      {
        onSuccess: (updated) => {
          if (action === "approve") {
            toast({
              title: `Gate pass #${updated.id} approved`,
              description: updated.exitedAt
                ? `${updated.assetName} has been released — exit recorded as a gate pass movement.`
                : `${updated.assetName} may now leave — the exit is recorded when a gate reader sees it.`,
            });
          } else {
            toast({
              title: `Gate pass #${updated.id} rejected`,
              description: `${updated.assetName} has been flagged — it must not leave the premises.`,
              variant: "destructive",
            });
          }
          qc.invalidateQueries();
          onOpenChange(false);
        },
        onError: (err) => setError(apiErrorMessage(err, `Could not ${action} the gate pass`)),
      },
    );
  }

  return (
    <Dialog open={!!pass} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <form onSubmit={submit}>
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              {action === "approve" ? <CheckCircle className="w-5 h-5 text-emerald-600 dark:text-emerald-400" /> : <XCircle className="w-5 h-5 text-destructive" />}
              {action === "approve" ? "Approve" : "Reject"} gate pass #{pass.id}
            </DialogTitle>
            <DialogDescription>
              {action === "approve"
                ? raisedAtGate
                  ? "The asset is waiting at the gate. Approving releases it and records the exit as a gate pass movement."
                  : "Approving authorises the exit. It is recorded as a gate pass movement when a gate reader sees the asset leave."
                : "Rejecting flags the asset: a critical alert is raised and a note is added to its history. The asset must not leave the premises."}
            </DialogDescription>
          </DialogHeader>

          <div className="my-4 rounded-md border bg-muted/40 p-3 text-sm space-y-1">
            <div className="font-medium">{pass.assetName} <span className="font-mono text-xs text-muted-foreground">{pass.rfidTag}</span></div>
            <div className="text-xs text-muted-foreground flex items-center gap-1.5">
              {raisedAtGate ? (
                <><Radio className="w-3 h-3" /> Raised automatically at {pass.gateLocationName ?? "a gate"} · {formatDate(pass.createdAt)}</>
              ) : (
                <><ShieldAlert className="w-3 h-3" /> Requested by {pass.requestedBy} · {formatDate(pass.createdAt)}</>
              )}
            </div>
            <div className="text-xs">{pass.reason}</div>
            {(pass.destination || pass.returnDate) && (
              <div className="text-xs text-muted-foreground">
                {pass.destination && <>To {pass.destination}</>}
                {pass.destination && pass.returnDate && " · "}
                {pass.returnDate && <>Due back {formatDateOnly(pass.returnDate)}</>}
              </div>
            )}
          </div>

          <div className="space-y-4">
            <div className="space-y-1.5">
              <Label htmlFor="gp-decided-by">Your name (approver) <span className="text-destructive">*</span></Label>
              <SearchableSelect
                id="gp-decided-by"
                value={decidedBy}
                onChange={setDecidedBy}
                options={peopleOptions}
                placeholder="Search people, or type your name…"
                allowCustom
                required
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="gp-note">{action === "reject" ? "Reason for rejecting" : "Note (optional)"}</Label>
              <Textarea
                id="gp-note"
                value={note}
                onChange={(e) => setNote(e.target.value)}
                rows={2}
                placeholder={action === "reject" ? "e.g. No business reason for the asset to leave site" : "e.g. Return by Friday"}
              />
            </div>
            {error && <p className="text-sm text-destructive bg-destructive/10 border border-destructive/20 rounded-md p-2">{error}</p>}
          </div>

          <DialogFooter className="mt-4">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button
              type="submit"
              disabled={!canSubmit}
              className={action === "approve" ? "bg-emerald-600 hover:bg-emerald-700" : ""}
              variant={action === "reject" ? "destructive" : "default"}
            >
              {mutation.isPending ? "Saving…" : action === "approve" ? "Approve" : "Reject & flag asset"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
