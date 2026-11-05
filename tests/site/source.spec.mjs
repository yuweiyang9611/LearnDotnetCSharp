import { test as base, expect } from "@playwright/test";
import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

// Source read in place: a lab's demo file sits on its chapter in the kit's code panel, every bundled file opens on
// the site's own source page, and nothing outside <noscript> sends the reader to GitHub for a file the site carries.
const siteRoot = new URL("../../artifacts/study-site/", import.meta.url);
const test = base.extend({
  page: async ({ page }, use) => {
    const errors = [];
    page.on("pageerror", (error) => errors.push(error.message));
    page.on("response", (response) => {
      if (response.url().startsWith("http://127.0.0.1:4173/") && response.status() >= 400) errors.push(`${response.status()} ${response.url()}`);
    });
    await use(page);
    expect(errors).toEqual([]);
  },
});

async function htmlFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = await Promise.all(entries.map((entry) => (
    entry.isDirectory() ? htmlFiles(join(directory, entry.name)) : entry.name.endsWith(".html") ? [join(directory, entry.name)] : []
  )));
  return files.flat();
}

const demoPath = "src/LearnDotnetCSharp.App/Demos/Runtime/RuntimeOverviewDemo.cs";

test("a chapter shows each lab's demo file in the panel with line numbers and colour", async ({ page }) => {
  await page.goto("guide/chapter-05/");
  const panel = page.locator("#source-runtime-overview");
  await expect(panel).toHaveClass(/nb-code/);
  await expect(panel.locator(".nb-code-tab")).toHaveCount(1);
  await expect(panel.locator(".nb-code-tab")).toHaveAttribute("aria-selected", "true");
  await expect(panel.locator(".nb-code-line").first()).toHaveAttribute("data-line", "1");
  expect(await panel.locator(".nb-code-line").count()).toBeGreaterThan(20);
  expect(await panel.locator(".nb-code-line .nb-code-text .tok-keyword").count()).toBeGreaterThan(0);
  expect(await panel.locator(".nb-code-line .tok-type").count()).toBeGreaterThan(0);
  // One span beside the number: the line is a two-column grid, so the tokens share it.
  expect(await panel.locator(".nb-code-line").first().evaluate((line) => line.children.length)).toBe(1);
  await expect(panel.locator(".nb-code-note")).toHaveText(/^\d+ 行$/);
  await expect(panel.locator(".nb-code-link")).toHaveAttribute("href", new RegExp(`github\\.com/.+/blob/[0-9a-f]{40}/${demoPath.replaceAll(".", "\\.")}$`));
  await expect(panel.locator(".nb-code-page")).toHaveAttribute("href", `../../source.html?path=${encodeURIComponent(demoPath)}`);
  // The code paper keeps the kit's night hues on the day scheme.
  expect(await panel.locator(".nb-code-view").evaluate((view) => getComputedStyle(view).backgroundColor)).toBe("rgb(28, 26, 23)");
  // The 源码 line above the panel opens the same file on the site.
  await expect(page.locator(`.guide-content a[href="../../source.html?path=${encodeURIComponent(demoPath)}"]`).first()).toHaveText("RuntimeOverviewDemo.cs");
  // A project lab carries its capstone files as extra tabs.
  await page.goto("guide/chapter-28/");
  const project = page.locator("#source-project-cancellable-data-pipeline");
  await expect(project.locator(".nb-code-tab")).toHaveCount(3);
  await project.locator(".nb-code-tab").nth(1).click();
  await expect(project.locator('.nb-code-tab[aria-selected="true"]')).toHaveText("ResumableDataPipeline.cs");
  await expect(project.locator(".nb-code-line").first()).toHaveAttribute("data-line", "1");
  await expect(project.locator(".nb-code-page")).toHaveAttribute("href", /ResumableDataPipeline\.cs$/);
});

test("the source page shows a file with a marked range, its siblings, and a directory listing", async ({ page }) => {
  await page.goto(`source.html?path=${encodeURIComponent(demoPath)}#L5-L9`);
  await expect(page.locator("h1")).toHaveText("RuntimeOverviewDemo.cs");
  await expect(page.locator("#source-crumbs a")).toHaveCount(5);
  await expect(page.locator("#source-crumbs a").nth(1)).toHaveAttribute("href", "source.html?dir=src");
  await expect(page.locator("#source-panel .nb-code-line[data-marked]")).toHaveCount(5);
  await expect(page.locator("#source-panel .nb-code-line[data-marked]").first()).toHaveAttribute("data-line", "5");
  expect(await page.locator("#source-panel .tok-keyword").count()).toBeGreaterThan(0);
  expect(await page.locator("#source-sibling-list li").count()).toBeGreaterThan(0);
  await expect(page.locator("#source-note")).toContainText("仓库提交");
  await expect(page).toHaveTitle(/^RuntimeOverviewDemo\.cs · /);

  await page.goto("source.html?dir=src%2FLearnDotnetCSharp.App%2FDemos");
  await expect(page.locator("h1")).toHaveText("Demos/");
  expect(await page.locator(".source-row").count()).toBeGreaterThan(10);
  await expect(page.locator(".source-row").first().locator("span")).toHaveText(/个文件 · \d+ 行$/);
  await page.locator(".source-row a", { hasText: "Runtime/" }).click();
  await expect(page.locator(".source-row a", { hasText: "RuntimeOverviewDemo.cs" })).toBeVisible();
  await expect(page.locator(".source-row").first().locator("span")).toHaveText(/行 · [\d.]+ KB$/);

  await page.goto("source.html");
  await expect(page.locator("h1")).toHaveText("源码");
  for (const root of ["src/", "native/", "python/", "docs/"]) await expect(page.locator(".source-row a", { hasText: root })).toBeVisible();
  await expect(page.locator('meta[name="robots"]')).toHaveAttribute("content", "noindex");

  await page.goto("source.html?path=src%2FMissing.cs");
  await expect(page.locator("h1")).toHaveText("没有这个文件");
  await expect(page.locator(".source-row").first()).toBeVisible();
});

test("every lab card and guide link to a bundled file stays on the site", async ({ page }) => {
  const index = JSON.parse(await readFile(new URL("assets/source/index.json", siteRoot), "utf8"));
  const bundle = new Set(index.files.map((file) => file.path));
  expect(index.version).toBe(1);
  expect(index.revision).toMatch(/^[0-9a-f]{40}$/);
  expect(index.blobBase).toBe(`${index.repository}/blob/${index.revision}/`);
  expect(bundle.size).toBeGreaterThanOrEqual(80);
  for (const path of [demoPath, "native/learn_c/learn_c.c", "python/interop_worker.py", "docs/learning-path.md"]) expect(bundle.has(path)).toBe(true);
  for (const file of await htmlFiles(fileURLToPath(siteRoot))) {
    const html = (await readFile(file, "utf8")).replace(/<noscript>[\s\S]*?<\/noscript>/g, "");
    for (const match of html.matchAll(/href="https:\/\/github\.com\/[^"]+\/(?:blob|tree)\/[^/"]+\/([^"#?]+)/g)) {
      const path = decodeURIComponent(match[1]).replace(/\/+$/, "");
      expect(bundle.has(path), `${file} links ${path} to GitHub`).toBe(false);
      expect([...bundle].some((bundled) => bundled.startsWith(`${path}/`)), `${file} links the folder ${path} to GitHub`).toBe(false);
    }
  }
  await page.goto("./");
  const cardLink = page.locator('#experiment-runtime\\.overview .source-link');
  await expect(cardLink).toHaveAttribute("href", `./source.html?path=${encodeURIComponent(demoPath)}`);
  await cardLink.click();
  await expect(page.locator("#source-panel .nb-code-line").first()).toHaveAttribute("data-line", "1");
  await page.goto("guide/chapter-26/");
  await expect(page.locator('.guide-content a[href^="../../source.html?path=native"]').first()).toBeVisible();
  await expect(page.locator('.guide-content a[href*="github.com"][href*="/blob/"]:not(.nb-code-link)')).toHaveCount(0);
});

test("without script the panel keeps its tabs and a GitHub link", async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  await page.goto("guide/chapter-05/");
  const panel = page.locator("#source-runtime-overview");
  await expect(panel.locator(".nb-code-tab")).toHaveText(["RuntimeOverviewDemo.cs"]);
  await expect(panel.locator("noscript a")).toHaveAttribute("href", new RegExp(`/blob/[0-9a-f]{40}/${demoPath}$`));
  await expect(panel.locator("noscript a")).toHaveText(demoPath);
  await context.close();
});

test("the panel and the source page do not scroll sideways on a 390px phone", async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== "mobile", "The narrow layout is the mobile project's.");
  await page.setViewportSize({ width: 390, height: 844 });
  for (const route of ["guide/chapter-05/", "guide/chapter-28/", `source.html?path=${encodeURIComponent(demoPath)}`, "source.html?dir=src"]) {
    await page.goto(route);
    await page.locator(".nb-code-line, .source-row").first().waitFor();
    await page.waitForLoadState("networkidle");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), route).toBe(true);
    const code = page.locator(".nb-code-view").first();
    if (await code.count()) {
      const sizes = await code.evaluate((view) => ({ clientWidth: view.clientWidth, scrollWidth: view.scrollWidth, pageWidth: document.documentElement.clientWidth }));
      expect(sizes.clientWidth, route).toBeLessThanOrEqual(sizes.pageWidth);
      expect(sizes.scrollWidth, route).toBeGreaterThan(sizes.clientWidth);
    }
  }
});
