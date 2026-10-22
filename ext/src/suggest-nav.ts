import { nextDownIndex, nextUpIndex } from "./find-query";

export type SuggestNavRow = { alias: string; label: string };

export type SuggestNavState = {
  rows: SuggestNavRow[];
  index: number;
};

export function emptySuggestNav(): SuggestNavState {
  return { rows: [], index: -1 };
}

/** Fresh list: select the first row when any exist. */
export function suggestNavFromRows(rows: SuggestNavRow[]): SuggestNavState {
  return { rows, index: rows.length > 0 ? 0 : -1 };
}

export function moveSuggestNav(state: SuggestNavState, down: boolean): SuggestNavState {
  if (state.rows.length === 0) return state;
  const index = down
    ? nextDownIndex(state.index, state.rows.length)
    : nextUpIndex(state.index, state.rows.length);
  return { rows: state.rows, index };
}

/** Alias for Enter: current highlight, else first row when the list is non-empty. */
export function selectedSuggestAlias(state: SuggestNavState): string | undefined {
  if (state.rows.length === 0) return undefined;
  return state.rows[state.index]?.alias ?? state.rows[0]?.alias;
}

export function renderSuggestList(
  listEl: HTMLUListElement,
  state: SuggestNavState,
  onPick: (alias: string) => void,
): void {
  listEl.replaceChildren();
  state.rows.forEach((row, index) => {
    const li = document.createElement("li");
    if (index === state.index) li.className = "selected";
    const btn = document.createElement("button");
    btn.type = "button";
    btn.textContent = row.label;
    btn.addEventListener("click", () => {
      onPick(row.alias);
    });
    li.append(btn);
    listEl.append(li);
  });
  scrollSelectedIntoContainer(listEl);
}

/** Scroll only inside `container` — avoid scrollIntoView moving the popup window. */
export function scrollSelectedIntoContainer(container: HTMLElement): void {
  const selected = container.querySelector("li.selected");
  if (!(selected instanceof HTMLElement)) return;
  const listRect = container.getBoundingClientRect();
  const rowRect = selected.getBoundingClientRect();
  if (rowRect.bottom > listRect.bottom) {
    container.scrollTop += rowRect.bottom - listRect.bottom;
  } else if (rowRect.top < listRect.top) {
    container.scrollTop -= listRect.top - rowRect.top;
  }
}
