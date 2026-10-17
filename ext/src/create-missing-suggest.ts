/** Aligns with ExplorerHost `CreateMissingSuggest.ShouldCheck`. */

export function shouldCheckCreateMissing(
  normalizedAlias: string | null | undefined,
  suggestAliases: Iterable<string>,
): boolean {
  if (normalizedAlias == null || normalizedAlias === "") return false;

  for (const alias of suggestAliases) {
    if (alias.toLowerCase() === normalizedAlias.toLowerCase()) return false;
  }
  return true;
}
