/** Open an extension HTML page in a tab, reusing an existing one when present. */
export async function openOrFocusExtensionPage(page: string): Promise<void> {
  const url = chrome.runtime.getURL(page);
  const existing = await chrome.tabs.query({ url });
  const found = existing[0];
  if (found?.id !== undefined) {
    await chrome.tabs.update(found.id, { active: true });
    if (found.windowId !== undefined) {
      await chrome.windows.update(found.windowId, { focused: true });
    }
    return;
  }
  await chrome.tabs.create({ url });
}
