import { useEffect, useMemo, useState } from "react";
import { useLocation, useParams } from "wouter";
import { useQueryClient } from "@tanstack/react-query";
import {
  getGetAssetQueryKey, useGetAsset, useListLocations, useListSbus, useUpdateAsset,
  type AssetUpdate,
} from "@workspace/api-client-react";
import { ShieldAlert } from "lucide-react";
import { Layout } from "@/components/layout";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { SearchableSelect, type SearchableOption } from "@/components/searchable-select";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS } from "@/lib/constants";
import { personHint, usePeople } from "@/hooks/use-people";
import { useCurrentUser, useHasRole } from "@/hooks/use-auth";

const CATEGORIES = [
  "Laptop", "Desktop", "Projector", "Phone", "Printer",
  "Network Equipment", "UPS", "Furniture", "Vehicle", "Other",
];
const STATUSES = ["active", "maintenance", "missing", "retired"] as const;

/**
 * Edit an asset record.
 *
 * Operators and administrators may change every field (PATCH /assets/:id needs
 * the operator role). The API is what enforces that; this page only decides
 * whether the form is offered.
 */
export default function EditAsset() {
  const { id } = useParams();
  const assetId = Number(id);
  const [, navigate] = useLocation();
  const qc = useQueryClient();
  const { toast } = useToast();
  const user = useCurrentUser();
  const canEdit = useHasRole("operator");

  const { data: asset, isLoading, isError } = useGetAsset(assetId, {
    query: { enabled: Number.isInteger(assetId) && assetId > 0, queryKey: getGetAssetQueryKey(assetId) },
  });
  const { data: locations } = useListLocations();
  const { data: sbus } = useListSbus();
  const { people } = usePeople();
  const updateAsset = useUpdateAsset();

  const [form, setForm] = useState({
    name: "", category: "", status: "active" as (typeof STATUSES)[number],
    assignedTo: "", currentLocationId: "", sbuId: "", notes: "",
  });
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!asset) return;
    setForm({
      name: asset.name,
      category: asset.category,
      status: asset.status,
      assignedTo: asset.assignedTo ?? "",
      currentLocationId: asset.currentLocationId != null ? String(asset.currentLocationId) : "",
      sbuId: asset.sbuId != null ? String(asset.sbuId) : "",
      notes: asset.notes ?? "",
    });
  }, [asset]);

  const peopleOptions: SearchableOption[] = useMemo(
    () => people.map((p) => ({ value: p.name, label: p.name, hint: personHint(p) || undefined })),
    [people],
  );
  const locationOptions: SearchableOption[] = useMemo(
    () =>
      (locations ?? []).map((l) => ({
        value: String(l.id),
        label: l.name,
        hint: [l.building, l.floor].filter(Boolean).join(" · "),
        badge: l.sbuCode ?? undefined,
        keywords: [l.type].filter(Boolean) as string[],
      })),
    [locations],
  );

  function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!asset) return;
    setError(null);

    // Send only what actually changed: an unchanged record field would
    // otherwise make the API refuse an operator's legitimate edit.
    const data: AssetUpdate = {};
    if (form.status !== asset.status) data.status = form.status as AssetUpdate["status"];
    if (form.assignedTo.trim() !== (asset.assignedTo ?? "")) data.assignedTo = form.assignedTo.trim() || null;
    if (form.currentLocationId !== (asset.currentLocationId != null ? String(asset.currentLocationId) : "")) {
      data.currentLocationId = form.currentLocationId ? Number(form.currentLocationId) : null;
    }
    if (form.notes.trim() !== (asset.notes ?? "")) data.notes = form.notes.trim() || null;
    if (form.name.trim() !== asset.name) data.name = form.name.trim();
    if (form.category !== asset.category) data.category = form.category;
    if (form.sbuId !== (asset.sbuId != null ? String(asset.sbuId) : "")) {
      data.sbuId = form.sbuId ? Number(form.sbuId) : null;
    }

    if (Object.keys(data).length === 0) {
      toast({ title: "Nothing to save", description: "No fields were changed." });
      return;
    }

    updateAsset.mutate(
      { id: assetId, data },
      {
        onSuccess: (updated) => {
          toast({ title: "Asset updated", description: updated.name });
          qc.invalidateQueries();
          navigate(`/assets/${assetId}`);
        },
        onError: (err) => setError(apiErrorMessage(err, "Could not update the asset")),
      },
    );
  }

  if (!canEdit) {
    return (
      <Layout>
        <div className="mx-auto max-w-2xl space-y-4">
          <h1 className="text-2xl font-bold tracking-tight">Edit asset</h1>
          <Card>
            <CardContent className="flex items-start gap-3 p-6">
              <ShieldAlert className="mt-0.5 h-5 w-5 flex-shrink-0 text-amber-500" />
              <div className="space-y-1 text-sm">
                <p className="font-medium">
                  {user ? "Your role cannot edit assets." : "Sign in to edit this asset."}
                </p>
                <p className="text-muted-foreground">
                  {user
                    ? `You are signed in as ${user.name} (${user.role}). Editing needs the operator or admin role.`
                    : "Use “Sign in” at the bottom of the sidebar."}
                </p>
              </div>
            </CardContent>
          </Card>
          <Button variant="outline" onClick={() => navigate(`/assets/${assetId}`)}>Back to the asset</Button>
        </div>
      </Layout>
    );
  }

  return (
    <Layout>
      <div className="mx-auto max-w-2xl space-y-6">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Edit asset</h1>
          <p className="text-sm text-muted-foreground">
            {asset ? asset.name : "Update this asset's details."}
          </p>
        </div>


        <Card>
          <CardHeader>
            <CardTitle>Asset details</CardTitle>
          </CardHeader>
          <CardContent>
            {isLoading ? (
              <div className="h-64 animate-pulse rounded bg-muted" />
            ) : isError || !asset ? (
              <p className="text-sm text-muted-foreground">Asset not found.</p>
            ) : (
              <form onSubmit={submit} className="space-y-4">
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <div className="space-y-2">
                    <Label htmlFor="as-name">Name</Label>
                    <Input
                      id="as-name"
                      value={form.name}
                      onChange={(e) => setForm({ ...form, name: e.target.value })}
                      required
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="as-category">Category</Label>
                    <select
                      id="as-category"
                      className={SELECT_CLASS}
                      value={form.category}
                      onChange={(e) => setForm({ ...form, category: e.target.value })}
                    >
                      {!CATEGORIES.includes(form.category) && form.category && (
                        <option value={form.category}>{form.category}</option>
                      )}
                      {CATEGORIES.map((c) => <option key={c} value={c}>{c}</option>)}
                    </select>
                  </div>
                </div>

                {/* Identity: fixed for the life of the asset, so the registry and
                    the physical tag can never disagree. */}
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <div className="space-y-2">
                    <Label>Serial number</Label>
                    <Input value={asset.serialNumber} disabled readOnly className="font-mono" />
                  </div>
                  <div className="space-y-2">
                    <Label>RFID tag</Label>
                    <Input value={asset.rfidTag} disabled readOnly className="font-mono" />
                  </div>
                </div>
                <p className="-mt-2 text-[11px] text-muted-foreground">
                  Serial number and RFID tag identify the asset and cannot be changed here — re-tagging is a
                  lifecycle event, so the history stays honest.
                </p>

                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <div className="space-y-2">
                    <Label htmlFor="as-status">Status</Label>
                    <select
                      id="as-status"
                      className={SELECT_CLASS}
                      value={form.status}
                      onChange={(e) => setForm({ ...form, status: e.target.value as (typeof STATUSES)[number] })}
                    >
                      {STATUSES.map((s) => <option key={s} value={s}>{s}</option>)}
                    </select>
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="as-sbu">SBU</Label>
                    <select
                      id="as-sbu"
                      className={SELECT_CLASS}
                      value={form.sbuId}
                      onChange={(e) => setForm({ ...form, sbuId: e.target.value })}
                    >
                      <option value="">Unassigned</option>
                      {sbus?.map((s) => <option key={s.id} value={s.id}>{s.code} — {s.name}</option>)}
                    </select>
                  </div>
                </div>

                <div className="space-y-2">
                  <Label htmlFor="as-assigned">Assigned to</Label>
                  <SearchableSelect
                    id="as-assigned"
                    value={form.assignedTo}
                    onChange={(next) => setForm({ ...form, assignedTo: next })}
                    options={peopleOptions}
                    placeholder="Search people, or type a new name…"
                    allowCustom
                  />
                </div>

                <div className="space-y-2">
                  <Label htmlFor="as-location">Current location</Label>
                  <SearchableSelect
                    id="as-location"
                    value={form.currentLocationId}
                    onChange={(next) => setForm({ ...form, currentLocationId: next })}
                    options={locationOptions}
                    placeholder="Search locations…"
                    emptyMessage="No location matches."
                  />
                  <p className="text-[11px] text-muted-foreground">
                    Changing it here corrects the record; it does not log a movement. Use
                    <em> Record movement</em> for an actual move.
                  </p>
                </div>

                <div className="space-y-2">
                  <Label htmlFor="as-notes">Notes</Label>
                  <Textarea
                    id="as-notes"
                    rows={3}
                    value={form.notes}
                    onChange={(e) => setForm({ ...form, notes: e.target.value })}
                  />
                </div>

                {error && (
                  <p className="rounded-md border border-destructive/20 bg-destructive/10 p-2 text-sm text-destructive">
                    {error}
                  </p>
                )}

                <div className="flex justify-end gap-2 pt-2">
                  <Button type="button" variant="outline" onClick={() => navigate(`/assets/${assetId}`)}>
                    Cancel
                  </Button>
                  <Button type="submit" disabled={updateAsset.isPending}>
                    {updateAsset.isPending ? "Saving…" : "Save changes"}
                  </Button>
                </div>
              </form>
            )}
          </CardContent>
        </Card>
      </div>
    </Layout>
  );
}
