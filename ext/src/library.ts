import { copy } from "./copy";
import { debounce } from "./debounce";
import type { TagInfo } from "./el-api";
import { sendToBackground } from "./runtime-message";
import { formatUiError } from "./ui-error";

function el<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  className?: string,
): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (className !== undefined) node.className = className;
  return node;
}

function renameFrom(tag: TagInfo): string {
  const match = tag.aliases.find((a) => a === tag.name);
  return match ?? tag.aliases[0] ?? tag.name;
}

function mountLibrary(root: HTMLElement): void {
  root.replaceChildren();
  root.className = "ext-shell";

  const head = el("header", "ext-head");
  const titleWrap = el("div");
  const heading = el("h1");
  heading.textContent = copy.libraryTitle;
  const lead = el("p", "ext-lead");
  lead.textContent = copy.libraryLead;
  titleWrap.append(heading, lead);
  const back = el("a", "btn");
  back.href = chrome.runtime.getURL("app.html");
  back.textContent = copy.libraryBack;
  head.append(titleWrap, back);

  const banner = el("p", "banner");
  banner.hidden = true;

  const search = document.createElement("input");
  search.type = "search";
  search.className = "library-search";
  search.placeholder = copy.librarySearchPlaceholder;
  search.setAttribute("aria-label", copy.librarySearchPlaceholder);

  const empty = el("p", "empty-hint library-empty");
  empty.textContent = copy.libraryEmpty;
  empty.hidden = true;

  const list = el("ul", "library-list");
  const footer = el("div", "footer");
  const status = el("p", "status");
  footer.append(status);

  const overlay = el("div", "library-dialog");
  overlay.hidden = true;
  const card = el("div", "library-dialog-card");
  const dialogTitle = el("h2");
  dialogTitle.textContent = copy.libraryDeleteTitle;
  const dialogBody = el("p");
  const dialogFooter = el("div", "footer");
  const cancelDelete = document.createElement("button");
  cancelDelete.type = "button";
  cancelDelete.className = "btn";
  cancelDelete.textContent = copy.libraryCancel;
  const confirmDelete = document.createElement("button");
  confirmDelete.type = "button";
  confirmDelete.className = "btn btn-danger";
  confirmDelete.textContent = copy.libraryDelete;
  dialogFooter.append(el("span", "status"), cancelDelete, confirmDelete);
  card.append(dialogTitle, dialogBody, dialogFooter);
  overlay.append(card);

  root.append(head, banner, search, empty, list, footer, overlay);

  let items: TagInfo[] = [];
  let pendingDelete: TagInfo | null = null;
  let editingId: string | null = null;
  /** Blocks rename/delete only; list reloads use loadGen so search is never dropped. */
  let mutating = false;
  let loadGen = 0;

  function showBanner(message: string | null): void {
    if (message == null || message === "") {
      banner.hidden = true;
      banner.textContent = "";
      return;
    }
    banner.hidden = false;
    banner.textContent = message;
  }

  function render(total?: number): void {
    list.replaceChildren();
    const emptyList = items.length === 0;
    const hasQuery = search.value.trim() !== "";
    empty.hidden = !emptyList;
    empty.textContent = hasQuery ? copy.libraryNoMatch : copy.libraryEmpty;
    list.hidden = emptyList;
    status.textContent = emptyList
      ? ""
      : copy.libraryCount(total ?? items.length);

    for (const tag of items) {
      const li = el("li");
      if (editingId === tag.id) {
        li.className = "library-edit";
        const row = el("div", "library-edit-row");
        const input = document.createElement("input");
        input.type = "text";
        input.value = tag.name;
        input.setAttribute("aria-label", copy.libraryRename);
        const cancel = document.createElement("button");
        cancel.type = "button";
        cancel.className = "btn btn-row";
        cancel.textContent = copy.libraryCancel;
        cancel.addEventListener("click", () => {
          editingId = null;
          render();
        });
        const save = document.createElement("button");
        save.type = "button";
        save.className = "btn btn-primary btn-row";
        save.textContent = copy.librarySave;
        const err = el("p", "library-error");
        err.hidden = true;
        save.addEventListener("click", () => {
          void (async () => {
            const next = input.value.trim();
            if (next === "") {
              err.hidden = false;
              err.textContent = copy.libraryEmptyName;
              return;
            }
            if (mutating) return;
            mutating = true;
            try {
              const result = await sendToBackground<TagInfo>({
                type: "renameTag",
                from: renameFrom(tag),
                to: next,
              });
              if (!result.ok) {
                err.hidden = false;
                err.textContent = formatUiError(result);
                return;
              }
              items = items.map((t) => (t.id === tag.id ? result.data : t));
              editingId = null;
              render();
            } finally {
              mutating = false;
            }
          })();
        });
        row.append(input, cancel, save);
        const hint = el("p", "library-hint");
        hint.textContent = copy.libraryRenameHint;
        li.append(row, hint, err);
      } else {
        const name = el("span", "library-name");
        name.textContent = tag.name;
        const alias = el("span", "library-alias");
        alias.textContent = copy.libraryExtraAliases(tag.aliases, tag.name);
        const actions = el("div", "actions-right");
        const renameBtn = document.createElement("button");
        renameBtn.type = "button";
        renameBtn.className = "btn btn-row";
        renameBtn.textContent = copy.libraryRename;
        renameBtn.addEventListener("click", () => {
          editingId = tag.id;
          render();
        });
        const deleteBtn = document.createElement("button");
        deleteBtn.type = "button";
        deleteBtn.className = "btn btn-row";
        deleteBtn.textContent = copy.libraryDelete;
        deleteBtn.addEventListener("click", () => {
          pendingDelete = tag;
          dialogBody.textContent = copy.libraryDeleteConfirm(tag.name);
          overlay.hidden = false;
        });
        actions.append(renameBtn, deleteBtn);
        li.append(name, alias, actions);
      }
      list.append(li);
    }
  }

  async function reload(): Promise<void> {
    const gen = ++loadGen;
    const q = search.value.trim();
    const result = await sendToBackground<{ items: TagInfo[]; total: number }>({
      type: "listTags",
      q: q === "" ? null : q,
    });
    if (gen !== loadGen) return;
    if (!result.ok) {
      showBanner(formatUiError(result));
      items = [];
      render();
      return;
    }
    showBanner(null);
    items = result.data.items ?? [];
    editingId = null;
    render(result.data.total);
  }

  cancelDelete.addEventListener("click", () => {
    pendingDelete = null;
    overlay.hidden = true;
  });

  confirmDelete.addEventListener("click", () => {
    void (async () => {
      if (pendingDelete == null || mutating) return;
      mutating = true;
      try {
        const result = await sendToBackground<{ dataVersion: number }>({
          type: "deleteTag",
          id: pendingDelete.id,
        });
        if (!result.ok) {
          showBanner(formatUiError(result));
          return;
        }
        items = items.filter((t) => t.id !== pendingDelete!.id);
        pendingDelete = null;
        overlay.hidden = true;
        render();
      } finally {
        mutating = false;
      }
    })();
  });

  search.addEventListener("input", debounce(() => {
    void reload();
  }, 200));

  render();
  void reload();
  search.focus();
}

const root = document.getElementById("app");
if (root !== null) mountLibrary(root);
