import { copy } from "./copy";
import type { ElExtensionConfig } from "./config";
import type { ElResponse } from "./messages";
import { formatUiError } from "./ui-error";

const root = document.getElementById("app")!;
root.replaceChildren();

const status = document.createElement("p");
status.className = "status";

const baseUrl = document.createElement("input");
baseUrl.type = "text";
baseUrl.autocomplete = "off";

const token = document.createElement("input");
token.type = "password";
token.autocomplete = "off";

const confirmUntag = document.createElement("input");
confirmUntag.type = "checkbox";

const previewLimit = document.createElement("input");
previewLimit.type = "number";
previewLimit.min = "1";
previewLimit.max = "16";
previewLimit.step = "1";

function labeled(text: string, control: HTMLElement): HTMLLabelElement {
  const label = document.createElement("label");
  label.append(text, control);
  return label;
}

const form = document.createElement("form");
form.append(
  labeled("服务地址", baseUrl),
  labeled("Token", token),
  labeled("去标前确认", confirmUntag),
  labeled("预览条数", previewLimit),
);

const saveBtn = document.createElement("button");
saveBtn.type = "submit";
saveBtn.textContent = "保存";

const probeBtn = document.createElement("button");
probeBtn.type = "button";
probeBtn.textContent = "探测连接";

const actions = document.createElement("div");
actions.className = "actions";
actions.append(saveBtn, probeBtn);

form.append(actions);
root.append(form, status);

async function send<T>(payload: unknown): Promise<ElResponse<T>> {
  const result = (await chrome.runtime.sendMessage(payload)) as ElResponse<T> | undefined;
  if (result === undefined) {
    return {
      ok: false,
      code: "internal_error",
      message: chrome.runtime.lastError?.message ?? "no response",
    };
  }
  return result;
}

function showOk(text: string): void {
  status.className = "status ok";
  status.textContent = text;
}

function showErr(text: string): void {
  status.className = "status err";
  status.textContent = text;
}

function probeFailText(err: { code: string; message: string; httpStatus?: number }): string {
  const mapped = formatUiError(err);
  if (err.message === mapped || err.message.includes(mapped)) return err.message;
  return err.message ? `${mapped} ${err.message}` : mapped;
}

function fill(cfg: ElExtensionConfig): void {
  baseUrl.value = cfg.baseUrl;
  token.value = cfg.token;
  confirmUntag.checked = cfg.confirmUntag;
  previewLimit.value = String(cfg.previewLimit);
}

async function load(): Promise<void> {
  const result = await send<ElExtensionConfig>({ type: "getConfig" });
  if (!result.ok) {
    showErr(formatUiError(result));
    return;
  }
  fill(result.data);
}

form.addEventListener("submit", (ev) => {
  ev.preventDefault();
  void (async () => {
    const result = await send({
      type: "saveConfig",
      draft: {
        baseUrl: baseUrl.value,
        token: token.value,
        confirmUntag: confirmUntag.checked,
        previewLimit: previewLimit.value,
      },
    });
    if (!result.ok) {
      showErr(formatUiError(result));
      return;
    }
    showOk(copy.saved);
  })();
});

probeBtn.addEventListener("click", () => {
  void (async () => {
    const result = await send<{ tokenChecked?: boolean }>({
      type: "probe",
      baseUrl: baseUrl.value,
      token: token.value,
    });
    if (!result.ok) {
      showErr(probeFailText(result));
      return;
    }
    showOk(result.data.tokenChecked ? copy.probeTokenOk : copy.probeHealthOk);
  })();
});

void load();
