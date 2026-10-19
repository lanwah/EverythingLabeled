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

Options 页填写 `baseUrl` 与 `token`。可从 CLI / AppData 的 `config.json` **手工复制**这两项；扩展不会自动读 AppData。

探测（health）与打标等业务请求依赖本机 Service 已监听 `127.0.0.1`（`baseUrl` 指向该地址）。Service 未启动时连接态为失败，业务操作不可用。

## 打标

别名框输入后走 suggest（250ms）。返回里没有与输入**大小写不敏感的精确别名**时，自动勾选「创建缺失标签」；仅有前缀命中也勾选。输入清空、或点选一条已有别名时取消勾选。提交仍读当前勾选状态。

## 管理标签

独立页面 `library.html`（与 `app.html` 各用一个标签页）。入口：网页右键「EverythingLabeled：管理标签」，或 Popup / 本页标签页的「管理标签」（页面内直接开标签页，不在 Popup 里导航）。可按名称前缀或拼首筛选、改名、删除；删除前确认。当前页芯片上的 × 仍只是去标，不删词库。

## 手工联调清单

Service 已起、Options 已填好、扩展已加载后建议逐项确认：

- 对当前 **https** 页打标（标签写入成功、列表刷新）
- 资源不存在时列表为空（Service **404** → 空列表，非致命错误）
- 打标：无精确别名时自动勾选「创建缺失标签」，点选已有别名后取消勾选；勾选状态随提交生效
- 去标前有确认
- 预览打开有硬顶（超出则拒绝/提示，不无限开 tab）
- 右键「EverythingLabeled：管理本页标签」走 `app.html`，并复用已有 tab（不重复堆叠）
- 右键「EverythingLabeled：管理标签」走 `library.html`，与 `app.html` 分开复用 tab
- Popup / 本页标签页「管理标签」在页面里直接 `tabs` 打开 `library.html`（复用已有；勿在 Popup 内导航，也不要经 SW 再开页）
- 非 `http`/`https` 页打标 / 去标 / 看本页标签禁用；管理标签可在普通网页右键使用
