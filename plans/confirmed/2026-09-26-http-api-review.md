# HTTP API 契约审核确认记录（网状标签 / 存取效率）

日期：2026-09-26  
方式：grill 审核（问题 + 推荐答案 → 用户确认）  
考察重点：

1. 知识图谱式网状标签结构的支持  
2. 标签存 / 取 / 查效率的保证  

已落盘同步：

- [HTTP API 契约](../2026-09-26-http-api.md)（主）
- [数据模型](../2026-09-23-data-model.md)（batch 集合校验、expand 闸门、缓存键交叉项）
- [待办事项列表](../2026-09-23-roadmap-backlog.md)（#1 / #2 措辞）

对 @plans/2026-09-26-http-api.md 的审核确认，确认点如下：

---

## Q1 — 标签邻域要不要进契约？

**问题**：数据模型是网状 `TagRelation`，但 `GET /v1/tags/{id}` 不含 children / parent / related；客户端只能反复打 `/relations` 拼图。

**确认**：选推荐 **B**。

- 专用邻域接口：`GET /v1/tags/{id}/neighbors`
- Tag 详情保持轻量，不把图快照嵌进 Tag JSON
- 与边表 `/relations` 分工：邻域带对端摘要，边表做原始 CRUD / 排查

---

## Q2 — `related` 是否参与查询集合展开？

**问题**：契约预留 `expand=child`；网状场景下搜「工作」时要不要把 `related` 邻居并进命中集合？

**确认**：选推荐 **A**。

- v1 查询热路径 **仅** `expand=child`（树闭包）
- `related` 只影响浏览 / 推荐（走 `neighbors`），**不**进 query 集合运算
- 多跳图展开不与 Everything 式标签查询绑死

---

## Q3 — `GET /v1/relations` 无过滤是否允许？

**问题**：过滤可选时，无 `from`/`to` = 全图表分页，是最贵读路径之一。

**确认**：选推荐 **A**（Q6 钉死为保留列表 + 强制过滤，不用 C）。

- 强制：至少提供 `fromTagId` **或** `toTagId`，否则 `400`
- 索引键：`(FromTagId, Kind, Seq)` / `(ToTagId, Kind)`；API 必须走端点过滤
- 全图导出留给日后显式导出接口

---

## Q4 — 写边是否要批量？

**问题**：打标已有批量；`POST /v1/relations` 一次一条，建小知识网成本高（往返 × 校验 × `data_version` 连跳）。

**确认**：选推荐 **B**。

- 增加 `POST /v1/relations/batch`
- **all-or-nothing** 单事务，成功只 `data_version` +1 一次

---

## Q5 — 效率保证要不要写进 HTTP 契约？

**问题**：索引在数据模型里，API 几乎未约束「哪些读必须 O(indexed)」。

**确认**：选推荐 **A**。

- 契约增加「性能边界」硬约束（禁模式 + 必选过滤）
- **不**写毫秒 SLA

---

## Q6 — 关系列表入口钉死 A 还是 C？

**问题**：Q3 曾写「A 或 C」；已有 `neighbors` 后，裸 `/relations` 列表怎么收？

**确认**：选推荐 **A**。

- 保留 `GET /v1/relations`，强制端点过滤
- `neighbors` = 图浏览；`/relations` = 原始边
- 不删裸列表、不并入 tag-scoped 唯一入口

---

## Q7 — `neighbors` 默认语义与载荷？

**确认**：选推荐方案。

- `direction` 默认 **`both`**（相对路径 `{id}`）
- `kind` 可选；缺省 = `child` + `related`
- 载荷：边字段 + 对端 Tag 摘要（`id, name, namespace, nameInitials`）；**不含**对端 `aliases`
- **禁止**嵌套对端邻域
- 分页沿用 `offset` / `limit`；扁平 `items`，每项带 `kind`（及 `direction`）
- 排序：可比时用边上 Seq 规则

---

## Q8 — `POST /v1/relations/batch` 边界？

**确认**：选推荐方案。

- 上限 **100** 条
- 允许同一请求混 `child` / `related`
- v1 batch **仅创建**（删除单条 DELETE；重排仍 `reorder`）
- 失败：对应 `409` / `422`；`details` 带 `index` + 该边 from/to/kind

---

## Q9 — `expand=child` 性能闸门是否现在写入契约？

**问题**：语法仍归待办 #2，成本上限要不要先占位？

**确认**：选推荐 **A 弱化版**。

- 契约写明必有 `maxDepth`（默认 **8**）与 `expandedTagLimit`（默认 **500**）
- 超限 → `422 expansion_limit_exceeded`（后续查询语言审核对「深度截断 vs 422」另有修订，见 [查询语言审核确认](./2026-09-26-query-language-review.md) Q1）
- 精确语法仍指向待办 / 查询语言细则

---

## Q10 — 性能边界小节还写死哪些禁模式？

**确认**：选推荐——下列全部写入。

1. `suggest` / `GET /v1/tags?q=`：最小有效前缀；空 / 空白 `q`：`suggest` → `400`；无 `q` 的 tags = 普通列表  
2. **禁止** substring / `%foo%`  
3. `GET /v1/resources` 的 `uri` **仅精确**  
4. `query` / `preview` 缓存键必须含 `expand` 及相关上限参数  
5. 单次打标 `add`+`remove` 元素总数上限（**100**）

---

## Q11 — batch 成环 / 单父校验：集合还是数组顺序？

**确认**：选推荐 **A**。

- **集合语义**：先对「旧图 ∪ 本批全部新边」校验，全部合法再一次性写入
- `details.index` 报第一个被判定违规的 item（可按数组序扫描）
- 对外语义：本批变更合起来合法才提交

---

## Q12 — `maxDepth` / `expandedTagLimit` 可否客户端覆盖？

**确认**：选推荐 **B**。

- 允许 query **下调**（如 `maxDepth=3`）
- 不得超过服务端上限；超过 → `422`
- 不允许借参数把服务器打穿

---

## Q13 — `related` 是否自动建反向边？

**确认**：选推荐 **A**。

- v1 严格有向；要双向就 batch 里显式写两条
- 与 UNIQUE(from,to,kind) 及 neighbors `direction=in|out` 一致
- UI 要无向 → 客户端一次 batch 写两条

---

## Q14 — 仅 `toTagId` 过滤时 `/relations` 怎么排序？

**确认**：选推荐 **A**。

- 固定：`CreatedAt ASC, Id ASC`（忽略 Seq）
- 文档写明：**Seq 排序仅保证同一 `(fromTagId, kind)` 下有意义**
- 「找父」场景 `child` 至多一条，排序无所谓

---

## Q15 — 是否立刻回写计划文档？

**确认**：选推荐 **A**。

- 理解一致后已改：`http-api.md`（主）、`data-model.md`、`roadmap-backlog.md`（薄同步）
- 对应提交：`#4 完善 HTTP API：邻域、关系 batch、expand 闸门与性能边界`

---

## 收口时约定的实现提醒（非独立题号）

- 图浏览走 `neighbors`；边运维走带过滤的 `/relations`；禁止无过滤全图表
- 查询集合只沿 `child` 展开；`related` 不进 expand、不自动反向
- 关系 / 打标批量上限与集合校验是契约硬约束，不是实现细节
- 效率靠 §9 禁模式保证，不承诺毫秒 SLA
- 本轮 expand 默认 `expandedTagLimit=500` 已写入当时契约；若与后续查询语言细则数值不一致，以**较新已确认文档**为准，并回写交叉引用
