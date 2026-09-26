# 浏览器扩展规格审核确认记录

日期：2026-09-27  
方式：主线澄清（brainstorm）+ grill 审核（问题 + 推荐答案 → 用户确认）  
已落盘同步：

- [浏览器扩展规格](../2026-09-27-browser-extension.md)
- [路线图 backlog](../2026-09-23-roadmap-backlog.md)（#4 已确认）
- [本地守护进程架构](../2026-09-22-local-daemon-architecture.md)（扩展配置说明交叉引用）

对 @plans/2026-09-27-browser-extension.md 的确认点如下。

---

## A. 主线澄清（规格起草前）

### A1 — 日用范围

**确认**：选 A — 日常薄客户端。

- 打标当前页、看标签、按查询打开、连本机 Service
- 不含完整标签关系管理 UI、离线库、跨设备同步

### A2 — 目标浏览器

**确认**：选 A — 仅 Chromium（Chrome / Edge）一套 MV3。

- Firefox / Safari 不进 v1 交付；Firefox 为兼容目标、实现后置

### A3 — 如何连上 Service

**确认**：选 A — Options 手动填 `baseUrl` + `token`；可选探测；存 `chrome.storage.local`。

- 不读 AppData；Native Messaging 延后

### A4 — 主要交互入口

**确认**：选 A — 工具栏 Popup 为主 + 右键辅助。

### A5 — 按查询打开

**确认**：选 A — 先预览再打开，并设打开硬顶。

### A6 — 可打标页面

**确认**：选 A — 仅 `http://` / `https://`。

### A7 — 缺失标签

**确认**：选 A — 默认不创建；需显式勾选「创建缺失标签」。

### A8 — 查询结果中的非网页

**确认**：选 A — 预览可显示全部 kind；真正开 tab 只打开 `http(s)` 的 `url`。

### A9 — displayName

**确认**：选 A — 首次隐式建资源时用 tab `title`；已存在不覆盖。

### A10 — 标签输入联想

**确认**：选 A — 调用 `GET /v1/tags/suggest`；失败则手打。

### A11 — 实现取向

**确认**：方案 1 — 经典 MV3 薄客户端（Options + Popup + SW + 右键；无 Content Script 浮层）。

- 用户追问：后续其它浏览器——Edge 同包；Firefox 需适配后置；Safari 不承诺

### A12 — 设计分节确认

**确认**：§1 定位、§2 组件/权限/配置、§3 Popup/API、§4 边界与非目标 — 均 ok 后落盘初稿。

---

## B. Grill 审核（落盘修订）

### Q1 — `localhost` 与 `::1`

**确认**：选推荐 A。

- v1 `baseUrl` **只允许** `127.0.0.1`；拒绝 `localhost` / `[::1]`（与 Service 只监听 `127.0.0.1` 一致）

### Q2 — 探测是否验 token

**确认**：选推荐 A。

- 先 `GET /v1/health`；已填 token 再 `GET /v1/meta`；文案区分「服务未开」与「token 无效」

### Q3 — `displayName` 与 CLI 不一致

**确认**：选推荐 A。

- 扩展可传 title；CLI 不传；规格标明**有意分歧**

### Q4 — 去标确认

**确认**：选 C（非初荐 A）。

- Options 增加 `confirmUntag`，**缺省 true**，与 CLI 同名键（仅扩展 storage，不同步 AppData）

### Q5 — 预览默认 limit

**确认**：用户定制（非初荐 A/B/C）。

- 默认 **8**；Options 可调；**最大 16**

### Q6 — 已打开同 URL

**确认**：选推荐 A。

- 一律 `tabs.create` 新开

### Q7 — 勾选混有不可打开项

**确认**：选推荐 A。

- 只打开可打开项；可打开数为 0 → 拒绝；超过硬顶 → 整次拒绝

### Q8 — 右键打开 Popup

**确认**：选推荐 B。

- 右键一律开 `app.html`，不依赖 `openPopup`；与 Popup 共用 UI 逻辑

### Q9 — 打开硬顶 vs preview 上限

**说明**：打开硬顶 = 一次「打开所选」最多新开多少个 tab。

**确认**：选推荐 A。

- 与 `previewLimit` **同一常量**（默认 8，上限 16）

### Q10 — `confirmUntag` UI

**确认**：选推荐 A。

- 去标前确认框；取消不发请求；「不再询问」只通过 Options 改 `confirmUntag`

### Q11 — 多别名输入

**确认**：选推荐 B。

- 每次只加一个别名

### Q12 — 右键菜单项数

**确认**：选推荐 A。

- 合并为一项：「管理本页标签」

### Q13 — `baseUrl` 形态

**确认**：选推荐 A。

- 仅 `http://127.0.0.1:<port>`（pathname 空或 `/`）；禁 query/hash；禁 https

### Q14 — 配置键名

**确认**：选推荐 A。

- `{ baseUrl, token, confirmUntag, previewLimit }`；`previewLimit` 缺省 8，范围 **1–16**

### Q15 — 工具栏图标

**确认**：选推荐 B。

- 图标仍用小 Popup；右键用 `app.html`

### Q16 — 无资源时 GET tags

**确认**：选推荐 A。

- `404 resource_not_found` → 空标签列表

### Q17 — 「创建缺失标签」是否记住

**确认**：选推荐 A。

- 不持久化；每次打开 UI 默认不勾选

### Q18 — 打标成功后 UI

**确认**：选推荐 A。

- 清空输入、刷新列表；本会话保留「创建缺失」勾选

### Q19 — app.html「当前页」是谁

**确认**：选推荐 A。

- 打开时写入来源 tab 快照；提供「跟随浏览器当前网页」刷新

### Q20 — 多次打开 app.html

**确认**：选推荐 A。

- 已有则聚焦并更新快照，不新开

### Q21 — 勾选数 > previewLimit

**确认**：选推荐 A。

- 打开前校验，超过则整次拒绝

---

## C. 落盘检查清单（实现时对照）

- [ ] `baseUrl` 校验仅 `http://127.0.0.1`；host_permissions 仅该 host
- [ ] 探测：health →（有 token）meta
- [ ] storage：`confirmUntag` 缺省 true；`previewLimit` 缺省 8、范围 1–16
- [ ] 打开硬顶 = `previewLimit`；超限拒绝；混选只开可打开项
- [ ] 图标 Popup + 右键单一菜单 → `app.html` 快照当前页
- [ ] 已有 `app.html` 聚焦更新；`GET tags` 404 → 空列表
- [ ] 单别名打标；`createMissing` 不持久；成功清输入
- [ ] 首次建资源可传 `displayName`=title；规格与 CLI 分歧已文档化
