import { describe, expect, it } from "vitest";
import {
  canonicalizeBaseUrl,
  DEFAULT_BASE_URL,
  DEFAULT_PREVIEW_LIMIT,
  DEFAULT_SUGGEST_LIMIT,
  normalizeStoredConfig,
  parsePreviewResourceType,
  previewApiKind,
  validateDraft,
} from "../config";

describe("canonicalizeBaseUrl", () => {
  it.each([
    "http://localhost:5123",
    "http://[::1]:5123",
    "https://127.0.0.1:5123",
    "http://127.0.0.1/foo",
    "http://127.0.0.1:5123?x=1",
    "http://127.0.0.1:5123#h",
  ])("rejects %s", (raw) => {
    expect(canonicalizeBaseUrl(raw).ok).toBe(false);
  });

  it("normalizes trailing slash to the same origin", () => {
    const a = canonicalizeBaseUrl("http://127.0.0.1:5123");
    const b = canonicalizeBaseUrl("http://127.0.0.1:5123/");
    expect(a).toEqual({ ok: true, value: "http://127.0.0.1:5123" });
    expect(b).toEqual({ ok: true, value: "http://127.0.0.1:5123" });
  });

  it("drops default port 80", () => {
    expect(canonicalizeBaseUrl("http://127.0.0.1")).toEqual({
      ok: true,
      value: "http://127.0.0.1",
    });
    expect(canonicalizeBaseUrl("http://127.0.0.1:80")).toEqual({
      ok: true,
      value: "http://127.0.0.1",
    });
  });
});

describe("normalizeStoredConfig", () => {
  it("uses defaults when storage is empty", () => {
    expect(normalizeStoredConfig(undefined)).toEqual({
      baseUrl: DEFAULT_BASE_URL,
      token: "",
      confirmUntag: true,
      previewLimit: DEFAULT_PREVIEW_LIMIT,
      suggestLimit: DEFAULT_SUGGEST_LIMIT,
      findTagLogic: "and",
      previewResourceType: "links",
    });
    expect(DEFAULT_BASE_URL).toBe("http://127.0.0.1:5123");
    expect(DEFAULT_PREVIEW_LIMIT).toBe(8);
    expect(DEFAULT_SUGGEST_LIMIT).toBe(8);
  });

  it("keeps explicit confirmUntag false and previewLimit / suggestLimit 16", () => {
    expect(
      normalizeStoredConfig({
        confirmUntag: false,
        previewLimit: 16,
        suggestLimit: 16,
      }),
    ).toEqual({
      baseUrl: DEFAULT_BASE_URL,
      token: "",
      confirmUntag: false,
      previewLimit: 16,
      suggestLimit: 16,
      findTagLogic: "and",
      previewResourceType: "links",
    });
  });

  it("parses findTagLogic or", () => {
    expect(normalizeStoredConfig({ findTagLogic: "or" }).findTagLogic).toBe("or");
    expect(normalizeStoredConfig({ findTagLogic: "AND" }).findTagLogic).toBe("and");
  });

  it("parses previewResourceType all", () => {
    expect(normalizeStoredConfig({ previewResourceType: "all" }).previewResourceType).toBe(
      "all",
    );
    expect(parsePreviewResourceType("ALL")).toBe("all");
    expect(parsePreviewResourceType("links")).toBe("links");
    expect(parsePreviewResourceType("nope")).toBe("links");
  });

  it("coerces illegal previewLimit / suggestLimit to 8", () => {
    expect(normalizeStoredConfig({ previewLimit: 0 }).previewLimit).toBe(8);
    expect(normalizeStoredConfig({ previewLimit: "99" }).previewLimit).toBe(8);
    expect(normalizeStoredConfig({ suggestLimit: 0 }).suggestLimit).toBe(8);
    expect(normalizeStoredConfig({ suggestLimit: "99" }).suggestLimit).toBe(8);
  });
});

describe("previewApiKind", () => {
  it("maps links to url and all to omitted", () => {
    expect(previewApiKind("links")).toBe("url");
    expect(previewApiKind("all")).toBeUndefined();
  });
});

describe("validateDraft", () => {
  const valid = {
    baseUrl: "http://127.0.0.1:5123",
    token: "",
    confirmUntag: true,
  };

  it("rejects previewLimit 0", () => {
    const result = validateDraft({
      ...valid,
      previewLimit: 0,
      suggestLimit: 8,
    });
    expect(result.ok).toBe(false);
  });

  it("rejects suggestLimit 0", () => {
    const result = validateDraft({
      ...valid,
      previewLimit: 8,
      suggestLimit: 0,
    });
    expect(result.ok).toBe(false);
  });

  it("accepts previewLimit / suggestLimit 8 and defaults previewResourceType to links", () => {
    const result = validateDraft({
      ...valid,
      previewLimit: 8,
      suggestLimit: 8,
    });
    expect(result).toEqual({
      ok: true,
      value: {
        baseUrl: "http://127.0.0.1:5123",
        token: "",
        confirmUntag: true,
        previewLimit: 8,
        suggestLimit: 8,
        findTagLogic: "and",
        previewResourceType: "links",
      },
    });
  });

  it("accepts previewResourceType all", () => {
    const result = validateDraft({
      ...valid,
      previewLimit: 8,
      suggestLimit: 8,
      previewResourceType: "all",
    });
    expect(result.ok).toBe(true);
    if (result.ok) expect(result.value.previewResourceType).toBe("all");
  });
});
