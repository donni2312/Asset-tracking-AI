import { useEffect, useState } from "react";
import { useListSbus } from "@workspace/api-client-react";
import { Radio } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { LOCATION_TYPES, SELECT_CLASS, type LocationTypeValue } from "@/lib/constants";

export interface LocationFormValues {
  name: string;
  type: LocationTypeValue | "";
  building: string;
  floor: string;
  description: string;
  sbuId: string; // "" = shared / group-wide
}

export const EMPTY_LOCATION: LocationFormValues = { name: "", type: "", building: "", floor: "", description: "", sbuId: "" };

interface LocationFormProps {
  initial: LocationFormValues;
  submitLabel: string;
  submitting: boolean;
  error: string | null;
  onSubmit: (values: LocationFormValues) => void;
  onCancel: () => void;
}

/** Shared create / edit form for locations (offices, gates, warehouses…). */
export function LocationForm({ initial, submitLabel, submitting, error, onSubmit, onCancel }: LocationFormProps) {
  const { data: sbus } = useListSbus();
  const [values, setValues] = useState<LocationFormValues>(initial);
  useEffect(() => setValues(initial), [initial]);

  const set = <K extends keyof LocationFormValues>(key: K, value: LocationFormValues[K]) => setValues((v) => ({ ...v, [key]: value }));
  const valid = values.name.trim() !== "" && values.type !== "" && values.building.trim() !== "";

  return (
    <form
      className="space-y-4"
      onSubmit={(e) => {
        e.preventDefault();
        if (valid && !submitting) onSubmit(values);
      }}
    >
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div className="space-y-2">
          <Label htmlFor="loc-name">Name <span className="text-destructive">*</span></Label>
          <Input id="loc-name" required value={values.name} onChange={(e) => set("name", e.target.value)} placeholder="e.g. TSAS Main Gate" />
        </div>
        <div className="space-y-2">
          <Label htmlFor="loc-type">Type <span className="text-destructive">*</span></Label>
          <select id="loc-type" required className={SELECT_CLASS} value={values.type} onChange={(e) => set("type", e.target.value as LocationTypeValue)}>
            <option value="">Select type…</option>
            {LOCATION_TYPES.map((t) => (
              <option key={t.value} value={t.value}>{t.label}</option>
            ))}
          </select>
        </div>
      </div>

      {values.type === "gate" && (
        <div className="flex items-start gap-2 rounded-md border border-blue-200 dark:border-blue-900 bg-blue-50 dark:bg-blue-950/40 p-3 text-xs text-blue-800 dark:text-blue-200">
          <Radio className="w-4 h-4 mt-0.5 flex-shrink-0" />
          <span>
            Gate locations run the gate-pass check: when a reader here scans an asset without an approved pass,
            a pending gate pass is raised and approvers are notified.
          </span>
        </div>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div className="space-y-2">
          <Label htmlFor="loc-building">Building <span className="text-destructive">*</span></Label>
          <Input id="loc-building" required value={values.building} onChange={(e) => set("building", e.target.value)} placeholder="e.g. TSAS House" />
        </div>
        <div className="space-y-2">
          <Label htmlFor="loc-floor">Floor</Label>
          <Input id="loc-floor" value={values.floor} onChange={(e) => set("floor", e.target.value)} placeholder="e.g. 2 or G" />
        </div>
      </div>

      <div className="space-y-2">
        <Label htmlFor="loc-sbu">Strategic Business Unit</Label>
        <select id="loc-sbu" className={SELECT_CLASS} value={values.sbuId} onChange={(e) => set("sbuId", e.target.value)}>
          <option value="">Shared / group-wide</option>
          {sbus?.map((s) => (
            <option key={s.id} value={s.id}>{s.code} — {s.name}</option>
          ))}
        </select>
        <p className="text-[11px] text-muted-foreground">
          Used by the anomaly engine: an asset seen at a location of another SBU raises a location-mismatch alert.
        </p>
      </div>

      <div className="space-y-2">
        <Label htmlFor="loc-description">Description</Label>
        <Textarea id="loc-description" rows={3} value={values.description} onChange={(e) => set("description", e.target.value)} placeholder="Access notes, purpose, opening hours…" />
      </div>

      {error && <p className="text-sm text-destructive bg-destructive/10 border border-destructive/20 rounded-md p-2">{error}</p>}

      <div className="pt-2 flex justify-end gap-2">
        <Button type="button" variant="outline" onClick={onCancel}>Cancel</Button>
        <Button type="submit" disabled={!valid || submitting}>{submitting ? "Saving…" : submitLabel}</Button>
      </div>
    </form>
  );
}
