/** Chip tags → preview `q`, matching Host `FindQueryCompose` (AND spaces / OR keyword). */

export function normalizeQueryToken(raw: string | null | undefined): string | null {
  if (raw == null) return null;
  const trimmed = raw.trim();
  return trimmed.length >= 1 ? trimmed : null;
}

export function composePreviewQuery(tags: readonly string[], useOr: boolean): string | null {
  const parts = tags.map((tag) => tag.trim()).filter((tag) => tag.length > 0);
  if (parts.length === 0) return null;
  return useOr ? parts.join(" OR ") : parts.join(" ");
}

export function tryAddPreviewTag(tags: string[], raw: string | null | undefined): boolean {
  const alias = normalizeQueryToken(raw);
  if (alias == null) return false;
  if (tags.some((tag) => tag.toLowerCase() === alias.toLowerCase())) return false;
  tags.push(alias);
  return true;
}

/** Down from the query box: first if none, otherwise advance one and stay on the last. */
export function nextDownIndex(selectedIndex: number, count: number): number {
  if (count <= 0) return -1;
  if (selectedIndex < 0) return 0;
  return selectedIndex < count - 1 ? selectedIndex + 1 : selectedIndex;
}

/** Up from the query box: last if none, otherwise move up one and stay on the first. */
export function nextUpIndex(selectedIndex: number, count: number): number {
  if (count <= 0) return -1;
  if (selectedIndex < 0) return count - 1;
  return selectedIndex > 0 ? selectedIndex - 1 : selectedIndex;
}
