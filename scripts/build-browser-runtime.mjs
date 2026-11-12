// Publishes the vendored Roslyn + .NET WebAssembly host (tools/BrowserRuntime) and stages what the site serves:
// _framework/ (the runtime and the compiler) and refs/ (the reference assemblies a lab compiles against, listed
// in manifest.json). The site build copies the staged folder to assets/runtime/. Generated, never committed.
import { spawnSync } from "node:child_process";
import { cp, mkdir, readdir, rm, stat, writeFile } from "node:fs/promises";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = resolve(fileURLToPath(new URL("..", import.meta.url)));
const project = resolve(repositoryRoot, "tools", "BrowserRuntime", "BrowserExercises");
const output = resolve(project, "bin", "Release", "net10.0");
const framework = resolve(output, "publish", "wwwroot", "_framework");
const compilerReferences = resolve(output, "compiler-refs");
const runtimeStage = resolve(repositoryRoot, "artifacts", "browser-runtime");
const dotnet = process.env.LEARN_DOTNET_WASM_DOTNET || "dotnet";

function run(args) {
  // No SDK version is pinned: dotnet picks the newest installed SDK, and the project files fix net10.0.
  const result = spawnSync(dotnet, args, { cwd: project, stdio: "inherit" });
  if (result.status !== 0) {
    throw new Error(`'${dotnet} ${args.join(" ")}' failed. Install a .NET SDK (10 or later) and its WebAssembly workloads: 'dotnet workload install wasm-tools wasm-experimental' on a .NET 10 SDK, 'dotnet workload install wasm-tools-net10 wasm-experimental-net10' on a newer one.`);
  }
}

async function exists(path) {
  try { await stat(path); return true; } catch { return false; }
}

async function bytes(directory) {
  let total = 0;
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    total += entry.isDirectory() ? await bytes(join(directory, entry.name)) : (await stat(join(directory, entry.name))).size;
  }
  return total;
}

run(["restore", "BrowserExercises.csproj"]);
run(["publish", "BrowserExercises.csproj", "-c", "Release", "--no-restore"]);
if (!await exists(resolve(framework, "dotnet.js"))) throw new Error(`Published browser framework is missing: ${framework}`);
if (!await exists(resolve(compilerReferences, "System.Runtime.dll"))) throw new Error(`Compiler reference assemblies are missing: ${compilerReferences}`);

await rm(runtimeStage, { force: true, recursive: true });
await mkdir(resolve(runtimeStage, "refs"), { recursive: true });
// The loader fetches the plain files; the precompressed .br/.gz twins only matter behind a negotiating server.
await cp(framework, resolve(runtimeStage, "_framework"), { recursive: true, filter: (source) => !/\.(?:br|gz)$/.test(source) });
// Every reference assembly of the ref pack, so a lab may use any namespace of the base library.
const references = (await readdir(compilerReferences)).filter((name) => name.endsWith(".dll")).sort();
for (const name of references) await cp(join(compilerReferences, name), resolve(runtimeStage, "refs", name));
await writeFile(resolve(runtimeStage, "refs", "manifest.json"), `${JSON.stringify(references, null, 2)}\n`, "utf8");
console.log(`Browser runtime staged in ${runtimeStage}: ${references.length} reference assemblies, ${Math.round(await bytes(runtimeStage) / 1024 / 1024)} MB.`);
