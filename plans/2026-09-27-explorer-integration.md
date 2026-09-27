# EverythingLabeled Explorer 集成规格

日期：2026-09-27  
状态：已确认（v1 基线；含 2026-09-27 grill 修订）  
审核记录：[Explorer 集成规格审核确认](./confirmed/2026-09-27-explorer-integration-review.md)  
关联：

- [本地守护进程架构](./2026-09-22-local-daemon-architecture.md)
- [数据模型](./2026-09-23-data-model.md)
- [HTTP API 契约](./2026-09-26-http-api.md)
- [查询语言细则](./2026-09-26-query-language.md)
- [CLI 规格](./2026-09-26-cli.md)
- [浏览器扩展规格](./2026-09-27-browser-extension.md)

## 1. 设计摘要

- Explorer 集成为本机**薄客户端**：稀疏包 `IExplorerCommand` 只负责菜单与**即发即忘**拉起 Host；**硬规则与业务**在进程外 **WPF** `EverythingLabeled.ExplorerHost`，只调 Service HTTP（Bearer），不直连 SQLite。
- v1 能力：对选中文件/文件夹 **打标 / 去标 / 看标签**；**按标签查找**（仅 `preview`，点选落到 Explorer / 浏览器）。**多选批量打标/去标**为一等场景（去重后上限 100）。
- 路径 → `file://`、存在性、`kind`（含 symlink / junction）与 [CLI §3–4](./2026-09-26-cli.md) **同规则**，且必须与 CLI **共用同一 PathUri 实现库**；不传 `displayName`；默认不 `createMissing`、不 `expand`。
- 读 `%AppData%/EverythingLabeled/config.json`（**不写回**）；`confirmUntag` 缺省 true；`previewLimit` 缺省 16；`maxConcurrency` 缺省 4。
- 菜单项与 Host UI 文案以**简体中文**为主；子菜单标题可用产品名 `EverythingLabeled`。
- **不做**：关系/标签 CRUD UI、`.lnk` 解析到目标、虚拟命名空间、文件跟标、托盘、强制单实例 Host、多资源单请求批量 API、find「加载更多」。

## 2. 组件与职责

```text
[Explorer 右键]
    └─ Sparse package: IExplorerCommand (DLL，尽量无业务)
           └─ 即发即忘启动 EverythingLabeled.ExplorerHost.exe
                  │  读 %AppData%/EverythingLabeled/config.json
                  │  HTTP Bearer → Service /v1
                  └─ WPF 单窗口（按 mode 切换面板）
```

| 组件 | 职责 | 不做 |
|---|---|---|
| **稀疏包 + `IExplorerCommand`** | 子菜单；尽量启停菜单项（如看标签仅单选）；写 `--paths-file` 并启动 Host；空白处仅「按标签查找」 | **不做**硬过滤上限/存在性/HTTP；不读 token；不画业务 UI；**不等待** Host 退出 |
| **`ExplorerHost`（WPF）** | 全部硬规则与业务 UI；路径→URI（共享库）；批量进度/取消/汇总；打开命中；读 config；启动探测 | 不直连 SQLite；无关系 CRUD；无 `expand` |
| **共享 PathUri 库** | 与 CLI 同一套规范化 / UNC / symlink·junction / kind | — |
| **Service** | 已有 `/v1` | — |

### 2.1 进程与路径传递

- Command DLL **out-of-proc、即发即忘**启动 Host；UI 不进入 `explorer.exe`。
- v1 **不**强制单实例：可同时开多个 Host。
- 启动参数：`--mode tag|untag|tags|find`；有选中时另加 `--paths-file <path>`。
- **`--paths-file`**：放在 `%LOCALAPPDATA%\EverythingLabeled\tmp\`（或等价私有目录），随机文件名，ACL 尽量仅当前用户；UTF-8，一行一个绝对路径；Host **读完即删**。若 Host 崩溃残留：下次启动或 DLL 写入前清理超过 **N 分钟**（建议 60）的旧文件。空白处查找 → `--mode find` 且**不传** `--paths-file`。
- 有选中时即使点「按标签查找」，也可不传路径文件（查找与选中无关）；若传入，Host **忽略**其内容。

## 3. 菜单与选中规则

### 3.1 子菜单「EverythingLabeled」

文案（简体中文）：打标 / 去标 / 看标签 / 按标签查找。

| 项 | 有选中时 | 文件夹空白处 |
|---|---|---|
| 打标 | ✓（DLL 不因数量灰掉；Host 校验 ≤100） | ✗ |
| 去标 | ✓（同上） | ✗ |
| 看标签 | ✓（**仅 1 项**；多选时 DLL/Host 禁用） | ✗ |
| 按标签查找 | ✓（**忽略选中**，全局查询） | ✓ |

### 3.2 合法选中（硬规则在 Host）

- 仅**真实文件系统**上的文件与文件夹（本地盘符路径或 UNC），能稳定得到路径并规范为 `file://`。
- **不支持**（丢弃）：「此电脑」等虚拟根、库、回收站、压缩包内虚拟项、未解析命名空间对象；**不**把 `.lnk` 解析成目标（若 `.lnk` 是磁盘文件，则身份为该快捷方式文件路径）。
- 混选含非法项：**丢弃非法项并继续**，顶部提示「已忽略 N 个不支持的项」；合法数为 0 → 提示退出；打标/去标去重后 **>100** → **整次拒绝**（不静默截断），提示「最多 100 项」。DLL **不**因数量禁用打标/去标。
- 多路径按规范化 `file://` **去重**后再计数与提交。

### 3.3 多选语义

| 动作 | 行为 |
|---|---|
| 打标 / 去标 | 对全部合法去重后选中项生效（每资源一次 HTTP） |
| 看标签 | 仅单选 |
| 按标签查找 | 与选中无关 |

## 4. 路径 → URI 与 `kind`

与 [CLI §3–4](./2026-09-26-cli.md)、[数据模型 §2](./2026-09-23-data-model.md) 一致；**实现必须与 CLI 共用同一库**，禁止分叉两套规则。

| 规则 | 说明 |
|---|---|
| 盘符路径 | `file:///C:/path/to/item`（盘符大写、`/` 分隔、非根去尾 `/`） |
| UNC | `file://server/share/...` |
| 符号链接 / junction / 挂载点 | URI **身份**用壳给出的路径；存在性与 `kind` 看**解析后目标**；断链或不存在 → 打标该项不可提交 |
| 存在性 | **仅打标**需要；去标 / 看标签不检查本地存在 |
| `kind` | 仅打标传递：目录 → `folder`，否则 → `file`。Explorer v1 **不**打 `url` |
| `displayName` | **永不传**（与 CLI 一致；与扩展可传 title **有意分歧**） |

**打标打开时预检**：进入 `tag` 面板时扫描路径——不存在/断链标为无效并**默认排除**出待提交列表（显示原因）；提交前再快速复核。排除后待提交数为 0 → 阻断提交。

## 5. Host UI 与 API 映射

### 5.1 配置（只读）

默认路径：`%AppData%/EverythingLabeled/config.json`。

```json
{
  "baseUrl": "http://127.0.0.1:5123",
  "token": "<local-secret>",
  "confirmUntag": true,
  "previewLimit": 16,
  "maxConcurrency": 4
}
```

| 键 | 规则 |
|---|---|
| `baseUrl` | 必填。与扩展同级校验：scheme 仅 `http`；host **仅** `127.0.0.1`；**拒绝** `localhost` / `[::1]` / 其它 host；pathname 为空或 `/`；拒绝 query/hash。缺或非法 → 阻断 |
| `token` | 必填非空；缺 → 阻断并提示配置路径 |
| `confirmUntag` | 可选；**缺省键或省略 = `true`** |
| `previewLimit` | 可选；缺省 **16**；合法范围 **1–50**。与扩展默认 8 / 上限 16 **有意不同** |
| `maxConcurrency` | 可选；批量打标/去标并发上限；**缺省键或省略 = 4**；合法范围建议 **1–16**（越界时夹紧到范围或视为缺省 4，实现选定一种并在日志说明；推荐越界 → 当缺省 4） |

- 每个请求：`Authorization: Bearer <token>`，`Accept: application/json`。
- 整请求超时 **30s**（与 CLI 对齐）。
- Host **不写回** config；v1 **不**提供 `--base-url` / `--token` 覆盖。
- 未知键忽略。

### 5.2 启动探测（必做）

每次打开 Host：

1. `GET /v1/health`（可无 token）→ 失败则置顶阻断，提示服务未运行或地址错误。
2. 若已有 token → `GET /v1/meta` → 失败则置顶阻断，提示 token 无效。

探测通过前禁用打标/去标/看标签/查找业务操作。

### 5.3 面板行为

| mode | UI |
|---|---|
| `tag` | 列出待提交路径（含预检排除项说明）；**每次提交一个别名** + suggest 防抖；勾选「创建缺失标签」（默认关，不写 config，本会话保留）；进度 + 可取消 + 成功/失败/已取消未发送汇总；成功后**保持窗口** |
| `untag` | **每次一个别名**。单选：可从 `GET tags` 列表点选删除（点一次 = 一个别名）或手输。多选：一个别名作用于全部路径（无「共有标签」合并 UI）。`confirmUntag != false` → 确认框（文案含路径数量）；取消则不发请求。成功后保持窗口 |
| `tags` | 单路径列表；`404 resource_not_found` → 空列表。可同窗口切到打标/去标（路径保留） |
| `find` | 输入 `q` **原样**上传；**仅** `GET /v1/query/preview`（无 expand、无「加载更多」）；双击或「打开」按 §5.5；打开后**保持窗口** |

- `createMissing = true` 当且仅当打标勾选；去标永不 `createMissing: true`。
- 每次提交别名恰好 **1** 个（与扩展 GUI 一致；批量维度是多路径）。空别名 → 本地提示、不发请求。

### 5.4 API 映射

| UI 动作 | HTTP |
|---|---|
| 启动探测 | `GET /v1/health`；有 token 再 `GET /v1/meta` |
| 看标签 | `GET /v1/resources/tags?uri=` |
| 打标（每资源一次） | `POST /v1/resources/tags`：`uri` + `kind` + `add: [一个别名]` + `createMissing`←勾选；不传 `displayName` |
| 去标（每资源一次） | 同上，`remove: [一个别名]`；`createMissing: false` |
| 联想 | `GET /v1/tags/suggest?q=&limit=`（默认 limit 10；失败静默） |
| 查找预览 | `GET /v1/query/preview?q=&limit=`（`limit`←`previewLimit`；不传 expand） |

**批量执行**

- 并发度 = `maxConcurrency`（缺省 4）。
- 提供「取消」：停止**尚未发出**的请求；已发出的不撤销；汇总「已成功 / 已失败 / 已取消未发送」。
- **401 / 缺配置**：整批中止剩余项。
- 其它错误（含 `tag_not_found`、超时、5xx、本地预检失败）：**按项继续**并计入汇总。

### 5.5 打开命中（find）

| `kind` / URI | 行为 |
|---|---|
| `file`（`file://`） | `explorer /select,"<本地路径>"` |
| `folder`（`file://`） | 在资源管理器中打开该目录 |
| `url` 且 `http(s)` | 系统默认浏览器打开 |
| 其它 | 列表可显示，标注不可打开 |

v1 单次打开一项（不对 preview「全部打开」）；无全量 `query` 翻页。

## 6. 错误与空态

| 情况 | 行为 |
|---|---|
| 无 config / 缺 token / `baseUrl` 非法 | 阻断，说明配置路径与规则 |
| health 失败 | 提示服务未运行或地址错误 |
| meta / 401 | 提示 token 无效；批量中遇 401 中止剩余 |
| 去重后合法 0 或 >100 | 本地提示，不发业务请求 |
| 混选丢弃非法项 | 提示已忽略 N 项 |
| 打标预检不存在/断链 | 默认排除；提交复核仍失败则计入失败汇总 |
| `tag_not_found`（未勾选创建） | 计入该项失败，继续其余项 |
| preview 0 条 | 成功空态 |
| suggest 失败 | 静默无联想 |
| 用户取消批量 | 汇总含已取消未发送 |

批量 `add`/`remove` 的服务端语义以 [HTTP API](./2026-09-26-http-api.md) 为准。

## 7. 安装与注册（规格边界）

- 交付物：稀疏包（菜单 / `IExplorerCommand` DLL）+ `ExplorerHost.exe`（可同安装包布局）。
- 安装后：右键子菜单可见；Host 能读到与 CLI 同一 `config.json`。
- 开机启动、首次 token、端口写入等归 [配置与安装](./2026-09-23-roadmap-backlog.md)（事项 6），不在本规格展开。

## 8. 与 CLI / 扩展对照

| 点 | Explorer | CLI | 浏览器扩展 |
|---|---|---|---|
| 配置来源 | AppData | AppData（可 flag/env 覆盖） | `chrome.storage.local`（手动） |
| `baseUrl` host | 仅 `127.0.0.1` | 未在本规格强制同级校验 | 仅 `127.0.0.1` |
| PathUri 实现 | **与 CLI 共享库** | 同左 | N/A（url 规则） |
| `displayName` | 不传 | 不传 | 可传 tab title |
| 每次提交别名 | 1 个 | 可多个（≤100） | 1 个 |
| `createMissing` 默认 | 勾选，默认关 | `--create`，默认关 | 勾选，默认关 |
| `confirmUntag` | AppData，缺省 true | 同左；可用 `--yes` | 扩展 storage，缺省 true |
| `expand` | 不提供 | 可选 | 不提供 |
| `previewLimit` 缺省 | 16（1–50） | N/A | 8（1–16） |
| `maxConcurrency` | AppData，缺省 4 | N/A（单 target） | N/A |
| 多 target | 去重后最多 100，客户端循环 | 单 target | 单 URL |

## 9. v1 非目标

- 标签 / 关系 CRUD UI、`expand=child`、find「加载更多」/`query` 翻页
- 解析 `.lnk` 到目标、虚拟命名空间、文件改名/移动跟标
- 系统托盘、Everything 式全盘索引
- 服务端「多资源一次绑标签」API（v1 用客户端循环）
- 强制单实例 Host、应用商店上架细则、完整 i18n 框架

## 10. 验收清单（规格层）

- [ ] 稀疏包：子菜单四项（中文项名）；空白处仅「按标签查找」；看标签仅单选；Invoke 即发即忘
- [ ] Host 硬规则：非法丢弃并提示 N；URI 去重；>100 拒绝不截断；DLL 不因数量灰打标/去标
- [ ] PathUri 与 CLI **同一库**；含 UNC / symlink / junction；打标预检排除无效项
- [ ] 启动必做 health（+ meta）；`baseUrl` 仅 127.0.0.1
- [ ] 打标/去标每次一别名；批量并发=`maxConcurrency`（缺省 4）；可取消；401 中止、其它按项继续
- [ ] find：仅 preview；打开后窗口保持；无 expand
- [ ] paths-file 私有 tmp + 读完删除；只读 AppData config（含 `previewLimit` / `maxConcurrency`）
- [ ] Command DLL 不承载业务 HTTP；Host 崩溃不拖死 Explorer（预期）
