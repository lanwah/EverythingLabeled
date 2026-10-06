export function stripFragment(url: string): string {
  const hash = url.indexOf("#");
  return hash === -1 ? url : url.slice(0, hash);
}

export function isHttpPage(url: string): boolean {
  try {
    const protocol = new URL(url).protocol.toLowerCase();
    return protocol === "http:" || protocol === "https:";
  } catch {
    return false;
  }
}

export function isOpenableUrl(kind: string, uri: string): boolean {
  return kind === "url" && isHttpPage(uri);
}
