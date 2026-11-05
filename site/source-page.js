// source.html: one bundled file in the panel, or a directory of them, chosen by ?path= or ?dir=.
(() => {
  "use strict";

  const params = new URL(location.href).searchParams;
  const requestedPath = params.get("path")?.replace(/^\/+/, "") ?? null;
  const requestedDir = (params.get("dir") ?? "").replace(/^\/+|\/+$/g, "");
  const crumbs = document.querySelector("#source-crumbs");
  const title = document.querySelector("#source-title");
  const note = document.querySelector("#source-note");
  const tree = document.querySelector("#source-tree");
  const panelRoot = document.querySelector("#source-panel");
  const siblings = document.querySelector("#source-siblings");
  const siblingList = document.querySelector("#source-sibling-list");
  const base = "assets/source/";

  function link(path, text) {
    const anchor = document.createElement("a");
    anchor.href = path ? `source.html?${path.endsWith("/") ? "dir" : "path"}=${encodeURIComponent(path.replace(/\/$/, ""))}` : "source.html";
    anchor.textContent = text ?? path;
    return anchor;
  }

  function separator() {
    const span = document.createElement("span");
    span.setAttribute("aria-hidden", "true");
    span.textContent = "/";
    return span;
  }

  function renderCrumbs(path) {
    crumbs.replaceChildren(link("", "源码"));
    const parts = path.split("/").filter(Boolean);
    parts.forEach((part, index) => {
      crumbs.append(separator());
      const isLast = index === parts.length - 1;
      if (isLast) {
        const span = document.createElement("span");
        span.textContent = part;
        crumbs.append(span);
      } else {
        crumbs.append(link(`${parts.slice(0, index + 1).join("/")}/`, part));
      }
    });
  }

  function renderTree(index, dir) {
    const prefix = dir ? `${dir}/` : "";
    const children = new Map();
    for (const file of index.files) {
      if (!file.path.startsWith(prefix)) continue;
      const rest = file.path.slice(prefix.length);
      const slash = rest.indexOf("/");
      const name = slash === -1 ? rest : rest.slice(0, slash);
      const entry = children.get(name) ?? { name, directory: slash !== -1, files: 0, bytes: 0, lines: 0 };
      entry.files += 1;
      entry.bytes += file.bytes;
      entry.lines += file.lines;
      children.set(name, entry);
    }
    tree.hidden = false;
    if (!children.size) {
      tree.textContent = "没有这个目录。";
      return;
    }
    const list = document.createElement("ol");
    list.className = "source-rows";
    for (const entry of [...children.values()].sort((a, b) => Number(b.directory) - Number(a.directory) || a.name.localeCompare(b.name))) {
      const row = document.createElement("li");
      row.className = "nb-entry source-row";
      const target = prefix + entry.name + (entry.directory ? "/" : "");
      const name = link(target, entry.name + (entry.directory ? "/" : ""));
      const meta = document.createElement("span");
      meta.textContent = entry.directory ? `${entry.files} 个文件 · ${entry.lines} 行` : `${entry.lines} 行 · ${(entry.bytes / 1024).toFixed(1)} KB`;
      row.append(name, meta);
      list.append(row);
    }
    tree.replaceChildren(list);
  }

  function renderFile(index, path) {
    const file = index.files.find((item) => item.path === path);
    if (!file) {
      title.textContent = "没有这个文件";
      note.textContent = `${path} 不在站内的源码包里，可能不是教材用到的文件。`;
      renderCrumbs("");
      renderTree(index, "");
      return;
    }
    const dir = path.includes("/") ? path.slice(0, path.lastIndexOf("/")) : "";
    renderCrumbs(path);
    title.textContent = window.SourcePanel.fileName(path);
    note.textContent = `${path} · ${file.lines} 行 · 仓库提交 ${index.revision.slice(0, 7)}`;
    tree.hidden = true;
    panelRoot.hidden = false;
    const panel = window.SourcePanel.create({ root: panelRoot, base, files: [{ path }], active: path, blobBase: index.blobBase });
    const range = window.SourcePanel.parseRange(location.hash);
    if (range) {
      panel.mark([range]);
      panel.load(path).then(() => panel.scrollToLine(range[0]));
    }
    const others = index.files.filter((item) => item.path !== path && item.path.startsWith(dir ? `${dir}/` : "") && !item.path.slice(dir ? dir.length + 1 : 0).includes("/"));
    if (others.length) {
      siblings.hidden = false;
      siblingList.replaceChildren(...others.map((item) => {
        const row = document.createElement("li");
        row.append(link(item.path, window.SourcePanel.fileName(item.path)));
        return row;
      }));
    }
  }

  // The literal path lets the build check that the index is in the output.
  fetch("assets/source/index.json").then((response) => {
    if (!response.ok) throw new Error(`无法读取源码索引（${response.status}）`);
    return response.json();
  }).then((index) => {
    document.title = `${requestedPath ? window.SourcePanel.fileName(requestedPath) : requestedDir || "源码"} · LearnDotnetCSharp`;
    if (requestedPath) renderFile(index, requestedPath);
    else {
      renderCrumbs(requestedDir ? `${requestedDir}/` : "");
      title.textContent = requestedDir ? `${requestedDir.split("/").pop()}/` : "源码";
      if (requestedDir) note.textContent = `${requestedDir} · 仓库提交 ${index.revision.slice(0, 7)}`;
      renderTree(index, requestedDir);
    }
  }).catch((error) => {
    title.textContent = "源码暂时读不到";
    note.textContent = error.message;
  });
})();
