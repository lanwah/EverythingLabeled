import { afterEach, describe, expect, it, vi } from "vitest";
import { copy } from "../copy";
import type { ElExtensionConfig } from "../config";
import { DEFAULT_BASE_URL, DEFAULT_PREVIEW_LIMIT } from "../config";
import type { ElResult } from "../el-api";
import { debounce } from "../debounce";
import { handleRequest } from "../messages";

const GOOD_BASE = "http://127.0.0.1:5123";
const TOKEN = "tok";
const PAGE = "https://ex.com/a#frag";
const PAGE_STRIPPED = "https://ex.com/a";

function ok<T>(data: T, message: string | null = null): ElResult<T> {
  return { ok: true, data, message };
}

function err(
  code: string,
  message: string,
  httpStatus?: number,
): ElResult<never> {
  return { ok: false, error: { code, message, httpStatus } };
}

function apiStub() {
  return {
    health: vi.fn(),
    meta: vi.fn(),
    getTags: vi.fn(),
    tag: vi.fn(),
    untag: vi.fn(),
    suggest: vi.fn(),
    preview: vi.fn(),
    listTags: vi.fn(),
    renameTag: vi.fn(),
    deleteTag: vi.fn(),
  };
}

function deps(overrides?: {
  api?: ReturnType<typeof apiStub>;
  stored?: Record<string, unknown> | undefined;
  saveStored?: (cfg: ElExtensionConfig) => Promise<void>;
}) {
  const api = overrides?.api ?? apiStub();
  const loadStored = vi.fn(async () => overrides?.stored);
  const saveStored =
    overrides?.saveStored ?? vi.fn(async (_cfg: ElExtensionConfig) => {});
  return { api, loadStored, saveStored };
}

function configured(api?: ReturnType<typeof apiStub>) {
  return deps({
    api,
    stored: { baseUrl: GOOD_BASE, token: TOKEN, confirmUntag: true, previewLimit: 8 },
  });
}

describe("handleRequest probe", () => {
  it("rejects localhost without calling the API", async () => {
    const d = deps();
    const result = await handleRequest(
      { type: "probe", baseUrl: "http://localhost:5123", token: TOKEN },
      d,
    );
    expect(result).toEqual({
      ok: false,
      code: "bad_request",
      message: copy.invalidBaseUrl,
    });
    expect(d.api.health).not.toHaveBeenCalled();
    expect(d.api.meta).not.toHaveBeenCalled();
    expect(d.loadStored).not.toHaveBeenCalled();
  });

  it("calls only health when the form token is empty", async () => {
    const api = apiStub();
    api.health.mockResolvedValue(ok({ health: "ok", dataVersion: 3 }));
    const d = deps({ api, stored: { token: TOKEN } });
    const result = await handleRequest(
      { type: "probe", baseUrl: GOOD_BASE, token: "   " },
      d,
    );
    expect(result).toEqual({
      ok: true,
      data: { health: "ok", tokenChecked: false },
      message: null,
    });
    expect(api.health).toHaveBeenCalledOnce();
    expect(api.health).toHaveBeenCalledWith(GOOD_BASE);
    expect(api.meta).not.toHaveBeenCalled();
    expect(d.loadStored).not.toHaveBeenCalled();
  });

  it("calls health then meta when the form token is nonempty", async () => {
    const api = apiStub();
    api.health.mockResolvedValue(ok({ health: "ok", dataVersion: 1 }));
    api.meta.mockResolvedValue(ok({ dataVersion: 9 }));
    const d = deps({ api });
    const result = await handleRequest(
      { type: "probe", baseUrl: `${GOOD_BASE}/`, token: ` ${TOKEN} ` },
      d,
    );
    expect(result).toEqual({
      ok: true,
      data: { health: "ok", dataVersion: 9, tokenChecked: true },
      message: null,
    });
    expect(api.health).toHaveBeenCalledWith(GOOD_BASE);
    expect(api.meta).toHaveBeenCalledWith(GOOD_BASE, ` ${TOKEN} `);
  });

  it("maps health failure to unreachable copy without calling meta", async () => {
    const api = apiStub();
    api.health.mockResolvedValue(err("connection_failed", "down"));
    const result = await handleRequest(
      { type: "probe", baseUrl: GOOD_BASE, token: TOKEN },
      deps({ api }),
    );
    expect(result.ok).toBe(false);
    if (result.ok) return;
    expect(result.code).toBe("connection_failed");
    expect(result.message).toContain(copy.serviceUnreachable);
    expect(result.message).toContain("down");
    expect(api.meta).not.toHaveBeenCalled();
  });

  it("maps meta failure to token-invalid copy", async () => {
    const api = apiStub();
    api.health.mockResolvedValue(ok({ health: "ok", dataVersion: 1 }));
    api.meta.mockResolvedValue(err("unauthorized", "nope", 401));
    const result = await handleRequest(
      { type: "probe", baseUrl: GOOD_BASE, token: TOKEN },
      deps({ api }),
    );
    expect(result).toEqual({
      ok: false,
      code: "unauthorized",
      message: expect.stringContaining(copy.tokenInvalid),
      httpStatus: 401,
    });
    expect((result as { message: string }).message).toContain("nope");
  });
});

describe("handleRequest config", () => {
  it("returns normalizeStoredConfig for getConfig", async () => {
    const result = await handleRequest({ type: "getConfig" }, deps({ stored: undefined }));
    expect(result).toEqual({
      ok: true,
      data: {
        baseUrl: DEFAULT_BASE_URL,
        token: "",
        confirmUntag: true,
        previewLimit: DEFAULT_PREVIEW_LIMIT,
        findTagLogic: "and",
      },
      message: null,
    });
  });

  it("rejects an invalid saveConfig draft without saving", async () => {
    const d = deps();
    const result = await handleRequest(
      {
        type: "saveConfig",
        draft: {
          baseUrl: GOOD_BASE,
          token: TOKEN,
          confirmUntag: true,
          previewLimit: 0,
        },
      },
      d,
    );
    expect(result).toEqual({
      ok: false,
      code: "bad_request",
      message: copy.invalidPreviewLimit,
    });
    expect(d.saveStored).not.toHaveBeenCalled();
  });

  it("saves only the four-key config on a valid draft", async () => {
    const d = deps();
    const result = await handleRequest(
      {
        type: "saveConfig",
        draft: {
          baseUrl: `${GOOD_BASE}/`,
          token: TOKEN,
          confirmUntag: false,
          previewLimit: 16,
        },
      },
      d,
    );
    expect(result).toEqual({
      ok: true,
      data: {
        baseUrl: GOOD_BASE,
        token: TOKEN,
        confirmUntag: false,
        previewLimit: 16,
      },
      message: null,
    });
    expect(d.saveStored).toHaveBeenCalledOnce();
    expect(d.saveStored).toHaveBeenCalledWith({
      baseUrl: GOOD_BASE,
      token: TOKEN,
      confirmUntag: false,
      previewLimit: 16,
    });
  });
});

describe("handleRequest business gates", () => {
  it("returns config_missing without fetch when stored token is empty", async () => {
    const d = deps({ stored: { baseUrl: GOOD_BASE, token: "  " } });
    const result = await handleRequest({ type: "getTags", uri: PAGE }, d);
    expect(result).toEqual({
      ok: false,
      code: "config_missing",
      message: copy.tokenMissing,
    });
    expect(d.api.getTags).not.toHaveBeenCalled();
  });

  it("returns bad_request without fetch when stored baseUrl cannot canonicalize", async () => {
    const d = deps({
      stored: { baseUrl: "http://localhost:5123", token: TOKEN },
    });
    const result = await handleRequest({ type: "getTags", uri: PAGE }, d);
    expect(result).toEqual({
      ok: false,
      code: "bad_request",
      message: copy.invalidBaseUrl,
    });
    expect(d.api.getTags).not.toHaveBeenCalled();
  });
});

describe("handleRequest getTags tag untag", () => {
  it("strips the fragment before getTags", async () => {
    const api = apiStub();
    api.getTags.mockResolvedValue(ok({ items: [], resourceExists: false }));
    const d = configured(api);
    await handleRequest({ type: "getTags", uri: PAGE }, d);
    expect(api.getTags).toHaveBeenCalledWith(GOOD_BASE, TOKEN, PAGE_STRIPPED);
  });

  it("rejects an empty tag alias without calling the API", async () => {
    const d = configured();
    const result = await handleRequest(
      {
        type: "tag",
        uri: PAGE,
        alias: "  ",
        createMissing: true,
        resourceExists: false,
        title: "Hello",
      },
      d,
    );
    expect(result.ok).toBe(false);
    if (result.ok) return;
    expect(result.code).toBe("bad_request");
    expect(d.api.tag).not.toHaveBeenCalled();
  });

  it("tags with buildTagBody and stripped uri", async () => {
    const api = apiStub();
    api.tag.mockResolvedValue(ok({}));
    const d = configured(api);
    await handleRequest(
      {
        type: "tag",
        uri: PAGE,
        alias: "工作",
        createMissing: true,
        resourceExists: false,
        title: " Hello ",
      },
      d,
    );
    expect(api.tag).toHaveBeenCalledWith(GOOD_BASE, TOKEN, {
      uri: PAGE_STRIPPED,
      kind: "url",
      add: ["工作"],
      createMissing: true,
      displayName: "Hello",
    });
  });

  it("passes tag_not_found through", async () => {
    const api = apiStub();
    api.tag.mockResolvedValue(err("tag_not_found", "missing", 400));
    const result = await handleRequest(
      {
        type: "tag",
        uri: PAGE,
        alias: "工作",
        createMissing: false,
        resourceExists: true,
        title: "",
      },
      configured(api),
    );
    expect(result).toEqual({
      ok: false,
      code: "tag_not_found",
      message: "missing",
      httpStatus: 400,
    });
  });

  it("untags by id without confirm", async () => {
    const api = apiStub();
    api.untag.mockResolvedValue(ok({}));
    await handleRequest(
      { type: "untagById", uri: PAGE, tagId: "tid" },
      configured(api),
    );
    expect(api.untag).toHaveBeenCalledWith(GOOD_BASE, TOKEN, PAGE_STRIPPED, [
      { id: "tid" },
    ]);
  });

  it("untags by alias without confirm", async () => {
    const api = apiStub();
    api.untag.mockResolvedValue(ok({}));
    await handleRequest(
      { type: "untagByAlias", uri: PAGE, alias: "工作" },
      configured(api),
    );
    expect(api.untag).toHaveBeenCalledWith(GOOD_BASE, TOKEN, PAGE_STRIPPED, [
      "工作",
    ]);
  });
});

describe("handleRequest suggest and preview", () => {
  it("returns an empty suggest success without fetch when q is blank", async () => {
    const d = configured();
    const result = await handleRequest({ type: "suggest", q: "   " }, d);
    expect(result).toEqual({ ok: true, data: [], message: null });
    expect(d.api.suggest).not.toHaveBeenCalled();
  });

  it("passes nonempty suggest q as received", async () => {
    const api = apiStub();
    api.suggest.mockResolvedValue(ok([]));
    await handleRequest({ type: "suggest", q: "  foo" }, configured(api));
    expect(api.suggest).toHaveBeenCalledWith(GOOD_BASE, TOKEN, "  foo");
  });

  it("turns suggest API failure into an empty success", async () => {
    const api = apiStub();
    api.suggest.mockResolvedValue(err("internal_error", "boom", 500));
    const result = await handleRequest({ type: "suggest", q: "foo" }, configured(api));
    expect(result).toEqual({ ok: true, data: [], message: null });
  });

  it("returns empty preview without fetch when q is blank", async () => {
    const d = configured();
    const result = await handleRequest({ type: "preview", q: "" }, d);
    expect(result).toEqual({ ok: true, data: [], message: null });
    expect(d.api.preview).not.toHaveBeenCalled();
  });

  it("previews with stored previewLimit and passes API errors through", async () => {
    const api = apiStub();
    api.preview.mockResolvedValue(err("bad_request", "syntax", 400));
    const d = deps({
      api,
      stored: { baseUrl: GOOD_BASE, token: TOKEN, previewLimit: 16 },
    });
    const result = await handleRequest({ type: "preview", q: "tag:a" }, d);
    expect(api.preview).toHaveBeenCalledWith(GOOD_BASE, TOKEN, "tag:a", 16);
    expect(result).toEqual({
      ok: false,
      code: "bad_request",
      message: "syntax",
      httpStatus: 400,
    });
  });
});

describe("handleRequest listTags", () => {
  it("passes trimmed q and default paging", async () => {
    const api = apiStub();
    api.listTags.mockResolvedValue(ok({ items: [], total: 0 }));
    await handleRequest({ type: "listTags", q: "  g" }, configured(api));
    expect(api.listTags).toHaveBeenCalledWith(GOOD_BASE, TOKEN, "g", 50, 0);
  });

  it("sends null q when blank so the service returns the full page", async () => {
    const api = apiStub();
    api.listTags.mockResolvedValue(ok({ items: [], total: 0 }));
    await handleRequest({ type: "listTags", q: "   " }, configured(api));
    expect(api.listTags).toHaveBeenCalledWith(GOOD_BASE, TOKEN, null, 50, 0);
  });

  it("never returns undefined for an unknown type", async () => {
    const result = await handleRequest(
      { type: "notARealType" } as never,
      configured(),
    );
    expect(result).toEqual({
      ok: false,
      code: "bad_request",
      message: "unknown request: notARealType",
    });
  });
});

describe("debounce", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("invokes only the last call after the wait", () => {
    vi.useFakeTimers();
    const fn = vi.fn();
    const delayed = debounce(fn, 250);
    delayed("a");
    delayed("b");
    vi.advanceTimersByTime(249);
    expect(fn).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(fn).toHaveBeenCalledOnce();
    expect(fn).toHaveBeenCalledWith("b");
  });
});
