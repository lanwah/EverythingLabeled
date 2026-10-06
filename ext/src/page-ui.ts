import { copy } from "./copy";
import type { ElExtensionConfig } from "./config";
import { debounce } from "./debounce";
import type { QueryHit, SuggestItem, TagInfo } from "./el-api";
import { tagChipLabel } from "./labels";
import type { ElResponse } from "./messages";
import { decideOpen, defaultSelectedIds } from "./open-rules";
import { isHttpPage, isOpenableUrl } from "./uri";
import { formatUiError } from "./ui-error";

export type PageUiMode = "popup" | "app";

type PageTags = { items: TagInfo[]; resourceExists: boolean };

type PageSnapshot = {
  tabId?: number;
  url?: string;
  title?: string;
};

const SUGGEST_MS = 250;
const TRUNCATE = 80;

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

function truncate(text: string): string {
  return text.length <= TRUNCATE ? text : `${text.slice(0, TRUNCATE - 1)}…`;
}

function el<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  className?: string,
): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (className) node.className = className;
  return node;
}

export function mountPageUi(root: HTMLElement, mode: PageUiMode): void {
  root.replaceChildren();

  const banner = el("p", "banner");
  banner.hidden = true;
  const pageMeta = el("p", "page-meta");
  const pageHint = el("p", "hint");
  const chips = el("ul", "chips");
  const aliasInput = document.createElement("input");
  aliasInput.type = "text";
  aliasInput.autocomplete = "off";
  const suggestList = el("ul", "suggest");
  const tagBtn = document.createElement("button");
  tagBtn.type = "button";
  tagBtn.textContent = "打标";
  const untagBtn = document.createElement("button");
  untagBtn.type = "button";
  untagBtn.textContent = "去标";
  const createMissing = document.createElement("input");
  createMissing.type = "checkbox";
  createMissing.checked = false;
  const createLabel = document.createElement("label");
  createLabel.className = "inline";
  createLabel.append(createMissing, "创建缺失标签");
  const qInput = document.createElement("input");
  qInput.type = "text";
  qInput.autocomplete = "off";
  const previewBtn = document.createElement("button");
  previewBtn.type = "button";
  previewBtn.textContent = "预览";
  const hitsList = el("ul", "hits");
  const previewHint = el("p", "hint");
  const openBtn = document.createElement("button");
  openBtn.type = "button";
  openBtn.textContent = "打开所选";
  const status = el("p", "status");
  const followBtn = document.createElement("button");
  followBtn.type = "button";
  followBtn.textContent = "跟随浏览器当前网页";

  const tagRow = el("div", "row");
  tagRow.append(aliasInput, tagBtn, untagBtn, createLabel);
  const queryRow = el("div", "row");
  queryRow.append(qInput, previewBtn, openBtn);

  root.append(banner, pageMeta, pageHint);
  if (mode === "app") root.append(followBtn);
  root.append(chips, tagRow, suggestList, queryRow, previewHint, hitsList, status);

  let config: ElExtensionConfig | undefined;
  let pageUrl = "";
  let pageTitle = "";
  let resourceExists = false;
  let hits: QueryHit[] = [];
  let selected = new Set<string>();
  let tagActionsEnabled = false;

  function setStatus(text: string, kind: "ok" | "err" | "" = ""): void {
    status.className = kind ? `status ${kind}` : "status";
    status.textContent = text;
  }

  function showConfigMissingBanner(): void {
    banner.replaceChildren();
    banner.append(copy.tokenMissing, " ");
    const link = document.createElement("button");
    link.type = "button";
    link.className = "link";
    link.textContent = "打开设置";
    link.addEventListener("click", () => {
      chrome.runtime.openOptionsPage();
    });
    banner.append(link);
    banner.hidden = false;
  }

  function showFailure(err: { code: string; message: string; httpStatus?: number }): void {
    if (err.code === "config_missing") showConfigMissingBanner();
    setStatus(formatUiError(err), "err");
  }

  function setTagEnabled(enabled: boolean): void {
    tagActionsEnabled = enabled;
    aliasInput.disabled = !enabled;
    tagBtn.disabled = !enabled;
    untagBtn.disabled = !enabled;
    createMissing.disabled = !enabled;
  }

  function renderPage(): void {
    const title = truncate(pageTitle || "(无标题)");
    const uri = truncate(pageUrl);
    pageMeta.textContent = `${title} ${uri}`;
    pageMeta.title = `${pageTitle} ${pageUrl}`.trim();
    const http = pageUrl !== "" && isHttpPage(pageUrl);
    if (!http) {
      pageHint.className = "hint page-warn";
      pageHint.textContent = copy.pageCannotTag;
      setTagEnabled(false);
    } else {
      pageHint.className = "hint";
      pageHint.textContent = "";
      setTagEnabled(config !== undefined && config.token.trim() !== "");
    }
  }

  function renderChips(items: TagInfo[]): void {
    chips.replaceChildren();
    for (const tag of items) {
      const li = el("li", "chip");
      const name = document.createElement("span");
      name.textContent = tagChipLabel(tag);
      const del = document.createElement("button");
      del.type = "button";
      del.textContent = "删除";
      del.disabled = !tagActionsEnabled;
      del.addEventListener("click", () => {
        void untagById(tag.id);
      });
      li.append(name, del);
      chips.append(li);
    }
  }

  function renderHits(next: QueryHit[]): void {
    hits = next;
    selected = defaultSelectedIds(hits);
    hitsList.replaceChildren();
    if (hits.length === 0) {
      previewHint.textContent = copy.previewEmpty;
      return;
    }
    previewHint.textContent = "";
    for (const hit of hits) {
      const li = document.createElement("li");
      const box = document.createElement("input");
      box.type = "checkbox";
      box.value = hit.id;
      const openable = isOpenableUrl(hit.kind, hit.uri);
      box.disabled = !openable;
      box.checked = selected.has(hit.id);
      box.addEventListener("change", () => {
        if (box.checked) selected.add(hit.id);
        else selected.delete(hit.id);
      });
      const label = document.createElement("label");
      label.className = "inline";
      const text = truncate(hit.displayName || hit.uri);
      label.append(box, ` ${text}`);
      if (!openable) {
        const note = document.createElement("span");
        note.className = "muted";
        note.textContent = ` ${copy.cannotOpen}`;
        label.append(note);
      }
      li.append(label);
      hitsList.append(li);
    }
  }

  async function confirmUntagIfNeeded(): Promise<boolean> {
    if (config?.confirmUntag === false) return true;
    return window.confirm(copy.untagConfirm);
  }

  async function refreshTags(): Promise<void> {
    if (!tagActionsEnabled || pageUrl === "") {
      resourceExists = false;
      renderChips([]);
      return;
    }
    const result = await send<PageTags>({ type: "getTags", uri: pageUrl });
    if (!result.ok) {
      showFailure(result);
      return;
    }
    resourceExists = result.data.resourceExists;
    renderChips(result.data.items);
  }

  async function applyPage(url: string | undefined, title: string | undefined): Promise<void> {
    pageUrl = url ?? "";
    pageTitle = title ?? "";
    renderPage();
    await refreshTags();
  }

  async function loadPopupPage(): Promise<void> {
    const tabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
    const tab = tabs[0];
    await applyPage(tab?.url, tab?.title);
  }

  async function loadAppPage(): Promise<void> {
    const stored = await chrome.storage.session.get("pageSnapshot");
    const snap = stored.pageSnapshot as PageSnapshot | undefined;
    await applyPage(snap?.url, snap?.title);
  }

  async function untagById(tagId: string): Promise<void> {
    if (!tagActionsEnabled) return;
    if (!(await confirmUntagIfNeeded())) return;
    const result = await send({ type: "untagById", uri: pageUrl, tagId });
    if (!result.ok) {
      showFailure(result);
      return;
    }
    setStatus("");
    await refreshTags();
  }

  async function untagByAlias(): Promise<void> {
    const alias = aliasInput.value.trim();
    if (!tagActionsEnabled || alias === "") return;
    if (!(await confirmUntagIfNeeded())) return;
    const result = await send({ type: "untagByAlias", uri: pageUrl, alias });
    if (!result.ok) {
      showFailure(result);
      return;
    }
    aliasInput.value = "";
    suggestList.replaceChildren();
    setStatus("");
    await refreshTags();
  }

  async function tagCurrent(): Promise<void> {
    const alias = aliasInput.value.trim();
    if (!tagActionsEnabled || alias === "") return;
    const result = await send({
      type: "tag",
      uri: pageUrl,
      alias,
      createMissing: createMissing.checked,
      resourceExists,
      title: pageTitle,
    });
    if (!result.ok) {
      showFailure(result);
      return;
    }
    aliasInput.value = "";
    suggestList.replaceChildren();
    setStatus("");
    await refreshTags();
  }

  const runSuggest = debounce(() => {
    void (async () => {
      const q = aliasInput.value.trim();
      if (q.length < 1) {
        suggestList.replaceChildren();
        return;
      }
      const result = await send<SuggestItem[]>({ type: "suggest", q });
      suggestList.replaceChildren();
      if (!result.ok) return;
      for (const item of result.data) {
        const li = document.createElement("li");
        const btn = document.createElement("button");
        btn.type = "button";
        btn.textContent = item.alias;
        btn.addEventListener("click", () => {
          aliasInput.value = item.alias;
          suggestList.replaceChildren();
        });
        li.append(btn);
        suggestList.append(li);
      }
    })();
  }, SUGGEST_MS);

  aliasInput.addEventListener("input", () => {
    runSuggest();
  });
  aliasInput.addEventListener("keydown", (ev) => {
    if (ev.key === "Enter") {
      ev.preventDefault();
      void tagCurrent();
    }
  });
  tagBtn.addEventListener("click", () => {
    void tagCurrent();
  });
  untagBtn.addEventListener("click", () => {
    void untagByAlias();
  });

  async function runPreview(): Promise<void> {
    const result = await send<QueryHit[]>({ type: "preview", q: qInput.value });
    if (!result.ok) {
      showFailure(result);
      return;
    }
    setStatus("");
    renderHits(result.data);
  }

  previewBtn.addEventListener("click", () => {
    void runPreview();
  });
  qInput.addEventListener("keydown", (ev) => {
    if (ev.key === "Enter") {
      ev.preventDefault();
      void runPreview();
    }
  });

  openBtn.addEventListener("click", () => {
    void (async () => {
      if (config === undefined) return;
      const decision = decideOpen(hits, selected, config.previewLimit);
      if (!decision.ok) {
        setStatus(decision.reason === "none" ? copy.openNone : copy.openOverLimit, "err");
        return;
      }
      const result = await send({ type: "openTabs", urls: decision.urls });
      if (!result.ok) {
        showFailure(result);
        return;
      }
      setStatus("");
    })();
  });

  followBtn.addEventListener("click", () => {
    void (async () => {
      const tabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
      const tab = tabs[0];
      await chrome.storage.session.set({
        pageSnapshot: { tabId: tab?.id, url: tab?.url, title: tab?.title },
      });
    })();
  });

  async function boot(): Promise<void> {
    const cfg = await send<ElExtensionConfig>({ type: "getConfig" });
    if (!cfg.ok) {
      showFailure(cfg);
      return;
    }
    config = cfg.data;
    if (config.token.trim() === "") showConfigMissingBanner();
    else banner.hidden = true;

    if (mode === "popup") {
      await loadPopupPage();
      return;
    }

    await loadAppPage();
    chrome.storage.session.onChanged.addListener((changes) => {
      if (!("pageSnapshot" in changes)) return;
      void loadAppPage();
    });
  }

  void boot();
}
