import { access, copyFile, cp, mkdir, readFile, readdir, rm, stat, writeFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { categories, experiments } from "../site/catalog-data.js";

const repositoryRoot = resolve(fileURLToPath(new URL("..", import.meta.url)));
const sourceDirectory = resolve(repositoryRoot, "site");
const outputDirectory = resolve(repositoryRoot, "artifacts", "study-site");
const pdfSource = resolve(repositoryRoot, "output", "pdf", "LearnDotnetCSharp-Study-Guide.pdf");
const pdfDestination = resolve(outputDirectory, "downloads", "LearnDotnetCSharp-Study-Guide.pdf");

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

const imageInfo = await stat(resolve(sourceDirectory, "og.png"));
if (imageInfo.size < 10_000) {
  throw new Error("The social preview image is missing or unexpectedly small.");
}

await rm(outputDirectory, { force: true, recursive: true });
await mkdir(dirname(pdfDestination), { recursive: true });
await cp(sourceDirectory, outputDirectory, { recursive: true });
await copyFile(pdfSource, pdfDestination);
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

console.log(`Built ${experiments.length} experiments into ${outputDirectory}`);
