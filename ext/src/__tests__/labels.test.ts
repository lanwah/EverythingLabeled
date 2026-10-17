import { describe, expect, it } from "vitest";
import { suggestRowLabel, tagChipLabel } from "../labels";

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

describe("suggestRowLabel", () => {
  it("shows the tag head when the alias is the name", () => {
    expect(suggestRowLabel({ alias: "工作", name: "工作", namespace: null })).toBe("工作");
  });

  it("shows the alias beside a namespaced name", () => {
    expect(suggestRowLabel({ alias: "gz", name: "工作", namespace: "proj" })).toBe(
      "gz  (proj/工作)",
    );
  });
});
