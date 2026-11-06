// Runs one lab in the browser on the vendored Roslyn + .NET runtime (compiler-worker.js).
// The lab's files (the demo as shown in the panel, its sibling tabs and the Infrastructure files) are the
// submission; the checks file supplies the App's implicit usings and an entry that constructs the demo and
// awaits RunAsync(new DemoContext(Console.Out), CancellationToken.None).
(() => {
  "use strict";

  const workerUrl = new URL("compiler-worker.js", document.currentScript.src);
  const stages = { load: [60000, "加载运行时"], compile: [30000, "编译"], execute: [5000, "执行"] };
  const usings = ["System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http", "System.Threading", "System.Threading.Tasks"]
    .map((name) => `global using ${name};`).join("\n");

  function checksFor(typeName) {
    return `${usings}
public static class ExerciseChecks
{
    public static async Task<string[]> Run()
    {
        global::LearnDotnetCSharp.Infrastructure.IDemo demo = new global::${typeName}();
        var availability = demo.Availability;
        if (!availability.CanRun) throw new InvalidOperationException("这个实验在当前环境不可用：" + availability.SkipReason);
        await demo.RunAsync(new global::LearnDotnetCSharp.Infrastructure.DemoContext(Console.Out), CancellationToken.None);
        return new[] { "+" + demo.Metadata.Id };
    }
}`;
  }

  function run({ files, typeName, onStage }) {
    let worker = null;
    let timer = 0;
    let settle = null;
    const done = new Promise((resolve) => { settle = resolve; });

    function finish(result) {
      clearTimeout(timer);
      worker?.terminate();
      worker = null;
      settle(result);
    }

    function stage(name) {
      clearTimeout(timer);
      const [timeout, label] = stages[name];
      onStage?.(name, label);
      timer = setTimeout(() => finish({ ok: false, timedOut: true, stage: name, label, output: "", diagnostics: [], error: `${label}超时（${timeout / 1000} 秒），已停止。` }), timeout);
    }

    const encoder = new TextEncoder();
    const oversized = files.find((file) => encoder.encode(file.text).length > 100 * 1024);
    if (oversized) {
      finish({ ok: false, stage: "compile", output: "", diagnostics: [], error: `${oversized.name} 超过 100 KB，已拒绝运行。` });
      return { done, stop: () => {} };
    }

    stage("load");
    try {
      const active = new Worker(workerUrl, { type: "module" });
      worker = active;
      active.onerror = (event) => { event.preventDefault(); finish({ ok: false, stage: "load", output: "", diagnostics: [], error: `运行失败：${event.message}` }); };
      active.onmessage = ({ data }) => {
        if (worker !== active) return;
        if (data.stage === "compile" || data.stage === "execute") { stage(data.stage); return; }
        if (data.stage === "error") { finish({ ok: false, stage: "load", output: "", diagnostics: [], error: `运行失败：${data.error}` }); return; }
        if (data.stage !== "done") return;
        const diagnostics = (data.compilation?.diagnostics ?? []).map((item) => `${item.file ? `${item.file} ` : ""}(${item.line},${item.column}): ${item.message}`);
        const result = data.result;
        if (!data.compilation?.success) { finish({ ok: false, stage: "compile", output: "", diagnostics, error: "编译失败。" }); return; }
        const output = (result?.output ?? "") + (result?.outputTruncated ? "\n[输出已达到 64 KB，后续输出已截断]" : "");
        finish({ ok: Boolean(result && !result.error), stage: "execute", output, diagnostics, error: result?.error ? `运行异常：${result.error}` : null });
      };
      active.postMessage({ files, checks: checksFor(typeName) });
    } catch (error) {
      finish({ ok: false, stage: "load", output: "", diagnostics: [], error: `无法启动运行环境：${error.message}` });
    }

    return { done, stop: () => finish({ ok: false, stopped: true, stage: "stopped", output: "", diagnostics: [], error: "已停止。" }) };
  }

  window.LabRunner = Object.freeze({ run, checksFor, stages });
})();
