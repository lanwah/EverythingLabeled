# EverythingLabeled 浏览器扩展规格

日期：2026-09-27  
状态：已确认（v1 基线）  
关联：

- [本地守护进程架构](./2026-09-22-local-daemon-architecture.md)
- [数据模型](./2026-09-23-data-model.md)
- [HTTP API 契约](./2026-09-26-http-api.md)
- [查询语言细则](./2026-09-26-query-language.md)
- [CLI 规格](./2026-09-26-cli.md)

## 1. 设计摘要

- 扩展为本机**薄客户端**：只调 Service HTTP（Bearer token），不直连 SQLite，**不**注入网页 DOM（无 Content Script）。
- v1 交付面：**Chrome / Edge**，Manifest V3，**一份代码**。Firefox / Safari 为日后移植目标，不进 v1 交付。
- 能力：当前页打标 / 去标 / 看标签；`suggest` 联想；按查询预览并打开 `http(s)` 结果；Options 配置 + 健康探测。
- 连接：扩展**读不到** `%AppData%/EverythingLabeled/config.json`；用户在 Options 手动填 `baseUrl` + `token`（可从 CLI 配置复制）。
- 与 CLI 对齐：默认**不**自动建标；查询串原样上传；默认**不** `expand`。
- **不做**：关系/标签 CRUD UI、Side Panel、Native Messaging、自动读 AppData、对非 `http(s)` 页打标、页面内浮层。

## 2. 定位与浏览器范围

### 2.1 职责

| 能力 | v1 |
|---|---|
| 当前页打标 / 去标 / 看标签 | ✓ |
| 标签建议（suggest） | ✓ |
| 按查询预览并打开网页 | ✓ |
| Options 配置 + `GET /v1/health` 探测 | ✓ |
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

## 3. 组件、权限与配置

### 3.1 组件

| 组件 | 职责 |
|---|---|
| **Options** | 编辑 `baseUrl`、`token`；「探测连接」→ `GET /v1/health`；展示连通状态 |
| **Popup** | 当前页标签；加/删标签（「创建缺失标签」勾选、`suggest`）；查询预览 + 勾选打开 |
| **Service Worker** | 统一 HTTP 客户端（Bearer、超时、错误映射）；`contextMenus` |
| **Context Menus** | 见 §4.5 |

### 3.2 Manifest / 权限（v1）

- `storage`
- `tabs` / `activeTab`（当前页 `url`/`title`；打开结果 tab）
- `contextMenus`
- `host_permissions`：`http://127.0.0.1/*`；若允许 `localhost` 作 `baseUrl` 则另加 `http://localhost/*`
- **不申请** 宽泛 `<all_urls>`；**无** Content Script

Chromium 扩展在已授权 host 上 `fetch` 一般不依赖网页 CORS；Service 仍只监听回环（见架构文档）。

### 3.3 配置（`chrome.storage.local`）

```json
{
  "baseUrl": "http://127.0.0.1:5123",
  "token": "<local-secret>"
}
```

- 缺省预填 `baseUrl`；`token` 为空则业务操作前引导 Options。
- **`baseUrl` 仅允许回环 host**：`127.0.0.1` 或 `localhost`（拒绝其它 host）。
- 不写回 `%AppData%`；与 CLI 配置靠用户复制，**不自动同步**。
- 请求头：`Authorization: Bearer <token>`，`Accept: application/json`。
- 整请求超时 **30s**（与 CLI 对齐）。

### 3.4 服务发现（v1）

- **不是**自动扫端口或读 `config.json`。
- **是**：Options「探测连接」→ 当前 `baseUrl` 的 `GET /v1/health`（可无 token）。
- 业务 API 必须带 token。

## 4. Popup / 右键与 API 映射

### 4.1 当前页资格

- 仅 `http://` / `https://` 启用打标、去标、看标签。
- 其它 scheme：提示「当前页不可打标」；右键相关项禁用或不提供。
- 送给 API 的 `uri`：tab URL；客户端可先去掉 `#fragment`（与[数据模型 §2 url](./2026-09-23-data-model.md) 一致）；最终规范化以服务端为准。
- 打标时显式传 `kind: "url"`。

### 4.2 Popup 逻辑分区

1. **连接状态**：未配置 / 探测失败 → 置顶提示并链到 Options。
2. **当前页**：标题 + URI（可截断）；`GET /v1/resources/tags?uri=` 列标签。
3. **打标**：别名输入（防抖 `GET /v1/tags/suggest?q=&limit=`，默认 limit 10）；勾选「创建缺失标签」→ `createMissing`；`POST /v1/resources/tags`（`add`）。
4. **去标**：标签旁删除或输入后 `remove`；`createMissing` 永不 `true`。
5. **查询打开**：输入 `q` → `GET /v1/query/preview`；勾选后打开；见 §4.4。

### 4.3 API 映射

| UI 动作 | HTTP |
|---|---|
| 探测连接 | `GET /v1/health`（可无 token） |
| 当前页标签 | `GET /v1/resources/tags?uri=` |
| 加标 | `POST /v1/resources/tags`：`uri` + `kind:"url"` + `add` + `createMissing`←勾选；`displayName` 见下 |
| 去标 | 同上，`remove`；`createMissing: false` |
| 联想 | `GET /v1/tags/suggest?q=&limit=` |
| 预览 | `GET /v1/query/preview?q=&limit=` |
| 打开 | 本地 `chrome.tabs.create`（以预览勾选为准，不再为打开重查） |

**`displayName`：** 仅在资源尚不存在、将发生隐式创建时带上当前 tab `title`；若已存在则不传或传 `null`，避免覆盖已有显示名。

**查询展开：** v1 Popup **不提供** `expand=child`（默认不展开）。

**`suggest` 失败：** 静默无联想，不打断输入。

### 4.4 打开规则

- 可打开：`kind === "url"` 且 URI 为 `http(s)`。预览可显示 `file`/`folder` 等，但标注不可打开。
- 硬顶：**单次最多 20** 个 tab。
- 超过硬顶：**整次拒绝打开并提示**（不静默截断）。
- 在当前窗口依次 `chrome.tabs.create`。

### 4.5 右键菜单（v1）

| 菜单项 | 行为 |
|---|---|
| 「EverythingLabeled：管理本页标签」 | 打开 Popup（或 `action.openPopup` 若可用） |
| 「EverythingLabeled：添加标签…」 | 打开 Popup 并尽量聚焦打标输入；可退化为仅打开 Popup |

- 只针对**当前页** URL；v1 **不**对链接/图片 URI 打标。

### 4.6 错误与空态

| 情况 | 行为 |
|---|---|
| 未配置 token | 阻断业务，引导 Options |
| health 失败 | 提示服务未运行或地址错误 |
| 非 http(s) 页 | 只读提示，禁用打标 |
| API 4xx/5xx | 展示 API/`error` 可读信息；未勾选创建时的 `404 tag_not_found` 需明确 |
| 查询 0 条 | 空列表（成功空态） |
| 打开超过 20 | 整次拒绝并提示 |

批量 `add`/`remove` 的成功/失败语义以 [HTTP API](./2026-09-26-http-api.md) 为准（整请求，不在客户端擅自拆成多次除非 API 支持部分成功）。

## 5. 安全与交叉约定

### 5.1 安全

- `baseUrl` 仅回环；token 只存 `chrome.storage.local`；不进日志、不写入网页、不上云。
- 不注入 Content Script；标题/URL 只来自 `tabs` API。

### 5.2 与 Service / CLI

| 点 | 约定 |
|---|---|
| 契约 | 只使用已确认 HTTP API |
| URI | 数据模型 url 规则；客户端可预去 `#fragment` |
| 建标默认 | 与 CLI 一致：`createMissing` 默认 false |
| 查询 | `q` 原样上传；扩展不暴露 expand |
| 配置 | 扩展自有 storage；与 AppData config **不自动同步** |
| 健康检查 | `GET /v1/health` |

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
