import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

/**
 * Dates are formatted in one place so the Settings preference applies everywhere.
 * Re-exported here because most pages already import it from "@/lib/utils".
 */
export { formatDate, formatDateOnly, formatTime, formatDayLabel } from "./date-format";

export function formatCurrency(value: number | null | undefined) {
  if (value == null) return "—";
  return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(value);
}
