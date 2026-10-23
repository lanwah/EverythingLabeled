import type { ElExtensionConfig } from "./config";
import { copy } from "./copy";
import { debounce } from "./debounce";
import type { QueryHit, SuggestItem } from "./el-api";
import {
  composePreviewQuery,
  normalizeQueryToken,
  SUGGEST_DEBOUNCE_MS,
  tryAddPreviewTag,
} from "./find-query";
import { suggestRowLabel } from "./labels";
import {
  guessReplaceKind,
  kindCompatible,
  replaceKindHint,
  replaceKindLabel,
  type ReplaceKind,
} from "./replace-kind";
import { sendToBackground } from "./runtime-message";
import {
  emptySuggestNav,
  moveSuggestNav,
  renderSuggestList,
  selectedSuggestAlias,
  suggestNavFromRows,
  type SuggestNavState,
} from "./suggest-nav";
import { formatUiError } from "./ui-error";

const ALIAS_BATCH = 100;
const KINDS: ReplaceKind[] = ["file", "folder", "url"];

function el<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  className?: string,
): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (className !== undefined) node.className = className;
  return node;
}

function displayTitle(hit: QueryHit): string {
  const name = hit.displayName;
  if (name != null && name.trim() !== "") return name.trim();
  const uri = hit.uri;
  const slash = Math.max(uri.lastIndexOf("/"), uri.lastIndexOf("\\"));
  const leaf = slash >= 0 ? uri.slice(slash + 1) : uri;
  return leaf === "" ? uri : leaf;
}

function kindClass(kind: string): string {
  if (kind === "folder") return "folder";
  if (kind === "url") return "webpage";
  return "file";
}

function kindWord(kind: string): string {
  if (kind === "folder" || kind === "url" || kind === "file") return replaceKindLabel(kind);
  return "文件";
}

function chunkAliases(aliases: readonly string[]): string[][] {
  if (aliases.length === 0) return [[]];
  const chunks: string[][] = [];
  for (let i = 0; i < aliases.length; i += ALIAS_BATCH) {
    chunks.push(aliases.slice(i, i + ALIAS_BATCH));
  }
  return chunks;
}

function mountResources(root: HTMLElement): void {
  root.replaceChildren();
  root.className = "ext-shell";

  const head = el("header", "ext-head");
  const titleWrap = el("div");
  const heading = el("h1");
  heading.textContent = copy.resourcesTitle;
  const lead = el("p", "ext-lead");
  lead.textContent = copy.resourcesLead;
  titleWrap.append(heading, lead);
  const back = el("a", "btn");
  back.href = chrome.runtime.getURL("app.html");
  back.textContent = copy.libraryBack;
  head.append(titleWrap, back);

  const banner = el("p", "banner");
  banner.hidden = true;

  const toolbar = el("div", "find-toolbar");
  const chipField = el("div", "chip-field resource-query");
  const chipWrap = el("div", "chip-wrap");
  const chipInputWrap = el("div", "chip-input");
  const chipInput = document.createElement("input");
  chipInput.type = "text";
  chipInput.autocomplete = "off";
  chipInput.placeholder = copy.findQueryPlaceholder;
  chipInput.setAttribute("aria-label", "标签查询");
  chipInput.setAttribute("aria-autocomplete", "list");
  chipInputWrap.append(chipInput);
  const clearBtn = el("button", "clear-all-tags");
  clearBtn.type = "button";
  clearBtn.textContent = "\uE894";
  clearBtn.title = copy.clearTags;
  clearBtn.hidden = true;
  const suggestList = el("ul", "suggest-list");
  chipField.append(chipWrap, suggestList);
  toolbar.append(chipField);

  const results = el("div", "resource-results");
  const empty = el("p", "empty-hint");
  empty.textContent = copy.resourcesEmpty;
  const list = el("ul", "result-list");
  results.append(empty, list);

  const status = el("span", "status");
  status.textContent = copy.resourcesNeedTags;
  const footer = el("div", "footer");
  footer.append(status);

  const deleteOverlay = el("div", "library-dialog");
  deleteOverlay.hidden = true;
  const deleteCard = el("div", "library-dialog-card");
  const deleteTitle = el("h2");
  deleteTitle.textContent = copy.resourcesDeleteTitle;
  const deleteBody = el("p");
  const deleteActions = el("div", "footer");
  const deleteCancel = el("button", "btn");
  deleteCancel.type = "button";
  deleteCancel.textContent = copy.libraryCancel;
  const deleteConfirm = el("button", "btn btn-danger");
  deleteConfirm.type = "button";
  deleteConfirm.textContent = copy.libraryDelete;
  deleteActions.append(el("span", "status"), deleteCancel, deleteConfirm);
  deleteCard.append(deleteTitle, deleteBody, deleteActions);
  deleteOverlay.append(deleteCard);

  const replaceOverlay = el("div", "library-dialog");
  replaceOverlay.hidden = true;
  const replaceCard = el("div", "library-dialog-card is-wide");
  const replaceTitle = el("h2");
  replaceTitle.textContent = copy.resourcesReplaceTitle;
  const replaceLead = el("p");
  const tagsLabel = el("span", "replace-label");
  tagsLabel.textContent = copy.resourcesReplaceTags;
  const replaceTags = el("div", "replace-tags");
  const addressLabel = el("label", "field");
  addressLabel.append(document.createTextNode(copy.resourcesAddress));
  const addressInput = document.createElement("input");
  addressInput.type = "text";
  addressInput.setAttribute("aria-label", copy.resourcesAddress);
  addressLabel.append(addressInput);
  const kindLabel = el("span", "replace-label");
  kindLabel.textContent = copy.resourcesKind;
  const kindChoice = el("div", "kind-choice");
  const kindButtons = new Map<ReplaceKind, HTMLButtonElement>();
  for (const kind of KINDS) {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = replaceKindLabel(kind);
    button.addEventListener("click", () => {
      selectedKind = kind;
      paintKind();
    });
    kindButtons.set(kind, button);
    kindChoice.append(button);
  }
  const kindHint = el("p", "library-hint");
  const replaceError = el("p", "library-error");
  replaceError.hidden = true;
  const replaceActions = el("div", "footer");
  const replaceCancel = el("button", "btn");
  replaceCancel.type = "button";
  replaceCancel.textContent = copy.libraryCancel;
  const replaceConfirm = el("button", "btn btn-primary");
  replaceConfirm.type = "button";
  replaceConfirm.textContent = copy.resourcesReplace;
  replaceActions.append(el("span", "status"), replaceCancel, replaceConfirm);
  replaceCard.append(
    replaceTitle,
    replaceLead,
    tagsLabel,
    replaceTags,
    addressLabel,
    kindLabel,
    kindChoice,
    kindHint,
    replaceError,
    replaceActions,
  );
  replaceOverlay.append(replaceCard);

  root.append(head, banner, toolbar, results, footer, deleteOverlay, replaceOverlay);

  let chips: string[] = [];
  let hits: QueryHit[] = [];
  let useOr = false;
  let pending: QueryHit | null = null;
  let selectedKind: ReplaceKind | null = null;
  let mutating = false;
  let loadGen = 0;
  let suggestRequestId = 0;
  let suggestNav: SuggestNavState = emptySuggestNav();

  function showBanner(text: string | null): void {
    banner.hidden = text == null;
    banner.textContent = text ?? "";
  }

  function paintKind(): void {
    for (const kind of KINDS) {
      const button = kindButtons.get(kind);
      if (button !== undefined) button.setAttribute("aria-pressed", kind === selectedKind ? "true" : "false");
    }
    kindHint.textContent = replaceKindHint(addressInput.value, selectedKind);
  }

  function renderChips(): void {
    chipWrap.replaceChildren();
    for (const alias of chips) {
      const chip = el("span", "chip");
      chip.append(document.createTextNode(alias));
      const remove = document.createElement("button");
      remove.type = "button";
      remove.textContent = "×";
      remove.setAttribute("aria-label", "移除");
      remove.addEventListener("click", () => {
        chips = chips.filter((item) => item !== alias);
        renderChips();
        void reload();
      });
      chip.append(remove);
      chipWrap.append(chip);
    }
    chipInput.placeholder = chips.length > 0 ? copy.findQueryContinue : copy.findQueryPlaceholder;
    chipWrap.append(chipInputWrap, clearBtn);
    clearBtn.hidden = chips.length === 0;
  }

  function paintSuggest(): void {
    renderSuggestList(suggestList, suggestNav, (alias) => {
      commitChip(alias);
    });
    updateResultsChrome();
  }

  function updateResultsChrome(): void {
    const draft = normalizeQueryToken(chipInput.value);
    const hasChips = chips.length > 0;
    const hasDraft = draft != null;
    const showHits = hasChips && hits.length > 0;
    const suggestMain = !hasChips && hasDraft && suggestNav.rows.length > 0;

    root.classList.toggle("resource-suggest-mode", suggestMain);
    results.classList.toggle("resource-show-hits", showHits);

    if (showHits) {
      empty.hidden = true;
      status.textContent = copy.previewCount(hits.length);
      return;
    }

    if (hasChips) {
      empty.hidden = false;
      empty.textContent = copy.resourcesNoMatch;
      status.textContent = copy.previewCount(0);
      return;
    }

    if (!hasDraft) {
      empty.hidden = false;
      empty.textContent = copy.resourcesEmpty;
      status.textContent = copy.resourcesNeedTags;
      return;
    }

    empty.hidden = true;
    status.textContent = copy.suggestCount(suggestNav.rows.length);
  }

  function commitChip(raw: string | null | undefined): void {
    if (!tryAddPreviewTag(chips, raw)) return;
    chipInput.value = "";
    suggestNav = emptySuggestNav();
    paintSuggest();
    renderChips();
    chipInput.focus();
    void reload();
  }

  function renderHits(): void {
    list.replaceChildren();
    const hasQuery = chips.length > 0;
    if (!hasQuery) {
      updateResultsChrome();
      return;
    }
    for (const hit of hits) {
      const li = document.createElement("li");
      li.className = "resource-row";
      const icon = el("span", `kind-icon ${kindClass(hit.kind)}`);
      icon.setAttribute("role", "img");
      const text = el("div", "result-text");
      const titleRow = el("div", "title-row");
      const name = el("span", "name");
      name.textContent = displayTitle(hit);
      const tags = el("span", "result-tags");
      for (const tag of hit.tags ?? []) {
        const capsule = el("span", "result-tag");
        capsule.textContent = tag;
        tags.append(capsule);
      }
      titleRow.append(name, tags);
      const sub = el("div", "sub");
      sub.textContent = `${kindWord(hit.kind)} · ${hit.uri}`;
      text.append(titleRow, sub);
      const actions = el("div", "actions-right");
      const replaceBtn = el("button", "btn btn-row");
      replaceBtn.type = "button";
      replaceBtn.textContent = copy.resourcesReplace;
      replaceBtn.addEventListener("click", () => openReplace(hit));
      const deleteBtn = el("button", "btn btn-row");
      deleteBtn.type = "button";
      deleteBtn.textContent = copy.libraryDelete;
      deleteBtn.addEventListener("click", () => openDelete(hit));
      actions.append(replaceBtn, deleteBtn);
      li.append(icon, text, actions);
      list.append(li);
    }
    updateResultsChrome();
  }

  function openDelete(hit: QueryHit): void {
    pending = hit;
    deleteBody.textContent = copy.resourceDeleteConfirm(displayTitle(hit), hit.uri);
    deleteOverlay.hidden = false;
  }

  function openReplace(hit: QueryHit): void {
    pending = hit;
    replaceLead.textContent = copy.resourceReplaceLead(displayTitle(hit));
    replaceTags.replaceChildren();
    for (const tag of hit.tags ?? []) {
      const capsule = el("span", "result-tag");
      capsule.textContent = tag;
      replaceTags.append(capsule);
    }
    addressInput.value = "";
    replaceError.hidden = true;
    selectedKind = null;
    paintKind();
    replaceOverlay.hidden = false;
    addressInput.focus();
  }

  async function reload(): Promise<void> {
    const gen = ++loadGen;
    const q = composePreviewQuery(chips, useOr);
    if (q == null) {
      hits = [];
      renderHits();
      return;
    }
    const result = await sendToBackground<QueryHit[]>({
      type: "preview",
      q,
      resourceType: "all",
    });
    if (gen !== loadGen) return;
    if (!result.ok) {
      showBanner(formatUiError(result));
      hits = [];
      renderHits();
      return;
    }
    showBanner(null);
    hits = result.data ?? [];
    renderHits();
  }

  const runSuggest = debounce(() => {
    void (async () => {
      const requestId = ++suggestRequestId;
      const q = normalizeQueryToken(chipInput.value);
      if (q == null) {
        suggestNav = emptySuggestNav();
        paintSuggest();
        return;
      }
      const result = await sendToBackground<SuggestItem[]>({ type: "suggest", q });
      if (requestId !== suggestRequestId) return;
      if (!result.ok) {
        suggestNav = emptySuggestNav();
        paintSuggest();
        return;
      }
      suggestNav = suggestNavFromRows(
        result.data.map((item) => ({
          alias: item.alias,
          label: suggestRowLabel(item),
        })),
      );
      paintSuggest();
    })();
  }, SUGGEST_DEBOUNCE_MS);

  let composing = false;
  chipInput.addEventListener("compositionstart", () => {
    composing = true;
  });
  chipInput.addEventListener("compositionend", () => {
    composing = false;
  });

  chipInput.addEventListener("input", () => {
    suggestRequestId += 1;
    if (normalizeQueryToken(chipInput.value) == null) {
      suggestNav = emptySuggestNav();
      paintSuggest();
    } else {
      updateResultsChrome();
    }
    runSuggest();
  });
  chipInput.addEventListener("keydown", (event) => {
    if (event.key === "Backspace" && chipInput.value === "" && chips.length > 0) {
      chips = chips.slice(0, -1);
      renderChips();
      void reload();
      event.preventDefault();
      return;
    }
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      if (suggestNav.rows.length === 0) return;
      suggestNav = moveSuggestNav(suggestNav, event.key === "ArrowDown");
      paintSuggest();
      event.preventDefault();
      return;
    }
    if (event.key !== "Enter") return;
    const draft = normalizeQueryToken(chipInput.value);
    const fromSuggest = selectedSuggestAlias(suggestNav);
    if (fromSuggest !== undefined && draft != null) {
      event.preventDefault();
      commitChip(fromSuggest);
      return;
    }
    if (event.isComposing || composing) return;
    event.preventDefault();
    commitChip(chipInput.value);
  });

  clearBtn.addEventListener("click", () => {
    chips = [];
    chipInput.value = "";
    suggestNav = emptySuggestNav();
    paintSuggest();
    renderChips();
    void reload();
    chipInput.focus();
  });

  deleteCancel.addEventListener("click", () => {
    pending = null;
    deleteOverlay.hidden = true;
  });

  deleteConfirm.addEventListener("click", () => {
    void (async () => {
      if (pending == null || mutating) return;
      mutating = true;
      try {
        const result = await sendToBackground<{ dataVersion: number }>({
          type: "deleteResource",
          id: pending.id,
        });
        if (!result.ok) {
          showBanner(formatUiError(result));
          return;
        }
        hits = hits.filter((hit) => hit.id !== pending?.id);
        pending = null;
        deleteOverlay.hidden = true;
        renderHits();
      } finally {
        mutating = false;
      }
    })();
  });

  addressInput.addEventListener("input", () => {
    selectedKind = guessReplaceKind(addressInput.value);
    replaceError.hidden = true;
    paintKind();
  });

  replaceCancel.addEventListener("click", () => {
    pending = null;
    replaceOverlay.hidden = true;
  });

  replaceConfirm.addEventListener("click", () => {
    void (async () => {
      if (pending == null || mutating) return;
      const address = addressInput.value.trim();
      if (address === "" || selectedKind == null) {
        replaceError.hidden = false;
        replaceError.textContent = copy.resourcesAddressRequired;
        return;
      }
      if (!kindCompatible(address, selectedKind)) {
        replaceError.hidden = false;
        replaceError.textContent = copy.resourcesKindMismatch;
        return;
      }
      mutating = true;
      try {
        const source = pending;
        let createdId: string | undefined;
        for (const aliases of chunkAliases(source.tags ?? [])) {
          const applied = await sendToBackground<{ resource?: { id?: string } }>({
            type: "applyTags",
            uri: address,
            kind: selectedKind,
            aliases,
          });
          if (!applied.ok) {
            replaceError.hidden = false;
            replaceError.textContent = formatUiError(applied);
            return;
          }
          createdId = applied.data.resource?.id;
        }
        if (createdId == null || createdId === source.id) {
          replaceError.hidden = false;
          replaceError.textContent = copy.resourcesSame;
          return;
        }
        const deleted = await sendToBackground<{ dataVersion: number }>({
          type: "deleteResource",
          id: source.id,
        });
        if (!deleted.ok) {
          replaceError.hidden = false;
          replaceError.textContent = copy.resourcesDeleteFailed + formatUiError(deleted);
          return;
        }
        pending = null;
        replaceOverlay.hidden = true;
        await reload();
      } finally {
        mutating = false;
      }
    })();
  });

  renderChips();
  renderHits();
  void (async () => {
    const cfg = await sendToBackground<ElExtensionConfig>({ type: "getConfig" });
    if (cfg.ok) useOr = cfg.data.findTagLogic === "or";
    chipInput.focus();
  })();
}

const root = document.getElementById("app");
if (root !== null) mountResources(root);
