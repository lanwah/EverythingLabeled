import { isHttpPage } from "./uri";

/** Minimal tab fields used when resolving「跟随浏览器当前网页」. */
export type FollowTabCandidate = {
  id?: number;
  url?: string;
  title?: string;
  active?: boolean;
  lastAccessed?: number;
};

/**
 * Prefer the active tab when it is http(s); otherwise the most recently
 * accessed http(s) tab. Extension / non-http(s) URLs are never chosen.
 */
export function pickFollowTab(
  tabs: readonly FollowTabCandidate[],
): FollowTabCandidate | undefined {
  const active = tabs.find((t) => t.active);
  if (active?.url !== undefined && isHttpPage(active.url)) return active;

  let best: FollowTabCandidate | undefined;
  let bestAccessed = -1;
  for (const tab of tabs) {
    if (tab.url === undefined || !isHttpPage(tab.url)) continue;
    const accessed = tab.lastAccessed ?? 0;
    if (best === undefined || accessed >= bestAccessed) {
      best = tab;
      bestAccessed = accessed;
    }
  }
  return best;
}
