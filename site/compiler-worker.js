// The browser compiler in a module worker: boots the vendored .NET runtime, loads every reference assembly in
// refs/manifest.json, compiles the lab's files with the entry, then runs it. Each stage is posted back so the
// page can show 加载运行时 / 编译 / 执行 and time each one.
import { dotnet } from "./assets/runtime/_framework/dotnet.js";

self.onmessage = async ({ data }) => {
  try {
    const runtime = await dotnet.create();
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    const compiler = exports.BrowserCompiler;
    const base = new URL("./assets/runtime/refs/", import.meta.url);
    const manifest = await fetch(new URL("manifest.json", base)).then((response) => {
      if (!response.ok) throw new Error("无法加载编译引用清单。");
      return response.json();
    });
    const references = await Promise.all(manifest.map(async (name) => {
      const response = await fetch(new URL(name, base));
      if (!response.ok) throw new Error(`无法加载 ${name}`);
      return new Uint8Array(await response.arrayBuffer());
    }));
    for (const reference of references) compiler.AddReference(reference);
    self.postMessage({ stage: "compile" });
    const compilation = JSON.parse(compiler.Compile(JSON.stringify(data.files), data.checks));
    if (!compilation.success) { self.postMessage({ stage: "done", compilation }); return; }
    self.postMessage({ stage: "execute", compilation });
    const result = JSON.parse(await compiler.Execute());
    self.postMessage({ stage: "done", compilation, result });
  } catch (error) {
    self.postMessage({ stage: "error", error: error.message ?? String(error) });
  }
};
