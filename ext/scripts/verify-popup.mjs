import puppeteer from "puppeteer-core";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ext = path.resolve(__dirname, "../dist");
const outDir = path.resolve(__dirname, "../../.tmp/chrome-ext-verify-shots");
fs.mkdirSync(outDir, { recursive: true });

const browserPath =
  process.env.EL_BROWSER ??
  [
    "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
    "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe",
  ].find((p) => fs.existsSync(p));

if (!browserPath) {
  console.error("No Chrome/Edge found");
  process.exit(1);
}
console.log("browser=", browserPath);

const userDataDir = path.resolve(__dirname, "../../.tmp/chrome-ext-puppeteer");
fs.rmSync(userDataDir, { recursive: true, force: true });
fs.mkdirSync(userDataDir, { recursive: true });

const browser = await puppeteer.launch({
  executablePath: browserPath,
  headless: false,
  userDataDir,
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
    // skip built-in component extensions
    if (/fignfifoniblkonapihmkfakmlgkbkcf|nkeimhogjdpnpccoofpliimaahmaaome|mhjfbmdgcfjbbpaeojofohoefgiehjai/.test(url)) {
      return false;
    }
    return t.type() === "service_worker" || t.type() === "background_page";
  });
}

async function waitForExt(timeoutMs = 15000) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    const hit = pickExtTarget(browser.targets());
    if (hit) return hit;
    await new Promise((r) => setTimeout(r, 400));
  }
  return undefined;
}

try {
  console.log(
    "initial targets:\n",
    browser.targets().map((t) => `${t.type()} ${t.url()}`).join("\n"),
  );
  const extTarget = await waitForExt();
  if (!extTarget) {
    console.error("EverythingLabeled service worker not found");
    process.exitCode = 2;
  } else {
    const id = new URL(extTarget.url()).host;
    console.log("extensionId=", id);

    const page = await browser.newPage();
    await page.setViewport({ width: 480, height: 720 });

    // Seed config so popup can talk to local service (token from user profile if present)
    const userSecure =
      process.env.LOCALAPPDATA +
      "\\Google\\Chrome\\User Data\\Default\\Secure Preferences";
    let token = "local-token";
    let baseUrl = "http://127.0.0.1:5123";
    try {
      if (fs.existsSync(userSecure)) {
        const raw = fs.readFileSync(userSecure, "utf8");
        const tokenMatch = raw.match(/"token":"([^"]*)"/);
        const baseMatch = raw.match(/"baseUrl":"([^"]*)"/);
        // Prefer chrome.storage in Local Extension Settings
      }
    } catch {
      /* ignore */
    }
    const storagePath = path.join(
      process.env.LOCALAPPDATA ?? "",
      "Google\\Chrome\\User Data\\Default\\Local Extension Settings",
      "dijhlihfncananiflhclbmefhpaaiolc",
    );
    console.log("userExtStorageExists=", fs.existsSync(storagePath));

    await page.goto(`chrome-extension://${id}/options.html`, {
      waitUntil: "domcontentloaded",
      timeout: 20000,
    });
    await new Promise((r) => setTimeout(r, 800));

    // Fill options from known local service defaults if empty
    await page.evaluate(
      async ({ baseUrl, token }) => {
        const inputs = [...document.querySelectorAll("input")];
        const base = inputs.find((i) => i.type === "text");
        const tok = inputs.find((i) => i.type === "password");
        if (base && !base.value) base.value = baseUrl;
        if (tok && !tok.value) tok.value = token;
        const select = document.querySelector("select");
        if (select) select.value = "links";
        const form = document.querySelector("form");
        if (form) form.requestSubmit();
      },
      { baseUrl, token },
    );
    await new Promise((r) => setTimeout(r, 800));
    await page.screenshot({
      path: path.join(outDir, "options.png"),
      fullPage: true,
    });

    await page.goto(`chrome-extension://${id}/popup.html`, {
      waitUntil: "domcontentloaded",
      timeout: 20000,
    });
    await new Promise((r) => setTimeout(r, 1500));

    const checks = await page.evaluate(() => {
      const buttons = [...document.querySelectorAll("button")].map((b) =>
        (b.textContent ?? "").trim(),
      );
      const modeBtn = document.querySelector(".mode-switch-control");
      const modeLabel = document.querySelector(".mode-switch-label");
      return {
        hasModeSwitch: !!modeBtn && modeBtn.getAttribute("role") === "switch",
        defaultTagMode:
          modeBtn?.getAttribute("aria-checked") === "false" &&
          (modeLabel?.textContent ?? "").trim() === "打标模式",
        hasTagActionBtn: buttons.includes("打标"),
        hasUntagBtn: buttons.includes("去标"),
        hasOpenSelected: buttons.includes("打开所选"),
        hasClearTagsLabelBtn: buttons.includes("清空标签"),
        hasClearIcon: !!document.querySelector(".clear-all-tags"),
        hasPreviewBlock: !!document.querySelector(".ext-preview-block"),
        hasCreateMissing: document.body.innerText.includes("创建缺失标签"),
        findPanelHidden:
          document.querySelector('[data-view-panel="find"]')?.hidden === true,
        buttonLabels: buttons,
        status: document.querySelector(".status")?.textContent ?? "",
      };
    });
    console.log("popupChecks=", JSON.stringify(checks, null, 2));
    await page.screenshot({
      path: path.join(outDir, "popup.png"),
      fullPage: true,
    });

    // Switch to find mode, then add a chip via Enter
    await page.click(".mode-switch-control");
    await new Promise((r) => setTimeout(r, 400));
    const afterFindTab = await page.evaluate(() => {
      const findPanel = document.querySelector('[data-view-panel="find"]');
      return {
        findVisible: findPanel != null && !findPanel.hidden,
        hasPreviewIdle: document.body.innerText.includes(
          "添加标签后，这里显示匹配结果",
        ),
      };
    });
    console.log("afterFindTab=", JSON.stringify(afterFindTab, null, 2));

    const previewInput = await page.$('.chip-field input[aria-label="标签查询"]');
    let previewFlow = { ok: false };
    if (previewInput) {
      await previewInput.click({ clickCount: 3 });
      await previewInput.type("工作", { delay: 40 });
      await new Promise((r) => setTimeout(r, 600));
      await previewInput.press("Enter");
      await new Promise((r) => setTimeout(r, 1200));
      previewFlow = await page.evaluate(() => {
        const chips = [...document.querySelectorAll(".chip-field .chip")].map(
          (c) => c.textContent?.replace("×", "").trim(),
        );
        const rows = [...document.querySelectorAll(".ext-preview-hits .result-list li")];
        const clearHidden = document.querySelector(".clear-all-tags")?.hidden ?? true;
        return {
          ok: true,
          chips,
          rowCount: rows.length,
          clearHidden,
          hasResultTags: !!document.querySelector(".result-tag"),
          status: document.querySelector(".footer .status")?.textContent ?? "",
          firstRowHtml: rows[0]?.innerHTML?.slice(0, 240) ?? "",
        };
      });
      await page.screenshot({
        path: path.join(outDir, "popup-preview.png"),
        fullPage: true,
      });
    }
    console.log("previewFlow=", JSON.stringify(previewFlow, null, 2));

    const failed =
      !checks.hasModeSwitch ||
      !checks.defaultTagMode ||
      checks.hasTagActionBtn ||
      checks.hasUntagBtn ||
      checks.hasOpenSelected ||
      checks.hasClearTagsLabelBtn ||
      !checks.hasPreviewBlock ||
      !checks.hasCreateMissing ||
      !checks.findPanelHidden ||
      !afterFindTab.findVisible ||
      !afterFindTab.hasPreviewIdle;
    if (failed) {
      console.error("VERIFY_FAILED_UI");
      process.exitCode = 3;
    } else if (!previewFlow.ok) {
      console.error("VERIFY_FAILED_PREVIEW_INPUT");
      process.exitCode = 4;
    } else {
      console.log("VERIFY_OK");
      process.exitCode = 0;
    }
  }
} finally {
  await browser.close();
}
