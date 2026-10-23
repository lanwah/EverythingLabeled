import {
  normalizeStoredConfig,
  type ElExtensionConfig,
} from "./config";
import { copy } from "./copy";
import { createElApi } from "./el-api";
import { handleRequest, type ElRequest, type ElResponse } from "./messages";
import { openOrFocusExtensionPage } from "./open-extension-page";
import { isHttpPage } from "./uri";

const STORED_KEYS = [
  "baseUrl",
  "token",
  "confirmUntag",
  "previewLimit",
  "suggestLimit",
  "findTagLogic",
  "previewResourceType",
] as const;
const MENU_MANAGE_PAGE = "el-manage-page";
const MENU_LIBRARY = "el-library";
const MENU_RESOURCES = "el-resources";
const APP_PAGE = "app.html";
const LIBRARY_PAGE = "library.html";
const RESOURCES_PAGE = "resources.html";

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
    suggestLimit: cfg.suggestLimit,
    findTagLogic: cfg.findTagLogic,
    previewResourceType: cfg.previewResourceType,
  });
}

function failBadRequest(message: string): ElResponse {
  return { ok: false, code: "bad_request", message };
}

function isMessageType(
  message: unknown,
  type: string,
): message is { type: string } {
  return (
    typeof message === "object" &&
    message !== null &&
    "type" in message &&
    (message as { type: unknown }).type === type
  );
}

function isOpenTabsMessage(
  message: unknown,
): message is { type: "openTabs"; urls: unknown } {
  return isMessageType(message, "openTabs");
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
  // 同步 return true 保活端口；勿把 listener 写成 async（会提前关通道，UI 收到 null）。
  void dispatch(message, sender).then(
    (response) => {
      sendResponse(
        response ?? {
          ok: false,
          code: "internal_error",
          message: copy.backgroundNoResponse,
        },
      );
    },
    (err: unknown) => {
      const text = err instanceof Error ? err.message : copy.backgroundNoResponse;
      sendResponse({ ok: false, code: "internal_error", message: text } satisfies ElResponse);
    },
  );
  return true;
});

async function ensureContextMenu(): Promise<void> {
  await chrome.contextMenus.removeAll();
  chrome.contextMenus.create({
    id: MENU_MANAGE_PAGE,
    title: "EverythingLabeled：管理本页标签",
    contexts: ["page"],
    documentUrlPatterns: ["http://*/*", "https://*/*"],
  });
  chrome.contextMenus.create({
    id: MENU_LIBRARY,
    title: "EverythingLabeled：管理标签",
    contexts: ["page"],
  });
  chrome.contextMenus.create({
    id: MENU_RESOURCES,
    title: "EverythingLabeled：管理资源",
    contexts: ["page"],
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
  await openOrFocusExtensionPage(APP_PAGE);
}

function openLibrary(): Promise<void> {
  return openOrFocusExtensionPage(LIBRARY_PAGE);
}

function openResources(): Promise<void> {
  return openOrFocusExtensionPage(RESOURCES_PAGE);
}

chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId === MENU_LIBRARY) {
    void openLibrary();
    return;
  }
  if (info.menuItemId === MENU_RESOURCES) {
    void openResources();
    return;
  }
  void openAppForTab(tab);
});
