import { useSyncExternalStore } from "react";

/**
 * Every date shown in the dashboard is formatted here, so one setting changes all
 * of them. The default is day-first (dd/mm/yyyy), which is how dates are written
 * in Zimbabwe; the alternatives are month-first and ISO.
 *
 * Stored in this browser only, next to the theme and the operator name — the MVP
 * has no user accounts. `Layout` re-mounts the page content when the preference
 * changes, so a switch is reflected everywhere at once (see components/layout.tsx).
 *
 * Note: the clock style follows the date order, because the two conventions go
 * together — month-first uses a 12-hour clock, day-first and ISO use 24-hour.
 */
export const DATE_FORMATS = [
  { value: "dd/mm/yyyy", label: "31/12/2026", hint: "Day first (default)" },
  { value: "mm/dd/yyyy", label: "12/31/2026", hint: "Month first" },
  { value: "yyyy-mm-dd", label: "2026-12-31", hint: "ISO 8601" },
] as const;

export type DateFormat = (typeof DATE_FORMATS)[number]["value"];

const STORAGE_KEY = "assetiq.dateFormat";
const DEFAULT_FORMAT: DateFormat = "dd/mm/yyyy";
const listeners = new Set<() => void>();

const isDateFormat = (v: unknown): v is DateFormat =>
  DATE_FORMATS.some((f) => f.value === v);

function read(): DateFormat {
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);
    return isDateFormat(stored) ? stored : DEFAULT_FORMAT;
  } catch {
    return DEFAULT_FORMAT;
  }
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  const onStorage = (e: StorageEvent) => {
    if (e.key === STORAGE_KEY) listener();
  };
  window.addEventListener("storage", onStorage);
  return () => {
    listeners.delete(listener);
    window.removeEventListener("storage", onStorage);
  };
}

export function setDateFormat(format: DateFormat) {
  try {
    window.localStorage.setItem(STORAGE_KEY, format);
  } catch {
    // storage unavailable (private mode) - the choice applies but isn't remembered
  }
  listeners.forEach((l) => l());
}

/** The current preference. Subscribing components re-render when it changes. */
export function useDateFormat(): [DateFormat, (format: DateFormat) => void] {
  const format = useSyncExternalStore(subscribe, read, () => DEFAULT_FORMAT);
  return [format, setDateFormat];
}

// ── Formatting ──────────────────────────────────────────────────────────────

const pad = (n: number) => String(n).padStart(2, "0");
const DATE_ONLY = /^(\d{4})-(\d{2})-(\d{2})$/;

type Parts = { year: number; month: number; day: number; hour: number; minute: number; hasTime: boolean };

/**
 * A date-only value ("2026-12-31", e.g. a gate pass return date) is a calendar
 * day, not an instant: `new Date()` would read it as UTC midnight and shift it a
 * day in western timezones, so its parts are taken from the string as written.
 */
function partsOf(value: string | Date): Parts | null {
  if (typeof value === "string") {
    const dateOnly = DATE_ONLY.exec(value);
    if (dateOnly) {
      const [, y, m, d] = dateOnly;
      return { year: +y, month: +m, day: +d, hour: 0, minute: 0, hasTime: false };
    }
  }
  const d = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(d.getTime())) return null;
  return {
    year: d.getFullYear(),
    month: d.getMonth() + 1,
    day: d.getDate(),
    hour: d.getHours(),
    minute: d.getMinutes(),
    hasTime: true,
  };
}

function datePart(p: Parts, format: DateFormat): string {
  const dd = pad(p.day);
  const mm = pad(p.month);
  switch (format) {
    case "mm/dd/yyyy": return `${mm}/${dd}/${p.year}`;
    case "yyyy-mm-dd": return `${p.year}-${mm}-${dd}`;
    default:           return `${dd}/${mm}/${p.year}`;
  }
}

function timePart(p: Parts, format: DateFormat): string {
  if (format !== "mm/dd/yyyy") return `${pad(p.hour)}:${pad(p.minute)}`;
  const suffix = p.hour < 12 ? "am" : "pm";
  const hour12 = p.hour % 12 === 0 ? 12 : p.hour % 12;
  return `${hour12}:${pad(p.minute)} ${suffix}`;
}

/** Date and time of an instant, e.g. "20/10/2026 14:05". Date-only values show no time. */
export function formatDate(value: string | Date | null | undefined, format: DateFormat = read()): string {
  if (!value) return "—";
  const p = partsOf(value);
  if (!p) return "—";
  return p.hasTime ? `${datePart(p, format)} ${timePart(p, format)}` : datePart(p, format);
}

/** Calendar day only, e.g. "20/10/2026" — for return dates, purchase dates. */
export function formatDateOnly(value: string | Date | null | undefined, format: DateFormat = read()): string {
  if (!value) return "—";
  const p = partsOf(value);
  return p ? datePart(p, format) : "—";
}

/** Time of day only, e.g. "14:05" — for same-day feeds. */
export function formatTime(value: string | Date | null | undefined, format: DateFormat = read()): string {
  if (!value) return "—";
  const p = partsOf(value);
  return p ? timePart(p, format) : "—";
}

/**
 * Short axis / chip label for a `YYYY-MM-DD` day, in the preferred order:
 * "31/12", "12/31" or "12-31", optionally prefixed with the weekday.
 */
export function formatDayLabel(
  day: string,
  { withWeekday = false }: { withWeekday?: boolean } = {},
  format: DateFormat = read(),
): string {
  const p = partsOf(day);
  if (!p) return day;
  const dd = pad(p.day);
  const mm = pad(p.month);
  const short =
    format === "mm/dd/yyyy" ? `${mm}/${dd}` : format === "yyyy-mm-dd" ? `${mm}-${dd}` : `${dd}/${mm}`;
  if (!withWeekday) return short;
  // Weekday of a calendar day, computed in UTC so it cannot drift by a day.
  const weekday = new Date(Date.UTC(p.year, p.month - 1, p.day))
    .toLocaleDateString("en-GB", { timeZone: "UTC", weekday: "short" });
  return `${weekday} ${short}`;
}
