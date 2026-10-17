import { describe, expect, it } from "vitest";
import {
  composePreviewQuery,
  nextDownIndex,
  nextUpIndex,
  tryAddPreviewTag,
} from "../find-query";

describe("composePreviewQuery", () => {
  it("returns null when there are no tags", () => {
    expect(composePreviewQuery([], false)).toBeNull();
    expect(composePreviewQuery(["  "], true)).toBeNull();
  });

  it("joins AND with spaces", () => {
    expect(composePreviewQuery(["项目A", "图片"], false)).toBe("项目A 图片");
  });

  it("joins OR with the OR keyword", () => {
    expect(composePreviewQuery(["项目A", "图片"], true)).toBe("项目A OR 图片");
  });

  it("trims and skips blanks", () => {
    expect(composePreviewQuery(["  a ", "", "b"], false)).toBe("a b");
  });
});

describe("tryAddPreviewTag", () => {
  it("rejects blank and duplicate aliases, case-insensitively", () => {
    const tags = ["Work"];
    expect(tryAddPreviewTag(tags, "  ")).toBe(false);
    expect(tryAddPreviewTag(tags, "work")).toBe(false);
    expect(tryAddPreviewTag(tags, "life")).toBe(true);
    expect(tags).toEqual(["Work", "life"]);
  });
});

describe("suggest list navigation", () => {
  it("moves down from none to the first item and stays on the last", () => {
    expect(nextDownIndex(-1, 3)).toBe(0);
    expect(nextDownIndex(0, 3)).toBe(1);
    expect(nextDownIndex(2, 3)).toBe(2);
    expect(nextDownIndex(0, 0)).toBe(-1);
  });

  it("moves up from none to the last item and stays on the first", () => {
    expect(nextUpIndex(-1, 3)).toBe(2);
    expect(nextUpIndex(2, 3)).toBe(1);
    expect(nextUpIndex(0, 3)).toBe(0);
    expect(nextUpIndex(0, 0)).toBe(-1);
  });
});
