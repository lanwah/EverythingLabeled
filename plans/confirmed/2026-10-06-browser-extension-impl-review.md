# 浏览器扩展（#13）实现计划讨论确认记录

日期：2026-10-06  
方式：主线澄清（brainstorm，带推荐答案 → 用户确认）Round 1–2；实现计划审核 Round 3（Q21–Q28 均选推荐 A）  
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

---

## C. Round 3 — 实现计划审核（对照规格找分叉）

### Q21 — Popup/app 的「连接状态」要不要开时自动探测

**问题**：规格 §4.2 写「未配置 / 探测失败 → 置顶提示」；§3.4 的探测按钮在 Options。计划 Task 6 只检查空 token，不打 health/meta。

| 选项 | 含义 |
|---|---|
| **A** | 打开 Popup/app **不**自动跑 Options 那套探测。无 token → 「未配置」链 Options。业务请求 `connection_failed` → 「服务未运行或地址错误」；HTTP 401 或信封鉴权失败 → 「token 无效」。Options 仍保留显式「探测连接」（表单未保存值） |
| B | 每次打开 Popup/app 用**已保存** config 先 health、有 token 再 meta |
| C | Popup 也放「探测连接」按钮 |

**确认**：选推荐 **A**。

### Q22 — 已存非法 `baseUrl` 的错误码

**问题**：计划把 canonicalize 失败与空 token 都打成 `config_missing`。

| 选项 | 含义 |
|---|---|
| **A** | 空 token → `config_missing`。已存/草稿 `baseUrl` 校验失败 → `bad_request` + 地址文案，**不**当成缺配置 |
| B | 一律 `config_missing` |
| C | 非法 baseUrl 时静默回退到缺省 `http://127.0.0.1:5123` 并发请求 |

**确认**：选推荐 **A**。

### Q23 — HTML 与 DOM 双源

**问题**：Task 6 写「DOM 由 TS 创建或静态结构，二选一」。

| 选项 | 含义 |
|---|---|
| **A** | 三个 html 只留 `#app` + 引入 `styles.css` / 入口脚本；节点全由 TS 创建 |
| B | 控件写在 html 里，TS 只绑定 |
| C | Popup 静态、app 动态 |

**确认**：选推荐 **A**。

### Q24 — 谁 `tabs.create`

**问题**：规格 §4.4 写 `chrome.tabs.create`；Popup 在连开多个 tab 或 `confirm` 时可能被关掉，页面里的 create 会中断。

| 选项 | 含义 |
|---|---|
| **A** | 页面 `decideOpen` 后发 `openTabs`；**仅 SW** `tabs.create`。SW 再校验：条数 1…`previewLimit`、每条 `isHttpPage`。`windowId`：有 `sender.tab` 用其 window，否则 `chrome.windows.getLastFocused`。`openTabs` **不**进纯函数 `handleRequest`（避免 `handleRequest` 依赖 `chrome.tabs`） |
| B | 按原计划由 popup/app 直接 `tabs.create` |
| C | 开 tab 也走 HTTP（不可行） |

**确认**：选推荐 **A**。

### Q25 — 已打开的 `app.html` 如何接到新快照

**问题**：右键「聚焦并更新快照」时，已挂着的 app 若只在 load 时读 session，不会刷新。

| 选项 | 含义 |
|---|---|
| **A** | `app.html` 订阅 `chrome.storage.session.onChanged`（键 `pageSnapshot`）并重跑当前页/标签。Popup **只在打开时**读一次 active tab，不跟随之后的标签切换（规格：「打开 Popup 时的 active tab」） |
| B | 右键再 `tabs.reload` app |
| C | 不刷新已打开 app，只聚焦 |

**确认**：选推荐 **A**。

### Q26 — 预览空 `q`

**问题**：suggest 空串不请求；preview 未写。

| 选项 | 含义 |
|---|---|
| **A** | `q` trim 后为空 → **不** fetch，成功空列表（与「查询 0 条」同一空态） |
| B | 空 q 仍请求 API |
| C | 空 q 点预览则提示错误 |

**确认**：选推荐 **A**。

### Q27 — Service Worker 打包会不会拆 chunk

**问题**：Vite 多入口常把共享模块打成 `chunks/*.js`；MV3 `type: module` 的 SW 引用 hashed chunk 易加载失败。

| 选项 | 含义 |
|---|---|
| **A** | **两次** Vite：`vite.config.ts` 打三个 html；`vite.background.ts` 以 `background.ts` 为 library **单文件** `dist/background.js`（`inlineDynamicImports: true`）。`npm run build` 串行两者。禁止 SW 依赖带 hash 的 chunk |
| B | 一次多入口，接受 `chunks/` |
| C | background 内联进一条很大的 IIFE 但不分第二个 config（手写 plugin） |

**确认**：选推荐 **A**。

### Q28 — `getTags` 翻页失败与 storage 写入面

**问题**：第一页 404 当空列表；中途 5xx 或其它 404 怎么办？`set` 整份对象会不会留下脏键？

| 选项 | 含义 |
|---|---|
| **A** | 仅 `code === "resource_not_found"` 映射为空 + `resourceExists:false`；其它错误 **整次失败**（已拼页丢弃）。`saveStored` **只写**四键 `baseUrl`/`token`/`confirmUntag`/`previewLimit`。去掉计划里未使用的 `createElApi({ now? })` |
| B | 任一页 404 都当无资源 |
| C | 翻页中途失败则返回已拼前缀（部分列表） |

**确认**：选推荐 **A**。
