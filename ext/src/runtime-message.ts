import { copy } from "./copy";
import type { ElResponse } from "./messages";

function noResponse(message: string | undefined): ElResponse {
  return {
    ok: false,
    code: "internal_error",
    message: message == null || message === "" ? copy.backgroundNoResponse : message,
  };
}

function isElResponse(value: unknown): value is ElResponse {
  return (
    typeof value === "object" &&
    value !== null &&
    "ok" in value &&
    typeof (value as { ok: unknown }).ok === "boolean"
  );
}

/** Service worker 无响应时 Chrome 会给出 null（不是 undefined）。 */
export async function sendToBackground<T>(payload: unknown): Promise<ElResponse<T>> {
  try {
    const result = await chrome.runtime.sendMessage(payload);
    if (!isElResponse(result)) {
      return noResponse(chrome.runtime.lastError?.message);
    }
    return result as ElResponse<T>;
  } catch (err) {
    const message = err instanceof Error ? err.message : undefined;
    return noResponse(message ?? chrome.runtime.lastError?.message);
  }
}
