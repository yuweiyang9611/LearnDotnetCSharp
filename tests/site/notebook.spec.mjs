import { test as base, expect } from "@playwright/test";
import { tasks } from "../../artifacts/study-site/progress-catalog.js";

// The learning dashboard: masthead figures, the banner, route cards with rings, bands with bars, the shared scheme control and Chinese typesetting.
const key = "learn-dotnet-csharp-learning-data-v1";
const verified = (id) => ({ revision: tasks[id].revision, steps: Array(tasks[id].steps).fill(true),
  criteria: Array(tasks[id].criteria).fill(true), notes: "已运行并保存完整输出与解释。", completed: true });
const record = {
  version: 1,
  experiments: ["runtime.overview", "runtime.jit-simd-pgo"],
  assessments: { "chapter-01": verified("chapter-01") },
  copies: [],
  lastReading: { title: "19.2 一个贯穿四层的例子", url: "guide/chapter-19/#19-2-一个贯穿四层的例子" },
};
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
async function seed(page, data = record) {
  await page.addInitScript(({ name, value }) => { if (localStorage.getItem(name) === null) localStorage.setItem(name, JSON.stringify(value)); }, { name: key, value: data });
}

test("an opening screen with no record shows zero figures, no stopped card and every ring at 0%", async ({ page }) => {
  await page.goto("./");
  await expect(page.locator("#continue-learning")).toHaveText("从前言开始阅读");
  await expect(page.locator("#continue-learning")).not.toHaveClass(/has-record/);
  await expect(page.locator("#resume-go")).toHaveText("开始阅读");
  await expect(page.locator("#stat-done")).toHaveText("0");
  await expect(page.locator("#stat-reading")).toHaveText("还没有");
  await expect(page.locator("[aria-current]:not(a)")).toHaveCount(0);
  await expect(page.locator(".nb-stopped:visible")).toHaveCount(0);
  await expect(page.locator("[data-record=done]")).toHaveCount(0);
  expect(await page.locator("#progress-bar").evaluate((bar) => getComputedStyle(bar).width)).toBe("0px");
  await expect(page.locator(".path-card")).toHaveCount(8);
  for (const ring of await page.locator(".path-card .nb-ring").all()) await expect(ring).toHaveAttribute("aria-label", "完成 0%");
  // Route heads cycle four hues; red is kept for wrong.
  expect(await page.locator(".path-card .nb-card-head").evaluateAll((heads) => new Set(heads.map((head) => getComputedStyle(head).backgroundColor)).size)).toBe(4);
  await expect(page.locator("#next-lab-command")).toHaveText("dotnet run --project src/LearnDotnetCSharp.App -- run runtime.overview");
});

test("the opening screen continues from the record and marks one stopped row", async ({ page }) => {
  await seed(page);
  await page.goto("./");
  const link = page.locator("#continue-learning");
  await expect(link).toHaveText("继续阅读：19.2 一个贯穿四层的例子");
  await expect(link).toHaveAttribute("href", /^guide\/chapter-19\/#/);
  await expect(link).toHaveClass(/has-record/);
  await expect(page.locator("#resume-go")).toHaveText("继续学习");
  await expect(page.locator("#resume-go")).toHaveAttribute("href", /^guide\/chapter-19\/#/);
  await expect(page.locator("#progress-count")).toHaveText("2 / 53");
  await expect(page.locator("#assessment-count")).toHaveText("1 / 30");
  await expect(page.locator("#stat-done")).toHaveText("2");
  await expect(page.locator("#stat-reading")).toHaveText("第 19 章");
  await expect(page.locator(".experiment-card[data-record=done]")).toHaveCount(2);
  const stopped = page.locator(".experiment-card[aria-current]");
  await expect(stopped).toHaveCount(1);
  await expect(stopped).toHaveAttribute("data-experiment-id", "runtime.collectible-plugin");
  await expect(stopped.locator(".nb-stopped")).toBeVisible();
  await expect(page.locator("#next-lab-title")).toHaveText("下一个实验");
  await expect(page.locator("#next-lab-command")).toContainText("run runtime.collectible-plugin");
  expect(await page.locator('[data-stage-row="01"]').evaluate((row) => row.style.getPropertyValue("--nb-fraction"))).toBe(String(2 / 3));
  await expect(page.locator('[data-stage-row="01"] .nb-ring')).toHaveAttribute("aria-label", "完成 67%");
  await expect(page.locator('[data-stage-row="01"] [data-stage-state]')).toHaveText("2 / 3 实验");
  await expect(page.locator('[data-stage-row="01"] [data-stage-next]')).toHaveText("下一个：可回收插件与卫星资源");
  await expect(page.locator('[data-catalog-group="01"] [data-group-state]')).toHaveText("2 / 3 实验");
  await expect(page.locator('[data-catalog-group="01"] [data-group-bar]')).toHaveAttribute("aria-valuenow", "2");
  await page.locator('[data-complete-id="runtime.collectible-plugin"]').check();
  await expect(page.locator('[data-stage-row="01"]')).toHaveAttribute("data-record", "done");
  await expect(page.locator('[data-stage-row="01"] [data-stage-state]')).toHaveText("3 / 3 已完成");
  await expect(page.locator('[data-stage-row="01"] .nb-ring')).toHaveAttribute("aria-label", "完成 100%");
  // Narrowing the catalog hides the routes with no matching lab, band and all.
  await page.locator('[data-category="runtime"]').click();
  await expect(page.locator(".catalog-group:visible")).toHaveCount(1);
  await expect(page.locator('[data-category="runtime"]')).toHaveAttribute("aria-pressed", "true");
});

test("a chapter opens with its route band and the route's lab record", async ({ page }) => {
  await seed(page);
  await page.goto("guide/chapter-05/");
  const band = page.locator("[data-route-band]");
  await expect(band).toHaveAttribute("data-route", "01");
  await expect(band).toContainText("第 01 条路线 · 运行时直觉");
  await expect(band.locator("[data-route-state]")).toHaveText("2 / 3 实验");
  await expect(band.locator("[data-route-bar]")).toHaveAttribute("aria-valuenow", "2");
  expect(await band.locator("[data-route-bar] > span").evaluate((bar) => getComputedStyle(bar).width)).not.toBe("0px");
});

test("the guide directory marks the stopped unit and verified chapters", async ({ page }) => {
  await seed(page);
  await page.goto("guide/");
  await expect(page.locator("#continue-reading")).toHaveText("继续阅读：19.2 一个贯穿四层的例子");
  await expect(page.locator("#guide-resume-go")).toHaveText("继续阅读");
  await expect(page.locator(".guide-unit-card[aria-current]")).toHaveAttribute("data-unit-route", "guide/chapter-19/");
  await expect(page.locator(".guide-unit-card[aria-current] .nb-stopped")).toBeVisible();
  await expect(page.locator('[data-chapter-task-id="chapter-01"]')).toHaveAttribute("data-record", "done");
  await expect(page.locator("#assessment-progress")).toHaveText("1 / 30 章验收");
  await expect(page.locator("#assessment-track")).toHaveAttribute("aria-valuenow", "1");
  await expect(page.locator("#guide-stat-verified")).toHaveText("1");
  await expect(page.locator("#guide-stat-reading")).toHaveText("第 19 章");
  await expect(page.locator('.guide-part-group:has([data-chapter-task-id="chapter-01"]) [data-part-state]')).toHaveText("1 / 4 章已验收");
});

test("resource pages keep the reading position of the book", async ({ page }) => {
  await seed(page);
  await page.goto("learning-path/");
  await expect(page.locator("html")).toHaveClass(/guide-js/);
  await page.evaluate(() => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  expect((await page.evaluate((name) => JSON.parse(localStorage.getItem(name)), key)).lastReading.url).toMatch(/^guide\/chapter-19\//);
});

test("day and night follow the shared site-theme key across pages", async ({ page }) => {
  await page.goto("./");
  const schemes = page.locator(".nb-theme button");
  await expect(schemes).toHaveText(["白天", "晚上"]);
  await page.locator('.nb-theme button[data-scheme="dark"]').click();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  expect(await page.evaluate(() => localStorage.getItem("site-theme"))).toBe("dark");
  await page.goto("guide/chapter-19/");
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await expect(page.locator('.nb-theme button[data-scheme="dark"]')).toHaveAttribute("aria-pressed", "true");
  await page.locator('.nb-theme button[data-scheme="light"]').click();
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "light");
});

test("reading pages keep one family, whole headings, squeezed marks and coloured code", async ({ page }) => {
  await page.goto("guide/chapter-19/");
  await expect(page.locator("html")).toHaveAttribute("lang", "zh-CN");
  const families = await page.evaluate(() => new Set(["body", ".nb-brand", "h1", "h2", "h3"].map((selector) => getComputedStyle(document.querySelector(selector)).fontFamily)).size);
  expect(families).toBe(1);
  const marks = await page.evaluate(() => {
    const loose = /[”’][，。、；：？！]|[，。、；：？！][“‘]/;
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    let open = 0;
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
      if (loose.test(node.data) && !node.parentElement.closest("code, pre, script, textarea")) open += 1;
    }
    return { open, halved: document.querySelectorAll(".nb-halt").length };
  });
  expect(marks.open).toBe(0);
  expect(marks.halved).toBeGreaterThan(0);
  expect(await page.locator(".toc-link").first().evaluate((link) => getComputedStyle(link).wordBreak)).toBe("keep-all");
  expect(await page.locator("h1").evaluate((heading) => getComputedStyle(heading).textWrap)).not.toContain("balance");
  const code = await page.locator("pre code.language-csharp").first().evaluate(async (element) => {
    const source = new DOMParser().parseFromString(await (await fetch(location.href)).text(), "text/html");
    return { keywords: element.querySelectorAll(".tok-keyword").length, same: element.textContent === source.querySelector("pre code.language-csharp").textContent };
  });
  expect(code.keywords).toBeGreaterThan(0);
  expect(code.same).toBe(true);
});

test("no page scrolls sideways and the menu opens on a phone", async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== "mobile", "The collapsed menu exists only below 56rem.");
  await seed(page);
  for (const route of ["./", "guide/", "guide/chapter-19/", "learning-path/", "search/?q=ValueTask", "layers/"]) {
    await page.goto(route);
    await page.waitForLoadState("networkidle");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), route).toBe(true);
  }
  await page.goto("./");
  const toggle = page.locator(".nb-nav-toggle");
  await expect(page.locator("#site-navigation")).toBeHidden();
  await toggle.click();
  await expect(toggle).toHaveAttribute("aria-expanded", "true");
  await expect(page.locator("#site-navigation")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.locator("#site-navigation")).toBeHidden();
});
