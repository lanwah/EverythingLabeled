# MCP 规格审核确认记录

日期：2026-09-27  
方式：grill 审核（问题 + 推荐答案 → 用户确认）  
已落盘同步：

- [MCP 规格](../2026-09-27-mcp.md)
- [路线图 backlog](../2026-09-23-roadmap-backlog.md)（#6 已确认 + 审核链接）

对 @plans/2026-09-27-mcp.md 的 grill 确认点如下。

---

## Q1 — 相对路径 cwd

**确认**：选推荐 A。

- 相对路径相对 **MCP 进程 cwd**（宿主决定）
- 文档提醒：不确定时用绝对路径；mcp.json 可设 `cwd`

---

## Q2 — 带 notice 的成功形状

**确认**：选推荐 A。

- 成功统一 `{ "data", "message" }`；无 notice 时 `message: null`

---

## Q3 — 工具名前缀

**确认**：选推荐 B。

- 统一 `el_` 前缀（如 `el_tag`）

---

## Q4 — host flags

**确认**：选推荐 A。

- v1 **必须**支持 `--config` / `--base-url` / `--token`（优先级同 CLI）

---

## Q5 — stdout 污染

**确认**：选推荐 A。

- 规格写死：协议模式禁止往 stdout 打日志；诊断走 stderr

---

## Q6 — `tags` 工具短名

**确认**：选推荐 A。

- `el_tags`（非 `el_list_tags`）

---

## Q7 — 未确认 untag 的 error.code

**确认**：选推荐 B。

- 专用码 `confirmation_required`

---

## Q8 — `aliases` 形态

**确认**：选推荐 A。

- 仅 `string[]`；不接受单字符串隐式分词

---

## Q9 — `IElApi` / Client 覆盖面

**确认**：选推荐 B。

- Client 做成较完整 HTTP 面；Tools 只用子集  
- （Q18）边界 = **已确认客户端所需**路由，非强制全 API、也非仅 MCP 六工具

---

## Q10 — 探活

**确认**：选 **B**（非初推 A）。

- 增加只读 `el_health` → `GET /v1/health`

---

## Q11 — `el_health` 与 token

**确认**：选推荐 A（不做 C）。

- 免 token；只需能解析 `baseUrl`
- v1 不加 `el_meta`

---

## Q12 — 与 CLI `--json` 分歧

**确认**：选推荐 A。

- 标明**有意分歧**；不改 CLI 成功形状

---

## Q13 — `el_query` 默认 limit

**确认**：选推荐 A。

- 保持与 CLI 同默认 50；Description 建议先 `el_preview`

---

## Q14 — 并发限流

**确认**：选推荐 A。

- 规格不限制；不绑定 Explorer `maxConcurrency`

---

## Q15 — mcp.json 示例与 `cwd`

**确认**：选推荐 A。

- 示例含可选 `"cwd"`；不强制

---

## Q16 — Description 语言

**确认**：选推荐 A。

- 运行时 Description / 参数说明 **英文**；规格正文可中文

---

## Q17 — `--help` / `--version`

**确认**：选推荐 A。

- 支持离线；进入 MCP 会话后不得污染 stdout

---

## Q18 — Client 边界（细化 Q9）

**确认**：选推荐 B。

- 按 CLI / MCP / Explorer / 扩展 v1 所需表面实现；可超集于 MCP Tools

---

## Q19 — `isError`

**确认**：选推荐 A。

- `{ error }` → `isError: true`；含 notice 的成功 → `isError: false`
