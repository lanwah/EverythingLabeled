import { describe, expect, it } from "vitest";
import { guessReplaceKind, kindCompatible, replaceKindHint } from "../replace-kind";

describe("guessReplaceKind", () => {
  it("leaves a blank address unset", () => {
    expect(guessReplaceKind(null)).toBeNull();
    expect(guessReplaceKind("  ")).toBeNull();
    expect(replaceKindHint("", null)).toBe("输入地址后会预选类型，仍可修改。");
  });

  it("treats http(s) as a page even when the path has an extension", () => {
    expect(guessReplaceKind("https://example.com/a.txt")).toBe("url");
    expect(guessReplaceKind("HTTP://example.com")).toBe("url");
  });

  it("treats an extension as a file and its absence as a folder", () => {
    expect(guessReplaceKind("D:\\notes.md")).toBe("file");
    expect(guessReplaceKind("D:\\Projects\\EverythingLabeled-v2\\")).toBe("folder");
    expect(guessReplaceKind(".gitignore")).toBe("folder");
  });

  it("keeps a manual choice and says so", () => {
    expect(replaceKindHint("D:\\notes.md", "folder")).toContain("当前选择是文件夹");
    expect(kindCompatible("D:\\notes.md", "folder")).toBe(true);
    expect(kindCompatible("D:\\notes.md", "url")).toBe(false);
    expect(kindCompatible("https://example.com", "file")).toBe(false);
  });
});
