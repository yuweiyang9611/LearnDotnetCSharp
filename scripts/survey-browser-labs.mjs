// Runs every lab in the browser against the local build and reports which finish: the source of truth for
// site/lab-availability.js. Build the runtime and the site first (npm run build:runtime && npm run build:site),
// then `npm run survey:labs`. Writes artifacts/lab-survey.json and prints one line per lab.
import { spawn } from "node:child_process";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "@playwright/test";

const repositoryRoot = resolve(fileURLToPath(new URL("..", import.meta.url)));
const siteRoot = resolve(repositoryRoot, "artifacts", "study-site");
const port = Number.parseInt(process.env.LAB_SURVEY_PORT ?? "4175", 10);
const base = `http://127.0.0.1:${port}/LearnDotnetCSharp/`;
const only = process.argv.slice(2);

const manifest = JSON.parse(await readFile(resolve(siteRoot, "guide-manifest.json"), "utf8"));
const { experiments } = await import(new URL("../site/catalog-data.js", import.meta.url));
const server = spawn(process.execPath, [resolve(repositoryRoot, "scripts", "serve-study-site.mjs"), String(port), siteRoot, "/LearnDotnetCSharp/"], { stdio: "ignore" });
await new Promise((done) => setTimeout(done, 800));

const browser = await chromium.launch();
const results = [];
try {
  const page = await browser.newPage();
  page.setDefaultTimeout(130_000);
  for (const experiment of experiments) {
    if (only.length && !only.includes(experiment.id)) continue;
    const key = experiment.id.replaceAll(".", "-");
    const route = manifest.experiments[experiment.id].replace(/^\.\//, "").split("#")[0];
    const started = Date.now();
    let record;
    try {
      await page.goto(`${base}${route}`);
      const panel = page.locator(`#source-${key}`);
      await panel.locator(".nb-code-line").first().waitFor();
      const run = panel.locator("[data-run-lab]");
      if (await run.count() === 0) {
        const note = await panel.locator(".lab-local-note").textContent();
        record = { id: experiment.id, ok: false, label: "需要本机", text: note?.trim() ?? "", ms: 0 };
      } else {
        await run.click();
        const output = page.locator(`#output-${key}`);
        await output.locator("xpath=self::*[@data-state='done' or @data-state='wrong']").waitFor({ timeout: 125_000 });
        const label = (await output.locator(".nb-code-output-label").textContent())?.trim() ?? "";
        const text = (await output.locator("pre").textContent()) ?? "";
        record = { id: experiment.id, ok: (await output.getAttribute("data-state")) === "done", label, text: text.slice(0, 1200), ms: Date.now() - started };
      }
    } catch (error) {
      record = { id: experiment.id, ok: false, label: "survey-error", text: error.message, ms: Date.now() - started };
    }
    results.push(record);
    console.log(`${record.ok ? "OK  " : "FAIL"} ${record.id.padEnd(42)} ${record.label} (${Math.round(record.ms / 1000)} s)${record.ok ? "" : `\n     ${record.text.split("\n").filter(Boolean).slice(-3).join(" | ").slice(0, 400)}`}`);
  }
} finally {
  await browser.close();
  server.kill();
}
await mkdir(resolve(repositoryRoot, "artifacts"), { recursive: true });
await writeFile(resolve(repositoryRoot, "artifacts", "lab-survey.json"), `${JSON.stringify(results, null, 2)}\n`, "utf8");
const ok = results.filter((record) => record.ok).length;
console.log(`\n${ok} of ${results.length} labs finished in the browser; survey written to artifacts/lab-survey.json.`);
