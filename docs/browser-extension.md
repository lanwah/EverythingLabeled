# 浏览器扩展（#13）

Chrome / Edge MV3 扩展，源码与构建产物在 `ext/`。只走本机 Service HTTP + Bearer；配置在 Options 页手工填写，**不读** `%APPDATA%\EverythingLabeled\config.json`。规格以 [浏览器扩展](../plans/2026-09-27-browser-extension.md) 与 [实现计划](../plans/2026-10-06-browser-extension-impl.md) 为准。

## 工程布局

- `ext/` — Manifest V3、Popup / Options / background service worker、双 Vite 构建
- 对应测试：`ext/` 内 Vitest（`npm test`）

需要 Node.js ≥ 20。

## 构建与测试

均在 `ext/` 目录：

```text
cd ext
npm install
npm test
npm run build
```

门禁只有 `npm test`。`npm run build` 产出加载目录 `ext/dist`。

## 加载扩展

1. 先按上节 `npm run build`，得到 `ext/dist`。
2. **Chrome**：打开 `chrome://extensions` → 打开「开发者模式」→「加载已解压的扩展程序」→ 选仓库内 `ext/dist`。
3. **Edge**：打开 `edge://extensions` → 同样打开开发者模式 → 加载 `ext/dist`。

改源码后需重新 `npm run build`，再在扩展页点「重新加载」。

## 配置

Options 页填写 `baseUrl` 与 `token`。可从 CLI / AppData 的 `config.json` **手工复制**这两项（AppData 路径与键说明见 [本机配置](./config.md)）；扩展不会自动读 AppData。

多标签预览的 AND/OR **不在界面切换**：在 `chrome.storage.local` 写入 `findTagLogic`（`"and"` 缺省，`"or"` 为任一匹配），与 `baseUrl` 等键同级；Options 页暂无表单项，可借开发者工具或同步脚本设置。改后重新打开扩展页生效。

探测（health）与打标等业务请求依赖本机 Service 已监听 `127.0.0.1`（`baseUrl` 指向该地址）。Service 未启动时连接态为失败，业务操作不可用。

## 打标

别名框输入后走 suggest（250ms）。有结果时默认高亮第一项；↑/↓ 切换，Enter 用当前高亮项直接打标（无联想时仍按输入原文提交）。返回里没有与输入**大小写不敏感的精确别名**时，自动勾选「创建缺失标签」；仅有前缀命中也勾选。输入清空、点选已有别名、或 Enter 采纳联想时取消勾选。提交仍读当前勾选状态。

## 管理标签

独立页面 `library.html`（与 `app.html` 各用一个标签页）。入口：网页右键「EverythingLabeled：管理标签」，或 Popup / 本页标签页的「管理标签」（页面内直接开标签页，不在 Popup 里导航）。打开页面后筛选框获焦。可按名称前缀或拼首筛选、改名、删除；删除前确认。当前页芯片上的 × 仍只是去标，不删词库。

## 管理资源

独立页面 `resources.html`（与 `app.html`、`library.html` 各用一个标签页）。入口：网页右键「EverythingLabeled：管理资源」，或打标页 / Popup 的「管理资源」。不使用当前网页地址。芯片查询与 `findTagLogic` 一致，末词 suggest 含简拼（Enter 落高亮项，与 Find 预览相同）。每一行可删除该资源（确认后去掉绑定，标签词库保留），或填入新地址打上同样的标签再删除原资源。类型按新地址预选：`http(s)` 为网页，路径有扩展名为文件，无扩展名为文件夹；预选可以改。「按标签查找」预览仍只打开，不在那里删除。

## 手工联调清单

Service 已起、Options 已填好、扩展已加载后建议逐项确认：

- 对当前 **https** 页打标（标签写入成功、列表刷新）
- 资源不存在时列表为空（Service **404** → 空列表，非致命错误）
- 打标：无精确别名时自动勾选「创建缺失标签」，点选已有别名后取消勾选；勾选状态随提交生效
- 去标前有确认
- 预览打开有硬顶（超出则拒绝/提示，不无限开 tab）
- 右键「EverythingLabeled：管理本页标签」走 `app.html`，并复用已有 tab（不重复堆叠）
- 右键「EverythingLabeled：管理标签」走 `library.html`，与 `app.html` 分开复用 tab
- 右键「EverythingLabeled：管理资源」走 `resources.html`，与上面两个页面分开复用 tab
- 打标页 / Popup「管理资源」在页面里直接打开 `resources.html`
- Popup / 本页标签页「管理标签」在页面里直接 `tabs` 打开 `library.html`（复用已有；勿在 Popup 内导航，也不要经 SW 再开页）
- 非 `http`/`https` 页打标 / 去标 / 看本页标签禁用；管理标签可在普通网页右键使用
