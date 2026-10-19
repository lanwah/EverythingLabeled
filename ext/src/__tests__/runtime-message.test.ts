import { afterEach, describe, expect, it, vi } from "vitest";
import { sendToBackground } from "../runtime-message";

afterEach(() => {
  vi.unstubAllGlobals();
});

function stubChrome(sendMessage: () => Promise<unknown>): void {
  vi.stubGlobal("chrome", {
    runtime: {
      sendMessage,
      lastError: undefined,
    },
  });
}

describe("sendToBackground", () => {
  it("returns the service worker response when it has ok", async () => {
    stubChrome(async () => ({ ok: true, data: { items: [] }, message: null }));
    const result = await sendToBackground({ type: "listTags", q: "g" });
    expect(result).toEqual({ ok: true, data: { items: [] }, message: null });
  });

  it("treats a null response as failure instead of reading ok", async () => {
    stubChrome(async () => null);
    const result = await sendToBackground({ type: "listTags", q: "g" });
    expect(result.ok).toBe(false);
    if (result.ok) return;
    expect(result.code).toBe("internal_error");
    expect(result.message).toContain("重新加载");
  });

  it("turns a rejected sendMessage into a failure response", async () => {
    stubChrome(async () => {
      throw new Error("Extension context invalidated.");
    });
    const result = await sendToBackground({ type: "listTags", q: null });
    expect(result).toEqual({
      ok: false,
      code: "internal_error",
      message: "Extension context invalidated.",
    });
  });
});
