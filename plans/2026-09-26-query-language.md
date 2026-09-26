# EverythingLabeled 查询语言细则

日期：2026-09-26  
状态：已确认（v1 基线）  
关联：

- [本地守护进程架构](./2026-09-22-local-daemon-architecture.md)
- [数据模型](./2026-09-23-data-model.md)
- [HTTP API 契约](./2026-09-26-http-api.md)

## 1. 设计摘要

- 查询串：空白分词；段间 `OR`；段内 AND；负词 `-alias`；**无引号、无括号**。
- 子孙展开：**仅** HTTP 参数 `expand=child`（正、负词都展开，含自身）；无词级 `*`。
- 闸门默认：`maxDepth=8`，`expandedTagLimit=512`（兼服务端上限；请求可下调）。
- 规范化：词面小写 + `OR` 统一；**解析 TagId 之前**生成，供缓存键。
- 结构非法 / 空 `q` → `400`；未知正词 → `200` 空结果；展开触顶 → `422`。

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
   - 以 `-` 开头且长度 > 1 → **负词**（去掉一个前导 `-` 后为别名文本）
   - 否则 → **正词**
4. 按 `OR` **拆成若干段（clause）**：段与段 OR；段内正词 AND、负词从该段排除
5. 每段至少 **一个正词**；同段可有多个负词
6. 别名文本经 **`AliasNormalized`（小写）** 解析为 `TagId`
7. **不**用 `Name` / `NameInitials` 解析查询词
8. **不支持**引号短语、括号、`AND` / `NOT` 关键字

### 2.3 非法结构 → `400 bad_request`

| 情况 | 例 |
|---|---|
| 空 / 仅空白 `q` | ` ` |
| `OR` 开头或结尾 | `OR 工作`、`工作 OR` |
| 连续 `OR` / 空段 | `工作 OR OR 生活` |
| 某段无正词 | `-归档`、`工作 OR -归档` |
| 孤立 `-` | `-` |
| 非法 `expand` 值 | `expand=related`、`expand=foo` |

说明：`--x` 视为负词，别名文本为 `-x`（合法结构；能否解析取决于是否存在该别名）。

### 2.4 合法示例

| `q` | 含义 |
|---|---|
| `工作 待读` | 一段：工作 ∧ 待读 |
| `工作 OR 生活` | (工作) ∨ (生活) |
| `工作 待读 OR 生活 -归档` | (工作 ∧ 待读) ∨ (生活 ∧ ¬归档) |
| `工作 -归档 -草稿` | 工作 ∧ ¬归档 ∧ ¬草稿 |

### 2.5 未知标签

- 任一段中**任一正词**无法解析 → **整句** `200`，`items: []`（`total: 0`）；不展开、不查库集合
- 负词无法解析 → **忽略**该负词

## 3. `expand=child` 集合语义

### 3.1 参数

| 参数 | 默认 | 说明 |
|---|---|---|
| `expand` | 省略 | 仅允许省略或 `child` |
| `maxDepth` | `8` | 可下调；大于服务端上限（默认 8）→ `422 expansion_limit_exceeded` |
| `expandedTagLimit` | `512` | 可下调；大于上限同左 |

未传 `expand`：不展开；每个词对应单一 `TagId`。

### 3.2 展开算法

对每个已解析标签词（**正、负相同**）：

1. 起点 = 该 `TagId`（**含自身**，depth 0）
2. 沿 `TagRelation` 且 `Kind=child`、方向 From=父 → To=子 向下遍历（BFS/DFS 均可）
3. 只收集 `depth ≤ maxDepth` 的节点
4. **整次查询**（所有正负词展开结果）去重后的 TagId 个数不得超过 `expandedTagLimit`；触顶 → `422 expansion_limit_exceeded`（不返回部分结果）
5. `related` **不**参与展开

顺序：结构校验 → 别名解析 →（正词皆可知后）展开与闸门 → 集合运算。

### 3.3 段内集合运算（展开后）

- 每个**正词**：资源须带有展开集合中 **至少一** 个 TagId
- 每个**负词**：资源不得带有展开集合中 **任何** 一个 TagId
- 段内多正词：上述条件 **AND**
- 段与段：**OR**
- 多段结果并集后再分页

例：`q=工作&expand=child` 命中仅打了「工作」之子标签的资源。

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

### 4.2 缓存键

```text
v{data_version}:{op}:{normalized_query}:{expand}:{maxDepth}:{expandedTagLimit}:{limit}:{offset}
```

| 场景 | `expand` | `maxDepth` | `expandedTagLimit` |
|---|---|---|---|
| 未展开 | `none` | `0` | `0` |
| `expand=child` | `child` | 钳位后生效值 | 钳位后生效值（默认 512） |

- `op` ∈ `query` | `preview`
- `suggest` 不使用本查询语法；其缓存键不含上述 expand 三维（或恒为 `none`/`0`），由实现与架构约定即可

字段顺序以实现为准，但 **必须包含** 上表全部维度。

## 5. 错误码（查询路径）

| 情况 | HTTP | code |
|---|---|---|
| 空 q / 结构非法 / 非法 expand | 400 | `bad_request` |
| 展开触顶（深度或标签数） | 422 | `expansion_limit_exceeded` |
| 未知正词 | 200 | 空列表 |
| 未知负词 | 200 | 忽略该词，继续 |

## 6. 非目标（v1）

- 引号短语、括号、`AND` / `NOT` 关键字
- 词级 `*`、`expand=related`、多跳 related 查询
- 查询词走主名或简拼
- 段内 token 重排、Guid 集合级语义缓存键
- 模糊 / 前后缀资源 URI 检索

## 7. 与其它文档

- 闸门数值与路由：[HTTP API §3.6 / §9](./2026-09-26-http-api.md)
- 实体与 `AliasNormalized`：[数据模型](./2026-09-23-data-model.md)
- 缓存抽象：[架构 · 查询缓存](./2026-09-22-local-daemon-architecture.md#5-查询缓存)
