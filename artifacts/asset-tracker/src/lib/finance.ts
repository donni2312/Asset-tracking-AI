import type { User, UserRole } from "@workspace/api-client-react";

/**
 * Helpers for the Priority 1 screens (AU-01 … AU-12): money, months, file downloads and what each role
 * may do. The API checks every action again; these only decide what the UI offers.
 */

const usd = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", minimumFractionDigits: 2, maximumFractionDigits: 2 });
const usdWhole = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", maximumFractionDigits: 0 });

/** $1,234.56 */
export const money = (value: number | null | undefined) => (value == null ? "—" : usd.format(value));

/** $1,235 (headline figures) */
export const moneyWhole = (value: number | null | undefined) => (value == null ? "—" : usdWhole.format(value));

/** The current month as YYYY-MM in the browser's time zone. */
export function currentPeriod(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
}

/** "October 2026" for "2026-10". */
export function periodLabel(period: string): string {
  const [y, m] = period.split("-").map(Number);
  return new Date(y, (m || 1) - 1, 1).toLocaleDateString("en-GB", { month: "long", year: "numeric" });
}

/** The last `count` months, newest first, as YYYY-MM. */
export function recentPeriods(count = 24): string[] {
  const d = new Date();
  return Array.from({ length: count }, (_, i) => {
    const x = new Date(d.getFullYear(), d.getMonth() - i, 1);
    return `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, "0")}`;
  });
}

/** Saves a downloaded Blob under `fileName`. */
export function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export const STATUS_LABEL: Record<string, string> = {
  pending_asset_number: "Pending asset number",
  active: "In use",
  maintenance: "In repair",
  retired: "Retired",
  missing: "Missing",
};

export const STATUS_TONE: Record<string, string> = {
  pending_asset_number: "bg-amber-500/15 text-amber-700 dark:text-amber-300",
  active: "bg-green-500/15 text-green-700 dark:text-green-400",
  maintenance: "bg-sky-500/15 text-sky-700 dark:text-sky-300",
  retired: "bg-slate-500/15 text-slate-600 dark:text-slate-300",
  missing: "bg-red-500/15 text-red-700 dark:text-red-400",
};

import { inGroup } from "@/lib/roles";

export { isGroupWide } from "@/lib/roles";

/** Roles that may register an asset of some kind (the API decides which categories and SBU). */
export const canRegisterSomething = (user: User | null) => inGroup(user, "registrars");

/** Mirrors AccessPolicy.CanRegister for the category picker (Stores registers non-IT items at receipt too). */
export function canRegisterCategory(user: User | null, isIt: boolean): boolean {
  if (!user) return false;
  if (!isIt) return (["sbu_admin", "stores"] as UserRole[]).includes(user.role);
  return (["group_it", "sbu_it", "stores"] as UserRole[]).includes(user.role);
}

export const isSbuFinance = (user: User | null) => user?.role === "sbu_finance";
