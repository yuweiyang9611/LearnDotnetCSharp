import { test as base, expect } from "@playwright/test";
import { chapters, localOnly, quizzes } from "../../artifacts/study-site/progress-catalog.js";

// Phase 3: a lab runs in the browser on the vendored Roslyn + .NET runtime, a lab that cannot says 需要本机,
// and the loop band on every chapter reads its five states from the record.
const key = "learn-dotnet-csharp-learning-data-v1";
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
async function stored(page) { return page.evaluate((name) => JSON.parse(localStorage.getItem(name)), key); }
async function seed(page, data) {
  await page.addInitScript(({ name, value }) => { if (localStorage.getItem(name) === null) localStorage.setItem(name, JSON.stringify(value)); }, { name: key, value: data });
}
const states = (band) => band.locator("li").evaluateAll((items) => items.map((item) => `${item.dataset.step}:${item.dataset.state}`));
const localLab = Object.keys(localOnly)[0];
const localChapter = chapters.find(({ labs }) => labs.includes(localLab));

test("a lab runs in the browser and its output reads 运行结果", async ({ page }) => {
  test.setTimeout(240_000);
  await page.goto("guide/chapter-05/");
  const panel = page.locator("#source-runtime-overview");
  await expect(panel).toHaveAttribute("data-run-type", "LearnDotnetCSharp.Demos.Runtime.RuntimeOverviewDemo");
  await expect(panel.locator("#run-runtime-overview")).toHaveText("运行");
  await expect(panel.locator("#edit-runtime-overview")).toHaveText("改一改再跑");
  await panel.locator("#run-runtime-overview").click();
  const output = panel.locator("#output-runtime-overview");
  await expect(output).toBeVisible();
  // The first click loads the runtime; the stage words show in the status while it does.
  await expect(panel.locator(".nb-code-status")).toContainText(/加载运行时中|编译中|执行中|运行完成/);
  await expect(output).toHaveAttribute("data-state", "done", { timeout: 150_000 });
  await expect(output.locator(".nb-code-output-label")).toContainText("运行结果");
  await expect(output.locator("pre")).toContainText("Framework");
  await expect(output.locator("pre")).toContainText("Dynamic code supported");
  // The run is recorded and the band moves on.
  const record = await stored(page);
  expect(record.runs["runtime.overview"].mode).toBe("browser");
  await expect(page.locator('#lesson-steps [data-step="run"]')).toHaveAttribute("data-state", "done");
  // The edited copy runs too: a changed line shows in the output.
  await panel.locator("#edit-runtime-overview").click();
  const editor = panel.locator("#editor-runtime-overview");
  await expect(editor).toBeVisible();
  await editor.fill((await editor.inputValue()).replace('context.WriteProperty("Framework"', 'context.WriteLine("改过的副本");\n        context.WriteProperty("Framework"'));
  await panel.locator("#run-runtime-overview").click();
  await expect(output).toHaveAttribute("data-state", "running");
  await expect(output).toHaveAttribute("data-state", "done", { timeout: 150_000 });
  await expect(output.locator(".nb-code-output-label")).toContainText("已改动的副本");
  await expect(output.locator("pre")).toContainText("改过的副本");
  // A compile error names the file and line.
  await editor.fill((await editor.inputValue()).replace("return ValueTask.CompletedTask;", "return ValueTask.CompletedTask"));
  await panel.locator("#run-runtime-overview").click();
  await expect(output).toHaveAttribute("data-state", "wrong", { timeout: 150_000 });
  await expect(output.locator(".nb-code-output-label")).toHaveText("编译失败");
  await expect(output.locator("pre")).toContainText("RuntimeOverviewDemo.cs");
  await panel.locator("#reset-runtime-overview").click();
  await expect(panel.locator("#editor-runtime-overview")).toBeHidden();
});

test("a lab that cannot run in the browser says 需要本机 and keeps its command", async ({ page }) => {
  expect(localLab, "the availability list names at least one lab").toBeTruthy();
  await page.goto(localChapter.route);
  const panel = page.locator(`#source-${localLab.replaceAll(".", "-")}`);
  await expect(panel.locator(".nb-code-line").first()).toBeVisible();
  await expect(panel.locator("[data-run-lab]")).toHaveCount(0);
  await expect(panel.locator(".lab-local-note")).toContainText("需要本机");
  await expect(panel.locator(".lab-local-note")).toContainText(localOnly[localLab]);
  await expect(panel.locator(".lab-local-command code")).toHaveText(`dotnet run --project src/LearnDotnetCSharp.App -- run ${localLab}`);
  await page.evaluate(() => Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText: async (text) => { window.copiedText = text; } } }));
  await panel.locator("[data-copy-lab-command]").click();
  expect(await page.evaluate(() => window.copiedText)).toContain(`run ${localLab}`);
  await expect(panel.locator("[data-copy-lab-command]")).toHaveText("已复制");
  expect((await stored(page)).runs[localLab].mode).toBe("local");
  await expect(page.locator('#lesson-steps [data-step="run"]')).toHaveAttribute("data-state", "done");
});

test("the band shows five steps, moves with what the reader does, and reads the same on the index", async ({ page }) => {
  await page.goto("guide/chapter-05/");
  const band = page.locator("#lesson-steps");
  await expect(band).toHaveClass(/nb-steps/);
  await expect(band.locator("li")).toHaveCount(5);
  await expect(band.locator("li a")).toHaveText(["读", "看代码", "跑", "自测", "记一笔"]);
  // Opening the page is reading it; the next step is where the reader stopped.
  expect(await states(band)).toEqual(["read:done", "code:now", "run:pending", "quiz:pending", "note:pending"]);
  await expect(band.locator('[data-step="quiz"] .nb-steps-count')).toHaveText("3 题");
  await expect(band.locator('[data-step="code"] .nb-steps-count')).toHaveText("0 / 2");
  await expect(band.locator('[data-step="code"] a')).toHaveAttribute("href", "#source-runtime-overview");
  await expect(band.locator('[data-step="note"] a')).toHaveAttribute("href", "#chapter-05-notes");
  await page.locator("#source-runtime-overview .nb-code-tab").first().click();
  expect(await states(band)).toEqual(["read:done", "code:done", "run:now", "quiz:pending", "note:pending"]);
  expect((await stored(page)).sourceViews).toEqual(["runtime.overview"]);
  await expect(band.locator('[data-step="code"] .nb-steps-count')).toHaveText("1 / 2");
  // A chapter with two labs counts them; a note makes the last step done without moving the stop.
  await page.locator("[data-assessment-notes]").fill("第 5 章的笔记。");
  expect(await states(band)).toEqual(["read:done", "code:done", "run:now", "quiz:pending", "note:done"]);
  await page.reload();
  expect(await states(band)).toEqual(["read:done", "code:done", "run:now", "quiz:pending", "note:done"]);
  // A chapter with no lab keeps the shape with dashed steps.
  await page.goto("guide/chapter-03/");
  expect(await states(page.locator("#lesson-steps"))).toEqual(["read:done", "code:none", "run:none", "quiz:now", "note:pending"]);
  // The index continues at the earliest chapter with a stop: chapter 1 (read by reaching chapter 5) at its quiz.
  await page.goto("guide/");
  await expect(page.locator("#continue-reading")).toHaveText("第 1 章 · 自测");
  await expect(page.locator("#continue-reading")).toHaveAttribute("href", /guide\/chapter-01\/#chapter-01-quiz$/);
  await expect(page.locator(".guide-unit-card[aria-current]")).toHaveAttribute("data-unit-route", "guide/chapter-01/");
});

test("the chapter self-test records a pass with its revision and the band follows", async ({ page }) => {
  await page.goto("guide/chapter-01/");
  const quiz = page.locator("#chapter-01-quiz");
  await expect(quiz.locator("[data-quiz-question]")).toHaveCount(3);
  await expect(quiz.locator("[data-quiz-state]")).toHaveText("未作答");
  await quiz.locator('button[type="submit"]').click();
  await expect(quiz.locator("[data-quiz-status]")).toContainText("还有 3 题没有选答案");
  // One wrong answer does not pass.
  for (const question of await quiz.locator("[data-quiz-question]").all()) {
    const answer = Number(await question.getAttribute("data-quiz-answer"));
    await question.locator("input").nth(answer === 0 ? 1 : 0).check();
  }
  await quiz.locator('button[type="submit"]').click();
  await expect(quiz.locator("[data-quiz-status]")).toContainText("0 / 3 答对");
  await expect(quiz.locator('[data-quiz-question][data-result="wrong"]')).toHaveCount(3);
  expect((await stored(page)).quizzes["chapter-01"]).toBeUndefined();
  for (const question of await quiz.locator("[data-quiz-question]").all()) {
    await question.locator("input").nth(Number(await question.getAttribute("data-quiz-answer"))).check();
  }
  await quiz.locator('button[type="submit"]').click();
  await expect(quiz.locator("[data-quiz-status]")).toContainText("3 / 3 全对");
  await expect(quiz.locator("[data-quiz-state]")).toContainText("已通过");
  expect((await stored(page)).quizzes["chapter-01"].revision).toBe(quizzes["chapter-01"].revision);
  expect(await states(page.locator("#lesson-steps"))).toEqual(["read:done", "code:none", "run:none", "quiz:done", "note:now"]);
  await page.reload();
  await expect(quiz.locator("[data-quiz-state]")).toContainText("已通过");
  // A pass under an older revision asks again.
  await page.evaluate((name) => {
    const data = JSON.parse(localStorage.getItem(name));
    data.quizzes["chapter-01"].revision = "old";
    localStorage.setItem(name, JSON.stringify(data));
  }, key);
  await page.reload();
  await expect(quiz.locator("[data-quiz-state]")).toHaveText("未作答");
});

test("a record written before the loop fields still loads and the index reads it", async ({ page }) => {
  await seed(page, { version: 1, experiments: ["runtime.overview"], assessments: {}, copies: [], lastReading: { title: "6.1", url: "guide/chapter-06/#6-1-language-csharp14" } });
  await page.goto("guide/");
  await expect(page.locator("#continue-reading")).toHaveText("第 1 章 · 自测");
  await page.goto("guide/chapter-06/");
  const record = await stored(page);
  expect(record.experiments).toEqual(["runtime.overview"]);
  expect(record.visited).toEqual(["guide/chapter-06/"]);
  expect(record.runs).toEqual({});
  expect(await states(page.locator("#lesson-steps"))).toEqual(["read:done", "code:now", "run:pending", "quiz:pending", "note:pending"]);
});

test("without script the band renders every step pending and the quiz is a plain form", async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  await page.goto("guide/chapter-05/");
  const band = page.locator("#lesson-steps");
  await expect(band.locator("li")).toHaveCount(5);
  expect(await states(band)).toEqual(["read:pending", "code:pending", "run:pending", "quiz:pending", "note:pending"]);
  await expect(page.locator("#chapter-05-quiz input[type=radio]")).toHaveCount(9);
  await context.close();
});

test("the band and the output keep their shape at 390px", async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== "mobile", "The narrow layout is the mobile project's.");
  await page.setViewportSize({ width: 390, height: 844 });
  await seed(page, { version: 1, experiments: [], assessments: {}, copies: [], lastReading: null, visited: ["guide/chapter-05/"], sourceViews: ["runtime.overview"], runs: { "runtime.overview": { ranAt: "2026-11-06T00:00:00.000Z", mode: "browser" } }, quizzes: {} });
  for (const route of ["guide/chapter-05/", localChapter.route, "guide/"]) {
    await page.goto(route);
    await page.waitForLoadState("networkidle");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), route).toBe(true);
  }
  await page.goto("guide/chapter-05/");
  const band = page.locator("#lesson-steps");
  const box = await band.boundingBox();
  expect(box.width).toBeLessThanOrEqual(390);
  // Five pills wrap to more than one row, each staying inside the page.
  const rows = await band.locator("li").evaluateAll((items) => new Set(items.map((item) => Math.round(item.getBoundingClientRect().top))).size);
  expect(rows).toBeGreaterThan(1);
  expect(await band.locator("li").evaluateAll((items) => items.every((item) => item.getBoundingClientRect().right <= 390))).toBe(true);
  expect(await states(band)).toEqual(["read:done", "code:done", "run:done", "quiz:now", "note:pending"]);
});
