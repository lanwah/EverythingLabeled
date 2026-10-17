export function tagChipLabel(tag: {
  name: string;
  namespace: string | null;
}): string {
  return tag.namespace ? `${tag.namespace}/${tag.name}` : tag.name;
}

/** Suggest row text: alias when it differs from the tag name, otherwise the name (with namespace). */
export function suggestRowLabel(item: {
  alias: string;
  name: string;
  namespace: string | null;
}): string {
  const head = tagChipLabel(item);
  return item.alias === item.name ? head : `${item.alias}  (${head})`;
}
