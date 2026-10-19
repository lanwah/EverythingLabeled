import { afterEach, describe, expect, it, vi } from "vitest";
import { buildTagBody, createElApi } from "../el-api";

const BASE = "http://127.0.0.1:5123";
const TOKEN = "tok";
const URI = "https://ex.com/a b";

type Recorded = { url: string; init: RequestInit };

function header(init: RequestInit, name: string): string | null {
  return new Headers(init.headers).get(name);
}

function envelope(
  status: number,
  envelopeStatus: string,
  message: string | null,
  data: unknown,
): Response {
  return new Response(
    JSON.stringify({ status: envelopeStatus, message, data }),
    { status, headers: { "Content-Type": "application/json" } },
  );
}

function ok(data: unknown, message: string | null = null): Response {
  return envelope(200, "ok", message, data);
}

function scriptedFetch(
  script: (url: string, init: RequestInit, i: number) => Response | Promise<Response>,
): { fetch: typeof fetch; calls: Recorded[] } {
  const calls: Recorded[] = [];
  const fetchFn: typeof fetch = async (input, init = {}) => {
    const url =
      typeof input === "string"
        ? input
        : input instanceof URL
          ? input.href
          : input.url;
    const i = calls.length;
    calls.push({ url, init });
    return script(url, init, i);
  };
  return { fetch: fetchFn, calls };
}

describe("buildTagBody", () => {
  it("omits displayName when the resource already exists", () => {
    const body = buildTagBody({
      uri: URI,
      alias: "工作",
      createMissing: true,
      resourceExists: true,
      title: "Hello",
    });
    expect(body).toEqual({
      uri: URI,
      kind: "url",
      add: ["工作"],
      createMissing: true,
    });
    expect("displayName" in body).toBe(false);
  });

  it("adds trimmed displayName when the resource is missing and title is non-empty", () => {
    expect(
      buildTagBody({
        uri: URI,
        alias: "工作",
        createMissing: false,
        resourceExists: false,
        title: "  Hello  ",
      }),
    ).toEqual({
      uri: URI,
      kind: "url",
      add: ["工作"],
      createMissing: false,
      displayName: "Hello",
    });
  });

  it("omits displayName when title is blank even if the resource is missing", () => {
    const body = buildTagBody({
      uri: URI,
      alias: "工作",
      createMissing: true,
      resourceExists: false,
      title: "   ",
    });
    expect("displayName" in body).toBe(false);
  });
});

describe("createElApi", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("GETs health without Authorization", async () => {
    const { fetch, calls } = scriptedFetch(() =>
      ok({ health: "ok", dataVersion: 4 }),
    );
    const api = createElApi({ fetch });
    const result = await api.health(BASE);

    expect(calls).toHaveLength(1);
    expect(calls[0]!.url).toBe(`${BASE}/v1/health`);
    expect(header(calls[0]!.init, "Authorization")).toBeNull();
    expect(header(calls[0]!.init, "Accept")).toBe("application/json");
    expect(calls[0]!.init.method ?? "GET").toBe("GET");
    expect(result).toEqual({
      ok: true,
      data: { health: "ok", dataVersion: 4 },
      message: null,
    });
  });

  it("GETs meta with Bearer token", async () => {
    const { fetch, calls } = scriptedFetch(() => ok({ dataVersion: 9 }));
    const api = createElApi({ fetch });
    const result = await api.meta(BASE, TOKEN);

    expect(calls[0]!.url).toBe(`${BASE}/v1/meta`);
    expect(header(calls[0]!.init, "Authorization")).toBe(`Bearer ${TOKEN}`);
    expect(header(calls[0]!.init, "Accept")).toBe("application/json");
    expect(result).toEqual({
      ok: true,
      data: { dataVersion: 9 },
      message: null,
    });
  });

  it("maps tags resource_not_found to empty items and resourceExists false", async () => {
    const { fetch, calls } = scriptedFetch(() =>
      envelope(404, "error", "missing", { code: "resource_not_found" }),
    );
    const api = createElApi({ fetch });
    const result = await api.getTags(BASE, TOKEN, URI);

    expect(calls[0]!.url).toBe(
      `${BASE}/v1/resources/tags?uri=${encodeURIComponent(URI)}&offset=0&limit=1024`,
    );
    expect(header(calls[0]!.init, "Authorization")).toBe(`Bearer ${TOKEN}`);
    expect(result).toEqual({
      ok: true,
      data: { items: [], resourceExists: false },
      message: null,
    });
  });

  it("concatenates two getTags pages while offset < total", async () => {
    const tagA = {
      id: "a",
      name: "a",
      namespace: null,
      aliases: ["a"],
    };
    const tagB = {
      id: "b",
      name: "b",
      namespace: "ns",
      aliases: ["b"],
    };
    const { fetch, calls } = scriptedFetch((_url, _init, i) =>
      i === 0
        ? ok({ items: [tagA], offset: 0, limit: 1024, total: 2 })
        : ok({ items: [tagB], offset: 1, limit: 1024, total: 2 }),
    );
    const api = createElApi({ fetch });
    const result = await api.getTags(BASE, TOKEN, URI);

    expect(calls.map((c) => c.url)).toEqual([
      `${BASE}/v1/resources/tags?uri=${encodeURIComponent(URI)}&offset=0&limit=1024`,
      `${BASE}/v1/resources/tags?uri=${encodeURIComponent(URI)}&offset=1&limit=1024`,
    ]);
    expect(result).toEqual({
      ok: true,
      data: { items: [tagA, tagB], resourceExists: true },
      message: null,
    });
  });

  it("fails the whole getTags call on a non-not-found page error", async () => {
    const tagA = {
      id: "a",
      name: "a",
      namespace: null,
      aliases: ["a"],
    };
    const { fetch } = scriptedFetch((_url, _init, i) =>
      i === 0
        ? ok({ items: [tagA], offset: 0, limit: 1024, total: 2 })
        : envelope(401, "error", "Nope.", { code: "unauthorized" }),
    );
    const api = createElApi({ fetch });
    const result = await api.getTags(BASE, TOKEN, URI);

    expect(result).toEqual({
      ok: false,
      error: { code: "unauthorized", message: "Nope.", httpStatus: 401 },
    });
  });

  it("POSTs untag body with createMissing false and no kind", async () => {
    const { fetch, calls } = scriptedFetch(() => ok({ dataVersion: 1 }));
    const api = createElApi({ fetch });
    const remove = ["工作", { id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" }];
    await api.untag(BASE, TOKEN, URI, remove);

    expect(calls[0]!.url).toBe(`${BASE}/v1/resources/tags`);
    expect(calls[0]!.init.method).toBe("POST");
    expect(header(calls[0]!.init, "Content-Type")).toBe("application/json");
    const body = JSON.parse(String(calls[0]!.init.body)) as Record<string, unknown>;
    expect(body).toEqual({
      uri: URI,
      remove,
      createMissing: false,
    });
    expect("kind" in body).toBe(false);
  });

  it("maps fetch reject to connection_failed", async () => {
    const fetchFn: typeof fetch = async () => {
      throw new TypeError("Failed to fetch");
    };
    const api = createElApi({ fetch: fetchFn });
    const result = await api.health(BASE);
    expect(result).toEqual({
      ok: false,
      error: {
        code: "connection_failed",
        message: "Unable to reach the service.",
      },
    });
  });

  it("maps abort timeout to connection_failed and clears the timer", async () => {
    vi.useFakeTimers();
    const fetchFn: typeof fetch = (_input, init) =>
      new Promise((_resolve, reject) => {
        init?.signal?.addEventListener("abort", () => {
          reject(new DOMException("Aborted", "AbortError"));
        });
      });
    const api = createElApi({ fetch: fetchFn });
    const pending = api.health(BASE);
    await vi.advanceTimersByTimeAsync(30_000);
    await expect(pending).resolves.toEqual({
      ok: false,
      error: {
        code: "connection_failed",
        message: "Unable to reach the service.",
      },
    });
  });

  it("returns suggest data.items and defaults missing items to []", async () => {
    const item = {
      tagId: "t1",
      alias: "工",
      name: "工作",
      namespace: null,
    };
    const { fetch, calls } = scriptedFetch((url) =>
      url.includes("q=wo")
        ? ok({ items: [item], offset: 0, limit: 10, total: 1 })
        : ok({ offset: 0, limit: 10, total: 0 }),
    );
    const api = createElApi({ fetch });
    const withItems = await api.suggest(BASE, TOKEN, "wo");
    const empty = await api.suggest(BASE, TOKEN, "zz");

    expect(calls[0]!.url).toBe(
      `${BASE}/v1/tags/suggest?q=${encodeURIComponent("wo")}&limit=10`,
    );
    expect(withItems).toEqual({ ok: true, data: [item], message: null });
    expect(empty).toEqual({ ok: true, data: [], message: null });
  });

  it("returns preview data.items for the given limit", async () => {
    const hit = {
      id: "r1",
      uri: "https://ex.com/a",
      kind: "url",
      displayName: "A",
    };
    const { fetch, calls } = scriptedFetch(() =>
      ok({ items: [hit], offset: 0, limit: 8 }),
    );
    const api = createElApi({ fetch });
    const result = await api.preview(BASE, TOKEN, "foo", 8);

    expect(calls[0]!.url).toBe(
      `${BASE}/v1/query/preview?q=${encodeURIComponent("foo")}&limit=8`,
    );
    expect(result).toEqual({ ok: true, data: [hit], message: null });
  });

  it("lists tags with q and maps items/total", async () => {
    const tag = {
      id: "t1",
      name: "工具",
      namespace: null,
      aliases: ["工具"],
    };
    const { fetch, calls } = scriptedFetch(() =>
      ok({ items: [tag], offset: 0, limit: 50, total: 1 }),
    );
    const api = createElApi({ fetch });
    const result = await api.listTags(BASE, TOKEN, "g", 50, 0);

    expect(calls[0]!.url).toBe(`${BASE}/v1/tags?limit=50&offset=0&q=g`);
    expect(result).toEqual({
      ok: true,
      data: { items: [tag], total: 1 },
      message: null,
    });
  });

  it("omits q when listing without a filter", async () => {
    const { fetch, calls } = scriptedFetch(() =>
      ok({ items: [], offset: 0, limit: 50, total: 0 }),
    );
    const api = createElApi({ fetch });
    await api.listTags(BASE, TOKEN, null, 50, 0);
    expect(calls[0]!.url).toBe(`${BASE}/v1/tags?limit=50&offset=0`);
  });

  it("POSTs tag JSON body with Content-Type", async () => {
    const body = buildTagBody({
      uri: URI,
      alias: "工作",
      createMissing: true,
      resourceExists: false,
      title: "Hello",
    });
    const { fetch, calls } = scriptedFetch(() => ok({ dataVersion: 2 }));
    const api = createElApi({ fetch });
    await api.tag(BASE, TOKEN, body);

    expect(calls[0]!.url).toBe(`${BASE}/v1/resources/tags`);
    expect(calls[0]!.init.method).toBe("POST");
    expect(header(calls[0]!.init, "Content-Type")).toBe("application/json");
    expect(JSON.parse(String(calls[0]!.init.body))).toEqual(body);
  });
});
