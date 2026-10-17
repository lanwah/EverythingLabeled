import { copy } from "./copy";
import { DEFAULT_BASE_URL, type ElExtensionConfig } from "./config";
import type { ElResponse } from "./messages";
import { formatUiError } from "./ui-error";

const root = document.getElementById("app")!;
root.replaceChildren();
root.className = "ext-shell";

const head = document.createElement("header");
head.className = "ext-head";
const heading = document.createElement("div");
const title = document.createElement("h1");
title.textContent = "设置";
const lead = document.createElement("p");
lead.className = "ext-lead";
lead.textContent = "连接本机标签服务。地址和 Token 在这里手工填写。";
heading.append(title, lead);
head.append(heading);

const status = document.createElement("span");
status.className = "status";

const baseUrl = document.createElement("input");
baseUrl.type = "text";
baseUrl.autocomplete = "off";
baseUrl.spellcheck = false;
baseUrl.placeholder = DEFAULT_BASE_URL;

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

function field(text: string, control: HTMLElement): HTMLLabelElement {
  const label = document.createElement("label");
  label.className = "field";
  const name = document.createElement("span");
  name.textContent = text;
  label.append(name, control);
  return label;
}

const confirmLabel = document.createElement("label");
confirmLabel.className = "check-row";
const confirmText = document.createElement("span");
confirmText.textContent = "去标前确认";
confirmLabel.append(confirmUntag, confirmText);

const form = document.createElement("form");
form.className = "options-form";
form.append(
  field("服务地址", baseUrl),
  field("Token", token),
  confirmLabel,
  field("预览条数", previewLimit),
);

const saveBtn = document.createElement("button");
saveBtn.type = "submit";
saveBtn.className = "btn btn-primary";
saveBtn.textContent = "保存";

const probeBtn = document.createElement("button");
probeBtn.type = "button";
probeBtn.className = "btn";
probeBtn.textContent = "探测连接";

const actions = document.createElement("div");
actions.className = "actions-right";
actions.append(probeBtn, saveBtn);

const footer = document.createElement("div");
footer.className = "footer";
footer.append(status, actions);

form.append(footer);
root.append(head, form);

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
