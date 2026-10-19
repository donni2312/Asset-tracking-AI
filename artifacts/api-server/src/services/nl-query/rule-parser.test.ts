import { describe, expect, it } from "vitest";
import { extractAssignee, parseDays, parsePeriod, parseQuestion } from "./rule-parser";

describe("parseQuestion — the plan's three example questions (section 6.5)", () => {
  it('"Where is the Dell laptop assigned to John Doe?"', () => {
    expect(parseQuestion("Where is the Dell laptop assigned to John Doe?")).toEqual({
      kind: "locate_asset",
      terms: ["dell", "laptop"],
      assignee: "john doe",
    });
  });

  it('"Which assets have not moved in 30 days?"', () => {
    expect(parseQuestion("Which assets have not moved in 30 days?")).toEqual({ kind: "not_moved", days: 30 });
  });

  it('"Show me all gate passes approved this week"', () => {
    expect(parseQuestion("Show me all gate passes approved this week")).toEqual({
      kind: "gate_passes",
      status: "approved",
      period: "this_week",
      periodDays: null,
    });
  });
});

describe("parseQuestion — variations", () => {
  it.each([
    ["Where's John Doe's laptop?", { kind: "locate_asset", terms: ["laptop"], assignee: "john doe" }],
    ["Find the Epson projector", { kind: "locate_asset", terms: ["epson", "projector"], assignee: null }],
    ["What is assigned to Tendai Moyo?", { kind: "assets_assigned_to", assignee: "tendai moyo" }],
    ["Which assets haven't moved for 6 weeks?", { kind: "not_moved", days: 42 }],
    ["Show idle assets", { kind: "not_moved", days: 30 }],
    ["Which gate passes are pending?", { kind: "gate_passes", status: "pending", period: "all", periodDays: null }],
    ["Gate passes rejected last week", { kind: "gate_passes", status: "rejected", period: "last_week", periodDays: null }],
    ["gate-passes in the last 10 days", { kind: "gate_passes", status: null, period: "last_n_days", periodDays: 10 }],
    ["Which assets are missing?", { kind: "assets_by_status", status: "missing" }],
    ["What is in repair?", { kind: "assets_by_status", status: "maintenance" }],
    ["How many laptops do we have?", { kind: "category_count", category: "Laptop" }],
    ["How many assets in total?", { kind: "count_summary" }],
    ["Show me unassigned assets", { kind: "unassigned" }],
    ["Tell me a joke", { kind: "help" }],
    ["   ", { kind: "help" }],
  ])("%s", (question, intent) => {
    expect(parseQuestion(question)).toEqual(intent);
  });
});

describe("helpers", () => {
  it("extracts the assignee and strips it from the remaining text", () => {
    const { assignee, rest } = extractAssignee("where is the dell laptop assigned to john doe currently");
    expect(assignee).toBe("john doe");
    expect(rest).not.toContain("john");
  });

  it("does not treat contractions as names", () => {
    expect(extractAssignee("where's the laptop").assignee).toBeNull();
  });

  it("converts durations to days", () => {
    expect(parseDays("not moved in 30 days")).toBe(30);
    expect(parseDays("not moved in 2 months")).toBe(60);
    expect(parseDays("not moved in a week")).toBe(7);
    expect(parseDays("not moved")).toBe(30);
  });

  it("recognises reporting periods", () => {
    expect(parsePeriod("approved today").period).toBe("today");
    expect(parsePeriod("approved yesterday").period).toBe("yesterday");
    expect(parsePeriod("approved this month").period).toBe("this_month");
    expect(parsePeriod("approved ever").period).toBe("all");
  });
});
