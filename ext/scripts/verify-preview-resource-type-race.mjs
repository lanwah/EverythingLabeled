/**
 * UI + race: open options, select all ASAP / via keyboard, save, reload.
 */
import puppeteer from "puppeteer-core";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ext = path.resolve(__dirname, "../dist");
const userDataDir = path.resolve(__dirname, "../../.tmp/chrome-ext-prt-race");

const browserPath =
  process.env.EL_BROWSER ??
  [
    "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
    "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe",
    "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
  ].find((p) => fs.existsSync(p));

fs.rmSync(userDataDir, { recursive: true, force: true });
fs.mkdirSync(userDataDir, { recursive: true });

const browser = await puppeteer.launch({
  executablePath: browserPath,
  headless: false,
  userDataDir,
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

async function waitExt() {
  for (let i = 0; i < 40; i++) {
    const t = browser.targets().find(
      (x) =>
        x.type() === "service_worker" &&
        x.url().includes("chrome-extension://") &&
        x.url().includes("background.js"),
    );
    if (t) return t;
    await new Promise((r) => setTimeout(r, 250));
  }
  throw new Error("no SW");
}

const id = new URL((await waitExt()).url()).host;
const page = await browser.newPage();
await page.setViewport({ width: 520, height: 760 });
const optionsUrl = `chrome-extension://${id}/options.html`;

function seedFields() {
  return page.evaluate(() => {
    const inputs = [...document.querySelectorAll("input")];
    const text = inputs.find((i) => i.type === "text");
    const pw = inputs.find((i) => i.type === "password");
    const num = inputs.find((i) => i.type === "number");
    if (text) text.value = "http://127.0.0.1:5123";
    if (pw) pw.value = "local-token";
    if (num) num.value = "5";
  });
}

async function readState() {
  return page.evaluate(async () => {
    const sel = document.querySelector('select[aria-label="预览资源类型"]');
    return {
      ui: sel?.value ?? null,
      index: sel?.selectedIndex ?? null,
      status: document.querySelector(".status")?.textContent ?? "",
      stored: await chrome.storage.local.get(["previewResourceType"]),
    };
  });
}

try {
  // --- race: submit all before getConfig returns ---
  await page.goto(optionsUrl, { waitUntil: "domcontentloaded" });
  await page.waitForSelector('select[aria-label="预览资源类型"]');
  await seedFields();
  await page.evaluate(() => {
    document.querySelector('select[aria-label="预览资源类型"]').value = "all";
    document.querySelector("form").requestSubmit();
  });
  await new Promise((r) => setTimeout(r, 1500));
  const race = await readState();
  console.log("raceImmediateSave=", JSON.stringify(race));

  // --- keyboard select after load ---
  await page.evaluate(async () => {
    await chrome.storage.local.clear();
  });
  await page.goto(optionsUrl, { waitUntil: "domcontentloaded" });
  await page.waitForSelector('select[aria-label="预览资源类型"]');
  await new Promise((r) => setTimeout(r, 1200));
  await seedFields();
  await page.focus('select[aria-label="预览资源类型"]');
  await page.keyboard.press("ArrowDown");
  await page.keyboard.press("Enter");
  console.log("afterKeyboard=", JSON.stringify(await readState()));
  await page.click("button.btn-primary");
  await new Promise((r) => setTimeout(r, 1000));
  console.log("afterButtonSave=", JSON.stringify(await readState()));

  await page.goto(optionsUrl, { waitUntil: "domcontentloaded" });
  await page.waitForSelector('select[aria-label="预览资源类型"]');
  await new Promise((r) => setTimeout(r, 1200));
  console.log("afterReload=", JSON.stringify(await readState()));

  const final = await readState();
  if (final.ui === "all" && final.stored.previewResourceType === "all") {
    console.log("PASS");
  } else {
    console.error("FAIL", final);
    process.exitCode = 1;
  }
} finally {
  await browser.close();
}
