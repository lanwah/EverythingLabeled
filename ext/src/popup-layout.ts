/** Suggest row: padding 8+8 + ~20px line. */
export const SUGGEST_ROW_PX = 36;
/** `.suggest-list` vertical padding 4+4. */
export const SUGGEST_LIST_PAD_PX = 8;
/** Gap between chip/input bottom and suggest top. */
export const SUGGEST_GAP_PX = 2;
/** Preview hit row: padding 10+10 + title-row/sub ≈38 + margin 2+2. */
export const PREVIEW_HIT_ROW_PX = 62;
/** `.ext-preview-hits` border 1+1. */
export const PREVIEW_HITS_BORDER_PX = 2;
/** Chromium action popup hard cap. */
export const CHROME_POPUP_MAX_HEIGHT_PX = 600;

/** Pixel height so `rowCount` suggest rows show without scrolling the list. */
export function suggestListHeightPx(
  rowCount: number,
  rowHeightPx: number = SUGGEST_ROW_PX,
): number {
  const n = Math.max(0, Math.floor(rowCount));
  const row = rowHeightPx > 0 ? rowHeightPx : SUGGEST_ROW_PX;
  if (n === 0) return SUGGEST_LIST_PAD_PX;
  return SUGGEST_LIST_PAD_PX + n * row;
}

/**
 * Reserved in-flow height for the tag-search / preview hits panel.
 * Driven only by Options「预览条数」— never used to size the suggest dropdown.
 */
export function previewHitsHeightPx(
  rowCount: number,
  rowHeightPx: number = PREVIEW_HIT_ROW_PX,
): number {
  const n = Math.max(0, Math.floor(rowCount));
  const row = rowHeightPx > 0 ? rowHeightPx : PREVIEW_HIT_ROW_PX;
  if (n === 0) return PREVIEW_HITS_BORDER_PX;
  return PREVIEW_HITS_BORDER_PX + n * row;
}

/**
 * Available space under an anchor inside the popup viewport.
 * Used only for the suggest (拼首联想) dropdown — not for preview hits.
 */
export function spaceBelowPx(args: {
  viewportHeightPx: number;
  anchorBottomPx: number;
  gapPx?: number;
  padPx?: number;
}): number {
  const gap = args.gapPx ?? SUGGEST_GAP_PX;
  const pad = args.padPx ?? 2;
  return Math.max(
    0,
    Math.floor(args.viewportHeightPx - args.anchorBottomPx - gap - pad),
  );
}

/**
 * Popup body height: fit in-flow content (including config-reserved hits).
 * Cap at Chrome 600px so the window itself does not grow a scrollbar.
 */
export function popupHeightPx(contentHeightPx: number): number {
  return Math.min(
    CHROME_POPUP_MAX_HEIGHT_PX,
    Math.max(0, Math.ceil(contentHeightPx)),
  );
}

/**
 * Prefer config-reserved hits height. Only if chrome + ideal hits exceed
 * Chrome's 600px hard cap, shrink hits so the panel stays fully inside the
 * window (avoids mid-box clipping / popup scrollbar).
 */
export function hitsHeightForPopupCap(args: {
  idealHitsHeightPx: number;
  contentHeightWithIdealPx: number;
  maxPopupHeightPx?: number;
}): number {
  const maxH = args.maxPopupHeightPx ?? CHROME_POPUP_MAX_HEIGHT_PX;
  const content = Math.ceil(args.contentHeightWithIdealPx);
  if (content <= maxH) return args.idealHitsHeightPx;
  const chromeH = content - args.idealHitsHeightPx;
  const room = maxH - chromeH;
  const minHits = PREVIEW_HITS_BORDER_PX + PREVIEW_HIT_ROW_PX;
  return Math.max(minHits, Math.min(args.idealHitsHeightPx, room));
}
