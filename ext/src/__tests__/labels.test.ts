import { describe, expect, it } from "vitest";
import { tagChipLabel } from "../labels";

describe("tagChipLabel", () => {
  it("joins a non-empty namespace with a slash", () => {
    expect(tagChipLabel({ name: "inbox", namespace: "work" })).toBe(
      "work/inbox",
    );
  });

  it("uses the name alone when namespace is null", () => {
    expect(tagChipLabel({ name: "inbox", namespace: null })).toBe("inbox");
  });
});
