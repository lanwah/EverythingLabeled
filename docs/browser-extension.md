# 浏览器扩展（#13）

Chrome / Edge MV3 扩展，源码与构建产物在 `ext/`。只走本机 Service HTTP + Bearer；配置在 Options 页手工填写，**不读** `%APPDATA%\EverythingLabeled\config.json`。规格以 [浏览器扩展](../plans/2026-09-27-browser-extension.md) 与 [实现计划](../plans/2026-10-06-browser-extension-impl.md) 为准。

## 工程布局

- `ext/` — Manifest V3、Popup / Options / background service worker、双 Vite 构建
- 对应测试：`ext/` 内 Vitest（`npm test`）

需要 Node.js ≥ 20。

## 构建与测试

仓库根可用 `build-ext.bat` / `.\build-ext.ps1`（缺 `node_modules` 时先 `npm ci`）；`build.bat` / `.\build.ps1` 在 `dotnet build` 之后也会调用它。也可直接在 `ext/`：

```text
cd ext
npm ci
npm test
npm run build
```

门禁只有 `npm test`。`npm run build` 产出加载目录 `ext/dist`。

## 加载扩展

1. 先按上节 `npm run build`，得到 `ext/dist`。
2. **Chrome**：打开 `chrome://extensions` → 打开「开发者模式」→「加载已解压的扩展程序」→ 选仓库内 `ext/dist`。
3. **Edge**：打开 `edge://extensions` → 同样打开开发者模式 → 加载 `ext/dist`。

改源码后需重新 `npm run build`，再在扩展页点「重新加载」。

**配置项变更必须重新加载扩展**：Options / `chrome.storage.local` 里**新增**配置键（或 Options 页新增表单项、读写新键的代码）之后，仅重新打开 Popup / Options **不够**——必须在 `chrome://extensions`（或 `edge://extensions`）对该扩展点「重新加载」，否则新键或新 UI 可能不生效。已有键改值仍可只关开扩展页即可（见下节）。

## 配置

Options 页填写 `baseUrl` 与 `token`。可从 CLI / AppData 的 `config.json` **手工复制**这两项（AppData 路径与键说明见 [本机配置](./config.md)）；扩展不会自动读 AppData。

多标签预览的 AND/OR **不在界面切换**：在 `chrome.storage.local` 写入 `findTagLogic`（`"and"` 缺省，`"or"` 为任一匹配），与 `baseUrl` 等键同级；Options 页暂无表单项，可借开发者工具或同步脚本设置。改后重新打开扩展页生效。

预览资源类型：Options「预览资源类型」写入扩展 `chrome.storage.local` 键 `previewResourceType`（`"links"` 缺省仅链接，`"all"` 为文件 / 文件夹 / 链接）；与 `baseUrl` / `token` 等同级，**不写** AppData `config.json`。打标页 / Popup 预览经 SW 映射为 `GET /v1/query/preview?kind=`（`links`→`url`，`all`→省略即服务端默认全部）；`limit` 作用在服务端 kind 过滤之后。管理资源预览固定不传 `kind`。改后重新打开扩展页生效。

探测（health）与打标等业务请求依赖本机 Service 已监听 `127.0.0.1`（`baseUrl` 指向该地址）。Service 未启动时连接态为失败，业务操作不可用。

## Popup 尺寸（浏览器硬顶）

工具栏 action Popup 由浏览器按内容自动定尺寸，**没有** API 可直接设窗体大小。Chromium（Chrome / Edge）官方硬顶与硬底（见 [chrome.action — Popup](https://developer.chrome.com/docs/extensions/reference/api/action#popup)）：

| | 最小 | 最大 |
|---|------|------|
| 宽度 | 25px | **800px** |
| 高度 | 25px | **600px** |

超出部分不会把 Popup 撑得更大，通常出现滚动条。本扩展 Popup 设计宽约 `420px`（远低于 800）；高度固定写入 **600px**（Chrome 硬顶）。主界面标题单独一行，第二行左侧 switch +「打标模式 / 查找模式」、右侧管理按钮（默认打标）；查找模式用命中区吃满剩余高度，`html`/`body`/`shell` 均 `overflow: hidden`，禁止外层滚动条；内容过多时只在命中区内滚动。

**浮层也不能画出窗外**：Popup 是浏览器单独开的原生小窗，`position: fixed` / `absolute` 的自定义下拉、菜单、提示等，一律画在这个窗的视口里，**不能**像网页里那样溢出到工具栏或页面上，也**不能**突破上述 800×600 硬顶。装不下就裁切或出现滚动条；本扩展用 `placeFixedNear` 在窗内下方不够时改为向上展开。例外：系统原生 `<select>` 可由 OS 画到窗外，自定义 div 浮层无此能力。

## 打标

「打标模式」（默认）：当前网页卡片、当前标签、别名输入。打开 Popup 或切回打标后别名框自动获焦（可打标时）；别名框输入后走 suggest（250ms）。有结果时默认高亮第一项；↑/↓ 切换，Enter 用当前高亮项直接打标（无联想时仍按输入原文提交）。无「打标」「去标」按钮：去标点当前标签芯片上的 ×。返回里没有与输入**大小写不敏感的精确别名**时，自动勾选「创建缺失标签」；仅有前缀命中也勾选。输入清空、点选已有别名、或 Enter 采纳联想时取消勾选。提交仍读当前勾选状态。

## 查找

「查找模式」：芯片输入对齐 Find；切入查找后查询框自动获焦。有芯片时右侧清除图标清空全部查询标签；无「清空标签」「打开所选」。因不再与打标区同屏，命中区可用高度更大，更易按 Options「预览条数」完整显示。两处列表勿混：

- **按标签检索（命中区）**：芯片框下方文档流占位；先按 Options「预览条数」计算理想高度，再按固定 600px 窗高把剩余空间分给命中区（不足则压缩，至少一行）；空提示也在该区内。
- **拼首联想下拉**：`position: fixed` 叠在输入框下；高度**只**跟下方可用空间（命中区实测高度 / 视口剩余），**不**按预览条数限行。

Popup 高度固定 600px，无外层滚动条；结果行无勾选框，名称后展示资源标签胶囊（preview `tags`）；单击高亮，双击打开可打开的链接。键盘对齐 Find：有拼首联想时 ↑/↓ 切联想，否则切结果；有草稿/联想时 Enter 落芯片，输入为空且有结果时 Enter 打开当前高亮项。查找页底部显示「预览 N 条」；错误提示在打标/查找页均可看到。

## 管理标签

独立页面 `library.html`（与 `app.html` 各用一个标签页）。入口：网页右键「EverythingLabeled：管理标签」，或 Popup / 本页标签页的「管理标签」（页面内直接开标签页，不在 Popup 里导航）。打开页面后筛选框获焦。可按名称前缀或拼首筛选、改名、删除；删除前确认。当前页芯片上的 × 仍只是去标，不删词库。

## 管理资源

独立页面 `resources.html`（与 `app.html`、`library.html` 各用一个标签页）。入口：网页右键「EverythingLabeled：管理资源」，或打标页 / Popup 的「管理资源」。不使用当前网页地址。芯片查询与 `findTagLogic` 一致，末词 suggest 含简拼（Enter 落高亮项，与 Find 预览相同）。每一行可删除该资源（确认后去掉绑定，标签词库保留），或填入新地址打上同样的标签再删除原资源。类型按新地址预选：`http(s)` 为网页，路径有扩展名为文件，无扩展名为文件夹；预选可以改。「按标签查找」预览仍只打开，不在那里删除。

## 手工联调清单

Service 已起、Options 已填好、扩展已加载后建议逐项确认：

- Popup 默认「打标模式」且别名框获焦；switch 可切到「查找模式」且查询框获焦；窗高仍 600px
- 对当前 **https** 页打标（标签写入成功、列表刷新）；别名框 Enter 打标，芯片 × 去标
- 资源不存在时列表为空（Service **404** → 空列表，非致命错误）
- 打标：无精确别名时自动勾选「创建缺失标签」，点选已有别名后取消勾选；勾选状态随提交生效
- 去标前有确认（芯片 ×）
- 查找：结果在输入框下方占位（按预览条数预留高度，查找页空间更大）、↑/↓ 切换、双击或空输入 Enter 打开、清除图标；默认仅链接，Options 可改为全部
- 查找打开有硬顶（超出则拒绝/提示，不无限开 tab）
- 右键「EverythingLabeled：管理本页标签」走 `app.html`，并复用已有 tab（不重复堆叠）
- 右键「EverythingLabeled：管理标签」走 `library.html`，与 `app.html` 分开复用 tab
- 右键「EverythingLabeled：管理资源」走 `resources.html`，与上面两个页面分开复用 tab
- 打标页 / Popup「管理资源」在页面里直接打开 `resources.html`
- Popup / 本页标签页「管理标签」在页面里直接 `tabs` 打开 `library.html`（复用已有；勿在 Popup 内导航，也不要经 SW 再开页）
- 非 `http`/`https` 页打标 / 去标 / 看本页标签禁用；管理标签可在普通网页右键使用
