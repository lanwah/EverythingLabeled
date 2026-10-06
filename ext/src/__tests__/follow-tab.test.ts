import { describe, expect, it } from "vitest";
import { pickFollowTab, type FollowTabCandidate } from "../follow-tab";

function tab(partial: FollowTabCandidate): FollowTabCandidate {
  return partial;
}

describe("pickFollowTab", () => {
  it("returns the active tab when it is http(s)", () => {
    const active = tab({
      id: 2,
      url: "https://ex.com/active",
      title: "Active",
      active: true,
      lastAccessed: 1,
    });
    const older = tab({
      id: 1,
      url: "https://ex.com/older",
      title: "Older",
      active: false,
      lastAccessed: 99,
    });
    expect(pickFollowTab([older, active])).toBe(active);
  });

  it("skips active chrome-extension tab and picks most recent http(s)", () => {
    const ext = tab({
      id: 3,
      url: "chrome-extension://abc/app.html",
      title: "App",
      active: true,
      lastAccessed: 100,
    });
    const recent = tab({
      id: 2,
      url: "https://ex.com/recent",
      title: "Recent",
      active: false,
      lastAccessed: 50,
    });
    const older = tab({
      id: 1,
      url: "http://ex.com/older",
      title: "Older",
      active: false,
      lastAccessed: 10,
    });
    expect(pickFollowTab([ext, older, recent])).toBe(recent);
  });

  it("returns undefined when no http(s) tab exists", () => {
    expect(
      pickFollowTab([
        tab({
          id: 1,
          url: "chrome-extension://abc/app.html",
          active: true,
        }),
        tab({ id: 2, url: "chrome://extensions", active: false }),
        tab({ id: 3, active: false }),
      ]),
    ).toBeUndefined();
  });

  it("ignores non-http schemes even when most recent", () => {
    const file = tab({
      id: 1,
      url: "file:///C:/tmp",
      active: false,
      lastAccessed: 200,
    });
    const http = tab({
      id: 2,
      url: "https://ex.com/",
      active: false,
      lastAccessed: 1,
    });
    expect(pickFollowTab([file, http])).toBe(http);
  });
});
