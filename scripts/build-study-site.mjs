import { createHash } from "node:crypto";
import { access, copyFile, cp, mkdir, readFile, readdir, rm, stat, writeFile } from "node:fs/promises";
import { dirname, extname, join, posix, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { categories, experiments, learningStages, repositoryUrl, resources } from "../site/catalog-data.js";
import { chapterTasks } from "../site/chapter-tasks.js";
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
    ? `<a class="assessment-lab-link" href="${escapeHtml(relativeDirectoryHref(unit.route, "layers/"))}">打开四层代码实验台 <span aria-hidden="true">→</span></a>`
    : "";
  const checklist = (items, kind) => items.map((item, index) => `
    <li><label><input type="checkbox" data-assessment-${kind}="${index}" /><span>${escapeHtml(item)}</span></label></li>`).join("");
  return `
  <section class="chapter-assessment" data-chapter-assessment data-task-id="${taskId}" data-task-chapter="${task.chapter}" data-task-revision="${chapterTaskRevision(task)}" aria-labelledby="${taskId}-title">
    <header class="assessment-heading">
      <div><p class="eyebrow"><span></span> 本章验收任务</p><h2 id="${taskId}-title">${escapeHtml(task.title)}</h2></div>
      <span class="assessment-state" data-assessment-state>尚未验收</span>
    </header>
    <p class="assessment-objective">${escapeHtml(task.objective)}</p>
    <noscript><p class="assessment-no-script">网页自动记录需要 JavaScript；你仍可按下面清单在本地执行，并把证据保存到自己的学习笔记。</p></noscript>
    <div class="assessment-command"><code>${escapeHtml(task.command)}</code><button type="button" data-copy-task-command="${escapeHtml(task.command)}">复制命令</button></div>
    ${layerLabLink}
    <div class="assessment-columns">
      <section><h3>完成步骤</h3><ol class="assessment-checklist">${checklist(task.steps, "step")}</ol></section>
      <section><h3>通过标准</h3><ul class="assessment-checklist criteria-list">${checklist(task.criteria, "criterion")}</ul></section>
    </div>
    <details class="assessment-evidence-guide"><summary>查看需要保留的证据</summary><ul>${task.evidence.map((item) => `<li>${escapeHtml(item)}</li>`).join("")}</ul></details>
    <label class="assessment-notes"><span>证据记录</span><textarea data-assessment-notes rows="5" maxlength="2400" placeholder="记录关键输出、修改位置与解释；内容只保存在当前浏览器。"></textarea></label>
    <div class="assessment-footer"><p data-assessment-status role="status" aria-live="polite">完成步骤、核对标准并填写证据记录后即可验收。</p><button class="button button-primary" type="button" data-assessment-complete disabled>标记为已验收</button></div>
  </section>`;
}

function renderStaticHomepage(experimentGuideUrls) {
  const pathHtml = learningStages.map((stage) => {
    const total = experiments.filter(({ category }) => stage.categories.includes(category)).length;
    return `
        <article class="path-card">
          <div class="path-card-topline"><span class="step-number">${escapeHtml(stage.number)}</span><span class="stage-progress">0/${total}</span></div>
          <p>${escapeHtml(stage.label)}</p><h3>${escapeHtml(stage.title)}</h3>
          <p class="path-description">${escapeHtml(stage.description)}</p>
          <div class="mini-progress" aria-label="${escapeHtml(stage.label)}完成 0%"><span style="width: 0%"></span></div>
          <button class="path-action" type="button" data-stage="${escapeHtml(stage.number)}">查看本阶段 <span aria-hidden="true">→</span></button>
        </article>`;
  }).join("");
  const filtersHtml = [
    `<button class="filter-chip is-active" type="button" data-category="all" aria-pressed="true">全部 <span>${experiments.length}</span></button>`,
    ...Object.entries(categories).map(([key, category]) => {
      const count = experiments.filter((experiment) => experiment.category === key).length;
      return `<button class="filter-chip" type="button" data-category="${escapeHtml(key)}" aria-pressed="false">${escapeHtml(category.label)} <span>${count}</span></button>`;
    }),
  ].join("");
  const experimentsHtml = experiments.map((experiment) => {
    const category = categories[experiment.category];
    const command = `dotnet run --project src/LearnDotnetCSharp.App -- run ${experiment.id}`;
    const sourceUrl = `${repositoryUrl}/blob/main/${experiment.source}`;
    return `
        <article class="experiment-card" id="experiment-${escapeHtml(experiment.id)}">
          <div class="experiment-topline">
            <span class="category-badge tone-${escapeHtml(category.tone)}">${escapeHtml(category.label)}</span>
            <button class="complete-toggle" type="button" data-complete-id="${escapeHtml(experiment.id)}" aria-pressed="false" aria-label="标记完成：${escapeHtml(experiment.title)}"><span aria-hidden="true"></span>标记完成</button>
          </div>
          <code class="experiment-id">${escapeHtml(experiment.id)}</code><h3>${escapeHtml(experiment.title)}</h3><p>${escapeHtml(experiment.summary)}</p>
          <div class="experiment-actions">
            <a href="${escapeHtml(experimentGuideUrls[experiment.id])}">阅读讲解 <span aria-hidden="true">→</span></a>
            <a href="${escapeHtml(sourceUrl)}">查看源码 <span aria-hidden="true">↗</span></a>
            <button type="button" data-copy="${escapeHtml(command)}">复制运行命令</button>
          </div>
        </article>`;
  }).join("");
  const resourcesHtml = resources.map((resource) => `
        <a class="resource-card" href="${escapeHtml(resource.href)}"${resource.download ? " download" : ""}>
          <span>${escapeHtml(resource.type)}</span><h3>${escapeHtml(resource.title)}</h3><p>${escapeHtml(resource.description)}</p>
          <strong>${escapeHtml(resource.action)} <span aria-hidden="true">${resource.download ? "↓" : "→"}</span></strong>
        </a>`).join("");
  return { experimentsHtml, filtersHtml, pathHtml, resourcesHtml };
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
  const label = direction === "previous" ? "上一个阅读单元" : "下一个阅读单元";
  return `<a class="${direction}-unit" href="${escapeHtml(relativeDirectoryHref(currentUnit.route, targetUnit.route))}"><small>${label}</small><strong>${escapeHtml(targetUnit.title)}</strong></a>`;
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
  });
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
    NEXT_LINK_HTML: paginationLink(unit, units[index + 1], "next"),
    PAGE_HEADING_ID: escapeHtml(pageHeading.id),
    PAGE_TITLE: escapeHtml(unit.title),
    PART_TITLE: escapeHtml(unit.partTitle),
    PDF_HREF: relativeFileHref(unit.route, "downloads/LearnDotnetCSharp-Study-Guide.pdf"),
    POSITION_LABEL: escapeHtml(positionLabel),
    PREVIOUS_LINK_HTML: paginationLink(unit, units[index - 1], "previous"),
    READING_ROUTE: escapeHtml(unit.route),
    SEARCH_HREF: relativeDirectoryHref(unit.route, "search/"),
    SHARED_CSS_HREF: relativeFileHref(unit.route, "styles.css"),
    TOC_HTML: tocHtml,
  });
  if (countMatches(html, /<h1(?:\s|>)/g) !== 1) throw new Error(`Guide unit '${unit.route}' must contain exactly one H1.`);
  if (/(?:href|src)=["']\/(?!\/)/i.test(html)) throw new Error(`Guide unit '${unit.route}' contains a root-relative URL.`);
  if (/<(?:iframe|embed|object)[^>]+\.pdf/i.test(html)) throw new Error(`Guide unit '${unit.route}' embeds a PDF.`);
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
const unitDirectoryHtml = [...groupedUnits.entries()].map(([partTitle, partUnits]) => {
  const groupHeadingId = partHeadingIdByTitle.get(partTitle) ?? `part-${partUnits[0].route.replaceAll("/", "-")}`;
  return `
  <section class="guide-part-group" aria-labelledby="${escapeHtml(groupHeadingId)}">
    <div class="guide-part-heading"><h3 id="${escapeHtml(groupHeadingId)}">${escapeHtml(partTitle)}</h3><span>${partUnits.length} 个阅读单元</span></div>
    <div class="guide-unit-grid">
      ${partUnits.map((unit) => {
        const chapterTask = unit.kind === "chapter" ? chapterTasks.find(({ chapter }) => chapter === unit.number) : null;
        const taskId = chapterTask ? `chapter-${String(unit.number).padStart(2, "0")}` : "";
        const taskRevision = chapterTask ? chapterTaskRevision(chapterTask) : "";
        const taskStatus = taskId ? `<span class="guide-task-status" data-guide-task-status>待验收</span>` : "";
        return `<a class="guide-unit-card"${taskId ? ` data-chapter-task-id="${taskId}" data-chapter-task-revision="${taskRevision}"` : ""} href="${escapeHtml(relativeDirectoryHref("guide/", unit.route))}"><span class="guide-unit-number">${escapeHtml(unit.label)}</span><div><div class="guide-unit-title-row"><h4>${escapeHtml(unit.title.replace(/^\d+\.\s*/, ""))}</h4>${taskStatus}</div><p>${escapeHtml(unit.description)}</p></div></a>`;
      }).join("\n")}
    </div>
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
  STATIC_EXPERIMENTS_HTML: staticHomepage.experimentsHtml,
  STATIC_FILTERS_HTML: staticHomepage.filtersHtml,
  STATIC_LEARNING_PATH_HTML: staticHomepage.pathHtml,
  STATIC_RESOURCES_HTML: staticHomepage.resourcesHtml,
});
if (countMatches(homepageHtml, /class="experiment-card"/g) !== experiments.length ||
    countMatches(homepageHtml, /class="path-card"/g) !== learningStages.length ||
    countMatches(homepageHtml, /class="resource-card"/g) !== resources.length) {
  throw new Error("The progressively rendered homepage is missing catalog content.");
}

const initialFocus = layerArtifacts.focuses[0];
const initialStackStep = layerArtifacts.stackSteps[0];
const layerHtml = applyTemplate(layerTemplate, {
  ASSEMBLY_CAPTION: escapeHtml(`${layerArtifacts.environment.architecture} · ${layerArtifacts.environment.captureMode}`),
  LAYER_ASSEMBLY_HTML: escapeHtml(layerArtifacts.stages.assembly),
  LAYER_CIL_HTML: escapeHtml(layerArtifacts.stages.cil),
  LAYER_FOCUS_CONTROLS_HTML: layerArtifacts.focuses.map((focus, index) => (
    `<button type="button" data-focus="${escapeHtml(focus.id)}" aria-pressed="${index === 0}">${escapeHtml(focus.label)}</button>`
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
await writeFile(resolve(outputDirectory, "progress-catalog.js"),
  `export const tasks = ${JSON.stringify(progressTasks)};\nexport const guideRoutes = ${JSON.stringify(units.map(({ route }) => route))};\n`, "utf8");
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

const staticFiles = await findStaticFiles(outputDirectory, new Set([".css", ".html", ".js"]));
for (const staticFile of staticFiles) {
  const content = await readFile(staticFile, "utf8");
  if (extname(staticFile) === ".html") {
    if (/\{\{[A-Z_]+\}\}/.test(content)) throw new Error(`Unresolved template token in ${staticFile}.`);
    if (countMatches(content, /<h1(?:\s|>)/g) !== 1) throw new Error(`HTML page must contain exactly one H1: ${staticFile}.`);
    if (/<(?:iframe|embed|object)[^>]+\.pdf/i.test(content) || /rel=["']preload["'][^>]+\.pdf/i.test(content)) {
      throw new Error(`HTML page embeds or preloads the PDF: ${staticFile}.`);
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

console.log(
  `Built ${experiments.length} experiments, ${chapters.length} chapters, ${units.length} reading units, ${contentDocuments.length} resource pages, and ${searchEntries.length} search entries into ${outputDirectory}`,
);
