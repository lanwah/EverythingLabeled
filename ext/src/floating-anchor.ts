export type FixedNearOpts = {
  gap?: number;
  maxHeight?: number;
  /** Viewport height for clamping; defaults to `window.innerHeight`. */
  viewportHeight?: number;
};

const DEFAULT_GAP = 2;
const DEFAULT_MAX_HEIGHT = 220;
const VIEWPORT_PAD = 2;

/**
 * Pin a layer with position:fixed next to an anchor (below when space allows,
 * otherwise above). Chrome action popups cannot paint outside their window —
 * fixed layers must stay inside the popup viewport.
 *
 * Prefer below whenever `spaceBelow` can fit `maxHeight`. Pass `maxHeight` equal
 * to the space below the anchor so the list fills that region and stays below.
 */
export function placeFixedNear(
  anchor: HTMLElement,
  floating: HTMLElement,
  opts: FixedNearOpts = {},
): void {
  const gap = opts.gap ?? DEFAULT_GAP;
  const maxHeight = opts.maxHeight ?? DEFAULT_MAX_HEIGHT;
  const viewportHeight = opts.viewportHeight ?? window.innerHeight;
  const rect = anchor.getBoundingClientRect();
  const spaceBelow = Math.max(0, viewportHeight - rect.bottom - gap - VIEWPORT_PAD);
  const spaceAbove = Math.max(0, rect.top - gap - VIEWPORT_PAD);
  const placeBelow = spaceBelow >= maxHeight || spaceBelow >= spaceAbove;
  const available = placeBelow ? spaceBelow : spaceAbove;
  const height = Math.min(maxHeight, Math.max(available, 48));

  floating.style.position = "fixed";
  floating.style.left = `${Math.round(rect.left)}px`;
  floating.style.width = `${Math.round(rect.width)}px`;
  floating.style.right = "auto";
  floating.style.maxHeight = `${height}px`;
  if (placeBelow) {
    floating.style.top = `${Math.round(rect.bottom + gap)}px`;
    floating.style.bottom = "auto";
  } else {
    floating.style.top = "auto";
    floating.style.bottom = `${Math.round(viewportHeight - rect.top + gap)}px`;
  }
}

/** @deprecated Use placeFixedNear — kept as alias for call-site clarity. */
export function placeFixedBelow(
  anchor: HTMLElement,
  floating: HTMLElement,
  opts: FixedNearOpts = {},
): void {
  placeFixedNear(anchor, floating, opts);
}

export function clearFixedPlacement(floating: HTMLElement): void {
  floating.style.position = "";
  floating.style.left = "";
  floating.style.top = "";
  floating.style.bottom = "";
  floating.style.width = "";
  floating.style.right = "";
  floating.style.maxHeight = "";
}
