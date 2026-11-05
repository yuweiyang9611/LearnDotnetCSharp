// The lab panels on a chapter page: each lab's demo file read in place, with its kit form built by source-panel.js.
// The build writes the tabs, the line count and a <noscript> GitHub link; this replaces them with the live panel.
(() => {
  "use strict";

  if (!window.SourcePanel) return;
  for (const root of document.querySelectorAll("[data-source-panel]")) {
    let files;
    try {
      files = JSON.parse(root.dataset.sourceFiles);
    } catch {
      continue;
    }
    if (!Array.isArray(files) || files.length === 0) continue;
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
  }
})();
