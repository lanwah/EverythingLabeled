import { isOpenableUrl } from "./uri";

export type PreviewHit = {
  id: string;
  uri: string;
  kind: string;
  displayName: string | null;
};

export type OpenDecision =
  | { ok: true; urls: string[] }
  | { ok: false; reason: "none" | "over_limit" };

export function decideOpen(
  hits: PreviewHit[],
  selected: ReadonlySet<string>,
  previewLimit: number,
): OpenDecision {
  const urls = hits
    .filter((hit) => selected.has(hit.id) && isOpenableUrl(hit.kind, hit.uri))
    .map((hit) => hit.uri);
  if (urls.length === 0) return { ok: false, reason: "none" };
  if (urls.length > previewLimit) return { ok: false, reason: "over_limit" };
  return { ok: true, urls };
}

export function defaultSelectedIds(hits: PreviewHit[]): Set<string> {
  return new Set(
    hits.filter((hit) => isOpenableUrl(hit.kind, hit.uri)).map((hit) => hit.id),
  );
}
