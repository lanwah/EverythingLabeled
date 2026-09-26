# CLI 规格审核确认记录

日期：2026-09-26 / 2026-09-27  
方式：grill 审核（问题 + 推荐答案 → 用户确认）  
已落盘同步：

- [CLI 规格](../2026-09-26-cli.md)
- [本地守护进程架构](../2026-09-22-local-daemon-architecture.md)（config 示例增加 `confirmUntag`）

对 @plans/2026-09-26-cli.md 的审核确认，确认点如下：

---

## Q1 — 单 target 还是多 target？

**问题**：`el tag a.txt b.txt 工作` 时 `b.txt` 会被当成别名。

**确认**：选推荐。

- v1 **坚持单 target**：首个位置参数 = target，其后全部是 alias
- 多文件靠壳循环；help 中写明约定

---

## Q2 — `el tags` 是否必须本地存在？

**问题**：`untag` 允许已删文件，`tags` 原稿要求存在，查看幽灵资源不便。

**确认**：选推荐。

- `tags` 与 `untag` 一致：**不检查**本地存在
- 仅 `tag` 强制存在（需推断 `kind` / 隐式建资源）

---

## Q3 — `--json` 输出形状？

**问题**：成功时打完整信封还是仅 `data`？

**确认**：选推荐。

- 成功 stdout = **仅 API `data`**
- 错误 stderr = `{ "error": { "code", "message" } }`

---

## Q4 — 隐式建资源是否传 `displayName`？

**确认**：选推荐。

- v1 **一律不传**（null）；只保证 `uri` +（`tag` 时）`kind`

---

## Q5 — 符号链接：身份 vs kind？

**确认**：选推荐。

- URI 身份 = 用户路径规范化结果（不跟随）
- 存在性 / `kind` = 解析后目标
- （Q8）断链 → `tag` 报 `path_not_found`

---

## Q6 — 幂等打标/去标退出码？

**确认**：选推荐。

- HTTP 200 → **一律 exit 0**（`added`/`removed` 可空）
- 人类模式可打印「无变更」

---

## Q7 — 查询人类输出是否附 URI？

**确认**：选推荐。

- 默认每行仅 `displayName` 否则 URI（管道友好）
- 完整字段用 `--json`；v1 不加 `--long`

---

## Q8 — 损坏的 symlink？

**确认**：选推荐 → `path_not_found`，不发请求。

---

## Q9 — UNC 路径？

**确认**：选推荐。

- v1 **支持**；规范为 `file://server/share/...`

---

## Q10 — 环境变量覆盖？

**确认**：选推荐。

- `EL_TOKEN`、`EL_BASE_URL`（Q23 另加 `EL_CONFIG`）
- 优先级：**flag > 环境变量 > config.json**

---

## Q11 — `el version` / 探活？

**确认**：选推荐。

- 全局 **`el --version`**（客户端版本，不调 API）
- **不加** `health` 子命令

---

## Q12 — 缺少 alias 参数？

**确认**：选推荐 → 本地 `bad_request`，exit 1，至少 1 个 alias。

---

## Q13 — 补全时服务不可达？

**确认**：选推荐 → **静默空补全**；显式命令仍按错误表报错。

---

## Q14 — 查询软失败 `message`？

**确认**：选推荐。

- 默认与 `--json`：非空 `message`（notice）→ **stderr**
- stdout 在 `--json` 下仍只含 `data`；exit 0

---

## Q15 — 离线全局 flag？

**确认**：选推荐。

- `--version` / `-h` / `--help`：**不读 config、不连服务**

---

## Q16 — 查询串以 `-` 开头？

**确认**：选推荐。

- `<q>` / `<prefix>` **恰好一个**位置参数；多段 → `bad_request`
- 以 `-` 开头须 `el query -- -archived`
- help 给负词示例

---

## Q17 — 空 / 空白 `q` 或 `prefix`？

**确认**：选推荐 → 本地先拦（trim 后空 → `bad_request`）。

---

## Q18 — `completion` 非法壳名？

**确认**：选推荐 → 仅 `powershell`；其它或缺失 → `bad_request`。

---

## Q19 — HTTP 401 退出码？

**确认**：选推荐 → **exit 3**（4xx）；exit 2 仅配置/TCP/超时。

---

## Q20 — `-c` 与 `--config`？

**确认**：选推荐。

- `-c` = `--create`
- `--config` **无短选项**

---

## Q21 — `untag` / `tags` 是否传 `kind`？

**确认**：选推荐。

- **仅 `tag` 传 `kind`**
- `untag` / `tags` 不传

---

## Q22 — `completion powershell` 要不要 config？

**确认**：选推荐。

- **打印脚本可离线**
- 脚本运行时 suggest 再读 config；失败静默空（Q13）

---

## Q23 — `EL_CONFIG`？

**确认**：选推荐 → 支持；`--config` > `EL_CONFIG` > 默认路径。

---

## Q24 — HTTP 超时？

**确认**：选推荐 → 默认 **30s**；超时归连接失败（exit 2）；v1 无 `--timeout`。

---

## Q25 — `tag --create` 人类输出是否提新建？

**确认**：选推荐 → 有 `createdTags` 时附 `created:` 行。

---

## Q26 — config 多余字段？

**确认**：选推荐 → **忽略未知键**；只校验 `baseUrl`/`token` 非空。

---

## Q27 — 本地错误码是否拆细？

**确认**：选推荐 → v1 **归并**现有码；细节靠 `message`。

---

## Q28 — `untag` 二次确认？

**问题**：去标是直接执行还是要确认？

**确认**：要确认；做成开关，默认打开。

### Q28a — 确认范围

**确认**：选推荐 → **仅 `untag`**（`tag --create` 不另加确认）。

### Q28b — 非交互环境

**确认**：选推荐 → 非 TTY 且需要确认又无 `--yes` → 本地失败，提示加 `--yes`；不从管道读 `y`。

### Q28c — 交互提示

**确认**：选推荐 → `[y/N]`；仅 `y`/`Y` 继续；默认 N；取消 exit 1。

### Q28d — 短选项

**确认**：选推荐 → `-y` = `--yes`。

### Q28e — 开关落点

**确认**：选推荐。

- config：`confirmUntag`（缺省 = `true`）
- 单次：`--yes` / `-y` 跳过
- **无**反向 `--confirm` flag；持久关闭靠 config `false`

---

## 落盘对照

| 主题 | 规格位置 |
|---|---|
| 配置 / env / 超时 / 离线 help | CLI §2 |
| 单 target、`--`、kind 仅 tag | CLI §3 |
| tags 不查存在、UNC、symlink | CLI §4 |
| untag 确认、created 摘要 | CLI §5 |
| json / notice / 退出码 | CLI §6 |
| completion 离线与静默 | CLI §7 |
