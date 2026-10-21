import { useEffect, useMemo, useState } from "react";
import { useLocation, useSearch } from "wouter";
import { useQueryClient } from "@tanstack/react-query";
import { useCreateMovement, useListAssets, useListLocations } from "@workspace/api-client-react";
import { AlertTriangle, ArrowRight, MapPin, ShieldAlert } from "lucide-react";
import { Layout } from "@/components/layout";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { useToast } from "@/hooks/use-toast";
import { useOperatorName } from "@/hooks/use-operator";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS, locationTypeLabel } from "@/lib/constants";
import { SearchableSelect, type SearchableOption } from "@/components/searchable-select";
import { personHint, usePeople } from "@/hooks/use-people";
import { PermissionNotice, usePermission } from "@/components/permission";

/** Manual movement — for moves that did not pass an RFID reader. */
export default function NewMovement() {
  const [, navigate] = useLocation();
  const search = useSearch();
  const qc = useQueryClient();
  const { toast } = useToast();
  const operator = useOperatorName();
  const { data: assets } = useListAssets();
  const { data: locations } = useListLocations();
  const { people } = usePeople();
  const createMovement = useCreateMovement();

  const [assetId, setAssetId] = useState(() => new URLSearchParams(search).get("assetId") ?? "");
  const [toLocationId, setToLocationId] = useState("");
  const [movedBy, setMovedBy] = useState(operator);
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!movedBy && operator) setMovedBy(operator);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [operator]);

  const movable = (assets ?? []).filter((a) => a.status !== "retired");
  const asset = movable.find((a) => String(a.id) === assetId);

  // Same picker as the gate-pass form: find an asset by name, tag or serial.
  const assetOptions: SearchableOption[] = useMemo(
    () =>
      movable.map((a) => ({
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
  const destination = locations?.find((l) => String(l.id) === toLocationId);

  // Destinations grouped by SBU, excluding where the asset already is.
  const groups = useMemo(() => {
    const bySbu = new Map<string, NonNullable<typeof locations>>();
    for (const loc of locations ?? []) {
      if (asset && loc.id === asset.currentLocationId) continue;
      const key = loc.sbuCode ? `${loc.sbuCode} — ${loc.sbuName ?? ""}` : "Shared / group-wide";
      if (!bySbu.has(key)) bySbu.set(key, []);
      bySbu.get(key)!.push(loc);
    }
    return [...bySbu.entries()];
  }, [locations, asset]);

  const crossSbu = asset?.sbuId != null && destination?.sbuId != null && asset.sbuId !== destination.sbuId;
  const { allowed: mayRecord } = usePermission("viewer", "record a movement");
  const canSubmit = mayRecord && !!asset && !!destination && movedBy.trim() !== "" && !createMovement.isPending;

  function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!asset || !destination || !canSubmit) return;
    setError(null);
    createMovement.mutate(
      { data: { assetId: asset.id, toLocationId: destination.id, movedBy: movedBy.trim(), notes: notes.trim() || undefined } },
      {
        onSuccess: (movement) => {
          toast({ title: "Movement recorded", description: `${movement.assetName} → ${movement.toLocationName}` });
          qc.invalidateQueries();
          navigate(`/assets/${asset.id}`);
        },
        onError: (err) => setError(apiErrorMessage(err, "Could not record the movement")),
      },
    );
  }

  return (
    <Layout>
      <div className="max-w-2xl mx-auto space-y-6">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Record Movement</h1>
          <p className="text-muted-foreground text-sm">
            Log a manual move when an asset changes location without passing an RFID reader.
          </p>
        </div>

        <PermissionNotice role="viewer" action="record a movement" />

        <Card>
          <CardHeader>
            <CardTitle>Movement Details</CardTitle>
          </CardHeader>
          <CardContent>
            <form onSubmit={submit} className="space-y-4">
              <div className="space-y-2">
                <Label htmlFor="mv-asset">Asset <span className="text-destructive">*</span></Label>
                <SearchableSelect
                  id="mv-asset"
                  value={assetId}
                  onChange={(next) => {
                    setAssetId(next);
                    setToLocationId("");
                  }}
                  options={assetOptions}
                  placeholder="Search by name, RFID tag or serial number…"
                  emptyMessage="No asset matches that name, tag or serial."
                  required
                />
              </div>

              {asset && (
                <div className="flex items-center gap-3 rounded-md border bg-muted/40 p-3 text-sm">
                  <MapPin className="w-4 h-4 text-muted-foreground flex-shrink-0" />
                  <div className="min-w-0 flex-1">
                    <div className="text-xs text-muted-foreground">Currently at</div>
                    <div className="font-medium truncate">{asset.currentLocationName ?? "Off-premises / no recorded location"}</div>
                  </div>
                  <ArrowRight className="w-4 h-4 text-muted-foreground flex-shrink-0" />
                  <div className="min-w-0 flex-1">
                    <div className="text-xs text-muted-foreground">Moving to</div>
                    <div className="font-medium truncate text-primary">{destination?.name ?? "—"}</div>
                  </div>
                </div>
              )}

              <div className="space-y-2">
                <Label htmlFor="mv-destination">Destination <span className="text-destructive">*</span></Label>
                <select
                  id="mv-destination"
                  className={SELECT_CLASS}
                  value={toLocationId}
                  onChange={(e) => setToLocationId(e.target.value)}
                  disabled={!asset}
                  required
                >
                  <option value="">{asset ? "Select a destination…" : "Choose an asset first"}</option>
                  {groups.map(([group, locs]) => (
                    <optgroup key={group} label={group}>
                      {locs.map((l) => (
                        <option key={l.id} value={l.id}>{l.name} ({locationTypeLabel(l.type)})</option>
                      ))}
                    </optgroup>
                  ))}
                </select>
              </div>

              {destination?.type === "gate" && (
                <div className="flex items-start gap-2 rounded-md border border-amber-200 dark:border-amber-900 bg-amber-50 dark:bg-amber-950/40 p-3 text-xs text-amber-800 dark:text-amber-200">
                  <ShieldAlert className="w-4 h-4 mt-0.5 flex-shrink-0" />
                  <span>Moving an asset to a gate does not authorise it to leave. Request a gate pass for exits.</span>
                </div>
              )}
              {crossSbu && (
                <div className="flex items-start gap-2 rounded-md border p-3 text-xs text-muted-foreground">
                  <AlertTriangle className="w-4 h-4 mt-0.5 flex-shrink-0" />
                  <span>
                    {destination?.name} belongs to {destination?.sbuCode}; this asset belongs to {asset?.sbuCode}.
                    Consider recording an SBU transfer on the asset if the move is permanent.
                  </span>
                </div>
              )}

              <div className="space-y-2">
                <Label htmlFor="mv-moved-by">Moved by <span className="text-destructive">*</span></Label>
                <SearchableSelect
                  id="mv-moved-by"
                  value={movedBy}
                  onChange={setMovedBy}
                  options={peopleOptions}
                  placeholder="Search people, or type a new name…"
                  allowCustom
                  required
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="mv-notes">Notes</Label>
                <Textarea id="mv-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="e.g. Moved for the board meeting" />
              </div>

              {error && <p className="text-sm text-destructive bg-destructive/10 border border-destructive/20 rounded-md p-2">{error}</p>}

              <div className="pt-2 flex justify-end gap-2">
                <Button type="button" variant="outline" onClick={() => navigate("/movements")}>Cancel</Button>
                <Button type="submit" disabled={!canSubmit}>{createMovement.isPending ? "Recording…" : "Record Movement"}</Button>
              </div>
            </form>
          </CardContent>
        </Card>
      </div>
    </Layout>
  );
}
