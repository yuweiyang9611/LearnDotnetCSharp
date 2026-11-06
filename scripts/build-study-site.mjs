import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { access, copyFile, cp, mkdir, readFile, readdir, rm, stat, writeFile } from "node:fs/promises";
import { dirname, extname, join, posix, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { categories, experiments, learningStages, repositoryUrl, resources } from "../site/catalog-data.js";
import { chapterTasks } from "../site/chapter-tasks.js";
import { chapterQuizzes } from "../site/chapter-quiz.js";
import { localOnly } from "../site/lab-availability.js";
import {
  escapeHtml,
  markdownToPlainText,
  parseFrontMatter,
  plainText,
  renderStudyGuide,
} from "./markdown_to_site.mjs";

const repositoryRoot = resolve(fileURLToPath(new URL("..", import.meta.url)));
const sourceDirectory = resolve(repositoryRoot, "site");
const outputDirectory = resolve(repositoryRoot, "artifacts", "study-site");
const pdfSource = resolve(repositoryRoot, "output", "pdf", "LearnDotnetCSharp-Study-Guide.pdf");
const pdfDestination = resolve(outputDirectory, "downloads", "LearnDotnetCSharp-Study-Guide.pdf");
const layerArtifactsSource = resolve(sourceDirectory, "layers", "artifacts.json");
const guideSourceRelative = "docs/advanced-dotnet-csharp-study-guide.md";
const guideSource = resolve(repositoryRoot, guideSourceRelative);
const templatesDirectory = resolve(repositoryRoot, "scripts", "templates");
const siteOrigin = "https://yuweiyang9611.github.io/LearnDotnetCSharp/";

// Source read in place: the folders a reader can learn from are copied into the site at this revision,
// as plain text under assets/source/, so the panel on a page never drifts from the repository.
const sourceBundleRoots = ["src", "native", "python", "docs", "tests"];
const sourceBundleExtensions = new Set([
  ".c", ".cmd", ".cpp", ".cs", ".csproj", ".editorconfig", ".fs", ".fsproj", ".h", ".hpp", ".json", ".md", ".mjs", ".props",
  ".ps1", ".psd1", ".py", ".resx", ".sh", ".sln", ".slnx", ".targets", ".txt", ".vb", ".vbproj", ".xml", ".yaml", ".yml",
]);
const sourceBundleExcludedDirectories = new Set(["bin", "obj", "node_modules", "TestResults", ".git", "__pycache__", ".venv", ".docs-venv"]);
const sourceBundleSizeCap = 256 * 1024;
const sourceBundleRoute = "assets/source/";
const sourcePageFile = "source.html";

// Run in the browser: the Roslyn + .NET WebAssembly runtime staged by `npm run build:runtime` is copied to
// assets/runtime/ (generated, never committed). A lab compiles as its panel files plus these Infrastructure
// files; the entry is written by lab-runner.js. The build refuses to proceed without the runtime.
const runtimeStage = resolve(repositoryRoot, "artifacts", "browser-runtime");
const runtimeRoute = "assets/runtime/";
const labSupportFiles = [
  "src/LearnDotnetCSharp.App/Infrastructure/DemoAssert.cs",
  "src/LearnDotnetCSharp.App/Infrastructure/DemoCatalog.cs",
  "src/LearnDotnetCSharp.App/Infrastructure/DemoContext.cs",
  "src/LearnDotnetCSharp.App/Infrastructure/DemoMetadata.cs",
  "src/LearnDotnetCSharp.App/Infrastructure/DemoResult.cs",
  "src/LearnDotnetCSharp.App/Infrastructure/DemoRunner.cs",
  "src/LearnDotnetCSharp.App/Infrastructure/FaultPlan.cs",
  "src/LearnDotnetCSharp.App/Infrastructure/IDemo.cs",
  "src/LearnDotnetCSharp.App/Infrastructure/WorkspaceLocator.cs",
];
const lessonSteps = [["read", "读"], ["code", "看代码"], ["run", "跑"], ["quiz", "自测"], ["note", "记一笔"]];

const contentDocuments = [
  {
    description: "八阶段推荐顺序、关键观察点与可重复的实验方法。",
    expected: { headingCount: 10, listItemCount: 59, sourceLinkCount: 1 },
    route: "learning-path/",
    source: "docs/learning-path.md",
    type: "学习路线",
  },
  {
    description: "把原 PDF 的 27 章主题映射到当前 30 章教材与 53 个现代实验。",
    expected: { headingCount: 5, listItemCount: 10, tableCount: 4 },
    route: "topic-map/",
    source: "docs/pdf-topic-map.md",
    type: "主题映射",
  },
  {
    description: "解释 Python 进程协议、C ABI、C++ 不透明句柄及其所有权边界。",
    expected: { codeBlockCount: 1, headingCount: 5, tableCount: 1 },
    route: "interop/",
    source: "docs/interop-boundaries.md",
    type: "互操作专题",
  },
  {
    description: "汇集规范、Microsoft 文档以及 Roslyn 与 CoreCLR 一手实现资料。",
    expected: { blockquoteCount: 1, headingCount: 9, listItemCount: 92, sourceLinkCount: 92 },
    route: "references/",
    source: "docs/references.md",
    type: "参考资料",
  },
];

const documentRouteBySource = new Map([
  [guideSourceRelative, "guide/"],
  ...contentDocuments.map(({ route, source }) => [source, route]),
]);

const experimentHeadingAliases = {
  "28.1 ": "project.cancellable-data-pipeline",
  "28.2 ": "project.versioned-local-service",
  "28.3 ": "project.collectible-plugin-host",
  "28.4 ": "project.polyglot-compute",
  "28.5 ": "project.resilient-analytics-workflow",
};

const expectedGuideStructure = {
  blockquoteCount: 3,
  codeBlockCount: 41,
  headingCount: 115,
  headingLevel1: 15,
  headingLevel2: 32,
  headingLevel3: 68,
  listItemCount: 448,
  orderedListCount: 39,
  sourceLinkCount: 79,
  tableCount: 13,
  taskListItemCount: 15,
  unorderedListCount: 43,
};

function countMatches(value, pattern) {
  return [...value.matchAll(pattern)].length;
}

function repositoryRevision() {
  const fromCi = process.env.GITHUB_SHA ?? "";
  if (/^[0-9a-f]{40}$/i.test(fromCi)) return fromCi.toLowerCase();
  try {
    return execFileSync("git", ["-C", repositoryRoot, "rev-parse", "HEAD"], { encoding: "utf8" }).trim().toLowerCase();
  } catch {
    return "main";
  }
}

async function collectSourceBundle() {
  const files = [];
  async function walk(directory, relativeDirectory) {
    const entries = await readdir(directory, { withFileTypes: true });
    for (const entry of entries.sort((left, right) => left.name.localeCompare(right.name, "en"))) {
      const relative = relativeDirectory ? `${relativeDirectory}/${entry.name}` : entry.name;
      if (entry.isDirectory()) {
        // Case-sensitive: an Output folder in the source tree is code, the repository's output/ is not.
        if (sourceBundleExcludedDirectories.has(entry.name)) continue;
        await walk(join(directory, entry.name), relative);
        continue;
      }
      if (!entry.isFile() || !sourceBundleExtensions.has(extname(entry.name).toLowerCase())) continue;
      const info = await stat(join(directory, entry.name));
      if (info.size > sourceBundleSizeCap) continue;
      const text = (await readFile(join(directory, entry.name), "utf8")).replaceAll("\r\n", "\n");
      let lines = countMatches(text, /\n/g);
      if (text.length > 0 && !text.endsWith("\n")) lines += 1;
      files.push({ bytes: Buffer.byteLength(text, "utf8"), lines, path: relative, text });
    }
  }
  for (const root of sourceBundleRoots) {
    try {
      await access(resolve(repositoryRoot, root));
    } catch {
      continue;
    }
    await walk(resolve(repositoryRoot, root), root);
  }
  files.sort((left, right) => (left.path < right.path ? -1 : left.path > right.path ? 1 : 0));
  return { byPath: new Map(files.map((file) => [file.path, file])), files };
}

const sourceBundle = await collectSourceBundle();
const sourceRevision = repositoryRevision();
const sourceBlobBase = `${repositoryUrl}/blob/${sourceRevision}/`;

function bundledDirectoryHasFiles(directory) {
  const prefix = `${directory.replace(/\/+$/, "")}/`;
  return sourceBundle.files.some(({ path }) => path.startsWith(prefix));
}

// A link to a bundled file opens the site's source page (keeping a #L5-L9 range); a folder opens its listing.
function createSourceHrefResolver(currentRoute) {
  const pageHref = relativeFileHref(currentRoute, sourcePageFile);
  return (repositoryPath, fragment = "") => {
    const path = repositoryPath.replace(/\/+$/, "");
    if (sourceBundle.byPath.has(path)) {
      const range = /^#L\d+(?:-L?\d+)?$/.test(fragment) ? fragment : "";
      return `${pageHref}?path=${encodeURIComponent(path)}${range}`;
    }
    if (bundledDirectoryHasFiles(path)) return `${pageHref}?dir=${encodeURIComponent(path)}`;
    return null;
  };
}

function sourceBlobUrl(path) {
  return sourceBlobBase + path.split("/").map(encodeURIComponent).join("/");
}

// The demo's full type name, read from its file: the entry constructs it by name.
function demoTypeName(experiment) {
  const text = sourceBundle.byPath.get(experiment.source)?.text ?? "";
  const namespace = text.match(/^namespace\s+([\w.]+);/m)?.[1];
  const className = text.match(/\bclass\s+(\w+)\s*(?:\([^)]*\))?\s*:\s*[^{]*\bIDemo\b/)?.[1];
  if (!namespace || !className) throw new Error(`Lab '${experiment.id}' has no file-scoped namespace and IDemo class in ${experiment.source}.`);
  return `${namespace}.${className}`;
}

// The lab panel as the build writes it: the kit's .nb-code with its tabs and line count, a <noscript> GitHub link
// in the view, and the files for lab-source.js to load. The script replaces the children with the live panel.
// A lab that runs in the browser carries its type name and support files; one that cannot carries the reason,
// and its 需要本机 line with the local command is written here so it reads without script.
function renderLabPanel(experiment, paths, currentRoute) {
  const experimentId = experiment.id;
  const files = paths.map((path) => {
    const file = sourceBundle.byPath.get(path);
    if (!file) throw new Error(`Lab '${experimentId}' names a file outside the source bundle: ${path}`);
    return file;
  });
  const tabs = files.map((file, index) => `<span class="nb-code-tab" role="tab" aria-selected="${index === 0}" title="${escapeHtml(file.path)}">${escapeHtml(posix.basename(file.path))}</span>`).join("");
  const noScriptLinks = files.map((file) => `<a href="${escapeHtml(sourceBlobUrl(file.path))}">${escapeHtml(file.path)}</a>`).join("、");
  const reason = localOnly[experimentId];
  const command = experimentCommand(experimentId);
  const runAttributes = reason
    ? ` data-local-only="${escapeHtml(reason)}" data-run-command="${escapeHtml(command)}"`
    : ` data-run-type="${escapeHtml(demoTypeName(experiment))}" data-run-support="${escapeHtml(JSON.stringify(labSupportFiles))}"`;
  const localNote = reason
    ? `<div class="nb-code-actions lab-local"><p class="lab-local-note"><strong>需要本机</strong>：${escapeHtml(reason)}</p><div class="assessment-command lab-local-command"><code tabindex="0">${escapeHtml(command)}</code><button class="text-action" type="button" data-copy-lab-command="${escapeHtml(experimentId)}">复制命令</button></div></div>`
    : "";
  return `<div class="nb-code lab-source" id="source-${escapeHtml(experimentId.replaceAll(".", "-"))}" data-source-panel data-lab-id="${escapeHtml(experimentId)}"${runAttributes} data-source-base="${escapeHtml(relativeDirectoryHref(currentRoute, sourceBundleRoute))}" data-source-page="${escapeHtml(relativeFileHref(currentRoute, sourcePageFile))}" data-blob-base="${escapeHtml(sourceBlobBase)}" data-source-files="${escapeHtml(JSON.stringify(files.map(({ path }) => ({ path }))))}" aria-label="${escapeHtml(experimentId)} 的源码"><div class="nb-code-tabs" role="tablist">${tabs}<span class="nb-code-note">${files[0].lines} 行</span></div><pre class="nb-code-view"><code><noscript>看代码需要 JavaScript。源文件在 GitHub：${noScriptLinks}。</noscript></code></pre>${localNote}</div>`;
}

// The loop band at the top of a chapter: five steps in the order the reader takes them, each a link to the
// section that does it. Without script every step is pending; guide/app.js sets the states from the record.
function renderLessonSteps(unit, labs, quiz) {
  if (unit.kind !== "chapter") return "";
  const chapterId = `chapter-${String(unit.number).padStart(2, "0")}`;
  const firstLab = labs[0] ? `#source-${labs[0].id.replaceAll(".", "-")}` : "";
  const anchors = { read: `#${unit.headings[0].id}`, code: firstLab, run: firstLab, quiz: `#${chapterId}-quiz`, note: `#${chapterId}-notes` };
  const counts = { quiz: quiz ? `${quiz.questions.length} 题` : "" };
  const items = lessonSteps.map(([step, label]) => {
    const text = anchors[step] ? `<a href="${escapeHtml(anchors[step])}">${label}</a>` : `<span>${label}</span>`;
    return `<li data-step="${step}" data-state="pending">${text}<span class="nb-steps-count"${counts[step] ? "" : " hidden"}>${escapeHtml(counts[step] ?? "")}</span></li>`;
  }).join("");
  return `<ol class="nb-steps lesson-steps" id="lesson-steps" data-lesson-steps="${chapterId}" aria-label="本章的五步">${items}</ol>`;
}

function chapterQuizRevision(quiz) {
  return createHash("sha256").update(JSON.stringify(quiz.questions)).digest("hex").slice(0, 16);
}

// The chapter's self-test: three questions from the chapter text, written as a form that reads without
// script; guide/app.js checks the answers and records a pass with the revision.
function renderChapterQuiz(quiz, unit) {
  if (!quiz || unit.kind !== "chapter") return "";
  const quizId = `chapter-${String(quiz.chapter).padStart(2, "0")}`;
  const questions = quiz.questions.map((question, index) => {
    const options = question.options.map((option, optionIndex) => `
        <label class="quiz-option"><input type="radio" name="${quizId}-q${index + 1}" value="${optionIndex}" /><span>${escapeHtml(option)}</span></label>`).join("");
    return `
      <li class="quiz-question nb-entry" data-quiz-question data-quiz-answer="${question.answer}">
        <fieldset><legend>${escapeHtml(question.prompt)}</legend><div class="quiz-options">${options}</div></fieldset>
        <p class="quiz-feedback" data-quiz-feedback role="status" hidden></p>
      </li>`;
  }).join("");
  return `
  <section class="chapter-quiz" id="${quizId}-quiz" data-chapter-quiz data-quiz-id="${quizId}" data-quiz-revision="${chapterQuizRevision(quiz)}" aria-labelledby="${quizId}-quiz-title">
    <header class="assessment-heading">
      <h2 id="${quizId}-quiz-title">本章自测</h2>
      <span class="assessment-state" data-quiz-state>未作答</span>
    </header>
    <p class="assessment-objective">${quiz.questions.length} 道题都答对才算通过，答错可以再来；通过记录只保存在当前浏览器。</p>
    <form data-quiz-form>
      <ol class="quiz-list">${questions}
      </ol>
      <div class="assessment-footer"><p data-quiz-status role="status" aria-live="polite">选好每一题的答案后提交。</p><button class="nb-button nb-button--solid" type="submit">提交答案</button></div>
    </form>
  </section>`;
}

// Each lab heading in a chapter is followed by its 源码 paragraph; the panel with those files goes under it.
function injectLabPanels(articleHtml, unit, labs) {
  let html = articleHtml;
  for (const experiment of labs) {
    const anchor = `experiment-${experiment.id.replaceAll(".", "-")}`;
    const headingIndex = html.indexOf(`id="${anchor}"`);
    if (headingIndex === -1) throw new Error(`Chapter '${unit.route}' has no heading for lab '${experiment.id}'.`);
    const nextHeading = html.indexOf("<h", html.indexOf("</h", headingIndex));
    const sectionEnd = nextHeading === -1 ? html.length : nextHeading;
    const paragraph = html.slice(headingIndex, sectionEnd).match(/<p>源码：.*?<\/p>/s);
    if (!paragraph) throw new Error(`Lab '${experiment.id}' has no 源码 paragraph under its heading.`);
    const linked = [...paragraph[0].matchAll(/href="[^"]*source\.html\?path=([^"#]+)"/g)].map((match) => decodeURIComponent(match[1]));
    if (!linked.includes(experiment.source)) {
      throw new Error(`Lab '${experiment.id}' must link its catalog source ${experiment.source} in its 源码 paragraph; found ${linked.join(", ") || "none"}.`);
    }
    const paths = [experiment.source, ...linked.filter((path) => path !== experiment.source)];
    const insertAt = headingIndex + paragraph.index + paragraph[0].length;
    html = `${html.slice(0, insertAt)}\n${renderLabPanel(experiment, paths, unit.route)}${html.slice(insertAt)}`;
  }
  return html;
}

function applyTemplate(template, tokens) {
  let result = template;
  for (const [token, value] of Object.entries(tokens)) {
    result = result.replaceAll(`{{${token}}}`, String(value));
  }
  if (/\{\{[A-Z_]+\}\}/.test(result)) {
    throw new Error("Generated page contains unresolved template tokens.");
  }
  return result;
}

function relativeDirectoryHref(fromRoute, toRoute) {
  const relative = posix.relative(fromRoute, toRoute);
  if (!relative) {
    return "./";
  }
  return `${relative.startsWith(".") ? relative : `./${relative}`}/`;
}

function relativeFileHref(fromRoute, targetFile) {
  const relative = posix.relative(fromRoute, targetFile);
  return relative.startsWith(".") ? relative : `./${relative}`;
}

function safeJsonForHtml(value) {
  return JSON.stringify(value).replaceAll("<", "\\u003c");
}

function normalizeSearch(value) {
  return String(value).normalize("NFKC").toLocaleLowerCase("zh-CN");
}

function chapterTaskRevision(task) {
  const content = JSON.stringify({
    command: task.command,
    criteria: task.criteria,
    evidence: task.evidence,
    objective: task.objective,
    steps: task.steps,
    title: task.title,
  });
  return createHash("sha256").update(content).digest("hex").slice(0, 16);
}

function createPageSearchEntries({ articleMarkdown, baseUrl, headings, kind, pageTitle, subtitle }) {
  const lines = articleMarkdown ? articleMarkdown.split("\n") : [];
  const firstHeadingLine = headings[0]?.sourceLine ? headings[0].sourceLine - 1 : lines.length;
  const introText = markdownToPlainText(lines.slice(0, firstHeadingLine).join("\n"));
  const entries = [{
    kind,
    subtitle,
    text: introText || pageTitle,
    title: pageTitle,
    url: baseUrl,
  }];

  for (let index = 0; index < headings.length; index += 1) {
    const heading = headings[index];
    const start = heading.sourceLine - 1;
    const end = headings[index + 1]?.sourceLine ? headings[index + 1].sourceLine - 1 : lines.length;
    entries.push({
      kind: `${kind}小节`,
      subtitle: `${pageTitle} · ${subtitle}`,
      text: markdownToPlainText(lines.slice(start, end).join("\n")),
      title: heading.text,
      url: `${baseUrl}#${heading.id}`,
    });
  }
  return entries;
}

function renderChapterAssessment(task, unit) {
  if (!task || unit.kind !== "chapter") return "";
  const taskId = `chapter-${String(task.chapter).padStart(2, "0")}`;
  const layerLabLink = task.chapter === 19 || task.chapter === 20
    ? `<p class="assessment-lab-link"><a href="${escapeHtml(relativeDirectoryHref(unit.route, "layers/"))}">打开四层代码实验台</a></p>`
    : "";
  const checklist = (items, kind) => items.map((item, index) => `
    <li class="nb-entry"><label><input type="checkbox" data-assessment-${kind}="${index}" /><span>${escapeHtml(item)}</span></label></li>`).join("");
  return `
  <section class="chapter-assessment" data-chapter-assessment data-task-id="${taskId}" data-task-chapter="${task.chapter}" data-task-revision="${chapterTaskRevision(task)}" aria-labelledby="${taskId}-title">
    <header class="assessment-heading">
      <h2 id="${taskId}-title">本章验收：${escapeHtml(task.title)}</h2>
      <span class="assessment-state" data-assessment-state>尚未验收</span>
    </header>
    <p class="assessment-objective">${escapeHtml(task.objective)}</p>
    <noscript><p class="assessment-no-script">网页自动记录需要 JavaScript；你仍可按下面清单在本地执行，并把证据保存到自己的学习笔记。</p></noscript>
    <div class="assessment-command"><code tabindex="0">${escapeHtml(task.command)}</code><button class="text-action" type="button" data-copy-task-command="${escapeHtml(task.command)}">复制命令</button></div>
    ${layerLabLink}
    <section class="assessment-list"><h3>完成步骤</h3><ol class="assessment-checklist">${checklist(task.steps, "step")}</ol></section>
    <section class="assessment-list"><h3>通过标准</h3><ul class="assessment-checklist criteria-list">${checklist(task.criteria, "criterion")}</ul></section>
    <details class="assessment-evidence-guide"><summary>需要保留的证据</summary><ul>${task.evidence.map((item) => `<li>${escapeHtml(item)}</li>`).join("")}</ul></details>
    <label class="assessment-notes" for="${taskId}-notes"><span>证据记录</span><textarea class="nb-input" id="${taskId}-notes" data-assessment-notes rows="5" maxlength="2400" placeholder="记录关键输出、修改位置与解释；内容只保存在当前浏览器。"></textarea></label>
    <div class="assessment-footer"><p data-assessment-status role="status" aria-live="polite">完成步骤、核对标准并填写证据记录后即可验收。</p><button class="nb-button nb-button--solid" type="button" data-assessment-complete disabled>标记为已验收</button></div>
  </section>`;
}

function experimentCommand(id) {
  return `dotnet run --project src/LearnDotnetCSharp.App -- run ${id}`;
}

// Every category belongs to exactly one route, so a route's labs are the labs of its categories.
function stageOfCategory(category) {
  const stage = learningStages.find(({ categories: keys }) => keys.includes(category));
  if (!stage) throw new Error(`Category '${category}' belongs to no learning stage.`);
  return stage;
}

function stageEyebrow(stage) {
  return `第 ${stage.number} 条路线 · ${stage.label}`;
}

// The homepage is complete without JavaScript; app.js only adds the reader's record and the filters.
function renderStaticHomepage(experimentGuideUrls) {
  const stageMembers = (stage) => experiments.filter(({ category }) => stage.categories.includes(category));
  // Route cards: the head in the route's hue (styles.css maps data-route to a hue), a ring for the record.
  const pathHtml = learningStages.map((stage) => {
    const members = stageMembers(stage);
    return `
          <li class="path-card nb-card" data-stage-row="${escapeHtml(stage.number)}" data-route="${escapeHtml(stage.number)}">
            <div class="nb-card-head">
              <div>
                <p class="nb-card-eyebrow">${escapeHtml(stageEyebrow(stage))}</p>
                <h3><a href="#catalog" data-stage="${escapeHtml(stage.number)}">${escapeHtml(stage.title)}</a></h3>
              </div>
              <div class="nb-ring" data-stage-ring role="img" aria-label="完成 0%"><span>0%</span></div>
            </div>
            <div class="nb-card-body">
              <p class="row-note">${escapeHtml(stage.description)}</p>
              <div class="nb-card-meta"><span class="row-state" data-stage-state>0 / ${members.length} 实验</span><span class="row-next" data-stage-next>下一个：${escapeHtml(members[0].title)}</span></div>
            </div>
          </li>`;
  }).join("");
  // Filter chips: 全部 fills ink when pressed, a category fills with its route's hue.
  const filtersHtml = [
    `<button class="filter-button nb-chip" type="button" data-category="all" data-hue="ink" aria-pressed="true">全部 <span class="nb-chip-count">${experiments.length}</span></button>`,
    ...Object.entries(categories).map(([key, category]) => {
      const count = experiments.filter((experiment) => experiment.category === key).length;
      return `<button class="filter-button nb-chip" type="button" data-category="${escapeHtml(key)}" data-route="${escapeHtml(stageOfCategory(key).number)}" aria-pressed="false">${escapeHtml(category.label)} <span class="nb-chip-count">${count}</span></button>`;
    }),
  ].join("");
  const experimentCard = (experiment) => {
    const category = categories[experiment.category];
    const sourceUrl = createSourceHrefResolver("")(experiment.source);
    if (!sourceUrl) throw new Error(`Lab '${experiment.id}' source ${experiment.source} is not in the source bundle.`);
    return `
            <article class="experiment-card nb-card" id="experiment-${escapeHtml(experiment.id)}" data-experiment-id="${escapeHtml(experiment.id)}">
              <div class="nb-card-body">
                <p class="row-key">${escapeHtml(category.label)} · <code class="experiment-id">${escapeHtml(experiment.id)}</code></p>
                <h3><a href="${escapeHtml(experimentGuideUrls[experiment.id])}">${escapeHtml(experiment.title)}</a></h3>
                <p class="nb-stopped" hidden>停在这里</p>
                <p class="row-note">${escapeHtml(experiment.summary)}</p>
                <div class="nb-card-meta row-state">
                  <label class="complete-toggle"><input type="checkbox" data-complete-id="${escapeHtml(experiment.id)}" aria-label="已完成：${escapeHtml(experiment.title)}" /><span>已完成</span></label>
                  <span class="experiment-actions"><button class="text-action" type="button" data-copy="${escapeHtml(experimentCommand(experiment.id))}">复制命令</button><a class="source-link" href="${escapeHtml(sourceUrl)}">源码</a></span>
                </div>
              </div>
            </article>`;
  };
  // The catalog is grouped by route: a band in the route's hue with its progress bar, then the labs as cards.
  const experimentsHtml = learningStages.map((stage) => {
    const members = stageMembers(stage);
    return `
          <section class="catalog-group" data-catalog-group="${escapeHtml(stage.number)}" data-route="${escapeHtml(stage.number)}" aria-labelledby="catalog-group-${escapeHtml(stage.number)}">
            <div class="route-band">
              <div class="route-band-copy">
                <p class="route-band-eyebrow">${escapeHtml(stageEyebrow(stage))}</p>
                <h3 id="catalog-group-${escapeHtml(stage.number)}">${escapeHtml(stage.title)}</h3>
              </div>
              <div class="route-band-progress"><span data-group-state>0 / ${members.length} 实验</span><div class="nb-bar nb-bar--on-fill" data-group-bar role="progressbar" aria-label="${escapeHtml(stage.label)}实验进度" aria-valuemin="0" aria-valuemax="${members.length}" aria-valuenow="0"><span></span></div></div>
            </div>
            <div class="nb-grid catalog-grid">${members.map(experimentCard).join("")}
            </div>
          </section>`;
  }).join("");
  const resourcesHtml = resources.map((resource) => `
          <li class="resource-card row">
            <span class="row-key">${escapeHtml(resource.type)}</span>
            <h3><a href="${escapeHtml(resource.href)}"${resource.download ? " download" : ""}>${escapeHtml(resource.title)}</a></h3>
            <p class="row-note">${escapeHtml(resource.description)}</p>
          </li>`).join("");
  const firstExperiment = experiments[0];
  return {
    experimentsHtml,
    filtersHtml,
    nextLab: {
      NEXT_LAB_COMMAND: escapeHtml(experimentCommand(firstExperiment.id)),
      NEXT_LAB_HREF: escapeHtml(experimentGuideUrls[firstExperiment.id]),
      NEXT_LAB_SUMMARY: escapeHtml(firstExperiment.summary),
      NEXT_LAB_TITLE: escapeHtml(firstExperiment.title),
    },
    pathHtml,
    resourcesHtml,
  };
}

function validateLayerArtifacts(payload) {
  const sha256 = /^[0-9a-f]{64}$/;
  if (payload?.schemaVersion !== 1 || !sha256.test(payload.sourceSha256) || !sha256.test(payload.peSha256)) {
    throw new Error("The four-layer artifact has an invalid schema or fingerprint.");
  }
  for (const key of ["runtime", "os", "architecture", "captureMode"]) {
    if (!String(payload.environment?.[key] ?? "").trim()) throw new Error(`Layer artifact environment '${key}' is missing.`);
  }
  const stages = payload.stages ?? {};
  const evidence = [
    [stages.highLevel, "foreach"], [stages.highLevel, "checked"], [stages.highLevel, "using var"],
    [stages.lowLevel, "try"], [stages.lowLevel, "finally"],
    [stages.cil, "add.ovf"], [stages.cil, "endfinally"],
    [stages.assembly, "Assembly listing for method"], [stages.assembly, "CORINFO_HELP_OVERFLOW"],
  ];
  for (const [text, token] of evidence) {
    if (!String(text ?? "").includes(token)) throw new Error(`Layer artifact is missing '${token}' evidence.`);
  }
  if (!Array.isArray(payload.focuses) || payload.focuses.length < 3 || !Array.isArray(payload.stackSteps) || payload.stackSteps.length < 4) {
    throw new Error("The four-layer artifact is missing focus mappings or CIL stack steps.");
  }
  const method = payload.method ?? {};
  if (!method.signature || !/^0x[0-9A-F]{8}$/i.test(method.metadataToken ?? "") || method.ilByteCount <= 0 || method.maxStack <= 0) {
    throw new Error("The four-layer artifact is missing MethodDef metadata evidence.");
  }
}

async function findCSharpFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = await Promise.all(
    entries.map(async (entry) => {
      const entryPath = join(directory, entry.name);
      if (entry.isDirectory()) return findCSharpFiles(entryPath);
      return entry.isFile() && entry.name.endsWith(".cs") ? [entryPath] : [];
    }),
  );
  return files.flat();
}

async function findStaticFiles(directory, extensions) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = await Promise.all(
    entries.map(async (entry) => {
      const entryPath = join(directory, entry.name);
      if (entry.isDirectory()) return findStaticFiles(entryPath, extensions);
      return entry.isFile() && extensions.has(extname(entry.name)) ? [entryPath] : [];
    }),
  );
  return files.flat();
}

async function validateMarkdownLinks(sourcePath, links) {
  for (const href of links) {
    if (/^(?:https?:|mailto:|#)/i.test(href)) continue;
    const pathPart = href.split(/[?#]/, 1)[0];
    const targetPath = resolve(dirname(sourcePath), decodeURIComponent(pathPart));
    if (targetPath !== repositoryRoot && !targetPath.startsWith(`${repositoryRoot}${sep}`)) {
      throw new Error(`Markdown link escapes the repository: ${href}`);
    }
    await access(targetPath);
  }
}

function splitGuideUnits(markdown, globalHeadings) {
  const { body } = parseFrontMatter(markdown);
  const lines = body.split("\n");
  const events = [];
  let currentPart = "导读";
  let inFence = false;

  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index];
    if (/^```/.test(line)) {
      inFence = !inFence;
      continue;
    }
    if (inFence) continue;

    const part = line.match(/^#\s+(第.+部分：.+)$/);
    if (part) {
      currentPart = plainText(part[1]);
      events.push({ index, type: "boundary" });
      continue;
    }

    const preface = line.match(/^#\s+(前言)\s*$/);
    if (preface) {
      events.push({ index, kind: "preface", label: "前言", partTitle: "导读", route: "guide/preface/", title: "前言", type: "unit" });
      continue;
    }

    const chapter = line.match(/^##\s+(\d+)\.\s+(.+)$/);
    if (chapter) {
      const number = Number.parseInt(chapter[1], 10);
      events.push({
        index,
        kind: "chapter",
        label: String(number).padStart(2, "0"),
        number,
        partTitle: currentPart,
        route: `guide/chapter-${String(number).padStart(2, "0")}/`,
        title: plainText(`${chapter[1]}. ${chapter[2]}`),
        type: "unit",
      });
      continue;
    }

    const appendix = line.match(/^#\s+(附录\s+([A-D])：.+)$/i);
    if (appendix) {
      const letter = appendix[2].toLocaleLowerCase("en-US");
      events.push({
        index,
        kind: "appendix",
        label: `附录 ${appendix[2].toLocaleUpperCase("en-US")}`,
        partTitle: "附录",
        route: `guide/appendix-${letter}/`,
        title: plainText(appendix[1]),
        type: "unit",
      });
    }
  }

  const units = [];
  for (let eventIndex = 0; eventIndex < events.length; eventIndex += 1) {
    const event = events[eventIndex];
    if (event.type !== "unit") continue;
    const endIndex = events[eventIndex + 1]?.index ?? lines.length;
    const unitLines = lines.slice(event.index, endIndex);
    const headings = globalHeadings.filter(
      ({ sourceLine }) => sourceLine >= event.index + 1 && sourceLine < endIndex + 1,
    );
    if (headings[0]?.text !== event.title) {
      throw new Error(`Guide unit heading mismatch for '${event.title}'.`);
    }
    const articleMarkdown = unitLines.slice(1).join("\n").trim();
    const summaryMarkdown = articleMarkdown
      .replace(/^#{1,6}\s+.+\n+/, "")
      .replace(/\[([^\]]+)]\([^)]+\)/g, "$1");
    const plainArticle = markdownToPlainText(summaryMarkdown);
    units.push({
      ...event,
      articleMarkdown,
      description: plainArticle.slice(0, 150) + (plainArticle.length > 150 ? "…" : ""),
      endLine: endIndex,
      headings,
      markdown: unitLines.join("\n").trim(),
      startLine: event.index + 1,
    });
  }

  const coveredLines = new Set();
  for (const unit of units) {
    for (let index = unit.startLine - 1; index < unit.endLine; index += 1) coveredLines.add(index);
  }
  for (let index = 0; index < lines.length; index += 1) {
    if (coveredLines.has(index) || !lines[index].trim() || /^#\s+第.+部分：/.test(lines[index])) continue;
    throw new Error(`Guide splitting left source content unassigned at body line ${index + 1}: ${lines[index]}`);
  }
  return units;
}

function createHrefResolver(sourceRelative, currentRoute, anchorRoutes) {
  return (href) => {
    const hashIndex = href.indexOf("#");
    const pathPart = hashIndex === -1 ? href : href.slice(0, hashIndex);
    const fragment = hashIndex === -1 ? "" : href.slice(hashIndex);
    const targetSource = posix.normalize(posix.join(posix.dirname(sourceRelative), pathPart));
    const targetRoute = documentRouteBySource.get(targetSource);
    if (!targetRoute) return null;

    if (targetSource === guideSourceRelative && fragment) {
      let id = fragment.slice(1);
      try {
        id = decodeURIComponent(id);
      } catch {
        // Keep the original fragment when it is already malformed.
      }
      const anchoredRoute = anchorRoutes.get(id);
      if (anchoredRoute) {
        return `${relativeDirectoryHref(currentRoute, anchoredRoute)}#${id}`;
      }
    }
    return `${relativeDirectoryHref(currentRoute, targetRoute)}${fragment}`;
  };
}

function paginationLink(currentUnit, targetUnit, direction) {
  if (!targetUnit) {
    return `<span class="pagination-placeholder ${direction}-unit" aria-hidden="true"></span>`;
  }
  const label = direction === "previous" ? "上一单元" : "下一单元";
  return `<a class="${direction}-unit" href="${escapeHtml(relativeDirectoryHref(currentUnit.route, targetUnit.route))}">${label}：${escapeHtml(targetUnit.title)}</a>`;
}

if (!outputDirectory.startsWith(`${repositoryRoot}\\`) && !outputDirectory.startsWith(`${repositoryRoot}/`)) {
  throw new Error(`Refusing to build outside the repository: ${outputDirectory}`);
}

if (experiments.length !== 53) {
  throw new Error(`Expected 53 experiments, found ${experiments.length}.`);
}

const uniqueIds = new Set(experiments.map(({ id }) => id));
if (uniqueIds.size !== experiments.length) {
  throw new Error("The study-site catalog contains duplicate experiment IDs.");
}

for (const experiment of experiments) {
  if (!categories[experiment.category]) {
    throw new Error(`Unknown category '${experiment.category}' for ${experiment.id}.`);
  }
  const sourcePath = resolve(repositoryRoot, experiment.source);
  await access(sourcePath);
  const sourceText = await readFile(sourcePath, "utf8");
  if (!sourceText.includes(`"${experiment.id}"`)) {
    throw new Error(`Catalog ID '${experiment.id}' was not found in ${experiment.source}.`);
  }
}

const demoDirectory = resolve(repositoryRoot, "src", "LearnDotnetCSharp.App", "Demos");
const sourceCatalogIds = new Set();
for (const sourcePath of await findCSharpFiles(demoDirectory)) {
  const sourceText = await readFile(sourcePath, "utf8");
  const match = sourceText.match(/DemoMetadata\s+Metadata\s*\{\s*get;\s*\}\s*=\s*new\(\s*"([^"]+)"/s);
  if (match) sourceCatalogIds.add(match[1]);
}

const missingFromSite = [...sourceCatalogIds].filter((id) => !uniqueIds.has(id));
const missingFromSource = [...uniqueIds].filter((id) => !sourceCatalogIds.has(id));
if (missingFromSite.length || missingFromSource.length) {
  throw new Error(
    `Catalog drift detected. Missing from site: ${missingFromSite.join(", ") || "none"}; missing from source: ${missingFromSource.join(", ") || "none"}.`,
  );
}

const indexHtml = await readFile(resolve(sourceDirectory, "index.html"), "utf8");
if (/(?:href|src)=["']\/(?!\/)/i.test(indexHtml)) {
  throw new Error("Root-relative assets are not compatible with the GitHub Pages project path.");
}
if (!indexHtml.includes('href="./guide/"') || !indexHtml.includes('href="./search/"') || !indexHtml.includes('href="./layers/"')) {
  throw new Error("The homepage must link directly to the online guide, search, and four-layer workbench.");
}

const expectedResourceHrefs = new Map([
  ["交互实验", "./layers/"],
  ["主教材", "./guide/"],
  ["PDF", "./downloads/LearnDotnetCSharp-Study-Guide.pdf"],
  ["路线图", "./learning-path/"],
  ["知识映射", "./topic-map/"],
  ["专题", "./interop/"],
  ["参考资料", "./references/"],
]);
for (const [type, href] of expectedResourceHrefs) {
  const resource = resources.find((candidate) => candidate.type === type);
  if (resource?.href !== href) throw new Error(`Resource '${type}' must use the in-site route '${href}'.`);
}
const pdfResource = resources.find(({ type }) => type === "PDF");
if (!pdfResource?.download) throw new Error("The PDF must remain an explicit download-only resource.");

const layerArtifacts = JSON.parse(await readFile(layerArtifactsSource, "utf8"));
validateLayerArtifacts(layerArtifacts);

const [guideMarkdown, chapterTemplate, guideIndexTemplate, contentTemplate, layerTemplate] = await Promise.all([
  readFile(guideSource, "utf8"),
  readFile(resolve(templatesDirectory, "study-guide.html"), "utf8"),
  readFile(resolve(templatesDirectory, "guide-index.html"), "utf8"),
  readFile(resolve(templatesDirectory, "content-page.html"), "utf8"),
  readFile(resolve(sourceDirectory, "layers", "index.html"), "utf8"),
]);

const renderedGuide = renderStudyGuide(guideMarkdown, {
  experimentHeadingAliases,
  experimentIds: experiments.map(({ id }) => id),
  repositoryUrl,
});
await validateMarkdownLinks(guideSource, renderedGuide.sourceLinks);

const actualGuideStructure = {
  blockquoteCount: renderedGuide.blockquoteCount,
  codeBlockCount: renderedGuide.codeBlockCount,
  headingCount: renderedGuide.headings.length,
  headingLevel1: renderedGuide.headings.filter(({ level }) => level === 1).length,
  headingLevel2: renderedGuide.headings.filter(({ level }) => level === 2).length,
  headingLevel3: renderedGuide.headings.filter(({ level }) => level === 3).length,
  listItemCount: renderedGuide.listItemCount,
  orderedListCount: renderedGuide.orderedListCount,
  sourceLinkCount: renderedGuide.sourceLinks.length,
  tableCount: renderedGuide.tableCount,
  taskListItemCount: renderedGuide.taskListItemCount,
  unorderedListCount: renderedGuide.unorderedListCount,
};
for (const [key, expected] of Object.entries(expectedGuideStructure)) {
  if (actualGuideStructure[key] !== expected) {
    throw new Error(`Online guide structure '${key}' changed: expected ${expected}, found ${actualGuideStructure[key]}.`);
  }
}

const expectedCodeLanguages = { asm: 2, bash: 1, csharp: 3, il: 2, markdown: 1, powershell: 10, text: 22 };
if (JSON.stringify(renderedGuide.codeLanguages) !== JSON.stringify(expectedCodeLanguages)) {
  throw new Error(`Online guide code-language distribution changed: ${JSON.stringify(renderedGuide.codeLanguages)}.`);
}

const units = splitGuideUnits(guideMarkdown, renderedGuide.headings);
const chapters = units.filter(({ kind }) => kind === "chapter");
if (chapterTasks.length !== chapters.length || chapterTasks.some((task, index) => task.chapter !== index + 1)) {
  throw new Error(`Chapter tasks must cover chapters 1-${chapters.length} exactly once.`);
}
for (const task of chapterTasks) {
  const chapter = chapters.find(({ number }) => number === task.chapter);
  if (!chapter || chapter.headings[0]?.id !== task.sourceAnchor) {
    throw new Error(`Chapter ${task.chapter} assessment anchor '${task.sourceAnchor}' does not match the guide.`);
  }
  if (!task.command || task.steps.length < 3 || task.criteria.length < 2 || task.evidence.length < 1) {
    throw new Error(`Chapter ${task.chapter} assessment is incomplete.`);
  }
  if (task.command.includes("\\")) {
    throw new Error(`Chapter ${task.chapter} assessment command must be portable across PowerShell and Bash.`);
  }
}
if (units.length !== 35 || chapters.length !== 30 || units.filter(({ kind }) => kind === "appendix").length !== 4) {
  throw new Error(`Expected 35 reading units (30 chapters), found ${units.length} units (${chapters.length} chapters).`);
}

// The self-test: three questions per chapter, 90 in all, each with one correct option among at least three.
if (chapterQuizzes.length !== chapters.length || chapterQuizzes.some((quiz, index) => quiz.chapter !== index + 1)) {
  throw new Error(`Chapter quizzes must cover chapters 1-${chapters.length} exactly once.`);
}
for (const quiz of chapterQuizzes) {
  if (quiz.questions.length !== 3) throw new Error(`Chapter ${quiz.chapter} must have exactly 3 quiz questions, found ${quiz.questions.length}.`);
  for (const question of quiz.questions) {
    if (!question.prompt?.trim() || !question.explanation?.trim()) throw new Error(`Chapter ${quiz.chapter} has a quiz question without a prompt or explanation.`);
    if (!Array.isArray(question.options) || question.options.length < 3 || new Set(question.options).size !== question.options.length) {
      throw new Error(`Chapter ${quiz.chapter} quiz question '${question.prompt}' needs at least three distinct options.`);
    }
    if (!Number.isInteger(question.answer) || question.answer < 0 || question.answer >= question.options.length) {
      throw new Error(`Chapter ${quiz.chapter} quiz question '${question.prompt}' has an answer outside its options.`);
    }
  }
}
if (chapterQuizzes.reduce((total, { questions }) => total + questions.length, 0) !== 90) throw new Error("Expected 90 quiz questions.");

// Run in the browser: the runtime must be staged, every lab is either runnable or listed with a reason,
// and the files a run compiles alongside the lab are in the bundle.
for (const id of Object.keys(localOnly)) {
  if (!uniqueIds.has(id)) throw new Error(`lab-availability.js names an unknown lab: ${id}`);
  if (!String(localOnly[id]).trim()) throw new Error(`lab-availability.js gives no reason for ${id}.`);
}
for (const path of labSupportFiles) {
  if (!sourceBundle.byPath.has(path)) throw new Error(`Lab support file is not in the source bundle: ${path}`);
}
for (const experiment of experiments) {
  if (!localOnly[experiment.id]) demoTypeName(experiment);
}
const runtimeManifestPath = resolve(runtimeStage, "refs", "manifest.json");
try {
  await access(resolve(runtimeStage, "_framework", "dotnet.js"));
  await access(runtimeManifestPath);
} catch {
  throw new Error(`The browser runtime is not staged in ${runtimeStage}. Run 'npm run build:runtime' first (needs the .NET SDK pinned in tools/BrowserRuntime/global.json with the wasm-tools and wasm-experimental workloads).`);
}
const runtimeManifest = JSON.parse(await readFile(runtimeManifestPath, "utf8"));
if (!Array.isArray(runtimeManifest) || runtimeManifest.length < 20 || !runtimeManifest.includes("System.Runtime.dll")) {
  throw new Error("The browser runtime's reference manifest is incomplete.");
}
for (const name of runtimeManifest) await access(resolve(runtimeStage, "refs", name));

const assignedHeadingIds = units.flatMap(({ headings }) => headings.map(({ id }) => id));
if (assignedHeadingIds.length !== 105 || new Set(assignedHeadingIds).size !== assignedHeadingIds.length) {
  throw new Error(`Guide splitting lost or duplicated headings: assigned ${assignedHeadingIds.length}, unique ${new Set(assignedHeadingIds).size}.`);
}

const partHeadings = renderedGuide.headings.filter(({ level, text }) => level === 1 && /^第.+部分：/.test(text));
if (partHeadings.length !== 10) {
  throw new Error(`Expected 10 guide part headings, found ${partHeadings.length}.`);
}
const partHeadingIdByTitle = new Map(partHeadings.map(({ id, text }) => [text, id]));
const anchorRoutes = new Map();
for (const unit of units) {
  for (const { id } of unit.headings) anchorRoutes.set(id, unit.route);
}
for (const { id } of partHeadings) anchorRoutes.set(id, "guide/");
if (anchorRoutes.size !== renderedGuide.headings.length) {
  throw new Error(`Legacy guide routing must cover all ${renderedGuide.headings.length} original anchors.`);
}

for (const experiment of experiments) {
  const anchor = `experiment-${experiment.id.replaceAll(".", "-")}`;
  if (!anchorRoutes.has(anchor)) throw new Error(`No chapter route contains experiment '${experiment.id}'.`);
}

// Each reading unit sits on one route: the route of its labs, or the route of the unit before it
// when it has none, so the preface opens route 01 and an appendix stays on route 08.
const stagesByUnitRoute = new Map();
for (const experiment of experiments) {
  const unitRoute = anchorRoutes.get(`experiment-${experiment.id.replaceAll(".", "-")}`);
  const stage = stageOfCategory(experiment.category);
  const current = stagesByUnitRoute.get(unitRoute);
  if (!current || stage.number > current.number) stagesByUnitRoute.set(unitRoute, stage);
}
const stageByUnitRoute = new Map();
let carriedStage = learningStages[0];
for (const unit of units) {
  carriedStage = stagesByUnitRoute.get(unit.route) ?? carriedStage;
  stageByUnitRoute.set(unit.route, carriedStage);
}

function renderRouteBand(stage) {
  const total = experiments.filter(({ category }) => stage.categories.includes(category)).length;
  return `<div class="route-band route-band--page" data-route="${escapeHtml(stage.number)}" data-route-band="${escapeHtml(stage.number)}">
            <p class="route-band-eyebrow">${escapeHtml(stageEyebrow(stage))}</p>
            <div class="route-band-progress"><span data-route-state>0 / ${total} 实验</span><div class="nb-bar nb-bar--on-fill" data-route-bar role="progressbar" aria-label="${escapeHtml(stage.label)}实验进度" aria-valuemin="0" aria-valuemax="${total}" aria-valuenow="0"><span></span></div></div>
          </div>`;
}

const bookTitle = renderedGuide.metadata.title ?? ".NET 10 与 C# 14 高级特性学习指导";
const bookSubtitle = renderedGuide.metadata.subtitle ?? "以 53 个可运行实验为主线";
const bookDate = renderedGuide.metadata.date ?? "持续更新";
const renderedUnitPages = [];

for (let index = 0; index < units.length; index += 1) {
  const unit = units[index];
  const chapterTask = unit.kind === "chapter"
    ? chapterTasks.find(({ chapter }) => chapter === unit.number)
    : null;
  const rendered = renderStudyGuide(unit.articleMarkdown, {
    experimentHeadingAliases,
    experimentIds: experiments.map(({ id }) => id),
    headingIds: unit.headings.slice(1).map(({ id }) => id),
    headingOffset: unit.kind === "chapter" ? -1 : 0,
    repositoryUrl,
    resolveHref: createHrefResolver(guideSourceRelative, unit.route, anchorRoutes),
    sourceHref: createSourceHrefResolver(unit.route),
  });
  const unitLabs = experiments.filter(({ id }) => anchorRoutes.get(`experiment-${id.replaceAll(".", "-")}`) === unit.route);
  rendered.articleHtml = injectLabPanels(rendered.articleHtml, unit, unitLabs);
  const chapterQuiz = unit.kind === "chapter" ? chapterQuizzes.find(({ chapter }) => chapter === unit.number) : null;
  const pageHeading = unit.headings[0];
  const tocHtml = [
    `<a class="toc-link toc-level-1" href="#${escapeHtml(pageHeading.id)}" data-toc-id="${escapeHtml(pageHeading.id)}">${escapeHtml(unit.title)}</a>`,
    rendered.tocHtml,
  ].filter(Boolean).join("\n");
  const positionLabel = unit.kind === "chapter"
    ? `第 ${String(unit.number).padStart(2, "0")} 章 · ${index + 1} / ${units.length}`
    : `${unit.label} · ${index + 1} / ${units.length}`;
  const html = applyTemplate(chapterTemplate, {
    ARTICLE_HTML: rendered.articleHtml,
    ASSESSMENT_HTML: renderChapterAssessment(chapterTask, unit),
    BOOK_TITLE: escapeHtml(bookTitle),
    CANONICAL_URL: `${siteOrigin}${unit.route}`,
    DESCRIPTION: escapeHtml(unit.description),
    GUIDE_APP_HREF: relativeFileHref(unit.route, "guide/app.js"),
    GUIDE_CSS_HREF: relativeFileHref(unit.route, "guide/styles.css"),
    GUIDE_HREF: relativeDirectoryHref(unit.route, "guide/"),
    HEADING_COUNT: rendered.headings.length + 1,
    HOME_HREF: relativeDirectoryHref(unit.route, ""),
    LAYERS_HREF: relativeDirectoryHref(unit.route, "layers/"),
    LEARNING_PATH_HREF: relativeDirectoryHref(unit.route, "learning-path/"),
    LESSON_STEPS_HTML: renderLessonSteps(unit, unitLabs, chapterQuiz),
    QUIZ_HTML: renderChapterQuiz(chapterQuiz, unit),
    NEXT_LINK_HTML: paginationLink(unit, units[index + 1], "next"),
    PAGE_HEADING_ID: escapeHtml(pageHeading.id),
    PAGE_TITLE: escapeHtml(unit.title),
    PART_TITLE: escapeHtml(unit.partTitle),
    PDF_HREF: relativeFileHref(unit.route, "downloads/LearnDotnetCSharp-Study-Guide.pdf"),
    POSITION_LABEL: escapeHtml(positionLabel),
    PREVIOUS_LINK_HTML: paginationLink(unit, units[index - 1], "previous"),
    READING_ROUTE: escapeHtml(unit.route),
    ROUTE_BAND_HTML: renderRouteBand(stageByUnitRoute.get(unit.route)),
    SEARCH_HREF: relativeDirectoryHref(unit.route, "search/"),
    SHARED_CSS_HREF: relativeFileHref(unit.route, "styles.css"),
    TOC_HTML: tocHtml,
  });
  if (countMatches(html, /<h1(?:\s|>)/g) !== 1) throw new Error(`Guide unit '${unit.route}' must contain exactly one H1.`);
  if (/(?:href|src)=["']\/(?!\/)/i.test(html)) throw new Error(`Guide unit '${unit.route}' contains a root-relative URL.`);
  if (/<(?:iframe|embed|object)[^>]+\.pdf/i.test(html)) throw new Error(`Guide unit '${unit.route}' embeds a PDF.`);
  if (unit.kind === "chapter") {
    // One band of five steps, one quiz of three questions, and a panel per lab that either runs or says 需要本机.
    if (countMatches(html, /<ol class="nb-steps lesson-steps"/g) !== 1 || countMatches(html, /<li data-step="/g) !== 5) throw new Error(`Chapter '${unit.route}' must carry one five-step band.`);
    if (countMatches(html, /data-quiz-question/g) !== 3) throw new Error(`Chapter '${unit.route}' must carry three quiz questions.`);
    const runnable = countMatches(html, /data-run-type="/g);
    const local = countMatches(html, /data-local-only="/g);
    if (runnable + local !== unitLabs.length || countMatches(html, /需要本机<\/strong>/g) !== local) throw new Error(`Chapter '${unit.route}' panels do not match its ${unitLabs.length} labs.`);
  }
  renderedUnitPages.push({ html, rendered, unit });
}

const splitStructure = renderedUnitPages.reduce(
  (totals, { rendered }) => {
    totals.blockquoteCount += rendered.blockquoteCount;
    totals.codeBlockCount += rendered.codeBlockCount;
    totals.headingCount += rendered.headings.length;
    totals.listItemCount += rendered.listItemCount;
    totals.orderedListCount += rendered.orderedListCount;
    totals.sourceLinkCount += rendered.sourceLinks.length;
    totals.tableCount += rendered.tableCount;
    totals.taskListItemCount += rendered.taskListItemCount;
    totals.unorderedListCount += rendered.unorderedListCount;
    return totals;
  },
  {
    blockquoteCount: 0,
    codeBlockCount: 0,
    headingCount: 0,
    listItemCount: 0,
    orderedListCount: 0,
    sourceLinkCount: 0,
    tableCount: 0,
    taskListItemCount: 0,
    unorderedListCount: 0,
  },
);
const expectedSplitStructure = {
  blockquoteCount: expectedGuideStructure.blockquoteCount,
  codeBlockCount: expectedGuideStructure.codeBlockCount,
  headingCount: 70,
  listItemCount: expectedGuideStructure.listItemCount,
  orderedListCount: expectedGuideStructure.orderedListCount,
  sourceLinkCount: expectedGuideStructure.sourceLinkCount,
  tableCount: expectedGuideStructure.tableCount,
  taskListItemCount: expectedGuideStructure.taskListItemCount,
  unorderedListCount: expectedGuideStructure.unorderedListCount,
};
if (JSON.stringify(splitStructure) !== JSON.stringify(expectedSplitStructure)) {
  throw new Error(`Split guide pages lost source structure: ${JSON.stringify(splitStructure)}.`);
}

const routeMap = Object.fromEntries(
  [...anchorRoutes.entries()].map(([id, route]) => [id, `${relativeDirectoryHref("guide/", route)}#${id}`]),
);
const groupedUnits = new Map();
for (const unit of units) {
  const group = groupedUnits.get(unit.partTitle) ?? [];
  group.push(unit);
  groupedUnits.set(unit.partTitle, group);
}
// The directory: a band per part in the route's hue of its first unit, then one card per reading unit.
const unitDirectoryHtml = [...groupedUnits.entries()].map(([partTitle, partUnits]) => {
  const groupHeadingId = partHeadingIdByTitle.get(partTitle) ?? `part-${partUnits[0].route.replaceAll("/", "-")}`;
  const stage = stageByUnitRoute.get(partUnits[0].route);
  const chapterCount = partUnits.filter(({ kind }) => kind === "chapter").length;
  const bandState = chapterCount ? `<span data-part-state>0 / ${chapterCount} 章已验收</span>` : `<span>${partUnits.length} 个阅读单元</span>`;
  return `
  <section class="guide-part-group" data-route="${escapeHtml(stage.number)}" aria-labelledby="${escapeHtml(groupHeadingId)}">
    <div class="route-band guide-part-heading">
      <div class="route-band-copy"><p class="route-band-eyebrow">${escapeHtml(stageEyebrow(stage))}</p><h3 id="${escapeHtml(groupHeadingId)}">${escapeHtml(partTitle)}</h3></div>
      <div class="route-band-progress">${bandState}${chapterCount ? `<div class="nb-bar nb-bar--on-fill" data-part-bar role="progressbar" aria-label="${escapeHtml(partTitle)}验收进度" aria-valuemin="0" aria-valuemax="${chapterCount}" aria-valuenow="0"><span></span></div>` : ""}</div>
    </div>
    <ol class="guide-unit-grid nb-grid">
      ${partUnits.map((unit) => {
        const chapterTask = unit.kind === "chapter" ? chapterTasks.find(({ chapter }) => chapter === unit.number) : null;
        const taskId = chapterTask ? `chapter-${String(unit.number).padStart(2, "0")}` : "";
        const taskRevision = chapterTask ? chapterTaskRevision(chapterTask) : "";
        const taskStatus = taskId ? `<span class="guide-task-status" data-guide-task-status>待验收</span>` : "";
        // A chapter is described by its verification objective, other units by their section titles;
        // the raw opening text carries table debris and loose spacing.
        const note = chapterTask
          ? chapterTask.objective
          : unit.headings.slice(1, 4).map(({ text }) => text.replace(/^[\d.]+\s*/, "")).join("、");
        const title = unit.title.replace(/^\d+\.\s*/, "").replace(/^附录\s+[A-D]：/, "");
        return `<li class="guide-unit-card nb-card"${taskId ? ` data-chapter-task-id="${taskId}" data-chapter-task-revision="${taskRevision}"` : ""} data-unit-route="${escapeHtml(unit.route)}"><div class="nb-card-body"><span class="guide-unit-number">${escapeHtml(unit.label)}</span><h4><a href="${escapeHtml(relativeDirectoryHref("guide/", unit.route))}">${escapeHtml(title)}</a></h4><p class="nb-stopped" hidden>停在这里</p>${note ? `<p class="guide-unit-description">${escapeHtml(note)}</p>` : ""}<div class="nb-card-meta"><span>${escapeHtml(unit.kind === "chapter" ? `第 ${unit.label} 章` : unit.label)}</span>${taskStatus}</div></div></li>`;
      }).join("\n")}
    </ol>
  </section>`;
}).join("\n");

const guideIndexHtml = applyTemplate(guideIndexTemplate, {
  CHAPTER_COUNT: chapters.length,
  DATE: escapeHtml(bookDate),
  DESCRIPTION: escapeHtml(`${bookSubtitle}。30 个章节均有独立 URL，并支持全站正文搜索。`),
  FIRST_UNIT_HREF: relativeDirectoryHref("guide/", units[0].route),
  ROUTE_MAP_JSON: safeJsonForHtml(routeMap),
  SUBTITLE: escapeHtml(bookSubtitle),
  TITLE: escapeHtml(bookTitle),
  UNIT_COUNT: units.length,
  UNIT_DIRECTORY_HTML: unitDirectoryHtml,
});
if (countMatches(guideIndexHtml, /<h1(?:\s|>)/g) !== 1) throw new Error("Guide directory must contain exactly one H1.");

const renderedContentPages = [];
for (const document of contentDocuments) {
  const sourcePath = resolve(repositoryRoot, document.source);
  const markdown = await readFile(sourcePath, "utf8");
  const fullRender = renderStudyGuide(markdown, {
    repositoryUrl,
    resolveHref: createHrefResolver(document.source, document.route, anchorRoutes),
    sourceHref: createSourceHrefResolver(document.route),
  });
  await validateMarkdownLinks(sourcePath, fullRender.sourceLinks);
  const actual = {
    blockquoteCount: fullRender.blockquoteCount,
    codeBlockCount: fullRender.codeBlockCount,
    headingCount: fullRender.headings.length,
    listItemCount: fullRender.listItemCount,
    sourceLinkCount: fullRender.sourceLinks.length,
    tableCount: fullRender.tableCount,
  };
  for (const [key, expected] of Object.entries(document.expected)) {
    if (actual[key] !== expected) throw new Error(`${document.source} '${key}' changed: expected ${expected}, found ${actual[key]}.`);
  }
  const { body } = parseFrontMatter(markdown);
  const lines = body.split("\n");
  const titleMatch = lines[0]?.match(/^#\s+(.+)$/);
  if (!titleMatch || fullRender.headings[0]?.level !== 1) throw new Error(`${document.source} must start with one H1.`);
  const pageTitle = plainText(titleMatch[1]);
  const articleMarkdown = lines.slice(1).join("\n").trim();
  const rendered = renderStudyGuide(articleMarkdown, {
    headingIds: fullRender.headings.slice(1).map(({ id }) => id),
    headingOffset: 0,
    repositoryUrl,
    resolveHref: createHrefResolver(document.source, document.route, anchorRoutes),
    sourceHref: createSourceHrefResolver(document.route),
  });
  const pageHeading = fullRender.headings[0];
  const tocHtml = [
    `<a class="toc-link toc-level-1" href="#${escapeHtml(pageHeading.id)}" data-toc-id="${escapeHtml(pageHeading.id)}">${escapeHtml(pageTitle)}</a>`,
    rendered.tocHtml,
  ].filter(Boolean).join("\n");
  const html = applyTemplate(contentTemplate, {
    ARTICLE_HTML: rendered.articleHtml,
    CANONICAL_URL: `${siteOrigin}${document.route}`,
    DESCRIPTION: escapeHtml(document.description),
    DOCUMENT_TYPE: escapeHtml(document.type),
    HEADING_COUNT: rendered.headings.length + 1,
    PAGE_HEADING_ID: escapeHtml(pageHeading.id),
    PAGE_TITLE: escapeHtml(pageTitle),
    READING_ROUTE: escapeHtml(document.route),
    TOC_HTML: tocHtml,
  });
  if (countMatches(html, /<h1(?:\s|>)/g) !== 1) throw new Error(`${document.route} must contain exactly one H1.`);
  renderedContentPages.push({ articleMarkdown, document, fullRender, html, markdown, pageTitle, rendered });
}

const experimentGuideUrls = {};
for (const experiment of experiments) {
  const anchor = `experiment-${experiment.id.replaceAll(".", "-")}`;
  experimentGuideUrls[experiment.id] = `${relativeDirectoryHref("", anchorRoutes.get(anchor))}#${anchor}`;
}

const staticHomepage = renderStaticHomepage(experimentGuideUrls);
const homepageHtml = applyTemplate(indexHtml, {
  ...staticHomepage.nextLab,
  STATIC_EXPERIMENTS_HTML: staticHomepage.experimentsHtml,
  STATIC_FILTERS_HTML: staticHomepage.filtersHtml,
  STATIC_LEARNING_PATH_HTML: staticHomepage.pathHtml,
  STATIC_RESOURCES_HTML: staticHomepage.resourcesHtml,
});
if (countMatches(homepageHtml, /class="experiment-card[ "]/g) !== experiments.length ||
    countMatches(homepageHtml, /class="path-card[ "]/g) !== learningStages.length ||
    countMatches(homepageHtml, /class="resource-card[ "]/g) !== resources.length) {
  throw new Error("The progressively rendered homepage is missing catalog content.");
}

const initialFocus = layerArtifacts.focuses[0];
const initialStackStep = layerArtifacts.stackSteps[0];
const layerHtml = applyTemplate(layerTemplate, {
  ASSEMBLY_CAPTION: escapeHtml(`${layerArtifacts.environment.architecture} · ${layerArtifacts.environment.captureMode}`),
  LAYER_ASSEMBLY_HTML: escapeHtml(layerArtifacts.stages.assembly),
  LAYER_CIL_HTML: escapeHtml(layerArtifacts.stages.cil),
  LAYER_FOCUS_CONTROLS_HTML: layerArtifacts.focuses.map((focus, index) => (
    `<button class="filter-button nb-chip" type="button" data-focus="${escapeHtml(focus.id)}" aria-pressed="${index === 0}">${escapeHtml(focus.label)}</button>`
  )).join(""),
  LAYER_FOCUS_SUMMARY: escapeHtml(initialFocus.explanation),
  LAYER_HASH: escapeHtml(`源码 ${layerArtifacts.sourceSha256.slice(0, 12)} · PE ${layerArtifacts.peSha256.slice(0, 12)}`),
  LAYER_HIGH_LEVEL_HTML: escapeHtml(layerArtifacts.stages.highLevel),
  LAYER_LOW_LEVEL_HTML: escapeHtml(layerArtifacts.stages.lowLevel),
  LAYER_PLATFORM: escapeHtml(`${layerArtifacts.environment.os} · ${layerArtifacts.environment.architecture}`),
  LAYER_RUNTIME: escapeHtml(layerArtifacts.environment.runtime),
  METHOD_EH_COUNT: layerArtifacts.method.exceptionRegionCount,
  METHOD_IL_BYTES: layerArtifacts.method.ilByteCount,
  METHOD_MAX_STACK: layerArtifacts.method.maxStack,
  METHOD_RVA: escapeHtml(layerArtifacts.method.relativeVirtualAddress),
  METHOD_SIGNATURE: escapeHtml(layerArtifacts.method.signature),
  METHOD_TOKEN: escapeHtml(layerArtifacts.method.metadataToken),
  STACK_AFTER: escapeHtml(initialStackStep.after),
  STACK_BEFORE: escapeHtml(initialStackStep.before),
  STACK_EXPLANATION: escapeHtml(initialStackStep.explanation),
  STACK_INSTRUCTION: escapeHtml(initialStackStep.instruction),
  STACK_STEP_COUNT: layerArtifacts.stackSteps.length,
});

const guideSearchEntries = renderedUnitPages.flatMap(({ rendered, unit }) => {
  const kind = unit.kind === "chapter" ? "教材章节" : unit.kind === "appendix" ? "教材附录" : "教材导读";
  return createPageSearchEntries({
    articleMarkdown: unit.articleMarkdown,
    baseUrl: relativeDirectoryHref("search/", unit.route),
    headings: rendered.headings,
    kind,
    pageTitle: unit.title,
    subtitle: unit.partTitle,
  });
});

const resourceSearchEntries = renderedContentPages.flatMap(({ articleMarkdown, document, pageTitle, rendered }) => (
  createPageSearchEntries({
    articleMarkdown,
    baseUrl: relativeDirectoryHref("search/", document.route),
    headings: rendered.headings,
    kind: document.type,
    pageTitle,
    subtitle: "配套资料",
  })
));

const searchEntries = [
  ...guideSearchEntries,
  ...resourceSearchEntries,
  {
    kind: "交互实验",
    subtitle: `${layerArtifacts.method.signature} · ${layerArtifacts.environment.architecture} · ${layerArtifacts.environment.captureMode}`,
    text: [
      "高级 C# 低层 C# lowering CIL IL 元数据 MethodDef RVA MaxStack JIT 汇编 assembly 求值栈",
      layerArtifacts.method.metadataToken,
      ...layerArtifacts.focuses.map(({ explanation, label }) => `${label} ${explanation}`),
    ].join(" "),
    title: "四层代码实验台：从高级 C# 到 JIT 汇编",
    url: "../layers/",
  },
  ...chapterTasks.map((task) => ({
    kind: "章节验收",
    subtitle: `第 ${task.chapter} 章 · ${task.objective}`,
    text: [...task.steps, ...task.criteria, ...task.evidence, task.command].join(" "),
    title: task.title,
    url: `${relativeDirectoryHref("search/", `guide/chapter-${String(task.chapter).padStart(2, "0")}/`)}#chapter-${String(task.chapter).padStart(2, "0")}-title`,
  })),
  ...experiments.map((experiment) => ({
    id: experiment.id,
    kind: "实验",
    subtitle: `${experiment.id} · ${categories[experiment.category].label}`,
    text: `${experiment.id} ${experiment.title} ${experiment.summary} ${categories[experiment.category].label} dotnet run --project src/LearnDotnetCSharp.App -- run ${experiment.id}`,
    title: experiment.title,
    url: experimentGuideUrls[experiment.id].replace(/^\.\//, "../"),
  })),
];

function assertSearch(query, predicate) {
  const tokens = normalizeSearch(query).split(/\s+/).filter(Boolean);
  const matches = searchEntries.filter((entry) => {
    const text = normalizeSearch(`${entry.title} ${entry.subtitle} ${entry.text}`);
    return tokens.every((token) => text.includes(token));
  });
  if (!matches.some(predicate)) throw new Error(`Search index query '${query}' did not find the expected page.`);
}
assertSearch("DOTNET_JitDisasm", ({ title, url }) => (
  url.includes("chapter-20/#20-5-") && title.includes("DOTNET_JitDisasm")
));
assertSearch("lowering 元数据", ({ url }) => url.includes("chapter-19"));
assertSearch("UnmanagedCallersOnly", ({ url }) => url.includes("chapter-26") || url.includes("interop"));
assertSearch("compiler.roslyn-il", ({ id }) => id === "compiler.roslyn-il");
assertSearch("CIL MethodDef JIT 汇编", ({ url }) => url === "../layers/");

const manifest = {
  anchors: Object.fromEntries([...anchorRoutes.entries()].map(([id, route]) => [id, `${route}#${id}`])),
  experiments: Object.fromEntries(experiments.map(({ id }) => [id, experimentGuideUrls[id]])),
  layers: {
    architecture: layerArtifacts.environment.architecture,
    captureMode: layerArtifacts.environment.captureMode,
    peSha256: layerArtifacts.peSha256,
    sourceSha256: layerArtifacts.sourceSha256,
    url: "layers/",
  },
  units: units.map(({ headings, kind, label, partTitle, route, title }) => ({
    anchors: headings.map(({ id }) => id), kind, label, partTitle, route, title,
  })),
  version: 1,
};

const imageInfo = await stat(resolve(sourceDirectory, "og.png"));
if (imageInfo.size < 10_000) throw new Error("The social preview image is missing or unexpectedly small.");

await rm(outputDirectory, { force: true, recursive: true });
await mkdir(dirname(pdfDestination), { recursive: true });
await cp(sourceDirectory, outputDirectory, { recursive: true });
await copyFile(pdfSource, pdfDestination);
await writeFile(resolve(outputDirectory, "index.html"), homepageHtml, "utf8");
await writeFile(resolve(outputDirectory, "layers", "index.html"), layerHtml, "utf8");
await writeFile(resolve(outputDirectory, "guide", "index.html"), guideIndexHtml, "utf8");

// Source read in place: the bundle as plain text plus its index.
const sourceBundleDirectory = resolve(outputDirectory, ...sourceBundleRoute.split("/").filter(Boolean));
for (const file of sourceBundle.files) {
  const destination = resolve(sourceBundleDirectory, `${file.path}.txt`);
  await mkdir(dirname(destination), { recursive: true });
  await writeFile(destination, file.text, "utf8");
}
await writeFile(
  resolve(sourceBundleDirectory, "index.json"),
  `${JSON.stringify({
    blobBase: sourceBlobBase,
    files: sourceBundle.files.map(({ bytes, lines, path }) => ({ bytes, lines, path })),
    repository: repositoryUrl,
    revision: sourceRevision,
    version: 1,
  })}\n`,
  "utf8",
);
for (const experiment of experiments) {
  if (!sourceBundle.byPath.has(experiment.source)) throw new Error(`Lab '${experiment.id}' source is not bundled: ${experiment.source}`);
}
const sourceBundleBytes = sourceBundle.files.reduce((total, { bytes }) => total + bytes, 0);
if (sourceBundle.files.length < 80 || sourceBundleBytes > 8 * 1024 * 1024) {
  throw new Error(`The source bundle should carry the learning folders within a few MB: ${sourceBundle.files.length} files, ${sourceBundleBytes} bytes.`);
}

for (const { html, unit } of renderedUnitPages) {
  const destination = resolve(outputDirectory, ...unit.route.split("/").filter(Boolean), "index.html");
  await mkdir(dirname(destination), { recursive: true });
  await writeFile(destination, html, "utf8");
}
for (const { document, html } of renderedContentPages) {
  const destination = resolve(outputDirectory, ...document.route.split("/").filter(Boolean), "index.html");
  await mkdir(dirname(destination), { recursive: true });
  await writeFile(destination, html, "utf8");
}

const anchorPageCache = new Map();
for (const [id, route] of anchorRoutes) {
  let html = anchorPageCache.get(route);
  if (!html) {
    const destination = resolve(outputDirectory, ...route.split("/").filter(Boolean), "index.html");
    html = await readFile(destination, "utf8");
    anchorPageCache.set(route, html);
  }
  if (!html.includes(`id="${escapeHtml(id)}"`)) {
    throw new Error(`Legacy guide anchor '${id}' does not exist at ${route}.`);
  }
}

await writeFile(
  resolve(outputDirectory, "guide-routes.js"),
  `export const experimentGuideUrls = Object.freeze(${JSON.stringify(experimentGuideUrls, null, 2)});\n`,
  "utf8",
);
const progressTasks = Object.fromEntries(chapterTasks.map((task) => [
  `chapter-${String(task.chapter).padStart(2, "0")}`, { revision: chapterTaskRevision(task), steps: task.steps.length, criteria: task.criteria.length },
]));
const progressQuizzes = Object.fromEntries(chapterQuizzes.map((quiz) => [
  `chapter-${String(quiz.chapter).padStart(2, "0")}`, { revision: chapterQuizRevision(quiz), questions: quiz.questions.length },
]));
// The shape of every chapter for the loop band and the index's continue reading: its labs and its quiz.
const progressChapters = chapters.map((unit) => {
  const id = `chapter-${String(unit.number).padStart(2, "0")}`;
  return {
    id,
    number: unit.number,
    route: unit.route,
    title: unit.title,
    heading: unit.headings[0].id,
    labs: experiments.filter(({ id: labId }) => anchorRoutes.get(`experiment-${labId.replaceAll(".", "-")}`) === unit.route).map(({ id: labId }) => labId),
    quiz: progressQuizzes[id] ?? null,
  };
});
await writeFile(resolve(outputDirectory, "progress-catalog.js"),
  `export const tasks = ${JSON.stringify(progressTasks)};\nexport const guideRoutes = ${JSON.stringify(units.map(({ route }) => route))};\nexport const quizzes = ${JSON.stringify(progressQuizzes)};\nexport const chapters = ${JSON.stringify(progressChapters)};\nexport const localOnly = ${JSON.stringify(localOnly)};\n`, "utf8");

// The browser runtime, staged by build:runtime, served next to the source bundle.
await cp(runtimeStage, resolve(outputDirectory, ...runtimeRoute.split("/").filter(Boolean)), { recursive: true });
await writeFile(resolve(outputDirectory, "guide-manifest.json"), `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
await writeFile(resolve(outputDirectory, "search-index.json"), `${JSON.stringify({ entries: searchEntries, version: 1 }, null, 2)}\n`, "utf8");
await writeFile(resolve(outputDirectory, ".nojekyll"), "", "utf8");
await writeFile(
  resolve(outputDirectory, "404.html"),
  `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8" /><meta name="viewport" content="width=device-width, initial-scale=1" /><meta http-equiv="refresh" content="0; url=/LearnDotnetCSharp/" /><title>返回 LearnDotnetCSharp</title></head><body><h1>页面不存在</h1><p>正在返回 <a href="/LearnDotnetCSharp/">LearnDotnetCSharp 学习站</a>。</p></body></html>\n`,
  "utf8",
);

for (const entry of searchEntries) {
  const pathPart = entry.url.split(/[?#]/, 1)[0];
  let targetPath = resolve(outputDirectory, "search", decodeURIComponent(pathPart));
  const info = await stat(targetPath);
  if (info.isDirectory()) targetPath = resolve(targetPath, "index.html");
  await access(targetPath);
}

const runtimeDirectory = resolve(outputDirectory, ...runtimeRoute.split("/").filter(Boolean));
const staticFiles = (await findStaticFiles(outputDirectory, new Set([".css", ".html", ".js"])))
  // The published .NET loader is generated vendor code with its own dynamic imports; it is not the site's to lint.
  .filter((staticFile) => !staticFile.startsWith(`${runtimeDirectory}${sep}`));
for (const staticFile of staticFiles) {
  const content = await readFile(staticFile, "utf8");
  if (extname(staticFile) === ".html") {
    if (/\{\{[A-Z_]+\}\}/.test(content)) throw new Error(`Unresolved template token in ${staticFile}.`);
    if (countMatches(content, /<h1(?:\s|>)/g) !== 1) throw new Error(`HTML page must contain exactly one H1: ${staticFile}.`);
    if (/<(?:iframe|embed|object)[^>]+\.pdf/i.test(content) || /rel=["']preload["'][^>]+\.pdf/i.test(content)) {
      throw new Error(`HTML page embeds or preloads the PDF: ${staticFile}.`);
    }
    // Source read in place: nothing outside <noscript> may send the reader to GitHub for a file the site carries.
    const scripted = content.replace(/<noscript>[\s\S]*?<\/noscript>/g, "");
    for (const match of scripted.matchAll(/href=["']https:\/\/github\.com\/[^"']+\/(?:blob|tree)\/[^/"']+\/([^"'#?]+)/g)) {
      const path = decodeURIComponent(match[1]).replace(/\/+$/, "");
      if (sourceBundle.byPath.has(path) || bundledDirectoryHasFiles(path)) {
        throw new Error(`Page sends the reader to GitHub for a bundled file: ${staticFile} -> ${path}`);
      }
    }
    for (const match of content.matchAll(/(?:href|src)=["']([^"']+)["']/gi)) {
      const url = match[1];
      if (/^(?:https?:|mailto:|data:|#)/i.test(url)) continue;
      if (url.startsWith("/")) {
        if (url !== "/LearnDotnetCSharp/") throw new Error(`Root-relative URL '${url}' breaks the project path.`);
        continue;
      }
      const relativePath = decodeURIComponent(url.split(/[?#]/, 1)[0]);
      if (!relativePath) continue;
      let targetPath = resolve(dirname(staticFile), relativePath);
      const targetInfo = await stat(targetPath);
      if (targetInfo.isDirectory()) {
        targetPath = resolve(targetPath, "index.html");
        await access(targetPath);
      }
      if (!targetPath.startsWith(`${outputDirectory}${sep}`) && targetPath !== outputDirectory) {
        throw new Error(`Static-site link escapes the output directory: ${url}`);
      }
    }
  } else if (extname(staticFile) === ".css") {
    if (/url\(\s*["']?\/(?!\/)/i.test(content)) throw new Error(`Root-relative CSS asset found in ${staticFile}.`);
  } else {
    if (/(?:fetch|import)\(\s*["']\/(?!\/)/i.test(content)) throw new Error(`Root-relative JavaScript request found in ${staticFile}.`);
    const localRequests = [
      ...content.matchAll(/\bfrom\s+["']([^"']+)["']/g),
      ...content.matchAll(/\b(?:fetch|import)\(\s*["']([^"']+)["']/g),
    ].map((match) => match[1]).filter((url) => !/^(?:https?:|data:)/i.test(url));
    for (const request of localRequests) {
      await access(resolve(dirname(staticFile), request.split(/[?#]/, 1)[0]));
    }
  }
}

const runnableLabs = experiments.length - Object.keys(localOnly).length;
console.log(
  `Built ${experiments.length} experiments (${runnableLabs} run in the browser, ${Object.keys(localOnly).length} 需要本机), ${chapters.length} chapters, ${units.length} reading units, ${contentDocuments.length} resource pages, ${searchEntries.length} search entries, and ${sourceBundle.files.length} bundled source files (${Math.round(sourceBundleBytes / 1024)} KB at ${sourceRevision.slice(0, 7)}) into ${outputDirectory}`,
);
