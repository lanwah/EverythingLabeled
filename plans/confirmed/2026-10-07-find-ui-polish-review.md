# Find 启动器风 UI + 自动聚焦 — 讨论确认记录

日期：2026-10-07  
方式：主线澄清（brainstorm，带推荐答案 → 用户确认）  
分支：`feature/find-ui-polish`

范围：仅 `HostMode.Find` 外观与打开时输入框聚焦；**不**改 Tag/Untag/Tags 皮肤；**不**做无边框圆角外壳；**不**做选中行右侧操作图标。

说明：此前 [Find UX 审核 Q4](./2026-10-07-find-ux-review.md) 选过 **C1**（先交互、不大改皮肤）。本轮用户另提视觉对齐图 2 + 自动聚焦，以下确认**覆盖**该条中的「不大改皮肤」部分；交互契约（suggest / preview / Enter）不变。

---

## A. 主线澄清

### Q1 — 视觉贴近图 2 的程度

**问题**：当前 Find 为浅色系统对话框（图 1）；目标参考深色启动器列表（图 2）。希望贴近到哪一层？

| 选项 | 含义 |
|---|---|
| **A（推荐）** | 视觉语言：Find 专用深色紧凑布局（顶栏搜索图标+输入、结果行图标+标题+副标题、选中条）；保留系统标题栏；「打开」与底栏状态保留；打开/再激活时自动聚焦输入框 |
| **B** | A + 无边框圆角启动器外壳（Esc 关闭等） |
| **C** | B + 选中行右侧操作图标（打开位置等） |

**确认**：选推荐 **A**（用户回「推荐」后又回「yes」批准短设计）。

### Q2 — 短设计要点（批准后实现）

**问题**：方案 A 的落地边界是否同意？

| 要点 | 约定 |
|---|---|
| 范围 | 仅 Find；查询/打开逻辑不变 |
| 视觉 | 深色 `#1B1F2A` 系；放大镜+输入；行模板 Glyph/Title/Subtitle；选中浅灰底+左侧蓝条 |
| 聚焦 | 首次显示与 `ActivateFromIpc` 后 `FindQueryBox.Focus()` |
| 验收 | 可直接打字；行层级接近图 2；Enter/双击/打开仍可用 |

**确认**：用户 **yes**。

> **后续修订（同日）**：标题栏改为无边框深色自绘；输入改为芯片 + AND/OR。见 [芯片审核](./2026-10-07-find-chip-and-or-review.md)。本条「保留系统标题栏」已废止。

---

## B. 落地摘要

- 纯函数 `FindResultPresentation`（Title / Subtitle / Glyph / KindLabel）+ 单测。
- `MainWindow` Find 面板深色样式；`PreviewRow` 绑定 Title/Subtitle/Glyph。
- `Loaded` / `ActivateFromIpc` / `ShowMode(Find)` 调用 `FocusFindQuery`。
