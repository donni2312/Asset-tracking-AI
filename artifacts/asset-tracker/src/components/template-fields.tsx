import type { AssetCategory, TemplateField } from "@workspace/api-client-react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { SELECT_CLASS } from "@/lib/constants";

/** Template values as typed in the form (field key → text). */
export type TemplateDraft = Record<string, string>;

/**
 * The fields of one category template (AU-01): only the fields defined for the category, required ones
 * marked *. Validation happens on the server; this only renders the form the template describes.
 */
export function TemplateFields({
  category,
  values,
  onChange,
  noSerial,
  disabled,
}: {
  category: AssetCategory;
  values: TemplateDraft;
  onChange: (values: TemplateDraft) => void;
  noSerial?: boolean;
  disabled?: boolean;
}) {
  const set = (key: string, value: string) => onChange({ ...values, [key]: value });
  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
      {category.fields.map((field) => {
        const excused = noSerial && field.isIdentifier;
        if (excused) return null;
        return (
          <div key={field.key} className="space-y-1.5">
            <Label htmlFor={`f-${field.key}`} className="text-xs text-muted-foreground">
              {field.label}
              {field.unit ? ` (${field.unit})` : ""}
              {field.required ? " *" : ""}
            </Label>
            <FieldInput field={field} value={values[field.key] ?? ""} onChange={(v) => set(field.key, v)} disabled={disabled} />
            {field.isIdentifier && <p className="text-[11px] text-muted-foreground">Unique across the Group · scan the manufacturer barcode</p>}
          </div>
        );
      })}
    </div>
  );
}

function FieldInput({ field, value, onChange, disabled }: { field: TemplateField; value: string; onChange: (v: string) => void; disabled?: boolean }) {
  const id = `f-${field.key}`;
  if (field.type === "list") {
    return (
      <select id={id} className={SELECT_CLASS} value={value} onChange={(e) => onChange(e.target.value)} disabled={disabled} required={field.required}>
        <option value="">Select…</option>
        {(field.allowedValues ?? []).map((v) => <option key={v} value={v}>{v}</option>)}
      </select>
    );
  }
  const mono = field.isIdentifier || field.key === "mac_address" || field.key === "ip_address";
  return (
    <Input
      id={id}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      disabled={disabled}
      required={field.required && !field.isIdentifier}
      type={field.type === "date" ? "date" : field.type === "integer" || field.type === "number" ? "number" : "text"}
      step={field.type === "number" ? "any" : undefined}
      inputMode={field.type === "integer" ? "numeric" : undefined}
      className={mono ? "font-mono" : undefined}
    />
  );
}

/** Live description preview: the same pattern rules as the API (the API's result is what is saved). */
export function describe(category: AssetCategory | undefined, subCategory: string, values: TemplateDraft): string {
  if (!category) return "";
  const pascal = (key: string) => key.split("_").map((p) => p.charAt(0).toUpperCase() + p.slice(1)).join("");
  const text = category.descriptionPattern.replace(/\{([A-Za-z0-9]+)\}/g, (_, token: string) => {
    if (token === "SubCategory") return subCategory;
    if (token === "Category") return category.name;
    const field = category.fields.find((f) => pascal(f.key) === token);
    const value = field ? values[field.key]?.trim() : "";
    if (!field || !value) return "";
    return field.unit ? `${value} ${field.unit}` : value;
  });
  return text
    .replace(/\s{2,}/g, " ")
    .replace(/\s*,(\s*,)+/g, ",")
    .replace(/\s*–\s*(,\s*)+/g, " – ")
    .replace(/^[\s,–]+|[\s,–]+$/g, "")
    .trim();
}

/** Only the filled-in values, for the API (blank = not given). */
export function filled(values: TemplateDraft): Record<string, string> {
  return Object.fromEntries(Object.entries(values).filter(([, v]) => v.trim() !== "").map(([k, v]) => [k, v.trim()]));
}
