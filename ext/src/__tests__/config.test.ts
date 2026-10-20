import { describe, expect, it } from "vitest";
import {
  canonicalizeBaseUrl,
  DEFAULT_BASE_URL,
  DEFAULT_PREVIEW_LIMIT,
  normalizeStoredConfig,
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
      findTagLogic: "and",
    });
    expect(DEFAULT_BASE_URL).toBe("http://127.0.0.1:5123");
    expect(DEFAULT_PREVIEW_LIMIT).toBe(8);
  });

  it("keeps explicit confirmUntag false and previewLimit 16", () => {
    expect(
      normalizeStoredConfig({ confirmUntag: false, previewLimit: 16 }),
    ).toEqual({
      baseUrl: DEFAULT_BASE_URL,
      token: "",
      confirmUntag: false,
      previewLimit: 16,
      findTagLogic: "and",
    });
  });

  it("parses findTagLogic or", () => {
    expect(normalizeStoredConfig({ findTagLogic: "or" }).findTagLogic).toBe("or");
    expect(normalizeStoredConfig({ findTagLogic: "AND" }).findTagLogic).toBe("and");
  });

  it("coerces illegal previewLimit to 8", () => {
    expect(normalizeStoredConfig({ previewLimit: 0 }).previewLimit).toBe(8);
    expect(normalizeStoredConfig({ previewLimit: "99" }).previewLimit).toBe(8);
  });
});

describe("validateDraft", () => {
  const valid = {
    baseUrl: "http://127.0.0.1:5123",
    token: "",
    confirmUntag: true,
  };

  it("rejects previewLimit 0", () => {
    const result = validateDraft({ ...valid, previewLimit: 0 });
    expect(result.ok).toBe(false);
  });

  it("accepts previewLimit 8", () => {
    const result = validateDraft({ ...valid, previewLimit: 8 });
    expect(result).toEqual({
      ok: true,
      value: {
        baseUrl: "http://127.0.0.1:5123",
        token: "",
        confirmUntag: true,
        previewLimit: 8,
      },
    });
  });
});
