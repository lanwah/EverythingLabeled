import { describe, expect, it } from "vitest";
import { shouldCheckCreateMissing } from "../create-missing-suggest";

describe("shouldCheckCreateMissing", () => {
  it("returns false for blank alias", () => {
    expect(shouldCheckCreateMissing(null, ["work"])).toBe(false);
    expect(shouldCheckCreateMissing(undefined, ["work"])).toBe(false);
    expect(shouldCheckCreateMissing("", ["work"])).toBe(false);
  });

  it("returns false on exact match", () => {
    expect(shouldCheckCreateMissing("work", ["work", "workspace"])).toBe(false);
  });

  it("exact match is case insensitive", () => {
    expect(shouldCheckCreateMissing("Work", ["work"])).toBe(false);
  });

  it("returns true when only prefix matches", () => {
    expect(shouldCheckCreateMissing("wor", ["work", "workspace"])).toBe(true);
  });

  it("returns true when suggest is empty", () => {
    expect(shouldCheckCreateMissing("newtag", [])).toBe(true);
  });
});
