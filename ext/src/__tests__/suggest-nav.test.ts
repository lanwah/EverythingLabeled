import { describe, expect, it } from "vitest";
import {
  emptySuggestNav,
  moveSuggestNav,
  selectedSuggestAlias,
  suggestNavFromRows,
} from "../suggest-nav";

const rows = [
  { alias: "工具", label: "工具" },
  { alias: "Git", label: "Git" },
  { alias: "官网", label: "官网" },
];

describe("suggestNavFromRows", () => {
  it("selects the first row by default", () => {
    expect(suggestNavFromRows(rows)).toEqual({ rows, index: 0 });
    expect(suggestNavFromRows([])).toEqual({ rows: [], index: -1 });
  });
});

describe("moveSuggestNav", () => {
  it("moves down and up within bounds", () => {
    const start = suggestNavFromRows(rows);
    const down = moveSuggestNav(start, true);
    expect(down.index).toBe(1);
    expect(moveSuggestNav(down, false).index).toBe(0);
    const last = moveSuggestNav(moveSuggestNav(down, true), true);
    expect(last.index).toBe(2);
    expect(moveSuggestNav(last, true).index).toBe(2);
  });

  it("is a no-op on an empty list", () => {
    expect(moveSuggestNav(emptySuggestNav(), true)).toEqual(emptySuggestNav());
  });
});

describe("selectedSuggestAlias", () => {
  it("returns the highlighted alias, falling back to the first", () => {
    expect(selectedSuggestAlias(suggestNavFromRows(rows))).toBe("工具");
    expect(selectedSuggestAlias({ rows, index: 2 })).toBe("官网");
    expect(selectedSuggestAlias({ rows, index: -1 })).toBe("工具");
    expect(selectedSuggestAlias(emptySuggestNav())).toBeUndefined();
  });
});
