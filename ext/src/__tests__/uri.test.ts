import { describe, expect, it } from "vitest";
import { isHttpPage, isOpenableUrl, stripFragment } from "../uri";

describe("stripFragment", () => {
  it("removes hash and keeps the query", () => {
    expect(stripFragment("https://ex.com/a?x=1#frag")).toBe(
      "https://ex.com/a?x=1",
    );
  });

  it("returns the same string when there is no hash", () => {
    expect(stripFragment("https://ex.com/a?x=1")).toBe("https://ex.com/a?x=1");
  });
});

describe("isHttpPage", () => {
  it("rejects chrome://extensions", () => {
    expect(isHttpPage("chrome://extensions")).toBe(false);
  });

  it("accepts http and https regardless of scheme case", () => {
    expect(isHttpPage("http://ex.com/")).toBe(true);
    expect(isHttpPage("HTTPS://ex.com/")).toBe(true);
  });
});

describe("isOpenableUrl", () => {
  it("requires kind url and an http(s) page", () => {
    expect(isOpenableUrl("url", "https://ex.com/a")).toBe(true);
    expect(isOpenableUrl("file", "https://ex.com/a")).toBe(false);
    expect(isOpenableUrl("url", "file:///tmp/x")).toBe(false);
  });
});
