# #13 浏览器扩展 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付 Chrome/Edge 共用的 Manifest V3 薄客户端（Options + Popup + Service Worker + 右键 `app.html`），只经 SW `fetch` 调本机 Service HTTP + Bearer。

**Architecture:** `ext/` 独立 npm 包（不进 `.sln`）。纯 TS 模块负责配置校验、URI fragment、打开规则、信封 HTTP、消息处理；`chrome.*` 只出现在 SW 与页面壳。Popup 与 `app.html` 共用 `page-ui.ts`。页面不 `fetch` Service。

**Tech Stack:** TypeScript；Vite（手写多入口，无 crx 插件）；Vitest；vanilla HTML/CSS；`@types/chrome`。npm，Node >=20。无 React/Vue。

**Spec:** [plans/2026-09-27-browser-extension.md](./2026-09-27-browser-extension.md)。HTTP：[plans/2026-09-26-http-api.md](./2026-09-26-http-api.md)。审核：[confirmed/2026-10-06-browser-extension-impl-review.md](./confirmed/2026-10-06-browser-extension-impl-review.md)（Q1–Q28 均选推荐 A）。

## Global Constraints

- 目录 **`ext/`**；不进 sln；`dotnet test` 不跑扩展。
- `engines.node` **>=20**；包管理器 **npm**；提交 **`ext/package-lock.json`**；gitignore **`ext/node_modules/`**、**`ext/dist/`**。
- Manifest V3；`name` = `EverythingLabeled`；`version` = **0.1.0**。权限仅：`storage`、`tabs`、`activeTab`、`contextMenus`；`host_permissions` **仅** `http://127.0.0.1/*`。无 Content Script、无 `<all_urls>`、不申请 `localhost` / `::1`。
- UI **中文**；不做 `_locales`。API `message` 原文展示。文案 SSOT：`ext/src/copy.ts`。
- `baseUrl`：scheme 仅 `http`；host 仅 `127.0.0.1`；拒绝 `localhost` / `[::1]` / 其它 host；pathname 空或 `/`；拒绝 query/hash；端口可省略。缺省预填 `http://127.0.0.1:5123`。规范化：无 path、无尾 `/`；默认端口 80 时为 `http://127.0.0.1`，否则 `http://127.0.0.1:<port>`。
- storage 键：`baseUrl`、`token`、`confirmUntag`、`previewLimit`。`confirmUntag` 缺省键或省略 = **`true`**。`previewLimit` 缺省 **8**，合法 **1–16**（含）；读到非法数字 → 当 8。token 空则业务操作前引导 Options。
- HTTP：仅 SW `fetch`；`Authorization: Bearer <token>`（health 除外）；`Accept: application/json`；整请求超时 **30s**。相对路径 `v1/...`（无前导 `/`），拼到规范化 `baseUrl + "/"`。
- 信封与 C# `HttpElApi` 同构：`status` `ok`/`error`；错误 `data.code`；超时/连不上 → `connection_failed`（文案 `Unable to reach the service.`）。
- 探测：仅 Options 按钮。先 `GET /v1/health`（可无 token）→ 失败提示服务未运行或地址错误；表单 token 非空再 `GET /v1/meta` → 失败提示 token 无效。探测用 **当前表单值**，不先保存。Popup/app **不**开时自动探测。
- Popup/app 连接态：无 token → `config_missing` 文案链 Options；业务 `connection_failed` → 服务未运行或地址错误；HTTP **401** 或信封鉴权失败 → token 无效。
- 业务：token 空 → `config_missing`（不 fetch）。已存 `baseUrl` canonicalize 失败 → `bad_request` + 地址文案（**不是** `config_missing`）。
- 当前页仅 `http://` / `https://` 可打标。送给 API 的 uri：**只去掉 `#fragment`**，保留 query。打标 `kind: "url"`。
- `GET /v1/resources/tags`：`limit=1024` 翻页直到 `offset >= total`。**仅** `code === "resource_not_found"` → 空列表且 `resourceExists: false`，不报错。其它错误 **整次失败**（已拼页丢弃）。
- `saveStored` **只写**四键 `baseUrl`/`token`/`confirmUntag`/`previewLimit`。
- `tabs.create` **只在 SW**（消息 `openTabs`）；页面 `decideOpen` 后发送。SW 再校验条数 1…`previewLimit`、每条 `isHttpPage`。`windowId`：`sender.tab` 有则用其 window，否则 `chrome.windows.getLastFocused`。`openTabs` 不进纯函数 `handleRequest`。
- 三个 html 只留 `#app` + `styles.css` + 入口脚本；DOM 全由 TS 创建。
- Popup **打开时**读一次 active tab，不跟随之后的标签切换。`app.html` 订阅 `chrome.storage.session.onChanged`（`pageSnapshot`）并刷新。
- preview：`q` trim 空 → 不 fetch，成功空列表。
- 构建：**两次** Vite（`vite.config.ts` 三个 html；`vite.background.ts` library 单文件 `dist/background.js`，`inlineDynamicImports: true`）。禁止 SW 依赖 hashed chunk。
- 打标：每次一个别名；`createMissing` 来自勾选（不写 storage；每次打开 UI 默认不勾；本页会话保留）。仅 `resourceExists===false` 且 `title` trim 非空时 POST 带 `displayName`。去标永不 `createMissing: true`。芯片删除 `remove: [{ id }]`；输入去标 `remove: ["alias"]`。`confirmUntag != false` → `window.confirm`，取消不发请求。
- suggest：`q` trim 长度 ≥1；**250ms** 防抖；`limit=10`；失败静默空列表。预览：`q` 原样；`limit=previewLimit`；不传 `expand`。
- 打开：`kind === "url"` 且 uri 为 http(s) 才可打开。可打开项默认勾选；不可打开展示、勾选禁用。勾选中可打开数为 0 → 拒绝；可打开且已勾选 `> previewLimit` → **整次拒绝**。通过后只打开可打开项。一律经 SW `openTabs` → `tabs.create`，当前窗口依次创建。
- `app.html` 当前页 = `chrome.storage.session` 的 `pageSnapshot`（`tabId?`、`url`、`title`）。右键写入后聚焦已有 `app.html` tab 或新开。不把 token 写入 URL。
- 不做：改 C# Client、Firefox、Native Messaging、上架、Playwright 门禁、React。
- commit subject：仓库递增 `#序号`（`git log -1 --format=%s` +1），**不要** backlog `#13`；不用 conventional commits。PowerShell here-string 传 `-m`。

---

## File structure

```text
ext/
  package.json
  package-lock.json
  tsconfig.json
  vitest.config.ts
  vite.config.ts
  vite.background.ts
  manifest.json
  public/icons/icon16.png  icon32.png  icon48.png  icon128.png
  src/
    copy.ts
    config.ts
    uri.ts
    open-rules.ts
    labels.ts
    el-api.ts
    messages.ts
    background.ts
    options.ts
    options.html
    popup.ts
    popup.html
    app.ts
    app.html
    page-ui.ts
    styles.css
    debounce.ts
  src/__tests__/
    config.test.ts
    uri.test.ts
    open-rules.test.ts
    labels.test.ts
    el-api.test.ts
    messages.test.ts
.gitignore                          # + ext/node_modules/ ext/dist/
docs/browser-extension.md
docs/README.md
plans/2026-09-27-browser-extension.md  # §8 #11 → #13
```

---

### Task 1: npm 骨架 + 配置校验/缺省

**Files:**

- Create: `ext/package.json`、`ext/tsconfig.json`、`ext/vitest.config.ts`
- Create: `ext/src/copy.ts`、`ext/src/config.ts`
- Create: `ext/src/__tests__/config.test.ts`
- Modify: `.gitignore`

**Interfaces:**

- Consumes: 无
- Produces:

```ts
// ext/src/config.ts
export const DEFAULT_BASE_URL = "http://127.0.0.1:5123";
export const DEFAULT_PREVIEW_LIMIT = 8;
export const MIN_PREVIEW_LIMIT = 1;
export const MAX_PREVIEW_LIMIT = 16;

export type ElExtensionConfig = {
  baseUrl: string;
  token: string;
  confirmUntag: boolean;
  previewLimit: number;
};

export type ConfigParse = { ok: true; value: ElExtensionConfig } | { ok: false; message: string };

export function canonicalizeBaseUrl(raw: string): { ok: true; value: string } | { ok: false; message: string };
export function parsePreviewLimit(raw: unknown): number; // 非法 → 8
export function normalizeStoredConfig(stored: Record<string, unknown> | undefined): ElExtensionConfig;
export function validateDraft(draft: {
  baseUrl: string;
  token: string;
  confirmUntag: boolean;
  previewLimit: unknown;
}): ConfigParse;
```

`canonicalizeBaseUrl`：trim；`new URL`；`protocol === "http:"`；`hostname === "127.0.0.1"`；无 user/password；`pathname` 为 `""` 或 `"/"`；`search` 与 `hash` 为空。端口空或 80 → `http://127.0.0.1`，否则 `http://127.0.0.1:<port>`。失败 message 用 `copy.ts`（如「地址必须是 http://127.0.0.1，不能带路径或查询」）。

`normalizeStoredConfig`：缺 `baseUrl` → `DEFAULT_BASE_URL`；`token` 缺或非 string → `""`；`confirmUntag` 缺省/`undefined` → `true`（仅当明确 `false` 才 false）；`previewLimit` 走 `parsePreviewLimit`。

`validateDraft`：先 `canonicalizeBaseUrl`；`previewLimit` 为整数 1–16（字符串数字可 parse）；token 允许空（保存可以，业务再拦）。

`copy.ts` 至少包含规格用句：当前页不可打标、服务未运行或地址错误、token 无效、未配置 token、查询 0 条不必报错（空列表）、打开 0 可打开、打开超过硬顶、`tag_not_found` 未勾选创建时的明确句。

`package.json`：

```json
{
  "name": "everything-labeled-extension",
  "private": true,
  "version": "0.1.0",
  "type": "module",
  "engines": { "node": ">=20" },
  "scripts": {
    "test": "vitest run",
    "build": "vite build"
  },
  "devDependencies": {
    "@types/chrome": "^0.0.287",
    "typescript": "~5.6.3",
    "vite": "^5.4.10",
    "vitest": "^2.1.4"
  }
}
```

版本允许 npm 解析到同时期兼容 semver；不要上 React。本任务 **先不写** `vite.config.ts`（`build` 脚本在 Task 5 改成两次 Vite；此处 `npm test` 必须能跑）。`tsconfig`：`strict` true，`module`/`moduleResolution` 适合 Vite+Vitest。

`.gitignore` 追加：

```
ext/node_modules/
ext/dist/
```

- [ ] **Step 1: 写失败测试**

`config.test.ts` 至少：

- `localhost` / `[::1]` / `https://127.0.0.1:5123` / `http://127.0.0.1/foo` / `http://127.0.0.1:5123?x=1` / `#h` → not ok
- `http://127.0.0.1:5123` 与 `http://127.0.0.1:5123/` → 同一 `http://127.0.0.1:5123`
- `http://127.0.0.1` 与 `:80` → `http://127.0.0.1`
- `normalizeStoredConfig(undefined)` → 缺省 5123 / `""` / `true` / `8`
- `{ confirmUntag: false, previewLimit: 16 }` 保留
- `{ previewLimit: 0 }` 或 `"99"` → previewLimit 8
- `validateDraft` previewLimit `0` 失败；`8` 成功

- [ ] **Step 2: 跑测试确认失败**

Run: `cd ext && npm install && npm test`

Expected: FAIL（模块不存在）或先建空文件后断言失败。

- [ ] **Step 3: 最小实现**

实现 `copy.ts` / `config.ts`；`npm install` 生成 lockfile（**提交 lockfile**）。

- [ ] **Step 4: 跑测试确认通过**

Run: `cd ext && npm test`

Expected: PASS

- [ ] **Step 5: Commit**

```powershell
git add ext .gitignore
git commit -m "#N 扩展配置校验与 Vitest 骨架"
```

---

### Task 2: URI、打开规则、芯片文案

**Files:**

- Create: `ext/src/uri.ts`、`ext/src/open-rules.ts`、`ext/src/labels.ts`
- Create: `ext/src/__tests__/uri.test.ts`、`open-rules.test.ts`、`labels.test.ts`

**Interfaces:**

```ts
// uri.ts
export function stripFragment(url: string): string; // 去掉 # 及其后；无 # 原样；保留 query
export function isHttpPage(url: string): boolean; // 仅 http: 或 https:（大小写不敏感 scheme）
export function isOpenableUrl(kind: string, uri: string): boolean; // kind==="url" && isHttpPage(uri)

// open-rules.ts
export type PreviewHit = { id: string; uri: string; kind: string; displayName: string | null };

export type OpenDecision =
  | { ok: true; urls: string[] }
  | { ok: false; reason: "none" | "over_limit" };

export function decideOpen(
  hits: PreviewHit[],
  selected: ReadonlySet<string>, // hit.id
  previewLimit: number,
): OpenDecision;
// 只统计 selected 且 isOpenableUrl 的条数。0 → none；> previewLimit → over_limit；
// 否则 urls = 这些项的 uri（顺序与 hits 一致）。不截断。

export function defaultSelectedIds(hits: PreviewHit[]): Set<string>;
// 可打开的 id；不可打开不进 set

// labels.ts
export function tagChipLabel(tag: { name: string; namespace: string | null }): string;
// namespace 非空 → `${namespace}/${name}`，否则 name
```

- [ ] **Step 1: 写失败测试**

- `https://ex.com/a?x=1#frag` → `https://ex.com/a?x=1`；`chrome://extensions` → `isHttpPage` false
- 混选：2 个 http url + 1 file，selected 全选，limit 8 → ok 两个 http；只选 file → none；选 3 个 http 且 limit 2 → over_limit
- `defaultSelectedIds` 只含可打开
- namespace `work` + name `inbox` → `work/inbox`

- [ ] **Step 2: 跑测试确认失败**

Run: `cd ext && npm test`

Expected: FAIL

- [ ] **Step 3: 最小实现**

- [ ] **Step 4: 跑测试确认通过**

Run: `cd ext && npm test`

Expected: PASS

- [ ] **Step 5: Commit**

```powershell
git add ext/src
git commit -m "#N 扩展 URI 与打开硬顶规则"
```

---

### Task 3: TS HTTP 客户端（可注入 fetch）

**Files:**

- Create: `ext/src/el-api.ts`
- Create: `ext/src/__tests__/el-api.test.ts`

**Interfaces:**

```ts
export type ElError = { code: string; message: string; httpStatus?: number };
export type ElResult<T> = { ok: true; data: T; message: string | null } | { ok: false; error: ElError };

export type TagInfo = {
  id: string;
  name: string;
  namespace: string | null;
  aliases: string[];
};

export type QueryHit = { id: string; uri: string; kind: string; displayName: string | null };

export type SuggestItem = { tagId: string; alias: string; name: string; namespace: string | null };

export type PageTags = { items: TagInfo[]; resourceExists: boolean };

export function buildTagBody(input: {
  uri: string;
  alias: string;
  createMissing: boolean;
  resourceExists: boolean;
  title: string;
}): Record<string, unknown>;
// 恒有 uri, kind:"url", add:[alias], createMissing
// 仅 !resourceExists && title.trim() !== "" 时加 displayName: title.trim()
// 不设 displayName: null 字段（省略）

export function createElApi(deps: {
  fetch: typeof fetch;
}): {
  health(baseUrl: string): Promise<ElResult<{ health: string; dataVersion: number }>>;
  meta(baseUrl: string, token: string): Promise<ElResult<{ dataVersion: number }>>;
  getTags(baseUrl: string, token: string, uri: string): Promise<ElResult<PageTags>>;
  tag(baseUrl: string, token: string, body: Record<string, unknown>): Promise<ElResult<unknown>>;
  untag(
    baseUrl: string,
    token: string,
    uri: string,
    remove: Array<string | { id: string }>,
  ): Promise<ElResult<unknown>>;
  suggest(baseUrl: string, token: string, q: string): Promise<ElResult<SuggestItem[]>>;
  preview(baseUrl: string, token: string, q: string, limit: number): Promise<ElResult<QueryHit[]>>;
};
```

实现要点：

- `AbortController` + `setTimeout` 30_000；结束时 `clearTimeout`。abort/网络失败 → `connection_failed` / `Unable to reach the service.`
- `health`：`GET ${baseUrl}/v1/health`，**不**带 Authorization。
- 其它：`Authorization: Bearer ${token}`，`Accept: application/json`。
- POST JSON `Content-Type: application/json`。
- 解析信封：HTTP 2xx 且 `status==="ok"` → data；否则 `code` 从 `data.code`，message 从信封 `message`，带 `httpStatus`。
- `getTags`：循环 `offset` + `limit=1024`，`uri` 用 `encodeURIComponent`。若某页 `ok:false` 且 `code==="resource_not_found"` → **立刻** `{ ok:true, data:{ items:[], resourceExists:false }, message:null }`（仅此码；其它错误整次失败，已拼页丢弃）。成功则 `resourceExists: true`，items 拼接。
- `untag` body：`{ uri, remove, createMissing: false }`（无 kind）。
- `suggest`：`v1/tags/suggest?q=&limit=10`，返回 `data.items`（缺省 `[]`）。
- `preview`：`v1/query/preview?q=&limit=`，返回 `data.items`。

测试用假 `fetch` 记录 URL/headers/body，不打真网。

- [ ] **Step 1: 写失败测试**

至少：health 无 Authorization；meta 有 Bearer；tags 404 resource_not_found → 空且 `resourceExists:false`；tags 两页翻页；`buildTagBody` 存在资源不带 displayName、不存在带 title；untag body `createMissing:false`；fetch reject → connection_failed。

- [ ] **Step 2–4:** `cd ext && npm test` 先红后绿。

- [ ] **Step 5: Commit**

```powershell
git add ext/src
git commit -m "#N 扩展 HTTP 信封客户端"
```

---

### Task 4: 消息协议（纯函数，假 storage）

**Files:**

- Create: `ext/src/messages.ts`、`ext/src/debounce.ts`
- Create: `ext/src/__tests__/messages.test.ts`

**Interfaces:**

```ts
export type ElRequest =
  | { type: "probe"; baseUrl: string; token: string }
  | { type: "getConfig" }
  | { type: "saveConfig"; draft: { baseUrl: string; token: string; confirmUntag: boolean; previewLimit: unknown } }
  | { type: "getTags"; uri: string }
  | { type: "tag"; uri: string; alias: string; createMissing: boolean; resourceExists: boolean; title: string }
  | { type: "untagById"; uri: string; tagId: string }
  | { type: "untagByAlias"; uri: string; alias: string }
  | { type: "suggest"; q: string }
  | { type: "preview"; q: string };

export type ElResponse<T = unknown> =
  | { ok: true; data: T; message: string | null }
  | { ok: false; code: string; message: string; httpStatus?: number };

export function debounce<T extends (...args: never[]) => void>(fn: T, ms: number): T;
// 250ms 由调用方传入；本函数可单测假时钟或只测「后一次生效」

export async function handleRequest(
  req: ElRequest,
  deps: {
    api: ReturnType<typeof createElApi>;
    loadStored: () => Promise<Record<string, unknown> | undefined>;
    saveStored: (cfg: ElExtensionConfig) => Promise<void>;
  },
): Promise<ElResponse>;
```

规则：

- `probe`：`canonicalizeBaseUrl(req.baseUrl)` 失败 → `bad_request` + 校验文案，不 fetch。成功则 `health`；失败 → `ok:false`，message 用「服务未运行或地址错误」（可附带 API message）。health 成功且 token trim 非空 → `meta`；meta 失败 → token 无效文案。health 成功且无 token → `ok:true` data `{ health, tokenChecked:false }`。
- `getConfig`：`normalizeStoredConfig`。
- `saveConfig`：`validateDraft` 失败 → `bad_request`；成功 `saveStored`（只写四键）。
- 业务（getTags/tag/untag/suggest/preview）：token 空 → `config_missing`，**不 fetch**。`canonicalizeBaseUrl` 失败 → `bad_request`，**不 fetch**。uri 先 `stripFragment`。
- `suggest`：`q.trim()` 空 → `ok:true, data:[]` 不 fetch。API 失败 → 仍 `ok:true, data:[]`（静默）。
- `preview`：`q.trim()` 空 → `ok:true, data:[]` 不 fetch（与 0 条同一空态）。API 失败按信封返回（不静默）。
- `tag`：`alias.trim()` 空 → `bad_request`。`createMissing` 按请求布尔。`tag_not_found` 原样返回（UI 用 copy 强调未勾选创建）。
- `untag*`：本层 **不**做 confirm（页面 `window.confirm` 之后才发消息）。

- [ ] **Step 1: 写失败测试**

probe 非法 localhost 不调用 fetch；无 token 只 health；有 token health+meta；业务无 token → config_missing；非法已存 baseUrl → bad_request 不 fetch；suggest 失败变空数组；preview 空 q 不 fetch；getTags 走 stripFragment。

- [ ] **Step 2–4:** 红绿。

- [ ] **Step 5: Commit**

```powershell
git add ext/src
git commit -m "#N 扩展 SW 消息闸门"
```

---

### Task 5: Vite 多入口、manifest、SW 壳、占位图标

**Files:**

- Create: `ext/vite.config.ts`、`ext/vite.background.ts`、`ext/manifest.json`
- Create: `ext/public/icons/icon16.png`（及 32/48/128，可同一文件复制）
- Create: `ext/src/background.ts`
- Create: `ext/src/popup.html`、`options.html`、`app.html`（可先空壳 + script）
- Create: `ext/src/styles.css`（最小：可读表单/列表，无框架）
- Create: `ext/src/popup.ts`、`options.ts`、`app.ts`（本任务可只 `console`/`document.body` 挂载点，完整 UI 在 Task 6）

**Interfaces:**

- Consumes: `handleRequest`、`createElApi`、`normalizeStoredConfig`
- Produces: `npm run build` 后 `ext/dist/` 可被 Chrome「加载已解压的扩展程序」选中

`manifest.json`（源文件；build 拷到 dist 根并改写 js/html 路径使之与 Vite 产物一致）：

```json
{
  "manifest_version": 3,
  "name": "EverythingLabeled",
  "version": "0.1.0",
  "description": "给当前网页打标签，按标签打开本机已标网页。",
  "action": {
    "default_popup": "popup.html",
    "default_icon": {
      "16": "icons/icon16.png",
      "32": "icons/icon32.png",
      "48": "icons/icon48.png",
      "128": "icons/icon128.png"
    }
  },
  "options_page": "options.html",
  "background": { "service_worker": "background.js", "type": "module" },
  "permissions": ["storage", "tabs", "activeTab", "contextMenus"],
  "host_permissions": ["http://127.0.0.1/*"],
  "icons": {
    "16": "icons/icon16.png",
    "32": "icons/icon32.png",
    "48": "icons/icon48.png",
    "128": "icons/icon128.png"
  }
}
```

两次构建（Q27）：

1. `vite.config.ts`：`root` = `ext`；`build.outDir` = `dist`；`emptyOutDir` **true**（先跑）。`rollupOptions.input` **仅**三个 html（`popup` / `options` / `app`）。html 产物 flatten 到 dist 根。拷贝 `public/icons` 与 `manifest.json`。
2. `vite.background.ts`：library 入口 `src/background.ts`，`formats: ["es"]`，`fileName: () => "background.js"`，`inlineDynamicImports: true`，`emptyOutDir` **false**，写出 `dist/background.js`（单文件，无 hashed chunk）。

`package.json`：`"build": "vite build && vite build --config vite.background.ts"`。

**不要** `@crxjs/vite-plugin`。不要把 background 放进第一次多 html 入口。

占位 PNG：单色（如 `#1e3a5f`）即可；可用 1×1 或 128×128 复制四档。允许无「EL」字（Q18 允许共用一张图缩放）。不要依赖系统 `System.Drawing`。

`background.ts`：

1. `createElApi({ fetch })` 一次。
2. `chrome.runtime.onMessage`：若 `type==="openTabs"`，**不要**进 `handleRequest`。校验 `urls` 为数组、长度 1…当前 `previewLimit`（`normalizeStoredConfig`）、每条 `isHttpPage`；失败回 `bad_request`。`windowId`：`sender.tab?.windowId`，否则 `chrome.windows.getLastFocused` 的 id。对该 window **依次** `tabs.create({ url, windowId })`。其余 type → `handleRequest`。`loadStored` = `chrome.storage.local.get(["baseUrl","token","confirmUntag","previewLimit"])`。`saveStored` = `set` **仅**这四键。
3. `onInstalled`/`onStartup`：`contextMenus.removeAll` 后 `create`：id `el-manage`，title `EverythingLabeled：管理本页标签`，contexts `["page"]`，`documentUrlPatterns: ["http://*/*", "https://*/*"]`。
4. `onClicked`：读 `tab.url`/`tab.title`/`tab.id`；`chrome.storage.session.set({ pageSnapshot: { tabId, url, title } })`；`chrome.tabs.query({ url: chrome.runtime.getURL("app.html") })`（及 `app.html?*` 若 flatten 后无 query 则精确 url）；已有 → `tabs.update` focused + `windows.update` focused；否则 `tabs.create({ url: getURL("app.html") })`。
5. 菜单点击时若 url 不可 http(s)，直接 return（patterns 已挡一层）。

本任务验收：`cd ext && npm run build` 退出 0；`dist/manifest.json` 存在且 `background.service_worker` 指向真实文件；`npm test` 仍绿。

- [ ] **Step 1:** 无单测也可；用 `npm run build` 当验收。若 flatten html 失败，先写失败再改 config。

- [ ] **Step 2: 实现并 build**

- [ ] **Step 3: Commit**

```powershell
git add ext
git commit -m "#N 扩展 MV3 构建与 Service Worker 壳"
```

---

### Task 6: Options + Popup/`app.html` 共用 UI

**Files:**

- Modify: `ext/src/options.ts`、`options.html`、`popup.ts`、`popup.html`、`app.ts`、`app.html`、`styles.css`
- Create: `ext/src/page-ui.ts`
- 纯 UI 无强制新测；确认/去标逻辑已在 messages。可选：给 `page-ui` 抽出的「根据 getTags 响应填 resourceExists」若仍分散则保持在 page-ui。

**Options：**

- 字段：baseUrl、token（`type=password`）、confirmUntag 勾选、previewLimit 数字。
- 打开时 `sendMessage({ type:"getConfig" })` 填表。
- 「保存」→ `saveConfig`；成功提示已保存。
- 「探测连接」→ `probe` 用**表单当前值**（未保存也行）；展示 copy 中的成功/失败句。
- 不发业务 API。

**page-ui.ts**（Popup 与 app 共用）：

参数 `mode: "popup" | "app"`。

1. 连接：`getConfig`；token 空 → 置顶提示 + 链 `chrome.runtime.openOptionsPage()`。
2. 解析当前页：popup 用 `tabs.query({ active:true, lastFocusedWindow:true })`；app 用 `storage.session.get("pageSnapshot")`。非 http(s) → 只读「当前页不可打标」，禁用打标/去标。展示截断后的 title + uri。
3. `getTags`（strip 在 SW）。渲染芯片：`tagChipLabel`；按钮删除 → 若 `confirmUntag` 则 `confirm(copy.untagConfirm)`；通过后 `untagById`。
4. 打标：单输入；回车/按钮；`debounce` 250ms `suggest`；点联想填入；勾选「创建缺失标签」默认 false，成功后不清勾选、清输入、再 `getTags`。输入去标：同一输入 +「去标」按钮 → confirm → `untagByAlias`。
5. 查询：输入 q → 预览按钮（或回车）`preview`；0 条空列表；渲染 checkbox，不可打开禁用并标注「不可打开」；默认按 `defaultSelectedIds`。
6. 「打开所选」：`decideOpen`；none/over_limit 用 copy 拒绝；否则 `sendMessage({ type:"openTabs", urls })`，页面不 `tabs.create`。
7. app 另有按钮「跟随浏览器当前网页」：读 active tab，写 `pageSnapshot`（由 onChanged 刷新，不必在按钮里直接重跑，避免双刷）。

三个 html **只**含 `#app`、stylesheet、入口 script。`popup.ts` / `app.ts` 调用 `mountPageUi(document.getElementById("app")!, mode)`；**全部**控件由 TS 插入。

错误：`config_missing` 引导 Options；`bad_request`（非法 baseUrl）展示校验文案；`connection_failed` → 服务未运行或地址错误；401 / 鉴权失败 → token 无效；`tag_not_found` 用 copy 明确「未勾选创建缺失标签」；其它 `error.message`。

「打开所选」：`decideOpen` 后 `sendMessage({ type:"openTabs", urls })`，页面 **不**调用 `tabs.create`。

Popup：mount 时 `tabs.query({ active:true, lastFocusedWindow:true })` **一次**，之后不随用户切 tab 更新。

app：读取 `pageSnapshot`；`chrome.storage.session.onChanged` 若 `pageSnapshot` 变化则重跑当前页 + `getTags`。「跟随浏览器当前网页」写 session（触发 onChanged）。

- [ ] **Step 1: 实现 UI**

- [ ] **Step 2:** `cd ext && npm test && npm run build`

Expected: PASS + dist 更新

- [ ] **Step 3: Commit**

```powershell
git add ext/src
git commit -m "#N 扩展 Options 与 Popup/app 页面"
```

---

### Task 7: 实现说明 + 规格编号修正

**Files:**

- Create: `docs/browser-extension.md`
- Modify: `docs/README.md`（表格加一行 #13）
- `plans/2026-09-27-browser-extension.md` §8 编号 **若仍为 #11 才改**；已是 #13 并已链实现计划则 **不要**再改规格

`docs/browser-extension.md` 写清：

- 目录 `ext/`；`npm install` / `npm test` / `npm run build`（均在 `ext/`）
- Chrome：`chrome://extensions` → 开发者模式 → 加载 `ext/dist`
- Edge：`edge://extensions` 同样加载 `ext/dist`
- Options 从 CLI/AppData `config.json` **手工复制** `baseUrl`+`token`（扩展不读 AppData）
- 探测与业务依赖本机 Service 已监听 `127.0.0.1`
- 门禁只有 `npm test`；手工联调清单：打标当前 https 页、404 空列表、创建缺失勾选、去标确认、预览打开硬顶、右键 `app.html` 复用 tab、非 http 页禁用

- [ ] **Step 1: 写文档并改规格编号**

- [ ] **Step 2: Commit**

```powershell
git add docs
git commit -m "#N 扩展加载说明"
```

---

## Self-review

1. **Spec coverage:** §3 组件/权限/配置、§4 Popup/API/打开/右键/错误、§5 安全 → Tasks 1–6。§6–7 非目标未列入任务。Q21–Q28：连接态映射、非法 baseUrl 码、DOM SSOT、SW 开 tab、app onChanged、空 preview、双 Vite、getTags 失败语义。
2. **Placeholders:** 无 TBD。图标允许无字母的单色 PNG。
3. **Types:** `ElRequest`/`ElExtensionConfig`/`PageTags` 在后续任务与 Task 1–4 一致。`openTabs` 只在 `background.ts`，不进 `ElRequest`/`handleRequest`。
