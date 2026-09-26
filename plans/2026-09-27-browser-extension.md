# EverythingLabeled 浏览器扩展规格

日期：2026-09-27  
状态：已确认（v1 基线；含 2026-09-27 grill 修订）  
审核记录：[浏览器扩展规格审核确认](./confirmed/2026-09-27-browser-extension-review.md)  
关联：

- [本地守护进程架构](./2026-09-22-local-daemon-architecture.md)
- [数据模型](./2026-09-23-data-model.md)
- [HTTP API 契约](./2026-09-26-http-api.md)
- [查询语言细则](./2026-09-26-query-language.md)
- [CLI 规格](./2026-09-26-cli.md)

## 1. 设计摘要

- 扩展为本机**薄客户端**：只调 Service HTTP（Bearer token），不直连 SQLite，**不**注入网页 DOM（无 Content Script）。
- v1 交付面：**Chrome / Edge**，Manifest V3，**一份代码**。Firefox / Safari 为日后移植目标，不进 v1 交付。
- 能力：当前页打标 / 去标 / 看标签；`suggest` 联想；按查询预览并打开 `http(s)` 结果；Options 配置 + 探测（health + 可选 meta）。
- 连接：扩展**读不到** `%AppData%/EverythingLabeled/config.json`；用户在 Options 手动填配置（可从 CLI 配置复制）。
- 与 CLI 对齐：默认**不**自动建标；查询串原样上传；默认**不** `expand`；去标受 `confirmUntag` 约束（扩展自有 storage）。
- 与 CLI **有意分歧**：隐式建资源时可传 tab `title` 作 `displayName`（CLI 不传）。
- **不做**：关系/标签 CRUD UI、Side Panel、Native Messaging、自动读 AppData、对非 `http(s)` 页打标、页面内浮层。

## 2. 定位与浏览器范围

### 2.1 职责

| 能力 | v1 |
|---|---|
| 当前页打标 / 去标 / 看标签 | ✓ |
| 标签建议（suggest） | ✓ |
| 按查询预览并打开网页 | ✓ |
| Options 配置 + 探测（health / meta） | ✓ |
| 标签 / 关系 CRUD UI | ✗ |
| `expand=child` 控件 | ✗ |
| Native Messaging / 读 AppData | ✗ |
| Firefox / Safari 正式交付 | ✗（移植见 §6） |

### 2.2 浏览器

| 目标 | v1 |
|---|---|
| Chrome | ✓ |
| Edge | ✓（同包） |
| Firefox | 兼容目标、实现后置 |
| Safari | 不承诺 |

实现时将 **API 客户端 + 配置读写** 与 `chrome.*` 壳分离，降低日后 Firefox 适配成本。

### 2.3 实现取向

经典 MV3 薄客户端：Options + Popup + Service Worker + 右键；TypeScript + 轻量静态页；**不**做页面 Content Script 浮层、**不**强绑 React SPA。

## 3. 组件、权限与配置

### 3.1 组件

| 组件 | 职责 |
|---|---|
| **Options** | 编辑 `baseUrl`、`token`、`confirmUntag`、`previewLimit`；「探测连接」；展示连通状态 |
| **Popup**（工具栏图标） | 与业务 UI 同逻辑：当前页标签、打标/去标、查询预览与打开；「当前页」= 打开 Popup 时的 active tab |
| **app.html**（右键入口） | 与 Popup **共用同一套 UI 逻辑**；「当前页」= 打开时写入的**来源 tab 快照**（见 §4.5） |
| **Service Worker** | 统一 HTTP 客户端（Bearer、超时、错误映射）；`contextMenus` |
| **Context Menus** | 见 §4.5 |

### 3.2 Manifest / 权限（v1）

- `storage`
- `tabs` / `activeTab`（当前页 `url`/`title`；打开结果 tab；管理 `app.html`）
- `contextMenus`
- `host_permissions`：**仅** `http://127.0.0.1/*`
- **不申请** 宽泛 `<all_urls>`；**无** Content Script；**不**申请 `localhost` / `::1` host

Chromium 扩展在已授权 host 上 `fetch` 一般不依赖网页 CORS；Service 仍只监听 `127.0.0.1`（见架构文档）。

### 3.3 配置（`chrome.storage.local`）

```json
{
  "baseUrl": "http://127.0.0.1:5123",
  "token": "<local-secret>",
  "confirmUntag": true,
  "previewLimit": 8
}
```

| 键 | 规则 |
|---|---|
| `baseUrl` | 缺省预填 `http://127.0.0.1:5123`。校验：scheme 仅 `http`；host 仅 `127.0.0.1`；**拒绝** `localhost` / `[::1]` / 其它 host；pathname 为空或 `/`；**拒绝** query / hash。端口可省略（默认 80，实际部署应显式端口）。 |
| `token` | 字符串；空则业务操作前引导 Options |
| `confirmUntag` | 可选；**缺省键或省略 = `true`**。语义对齐 CLI（仅作用于本扩展 storage，**不**读写 AppData 文件） |
| `previewLimit` | 可选；**缺省 = 8**；合法范围 **1–16**（含）。同时作为预览 `limit` 与单次打开 tab 硬顶 |

- 不写回 `%AppData%`；与 CLI 配置靠用户复制，**不自动同步**。
- 请求头：`Authorization: Bearer <token>`，`Accept: application/json`。
- 整请求超时 **30s**（与 CLI 对齐）。

### 3.4 服务发现 / 探测（v1）

- **不是**自动扫端口或读 `config.json`。
- Options「探测连接」：
  1. `GET /v1/health`（可无 token）→ 失败则提示服务未运行或地址错误
  2. 若已填 token → 再 `GET /v1/meta` → 失败则提示 token 无效（与 health 失败区分文案）
- 业务 API 必须带 token。

## 4. Popup / app / 右键与 API 映射

### 4.1 当前页资格

- 仅 `http://` / `https://` 启用打标、去标、看标签。
- 其它 scheme：提示「当前页不可打标」；右键在不可打标页可禁用菜单或不提供。
- 送给 API 的 `uri`：目标 tab URL；客户端可先去掉 `#fragment`（与[数据模型 §2 url](./2026-09-23-data-model.md) 一致）；最终规范化以服务端为准。
- 打标时显式传 `kind: "url"`。

### 4.2 UI 逻辑分区（Popup 与 app.html 共用）

1. **连接状态**：未配置 / 探测失败 → 置顶提示并链到 Options。
2. **当前页**：标题 + URI（可截断）；`GET /v1/resources/tags?uri=` 列标签。`404 resource_not_found`（或等价无资源）→ **空标签列表**，不报错。
3. **打标**：每次输入**一个**别名（回车/按钮提交）；防抖 `GET /v1/tags/suggest?q=&limit=`（suggest 默认 limit 10）；勾选「创建缺失标签」→ `createMissing`（**不**写入 storage，每次打开 UI 默认不勾选）；成功后清空输入、刷新标签列表，本会话保留勾选状态。
4. **去标**：标签旁删除或输入后 `remove`；`createMissing` 永不 `true`。若 `confirmUntag != false` → 确认框后再请求；取消则不发请求。
5. **查询打开**：输入 `q` → `GET /v1/query/preview?q=&limit=`，`limit` = 当前 `previewLimit`；勾选后打开；见 §4.4。`app.html` 另提供「跟随浏览器当前网页」以刷新当前页快照。

### 4.3 API 映射

| UI 动作 | HTTP |
|---|---|
| 探测连接 | `GET /v1/health`；有 token 时再 `GET /v1/meta` |
| 当前页标签 | `GET /v1/resources/tags?uri=` |
| 加标 | `POST /v1/resources/tags`：`uri` + `kind:"url"` + `add`（单元素）+ `createMissing`←勾选；`displayName` 见下 |
| 去标 | 同上，`remove`；`createMissing: false` |
| 联想 | `GET /v1/tags/suggest?q=&limit=` |
| 预览 | `GET /v1/query/preview?q=&limit=`（`limit`←`previewLimit`） |
| 打开 | 本地 `chrome.tabs.create`（以预览勾选为准，不再为打开重查） |

**`displayName`：** 仅在资源尚不存在、将发生隐式创建时带上目标 tab `title`；若已存在则不传或传 `null`。与 CLI「永不传 displayName」为**有意分歧**。

**查询展开：** v1 **不提供** `expand=child`（默认不展开）。

**`suggest` 失败：** 静默无联想，不打断输入。

### 4.4 打开规则

- 可打开：`kind === "url"` 且 URI 为 `http(s)`。预览可显示 `file`/`folder` 等，但标注不可打开。
- 打开硬顶 = 当前 `previewLimit`（默认 8，最大 16）。
- 勾选中可打开数量为 0 → 提示并拒绝。
- 可打开且已勾选数 `> previewLimit` → **整次拒绝**并提示（不静默截断）。
- 混有不可打开项：只打开可打开的（在通过硬顶校验后）。
- 一律 `chrome.tabs.create` 新开（不尝试聚焦已有同 URL tab）。
- 在当前窗口依次创建。

### 4.5 右键与 app.html（v1）

| 菜单项 | 行为 |
|---|---|
| 「EverythingLabeled：管理本页标签」 | 打开或聚焦 `app.html`（见下） |

- **不**提供第二项「添加标签…」；**不**依赖 `action.openPopup`。
- 只针对**来源页** URL；v1 **不**对链接/图片 URI 打标。
- 打开 `app.html` 时写入**来源 tabId**（及当时 url/title 快照）到 session/storage 或 URL query；UI 以该快照为「当前页」。
- 若已存在 `app.html` tab → **聚焦并更新**当前页快照，不新开。
- 工具栏图标仍用 **Popup**（`default_popup`）；与 `app.html` 共用逻辑模块。

### 4.6 错误与空态

| 情况 | 行为 |
|---|---|
| 未配置 token | 阻断业务，引导 Options |
| health 失败 | 提示服务未运行或地址错误 |
| meta 失败（token） | 提示 token 无效 |
| 非 http(s) 页 / 快照 | 只读提示，禁用打标 |
| `GET tags` → `404 resource_not_found` | 空标签列表 |
| API 其它 4xx/5xx | 展示可读信息；未勾选创建时的 `404 tag_not_found` 需明确 |
| 查询 0 条 | 空列表（成功空态） |
| 打开超过 `previewLimit` 或 0 可打开 | 整次拒绝并提示 |

批量 `add`/`remove` 的成功/失败语义以 [HTTP API](./2026-09-26-http-api.md) 为准。

## 5. 安全与交叉约定

### 5.1 安全

- `baseUrl` 仅 `http://127.0.0.1`；token 只存 `chrome.storage.local`；不进日志、不写入网页、不上云。
- 不注入 Content Script；标题/URL 只来自 `tabs` API 或显式快照。

### 5.2 与 Service / CLI

| 点 | 约定 |
|---|---|
| 契约 | 只使用已确认 HTTP API |
| URI | 数据模型 url 规则；客户端可预去 `#fragment` |
| 建标默认 | 与 CLI 一致：`createMissing` 默认 false；勾选不持久化 |
| `displayName` | 扩展可传 title（首次创建）；CLI 不传 — **有意分歧** |
| `confirmUntag` | 扩展 storage 同名键，缺省 true；**不**同步 AppData 文件 |
| 查询 | `q` 原样上传；扩展不暴露 expand |
| 配置 | 扩展自有 storage；与 AppData config **不自动同步** |
| 探测 | `health` +（有 token 时）`meta` |

## 6. 日后移植（不实现）

- **Edge**：与 Chrome 同包。
- **Firefox**：复用 API/配置模块；重做 Manifest / 后台页；单独立项联调。
- **Native Messaging**：日后可选，用于自动灌入 `config.json`；v1 不做。

## 7. 明确非目标（v1）

- Firefox / Safari 正式交付与上架
- Native Messaging、自动读 AppData
- Side Panel、页面内浮层、离线库
- 标签/关系管理 UI、`expand=child` 控件
- 对 `file://` / 浏览器内部页打标
- 安装程序改扩展配置、企业策略强制下发（归入配置与安装讨论）

## 8. 后续

- 实现跟踪：Backlog **#11** 浏览器扩展（Windows 联调）
- 依赖：Service HTTP（#7–#9）可用；手动 Options 配置即可联调，不阻塞于 #6 安装程序
