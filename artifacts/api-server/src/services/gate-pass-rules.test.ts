import { describe, expect, it } from "vitest";
import {
  findValidApprovedPass,
  gateDirection,
  isAuthorizedOutcome,
  isPassValidAt,
  resolveGateOutcome,
  validateReturnDate,
  type PassValidityFields,
} from "./gate-pass-rules";

const TZ = "Africa/Harare";
const wednesday10am = new Date("2026-10-14T08:00:00Z");

const pass = (overrides: Partial<PassValidityFields> = {}): PassValidityFields => ({
  id: 1,
  status: "approved",
  returnDate: null,
  decidedAt: new Date("2026-10-13T08:00:00Z"),
  ...overrides,
});

describe("isPassValidAt", () => {
  it("only approved passes authorise an exit", () => {
    expect(isPassValidAt(pass({ status: "pending" }), wednesday10am, TZ)).toBe(false);
    expect(isPassValidAt(pass({ status: "rejected" }), wednesday10am, TZ)).toBe(false);
    expect(isPassValidAt(pass(), wednesday10am, TZ)).toBe(true);
  });

  it("is valid up to and including the return date (organisation timezone)", () => {
    expect(isPassValidAt(pass({ returnDate: "2026-10-14" }), wednesday10am, TZ)).toBe(true);
    expect(isPassValidAt(pass({ returnDate: "2026-10-20" }), wednesday10am, TZ)).toBe(true);
    expect(isPassValidAt(pass({ returnDate: "2026-10-13" }), wednesday10am, TZ)).toBe(false);
  });

  it("does not authorise a scan that happened before the approval", () => {
    expect(isPassValidAt(pass({ decidedAt: new Date("2026-10-14T09:00:00Z") }), wednesday10am, TZ)).toBe(false);
  });
});

describe("findValidApprovedPass", () => {
  it("returns the most recently approved valid pass", () => {
    const older = pass({ id: 1, decidedAt: new Date("2026-10-10T08:00:00Z") });
    const newer = pass({ id: 2, decidedAt: new Date("2026-10-13T08:00:00Z") });
    const expired = pass({ id: 3, returnDate: "2026-10-01", decidedAt: new Date("2026-10-13T09:00:00Z") });
    expect(findValidApprovedPass([older, expired, newer], wednesday10am, TZ)?.id).toBe(2);
  });

  it("returns undefined when nothing is valid", () => {
    expect(findValidApprovedPass([pass({ status: "pending" })], wednesday10am, TZ)).toBeUndefined();
  });
});

describe("gate direction and outcome", () => {
  it("infers direction from whether the asset is on the premises", () => {
    expect(gateDirection(false, true)).toBe("internal");
    expect(gateDirection(true, true)).toBe("exit");
    expect(gateDirection(true, false)).toBe("entry");
  });

  it.each([
    [{ isGate: false, direction: "internal", hasValidPass: false, hasPendingPass: false }, "not_gate", true],
    [{ isGate: true, direction: "entry", hasValidPass: false, hasPendingPass: false }, "entry", true],
    [{ isGate: true, direction: "exit", hasValidPass: true, hasPendingPass: false }, "authorized", true],
    [{ isGate: true, direction: "exit", hasValidPass: false, hasPendingPass: true }, "pending_existing", false],
    [{ isGate: true, direction: "exit", hasValidPass: false, hasPendingPass: false }, "pending_created", false],
  ] as const)("%o → %s", (args, outcome, authorized) => {
    const result = resolveGateOutcome(args);
    expect(result).toBe(outcome);
    expect(isAuthorizedOutcome(result)).toBe(authorized);
  });
});

describe("validateReturnDate", () => {
  it("accepts empty, today and future dates", () => {
    expect(validateReturnDate(undefined, wednesday10am, TZ)).toBeNull();
    expect(validateReturnDate("2026-10-14", wednesday10am, TZ)).toBeNull();
    expect(validateReturnDate("2026-11-01", wednesday10am, TZ)).toBeNull();
  });

  it("rejects past or malformed dates", () => {
    expect(validateReturnDate("2026-10-13", wednesday10am, TZ)).toMatch(/past/);
    expect(validateReturnDate("14/10/2026", wednesday10am, TZ)).toMatch(/YYYY-MM-DD/);
  });
});
