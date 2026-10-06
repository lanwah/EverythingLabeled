import { describe, expect, it } from "vitest";
import { defaultSelectedIds, decideOpen, type PreviewHit } from "../open-rules";

function hit(
  id: string,
  uri: string,
  kind: string,
): PreviewHit {
  return { id, uri, kind, displayName: null };
}

const httpA = hit("a", "https://ex.com/a", "url");
const httpB = hit("b", "http://ex.com/b", "url");
const fileC = hit("c", "file:///tmp/c", "file");
const httpD = hit("d", "https://ex.com/d", "url");

describe("decideOpen", () => {
  it("opens selected http urls in hits order and ignores a selected file", () => {
    const hits = [httpA, fileC, httpB];
    const result = decideOpen(
      hits,
      new Set(["a", "b", "c"]),
      8,
    );
    expect(result).toEqual({
      ok: true,
      urls: ["https://ex.com/a", "http://ex.com/b"],
    });
  });

  it("returns none when only a file is selected", () => {
    expect(decideOpen([httpA, fileC], new Set(["c"]), 8)).toEqual({
      ok: false,
      reason: "none",
    });
  });

  it("returns over_limit without truncating when openable count exceeds the limit", () => {
    const hits = [httpA, httpB, httpD];
    expect(decideOpen(hits, new Set(["a", "b", "d"]), 2)).toEqual({
      ok: false,
      reason: "over_limit",
    });
  });
});

describe("defaultSelectedIds", () => {
  it("includes only openable hit ids", () => {
    expect([...defaultSelectedIds([httpA, fileC, httpB])].sort()).toEqual([
      "a",
      "b",
    ]);
  });
});
