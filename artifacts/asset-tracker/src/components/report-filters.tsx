import { useListCategories, useListLocations, useListSbus } from "@workspace/api-client-react";
import { useCurrentUser } from "@/hooks/use-auth";
import { SELECT_CLASS } from "@/lib/constants";
import { isGroupWide, periodLabel, recentPeriods } from "@/lib/finance";

export interface ReportFilterValues {
  period: string;
  sbuId: string;
  locationId: string;
  categoryId: string;
}

/** Query parameters for the generated client (blank = no filter). */
export function toParams(f: ReportFilterValues) {
  return {
    period: f.period || undefined,
    sbuId: f.sbuId ? Number(f.sbuId) : undefined,
    locationId: f.locationId ? Number(f.locationId) : undefined,
    categoryId: f.categoryId ? Number(f.categoryId) : undefined,
  };
}

/**
 * Period / SBU / location / category filters shared by the register, the category report and the
 * dashboard. SBU roles see no SBU picker: the API limits them to their own SBU anyway.
 */
export function ReportFilters({
  value, onChange, show = ["period", "sbu", "location", "category"],
}: {
  value: ReportFilterValues;
  onChange: (v: ReportFilterValues) => void;
  show?: Array<"period" | "sbu" | "location" | "category">;
}) {
  const user = useCurrentUser();
  const { data: sbus } = useListSbus();
  const { data: locations } = useListLocations();
  const { data: categories } = useListCategories();
  const set = (k: keyof ReportFilterValues) => (e: React.ChangeEvent<HTMLSelectElement>) => onChange({ ...value, [k]: e.target.value });
  const visibleLocations = (locations ?? []).filter((l) => !value.sbuId || String(l.sbuId) === value.sbuId);
  return (
    <div className="flex flex-wrap items-end gap-3">
      {show.includes("period") && (
        <label className="flex flex-col gap-1 text-xs text-muted-foreground">Period
          <select className={SELECT_CLASS + " min-w-40"} value={value.period} onChange={set("period")}>
            {recentPeriods(36).map((p) => <option key={p} value={p}>{periodLabel(p)}</option>)}
          </select>
        </label>
      )}
      {show.includes("sbu") && isGroupWide(user) && (
        <label className="flex flex-col gap-1 text-xs text-muted-foreground">SBU
          <select className={SELECT_CLASS + " min-w-32"} value={value.sbuId} onChange={(e) => onChange({ ...value, sbuId: e.target.value, locationId: "" })}>
            <option value="">All SBUs</option>
            {(sbus ?? []).map((s) => <option key={s.id} value={s.id}>{s.code}</option>)}
          </select>
        </label>
      )}
      {show.includes("location") && (
        <label className="flex flex-col gap-1 text-xs text-muted-foreground">Location
          <select className={SELECT_CLASS + " min-w-44"} value={value.locationId} onChange={set("locationId")}>
            <option value="">All locations</option>
            {visibleLocations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
          </select>
        </label>
      )}
      {show.includes("category") && (
        <label className="flex flex-col gap-1 text-xs text-muted-foreground">Category
          <select className={SELECT_CLASS + " min-w-44"} value={value.categoryId} onChange={set("categoryId")}>
            <option value="">All categories</option>
            {(categories ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </label>
      )}
    </div>
  );
}

/** Reads the filters from the URL query (?period=&sbuId=…), so drill-down links keep them. */
export function filtersFromSearch(search: string, period: string): ReportFilterValues {
  const q = new URLSearchParams(search);
  return { period: q.get("period") ?? period, sbuId: q.get("sbuId") ?? "", locationId: q.get("locationId") ?? "", categoryId: q.get("categoryId") ?? "" };
}

export function searchOf(f: ReportFilterValues): string {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(f)) if (v) q.set(k, v);
  const s = q.toString();
  return s ? `?${s}` : "";
}
