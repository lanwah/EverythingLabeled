export type ElError = { code: string; message: string; httpStatus?: number };
export type ElResult<T> =
  | { ok: true; data: T; message: string | null }
  | { ok: false; error: ElError };

export type TagInfo = {
  id: string;
  name: string;
  namespace: string | null;
  aliases: string[];
};

export type QueryHit = { id: string; uri: string; kind: string; displayName: string | null };

export type SuggestItem = {
  tagId: string;
  alias: string;
  name: string;
  namespace: string | null;
};

export type PageTags = { items: TagInfo[]; resourceExists: boolean };

const REQUEST_TIMEOUT_MS = 30_000;

function connectionFailed<T>(): ElResult<T> {
  return {
    ok: false,
    error: { code: "connection_failed", message: "Unable to reach the service." },
  };
}

function joinUrl(baseUrl: string, relative: string): string {
  return new URL(relative, `${baseUrl}/`).href;
}

function parseEnvelope<T>(payload: string, httpStatus: number): ElResult<T> {
  let envelope: { status?: unknown; message?: unknown; data?: unknown } | undefined;
  try {
    envelope = JSON.parse(payload) as typeof envelope;
  } catch {
    envelope = undefined;
  }

  const httpOk = httpStatus >= 200 && httpStatus <= 299;
  if (envelope?.status === "ok" && httpOk) {
    return {
      ok: true,
      data: envelope.data as T,
      message: typeof envelope.message === "string" ? envelope.message : null,
    };
  }

  let code: string | undefined;
  const data = envelope?.data;
  if (data !== null && typeof data === "object" && "code" in data) {
    const raw = (data as { code: unknown }).code;
    if (typeof raw === "string") code = raw;
  }
  code ??= httpStatus >= 500 ? "internal_error" : "bad_request";
  const message =
    typeof envelope?.message === "string" ? envelope.message : String(httpStatus);
  return { ok: false, error: { code, message, httpStatus } };
}

async function request<T>(
  fetchFn: typeof fetch,
  url: string,
  init: RequestInit,
): Promise<ElResult<T>> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
  try {
    const response = await fetchFn(url, { ...init, signal: controller.signal });
    const payload = await response.text();
    return parseEnvelope<T>(payload, response.status);
  } catch {
    return connectionFailed();
  } finally {
    clearTimeout(timer);
  }
}

function headers(token?: string, jsonBody = false): Headers {
  const h = new Headers();
  h.set("Accept", "application/json");
  if (token !== undefined) h.set("Authorization", `Bearer ${token}`);
  if (jsonBody) h.set("Content-Type", "application/json");
  return h;
}

export function buildTagBody(input: {
  uri: string;
  alias: string;
  createMissing: boolean;
  resourceExists: boolean;
  title: string;
}): Record<string, unknown> {
  const body: Record<string, unknown> = {
    uri: input.uri,
    kind: "url",
    add: [input.alias],
    createMissing: input.createMissing,
  };
  const title = input.title.trim();
  if (!input.resourceExists && title !== "") body.displayName = title;
  return body;
}

export function createElApi(deps: { fetch: typeof fetch }): {
  health(baseUrl: string): Promise<ElResult<{ health: string; dataVersion: number }>>;
  meta(baseUrl: string, token: string): Promise<ElResult<{ dataVersion: number }>>;
  getTags(baseUrl: string, token: string, uri: string): Promise<ElResult<PageTags>>;
  tag(baseUrl: string, token: string, body: Record<string, unknown>): Promise<ElResult<unknown>>;
  untag(
    baseUrl: string,
    token: string,
    uri: string,
    remove: Array<string | { id: string }>,
  ): Promise<ElResult<unknown>>;
  suggest(baseUrl: string, token: string, q: string): Promise<ElResult<SuggestItem[]>>;
  preview(baseUrl: string, token: string, q: string, limit: number): Promise<ElResult<QueryHit[]>>;
} {
  const send = <T>(relative: string, baseUrl: string, init: RequestInit) =>
    request<T>(deps.fetch, joinUrl(baseUrl, relative), init);

  const authGet = <T>(relative: string, baseUrl: string, token: string) =>
    send<T>(relative, baseUrl, { method: "GET", headers: headers(token) });

  const authPost = <T>(relative: string, baseUrl: string, token: string, body: unknown) =>
    send<T>(relative, baseUrl, {
      method: "POST",
      headers: headers(token, true),
      body: JSON.stringify(body),
    });

  async function unwrapItems<T>(
    result: ElResult<{ items?: T[] }>,
  ): Promise<ElResult<T[]>> {
    if (!result.ok) return result;
    return { ok: true, data: result.data.items ?? [], message: result.message };
  }

  return {
    health(baseUrl) {
      return send("v1/health", baseUrl, { method: "GET", headers: headers() });
    },
    meta(baseUrl, token) {
      return authGet("v1/meta", baseUrl, token);
    },
    async getTags(baseUrl, token, uri) {
      const items: TagInfo[] = [];
      let offset = 0;
      let message: string | null = null;
      while (true) {
        const relative = `v1/resources/tags?uri=${encodeURIComponent(uri)}&offset=${offset}&limit=1024`;
        const page = await authGet<{ items?: TagInfo[]; total?: number }>(
          relative,
          baseUrl,
          token,
        );
        if (!page.ok) {
          if (page.error.code === "resource_not_found") {
            return { ok: true, data: { items: [], resourceExists: false }, message: null };
          }
          return page;
        }
        message = page.message;
        const pageItems = page.data.items ?? [];
        const total = page.data.total ?? 0;
        items.push(...pageItems);
        offset += pageItems.length;
        if (pageItems.length === 0 || offset >= total) break;
      }
      return { ok: true, data: { items, resourceExists: true }, message };
    },
    tag(baseUrl, token, body) {
      return authPost("v1/resources/tags", baseUrl, token, body);
    },
    untag(baseUrl, token, uri, remove) {
      return authPost("v1/resources/tags", baseUrl, token, {
        uri,
        remove,
        createMissing: false,
      });
    },
    async suggest(baseUrl, token, q) {
      return unwrapItems(
        await authGet<{ items?: SuggestItem[] }>(
          `v1/tags/suggest?q=${encodeURIComponent(q)}&limit=10`,
          baseUrl,
          token,
        ),
      );
    },
    async preview(baseUrl, token, q, limit) {
      return unwrapItems(
        await authGet<{ items?: QueryHit[] }>(
          `v1/query/preview?q=${encodeURIComponent(q)}&limit=${limit}`,
          baseUrl,
          token,
        ),
      );
    },
  };
}
