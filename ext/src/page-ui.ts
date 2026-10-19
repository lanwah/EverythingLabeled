import { copy } from "./copy";
import type { ElExtensionConfig } from "./config";
import { shouldCheckCreateMissing } from "./create-missing-suggest";
import { debounce } from "./debounce";
import type { QueryHit, SuggestItem, TagInfo } from "./el-api";
import {
  composePreviewQuery,
  normalizeQueryToken,
  tryAddPreviewTag,
} from "./find-query";
import { suggestRowLabel, tagChipLabel } from "./labels";
import { sendToBackground } from "./runtime-message";
import { pickFollowTab } from "./follow-tab";
import { openOrFocusExtensionPage } from "./open-extension-page";
import { decideOpen, defaultSelectedIds } from "./open-rules";
import {
  emptySuggestNav,
  moveSuggestNav,
  renderSuggestList,
  selectedSuggestAlias,
  suggestNavFromRows,
  type SuggestNavState,
} from "./suggest-nav";
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

function kindIconClass(kind: string): string {
  if (kind === "url") return "webpage";
  if (kind === "folder") return "folder";
  return "file";
}

function displayTitle(hit: QueryHit): string {
  const name = hit.displayName;
  return name == null || name.trim() === "" ? hit.uri : name;
}

export function mountPageUi(root: HTMLElement, mode: PageUiMode): void {
  root.replaceChildren();
  root.className = "ext-shell";

  const head = el("header", "ext-head");
  const heading = el("h1");
  heading.textContent = "EverythingLabeled";
  const headActions = el("div", "actions-right");
  const libraryLink = el("a", "btn");
  libraryLink.href = chrome.runtime.getURL("library.html");
  libraryLink.textContent = copy.libraryTitle;
  libraryLink.addEventListener("click", (event) => {
    event.preventDefault();
    void openOrFocusExtensionPage("library.html");
  });
  headActions.append(libraryLink);
  const followBtn = document.createElement("button");
  followBtn.type = "button";
  followBtn.className = "btn";
  followBtn.textContent = "跟随当前网页";
  if (mode === "app") headActions.append(followBtn);
  head.append(heading, headActions);

  const banner = el("p", "banner");
  banner.hidden = true;
  const pageCard = el("section", "page-card surface");
  const pageTitleEl = el("div", "title");
  const pageUriEl = el("div", "sub");
  pageCard.append(pageTitleEl, pageUriEl);
  const pageHint = el("p", "hint");

  const tagsLabel = el("p", "section-label");
  tagsLabel.textContent = "当前标签";
  const tagWell = el("div", "tag-chip-well surface");
  const tagEmpty = el("p", "empty-hint");
  tagEmpty.textContent = copy.tagsEmpty;
  const chips = el("ul", "tag-chips");
  tagWell.append(tagEmpty, chips);

  const tagLabel = el("p", "section-label");
  tagLabel.textContent = "打标";
  const aliasField = el("div", "alias-field");
  const aliasInput = document.createElement("input");
  aliasInput.type = "text";
  aliasInput.autocomplete = "off";
  aliasInput.placeholder = "输入别名…";
  aliasInput.setAttribute("aria-label", "别名");
  aliasInput.setAttribute("aria-autocomplete", "list");
  const suggestList = el("ul", "suggest-list");
  aliasField.append(aliasInput, suggestList);

  const tagBtn = document.createElement("button");
  tagBtn.type = "button";
  tagBtn.className = "btn btn-primary";
  tagBtn.textContent = "打标";
  const untagBtn = document.createElement("button");
  untagBtn.type = "button";
  untagBtn.className = "btn";
  untagBtn.textContent = "去标";
  const createMissing = document.createElement("input");
  createMissing.type = "checkbox";
  createMissing.checked = false;
  const createLabel = document.createElement("label");
  createLabel.className = "check-row";
  const createText = document.createElement("span");
  createText.textContent = "创建缺失标签";
  createLabel.append(createMissing, createText);
  const tagActions = el("div", "actions-right");
  tagActions.append(untagBtn, tagBtn);
  const tagFooter = el("div", "footer");
  tagFooter.append(createLabel, tagActions);

  const previewLabel = el("p", "section-label");
  previewLabel.textContent = "预览";
  const chipField = el("div", "chip-field");
  const chipWrap = el("div", "chip-wrap");
  const chipInputWrap = el("div", "chip-input");
  const qInput = document.createElement("input");
  qInput.type = "text";
  qInput.autocomplete = "off";
  qInput.placeholder = copy.findQueryPlaceholder;
  qInput.setAttribute("aria-label", "标签查询");
  qInput.setAttribute("aria-autocomplete", "list");
  const previewSuggest = el("ul", "suggest-list");
  chipInputWrap.append(qInput);
  chipWrap.append(chipInputWrap);
  chipField.append(chipWrap, previewSuggest);
  const andBtn = document.createElement("button");
  andBtn.type = "button";
  andBtn.textContent = "AND";
  andBtn.setAttribute("aria-pressed", "true");
  const orBtn = document.createElement("button");
  orBtn.type = "button";
  orBtn.textContent = "OR";
  orBtn.setAttribute("aria-pressed", "false");
  const modeToggle = el("div", "mode-toggle");
  modeToggle.setAttribute("role", "group");
  modeToggle.setAttribute("aria-label", "匹配模式");
  modeToggle.append(andBtn, orBtn);
  const queryRow = el("div", "find-toolbar");
  queryRow.append(chipField, modeToggle);
  const hitsList = el("ul", "result-list surface");
  const previewHint = el("p", "empty-hint");
  previewHint.textContent = copy.previewIdle;
  const clearBtn = document.createElement("button");
  clearBtn.type = "button";
  clearBtn.className = "btn";
  clearBtn.textContent = copy.clearTags;
  const openBtn = document.createElement("button");
  openBtn.type = "button";
  openBtn.className = "btn btn-primary";
  openBtn.textContent = "打开所选";
  const status = el("span", "status");
  const previewActions = el("div", "actions-right");
  previewActions.append(clearBtn, openBtn);
  const previewFooter = el("div", "footer");
  previewFooter.append(status, previewActions);

  root.append(
    head,
    banner,
    pageCard,
    pageHint,
    tagsLabel,
    tagWell,
    tagLabel,
    aliasField,
    tagFooter,
    previewLabel,
    queryRow,
    previewHint,
    hitsList,
    previewFooter,
  );

  let config: ElExtensionConfig | undefined;
  let pageUrl = "";
  let pageTitle = "";
  let resourceExists = false;
  let tagsRequestId = 0;
  let hits: QueryHit[] = [];
  let selected = new Set<string>();
  let tagActionsEnabled = false;
  let previewCount = 0;
  let previewTags: string[] = [];
  let previewUseOr = false;
  let previewRequestId = 0;
  let tagSuggestRequestId = 0;
  let tagSuggest: SuggestNavState = emptySuggestNav();
  let previewSuggestRequestId = 0;
  let previewSuggestNav: SuggestNavState = emptySuggestNav();

  function previewStatusText(): string {
    return `预览 ${previewCount} 条`;
  }

  function setStatus(text: string, kind: "ok" | "err" | "" = ""): void {
    status.className = kind ? `status ${kind}` : "status";
    status.textContent = text;
  }

  setStatus(previewStatusText());

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
    const uri = pageUrl === "" ? copy.pageNotFollowed : truncate(pageUrl);
    pageTitleEl.textContent = title;
    pageUriEl.textContent = uri;
    pageCard.title = `${pageTitle} ${pageUrl}`.trim();
    const http = pageUrl !== "" && isHttpPage(pageUrl);
    if (pageUrl === "") {
      pageHint.className = "hint";
      pageHint.textContent = "";
      setTagEnabled(false);
    } else if (!http) {
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
    tagEmpty.hidden = items.length > 0;
    for (const tag of items) {
      const li = el("li", "chip");
      const name = document.createElement("span");
      name.textContent = tagChipLabel(tag);
      const del = document.createElement("button");
      del.type = "button";
      del.textContent = "×";
      del.setAttribute("aria-label", "删除");
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
    previewCount = hits.length;
    selected = defaultSelectedIds(hits);
    hitsList.replaceChildren();
    setStatus(previewStatusText());
    if (hits.length === 0) {
      previewHint.hidden = false;
      previewHint.textContent = copy.previewEmpty;
      return;
    }
    previewHint.hidden = true;
    previewHint.textContent = "";
    for (const hit of hits) {
      const li = document.createElement("li");
      const box = document.createElement("input");
      box.type = "checkbox";
      box.value = hit.id;
      const openable = isOpenableUrl(hit.kind, hit.uri);
      box.disabled = !openable;
      box.checked = selected.has(hit.id);
      if (box.checked) li.classList.add("selected");
      box.addEventListener("change", () => {
        if (box.checked) selected.add(hit.id);
        else selected.delete(hit.id);
        li.classList.toggle("selected", box.checked);
      });
      const icon = el("span", `kind-icon ${kindIconClass(hit.kind)}`);
      icon.setAttribute("aria-hidden", "true");
      const text = el("div", "result-text");
      const title = el("div", "title");
      title.textContent = truncate(displayTitle(hit));
      const sub = el("div", "sub");
      sub.textContent = openable ? hit.uri : `${hit.uri} · ${copy.cannotOpen}`;
      text.append(title, sub);
      li.append(box, icon, text);
      hitsList.append(li);
    }
  }

  async function confirmUntagIfNeeded(): Promise<boolean> {
    if (config?.confirmUntag === false) return true;
    return window.confirm(copy.untagConfirm);
  }

  function resetTagsState(): void {
    resourceExists = false;
    renderChips([]);
  }

  async function refreshTags(): Promise<void> {
    const requestId = ++tagsRequestId;
    resetTagsState();
    if (!tagActionsEnabled || pageUrl === "") return;
    const uri = pageUrl;
    const result = await sendToBackground<PageTags>({ type: "getTags", uri });
    if (requestId !== tagsRequestId) return;
    if (!result.ok) {
      resetTagsState();
      showFailure(result);
      return;
    }
    resourceExists = result.data.resourceExists;
    renderChips(result.data.items);
  }

  async function applyPage(url: string | undefined, title: string | undefined): Promise<void> {
    ++tagsRequestId;
    pageUrl = url ?? "";
    pageTitle = title ?? "";
    resetTagsState();
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
    const result = await sendToBackground({ type: "untagById", uri: pageUrl, tagId });
    if (!result.ok) {
      showFailure(result);
      return;
    }
    setStatus(previewStatusText());
    await refreshTags();
  }

  async function untagByAlias(): Promise<void> {
    const alias = aliasInput.value.trim();
    if (!tagActionsEnabled || alias === "") return;
    if (!(await confirmUntagIfNeeded())) return;
    const result = await sendToBackground({ type: "untagByAlias", uri: pageUrl, alias });
    if (!result.ok) {
      showFailure(result);
      return;
    }
    aliasInput.value = "";
    clearTagSuggest();
    setStatus(previewStatusText());
    await refreshTags();
  }

  async function tagCurrent(): Promise<void> {
    const alias = aliasInput.value.trim();
    if (!tagActionsEnabled || alias === "") return;
    const result = await sendToBackground({
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
    clearTagSuggest();
    createMissing.checked = false;
    setStatus(previewStatusText());
    await refreshTags();
  }

  function clearTagSuggest(): void {
    tagSuggestRequestId += 1;
    tagSuggest = emptySuggestNav();
    suggestList.replaceChildren();
  }

  function paintTagSuggest(): void {
    renderSuggestList(suggestList, tagSuggest, (alias) => {
      aliasInput.value = alias;
      clearTagSuggest();
      createMissing.checked = false;
    });
  }

  const runSuggest = debounce(() => {
    void (async () => {
      const requestId = ++tagSuggestRequestId;
      const q = normalizeQueryToken(aliasInput.value);
      if (q == null) {
        clearTagSuggest();
        createMissing.checked = false;
        return;
      }
      const result = await sendToBackground<SuggestItem[]>({ type: "suggest", q });
      if (requestId !== tagSuggestRequestId) return;
      if (!result.ok) {
        tagSuggest = emptySuggestNav();
        suggestList.replaceChildren();
        return;
      }
      createMissing.checked = shouldCheckCreateMissing(
        q,
        result.data.map((item) => item.alias),
      );
      tagSuggest = suggestNavFromRows(
        result.data.map((item) => ({
          alias: item.alias,
          label: suggestRowLabel(item),
        })),
      );
      paintTagSuggest();
    })();
  }, SUGGEST_MS);

  aliasInput.addEventListener("input", () => {
    runSuggest();
  });
  aliasInput.addEventListener("keydown", (ev) => {
    if (ev.key === "ArrowDown" || ev.key === "ArrowUp") {
      if (tagSuggest.rows.length === 0) return;
      tagSuggest = moveSuggestNav(tagSuggest, ev.key === "ArrowDown");
      paintTagSuggest();
      ev.preventDefault();
      return;
    }
    if (ev.key !== "Enter") return;
    ev.preventDefault();
    const fromSuggest = selectedSuggestAlias(tagSuggest);
    if (fromSuggest !== undefined) {
      aliasInput.value = fromSuggest;
      clearTagSuggest();
      createMissing.checked = false;
    }
    void tagCurrent();
  });
  tagBtn.addEventListener("click", () => {
    void tagCurrent();
  });
  untagBtn.addEventListener("click", () => {
    void untagByAlias();
  });

  function clearPreviewSuggest(): void {
    previewSuggestRequestId += 1;
    previewSuggestNav = emptySuggestNav();
    previewSuggest.replaceChildren();
  }

  function paintPreviewSuggest(): void {
    renderSuggestList(previewSuggest, previewSuggestNav, (alias) => {
      commitPreviewTag(alias);
    });
  }

  function renderPreviewChips(): void {
    for (const node of [...chipWrap.children]) {
      if (node !== chipInputWrap) node.remove();
    }
    for (const alias of previewTags) {
      const chip = el("span", "chip");
      const name = document.createElement("span");
      name.textContent = alias;
      const del = document.createElement("button");
      del.type = "button";
      del.textContent = "×";
      del.setAttribute("aria-label", "移除");
      del.addEventListener("click", () => {
        previewTags = previewTags.filter((tag) => tag !== alias);
        renderPreviewChips();
        void runPreview();
        qInput.focus();
      });
      chip.append(name, del);
      chipWrap.insertBefore(chip, chipInputWrap);
    }
    qInput.placeholder =
      previewTags.length > 0 ? copy.findQueryContinue : copy.findQueryPlaceholder;
  }

  function commitPreviewTag(raw: string | null): void {
    if (!tryAddPreviewTag(previewTags, raw)) return;
    qInput.value = "";
    clearPreviewSuggest();
    renderPreviewChips();
    void runPreview();
  }

  function showIdlePreview(): void {
    hits = [];
    previewCount = 0;
    selected = new Set();
    hitsList.replaceChildren();
    previewHint.hidden = false;
    previewHint.textContent = copy.previewIdle;
    setStatus(previewStatusText());
  }

  async function runPreview(): Promise<void> {
    const requestId = ++previewRequestId;
    const q = composePreviewQuery(previewTags, previewUseOr);
    if (q == null) {
      showIdlePreview();
      return;
    }
    const result = await sendToBackground<QueryHit[]>({ type: "preview", q });
    if (requestId !== previewRequestId) return;
    if (!result.ok) {
      showFailure(result);
      return;
    }
    renderHits(result.data);
  }

  const runPreviewSuggest = debounce(() => {
    void (async () => {
      const requestId = ++previewSuggestRequestId;
      const q = normalizeQueryToken(qInput.value);
      if (q == null) {
        clearPreviewSuggest();
        return;
      }
      const result = await sendToBackground<SuggestItem[]>({ type: "suggest", q });
      if (requestId !== previewSuggestRequestId) return;
      if (!result.ok) {
        previewSuggestNav = emptySuggestNav();
        previewSuggest.replaceChildren();
        return;
      }
      previewSuggestNav = suggestNavFromRows(
        result.data.map((item) => ({
          alias: item.alias,
          label: suggestRowLabel(item),
        })),
      );
      paintPreviewSuggest();
    })();
  }, SUGGEST_MS);

  function setPreviewUseOr(useOr: boolean): void {
    if (useOr === previewUseOr) return;
    previewUseOr = useOr;
    andBtn.setAttribute("aria-pressed", useOr ? "false" : "true");
    orBtn.setAttribute("aria-pressed", useOr ? "true" : "false");
    void runPreview();
  }

  qInput.addEventListener("input", () => {
    runPreviewSuggest();
  });
  qInput.addEventListener("keydown", (ev) => {
    if (ev.key === "Backspace" && qInput.value === "" && previewTags.length > 0) {
      previewTags.pop();
      renderPreviewChips();
      void runPreview();
      ev.preventDefault();
      return;
    }
    if (ev.key === "ArrowDown" || ev.key === "ArrowUp") {
      if (previewSuggestNav.rows.length === 0) return;
      previewSuggestNav = moveSuggestNav(previewSuggestNav, ev.key === "ArrowDown");
      paintPreviewSuggest();
      ev.preventDefault();
      return;
    }
    if (ev.key !== "Enter") return;
    ev.preventDefault();
    const fromSuggest = selectedSuggestAlias(previewSuggestNav);
    if (fromSuggest !== undefined && normalizeQueryToken(qInput.value) != null) {
      commitPreviewTag(fromSuggest);
      return;
    }
    const draft = normalizeQueryToken(qInput.value);
    if (draft != null) commitPreviewTag(draft);
  });
  andBtn.addEventListener("click", () => {
    setPreviewUseOr(false);
  });
  orBtn.addEventListener("click", () => {
    setPreviewUseOr(true);
  });
  clearBtn.addEventListener("click", () => {
    previewTags = [];
    qInput.value = "";
    clearPreviewSuggest();
    renderPreviewChips();
    void runPreview();
    qInput.focus();
  });

  async function openSelected(): Promise<void> {
    if (config === undefined) return;
    const decision = decideOpen(hits, selected, config.previewLimit);
    if (!decision.ok) {
      setStatus(decision.reason === "none" ? copy.openNone : copy.openOverLimit, "err");
      return;
    }
    const result = await sendToBackground({ type: "openTabs", urls: decision.urls });
    if (!result.ok) {
      showFailure(result);
      return;
    }
    setStatus(previewStatusText());
  }

  openBtn.addEventListener("click", () => {
    void openSelected();
  });

  followBtn.addEventListener("click", () => {
    void (async () => {
      const tabs = await chrome.tabs.query({ lastFocusedWindow: true });
      const tab = pickFollowTab(tabs);
      if (tab === undefined) {
        setStatus(copy.followNoHttpPage, "err");
        return;
      }
      setStatus(previewStatusText());
      await chrome.storage.session.set({
        pageSnapshot: { tabId: tab.id, url: tab.url, title: tab.title },
      });
    })();
  });

  async function boot(): Promise<void> {
    const cfg = await sendToBackground<ElExtensionConfig>({ type: "getConfig" });
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
