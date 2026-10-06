# 浏览器扩展（#13）实现计划讨论确认记录

日期：2026-10-06  
方式：主线澄清（brainstorm，带推荐答案 → 用户确认）Round 1–2  
已落盘同步：

- [浏览器扩展规格](../2026-09-27-browser-extension.md)（产品规格不改；[规格审核](./2026-09-27-browser-extension-review.md)）
- [浏览器扩展实现计划](../2026-10-06-browser-extension-impl.md)
- [路线图 backlog](../2026-09-23-roadmap-backlog.md)（#13 已确认实现计划）
- [项目看板](../../project.md)

前提（未作为开放题，与规格一致）：v1 仅 Chrome/Edge 一份 MV3；无 Content Script / Side Panel / Native Messaging / Firefox 交付；Options 手填配置；Popup + 右键 `app.html`；`baseUrl` 仅 `http://127.0.0.1`；探测 health→（有 token）meta；`confirmUntag` 缺省 true；`previewLimit` 缺省 8、范围 1–16、打开硬顶同一常量；不改 C# `IElApi`。

---

## A. Round 1 — 交付、布局、客户端、测试

### Q1 — #13 交付边界

**问题**：本轮做到哪一层？是否碰 C# Client / 上架？

| 选项 | 含义 |
|---|---|
| **A** | 仓库内一份 MV3 源码：Options + Popup + SW + 右键/`app.html` + 纯逻辑测试 + `docs/browser-extension.md`（如何 `build`、Chrome/Edge 加载未打包）。**不**改 `IElApi`（无 `displayName`/`Meta` 也本轮不补 C#）。不上架、不做 Firefox、不做 Native Messaging |
| B | A + 顺带给 C# `IElApi` 加 `Meta`/`displayName`（扩展仍用不到） |
| C | 只做无 UI 的 TS HTTP/配置库，Popup 下轮再接 |

**确认**：选推荐 **A**。

### Q2 — 仓库位置

**问题**：扩展源码放哪？

| 选项 | 含义 |
|---|---|
| **A** | `ext/`（如 `ext/package.json`、`ext/src/`、`ext/public/`）。**不进** `.sln`；`dotnet test` 不管扩展。根 `docs/README.md` 加一行 |
| B | `src/EverythingLabeled.BrowserExtension/`（与 C# 工程并列，仍不进 sln） |
| C | 独立 git 子模块 / 另仓 |

**确认**：选推荐 **A**。

### Q3 — 构建工具

**问题**：规格要求 TS + 轻量静态页，不绑 React SPA。用什么打 MV3？

| 选项 | 含义 |
|---|---|
| **A** | **Vite** + TypeScript（vanilla）。多入口：`background`、`popup`、`options`、`app`。产物 `ext/dist/`，`manifest.json` 拷进 dist。无 React/Vue |
| B | 裸 **esbuild** 脚本，无 Vite |
| C | Webpack |

**确认**：选推荐 **A**。

### Q4 — HTTP 客户端

**问题**：浏览器读不到 AppData，也不能引用 `EverythingLabeled.Client`。

| 选项 | 含义 |
|---|---|
| **A** | 扩展内 **一份 TS 客户端**：`fetch` + Bearer + 30s `AbortController`；解析与 C# `HttpElApi` 同构的 `{status,message,data}` 信封；health 不带 token。API/配置模块与 `chrome.*` 壳分离（规格 §2.2）。路径、超时、错误码对齐 Client，**不**复制 C# 源 |
| B | 扩展 `fetch` 散落在 Popup/SW，不做独立模块 |
| C | 想办法从扩展调 `el`/Named Pipe |

**确认**：选推荐 **A**。

### Q5 — 测试门禁

**问题**：无 GitHub Actions。门禁测到哪一层？

| 选项 | 含义 |
|---|---|
| **A** | **Vitest** 测纯函数：`baseUrl` 校验、缺省 `confirmUntag`/`previewLimit`、去 `#fragment`、打开规则（0 / 超硬顶 / 混选）、`displayName` 是否带、404→空列表。`chrome.*` 用假对象。门禁 **不**跑真 Chrome。Windows 联调写在 `docs/browser-extension.md` 手工步骤 |
| B | A + Playwright 真浏览器作为门禁 |
| C | 无自动测，只手工加载扩展 |

**确认**：选推荐 **A**。

### Q6 — 送给 API 的 URI

**问题**：规格允许客户端先去掉 `#fragment`；PathUri 对 http(s) 还会小写 host、省略默认端口。

| 选项 | 含义 |
|---|---|
| **A** | 客户端 **只去掉 `#fragment`**（保留 query）。host/端口规范化交给 Service。不在 TS 里复刻整份 PathUri |
| B | TS 复刻 PathUri 的 http(s) 规范化（小写 host、去默认端口、去 fragment） |
| C | 原样传 tab URL（含 fragment） |

**确认**：选推荐 **A**。

### Q7 — `app.html` 当前页快照

**问题**：打开时写入来源 tabId + url/title；已有 tab 则聚焦并更新，不新开。存在哪？

| 选项 | 含义 |
|---|---|
| **A** | `chrome.storage.session` 键（如 `pageSnapshot`：`tabId`/`url`/`title`）。SW 右键写入后 `tabs.create`/`update` 打开 `app.html`。页面读 session；「跟随浏览器当前网页」重写快照。不把 token 放进 URL |
| B | 仅 URL query（`app.html?url=&title=`） |
| C | 仅 SW 内存，刷新 `app.html` 即丢 |

**确认**：选推荐 **A**。

### Q8 — 何时传 `displayName`

**问题**：仅资源尚不存在、将隐式创建时带 tab `title`。当前 `IElApi.TagAsync` 也不传该字段（C# 本轮不动）。

| 选项 | 含义 |
|---|---|
| **A** | 以 **刚拉过的** `GET /v1/resources/tags` 为准：`404 resource_not_found` → POST 带 `displayName=title`；成功列表（含 0 标签）→ 不传或 `null`。不另打 `by-uri` |
| B | 每次打标都带 title，让服务端忽略已存在资源 |
| C | 打标前再 `GET /v1/resources/by-uri` |

**确认**：选推荐 **A**。

### Q9 — 当前页标签是否翻页

**问题**：C# `GetTagsAsync` 会按 1024 翻到 `total`。扩展 UI 是短列表。

| 选项 | 含义 |
|---|---|
| **A** | 与 Client 同：循环直到收齐（`limit=1024`）。避免标签多时 Popup 只看到第一页 |
| B | 只请求一页（如 `limit=100`），多了截断并提示 |
| C | 只请求默认 limit（服务端 50） |

**确认**：选推荐 **A**。

### Q10 — 构建产物是否进 git

**问题**：`ext/dist/` 提交吗？

| 选项 | 含义 |
|---|---|
| **A** | `ext/dist/` **gitignore**。提交源码 + `package-lock.json`。加载未打包指向本地 `npm run build` 后的 dist。文档写清这两步 |
| B | 把 dist 提交进仓库，免 Node 也能加载 |
| C | 无 lockfile，每次最新依赖 |

**确认**：选推荐 **A**。

---

## B. Round 2 — 接线、UI、构建细节

### Q11 — 谁发 HTTP

**问题**：规格写 SW 是「统一 HTTP 客户端」；扩展页自己 `fetch` `127.0.0.1` 在 Chromium 里也通。

| 选项 | 含义 |
|---|---|
| **A** | **只有 Service Worker `fetch`**。Popup / Options / `app.html` 经 `runtime.sendMessage` 调 API。TS 客户端只跑在 SW。页面不直连 Service |
| B | 共享 TS 模块，SW 与各页面各自 `fetch`（无消息层） |
| C | 仅页面 `fetch`，SW 只管右键/开 tab |

**确认**：选推荐 **A**。

### Q12 — 包管理器

**问题**：npm / pnpm / yarn？

| 选项 | 含义 |
|---|---|
| **A** | **npm** + 提交 `ext/package-lock.json`。`engines.node` 标 **>=20** |
| B | pnpm |
| C | yarn |

**确认**：选推荐 **A**。

### Q13 — Vite 怎么打 MV3

**问题**：是否上 MV3 Vite 插件？

| 选项 | 含义 |
|---|---|
| **A** | **手写** Vite 多入口（`background.ts` / `popup` / `options` / `app`）+ 把 `manifest.json` 与静态资源拷到 `dist/`。不引入 `@crxjs/vite-plugin` / `vite-plugin-web-extension` |
| B | `@crxjs/vite-plugin` |
| C | `vite-plugin-web-extension` |

**确认**：选推荐 **A**。

### Q14 — 预览列表默认勾选

**问题**：打开以勾选为准。返回后哪些默认勾上？

| 选项 | 含义 |
|---|---|
| **A** | 预览返回后：**可打开**项默认勾选；`file`/`folder` 等不可打开项展示但默认不勾且勾选框禁用 |
| B | 全部默认不勾，用户自己勾 |
| C | 全部默认勾选（含不可打开，打开时再过滤） |

**确认**：选推荐 **A**。

### Q15 — Options「探测连接」读哪份配置

**问题**：表单未保存时探测用什么？

| 选项 | 含义 |
|---|---|
| **A** | 探测用 **当前表单值**（可尚未保存）。校验失败则不发请求并提示。点「保存」才写入 `chrome.storage.local` |
| B | 探测只读已保存 storage；未保存的输入无效 |
| C | 探测即保存再请求 |

**确认**：选推荐 **A**。

### Q16 — 去标确认框

**问题**：用系统确认还是自绘？芯片删除是否也确认？

| 选项 | 含义 |
|---|---|
| **A** | `window.confirm`。取消不发请求。「不再询问」只在 Options 改 `confirmUntag`，确认框里不提供该勾选 |
| B | 自绘模态 |
| C | 芯片删除不确认，仅输入去标才确认 |

**确认**：选推荐 **A**。

### Q17 — UI 语言与扩展名

**问题**：CLI 是英文消息；规格示例文案是中文。

| 选项 | 含义 |
|---|---|
| **A** | UI **中文**（对齐规格：「当前页不可打标」等）。`manifest.name` = `EverythingLabeled`，`version` **0.1.0**。不做 `_locales` i18n。API `message` 原文展示 |
| B | UI 全英文 |
| C | 中英 `_locales` |

**确认**：选推荐 **A**。

### Q18 — 图标

**问题**：工具栏 / 管理页图标从哪来？

| 选项 | 含义 |
|---|---|
| **A** | 仓库放简单占位 PNG（16/32/48/128，单色块+「EL」即可），`action.default_icon` / `icons` 都指向它们。本轮不请设计师 |
| B | 本轮不设 icons，用浏览器默认拼图 |
| C | 内联 SVG 当扩展图标（MV3 对 action 图标仍要位图） |

**确认**：选推荐 **A**。实现上允许四档共用同一张占位 PNG（浏览器缩放）。

### Q19 — 标签芯片文案与去标身份

**问题**：`GET tags` 有 `id` / `name` / `namespace` / `aliases`。芯片显示什么、删除传什么？

| 选项 | 含义 |
|---|---|
| **A** | 芯片显示 `namespace ? namespace + "/" + name : name`。旁删除 POST `remove: [{ id }]`。输入框去标仍提交**一个别名字符串** |
| B | 芯片显示主别名；删除也用别名 |
| C | 芯片只显示 `id` |

**确认**：选推荐 **A**。

### Q20 — suggest 防抖

**问题**：何时请求联想、limit 跟谁走？

| 选项 | 含义 |
|---|---|
| **A** | 输入 trim 后长度 **≥ 1** 才请求；**250ms** 防抖；`limit=10`（不跟 `previewLimit`）。失败静默空联想。空输入不请求、清空联想 |
| B | 500ms，≥ 2 字符 |
| C | 不防抖，每个按键请求 |

**确认**：选推荐 **A**。
