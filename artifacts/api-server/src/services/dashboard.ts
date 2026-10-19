import { db, movementsTable } from "@workspace/db";
import { gte } from "drizzle-orm";
import { getConfig } from "../lib/config";
import { dateKey, lastNDayKeys, startOfDayKey } from "../lib/time";

export interface MovementTrendPoint {
  date: string; // YYYY-MM-DD, organisation timezone
  rfid: number;
  manual: number;
  gatePass: number;
  total: number;
}

/** Pure: bucket movements into calendar days (organisation timezone) by trigger type. */
export function bucketMovementsByDay(
  rows: Array<{ timestamp: Date; triggeredBy: string }>,
  dayKeys: string[],
  timeZone: string,
): MovementTrendPoint[] {
  const points = new Map<string, MovementTrendPoint>(
    dayKeys.map((date) => [date, { date, rfid: 0, manual: 0, gatePass: 0, total: 0 }]),
  );
  for (const row of rows) {
    const point = points.get(dateKey(row.timestamp, timeZone));
    if (!point) continue;
    if (row.triggeredBy === "rfid") point.rfid++;
    else if (row.triggeredBy === "gate_pass") point.gatePass++;
    else point.manual++;
    point.total++;
  }
  return [...points.values()];
}

/** Daily movement counts for the last `days` days, oldest first (dashboard chart). */
export async function getMovementTrend(days: number, now: Date = new Date()): Promise<MovementTrendPoint[]> {
  const { timezone } = getConfig();
  const keys = lastNDayKeys(now, days, timezone);
  const from = startOfDayKey(keys[0], timezone);
  const rows = await db
    .select({ timestamp: movementsTable.timestamp, triggeredBy: movementsTable.triggeredBy })
    .from(movementsTable)
    .where(gte(movementsTable.timestamp, from));
  return bucketMovementsByDay(rows, keys, timezone);
}
