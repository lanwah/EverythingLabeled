export type ReplaceKind = "file" | "folder" | "url";

export function guessReplaceKind(raw: string | null | undefined): ReplaceKind | null {
  if (raw == null) return null;
  const text = raw.trim();
  if (text.length === 0) return null;
  if (/^https?:\/\//i.test(text)) return "url";
  return hasExtension(lastSegment(text)) ? "file" : "folder";
}

export function replaceKindLabel(kind: ReplaceKind): string {
  if (kind === "folder") return "文件夹";
  if (kind === "url") return "网页";
  return "文件";
}

export function replaceKindHint(
  raw: string | null | undefined,
  selected: ReplaceKind | null,
): string {
  const guessed = guessReplaceKind(raw);
  if (guessed == null) return "输入地址后会预选类型，仍可修改。";
  const reason =
    guessed === "url"
      ? "以 http(s) 开头，已预选网页。"
      : guessed === "file"
        ? "有扩展名，已预选文件。"
        : "无扩展名，已预选文件夹。";
  if (selected != null && selected !== guessed) {
    return `${reason}当前选择是${replaceKindLabel(selected)}。`;
  }
  return `${reason}识别不对可以改。`;
}

/** http(s) only pairs with 网页; a local path pairs with 文件 or 文件夹. */
export function kindCompatible(raw: string, kind: ReplaceKind): boolean {
  const guessed = guessReplaceKind(raw);
  if (guessed == null) return false;
  if (guessed === "url") return kind === "url";
  return kind === "file" || kind === "folder";
}

function lastSegment(text: string): string {
  let end = text.length;
  while (end > 0 && (text[end - 1] === "/" || text[end - 1] === "\\")) end -= 1;
  let start = end;
  while (start > 0 && text[start - 1] !== "/" && text[start - 1] !== "\\") start -= 1;
  return text.slice(start, end);
}

function hasExtension(name: string): boolean {
  const dot = name.lastIndexOf(".");
  return dot > 0 && dot < name.length - 1;
}
