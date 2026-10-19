import { describe, expect, it } from "vitest";
import { loadConfig, parseWorkDays } from "./config";
import {
  dateKey,
  describeWorkingHours,
  formatZoned,
  isWithinWorkingHours,
  lastNDayKeys,
  startOfZonedDay,
  startOfZonedWeek,
  zonedParts,
} from "./time";

const TZ = "Africa/Harare"; // UTC+2, no daylight saving
const HOURS = { startHour: 7, endHour: 18, days: [1, 2, 3, 4, 5] };

describe("zoned time helpers", () => {
  it("reads wall-clock parts in the organisation timezone", () => {
    const p = zonedParts(new Date("2026-10-14T08:00:00Z"), TZ);
    expect(p).toMatchObject({ year: 2026, month: 10, day: 14, hour: 10, minute: 0, weekday: 3 });
  });

  it("computes the local calendar day, not the UTC day", () => {
    // 23:30 UTC on the 13th is already 01:30 on the 14th in Harare
    expect(dateKey(new Date("2026-10-13T23:30:00Z"), TZ)).toBe("2026-10-14");
  });

  it("finds local midnight and the Monday that starts the week", () => {
    const wednesday = new Date("2026-10-14T08:00:00Z");
    expect(startOfZonedDay(wednesday, TZ).toISOString()).toBe("2026-10-13T22:00:00.000Z");
    expect(startOfZonedWeek(wednesday, TZ).toISOString()).toBe("2026-10-11T22:00:00.000Z");
    // A Monday is its own week start; a Sunday belongs to the week that started six days earlier
    expect(startOfZonedWeek(new Date("2026-10-12T05:00:00Z"), TZ).toISOString()).toBe("2026-10-11T22:00:00.000Z");
    expect(startOfZonedWeek(new Date("2026-10-18T12:00:00Z"), TZ).toISOString()).toBe("2026-10-11T22:00:00.000Z");
  });

  it("lists the last N calendar days oldest first", () => {
    expect(lastNDayKeys(new Date("2026-10-14T08:00:00Z"), 3, TZ)).toEqual(["2026-10-12", "2026-10-13", "2026-10-14"]);
  });

  it("formats times for messages", () => {
    expect(formatZoned(new Date("2026-10-17T00:14:00Z"), TZ)).toBe("Sat 17 Oct 02:14");
    expect(describeWorkingHours(HOURS)).toBe("Mon–Fri 07:00–18:00");
  });
});

describe("working hours", () => {
  const at = (iso: string) => isWithinWorkingHours(new Date(iso), TZ, HOURS);

  it("is inside working hours on a weekday between 07:00 and 17:59 local", () => {
    expect(at("2026-10-14T05:00:00Z")).toBe(true); // Wed 07:00
    expect(at("2026-10-14T15:59:00Z")).toBe(true); // Wed 17:59
  });

  it("is outside working hours before 07:00, from 18:00, and at weekends", () => {
    expect(at("2026-10-14T04:59:00Z")).toBe(false); // Wed 06:59
    expect(at("2026-10-14T16:00:00Z")).toBe(false); // Wed 18:00
    expect(at("2026-10-17T08:00:00Z")).toBe(false); // Sat 10:00
  });
});

describe("configuration", () => {
  it("parses working-day lists and ranges", () => {
    expect(parseWorkDays("1-5")).toEqual([1, 2, 3, 4, 5]);
    expect(parseWorkDays("0,6")).toEqual([0, 6]);
    expect(parseWorkDays("1-3,5")).toEqual([1, 2, 3, 5]);
    expect(parseWorkDays("nonsense")).toEqual([1, 2, 3, 4, 5]);
  });

  it("falls back to safe defaults for invalid values", () => {
    const cfg = loadConfig({ ORG_TIMEZONE: "Not/AZone", WORK_HOURS_START: "25", OPENAI_API_KEY: "  " });
    expect(cfg.timezone).toBe("Africa/Harare");
    expect(cfg.workingHours.startHour).toBe(7);
    expect(cfg.openai.apiKey).toBeNull();
  });

  it("reads the OpenAI settings", () => {
    const cfg = loadConfig({ OPENAI_API_KEY: "sk-test", OPENAI_BASE_URL: "https://example.test/openai/v1/", OPENAI_AUTH_HEADER: "api-key" });
    expect(cfg.openai).toMatchObject({ apiKey: "sk-test", baseUrl: "https://example.test/openai/v1", authHeader: "api-key" });
  });
});
