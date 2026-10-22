import { describe, expect, it } from "vitest";
import {
  CHROME_POPUP_MAX_HEIGHT_PX,
  hitsHeightForPopupCap,
  PREVIEW_HIT_ROW_PX,
  PREVIEW_HITS_BORDER_PX,
  popupHeightPx,
  previewHitsHeightPx,
  spaceBelowPx,
  SUGGEST_GAP_PX,
  SUGGEST_LIST_PAD_PX,
  SUGGEST_ROW_PX,
  suggestListHeightPx,
} from "../popup-layout";

describe("suggestListHeightPx", () => {
  it("sizes for N full rows plus list padding", () => {
    expect(suggestListHeightPx(0)).toBe(SUGGEST_LIST_PAD_PX);
    expect(suggestListHeightPx(1)).toBe(SUGGEST_LIST_PAD_PX + SUGGEST_ROW_PX);
    expect(suggestListHeightPx(5)).toBe(SUGGEST_LIST_PAD_PX + 5 * SUGGEST_ROW_PX);
  });

  it("accepts a measured row height", () => {
    expect(suggestListHeightPx(5, 40)).toBe(SUGGEST_LIST_PAD_PX + 5 * 40);
  });
});

describe("previewHitsHeightPx", () => {
  it("sizes for N hit rows plus panel border (config-driven preview panel)", () => {
    expect(previewHitsHeightPx(0)).toBe(PREVIEW_HITS_BORDER_PX);
    expect(previewHitsHeightPx(1)).toBe(PREVIEW_HITS_BORDER_PX + PREVIEW_HIT_ROW_PX);
    expect(previewHitsHeightPx(3)).toBe(PREVIEW_HITS_BORDER_PX + 3 * PREVIEW_HIT_ROW_PX);
  });

  it("accepts a measured row height", () => {
    expect(previewHitsHeightPx(5, 60)).toBe(PREVIEW_HITS_BORDER_PX + 5 * 60);
  });
});

describe("spaceBelowPx", () => {
  it("returns remaining viewport under the anchor (suggest only)", () => {
    expect(
      spaceBelowPx({
        viewportHeightPx: 500,
        anchorBottomPx: 300,
        gapPx: SUGGEST_GAP_PX,
        padPx: 2,
      }),
    ).toBe(196); // 500 - 300 - 2 - 2
  });

  it("floors at zero when the anchor is past the viewport", () => {
    expect(
      spaceBelowPx({
        viewportHeightPx: 400,
        anchorBottomPx: 420,
      }),
    ).toBe(0);
  });
});

describe("popupHeightPx", () => {
  it("always uses Chrome popup max height", () => {
    expect(popupHeightPx(420)).toBe(CHROME_POPUP_MAX_HEIGHT_PX);
    expect(popupHeightPx(800)).toBe(CHROME_POPUP_MAX_HEIGHT_PX);
    expect(popupHeightPx()).toBe(CHROME_POPUP_MAX_HEIGHT_PX);
  });
});

describe("hitsHeightForPopupCap", () => {
  it("expands hits to fill remaining space under 600", () => {
    // chrome 332 + ideal 188 = 520 → room for hits = 268
    expect(
      hitsHeightForPopupCap({
        idealHitsHeightPx: 188,
        contentHeightWithIdealPx: 520,
      }),
    ).toBe(268);
  });

  it("shrinks hits when chrome + ideal exceed 600", () => {
    // chrome 350 + ideal 400 = 750 → room for hits = 250
    expect(
      hitsHeightForPopupCap({
        idealHitsHeightPx: 400,
        contentHeightWithIdealPx: 750,
      }),
    ).toBe(250);
  });

  it("keeps at least one hit row when chrome alone nearly fills 600", () => {
    // chrome 800 + ideal 200 = 1000 → room = 600-800 < 0 → clamp to one row
    expect(
      hitsHeightForPopupCap({
        idealHitsHeightPx: 200,
        contentHeightWithIdealPx: 1000,
      }),
    ).toBe(PREVIEW_HITS_BORDER_PX + PREVIEW_HIT_ROW_PX);
  });
});
