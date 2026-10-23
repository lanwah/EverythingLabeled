import { copy } from "./copy";
import {
  DEFAULT_PREVIEW_LIMIT,
  DEFAULT_SUGGEST_LIMIT,
  type ElExtensionConfig,
} from "./config";
import { shouldCheckCreateMissing } from "./create-missing-suggest";
import { debounce } from "./debounce";
import type { QueryHit, SuggestItem, TagInfo } from "./el-api";
import {
  composePreviewQuery,
  nextDownIndex,
  nextUpIndex,
  normalizeQueryToken,
  SUGGEST_DEBOUNCE_MS,
  tryAddPreviewTag,
} from "./find-query";
import { clearFixedPlacement, placeFixedNear } from "./floating-anchor";
import { suggestRowLabel, tagChipLabel } from "./labels";
import {
  hitsHeightForPopupCap,
  popupHeightPx,
  previewHitsHeightPx,
  spaceBelowPx,
  suggestListHeightPx,
  SUGGEST_GAP_PX,
} from "./popup-layout";
import { sendToBackground } from "./runtime-message";
import { pickFollowTab } from "./follow-tab";
import { openOrFocusExtensionPage } from "./open-extension-page";
import { decideOpen } from "./open-rules";
import {
  emptySuggestNav,
  moveSuggestNav,
  renderSuggestList,
  scrollSelectedIntoContainer,
  selectedSuggestAlias,
  suggestNavFromRows,
  type SuggestNavState,
} from "./suggest-nav";
import { isHttpPage, isOpenableUrl } from "./uri";
import { formatUiError } from "./ui-error";

export type PageUiMode = "popup" | "app";

/** Popup / 打标页主界面：打标与查找分模式（switch 切换）。 */
export type PageView = "tag" | "find";

type PageTags = { items: TagInfo[]; resourceExists: boolean };

type PageSnapshot = {
  tabId?: number;
  url?: string;
  title?: string;
};

const TRUNCATE = 80;
const DEFAULT_PAGE_VIEW: PageView = "tag";

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
  /** Popup: suggest stays fixed so it does not enlarge .ext-shell scrollHeight. */
  const useFixedSuggest = mode === "popup";

  const head = el("header", "ext-head ext-head-stacked");
  const heading = el("h1");
  heading.textContent = "EverythingLabeled";
  const headToolbar = el("div", "ext-head-toolbar");
  const modeSwitch = el("div", "mode-switch");
  modeSwitch.setAttribute("role", "group");
  modeSwitch.setAttribute("aria-label", "功能模式");
  const modeSwitchBtn = document.createElement("button");
  modeSwitchBtn.type = "button";
  modeSwitchBtn.className = "mode-switch-control";
  modeSwitchBtn.setAttribute("role", "switch");
  modeSwitchBtn.setAttribute("aria-label", copy.viewModeSwitch);
  const modeLabel = el("span", "mode-switch-label");
  modeSwitch.append(modeSwitchBtn, modeLabel);
  const headActions = el("div", "actions-right");
  const libraryLink = el("a", "btn");
  libraryLink.href = chrome.runtime.getURL("library.html");
  libraryLink.textContent = copy.libraryTitle;
  libraryLink.addEventListener("click", (event) => {
    event.preventDefault();
    void openOrFocusExtensionPage("library.html");
  });
  const resourcesLink = el("a", "btn");
  resourcesLink.href = chrome.runtime.getURL("resources.html");
  resourcesLink.textContent = copy.resourcesTitle;
  resourcesLink.addEventListener("click", (event) => {
    event.preventDefault();
    void openOrFocusExtensionPage("resources.html");
  });
  headActions.append(libraryLink, resourcesLink);
  const followBtn = document.createElement("button");
  followBtn.type = "button";
  followBtn.className = "btn";
  followBtn.textContent = "跟随当前网页";
  if (mode === "app") headActions.append(followBtn);
  headToolbar.append(modeSwitch, headActions);
  head.append(heading, headToolbar);

  const banner = el("p", "banner");
  banner.hidden = true;

  const tagPanel = el("div", "view-panel");
  tagPanel.dataset.viewPanel = "tag";
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

  const createMissing = document.createElement("input");
  createMissing.type = "checkbox";
  createMissing.checked = false;
  const createLabel = document.createElement("label");
  createLabel.className = "check-row";
  const createText = document.createElement("span");
  createText.textContent = "创建缺失标签";
  createLabel.append(createMissing, createText);
  const tagFooter = el("div", "footer");
  tagFooter.append(createLabel);
  tagPanel.append(
    pageCard,
    pageHint,
    tagsLabel,
    tagWell,
    tagLabel,
    aliasField,
    tagFooter,
  );

  const findPanel = el("div", "view-panel");
  findPanel.dataset.viewPanel = "find";
  findPanel.hidden = true;
  const previewBlock = el("div", "ext-preview-block");
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
  const clearBtn = document.createElement("button");
  clearBtn.type = "button";
  clearBtn.className = "clear-all-tags";
  clearBtn.textContent = "\uE894";
  clearBtn.title = copy.clearTags;
  clearBtn.setAttribute("aria-label", copy.clearTags);
  clearBtn.hidden = true;
  chipField.append(chipWrap, clearBtn, previewSuggest);
  const queryRow = el("div", "find-toolbar");
  queryRow.append(chipField);
  const previewHits = el("div", "ext-preview-hits");
  previewHits.setAttribute("role", "listbox");
  previewHits.setAttribute("aria-label", "查找结果");
  const previewHint = el("p", "empty-hint ext-preview-empty");
  previewHint.textContent = copy.previewIdle;
  const hitsList = el("ul", "result-list");
  previewHits.append(previewHint, hitsList);
  previewBlock.append(queryRow, previewHits);
  findPanel.append(previewBlock);

  const status = el("span", "status");
  const statusFooter = el("div", "footer");
  statusFooter.append(status);

  root.append(head, banner, tagPanel, findPanel, statusFooter);

  let config: ElExtensionConfig | undefined;
  let pageUrl = "";
  let pageTitle = "";
  let resourceExists = false;
  let tagsRequestId = 0;
  let hits: QueryHit[] = [];
  let selectedId: string | null = null;
  let tagActionsEnabled = false;
  let previewCount = 0;
  let previewTags: string[] = [];
  let previewRequestId = 0;
  let tagSuggestRequestId = 0;
  let tagSuggest: SuggestNavState = emptySuggestNav();
  let previewSuggestRequestId = 0;
  let previewSuggestNav: SuggestNavState = emptySuggestNav();
  let activeView: PageView = DEFAULT_PAGE_VIEW;

  function previewStatusText(): string {
    return copy.previewCount(previewCount);
  }

  function previewUseOr(): boolean {
    return config?.findTagLogic === "or";
  }

  function setStatus(text: string, kind: "ok" | "err" | "" = ""): void {
    status.className = kind ? `status ${kind}` : "status";
    status.textContent = text;
  }

  function paintModeSwitch(): void {
    const findOn = activeView === "find";
    modeSwitchBtn.setAttribute("aria-checked", findOn ? "true" : "false");
    modeSwitchBtn.dataset.view = activeView;
    modeLabel.textContent = findOn ? copy.viewFindMode : copy.viewTagMode;
    tagPanel.hidden = activeView !== "tag";
    findPanel.hidden = activeView !== "find";
  }

  function syncStatusForView(): void {
    if (activeView === "find") {
      if (!status.classList.contains("err")) setStatus(previewStatusText());
      statusFooter.hidden = false;
      return;
    }
    if (!status.classList.contains("err")) {
      setStatus("");
      statusFooter.hidden = true;
      return;
    }
    statusFooter.hidden = false;
  }

  /** 当前模式对应文本框获焦，便于打开后立刻录入 / 查找。 */
  function focusActiveInput(): void {
    const target = activeView === "find" ? qInput : aliasInput;
    const run = (): void => {
      if (target.disabled) return;
      target.focus({ preventScroll: true });
    };
    // Popup 打开时焦点常落在首个按钮（模式开关）；点开关切换后也会留在开关上。
    // rAF 让获焦发生在当前点击与布局之后。
    requestAnimationFrame(run);
  }

  function setView(next: PageView): void {
    if (activeView === next) return;
    if (activeView === "tag") clearTagSuggest();
    if (activeView === "find") clearPreviewSuggest();
    activeView = next;
    paintModeSwitch();
    applyPopupLayout();
    syncStatusForView();
    focusActiveInput();
  }

  paintModeSwitch();
  syncStatusForView();
  const toggleMode = (): void => {
    setView(activeView === "tag" ? "find" : "tag");
  };
  // 避免指针切换时焦点粘在开关上，随后由 focusActiveInput 落到文本框。
  modeSwitchBtn.addEventListener("mousedown", (ev) => {
    if (ev.button === 0) ev.preventDefault();
  });
  modeSwitchBtn.addEventListener("click", toggleMode);
  modeLabel.addEventListener("click", toggleMode);

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
    statusFooter.hidden = false;
  }

  function setTagEnabled(enabled: boolean): void {
    tagActionsEnabled = enabled;
    aliasInput.disabled = !enabled;
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

  function paintHitSelection(): void {
    for (const li of hitsList.querySelectorAll("li")) {
      const id = li.dataset.hitId;
      li.classList.toggle("selected", id !== undefined && id === selectedId);
    }
    scrollSelectedIntoContainer(previewHits);
  }

  /** Align Host Find: suggest first, else preview hits. */
  function movePreviewList(down: boolean): boolean {
    if (previewSuggestNav.rows.length > 0) {
      previewSuggestNav = moveSuggestNav(previewSuggestNav, down);
      paintPreviewSuggest();
      return true;
    }
    if (hits.length === 0) return false;
    const current = hits.findIndex((hit) => hit.id === selectedId);
    const next = down
      ? nextDownIndex(current, hits.length)
      : nextUpIndex(current, hits.length);
    if (next < 0) return false;
    const hit = hits[next];
    if (hit === undefined) return false;
    selectedId = hit.id;
    paintHitSelection();
    return true;
  }

  function tryOpenSelectedHit(): boolean {
    if (hits.length === 0) return false;
    const hit =
      (selectedId != null ? hits.find((row) => row.id === selectedId) : undefined) ??
      hits[0];
    if (hit === undefined) return false;
    selectedId = hit.id;
    paintHitSelection();
    void openHit(hit);
    return true;
  }

  function renderHits(next: QueryHit[]): void {
    hits = next;
    previewCount = hits.length;
    selectedId = hits[0]?.id ?? null;
    hitsList.replaceChildren();
    setStatus(previewStatusText());
    if (hits.length === 0) {
      previewHint.hidden = false;
      previewHint.textContent =
        previewTags.length === 0 ? copy.previewIdle : copy.previewEmpty;
      return;
    }
    previewHint.hidden = true;
    previewHint.textContent = "";
    for (const hit of hits) {
      const li = document.createElement("li");
      li.dataset.hitId = hit.id;
      li.title = "双击或回车打开";
      if (hit.id === selectedId) li.classList.add("selected");
      const openable = isOpenableUrl(hit.kind, hit.uri);
      const icon = el("span", `kind-icon ${kindIconClass(hit.kind)}`);
      icon.setAttribute("aria-hidden", "true");
      const text = el("div", "result-text");
      const titleRow = el("div", "title-row");
      const name = el("span", "name");
      name.textContent = truncate(displayTitle(hit));
      const tags = el("span", "result-tags");
      tags.setAttribute("aria-label", "标签");
      for (const tag of hit.tags ?? []) {
        const capsule = el("span", "result-tag");
        capsule.textContent = tag;
        tags.append(capsule);
      }
      titleRow.append(name, tags);
      const sub = el("div", "sub");
      sub.textContent = openable ? hit.uri : `${hit.uri} · ${copy.cannotOpen}`;
      text.append(titleRow, sub);
      li.append(icon, text);
      li.addEventListener("click", () => {
        selectedId = hit.id;
        paintHitSelection();
      });
      li.addEventListener("dblclick", () => {
        void openHit(hit);
      });
      hitsList.append(li);
    }
    syncPopupFloats();
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
    applyPopupLayout();
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
    syncStatusForView();
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
    syncStatusForView();
    await refreshTags();
  }

  /**
   * 拼首联想 max-height：按 Options「拼首联想预览条数」算理想高度，
   * 再与锚点下方可用空间（或命中区实测高度）取较小值。
   */
  function suggestMaxHeightFor(anchor: HTMLElement): number {
    const ideal = suggestListHeightPx(
      config?.suggestLimit ?? DEFAULT_SUGGEST_LIMIT,
    );
    let available: number;
    if (anchor === chipField) {
      const hitsH = previewHits.getBoundingClientRect().height;
      available =
        hitsH > 0
          ? Math.max(48, Math.floor(hitsH))
          : Math.max(
              48,
              spaceBelowPx({
                viewportHeightPx: window.innerHeight,
                anchorBottomPx: anchor.getBoundingClientRect().bottom,
                gapPx: SUGGEST_GAP_PX,
              }),
            );
    } else {
      available = Math.max(
        48,
        spaceBelowPx({
          viewportHeightPx: window.innerHeight,
          anchorBottomPx: anchor.getBoundingClientRect().bottom,
          gapPx: SUGGEST_GAP_PX,
        }),
      );
    }
    return Math.min(ideal, available);
  }

  /**
   * 按标签检索列表：先按「标签查询预览条数」预留，再把命中区拉满到 Popup 固定 600px，
   * 整窗 overflow:hidden，避免浏览器外层滚动条。打标模式不测命中区。
   */
  function applyPopupLayout(): void {
    const limit = config?.previewLimit ?? DEFAULT_PREVIEW_LIMIT;
    const suggestLimit = config?.suggestLimit ?? DEFAULT_SUGGEST_LIMIT;
    const idealHitsH = previewHitsHeightPx(limit);
    const idealSuggestH = suggestListHeightPx(suggestLimit);
    if (!useFixedSuggest) {
      document.body.style.setProperty("--preview-hits-height", `${idealHitsH}px`);
      document.body.style.setProperty("--suggest-max-height", `${idealSuggestH}px`);
      return;
    }

    // 解除定高再测 chrome，否则 scrollHeight 被旧高度截断。
    document.body.style.height = "auto";
    document.body.style.setProperty("--popup-height", "auto");
    root.style.height = "auto";
    root.style.paddingBottom = "";
    document.body.style.setProperty("--preview-hits-height", `${idealHitsH}px`);
    void root.offsetHeight;

    if (activeView === "find") {
      const contentWithIdeal = Math.max(
        root.scrollHeight,
        Math.ceil(root.getBoundingClientRect().height),
      );
      const hitsH = hitsHeightForPopupCap({
        idealHitsHeightPx: idealHitsH,
        contentHeightWithIdealPx: contentWithIdeal,
      });
      document.body.style.setProperty("--preview-hits-height", `${hitsH}px`);
      void root.offsetHeight;
    }

    const height = popupHeightPx();
    document.body.style.height = `${height}px`;
    document.body.style.setProperty("--popup-height", `${height}px`);
    root.style.height = "100%";
  }

  function setPreviewSuggestOpen(open: boolean): void {
    if (open) {
      // Collapse idle hint so it does not look like a large gap under the chip.
      previewHint.hidden = true;
    } else if (hits.length === 0) {
      previewHint.hidden = false;
      previewHint.textContent =
        previewTags.length === 0 ? copy.previewIdle : copy.previewEmpty;
    }
  }

  function syncPopupFloats(): void {
    if (!useFixedSuggest) return;
    if (suggestList.childElementCount > 0) {
      placeFixedNear(aliasField, suggestList, {
        gap: SUGGEST_GAP_PX,
        maxHeight: suggestMaxHeightFor(aliasField),
      });
    }
    if (previewSuggest.childElementCount > 0) {
      placeFixedNear(chipField, previewSuggest, {
        gap: SUGGEST_GAP_PX,
        maxHeight: suggestMaxHeightFor(chipField),
      });
    }
  }

  function clearTagSuggest(): void {
    tagSuggestRequestId += 1;
    tagSuggest = emptySuggestNav();
    suggestList.replaceChildren();
    if (useFixedSuggest) clearFixedPlacement(suggestList);
  }

  function paintTagSuggest(): void {
    renderSuggestList(suggestList, tagSuggest, (alias) => {
      aliasInput.value = alias;
      clearTagSuggest();
      createMissing.checked = false;
    });
    syncPopupFloats();
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
        if (useFixedSuggest) clearFixedPlacement(suggestList);
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
  }, SUGGEST_DEBOUNCE_MS);

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

  function clearPreviewSuggest(): void {
    previewSuggestRequestId += 1;
    previewSuggestNav = emptySuggestNav();
    previewSuggest.replaceChildren();
    if (useFixedSuggest) {
      clearFixedPlacement(previewSuggest);
      root.style.paddingBottom = "";
    }
    setPreviewSuggestOpen(false);
  }

  function paintPreviewSuggest(): void {
    renderSuggestList(previewSuggest, previewSuggestNav, (alias) => {
      commitPreviewTag(alias);
    });
    setPreviewSuggestOpen(previewSuggestNav.rows.length > 0);
    syncPopupFloats();
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
    clearBtn.hidden = previewTags.length === 0;
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
    selectedId = null;
    hitsList.replaceChildren();
    previewHint.hidden = false;
    previewHint.textContent = copy.previewIdle;
    setStatus(previewStatusText());
  }

  async function runPreview(): Promise<void> {
    const requestId = ++previewRequestId;
    const q = composePreviewQuery(previewTags, previewUseOr());
    if (q == null) {
      showIdlePreview();
      return;
    }
    const result = await sendToBackground<QueryHit[]>({
      type: "preview",
      q,
      resourceType: config?.previewResourceType ?? "links",
    });
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
        if (useFixedSuggest) {
          clearFixedPlacement(previewSuggest);
          root.style.paddingBottom = "";
        }
        setPreviewSuggestOpen(false);
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
  }, SUGGEST_DEBOUNCE_MS);

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
      if (movePreviewList(ev.key === "ArrowDown")) ev.preventDefault();
      return;
    }
    if (ev.key !== "Enter") return;
    // Chip mode (Find Q3): draft/suggest → chip; empty input + hits → open.
    const fromSuggest = selectedSuggestAlias(previewSuggestNav);
    if (fromSuggest !== undefined && normalizeQueryToken(qInput.value) != null) {
      ev.preventDefault();
      commitPreviewTag(fromSuggest);
      return;
    }
    const draft = normalizeQueryToken(qInput.value);
    if (draft != null) {
      ev.preventDefault();
      commitPreviewTag(draft);
      return;
    }
    if (tryOpenSelectedHit()) ev.preventDefault();
  });
  clearBtn.addEventListener("click", () => {
    previewTags = [];
    qInput.value = "";
    clearPreviewSuggest();
    renderPreviewChips();
    void runPreview();
    qInput.focus();
  });

  async function openHit(hit: QueryHit): Promise<void> {
    if (config === undefined) return;
    const decision = decideOpen([hit], new Set([hit.id]), config.previewLimit);
    if (!decision.ok) {
      setStatus(decision.reason === "none" ? copy.openNone : copy.openOverLimit, "err");
      return;
    }
    const result = await sendToBackground({ type: "openTabs", urls: decision.urls });
    if (!result.ok) {
      showFailure(result);
      return;
    }
    syncStatusForView();
  }

  followBtn.addEventListener("click", () => {
    void (async () => {
      const tabs = await chrome.tabs.query({ lastFocusedWindow: true });
      const tab = pickFollowTab(tabs);
      if (tab === undefined) {
        setStatus(copy.followNoHttpPage, "err");
        return;
      }
      syncStatusForView();
      await chrome.storage.session.set({
        pageSnapshot: { tabId: tab.id, url: tab.url, title: tab.title },
      });
    })();
  });

  if (useFixedSuggest) {
    root.addEventListener("scroll", () => {
      syncPopupFloats();
    });
    window.addEventListener("resize", () => {
      syncPopupFloats();
    });
  }

  async function boot(): Promise<void> {
    const cfg = await sendToBackground<ElExtensionConfig>({ type: "getConfig" });
    if (!cfg.ok) {
      showFailure(cfg);
      focusActiveInput();
      return;
    }
    config = cfg.data;
    if (config.token.trim() === "") showConfigMissingBanner();
    else banner.hidden = true;

    if (mode === "popup") {
      await loadPopupPage();
      applyPopupLayout();
      focusActiveInput();
      return;
    }

    applyPopupLayout();
    await loadAppPage();
    focusActiveInput();
    chrome.storage.session.onChanged.addListener((changes) => {
      if (!("pageSnapshot" in changes)) return;
      void loadAppPage().then(() => {
        focusActiveInput();
      });
    });
  }

  void boot();
}
