import { useEffect, useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useCreateGatePass, useListAssets } from "@workspace/api-client-react";
import { ShieldPlus } from "lucide-react";
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
import { todayInputValue } from "@/lib/constants";
import { SearchableSelect, type SearchableOption } from "@/components/searchable-select";
import { DateField } from "@/components/date-field";
import { personHint, usePeople } from "@/hooks/use-people";
import { acknowledgeGatePass } from "@/components/gate-pass-notifications";

interface GatePassRequestDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Pre-select an asset (e.g. when opened from the asset detail page). */
  assetId?: number;
}

/** "Request gate pass" form — authorisation requested in advance, before the asset reaches a gate. */
export function GatePassRequestDialog({ open, onOpenChange, assetId }: GatePassRequestDialogProps) {
  const qc = useQueryClient();
  const { toast } = useToast();
  const [operator, setOperator] = useOperatorName();
  const { data: assets } = useListAssets();
  const { people } = usePeople();
  const createGatePass = useCreateGatePass();

  const [selectedAsset, setSelectedAsset] = useState<string>("");
  const [requestedBy, setRequestedBy] = useState("");
  const [reason, setReason] = useState("");
  const [destination, setDestination] = useState("");
  const [returnDate, setReturnDate] = useState("");
  const [error, setError] = useState<string | null>(null);

  // Reset the form each time it opens.
  useEffect(() => {
    if (!open) return;
    setSelectedAsset(assetId ? String(assetId) : "");
    setRequestedBy(operator);
    setReason("");
    setDestination("");
    setReturnDate("");
    setError(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, assetId]);

  const requestable = (assets ?? []).filter((a) => a.status !== "retired");

  // An asset is found by name, RFID tag, serial number, category, SBU or the
  // location it is sitting in - whichever the requester happens to know.
  const assetOptions: SearchableOption[] = useMemo(
    () =>
      requestable.map((a) => ({
        value: String(a.id),
        label: a.name,
        hint: [a.rfidTag, a.serialNumber, a.currentLocationName].filter(Boolean).join(" · "),
        badge: a.sbuCode ?? undefined,
        keywords: [a.category, a.assignedTo, a.sbuName, a.status].filter(Boolean) as string[],
      })),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [assets],
  );

  const peopleOptions: SearchableOption[] = useMemo(
    () => people.map((p) => ({ value: p.name, label: p.name, hint: personHint(p) || undefined })),
    [people],
  );
  const canSubmit = !!selectedAsset && requestedBy.trim() !== "" && reason.trim() !== "" && !createGatePass.isPending;

  function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!canSubmit) return;
    setError(null);
    createGatePass.mutate(
      {
        data: {
          assetId: Number(selectedAsset),
          requestedBy: requestedBy.trim(),
          reason: reason.trim(),
          destination: destination.trim() || undefined,
          returnDate: returnDate || undefined,
        },
      },
      {
        onSuccess: (pass) => {
          acknowledgeGatePass(pass.id); // the requester doesn't need an "awaiting approval" toast for their own pass
          if (!operator) setOperator(requestedBy.trim());
          toast({ title: `Gate pass #${pass.id} requested`, description: `${pass.assetName} is now awaiting approval.` });
          qc.invalidateQueries();
          onOpenChange(false);
        },
        onError: (err) => setError(apiErrorMessage(err, "Could not create the gate pass")),
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <form onSubmit={submit}>
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <ShieldPlus className="w-5 h-5 text-primary" />
              Request gate pass
            </DialogTitle>
            <DialogDescription>
              Ask for permission to take an asset off the premises. An approver is notified in-app; once approved,
              the exit is recorded automatically when a gate reader sees the asset leave.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4 py-4">
            <div className="space-y-1.5">
              <Label htmlFor="gp-asset">Asset <span className="text-destructive">*</span></Label>
              <SearchableSelect
                id="gp-asset"
                value={selectedAsset}
                onChange={setSelectedAsset}
                options={assetOptions}
                placeholder="Search by name, RFID tag or serial number…"
                emptyMessage="No asset matches that name, tag or serial."
                required
              />
              <p className="text-[11px] text-muted-foreground">
                {assetOptions.length} assets available. Retired assets cannot leave the premises.
              </p>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="gp-requested-by">Requested by <span className="text-destructive">*</span></Label>
                <SearchableSelect
                  id="gp-requested-by"
                  value={requestedBy}
                  onChange={setRequestedBy}
                  options={peopleOptions}
                  placeholder="Search people, or type a new name…"
                  allowCustom
                  required
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="gp-return">Return date</Label>
                <DateField id="gp-return" value={returnDate} min={todayInputValue()} onChange={setReturnDate} />
                <p className="text-[11px] text-muted-foreground">Leave empty for an open-ended pass.</p>
              </div>
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="gp-destination">Destination</Label>
              <Input id="gp-destination" value={destination} onChange={(e) => setDestination(e.target.value)} placeholder="e.g. Client site – Borrowdale" />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="gp-reason">Reason <span className="text-destructive">*</span></Label>
              <Textarea id="gp-reason" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Why does the asset need to leave the premises?" rows={3} required />
            </div>

            {error && <p className="text-sm text-destructive bg-destructive/10 border border-destructive/20 rounded-md p-2">{error}</p>}
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit" disabled={!canSubmit}>
              {createGatePass.isPending ? "Submitting…" : "Submit request"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
