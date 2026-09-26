# EverythingLabeled 查询语言细则

日期：2026-09-26  
状态：已确认（v1 基线；含信封 notice、闸门截断/触顶分流、别名字符集交叉）  
关联：

- [本地守护进程架构](./2026-09-22-local-daemon-architecture.md)
- [数据模型](./2026-09-23-data-model.md)
- [HTTP API 契约](./2026-09-26-http-api.md)

## 1. 设计摘要

- 查询串：空白分词；段间 `OR`；段内 AND；负词 `-alias`；**无引号、无括号**。
- 子孙展开：**仅** HTTP 参数 `expand=child`（正、负词都展开，含自身）；无词级 `*`。
- 闸门默认：`maxDepth=8`，`expandedTagLimit=512`（兼服务端上限；请求可下调）。深度**截断**；标签数触顶 → `422`。
- 规范化：词面小写 + `OR` 统一；**解析 TagId 之前**生成；段内**保持用户词序**（不重排）。
- 结构非法 / 空 `q` / 参数形状非法 → `400`；未知正词 → `200` 空结果 + 成功信封非空 `message`；展开标签数触顶 / 参数超服务端上限 → `422`。

## 2. 语法与解析

### 2.1 适用范围

- `GET /v1/query?q=`
- `GET /v1/query/preview?q=`
- `q` 必填且非空白；否则 `400 bad_request`
- `GET /v1/tags/suggest` **不**使用本语法（仅别名 / 简拼前缀）

### 2.2 流水线

1. Trim；按 **Unicode 空白** 分词（连续空白 = 一个分隔符）
2. 大小写不敏感的 token `OR` → 运算符
3. 其余为标签词：
   - 以 `-` 开头且长度 > 1 → **负词**（去掉**一个**前导 `-` 后为别名文本）
   - 否则 → **正词**
4. 按 `OR` **拆成若干段（clause）**：段与段 OR；段内正词 AND、负词从该段排除
5. 每段至少 **一个正词**；同段可有多个负词
6. 别名文本经 **`AliasNormalized`（小写）** 解析为 `TagId`
7. **不**用 `Name` / `NameInitials` 解析查询词
8. **不支持**引号短语、括号、`AND` / `NOT` 关键字

别名字符集（含禁止以 `-` 开头、禁止空白/控制字符、禁止保留词 `or`/`and`/`not`）见[数据模型 · TagAlias](./2026-09-23-data-model.md)；与本词法配套，保证正词可写。

### 2.3 非法结构 → `400 bad_request`

| 情况 | 例 |
|---|---|
| 空 / 仅空白 `q` | ` ` |
| `OR` 开头或结尾 | `OR 工作`、`工作 OR` |
| 连续 `OR` / 空段 | `工作 OR OR 生活` |
| 某段无正词 | `-归档`、`工作 OR -归档` |
| 孤立 `-` | `-` |
| 非法 `expand` 值 | `expand=related`、`expand=foo` |

实现注记：词法上连续前导 `-`（如 `--x`）仍按「去掉一个 `-`」切分为负词；因别名不得以 `-` 开头，v1 **不存在**可解析的对应别名，用户文档**不**示范该形式。

### 2.4 合法示例

| `q` | 含义 |
|---|---|
| `工作 待读` | 一段：工作 ∧ 待读 |
| `工作 OR 生活` | (工作) ∨ (生活) |
| `工作 待读 OR 生活 -归档` | (工作 ∧ 待读) ∨ (生活 ∧ ¬归档) |
| `工作 -归档 -草稿` | 工作 ∧ ¬归档 ∧ ¬草稿 |

### 2.5 未知标签

- 任一段中**任一正词**无法解析 → **整句** HTTP `200`，`status: "ok"`，`data.items: []`（`total: 0`）；**不**展开、**不**查库集合；`message` 非空说明原因（见 §6）；服务端日志必含 `reason=unknown_positive_term` 及 token / 段下标 / `normalized_query`
- 动机：宁可不返回，也不在用户以为 OR 扩召回时默默丢掉一段
- 负词无法解析 → **忽略**该负词并继续；**仅**打日志 `reason=unknown_negative_term_ignored`（成功 `message` 保持 `null`，除非另有正词 notice）

## 3. `expand=child` 集合语义

### 3.1 参数与常量

| 参数 | 默认 | 合法区间 | 说明 |
|---|---|---|---|
| `expand` | 省略 | 省略或 `child` | 其它值 → `400 bad_request` |
| `maxDepth` | `8` | 整数 `0…serverMax`（默认 serverMax=8） | 可下调；`0` = 仅自身。大于 serverMax → `422 expansion_limit_exceeded`（`param_exceeds_server_max`）。非整数/负数/空串 → `400 bad_request`（`param_invalid`） |
| `expandedTagLimit` | `512` | 整数 `1…serverMax`（默认 serverMax=512） | 可下调。大于 serverMax → `422`；`0`/非整数/负数 → `400` |

未传 `expand`：不展开；每个词对应单一 `TagId`；缓存键占位见 §4.2。

**实现要求**：`maxDepth` / `expandedTagLimit` 的默认值与服务端上限须在代码中**集中定义**（如 `QueryExpansionLimits`），注释写清：

- 与 HTTP 参数名对应关系
- `maxDepth` = 展开半径（超出深度**静默截断**，不 422）
- `expandedTagLimit` = 硬成本闸门（去重后触顶 → 422，不返回部分结果）
- 默认与上限相同；若实现允许配置覆盖上限，须在运维/配置文档写明

### 3.2 展开算法

对每个已解析标签词（**正、负相同**）：

1. 起点 = 该 `TagId`（**含自身**，depth 0）
2. 沿 `TagRelation` 且 `Kind=child`、方向 From=父 → To=子 向下遍历（BFS/DFS 均可）
3. 只收集 `depth ≤ maxDepth` 的节点；更深子树**不进入集合**（截断）。若发生截断，打日志 `reason=max_depth_truncated`（**不**写入成功 `message`）
4. **整次查询**所有正负词展开结果**合并去重**后的 TagId 个数不得超过 `expandedTagLimit`；触顶 → `422 expansion_limit_exceeded`（`reason=expansion_tag_limit_exceeded`；不返回部分结果）
5. `related` **不**参与展开

顺序：结构校验 → 别名解析 →（正词皆可知后）展开与闸门 → 集合运算。

### 3.3 段内集合运算（展开后）

- 每个**正词**：资源须带有展开集合中 **至少一** 个 TagId
- 每个**负词**：资源不得带有展开集合中 **任何** 一个 TagId
- 段内多正词：上述条件 **AND**
- 段与段：**OR**
- 多段结果**并集**后按资源默认序分页（见 §3.4）

例：`q=工作&expand=child` 命中仅打了「工作」之子标签的资源。

**警告（负词 + 展开）**：`q=工作 -项目&expand=child` 会排除打了「项目」**或其任一子孙**标签的资源（正负对称展开）。不要理解为「只排除精确打了项目的资源」。

### 3.4 并集后排序与分页

`query` / `preview` 多段 OR 并集后的资源序与 `GET /v1/resources` 默认序一致：

1. `CreatedAt` 升序
2. 再相同 → `Id` 升序

然后再应用 `offset` / `limit`。客户端可依赖该稳定序做分页。

## 4. 规范化串与缓存键

### 4.1 `normalized_query`（方案：词面规范化）

在 TagId 解析与展开 **之前** 生成：

1. 分词并识别 `OR` / 正负词
2. 运算符统一为 `OR`
3. 正词别名文本 → 与 `AliasNormalized` 相同的小写
4. 负词 → `-` + 别名文本小写
5. 段内 token 单空格连接，**保持用户顺序**（不字典序重排）
6. 段与段用 ` OR `（两侧各一空格）连接
7. **不**写入 expand / depth / limit

例：`  工作   or  生活 -归档 ` → `工作 OR 生活 -归档`（ASCII 别名会小写，如 `Work` → `work`）。

**缓存碎片（刻意保留）**：`工作 待读` 与 `待读 工作` 段内 AND 语义相同，但 `normalized_query` 不同 → **两个缓存键**。v1 接受本机键空间碎片，换取实现简单与调试时键与用户输入同序。不在 v1 做段内重排或 TagId 集合级语义键（见 §7）。

### 4.2 缓存键

```text
v{data_version}:{op}:{normalized_query}:{expand}:{maxDepth}:{expandedTagLimit}:{limit}:{offset}
```

| 场景 | `expand` | `maxDepth` | `expandedTagLimit` |
|---|---|---|---|
| 未展开 | `none` | `0` | `0` |
| `expand=child` | `child` | 生效值（已校验落在合法区间） | 生效值（默认 512） |

- `op` ∈ `query` | `preview`
- `suggest` 不使用本查询语法；其缓存键不含上述 expand 三维（或恒为 `none`/`0`），由实现与架构约定即可

字段顺序以实现为准，但 **必须包含** 上表全部维度。

## 5. 错误码与响应（查询路径）

成功/错误均使用 [HTTP API 统一信封](./2026-09-26-http-api.md#22-响应信封)：`status` + `message` + `data`。

| 情况 | HTTP | status | data.code（错误时） | 说明 |
|---|---|---|---|---|
| 空 q / 结构非法 / 非法 expand / 参数形状非法 | 400 | `error` | `bad_request` | 日志 `param_invalid` 或结构原因 |
| 参数 `>` 服务端上限 | 422 | `error` | `expansion_limit_exceeded` | 日志 `param_exceeds_server_max` |
| 展开标签数触顶 | 422 | `error` | `expansion_limit_exceeded` | 日志 `expansion_tag_limit_exceeded`；**不含**「深度触顶」 |
| 未知正词 | 200 | `ok` | — | 空列表；`message` 非空；日志 `unknown_positive_term` |
| 未知负词 | 200 | `ok` | — | 忽略该词；`message` 通常 `null`；仅日志 |
| 深度截断 | 200 | `ok` | — | 正常返回截断后集合；仅日志 `max_depth_truncated` |

## 6. 日志 reason 与成功 notice

| reason（日志必填） | 是否写入成功 `message` | 说明 |
|---|---|---|
| `unknown_positive_term` | **是** | 唯一须让 UI 可见的查询软失败 |
| `unknown_negative_term_ignored` | 否 | 仅日志 |
| `max_depth_truncated` | 否 | 仅日志 |
| `param_invalid` | 否（走错误信封 `message`） | HTTP 400 |
| `param_exceeds_server_max` | 否（走错误信封） | HTTP 422 |
| `expansion_tag_limit_exceeded` | 否（走错误信封） | HTTP 422 |
| `alias_rejected_*` | 否 | 别名写入校验（见数据模型）；创建路径 |

成功 notice 时 `message` 为非空 string，建议稳定句式（中英均可，实现固定一种），例如：

`Unknown positive term 'typo'; returning empty result.`

日志字段至少包含：`reason`、原始 `q`、`normalized_query`、相关 `token`、段下标（若适用）、生效 `expand`/`maxDepth`/`expandedTagLimit`。

## 7. 非目标（v1）

- 引号短语、括号、`AND` / `NOT` 关键字
- 词级 `*`、`expand=related`、多跳 related 查询
- 查询词走主名或简拼
- 段内 token 重排、Guid 集合级语义缓存键
- 模糊 / 前后缀资源 URI 检索
- 成功响应用结构化 `notices[]`（v1 仅用 `message` 文本）

## 8. 与其它文档

- 统一信封、路由与性能边界：[HTTP API §2.2 / §3.6 / §9](./2026-09-26-http-api.md)
- 实体、别名字符集与 `AliasNormalized`：[数据模型](./2026-09-23-data-model.md)
- 缓存抽象：[架构 · 查询缓存](./2026-09-22-local-daemon-architecture.md#5-查询缓存)
