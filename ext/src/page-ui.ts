import { copy } from "./copy";
import {
  filterPreviewByResourceType,
  type ElExtensionConfig,
} from "./config";
import { shouldCheckCreateMissing } from "./create-missing-suggest";
import { debounce } from "./debounce";
import type { QueryHit, SuggestItem, TagInfo } from "./el-api";
import {
  composePreviewQuery,
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
  /** Popup: suggest stays fixed so it does not enlarge .ext-shell scrollHeight. */
  const useFixedSuggest = mode === "popup";

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

  const previewLabel = el("p", "section-label");
  previewLabel.textContent = "预览";
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
  previewHits.setAttribute("aria-label", "预览结果");
  const previewHint = el("p", "empty-hint ext-preview-empty");
  previewHint.textContent = copy.previewIdle;
  const hitsList = el("ul", "result-list");
  previewHits.append(previewHint, hitsList);
  previewBlock.append(queryRow, previewHits);
  const status = el("span", "status");
  const previewFooter = el("div", "footer");
  previewFooter.append(status);

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
    previewBlock,
    previewFooter,
  );

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

  function previewStatusText(): string {
    return `预览 ${previewCount} 条`;
  }

  function previewUseOr(): boolean {
    return config?.findTagLogic === "or";
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
      li.title = "双击打开";
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

  /**
   * 拼首联想 max-height：只看锚点下方实际可用高度（或预留命中区实测高度），
   * 不读 previewLimit / 不按「条数×行高」限行。
   */
  function suggestMaxHeightFor(anchor: HTMLElement): number {
    if (anchor === chipField) {
      const hitsH = previewHits.getBoundingClientRect().height;
      if (hitsH > 0) return Math.max(48, Math.floor(hitsH));
    }
    return Math.max(
      48,
      spaceBelowPx({
        viewportHeightPx: window.innerHeight,
        anchorBottomPx: anchor.getBoundingClientRect().bottom,
        gapPx: SUGGEST_GAP_PX,
      }),
    );
  }

  /**
   * 按标签检索列表：高度按 previewLimit 预留。
   * 打标 Popup：先让 body/shell 按内容撑开再测高，窗口贴合内容（≤600），避免底部裁切。
   */
  function applyPopupLayout(): void {
    const limit = config?.previewLimit ?? 8;
    const idealHitsH = previewHitsHeightPx(limit);
    if (!useFixedSuggest) {
      document.body.style.setProperty("--preview-hits-height", `${idealHitsH}px`);
      document.body.style.setProperty("--suggest-max-height", `${idealHitsH}px`);
      return;
    }

    // 解除定高，否则 scrollHeight 仍按旧窗口高度截断，底部显示不全。
    document.body.style.height = "auto";
    document.body.style.setProperty("--popup-height", "auto");
    root.style.height = "auto";
    root.style.paddingBottom = "";
    document.body.style.setProperty("--preview-hits-height", `${idealHitsH}px`);
    void root.offsetHeight;

    const contentWithIdeal = Math.max(
      root.scrollHeight,
      Math.ceil(root.getBoundingClientRect().height),
    );
    const hitsH = hitsHeightForPopupCap({
      idealHitsHeightPx: idealHitsH,
      contentHeightWithIdealPx: contentWithIdeal,
    });
    if (hitsH !== idealHitsH) {
      document.body.style.setProperty("--preview-hits-height", `${hitsH}px`);
      void root.offsetHeight;
    }

    const contentH = Math.max(
      root.scrollHeight,
      Math.ceil(root.getBoundingClientRect().height),
    );
    const height = popupHeightPx(contentH);
    document.body.style.height = `${height}px`;
    document.body.style.setProperty("--popup-height", `${height}px`);
    root.style.height = "";
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
    const result = await sendToBackground<QueryHit[]>({ type: "preview", q });
    if (requestId !== previewRequestId) return;
    if (!result.ok) {
      showFailure(result);
      return;
    }
    const type = config?.previewResourceType ?? "links";
    renderHits(filterPreviewByResourceType(result.data, type));
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
    setStatus(previewStatusText());
  }

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
      return;
    }
    config = cfg.data;
    if (config.token.trim() === "") showConfigMissingBanner();
    else banner.hidden = true;

    if (mode === "popup") {
      await loadPopupPage();
      applyPopupLayout();
      return;
    }

    applyPopupLayout();
    await loadAppPage();
    chrome.storage.session.onChanged.addListener((changes) => {
      if (!("pageSnapshot" in changes)) return;
      void loadAppPage();
    });
  }

  void boot();
}
