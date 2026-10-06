import {
  normalizeStoredConfig,
  type ElExtensionConfig,
} from "./config";
import { copy } from "./copy";
import { createElApi } from "./el-api";
import { handleRequest, type ElRequest, type ElResponse } from "./messages";
import { isHttpPage } from "./uri";

const STORED_KEYS = ["baseUrl", "token", "confirmUntag", "previewLimit"] as const;
const MENU_ID = "el-manage";
const APP_PAGE = "app.html";

const api = createElApi({ fetch });

async function loadStored(): Promise<Record<string, unknown> | undefined> {
  return chrome.storage.local.get([...STORED_KEYS]);
}

async function saveStored(cfg: ElExtensionConfig): Promise<void> {
  await chrome.storage.local.set({
    baseUrl: cfg.baseUrl,
    token: cfg.token,
    confirmUntag: cfg.confirmUntag,
    previewLimit: cfg.previewLimit,
  });
}

function failBadRequest(message: string): ElResponse {
  return { ok: false, code: "bad_request", message };
}

function isOpenTabsMessage(
  message: unknown,
): message is { type: "openTabs"; urls: unknown } {
  return (
    typeof message === "object" &&
    message !== null &&
    "type" in message &&
    (message as { type: unknown }).type === "openTabs"
  );
}

async function resolveWindowId(
  sender: chrome.runtime.MessageSender,
): Promise<number | undefined> {
  const fromTab = sender.tab?.windowId;
  if (fromTab !== undefined) return fromTab;
  const focused = await chrome.windows.getLastFocused();
  return focused.id;
}

async function handleOpenTabs(
  urls: unknown,
  sender: chrome.runtime.MessageSender,
): Promise<ElResponse> {
  if (!Array.isArray(urls) || !urls.every((u) => typeof u === "string")) {
    return failBadRequest(copy.cannotOpen);
  }
  const cfg = normalizeStoredConfig(await loadStored());
  if (urls.length < 1 || urls.length > cfg.previewLimit) {
    return failBadRequest(copy.openOverLimit);
  }
  if (!urls.every((u) => isHttpPage(u))) {
    return failBadRequest(copy.cannotOpen);
  }
  const windowId = await resolveWindowId(sender);
  for (const url of urls) {
    await chrome.tabs.create({ url, windowId });
  }
  return { ok: true, data: null, message: null };
}

async function dispatch(
  message: unknown,
  sender: chrome.runtime.MessageSender,
): Promise<ElResponse> {
  if (isOpenTabsMessage(message)) {
    return handleOpenTabs(message.urls, sender);
  }
  return handleRequest(message as ElRequest, { api, loadStored, saveStored });
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  void dispatch(message, sender).then(sendResponse);
  return true;
});

async function ensureContextMenu(): Promise<void> {
  await chrome.contextMenus.removeAll();
  chrome.contextMenus.create({
    id: MENU_ID,
    title: "EverythingLabeled：管理本页标签",
    contexts: ["page"],
    documentUrlPatterns: ["http://*/*", "https://*/*"],
  });
}

chrome.runtime.onInstalled.addListener(() => {
  void ensureContextMenu();
});
chrome.runtime.onStartup.addListener(() => {
  void ensureContextMenu();
});

async function openAppForTab(tab: chrome.tabs.Tab | undefined): Promise<void> {
  const url = tab?.url;
  if (url === undefined || !isHttpPage(url)) return;
  await chrome.storage.session.set({
    pageSnapshot: { tabId: tab.id, url, title: tab.title },
  });
  const appUrl = chrome.runtime.getURL(APP_PAGE);
  const existing = await chrome.tabs.query({ url: appUrl });
  const found = existing[0];
  if (found?.id !== undefined) {
    await chrome.tabs.update(found.id, { active: true });
    if (found.windowId !== undefined) {
      await chrome.windows.update(found.windowId, { focused: true });
    }
    return;
  }
  await chrome.tabs.create({ url: appUrl });
}

chrome.contextMenus.onClicked.addListener((_info, tab) => {
  void openAppForTab(tab);
});
