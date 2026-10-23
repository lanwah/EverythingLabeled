/**
 * Repro: Options「预览资源类型」选 all → 保存 → 是否仍为 all / storage 是否写入。
 * Usage: node ext/scripts/verify-preview-resource-type.mjs
 */
import puppeteer from "puppeteer-core";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ext = path.resolve(__dirname, "../dist");
const userDataDir = path.resolve(__dirname, "../../.tmp/chrome-ext-prt-verify");

const browserPath =
  process.env.EL_BROWSER ??
  [
    "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
    "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe",
    "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
  ].find((p) => fs.existsSync(p));

if (!browserPath) {
  console.error("No Chrome/Edge found");
  process.exit(1);
}
if (!fs.existsSync(path.join(ext, "manifest.json"))) {
  console.error("ext/dist missing; run npm run build in ext/");
  process.exit(1);
}

fs.rmSync(userDataDir, { recursive: true, force: true });
fs.mkdirSync(userDataDir, { recursive: true });

console.log("browser=", browserPath);
const browser = await puppeteer.launch({
  executablePath: browserPath,
  headless: false,
  userDataDir,
  // Puppeteer 默认带 --disable-extensions，会盖掉 --load-extension
  ignoreDefaultArgs: ["--disable-extensions"],
  args: [
    `--disable-extensions-except=${ext}`,
    `--load-extension=${ext}`,
    "--disable-features=DisableLoadExtensionCommandLineSwitch",
    "--enable-unsafe-extension-debugging",
    "--no-first-run",
    "--no-default-browser-check",
  ],
});

function pickExtTarget(targets) {
  return targets.find((t) => {
    const url = t.url();
    if (!url.startsWith("chrome-extension://")) return false;
    if (
      /fignfifoniblkonapihmkfakmlgkbkcf|nkeimhogjdpnpccoofpliimaahmaaome|mhjfbmdgcfjbbpaeojofohoefgiehjai/.test(
        url,
      )
    ) {
      return false;
    }
    return t.type() === "service_worker" || t.type() === "background_page";
  });
}

async function waitForExt(timeoutMs = 20000) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    const hit = pickExtTarget(browser.targets());
    if (hit) return hit;
    await new Promise((r) => setTimeout(r, 300));
  }
  return undefined;
}

function fail(msg, extra) {
  console.error("FAIL:", msg, extra ?? "");
  process.exitCode = 1;
}

try {
  const extTarget = await waitForExt();
  if (!extTarget) {
    fail("service worker not found");
  } else {
    const id = new URL(extTarget.url()).host;
    console.log("extensionId=", id);

    const page = await browser.newPage();
    await page.setViewport({ width: 520, height: 760 });

    const optionsUrl = `chrome-extension://${id}/options.html`;
    await page.goto(optionsUrl, { waitUntil: "domcontentloaded", timeout: 20000 });
    await page.waitForSelector('select[aria-label="预览资源类型"]', { timeout: 10000 });
    // Wait for initial getConfig fill
    await new Promise((r) => setTimeout(r, 1200));

    const before = await page.evaluate(() => {
      const sel = document.querySelector('select[aria-label="预览资源类型"]');
      return {
        value: sel?.value ?? null,
        selectedIndex: sel?.selectedIndex ?? null,
        options: [...(sel?.options ?? [])].map((o) => ({
          value: o.value,
          text: o.textContent,
        })),
      };
    });
    console.log("before=", JSON.stringify(before));

    // Seed required fields so save validates
    await page.evaluate(() => {
      const inputs = [...document.querySelectorAll("input")];
      const base = inputs.find((i) => i.type === "text");
      const tok = inputs.find((i) => i.type === "password");
      const limit = inputs.find((i) => i.type === "number");
      if (base) base.value = "http://127.0.0.1:5123";
      if (tok && !tok.value) tok.value = "local-token";
      if (limit) limit.value = "5";
    });

    await page.select('select[aria-label="预览资源类型"]', "all");
    const afterSelect = await page.evaluate(() => {
      const sel = document.querySelector('select[aria-label="预览资源类型"]');
      return { value: sel?.value, selectedIndex: sel?.selectedIndex };
    });
    console.log("afterSelect=", JSON.stringify(afterSelect));

    // Intercept save response via page evaluate of the same path UI uses
    const saveProbe = await page.evaluate(async () => {
      const sel = document.querySelector('select[aria-label="预览资源类型"]');
      const inputs = [...document.querySelectorAll("input")];
      const base = inputs.find((i) => i.type === "text");
      const tok = inputs.find((i) => i.type === "password");
      const limit = inputs.find((i) => i.type === "number");
      const confirm = inputs.find((i) => i.type === "checkbox");
      const previewLimitEl = document.querySelector(
        'input[aria-label="标签查询预览条数"]',
      );
      const suggestLimitEl = document.querySelector(
        'input[aria-label="拼首联想预览条数"]',
      );
      const draft = {
        baseUrl: base?.value ?? "",
        token: tok?.value ?? "",
        confirmUntag: confirm?.checked ?? true,
        previewLimit: previewLimitEl?.value ?? limit?.value ?? "8",
        suggestLimit: suggestLimitEl?.value ?? "8",
        previewResourceType: sel?.value ?? "",
      };
      const saveResult = await chrome.runtime.sendMessage({
        type: "saveConfig",
        draft,
      });
      const stored = await chrome.storage.local.get([
        "baseUrl",
        "previewLimit",
        "suggestLimit",
        "previewResourceType",
        "findTagLogic",
      ]);
      const getResult = await chrome.runtime.sendMessage({ type: "getConfig" });
      return { draft, saveResult, stored, getResult };
    });
    console.log("saveProbe=", JSON.stringify(saveProbe, null, 2));

    // Also click the real Save button and read UI after
    await page.click('button.btn-primary');
    await new Promise((r) => setTimeout(r, 800));
    const afterSaveUi = await page.evaluate(() => {
      const sel = document.querySelector('select[aria-label="预览资源类型"]');
      const status = document.querySelector(".status");
      return {
        value: sel?.value,
        selectedIndex: sel?.selectedIndex,
        status: status?.textContent ?? "",
        statusClass: status?.className ?? "",
      };
    });
    console.log("afterSaveUi=", JSON.stringify(afterSaveUi));

    // Reload options page
    await page.goto(optionsUrl, { waitUntil: "domcontentloaded", timeout: 20000 });
    await page.waitForSelector('select[aria-label="预览资源类型"]', { timeout: 10000 });
    await new Promise((r) => setTimeout(r, 1200));
    const afterReload = await page.evaluate(async () => {
      const sel = document.querySelector('select[aria-label="预览资源类型"]');
      const stored = await chrome.storage.local.get(["previewResourceType"]);
      return {
        value: sel?.value,
        selectedIndex: sel?.selectedIndex,
        stored,
      };
    });
    console.log("afterReload=", JSON.stringify(afterReload));

    const ok =
      saveProbe?.saveResult?.ok === true &&
      saveProbe?.saveResult?.data?.previewResourceType === "all" &&
      saveProbe?.stored?.previewResourceType === "all" &&
      saveProbe?.getResult?.data?.previewResourceType === "all" &&
      afterSaveUi?.value === "all" &&
      afterReload?.value === "all" &&
      afterReload?.stored?.previewResourceType === "all";

    if (ok) {
      console.log("PASS: previewResourceType persists as all");
    } else {
      fail("previewResourceType did not stay all", {
        saveData: saveProbe?.saveResult?.data?.previewResourceType,
        stored: saveProbe?.stored?.previewResourceType,
        afterSaveUi: afterSaveUi?.value,
        afterReload: afterReload?.value,
      });
    }
  }
} finally {
  await browser.close();
}
