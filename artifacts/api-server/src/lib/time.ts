/**
 * Timezone helpers built on `Intl` (no extra dependencies).
 *
 * Business rules that care about "working hours", "today" or "this week" are
 * evaluated in the organisation's timezone (ORG_TIMEZONE, default
 * Africa/Harare) rather than the server's timezone — the API may run in UTC
 * on Replit or Azure.
 */

export interface WorkingHours {
  /** First working hour, inclusive (0-23). */
  startHour: number;
  /** Hour the working day ends, exclusive (1-24). 18 means "until 17:59". */
  endHour: number;
  /** Working weekdays, 0 = Sunday … 6 = Saturday. */
  days: number[];
}

export interface ZonedParts {
  year: number;
  month: number; // 1-12
  day: number;
  hour: number; // 0-23
  minute: number;
  second: number;
  weekday: number; // 0 = Sunday … 6 = Saturday
}

export const DAY_MS = 24 * 60 * 60 * 1000;

const WEEKDAY_INDEX: Record<string, number> = { Sun: 0, Mon: 1, Tue: 2, Wed: 3, Thu: 4, Fri: 5, Sat: 6 };
const WEEKDAY_NAMES = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
const MONTH_NAMES = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

const formatterCache = new Map<string, Intl.DateTimeFormat>();

function formatter(timeZone: string): Intl.DateTimeFormat {
  let f = formatterCache.get(timeZone);
  if (!f) {
    f = new Intl.DateTimeFormat("en-US", {
      timeZone,
      hourCycle: "h23",
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
      weekday: "short",
    });
    formatterCache.set(timeZone, f);
  }
  return f;
}

/** Wall-clock components of `date` in `timeZone`. */
export function zonedParts(date: Date, timeZone: string): ZonedParts {
  const parts = formatter(timeZone).formatToParts(date);
  const get = (type: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === type)?.value ?? "0";
  return {
    year: Number(get("year")),
    month: Number(get("month")),
    day: Number(get("day")),
    hour: Number(get("hour")) % 24,
    minute: Number(get("minute")),
    second: Number(get("second")),
    weekday: WEEKDAY_INDEX[get("weekday")] ?? 0,
  };
}

/** Offset in ms between the zone's wall clock and UTC at the given instant. */
function zoneOffsetMs(date: Date, timeZone: string): number {
  const p = zonedParts(date, timeZone);
  const wallAsUtc = Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute, p.second);
  const instant = Math.floor(date.getTime() / 1000) * 1000;
  return wallAsUtc - instant;
}

/** UTC instant for a wall-clock time in `timeZone`. */
export function zonedTimeToUtc(
  year: number,
  month: number,
  day: number,
  hour: number,
  minute: number,
  timeZone: string,
): Date {
  const guess = Date.UTC(year, month - 1, day, hour, minute);
  const offset = zoneOffsetMs(new Date(guess), timeZone);
  let result = guess - offset;
  // A second pass corrects the rare case where the offset differs between the guess and the result (DST).
  const corrected = zoneOffsetMs(new Date(result), timeZone);
  if (corrected !== offset) result = guess - corrected;
  return new Date(result);
}

const pad = (n: number) => String(n).padStart(2, "0");

/** Calendar day (YYYY-MM-DD) of `date` in `timeZone`. */
export function dateKey(date: Date, timeZone: string): string {
  const p = zonedParts(date, timeZone);
  return `${p.year}-${pad(p.month)}-${pad(p.day)}`;
}

/** Midnight (start of the local day) containing `date`. */
export function startOfZonedDay(date: Date, timeZone: string): Date {
  const p = zonedParts(date, timeZone);
  return zonedTimeToUtc(p.year, p.month, p.day, 0, 0, timeZone);
}

/** Monday 00:00 of the local week containing `date` (ISO weeks start on Monday). */
export function startOfZonedWeek(date: Date, timeZone: string): Date {
  const p = zonedParts(date, timeZone);
  const daysSinceMonday = (p.weekday + 6) % 7;
  const noon = zonedTimeToUtc(p.year, p.month, p.day, 12, 0, timeZone);
  return startOfZonedDay(new Date(noon.getTime() - daysSinceMonday * DAY_MS), timeZone);
}

/** First day of the local month containing `date`, 00:00. */
export function startOfZonedMonth(date: Date, timeZone: string): Date {
  const p = zonedParts(date, timeZone);
  return zonedTimeToUtc(p.year, p.month, 1, 0, 0, timeZone);
}

/** The last `count` calendar days (YYYY-MM-DD), oldest first, ending with the day containing `now`. */
export function lastNDayKeys(now: Date, count: number, timeZone: string): string[] {
  const p = zonedParts(now, timeZone);
  const todayNoon = zonedTimeToUtc(p.year, p.month, p.day, 12, 0, timeZone).getTime();
  const keys: string[] = [];
  for (let i = count - 1; i >= 0; i--) keys.push(dateKey(new Date(todayNoon - i * DAY_MS), timeZone));
  return keys;
}

/** Start (00:00 local) of a YYYY-MM-DD calendar day. */
export function startOfDayKey(key: string, timeZone: string): Date {
  const [y, m, d] = key.split("-").map(Number);
  return zonedTimeToUtc(y, m, d, 0, 0, timeZone);
}

export function isWithinWorkingHours(date: Date, timeZone: string, hours: WorkingHours): boolean {
  const p = zonedParts(date, timeZone);
  return hours.days.includes(p.weekday) && p.hour >= hours.startHour && p.hour < hours.endHour;
}

/** e.g. "Mon–Fri 07:00–18:00" */
export function describeWorkingHours(hours: WorkingHours): string {
  const days = [...hours.days].sort((a, b) => a - b);
  const contiguous = days.every((d, i) => i === 0 || d === days[i - 1] + 1);
  const dayText =
    days.length === 0
      ? "no days"
      : contiguous && days.length > 1
        ? `${WEEKDAY_NAMES[days[0]]}–${WEEKDAY_NAMES[days[days.length - 1]]}`
        : days.map((d) => WEEKDAY_NAMES[d]).join(", ");
  return `${dayText} ${pad(hours.startHour)}:00–${pad(hours.endHour)}:00`;
}

/** e.g. "Sat 18 Oct 02:14" in the organisation timezone. */
export function formatZoned(date: Date, timeZone: string): string {
  const p = zonedParts(date, timeZone);
  return `${WEEKDAY_NAMES[p.weekday]} ${p.day} ${MONTH_NAMES[p.month - 1]} ${pad(p.hour)}:${pad(p.minute)}`;
}

/** e.g. "Sat 18 Oct" in the organisation timezone. */
export function formatZonedDay(date: Date, timeZone: string): string {
  const p = zonedParts(date, timeZone);
  return `${WEEKDAY_NAMES[p.weekday]} ${p.day} ${MONTH_NAMES[p.month - 1]}`;
}
