export function tagChipLabel(tag: {
  name: string;
  namespace: string | null;
}): string {
  return tag.namespace ? `${tag.namespace}/${tag.name}` : tag.name;
}
