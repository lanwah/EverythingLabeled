import {
  canonicalizeBaseUrl,
  normalizeStoredConfig,
  validateDraft,
  type ElExtensionConfig,
} from "./config";
import { copy } from "./copy";
import { buildTagBody, createElApi, type ElResult } from "./el-api";
import { stripFragment } from "./uri";

export type ElRequest =
  | { type: "probe"; baseUrl: string; token: string }
  | { type: "getConfig" }
  | {
      type: "saveConfig";
      draft: {
        baseUrl: string;
        token: string;
        confirmUntag: boolean;
        previewLimit: unknown;
      };
    }
  | { type: "getTags"; uri: string }
  | {
      type: "tag";
      uri: string;
      alias: string;
      createMissing: boolean;
      resourceExists: boolean;
      title: string;
    }
  | { type: "untagById"; uri: string; tagId: string }
  | { type: "untagByAlias"; uri: string; alias: string }
  | { type: "suggest"; q: string }
  | { type: "preview"; q: string };

export type ElResponse<T = unknown> =
  | { ok: true; data: T; message: string | null }
  | { ok: false; code: string; message: string; httpStatus?: number };

type Api = ReturnType<typeof createElApi>;

type Deps = {
  api: Api;
  loadStored: () => Promise<Record<string, unknown> | undefined>;
  saveStored: (cfg: ElExtensionConfig) => Promise<void>;
};

function toElResponse<T>(result: ElResult<T>): ElResponse<T> {
  if (result.ok) return { ok: true, data: result.data, message: result.message };
  const { code, message, httpStatus } = result.error;
  return httpStatus === undefined
    ? { ok: false, code, message }
    : { ok: false, code, message, httpStatus };
}

function overlayFail(
  result: Extract<ElResult<unknown>, { ok: false }>,
  prefix: string,
): ElResponse {
  const mapped = toElResponse(result);
  if (mapped.ok) return mapped;
  const extra = result.error.message;
  return { ...mapped, message: extra ? `${prefix} ${extra}` : prefix };
}

async function storedGate(
  loadStored: Deps["loadStored"],
): Promise<
  { ok: true; cfg: ElExtensionConfig } | { ok: false; response: ElResponse }
> {
  const cfg = normalizeStoredConfig(await loadStored());
  if (cfg.token.trim() === "") {
    return {
      ok: false,
      response: { ok: false, code: "config_missing", message: copy.tokenMissing },
    };
  }
  const base = canonicalizeBaseUrl(cfg.baseUrl);
  if (!base.ok) {
    return {
      ok: false,
      response: { ok: false, code: "bad_request", message: base.message },
    };
  }
  return { ok: true, cfg: { ...cfg, baseUrl: base.value } };
}

const emptyOk: ElResponse = { ok: true, data: [], message: null };

async function handleProbe(req: Extract<ElRequest, { type: "probe" }>, api: Api): Promise<ElResponse> {
  const base = canonicalizeBaseUrl(req.baseUrl);
  if (!base.ok) {
    return { ok: false, code: "bad_request", message: base.message };
  }
  const health = await api.health(base.value);
  if (!health.ok) return overlayFail(health, copy.serviceUnreachable);
  if (req.token.trim() === "") {
    return {
      ok: true,
      data: { health: health.data.health, tokenChecked: false },
      message: health.message,
    };
  }
  const meta = await api.meta(base.value, req.token);
  if (!meta.ok) return overlayFail(meta, copy.tokenInvalid);
  return {
    ok: true,
    data: {
      health: health.data.health,
      dataVersion: meta.data.dataVersion,
      tokenChecked: true,
    },
    message: meta.message,
  };
}

export async function handleRequest(req: ElRequest, deps: Deps): Promise<ElResponse> {
  switch (req.type) {
    case "probe":
      return handleProbe(req, deps.api);
    case "getConfig":
      return {
        ok: true,
        data: normalizeStoredConfig(await deps.loadStored()),
        message: null,
      };
    case "saveConfig": {
      const parsed = validateDraft(req.draft);
      if (!parsed.ok) {
        return { ok: false, code: "bad_request", message: parsed.message };
      }
      await deps.saveStored(parsed.value);
      return { ok: true, data: parsed.value, message: null };
    }
    default:
      break;
  }

  const gate = await storedGate(deps.loadStored);
  if (!gate.ok) return gate.response;
  const { cfg } = gate;
  const { api } = deps;

  switch (req.type) {
    case "getTags":
      return toElResponse(await api.getTags(cfg.baseUrl, cfg.token, stripFragment(req.uri)));
    case "tag": {
      if (req.alias.trim() === "") {
        return { ok: false, code: "bad_request", message: "alias is required" };
      }
      return toElResponse(
        await api.tag(
          cfg.baseUrl,
          cfg.token,
          buildTagBody({
            uri: stripFragment(req.uri),
            alias: req.alias,
            createMissing: req.createMissing,
            resourceExists: req.resourceExists,
            title: req.title,
          }),
        ),
      );
    }
    case "untagById":
      return toElResponse(
        await api.untag(cfg.baseUrl, cfg.token, stripFragment(req.uri), [{ id: req.tagId }]),
      );
    case "untagByAlias":
      return toElResponse(
        await api.untag(cfg.baseUrl, cfg.token, stripFragment(req.uri), [req.alias]),
      );
    case "suggest": {
      if (req.q.trim() === "") return emptyOk;
      const result = await api.suggest(cfg.baseUrl, cfg.token, req.q);
      if (!result.ok) return emptyOk;
      return toElResponse(result);
    }
    case "preview": {
      if (req.q.trim() === "") return emptyOk;
      return toElResponse(
        await api.preview(cfg.baseUrl, cfg.token, req.q, cfg.previewLimit),
      );
    }
  }
}
