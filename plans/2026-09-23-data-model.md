# EverythingLabeled 数据模型

日期：2026-09-23  
状态：已确认（v1 基线；含网状标签关系、`NameInitials` 简拼、别名字符集）  
关联：

- [本地守护进程架构](./2026-09-22-local-daemon-architecture.md)
- [HTTP API 契约](./2026-09-26-http-api.md)
- [查询语言细则](./2026-09-26-query-language.md)

## 1. 实体与字段

```text
Tag 1──* TagAlias
Tag *──* Resource     (via ResourceTag)
Tag *──* Tag          (via TagRelation，网状；父子为特例)
Meta (key-value，含 data_version)
```

### Tag

| 字段 | 类型 | 说明 |
|---|---|---|
| Id | Guid | 主键 |
| Name | string | 主名（展示名） |
| NameInitials | string | 主名简拼；服务端由 `Name` 自动生成；小写；只读 |
| Namespace | string? | 可选命名空间；空 = 全局扁平 |
| Seq | int? | 可选顺序号；越小越靠前；未设置则为 null |
| CreatedAt | DateTimeOffset | |
| UpdatedAt | DateTimeOffset | |
| Revision | long | 同步预留；本地写时递增 |

约束：同一 `Namespace` 内 `Name` 唯一（`Namespace` 为 NULL 时用统一空键参与唯一约束）。

**NameInitials（简拼）**

- 仅由 **主名 `Name`** 生成；不按 Alias 存简拼。
- 创建 Tag、更新 `Name` 时重算；客户端不可写入。
- 算法：汉字 → 拼音首字母小写；ASCII 字母数字 → 小写保留；其余跳过。例：`工作笔记` → `gzbj`，`API设计` → `apisj`。若结果为空字符串仍入库（极少见；检索仅精确空前缀时有意义）。
- 多音字：v1 用拼音库默认读音。
- 用途：`suggest` / 标签列表 `q=` 前缀检索（与 `AliasNormalized` 为 OR）。

### TagAlias

| 字段 | 类型 | 说明 |
|---|---|---|
| TagId | Guid | FK → Tag |
| Alias | string | 别名；**全局唯一** |

规则：

- 一个 Tag 可有多个 Alias。
- **创建 Tag 时若未显式提供别名，自动插入 `Alias = Name`。**
- 查询与打标输入经 Alias（或与主名相同的默认别名）解析为 `TagId`。

**别名字符集（写入校验；与查询词法配套）**

创建 Tag 默认别名、显式 `aliases`、`POST …/aliases`、`createMissing` 建标所用别名均须满足：

| 规则 | 说明 |
|---|---|
| 非空 | trim 后长度 ≥ 1 |
| 不以 `-` 开头 | 保证查询语言能写正词（以 `-` 开头的 token 一律为负词） |
| 不含 Unicode 空白 | 查询按空白分词，含空白的别名无法用 `q=` 引用 |
| 不含 ASCII 控制字符 | U+0000–U+001F、U+007F |
| 保留词 | `AliasNormalized` ∉ `{ or, and, not }`（大小写不敏感归一后）；避免与查询运算符 / 未来关键字冲突 |
| 其它字符 | v1 **允许**（含中文、`:` 等，以便 `proj:foo` 风格别名） |

违规 → HTTP `422 validation_failed`，`data.details` 建议带原因；服务端日志写清（如 `alias_rejected_leading_dash` / `alias_rejected_whitespace` / `alias_rejected_control` / `alias_rejected_reserved`）。

展示名 `Tag.Name` **可以**含空白等（仅展示/管理）；若未显式提供别名且 `Name` 本身违反上表，创建失败（不得插入非法默认别名）。

### Resource

| 字段 | 类型 | 说明 |
|---|---|---|
| Id | Guid | 主键 |
| Uri | string | 规范化后的唯一键 |
| Kind | string | `file` \| `folder` \| `url` |
| DisplayName | string? | 可选展示名（网页标题等） |
| CreatedAt | DateTimeOffset | |
| UpdatedAt | DateTimeOffset | |
| Revision | long | 同步预留 |

约束：`Uri` 全局唯一。

### ResourceTag

| 字段 | 类型 | 说明 |
|---|---|---|
| ResourceId | Guid | PK、FK |
| TagId | Guid | PK、FK |
| CreatedAt | DateTimeOffset | |

同一 `(ResourceId, TagId)` 仅一行。

### TagRelation（网状标签关系）

标签之间的有向边。父子树是网的特例，不另建树表。

| 字段 | 类型 | 说明 |
|---|---|---|
| Id | Guid | 主键 |
| FromTagId | Guid | FK → Tag（边起点） |
| ToTagId | Guid | FK → Tag（边终点） |
| Kind | string | 关系类型，见下表 |
| Seq | int? | 同一 `(FromTagId, Kind)` 下的顺序；越小越靠前；未设置则为 null |
| CreatedAt | DateTimeOffset | |
| UpdatedAt | DateTimeOffset | |
| Revision | long | 同步预留 |

**v1 内置 `Kind`：**

| Kind | 边含义 | 额外约束 |
|---|---|---|
| `child` | `From` = 父，`To` = 子 | 同一 `To` 在 `Kind=child` 下至多一条边（单父）；禁止成环 |
| `related` | 一般相关 / 网状链接 | 允许双向各一条；禁止自环；不强制无环 |

约束（全 Kind 共用）：

- `FromTagId ≠ ToTagId`
- UNIQUE `(FromTagId, ToTagId, Kind)`
- 写 `child` 边时：若会引入环则拒绝
- 调整子顺序：更新同父下各 `child` 边的 `Seq`（可批量重排）
- HTTP `POST /v1/relations/batch`：在「旧图 ∪ 本批全部新边」上做集合校验，全部合法才提交（见 [HTTP API §3.5](./2026-09-26-http-api.md)）；`related` 不自动补反向边

`Namespace` 仍只作管理归类，**不**表达父子；结构关系一律走 `TagRelation`。

### Meta

| 字段 | 类型 | 说明 |
|---|---|---|
| Key | string | 主键 |
| Value | string | |

至少包含：

| Key | 含义 |
|---|---|
| `data_version` | 单调递增整数（字符串存储亦可）；任意标签 / 别名 / 绑定 / **关系** 写成功后 +1 |

## 2. URI 规范化

### file / folder

- 形式：`file:///C:/Users/example/doc.txt`
- 盘符大写；路径分隔符统一为 `/`
- 非盘符根路径去掉末尾 `/`
- Windows 本地路径转入此形式后再入库
- **身份 = 规范化 URI 字符串**；改名 / 移动不自动迁移绑定

### url

- scheme、host 小写
- 省略默认端口（`http` 80、`https` 443）
- 去掉 `#fragment`
- 保留 query（不同 query 视为不同资源）

`Kind` 由调用方或根据 URI 判定：目录显式标 `folder`，普通文件 `file`，`http(s)` 为 `url`。

## 3. 索引

| 对象 | 索引 | 用途 |
|---|---|---|
| Tag | UNIQUE (Namespace, Name) | 主名唯一 |
| TagAlias | UNIQUE (Alias) | 解析与建议 |
| TagAlias | INDEX (TagId) | 按标签列别名 |
| Resource | UNIQUE (Uri) | 按 URI 定位 |
| Resource | INDEX (Kind) | 按类型过滤（可选） |
| ResourceTag | PK (ResourceId, TagId) | 绑定唯一 |
| ResourceTag | INDEX (TagId, ResourceId) | 按标签查资源（查询热路径） |
| TagRelation | UNIQUE (FromTagId, ToTagId, Kind) | 边唯一 |
| TagRelation | INDEX (FromTagId, Kind, Seq) | 列出子节点 / 相关并保序 |
| TagRelation | UNIQUE (ToTagId) WHERE Kind = 'child' | 单父（若引擎不支持部分唯一索引，则用应用层校验 + 普通 INDEX (ToTagId, Kind)） |
| Tag | INDEX (Seq, CreatedAt) | 标签列表默认排序 |
| Tag | INDEX (NameInitials) | 简拼前缀检索 |

建议接口：对 `TagAlias.AliasNormalized`、`Tag.NameInitials` 做前缀匹配（SQLite `LIKE 'prefix%'`）。别名列大小写策略：**保留用户大小写展示，另存 `AliasNormalized` 小写列并对其建唯一索引**。

补充字段（写入实现约定）：

| 表 | 字段 | 说明 |
|---|---|---|
| TagAlias | AliasNormalized | `Alias` 的小写形式；UNIQUE；解析与 suggest 用此列 |
| Tag | NameNormalized | 小写；与 Namespace 组成唯一（若与 Alias 策略冲突则以 Alias 解析为准） |

为减少歧义：**对外解析只走 `TagAlias.AliasNormalized`**（含默认别名 = 主名）。`Tag.Name` 仅作展示与管理。

## 3.1 标签列表默认排序

凡返回**标签集合**的接口（标签列表、某资源上的标签、某父下的子标签、某标签的 `related` 邻居等），默认排序规则统一为：

1. **`Seq` 升序**（数值越小越靠前）
2. 若 **`Seq` 未设置（null）或彼此相等**：再按 **`CreatedAt` 升序**（先创建的在前）
3. 若仍相同：按 **`Id` 升序** 作为稳定次序

SQL 等价表达（示意）：

```sql
ORDER BY (Seq IS NULL) ASC, Seq ASC, CreatedAt ASC, Id ASC
```

说明：

- `(Seq IS NULL) ASC` 使**已设置 Seq 的排在未设置之前**；未设置的一组内部只按 `CreatedAt`（及 `Id`）排。
- 列表「子标签 / related」时，排序字段取 **边上的 `TagRelation.Seq` / `TagRelation.CreatedAt`**（边的创建时间），不是目标 Tag 自身的 Seq（除非接口文档另行约定合并规则；v1 固定用边）。
- 全局标签列表 / 资源上的标签列表：用 **`Tag.Seq` / `Tag.CreatedAt`**。
- 客户端不得依赖未文档化的其它默认序；需要别种排序时由查询参数显式指定（见 [HTTP API](./2026-09-26-http-api.md)）。

## 3.2 资源列表默认排序

凡返回**资源集合**的接口（`GET /v1/resources`、`GET /v1/query`、`GET /v1/query/preview` 等），无显式排序参数时：

1. **`CreatedAt` 升序**
2. 再相同 → **`Id` 升序**

查询多段 OR 并集后再按此序分页（见 [查询语言细则 §3.4](./2026-09-26-query-language.md)）。

## 4. 查询语义

要点（全文以 [查询语言细则](./2026-09-26-query-language.md) 为准）：

1. 对查询字符串 trim；按 Unicode 空白分词。
2. 词 `OR`（大小写不敏感）为并集运算符；默认相邻正词为 AND。
3. 以 `-` 开头的词为排除（去掉**一个**前导 `-` 后解析标签）。
4. 每个标签词经 `AliasNormalized` 解析为 `TagId`（受别名字符集约束）。
5. 集合运算在 `ResourceTag` 上完成，再取 Resource 详情。
6. **含子孙（可选展开）**：仅 HTTP 参数 `expand=child`（无词级 `*`）；正、负词均沿 `Kind=child` 展开（含自身；负词排除整个子树）；`related` **不**自动展开。闸门默认 `maxDepth=8`、`expandedTagLimit=512`；深度截断、标签数触顶分流见细则。
7. 未知正词 → 整句空结果 + 成功信封非空 `message`；未知负词 → 忽略（仅日志）。

示例：

| 输入 | 含义 |
|---|---|
| `工作 待读` | 同时有两标签 |
| `工作 OR 生活` | 任一 |
| `工作 -归档` | 有「工作」且无「归档」 |
| `proj:foo 待读` | 别名 / 命名空间风格别名与「待读」AND |

命名空间不单独作为查询语法层：用户通过别名（如 `proj:foo`）表达即可；Tag.Namespace 用于管理归类。结构父子用 `TagRelation(Kind=child)`，不用 Namespace 模拟。

## 5. data_version 与缓存键

- 写事务成功提交后：`data_version := data_version + 1`（与业务写同一事务）。
- 读接口缓存键：

```text
v{data_version}:{op}:{normalized_query}:{expand}:{maxDepth}:{expandedTagLimit}:{limit}:{offset}
```

- `op` ∈ `suggest` | `preview` | `query`
- `normalized_query`：分词、`OR` 统一、别名小写、解析 TagId **之前**的词面串；**保持用户词序**（段内不重排 → 语义相同但词序不同会产生不同缓存键）；算法见 [查询语言细则 §4](./2026-09-26-query-language.md)
- `expand` / `maxDepth` / `expandedTagLimit`：未展开时固定占位 `none` / `0` / `0`；展开时为校验后生效值（默认深度 8、标签数 512）

详情见[架构文档 · 查询缓存](./2026-09-22-local-daemon-architecture.md#5-查询缓存)与 [查询语言细则](./2026-09-26-query-language.md)。

HTTP 读写响应使用统一信封 `{ status, message, data }`（见 [HTTP API §2.2](./2026-09-26-http-api.md)）。

## 6. 同步预留字段

| 字段 | v1 行为 | 日后 |
|---|---|---|
| `Revision` | 每次更新该行 +1 | 向量时钟 / 冲突检测输入 |
| `UpdatedAt` | 写时刷新 UTC | 同步水位 |
| `data_version` | 本地缓存失效 | 可映射为变更流版本（语义不同，勿混用） |

v1 不实现同步协议；跨机文件资源仅能同步 URI 字符串元数据，路径在另一台机器上可能无效。

## 7. 后续

- [HTTP API 契约](./2026-09-26-http-api.md)（已确认）
- [查询语言细则](./2026-09-26-query-language.md)（已确认）
- CLI / 扩展 / Explorer 字段映射
