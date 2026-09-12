import { test as base, expect } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { tasks } from "../../artifacts/study-site/progress-catalog.js";

const key = "learn-dotnet-csharp-learning-data-v1";
const empty = () => ({ version: 1, experiments: [], assessments: {}, copies: [], lastReading: null });
const record = (notes, revision = tasks["chapter-01"].revision) => ({ revision, notes, completed: true,
  steps: Array(tasks["chapter-01"].steps).fill(true), criteria: Array(tasks["chapter-01"].criteria).fill(true) });
const backup = (data) => ({ format: "LearnDotnetCSharp.learning-backup", version: 1, exportedAt: "2026-09-12T00:00:00Z", data });
const test = base.extend({
  page: async ({ page }, use) => {
    const errors = [];
    page.on("pageerror", (error) => errors.push(error.message));
    page.on("response", (response) => {
      if (response.url().startsWith("http://127.0.0.1:4173/") && response.status() >= 400) errors.push(`${response.status()} ${response.url()}`);
    });
    page.on("requestfailed", (request) => {
      if (request.url().startsWith("http://127.0.0.1:4173/") && !request.failure()?.errorText.includes("ERR_ABORTED")) errors.push(request.failure()?.errorText);
    });
    await use(page);
    expect(errors).toEqual([]);
  },
});
async function upload(page, value) {
  await page.locator("[data-backup-file]").setInputFiles({ name: "backup.json", mimeType: "application/json", buffer: Buffer.from(JSON.stringify(value)) });
}
async function stored(page) { return page.evaluate((name) => JSON.parse(localStorage.getItem(name)), key); }
async function seed(page, data) {
  await page.addInitScript(({ name, value }) => { if (localStorage.getItem(name) === null) localStorage.setItem(name, JSON.stringify(value)); }, { name: key, value: data });
}
async function download(page) {
  const pending = page.waitForEvent("download");
  await page.locator("[data-export-backup]").click();
  const result = await pending;
  return JSON.parse(await readFile(await result.path(), "utf8"));
}

test("catalog filters, persists progress, copies and navigates", async ({ page }) => {
  await page.goto("./");
  await page.locator('[data-category="runtime"]').click();
  await expect(page.locator("#catalog-count")).toHaveText("3");
  await page.locator("#catalog-search").fill("runtime.overview");
  await expect(page.locator("#catalog-count")).toHaveText("1");
  await page.locator('[data-complete-id="runtime.overview"]').click();
  await expect(page.locator("#progress-count")).toHaveText("1 / 53");
  await page.reload();
  await expect(page.locator("#progress-count")).toHaveText("1 / 53");
  await page.evaluate(() => Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText: async (text) => { window.copiedText = text; } } }));
  await page.locator('#experiment-runtime\\.overview [data-copy]').click();
  expect(await page.evaluate(() => window.copiedText)).toContain("run runtime.overview");
  await page.locator('#experiment-runtime\\.overview a').filter({ hasText: "阅读讲解" }).click();
  await expect(page).toHaveURL(/LearnDotnetCSharp\/guide\/chapter-05\//);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
});

test("full text search and legacy chapter anchor navigation", async ({ page }) => {
  await page.goto("search/?q=runtime.overview");
  await expect(page.locator("#search-results-list a").first()).toBeVisible();
  await page.locator("#search-results-list a").first().click();
  await expect(page).toHaveURL(/LearnDotnetCSharp\//);
  await page.goto("guide/#1-环境基线");
  await expect(page).toHaveURL(/guide\/chapter-01\//);
  await expect(page.locator("[data-chapter-assessment]")).toBeVisible();
});

test("chapter assessment survives reload and updates another tab", async ({ page, context }) => {
  await page.goto("guide/chapter-01/");
  const directory = await context.newPage();
  await directory.goto("http://127.0.0.1:4173/LearnDotnetCSharp/guide/");
  for (const box of await page.locator("[data-assessment-step], [data-assessment-criterion]").all()) await box.check();
  await page.locator("[data-assessment-notes]").fill("已运行全部实验，记录输出并验证通过标准。");
  await page.locator("[data-assessment-complete]").click();
  await expect(page.locator("[data-assessment-state]")).toHaveText("已完成验收");
  await expect(directory.locator('[data-chapter-task-id="chapter-01"] [data-guide-task-status]')).toHaveText("已验收");
  await page.reload();
  await expect(page.locator("[data-assessment-state]")).toHaveText("已完成验收");
  await expect(page.locator("[data-assessment-notes]")).toHaveValue("已运行全部实验，记录输出并验证通过标准。");
  await directory.close();
});

test("preview cancellation, merge conflicts and repeated backup roundtrip", async ({ page }) => {
  const local = { ...empty(), experiments: ["runtime.overview"], assessments: { "chapter-01": record("本地笔记：已经完成第一章验收并保留记录。") } };
  await seed(page, local);
  await page.goto("./");
  const incoming = backup({ ...empty(), experiments: ["language.csharp14"], assessments: { "chapter-01": record("导入笔记：这是另一台设备保留的第一章记录。") } });
  await upload(page, incoming);
  await expect(page.locator("[data-preview-summary]")).toContainText("冲突副本 1");
  await page.locator("[data-cancel-import]").click();
  expect(await stored(page)).toEqual(local);
  await upload(page, incoming);
  await page.locator("[data-confirm-import]").click();
  await expect(page.locator("#progress-count")).toHaveText("2 / 53");
  let result = await stored(page);
  expect(result.assessments["chapter-01"].notes).toBe(local.assessments["chapter-01"].notes);
  expect(result.copies).toHaveLength(1);
  await page.locator("[data-note-copies] summary").click();
  await expect(page.locator("[data-copy-list] pre")).toHaveText(incoming.data.assessments["chapter-01"].notes);
  await upload(page, incoming);
  await expect(page.locator("[data-preview-summary]")).toContainText("冲突副本 0");
  await page.locator("[data-confirm-import]").click();
  result = await stored(page);
  expect(result.copies).toHaveLength(1);
  expect((await download(page)).data).toEqual(result);
});

test("old revisions and unknown records survive without counting", async ({ page }) => {
  await page.goto("./");
  await upload(page, backup({ ...empty(), experiments: ["future.demo"], assessments: {
    "chapter-01": record("历史版本的笔记需要保留，不能计入新课程。", "old-revision"),
    "chapter-99": record("未来章节的笔记需要保留，不可丢弃。", "future"),
  } }));
  await page.locator("[data-confirm-import]").click();
  await expect(page.locator("#progress-count")).toHaveText("0 / 53");
  const exported = await download(page);
  expect(exported.data.copies).toHaveLength(1);
  expect(exported.data.assessments["chapter-99"]).toBeTruthy();
  await page.goto("guide/");
  await expect(page.locator('[data-chapter-task-id="chapter-01"] [data-guide-task-status]')).toHaveText("待验收");
  await upload(page, exported);
  await page.locator("[data-confirm-import]").click();
  expect((await download(page)).data.copies).toHaveLength(1);
});

test("legacy storage migrates and preserves original keys", async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem("learn-dotnet-csharp-progress-v1", JSON.stringify(["runtime.overview"]));
    localStorage.setItem("learn-dotnet-csharp-guide-last-reading-v2", JSON.stringify({ title: "第一章", url: "/LearnDotnetCSharp/guide/chapter-01/#1-环境基线" }));
  });
  await page.goto("./");
  await expect(page.locator("#progress-count")).toHaveText("1 / 53");
  expect(await page.evaluate(() => localStorage.getItem("learn-dotnet-csharp-progress-v1"))).not.toBeNull();
  await page.goto("guide/");
  await expect(page.locator("#continue-reading")).toHaveAttribute("href", /LearnDotnetCSharp\/guide\/chapter-01\//);
});

test("invalid and oversized imports never modify saved progress", async ({ page }) => {
  await page.goto("./");
  const previous = await stored(page);
  for (const value of [{}, { ...backup(empty()), version: 999 }, backup({ ...empty(), assessments: { "chapter-01": { notes: 42 } } })]) {
    await upload(page, value);
    await expect(page.locator("[data-backup-message]")).not.toBeEmpty();
    expect(await stored(page)).toEqual(previous);
    await expect(page.locator("dialog")).not.toBeVisible();
  }
  await page.locator("[data-backup-file]").setInputFiles({ name: "large.json", mimeType: "application/json", buffer: Buffer.alloc(5 * 1024 * 1024 + 1, " ") });
  await expect(page.locator("[data-backup-message]")).toContainText("超过 5 MiB");
  expect(await stored(page)).toEqual(previous);
});

test("untrusted reading destinations are rejected", async ({ page }) => {
  await page.goto("./");
  for (const url of ["https://example.com/guide/chapter-01/", "javascript:alert(1)", "/outside/guide/chapter-01/", "guide/../../outside"]) {
    await upload(page, backup({ ...empty(), lastReading: { title: "bad", url } }));
    await page.locator("[data-confirm-import]").click();
    expect((await stored(page)).lastReading).toBeNull();
  }
});

test("quota failure retains edits, exports them and retries", async ({ page }) => {
  await page.goto("guide/chapter-01/");
  await page.evaluate(() => {
    window.originalSetItem = Storage.prototype.setItem;
    Storage.prototype.setItem = function () { throw new DOMException("quota", "QuotaExceededError"); };
  });
  const note = "保存失败时，这条笔记仍应保留在页面和导出备份里。";
  await page.locator("[data-assessment-notes]").fill(note);
  await expect(page.locator("[data-assessment-status]")).toContainText("尚未保存");
  await expect(page.locator("[data-retry-save]")).toBeVisible();
  expect((await download(page)).data.assessments["chapter-01"].notes).toBe(note);
  await page.evaluate(() => { Storage.prototype.setItem = window.originalSetItem; });
  await page.locator("[data-retry-save]").click();
  await expect(page.locator("[data-retry-save]")).toBeHidden();
  expect((await stored(page)).assessments["chapter-01"].notes).toBe(note);
});

test("denied storage permits reading and exporting unsaved progress", async ({ page }) => {
  await page.addInitScript(() => Object.defineProperty(window, "localStorage", { get() { throw new DOMException("denied", "SecurityError"); } }));
  await page.goto("./");
  await page.locator('[data-complete-id="runtime.overview"]').click();
  await expect(page.locator("[data-storage-state]")).toContainText("尚未保存");
  expect((await download(page)).data.experiments).toContain("runtime.overview");
});

test("failed import persistence keeps disk unchanged and can retry", async ({ page }) => {
  await page.goto("./");
  const original = await stored(page);
  await upload(page, backup({ ...empty(), experiments: ["runtime.overview"] }));
  await page.evaluate(() => {
    window.originalSetItem = Storage.prototype.setItem;
    Storage.prototype.setItem = () => { throw new DOMException("quota", "QuotaExceededError"); };
  });
  await page.locator("[data-confirm-import]").click();
  await expect(page.locator("[data-backup-message]")).toContainText("尚未保存");
  expect(await stored(page)).toEqual(original);
  await page.evaluate(() => { Storage.prototype.setItem = window.originalSetItem; });
  await page.locator("[data-retry-save]").click();
  expect((await stored(page)).experiments).toContain("runtime.overview");
});

test("clipboard failures never show success; clearing keeps notes", async ({ page }) => {
  await seed(page, { ...empty(), experiments: ["runtime.overview"], assessments: { "chapter-01": record("这里是需要保留的完整章节验收笔记。") } });
  await page.goto("./");
  await page.evaluate(() => {
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText: async () => { throw new Error("denied"); } } });
    document.execCommand = () => false;
  });
  await page.locator('#experiment-runtime\\.overview [data-copy]').click();
  await expect(page.locator("#toast")).toContainText("复制失败");
  page.once("dialog", (dialog) => dialog.accept());
  await page.locator("#reset-progress").click();
  await expect(page.locator("#progress-count")).toHaveText("0 / 53");
  expect((await stored(page)).assessments["chapter-01"]).toBeTruthy();
});
