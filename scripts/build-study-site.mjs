import { access, copyFile, cp, mkdir, readFile, readdir, rm, stat, writeFile } from "node:fs/promises";
import { dirname, extname, join, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { categories, experiments, repositoryUrl, resources } from "../site/catalog-data.js";
import { escapeHtml, renderStudyGuide } from "./markdown_to_site.mjs";

const repositoryRoot = resolve(fileURLToPath(new URL("..", import.meta.url)));
const sourceDirectory = resolve(repositoryRoot, "site");
const outputDirectory = resolve(repositoryRoot, "artifacts", "study-site");
const pdfSource = resolve(repositoryRoot, "output", "pdf", "LearnDotnetCSharp-Study-Guide.pdf");
const pdfDestination = resolve(outputDirectory, "downloads", "LearnDotnetCSharp-Study-Guide.pdf");
const guideSource = resolve(repositoryRoot, "docs", "advanced-dotnet-csharp-study-guide.md");
const guideTemplateSource = resolve(repositoryRoot, "scripts", "templates", "study-guide.html");
const guideDestination = resolve(outputDirectory, "guide", "index.html");

async function findCSharpFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = await Promise.all(
    entries.map(async (entry) => {
      const entryPath = join(directory, entry.name);
      if (entry.isDirectory()) {
        return findCSharpFiles(entryPath);
      }

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
      if (entry.isDirectory()) {
        return findStaticFiles(entryPath, extensions);
      }

      return entry.isFile() && extensions.has(extname(entry.name)) ? [entryPath] : [];
    }),
  );
  return files.flat();
}

function countMatches(value, pattern) {
  return [...value.matchAll(pattern)].length;
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
  if (match) {
    sourceCatalogIds.add(match[1]);
  }
}

const missingFromSite = [...sourceCatalogIds].filter((id) => !uniqueIds.has(id));
const missingFromSource = [...uniqueIds].filter((id) => !sourceCatalogIds.has(id));
if (missingFromSite.length || missingFromSource.length) {
  throw new Error(
    `Catalog drift detected. Missing from site: ${missingFromSite.join(", ") || "none"}; ` +
      `missing from source: ${missingFromSource.join(", ") || "none"}.`,
  );
}

const indexHtml = await readFile(resolve(sourceDirectory, "index.html"), "utf8");
if (/(?:href|src)=["']\/(?!\/)/i.test(indexHtml)) {
  throw new Error("Root-relative assets are not compatible with the GitHub Pages project path.");
}

if (!indexHtml.includes('href="./guide/"')) {
  throw new Error("The study-site homepage has no direct online-guide entry point.");
}

const guideResource = resources.find(({ type }) => type === "主教材");
const pdfResource = resources.find(({ type }) => type === "PDF");
if (guideResource?.href !== "./guide/") {
  throw new Error("The primary study-guide resource must open the in-site HTML reader.");
}
if (pdfResource?.href !== "./downloads/LearnDotnetCSharp-Study-Guide.pdf" || !pdfResource.download) {
  throw new Error("The PDF resource must remain an explicit download-only secondary entry point.");
}

const guideMarkdown = await readFile(guideSource, "utf8");
const guideTemplate = await readFile(guideTemplateSource, "utf8");
const experimentHeadingAliases = {
  "28.1 ": "project.cancellable-data-pipeline",
  "28.2 ": "project.versioned-local-service",
  "28.3 ": "project.collectible-plugin-host",
  "28.4 ": "project.polyglot-compute",
  "28.5 ": "project.resilient-analytics-workflow",
};
const renderedGuide = renderStudyGuide(guideMarkdown, {
  experimentHeadingAliases,
  experimentIds: experiments.map(({ id }) => id),
  repositoryUrl,
});
const numberedChapters = renderedGuide.headings.filter(
  ({ level, text }) => level === 2 && /^\d+\.\s/.test(text),
);

if (numberedChapters.length !== 30) {
  throw new Error(`Expected 30 numbered guide chapters, found ${numberedChapters.length}.`);
}

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

const expectedCodeLanguages = {
  asm: 2,
  bash: 1,
  csharp: 3,
  il: 2,
  markdown: 1,
  powershell: 10,
  text: 22,
};
if (JSON.stringify(renderedGuide.codeLanguages) !== JSON.stringify(expectedCodeLanguages)) {
  throw new Error(`Online guide code-language distribution changed: ${JSON.stringify(renderedGuide.codeLanguages)}.`);
}

const uniqueHeadingIds = new Set(renderedGuide.headings.map(({ id }) => id));
if (uniqueHeadingIds.size !== renderedGuide.headings.length) {
  throw new Error("The generated online guide contains duplicate heading IDs.");
}

for (const { id } of renderedGuide.headings) {
  if (!renderedGuide.tocHtml.includes(`href="#${id}"`)) {
    throw new Error(`The online guide table of contents does not link to heading '${id}'.`);
  }
}

for (const href of renderedGuide.sourceLinks) {
  if (/^(?:https?:|mailto:|#)/i.test(href)) {
    continue;
  }

  const pathPart = href.split(/[?#]/, 1)[0];
  const targetPath = resolve(dirname(guideSource), decodeURIComponent(pathPart));
  if (targetPath !== repositoryRoot && !targetPath.startsWith(`${repositoryRoot}${sep}`)) {
    throw new Error(`Study-guide link escapes the repository: ${href}`);
  }
  await access(targetPath);
}

for (const experiment of experiments) {
  const expectedAnchor = `experiment-${experiment.id.replaceAll(".", "-")}`;
  if (!renderedGuide.headings.some(({ id }) => id === expectedAnchor)) {
    throw new Error(`The generated online guide has no stable anchor for experiment '${experiment.id}'.`);
  }
}

const guideTitle = renderedGuide.metadata.title ?? ".NET 10 与 C# 14 高级特性学习指导";
const guideSubtitle = renderedGuide.metadata.subtitle ?? "以 53 个可运行实验为主线";
const guideDate = renderedGuide.metadata.date ?? "持续更新";
const guideDescription = `${guideSubtitle}。可直接按章节在线阅读，并保留 PDF 离线版本。`;
const guideTokens = new Map([
  ["{{TITLE}}", escapeHtml(guideTitle)],
  ["{{SUBTITLE}}", escapeHtml(guideSubtitle)],
  ["{{DATE}}", escapeHtml(guideDate)],
  ["{{DESCRIPTION}}", escapeHtml(guideDescription)],
  ["{{CHAPTER_COUNT}}", String(numberedChapters.length)],
  ["{{HEADING_COUNT}}", String(renderedGuide.headings.length)],
  ["{{TOC_HTML}}", renderedGuide.tocHtml],
  ["{{ARTICLE_HTML}}", renderedGuide.articleHtml],
]);

let guideHtml = guideTemplate;
for (const [token, value] of guideTokens) {
  guideHtml = guideHtml.replaceAll(token, value);
}

if (/\{\{[A-Z_]+\}\}/.test(guideHtml)) {
  throw new Error("The generated online guide contains unresolved template tokens.");
}

if (/(?:href|src)=["']\/(?!\/)/i.test(guideHtml)) {
  throw new Error("The online guide contains a root-relative URL that will break on GitHub Pages.");
}

if (Buffer.byteLength(guideHtml, "utf8") < 150_000) {
  throw new Error("The generated online guide is unexpectedly small.");
}

const generatedHtmlCounts = {
  blockquotes: countMatches(renderedGuide.articleHtml, /<blockquote>/g),
  checkboxes: countMatches(renderedGuide.articleHtml, /<input type="checkbox"/g),
  codeBlocks: countMatches(renderedGuide.articleHtml, /<figure class="code-block">/g),
  headingLevel1: countMatches(renderedGuide.articleHtml, /<h2 class="guide-heading guide-heading-1"/g),
  headingLevel2: countMatches(renderedGuide.articleHtml, /<h3 class="guide-heading guide-heading-2"/g),
  headingLevel3: countMatches(renderedGuide.articleHtml, /<h4 class="guide-heading guide-heading-3"/g),
  listItems: countMatches(renderedGuide.articleHtml, /<li(?:\s|>)/g),
  orderedLists: countMatches(renderedGuide.articleHtml, /<ol(?:\s|>)/g),
  tables: countMatches(renderedGuide.articleHtml, /<table>/g),
  unorderedLists: countMatches(renderedGuide.articleHtml, /<ul(?:\s|>)/g),
};
const expectedHtmlCounts = {
  blockquotes: expectedGuideStructure.blockquoteCount,
  checkboxes: expectedGuideStructure.taskListItemCount,
  codeBlocks: expectedGuideStructure.codeBlockCount,
  headingLevel1: expectedGuideStructure.headingLevel1,
  headingLevel2: expectedGuideStructure.headingLevel2,
  headingLevel3: expectedGuideStructure.headingLevel3,
  listItems: expectedGuideStructure.listItemCount,
  orderedLists: expectedGuideStructure.orderedListCount,
  tables: expectedGuideStructure.tableCount,
  unorderedLists: expectedGuideStructure.unorderedListCount,
};
if (JSON.stringify(generatedHtmlCounts) !== JSON.stringify(expectedHtmlCounts)) {
  throw new Error(`Generated guide HTML lost structure: ${JSON.stringify(generatedHtmlCounts)}.`);
}

if (renderedGuide.articleHtml.includes("[ ]")) {
  throw new Error("The generated online guide contains an unrendered task-list marker.");
}

if (countMatches(guideHtml, /<h1(?:\s|>)/g) !== 1) {
  throw new Error("The generated online guide must contain exactly one page-level H1.");
}

if (/<(?:iframe|embed|object)[^>]+\.pdf/i.test(guideHtml) || /rel=["']preload["'][^>]+\.pdf/i.test(guideHtml)) {
  throw new Error("The online guide must not embed or preload the PDF.");
}

if (!/href=["']\.\.\/downloads\/LearnDotnetCSharp-Study-Guide\.pdf["'][^>]*\sdownload/i.test(guideHtml)) {
  throw new Error("The online guide must expose the PDF only as an explicit download link.");
}

const imageInfo = await stat(resolve(sourceDirectory, "og.png"));
if (imageInfo.size < 10_000) {
  throw new Error("The social preview image is missing or unexpectedly small.");
}

await rm(outputDirectory, { force: true, recursive: true });
await mkdir(dirname(pdfDestination), { recursive: true });
await cp(sourceDirectory, outputDirectory, { recursive: true });
await copyFile(pdfSource, pdfDestination);
await mkdir(dirname(guideDestination), { recursive: true });
await writeFile(guideDestination, guideHtml, "utf8");
await writeFile(resolve(outputDirectory, ".nojekyll"), "", "utf8");
await writeFile(
  resolve(outputDirectory, "404.html"),
  `<!doctype html>
<html lang="zh-CN">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <meta http-equiv="refresh" content="0; url=/LearnDotnetCSharp/" />
    <title>返回 LearnDotnetCSharp</title>
  </head>
  <body>
    <p>页面不存在，正在返回 <a href="/LearnDotnetCSharp/">LearnDotnetCSharp 学习站</a>。</p>
  </body>
</html>
`,
  "utf8",
);

const staticFiles = await findStaticFiles(outputDirectory, new Set([".css", ".html", ".js"]));
for (const staticFile of staticFiles) {
  const content = await readFile(staticFile, "utf8");
  if (extname(staticFile) === ".html") {
    for (const match of content.matchAll(/(?:href|src)=["']([^"']+)["']/gi)) {
      const url = match[1];
      if (/^(?:https?:|mailto:|data:|#)/i.test(url)) {
        continue;
      }
      if (url.startsWith("/")) {
        if (url !== "/LearnDotnetCSharp/") {
          throw new Error(`Root-relative URL '${url}' is not compatible with the GitHub Pages project path.`);
        }
        continue;
      }

      const relativePath = decodeURIComponent(url.split(/[?#]/, 1)[0]);
      if (!relativePath) {
        continue;
      }
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
  } else if (extname(staticFile) === ".css" && /url\(\s*["']?\/(?!\/)/i.test(content)) {
    throw new Error(`Root-relative CSS asset found in ${staticFile}.`);
  } else if (extname(staticFile) === ".js" && /(?:fetch|import)\(\s*["']\/(?!\/)/i.test(content)) {
    throw new Error(`Root-relative JavaScript request found in ${staticFile}.`);
  }
}

console.log(
  `Built ${experiments.length} experiments and ${numberedChapters.length} guide chapters into ${outputDirectory}`,
);
