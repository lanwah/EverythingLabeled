# Explorer 上下文菜单包（#14）实现计划讨论确认记录

日期：2026-10-07  
方式：主线澄清（brainstorm，带推荐答案 → 用户确认）Round 0；实现计划审核 grill Round 1–3（Q1–Q16 均选推荐 A）  
已落盘同步：

- [Explorer 集成规格](../2026-09-27-explorer-integration.md)（产品规格不改行为；[规格审核](./2026-09-27-explorer-integration-review.md)；§11 链实现计划）
- [Explorer 实现计划](../2026-10-07-explorer-impl.md)
- [路线图 backlog](../2026-09-23-roadmap-backlog.md)（#14 已确认实现计划）
- [项目看板](../../project.md)

前提（未作为开放题，与规格一致）：稀疏包 `IExplorerCommand` + 进程外 WPF `ExplorerHost`；只走 Service HTTP + Bearer；PathUri 与 CLI 同库；读 AppData、不写回；四菜单项；批量去重后 ≤100；不做安装器/关系 CRUD/单实例/UI 自动化门禁。

---

## A. Round 0 — 主线澄清（brainstorm）

### Q-B1 — 实现计划是否含安装注册

**问题**：#14 实现计划 v1 要不要包含「可安装注册」，还是只交付可手动注册的工程 + Host？

| 选项 | 含义 |
|---|---|
| **A** | 计划含：工程布局、DLL/Host、本地开发用注册步骤、手工验收；**不做**正式安装器（留给 #6） |
| B | 计划只做 Host + 命令行可测路径，菜单注册整段延后 |
| C | 计划绑定完整安装/卸载（接近 #6） |

**确认**：选推荐 **A**。

### Q-B2 — sln 项目怎么切

**问题**：稀疏包 DLL 与 `ExplorerHost` 放在 sln 里怎么切项目？

| 选项 | 含义 |
|---|---|
| **A** | 两个项目：`EverythingLabeled.ExplorerCommand` + `EverythingLabeled.ExplorerHost`；都进现有 sln；Host 引用 Client / PathUri |
| B | 三项目：再拆 Explorer 领域库（路径文件、批量编排），DLL/Host 都引用它 |
| C | 仅 Host 进 sln；Command DLL 单独目录/解决方案 |

**确认**：选推荐 **A**。（grill Q7 另增 `ExplorerShared` 仅 paths-file，不改「业务在 Host」）

### Q-B3 — 自动化测试怎么划

**问题**：#14 实现计划的自动化测试怎么划？

| 选项 | 含义 |
|---|---|
| **A** | Host 侧可测逻辑用 xUnit；Command DLL 无自动化 UI/COM 测；菜单注册与右键靠手工验收 |
| B | 再加 ExplorerHost UI 自动化（FlaUI 等）进门禁 |
| C | 几乎全靠手工；sln 不新增 Explorer 相关测试项目 |

**确认**：选推荐 **A**。

### Q-B4 — DLL 如何找到 Host

**问题**：稀疏包启动 Host 时，`ExplorerHost.exe` 路径怎么定？

| 选项 | 含义 |
|---|---|
| **A** | 与 Command DLL **同目录**；找不到则提示一次后返回 |
| B | 读注册表 / AppData 里的 `hostPath` |
| C | 固定 `%LocalAppData%\EverythingLabeled\bin\ExplorerHost.exe` |

**确认**：选推荐 **A**。

### Q-B5 — 开发期菜单注册方式

**问题**：开发期把 `IExplorerCommand` 挂进 Explorer，采用哪种注册方式？

| 选项 | 含义 |
|---|---|
| **A** | **稀疏包 + 外部 location**；计划写清 `Add-AppxPackage -Register`（或等价）与卸载；与 v1 产品形态一致 |
| B | 经典 COM / `DllRegisterServer`（与规格「稀疏包」分叉） |
| C | 先不做菜单注册，只用命令行起 Host |

**确认**：选推荐 **A**。

### Q-B6 — 实现切法

**问题**：三条实现切法选哪条？

| 选项 | 含义 |
|---|---|
| **A（方案 1）** | **先 Host、后菜单**：先落 Host（args / paths-file / config / 探测 / 四面板逻辑 + xUnit），再落 Command 稀疏包与注册 |
| B（方案 2） | 先最小 DLL + 注册，再填 Host |
| C（方案 3） | 按 mode 垂直切片 |

**确认**：选推荐 **A（方案 1）**。设计摘要 §1–4（工程布局、启动契约、测试验收、任务切分）均 **ok**。

---

## B. Round 1 — grill（Config / Client / 交付）

### Q1 — `previewLimit` / `maxConcurrency` 放哪

**问题**：计划把两键塞进共享 `ElConfig`（CLI/MCP 也会带上默认 16/4）。也可 Host 只读这两键、不改 `ElConfig` 形状。

| 选项 | 含义 |
|---|---|
| **A** | 扩展 `ElConfig` + `ReadFile`；CLI/MCP 忽略两字段 |
| B | Host 单独解析这两键；`ElConfig` 只保留三键 |
| C | 新建 `ElAppDataConfig` 含五键，CLI 仍用旧三键 API |

**确认**：选推荐 **A**。

### Q2 — `baseUrl` scheme

**问题**：规格要求仅 `http` + host `127.0.0.1`。现 `ElConfigLoader` 允许 `https`。计划原只写 Host 再校验 host。

| 选项 | 含义 |
|---|---|
| **A** | Host 在 Load 后强制：`scheme == http` **且** `Host == 127.0.0.1` |
| B | 只卡 host，允许 `https://127.0.0.1` |
| C | 把「仅 http + 127.0.0.1」下沉进 Client 全局（CLI 也变严） |

**确认**：选推荐 **A**。

### Q3 — 环境变量

**问题**：计划写对 `ElConfigLoader` 传空 env（忽略 `EL_*`）。若用户 shell 里设了 env，Host 仍只看 AppData。

| 选项 | 含义 |
|---|---|
| **A** | 空 env + 空 overrides |
| B | 传入真实 `Environment`，与 CLI 同优先级 |
| C | 仅允许 `EL_CONFIG` 改路径，仍忽略 base/token env |

**确认**：选推荐 **A**。

### Q4 — 同目录 Host 交付形态

**问题**：Command DLL 旁要有 `ExplorerHost.exe`。framework-dependent 还要一堆 dll；self-contained 体积大。

| 选项 | 含义 |
|---|---|
| **A** | 开发布局：`dotnet build` 后把 Host **输出目录整份**拷到 Command 输出/包 layout；不强制 single-file |
| B | `PublishSingleFile` + self-contained 单 exe |
| C | 只拷 exe，假定全局已装 Desktop 运行时 |

**确认**：选推荐 **A**。

### Q5 — `MetaAsync` 放哪

**问题**：探测要 `GET /v1/meta`，当前 `IElApi` 没有。

| 选项 | 含义 |
|---|---|
| **A** | 给 `IElApi`/`HttpElApi` 加 `MetaAsync` |
| B | Host 用裸 `HttpClient` 只打 meta |
| C | 探测只做 health，不做 meta |

**确认**：选推荐 **A**。

### Q6 — 非 Windows 编 sln

**问题**：Explorer 项目如何进解决方案？

| 选项 | 含义 |
|---|---|
| **A** | 正常 `dotnet sln add`；文档写明 `Explorer*` 需 Windows；不搞复杂条件排除 |
| B | 非 Windows 用 props/配置排除 Explorer 项目 |
| C | Explorer 不进主 sln，另开 sln |

**确认**：选推荐 **A**。

---

## C. Round 2 — grill（共享 / 菜单 / UI / 批量）

### Q7 — paths-file 读写代码放哪

**问题**：Host 与 Command 都会碰同一 tmp 目录约定（写/读删/Cleanup）。

| 选项 | 含义 |
|---|---|
| **A** | 抽小库 `EverythingLabeled.ExplorerShared`：tmp 路径、Cleanup、Write、ReadAndDelete；Command/Host 都引用 |
| B | 不抽项目；常量与 Cleanup 各写一份 |
| C | 只放 Host；Command 调 Host 写文件 |

**确认**：选推荐 **A**。

### Q8 — `IExplorerCommand` 怎么挂四个菜单项

**问题**：四个中文菜单项如何实现？

| 选项 | 含义 |
|---|---|
| **A** | 四个独立 Command 类 + Manifest 四项，各自 `Invoke` 带固定 mode |
| B | 一个 Command，用 verb/状态区分 |
| C | 自定义子菜单自绘 |

**确认**：选推荐 **A**。

### Q9 — Command / Host 平台目标

**问题**：目标 CPU 架构？

| 选项 | 含义 |
|---|---|
| **A** | **x64 only**；文档写明；ARM64 以后再说 |
| B | AnyCPU / 双平台尽量都出 |
| C | x86 |

**确认**：选推荐 **A**。

### Q10 — WPF UI 结构（Task 6）

**问题**：UI 用什么结构？

| 选项 | 含义 |
|---|---|
| **A** | code-behind + 少量纯函数；不引入社区 MVVM 框架；需要时手写最小 VM |
| B | CommunityToolkit.Mvvm 全套 |
| C | 纯命令行 Host，无 WPF |

**确认**：选推荐 **A**。

### Q11 — 批量里如何认定「401 中止」

**问题**：规格：401 中止剩余；其它按项继续。

| 选项 | 含义 |
|---|---|
| **A** | `ElError.HttpStatus == 401`（或已映射鉴权码）→ 中止剩余；以 HttpStatus 为主 |
| B | 只看信封 `code` 字符串 |
| C | 任一失败都中止整批 |

**确认**：选推荐 **A**。

### Q12 — Host 误传「看标签 + 多路径」

**问题**：DLL 应灰掉；CLI 调试仍可能传入多路径。

| 选项 | 含义 |
|---|---|
| **A** | Host：`mode=tags` 且规范化后 Included≠1 → 中文提示并禁用业务 |
| B | 静默只取第一项 |
| C | 直接退出码非零无 UI |

**确认**：选推荐 **A**。

---

## D. Round 3 — grill（包身份 / suggest / q / 收尾）

### Q13 — 开发稀疏包身份与签名

**问题**：本地 `Add-AppxPackage -Register` 的包身份怎么定？

| 选项 | 含义 |
|---|---|
| **A** | 仓库内固定开发用 Identity Name / Publisher；文档写侧载/开发者模式；**不**提交私钥；证书用本机自签或测试证书（步骤进 `docs/explorer.md`） |
| B | 每台机器随机包名 |
| C | 本轮不做真实 Register |

**确认**：选推荐 **A**。

### Q14 — suggest 防抖与失败

**问题**：联想请求策略？

| 选项 | 含义 |
|---|---|
| **A** | trim 后长度 ≥1 才请求；250ms 防抖；limit 10；失败 → 空列表不弹错 |
| B | 无防抖，每次按键立刻请求 |
| C | 失败弹 MessageBox |

**确认**：选推荐 **A**。

### Q15 — find 的 `q`

**问题**：规格表写「原样上传」，CLI 惯例是 trim。冲突时以谁为准？

| 选项 | 含义 |
|---|---|
| **A** | **trim 后**提交（空则不请求/本地提示）；钉进实现计划；规格表「原样」以本计划为准，不强制改规格全文 |
| B | 真正原样（含首尾空格）上传 |
| C | 本轮改 Explorer 规格正文 |

**确认**：选推荐 **A**。

### Q16 — grill 收尾产物

**问题**：确认后改哪些文件？

| 选项 | 含义 |
|---|---|
| **A** | 回写本审核文件；同步改实现计划 Global Constraints / File structure；看板下一步改为开工 Task 1 |
| B | 只改计划，不写 confirmed |
| C | 只写审核，计划不动 |

**确认**：选推荐 **A**。
