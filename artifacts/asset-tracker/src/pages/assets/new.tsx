import { useMemo, useState } from "react";
import { useLocation } from "wouter";
import { useCreateAsset, useListCategories, useListLocations, useListSbus, type AssetCondition } from "@workspace/api-client-react";
import { Layout } from "@/components/layout";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { TemplateFields, describe, filled, type TemplateDraft } from "@/components/template-fields";
import { useToast } from "@/hooks/use-toast";
import { useCurrentUser } from "@/hooks/use-auth";
import { apiErrorMessage } from "@/lib/api-error";
import { SELECT_CLASS } from "@/lib/constants";
import { canRegisterCategory, isGroupWide } from "@/lib/finance";

const CONDITIONS: AssetCondition[] = ["new", "good", "fair", "poor", "damaged"];

/**
 * Register an asset (AU-01 … AU-03, GI-02, AM-01). Picking a category shows only that category's
 * template fields; the description is built from them. The API validates everything again, issues the
 * asset number through Business Central and refuses duplicates.
 */
export default function NewAsset() {
  const [, navigate] = useLocation();
  const { toast } = useToast();
  const user = useCurrentUser();
  const createAsset = useCreateAsset();
  const { data: categories } = useListCategories();
  const { data: locations } = useListLocations();
  const { data: sbus } = useListSbus();

  const [categoryId, setCategoryId] = useState("");
  const [subCategory, setSubCategory] = useState("");
  const [values, setValues] = useState<TemplateDraft>({});
  const [noSerial, setNoSerial] = useState(false);
  const [noSerialReason, setNoSerialReason] = useState("");
  const [sbuId, setSbuId] = useState("");
  const [locationId, setLocationId] = useState("");
  const [custodian, setCustodian] = useState("");
  const [condition, setCondition] = useState<AssetCondition>("new");
  const [previousAssetNumber, setPreviousAssetNumber] = useState("");
  const [supplier, setSupplier] = useState("");
  const [poNo, setPoNo] = useState("");
  const [purchaseDate, setPurchaseDate] = useState("");
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);

  const allowedCategories = useMemo(() => (categories ?? []).filter((c) => canRegisterCategory(user, c.isIt)), [categories, user]);
  const category = allowedCategories.find((c) => String(c.id) === categoryId);
  const effectiveSbu = isGroupWide(user) ? sbuId : String(user?.sbuId ?? "");
  const sbuLocations = (locations ?? []).filter((l) => !effectiveSbu || String(l.sbuId) === effectiveSbu);
  const preview = describe(category, subCategory, values);
  const noIdentifier = category && !category.identifierFieldKey;

  function pickCategory(id: string) {
    setCategoryId(id);
    setSubCategory("");
    setValues({});
    setNoSerial(false);
    setNoSerialReason("");
  }

  function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!category) return;
    setError(null);
    const attributes = filled(values);
    if (noSerial && category.identifierFieldKey) delete attributes[category.identifierFieldKey];
    createAsset.mutate(
      {
        data: {
          categoryId: category.id,
          subCategory: subCategory || undefined,
          attributes,
          noSerialReason: noSerial || noIdentifier ? noSerialReason.trim() || undefined : undefined,
          sbuId: effectiveSbu ? Number(effectiveSbu) : undefined,
          currentLocationId: Number(locationId),
          assignedTo: custodian.trim(),
          condition,
          previousAssetNumber: previousAssetNumber.trim() || undefined,
          supplier: supplier.trim() || undefined,
          poNo: poNo.trim() || undefined,
          purchaseDate: purchaseDate || undefined,
          notes: notes.trim() || undefined,
        },
      },
      {
        onSuccess: (asset) => {
          toast({
            title: "Asset registered",
            description: asset.assetNumber ? `${asset.assetNumber} · ${asset.name}` : `${asset.name} — asset number pending from Business Central`,
          });
          navigate(`/assets/${asset.id}/card`);
        },
        onError: (err) => setError(apiErrorMessage(err, "Could not register the asset")),
      },
    );
  }

  if (!user || allowedCategories.length === 0 && categories) {
    return (
      <Layout>
        <div className="mx-auto max-w-2xl space-y-4">
          <h1 className="text-2xl font-bold tracking-tight">Register asset</h1>
          <Card><CardContent className="p-6 text-sm text-muted-foreground">
            {user ? `Your role (${user.roleName}) does not register assets. IT assets are registered by SBU IT, Group IT or Stores; non-IT assets only by the SBU's Admin Manager.` : "Sign in to register assets."}
          </CardContent></Card>
        </div>
      </Layout>
    );
  }

  return (
    <Layout>
      <form onSubmit={submit} className="space-y-6">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Register asset</h1>
          <p className="text-sm text-muted-foreground">Pick the category first: the form shows the fields that category needs. Fields marked * are required.</p>
        </div>

        <Card>
          <CardHeader><CardTitle className="text-base">Category</CardTitle></CardHeader>
          <CardContent className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <div className="space-y-1.5">
              <Label htmlFor="category" className="text-xs text-muted-foreground">Category *</Label>
              <select id="category" className={SELECT_CLASS} value={categoryId} onChange={(e) => pickCategory(e.target.value)} required>
                <option value="">Select a category…</option>
                {allowedCategories.map((c) => <option key={c.id} value={c.id}>{c.name}{c.isIt ? "" : " (non-IT)"}</option>)}
              </select>
            </div>
            {category && category.subCategories.length > 0 && (
              <div className="space-y-1.5">
                <Label htmlFor="sub" className="text-xs text-muted-foreground">Sub-category *</Label>
                <select id="sub" className={SELECT_CLASS} value={subCategory} onChange={(e) => setSubCategory(e.target.value)} required>
                  <option value="">Select…</option>
                  {category.subCategories.map((s) => <option key={s} value={s}>{s}</option>)}
                </select>
              </div>
            )}
            {isGroupWide(user) && (
              <div className="space-y-1.5">
                <Label htmlFor="sbu" className="text-xs text-muted-foreground">SBU *</Label>
                <select id="sbu" className={SELECT_CLASS} value={sbuId} onChange={(e) => { setSbuId(e.target.value); setLocationId(""); }} required>
                  <option value="">Select…</option>
                  {(sbus ?? []).map((s) => <option key={s.id} value={s.id}>{s.code} — {s.name}</option>)}
                </select>
              </div>
            )}
          </CardContent>
        </Card>

        {category && (
          <>
            <Card>
              <CardHeader><CardTitle className="text-base">{category.name} — details</CardTitle></CardHeader>
              <CardContent className="space-y-4">
                <TemplateFields category={category} values={values} onChange={setValues} noSerial={noSerial} />
                {category.identifierFieldKey ? (
                  <label className="flex items-center gap-2 text-sm">
                    <input type="checkbox" checked={noSerial} onChange={(e) => setNoSerial(e.target.checked)} className="h-4 w-4" />
                    This item has no manufacturer serial
                  </label>
                ) : (
                  <p className="text-xs text-muted-foreground">{category.name} items carry no manufacturer serial: give the reason; SBU Finance approves it and the asset number becomes the identifier.</p>
                )}
                {(noSerial || noIdentifier) && (
                  <div className="space-y-1.5">
                    <Label htmlFor="nsr" className="text-xs text-muted-foreground">Reason for "No serial" * (SBU Finance approves)</Label>
                    <Input id="nsr" value={noSerialReason} onChange={(e) => setNoSerialReason(e.target.value)} required />
                  </div>
                )}
              </CardContent>
            </Card>

            <Card>
              <CardHeader><CardTitle className="text-base">Custody</CardTitle></CardHeader>
              <CardContent className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                <div className="space-y-1.5">
                  <Label htmlFor="loc" className="text-xs text-muted-foreground">Location *</Label>
                  <select id="loc" className={SELECT_CLASS} value={locationId} onChange={(e) => setLocationId(e.target.value)} required>
                    <option value="">Select…</option>
                    {sbuLocations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
                  </select>
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="cust" className="text-xs text-muted-foreground">Custodian *</Label>
                  <Input id="cust" value={custodian} onChange={(e) => setCustodian(e.target.value)} placeholder="Person, or the store holding it" required />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="cond" className="text-xs text-muted-foreground">Condition *</Label>
                  <select id="cond" className={SELECT_CLASS} value={condition} onChange={(e) => setCondition(e.target.value as AssetCondition)}>
                    {CONDITIONS.map((c) => <option key={c} value={c}>{c[0].toUpperCase() + c.slice(1)}</option>)}
                  </select>
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="po" className="text-xs text-muted-foreground">PO number</Label>
                  <Input id="po" value={poNo} onChange={(e) => setPoNo(e.target.value)} className="font-mono" />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="sup" className="text-xs text-muted-foreground">Supplier</Label>
                  <Input id="sup" value={supplier} onChange={(e) => setSupplier(e.target.value)} />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="pd" className="text-xs text-muted-foreground">Purchase date</Label>
                  <Input id="pd" type="date" value={purchaseDate} onChange={(e) => setPurchaseDate(e.target.value)} />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="prev" className="text-xs text-muted-foreground">Previous asset number (imported assets)</Label>
                  <Input id="prev" value={previousAssetNumber} onChange={(e) => setPreviousAssetNumber(e.target.value)} className="font-mono" />
                </div>
                <div className="space-y-1.5 sm:col-span-2">
                  <Label htmlFor="notes" className="text-xs text-muted-foreground">Notes</Label>
                  <Textarea id="notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
                </div>
              </CardContent>
            </Card>

            <div className="flex flex-wrap items-center gap-6 rounded-md border border-dashed p-4">
              <div className="min-w-0 flex-1">
                <div className="text-xs text-muted-foreground">Description preview (built from the fields, cannot be typed)</div>
                <div className="mt-1 text-sm font-medium">{preview || "—"}</div>
              </div>
              <div>
                <div className="text-xs text-muted-foreground">Asset number</div>
                <div className="mt-1 font-mono text-sm">Issued by Business Central on save</div>
              </div>
            </div>
          </>
        )}

        {error && <p role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">{error}</p>}

        <div className="flex justify-end gap-2">
          <Button type="button" variant="outline" onClick={() => navigate("/register")}>Cancel</Button>
          <Button type="submit" disabled={!category || createAsset.isPending}>{createAsset.isPending ? "Registering…" : "Register asset"}</Button>
        </div>
      </form>
    </Layout>
  );
}
