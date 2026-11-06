// The lab panels on a chapter page: each lab's demo file read in place, with its kit form built by source-panel.js.
// The build writes the tabs, the line count and a <noscript> GitHub link; this replaces them with the live panel.
// A lab that runs in the browser gets 运行 / 改一改再跑 / 恢复原文 / 停止 and an output box (lab-runner.js);
// a 需要本机 lab keeps its command row. Steps the reader takes are announced as `lesson-step` events on
// document, which guide/app.js records: a tab clicked (code), a run finished or a command copied (run).
(() => {
  "use strict";

  if (!window.SourcePanel) return;

  function announce(step, id, detail = {}) {
    document.dispatchEvent(new CustomEvent("lesson-step", { detail: { step, id, ...detail } }));
  }

  function button(id, className, text, hidden = false) {
    const node = document.createElement("button");
    node.type = "button";
    node.className = className;
    node.id = id;
    node.textContent = text;
    node.hidden = hidden;
    return node;
  }

  // The run controls of one runnable panel: the lab's file as shown (editable) plus its siblings and the
  // Infrastructure files are compiled together; the output sits under the actions row.
  function attachRunner(root, panel, files, labId) {
    const typeName = root.dataset.runType;
    let support;
    try { support = JSON.parse(root.dataset.runSupport ?? "[]"); } catch { support = []; }
    const mainPath = files[0].path;
    const key = labId.replaceAll(".", "-");
    const runButton = button(`run-${key}`, "nb-button nb-button--solid", "运行");
    const editButton = button(`edit-${key}`, "nb-button", "改一改再跑");
    const resetButton = button(`reset-${key}`, "nb-button", "恢复原文", true);
    const stopButton = button(`stop-${key}`, "nb-button", "停止", true);
    runButton.dataset.runLab = labId;
    panel.actions.prepend(runButton, editButton, resetButton, stopButton);

    const output = document.createElement("div");
    output.className = "nb-code-output";
    output.id = `output-${key}`;
    output.hidden = true;
    const outputLabel = document.createElement("p");
    outputLabel.className = "nb-code-output-label";
    const outputText = document.createElement("pre");
    outputText.tabIndex = 0;
    output.append(outputLabel, outputText);
    root.append(output);

    const editor = document.createElement("textarea");
    editor.className = "nb-code-editor";
    editor.id = `editor-${key}`;
    editor.spellcheck = false;
    editor.setAttribute("aria-label", `${window.SourcePanel.fileName(mainPath)} 的可编辑副本`);
    editor.hidden = true;
    panel.view.after(editor);

    let editing = false;
    let original = null;
    let handle = null;

    function setEditing(next) {
      editing = next;
      resetButton.hidden = !next;
      editButton.hidden = next;
      panel.setOverride(next ? { path: mainPath, element: editor, value: () => editor.value } : null);
      if (next) editor.focus();
    }

    editButton.addEventListener("click", async () => {
      original ??= await panel.load(mainPath);
      if (!editing) editor.value = original;
      await panel.select(mainPath);
      setEditing(true);
      panel.status.textContent = "这是副本，改完点运行；原文件不会变。";
    });

    resetButton.addEventListener("click", async () => {
      editor.value = original ?? "";
      setEditing(false);
      await panel.select(mainPath);
      panel.status.textContent = "已恢复原文。";
    });

    function showOutput(state, label, text) {
      output.hidden = false;
      output.dataset.state = state;
      outputLabel.textContent = label;
      outputText.textContent = text;
    }

    async function runLab() {
      if (handle) return;
      runButton.disabled = true;
      stopButton.hidden = false;
      showOutput("running", "运行中", "");
      let sources;
      try {
        original ??= await panel.load(mainPath);
        const paths = [...files.slice(1).map(({ path }) => path), ...support.filter((path) => !files.some((file) => file.path === path))];
        const texts = await Promise.all(paths.map((path) => panel.load(path)));
        sources = [{ name: mainPath, text: editing ? editor.value : original }, ...paths.map((path, index) => ({ name: path, text: texts[index] }))];
      } catch (error) {
        runButton.disabled = false;
        stopButton.hidden = true;
        showOutput("wrong", "运行失败", `无法读取源文件：${error.message}`);
        return;
      }
      handle = window.LabRunner.run({
        files: sources,
        typeName,
        onStage: (name, label) => { panel.status.textContent = `${label}中…`; outputLabel.textContent = `${label}中`; },
      });
      const result = await handle.done;
      handle = null;
      runButton.disabled = false;
      stopButton.hidden = true;
      const diagnostics = result.diagnostics.length ? result.diagnostics.join("\n") : "";
      if (result.ok) {
        showOutput("done", `运行结果 · 浏览器内 .NET${editing ? " · 已改动的副本" : ""}`, result.output.trimEnd() || "（没有输出）");
        panel.status.textContent = "运行完成。对照正文里的预期，再决定要不要改一改再跑。";
        announce("run", labId, { mode: "browser" });
      } else {
        const label = result.timedOut ? "运行超时" : result.stopped ? "已停止" : result.stage === "compile" ? "编译失败" : "运行失败";
        showOutput("wrong", label, [result.output.trimEnd(), diagnostics, result.error].filter(Boolean).join("\n"));
        panel.status.textContent = result.timedOut ? "超时了。可以改代码后重新运行。" : result.stage === "compile" ? "编译失败，看诊断里的文件和行号。" : result.error ?? "";
      }
    }

    runButton.addEventListener("click", runLab);
    stopButton.addEventListener("click", () => handle?.stop());
    window.addEventListener("pagehide", () => handle?.stop());
  }

  // A 需要本机 lab: the build wrote the note and command row inside the panel; keep them under the live panel.
  function attachLocalCommand(root, panel, labId, localNote) {
    if (localNote) root.append(localNote);
    const copy = root.querySelector("[data-copy-lab-command]");
    if (!copy) return;
    copy.addEventListener("click", async () => {
      const command = root.dataset.runCommand ?? "";
      let copied = false;
      try { await navigator.clipboard.writeText(command); copied = true; } catch { copied = false; }
      copy.textContent = copied ? "已复制" : "请手动复制";
      panel.status.textContent = copied ? "运行命令已复制，在本机的仓库目录里执行。" : "无法访问剪贴板，请手动选中命令复制。";
      window.setTimeout(() => { copy.textContent = "复制命令"; }, 1600);
      if (copied) announce("run", labId, { mode: "local" });
    });
  }

  for (const root of document.querySelectorAll("[data-source-panel]")) {
    let files;
    try {
      files = JSON.parse(root.dataset.sourceFiles);
    } catch {
      continue;
    }
    if (!Array.isArray(files) || files.length === 0) continue;
    const labId = root.dataset.labId ?? "";
    const localNote = root.querySelector(".lab-local");
    const sourcePage = root.dataset.sourcePage;
    const open = document.createElement("a");
    open.className = "nb-code-page";
    open.textContent = "站内源码页";
    const panel = window.SourcePanel.create({
      root,
      base: root.dataset.sourceBase,
      files,
      active: files[0].path,
      blobBase: root.dataset.blobBase,
      onSelect: (path) => { open.href = `${sourcePage}?path=${encodeURIComponent(path)}`; },
    });
    open.href = `${sourcePage}?path=${encodeURIComponent(files[0].path)}`;
    panel.actions.insertBefore(open, panel.actions.querySelector(".nb-code-link"));
    // A tab clicked is the reader looking at the code; the first file opening by itself is not.
    root.querySelector(".nb-code-tabs")?.addEventListener("click", (event) => {
      if (labId && event.target.closest(".nb-code-tab")) announce("code", labId);
    });
    if (root.dataset.runType && window.LabRunner) attachRunner(root, panel, files, labId);
    else if (root.dataset.localOnly) attachLocalCommand(root, panel, labId, localNote);
  }
})();
