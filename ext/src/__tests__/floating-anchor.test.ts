import { describe, expect, it } from "vitest";
import { clearFixedPlacement, placeFixedNear } from "../floating-anchor";

function fakeEl(rect?: {
  left: number;
  top: number;
  right: number;
  bottom: number;
  width: number;
  height: number;
}): HTMLElement {
  const style: Record<string, string> = {};
  return {
    style,
    getBoundingClientRect: () => {
      if (rect == null) throw new Error("missing rect");
      return {
        ...rect,
        x: rect.left,
        y: rect.top,
        toJSON() {
          return {};
        },
      };
    },
  } as unknown as HTMLElement;
}

describe("placeFixedNear", () => {
  it("places below when there is enough room under the anchor", () => {
    const anchor = fakeEl({
      left: 10,
      top: 20,
      right: 210,
      bottom: 60,
      width: 200,
      height: 40,
    });
    const floating = fakeEl();

    placeFixedNear(anchor, floating, {
      gap: 2,
      maxHeight: 220,
      viewportHeight: 600,
    });

    expect(floating.style.position).toBe("fixed");
    expect(floating.style.left).toBe("10px");
    expect(floating.style.top).toBe("62px");
    expect(floating.style.bottom).toBe("auto");
    expect(floating.style.width).toBe("200px");
    expect(floating.style.maxHeight).toBe("220px");
  });

  it("places above when space below cannot fit maxHeight", () => {
    const anchor = fakeEl({
      left: 0,
      top: 500,
      right: 100,
      bottom: 540,
      width: 100,
      height: 40,
    });
    const floating = fakeEl();

    placeFixedNear(anchor, floating, {
      gap: 2,
      maxHeight: 220,
      viewportHeight: 600,
    });

    // spaceBelow ≈ 56 < 220, spaceAbove large → above; bottom = 600 - 500 + 2 = 102
    expect(floating.style.top).toBe("auto");
    expect(floating.style.bottom).toBe("102px");
    expect(floating.style.maxHeight).toBe("220px");
  });

  it("clearFixedPlacement removes inline placement styles", () => {
    const floating = fakeEl();
    floating.style.position = "fixed";
    floating.style.left = "1px";
    floating.style.top = "2px";
    floating.style.bottom = "3px";
    floating.style.width = "4px";
    floating.style.right = "auto";
    floating.style.maxHeight = "5px";

    clearFixedPlacement(floating);

    expect(floating.style.position).toBe("");
    expect(floating.style.left).toBe("");
    expect(floating.style.top).toBe("");
    expect(floating.style.bottom).toBe("");
    expect(floating.style.width).toBe("");
    expect(floating.style.right).toBe("");
    expect(floating.style.maxHeight).toBe("");
  });
});
