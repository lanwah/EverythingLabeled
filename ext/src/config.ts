import { copy } from "./copy";

export const DEFAULT_BASE_URL = "http://127.0.0.1:5123";
export const DEFAULT_PREVIEW_LIMIT = 8;
export const MIN_PREVIEW_LIMIT = 1;
export const MAX_PREVIEW_LIMIT = 16;

export type FindTagLogic = "and" | "or";

/** Popup / 打标页预览列表过滤：默认仅链接。 */
export type PreviewResourceType = "links" | "all";

export type ElExtensionConfig = {
  baseUrl: string;
  token: string;
  confirmUntag: boolean;
  previewLimit: number;
  findTagLogic: FindTagLogic;
  previewResourceType: PreviewResourceType;
};

export type ConfigParse =
  | { ok: true; value: ElExtensionConfig }
  | { ok: false; message: string };

function failUrl(): { ok: false; message: string } {
  return { ok: false, message: copy.invalidBaseUrl };
}

export function canonicalizeBaseUrl(
  raw: string,
): { ok: true; value: string } | { ok: false; message: string } {
  let url: URL;
  try {
    url = new URL(raw.trim());
  } catch {
    return failUrl();
  }

  if (url.protocol !== "http:") return failUrl();
  if (url.hostname !== "127.0.0.1") return failUrl();
  if (url.username !== "" || url.password !== "") return failUrl();
  if (url.pathname !== "" && url.pathname !== "/") return failUrl();
  if (url.search !== "" || url.hash !== "") return failUrl();

  const port = url.port;
  if (port === "" || port === "80") {
    return { ok: true, value: "http://127.0.0.1" };
  }
  return { ok: true, value: `http://127.0.0.1:${port}` };
}

function parseIntegerInRange(raw: unknown): number | undefined {
  let n: number | undefined;
  if (typeof raw === "number") {
    n = raw;
  } else if (typeof raw === "string" && raw.trim() !== "") {
    n = Number(raw.trim());
  }
  if (n === undefined || !Number.isInteger(n)) return undefined;
  if (n < MIN_PREVIEW_LIMIT || n > MAX_PREVIEW_LIMIT) return undefined;
  return n;
}

export function parsePreviewLimit(raw: unknown): number {
  return parseIntegerInRange(raw) ?? DEFAULT_PREVIEW_LIMIT;
}

export function parseFindTagLogic(raw: unknown): FindTagLogic {
  if (typeof raw !== "string") return "and";
  const v = raw.trim().toLowerCase();
  return v === "or" ? "or" : "and";
}

export function parsePreviewResourceType(raw: unknown): PreviewResourceType {
  if (typeof raw !== "string") return "links";
  const v = raw.trim().toLowerCase();
  return v === "all" ? "all" : "links";
}

/** Client-side filter after preview API; `links` keeps only `kind === "url"`. */
export function filterPreviewByResourceType<T extends { kind: string }>(
  hits: readonly T[],
  type: PreviewResourceType,
): T[] {
  if (type === "all") return [...hits];
  return hits.filter((hit) => hit.kind === "url");
}

export function normalizeStoredConfig(
  stored: Record<string, unknown> | undefined,
): ElExtensionConfig {
  const baseUrl =
    typeof stored?.baseUrl === "string" ? stored.baseUrl : DEFAULT_BASE_URL;
  const token = typeof stored?.token === "string" ? stored.token : "";
  const confirmUntag = stored?.confirmUntag === false ? false : true;
  return {
    baseUrl,
    token,
    confirmUntag,
    previewLimit: parsePreviewLimit(stored?.previewLimit),
    findTagLogic: parseFindTagLogic(stored?.findTagLogic),
    previewResourceType: parsePreviewResourceType(stored?.previewResourceType),
  };
}

export function validateDraft(draft: {
  baseUrl: string;
  token: string;
  confirmUntag: boolean;
  previewLimit: unknown;
  previewResourceType?: unknown;
}): ConfigParse {
  const base = canonicalizeBaseUrl(draft.baseUrl);
  if (!base.ok) return base;
  const previewLimit = parseIntegerInRange(draft.previewLimit);
  if (previewLimit === undefined) {
    return { ok: false, message: copy.invalidPreviewLimit };
  }
  return {
    ok: true,
    value: {
      baseUrl: base.value,
      token: draft.token,
      confirmUntag: draft.confirmUntag,
      previewLimit,
      findTagLogic: "and",
      previewResourceType: parsePreviewResourceType(draft.previewResourceType),
    },
  };
}
