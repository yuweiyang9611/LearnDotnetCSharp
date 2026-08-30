import { posix } from "node:path";

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function parseFrontMatter(markdown) {
  const normalized = markdown.replaceAll("\r\n", "\n");
  const lines = normalized.split("\n");
  const metadata = {};

  if (lines[0]?.trim() !== "---") {
    return { body: normalized, metadata };
  }

  const closingIndex = lines.findIndex((line, index) => index > 0 && line.trim() === "---");
  if (closingIndex === -1) {
    throw new Error("The study guide front matter is not closed.");
  }

  for (const line of lines.slice(1, closingIndex)) {
    const match = line.match(/^([a-z][a-z0-9-]*):\s*(.+)$/i);
    if (!match) {
      continue;
    }

    const value = match[2].trim().replace(/^(["'])(.*)\1$/, "$2");
    metadata[match[1]] = value;
  }

  return {
    body: lines.slice(closingIndex + 1).join("\n").trim(),
    metadata,
  };
}

function plainText(value) {
  return String(value)
    .replace(/\[([^\]]+)]\([^)]+\)/g, "$1")
    .replace(/`([^`]+)`/g, "$1")
    .replace(/[*~]/g, "")
    .replace(/<[^>]+>/g, "")
    .trim();
}

function createSlugger() {
  const counts = new Map();
  return (value, preserve = false) => {
    const base = preserve
      ? value
      : plainText(value)
          .normalize("NFKC")
          .toLocaleLowerCase("zh-CN")
          .replace(/[^\p{Letter}\p{Number}]+/gu, "-")
          .replace(/^-+|-+$/g, "") || "section";
    const count = counts.get(base) ?? 0;
    counts.set(base, count + 1);
    return count === 0 ? base : `${base}-${count + 1}`;
  };
}

function rewriteHref(href, repositoryUrl, resolveHref) {
  const trimmed = href.trim();
  if (/^(?:https?:|mailto:|#)/i.test(trimmed)) {
    return trimmed;
  }

  if (resolveHref) {
    const resolved = resolveHref(trimmed);
    if (resolved) {
      return resolved;
    }
  }

  const hashIndex = trimmed.indexOf("#");
  const pathPart = hashIndex === -1 ? trimmed : trimmed.slice(0, hashIndex);
  const fragment = hashIndex === -1 ? "" : trimmed.slice(hashIndex);
  const repositoryPath = posix.normalize(posix.join("docs", pathPart));
  if (repositoryPath.startsWith("../")) {
    throw new Error(`Refusing to create a link outside the repository: ${href}`);
  }

  return `${repositoryUrl}/blob/main/${repositoryPath}${fragment}`;
}

function renderInline(value, repositoryUrl, resolveHref) {
  const tokens = [];
  const reserve = (html) => {
    const token = `@@LDCS_INLINE_${tokens.length}@@`;
    tokens.push(html);
    return token;
  };

  let rendered = String(value);
  rendered = rendered.replace(/\[([^\]]+)]\(([^)]+)\)/g, (_match, label, href) => {
    const normalizedHref = rewriteHref(href, repositoryUrl, resolveHref);
    return reserve(`<a href="${escapeHtml(normalizedHref)}">${renderInline(label, repositoryUrl, resolveHref)}</a>`);
  });
  rendered = rendered.replace(/`([^`]+)`/g, (_match, code) => reserve(`<code>${escapeHtml(code)}</code>`));
  rendered = escapeHtml(rendered);
  rendered = rendered.replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");
  rendered = rendered.replace(/(^|[^*])\*([^*\n]+)\*/g, "$1<em>$2</em>");

  tokens.forEach((html, index) => {
    rendered = rendered.replaceAll(`@@LDCS_INLINE_${index}@@`, html);
  });
  return rendered;
}

function splitTableRow(line) {
  let source = line.trim();
  if (source.startsWith("|")) {
    source = source.slice(1);
  }
  if (source.endsWith("|") && !source.endsWith("\\|")) {
    source = source.slice(0, -1);
  }

  const cells = [];
  let current = "";
  let inCode = false;
  for (let index = 0; index < source.length; index += 1) {
    const character = source[index];
    if (character === "`" && source[index - 1] !== "\\") {
      inCode = !inCode;
      current += character;
      continue;
    }

    if (character === "\\" && source[index + 1] === "|") {
      current += "|";
      index += 1;
      continue;
    }

    if (character === "|" && !inCode) {
      cells.push(current.trim());
      current = "";
      continue;
    }

    current += character;
  }
  cells.push(current.trim());
  return cells;
}

function isTableDelimiter(line) {
  const cells = splitTableRow(line);
  return cells.length > 1 && cells.every((cell) => /^:?-{3,}:?$/.test(cell.replaceAll(" ", "")));
}

function tableAlignment(cell) {
  const normalized = cell.replaceAll(" ", "");
  if (normalized.startsWith(":") && normalized.endsWith(":")) {
    return "center";
  }
  return normalized.endsWith(":") ? "right" : "left";
}

function isHorizontalRule(line) {
  return /^\s{0,3}(?:(?:-\s*){3,}|(?:\*\s*){3,}|(?:_\s*){3,})$/.test(line);
}

function listMatch(line) {
  return line.match(/^\s*(?:(?<unordered>[-+*])|(?<ordered>\d+[.)]))\s+(?<content>.+)$/);
}

function startsBlock(lines, index) {
  const line = lines[index] ?? "";
  return (
    /^```/.test(line) ||
    /^#{1,6}\s+/.test(line) ||
    /^>\s?/.test(line) ||
    Boolean(listMatch(line)) ||
    isHorizontalRule(line) ||
    (line.includes("|") && isTableDelimiter(lines[index + 1] ?? ""))
  );
}

function extractMarkdownLinks(lines) {
  const links = [];
  let inFence = false;
  for (const line of lines) {
    if (/^```/.test(line)) {
      inFence = !inFence;
      continue;
    }
    if (inFence) {
      continue;
    }

    for (const match of line.matchAll(/\[[^\]]+]\(([^)]+)\)/g)) {
      links.push(match[1].trim());
    }
  }
  return links;
}

function renderToc(headings) {
  return headings
    .filter(({ displayLevel, level }) => (displayLevel ?? level) <= 3)
    .map(
      ({ displayLevel, id, level, text }) =>
        `<a class="toc-link toc-level-${displayLevel ?? level}" href="#${escapeHtml(id)}" data-toc-id="${escapeHtml(id)}">${escapeHtml(text)}</a>`,
    )
    .join("\n");
}

export function renderStudyGuide(
  markdown,
  {
    experimentHeadingAliases = {},
    experimentIds = [],
    headingIds = [],
    headingOffset = 1,
    repositoryUrl,
    resolveHref,
  },
) {
  const { body, metadata } = parseFrontMatter(markdown);
  const lines = body.split("\n");
  const slug = createSlugger();
  const headings = [];
  const html = [];
  let blockquoteCount = 0;
  let codeBlockCount = 0;
  const codeLanguages = new Map();
  let listItemCount = 0;
  let orderedListCount = 0;
  let tableCount = 0;
  let taskListItemCount = 0;
  let unorderedListCount = 0;
  let headingIndex = 0;

  for (let index = 0; index < lines.length; ) {
    const line = lines[index];
    if (!line.trim()) {
      index += 1;
      continue;
    }

    const fence = line.match(/^```([^\s`]*)\s*$/);
    if (fence) {
      const language = fence[1] || "text";
      const codeLines = [];
      index += 1;
      while (index < lines.length && !/^```\s*$/.test(lines[index])) {
        codeLines.push(lines[index]);
        index += 1;
      }
      if (index >= lines.length) {
        throw new Error(`Unclosed code fence near: ${codeLines[0] ?? "empty block"}`);
      }
      index += 1;
      codeBlockCount += 1;
      codeLanguages.set(language, (codeLanguages.get(language) ?? 0) + 1);
      html.push(
        `<figure class="code-block"><figcaption><span>${escapeHtml(language)}</span><button type="button" data-copy-code>复制代码</button></figcaption><pre><code class="language-${escapeHtml(language)}">${escapeHtml(codeLines.join("\n"))}</code></pre></figure>`,
      );
      continue;
    }

    const heading = line.match(/^(#{1,6})\s+(.+)$/);
    if (heading) {
      const level = heading[1].length;
      const text = plainText(heading[2]);
      const directExperimentId = experimentIds.find((experimentId) => text.includes(experimentId));
      const aliasedExperimentId = Object.entries(experimentHeadingAliases).find(([prefix]) =>
        text.startsWith(prefix),
      )?.[1];
      const experimentId = directExperimentId ?? aliasedExperimentId;
      const suppliedId = headingIds[headingIndex];
      const id = suppliedId ?? (experimentId
        ? slug(`experiment-${experimentId.replaceAll(".", "-")}`, true)
        : slug(text));
      const htmlLevel = Math.min(Math.max(level + headingOffset, 2), 6);
      const displayLevel = headingOffset < 0 ? htmlLevel : level;
      headings.push({ displayLevel, id, level, sourceLine: index + 1, text });
      headingIndex += 1;
      html.push(
        `<h${htmlLevel} class="guide-heading guide-heading-${displayLevel}" id="${escapeHtml(id)}" data-guide-heading><a class="heading-anchor" href="#${escapeHtml(id)}" aria-label="链接到“${escapeHtml(text)}”">#</a>${renderInline(heading[2], repositoryUrl, resolveHref)}</h${htmlLevel}>`,
      );
      index += 1;
      continue;
    }

    if (line.includes("|") && isTableDelimiter(lines[index + 1] ?? "")) {
      const headers = splitTableRow(line);
      const alignments = splitTableRow(lines[index + 1]).map(tableAlignment);
      const rows = [];
      index += 2;
      while (index < lines.length && lines[index].trim() && lines[index].includes("|")) {
        rows.push(splitTableRow(lines[index]));
        index += 1;
      }
      tableCount += 1;
      const headerHtml = headers
        .map(
          (cell, cellIndex) =>
            `<th scope="col" style="text-align:${alignments[cellIndex] ?? "left"}">${renderInline(cell, repositoryUrl, resolveHref)}</th>`,
        )
        .join("");
      const bodyHtml = rows
        .map(
          (row) =>
            `<tr>${headers
              .map(
                (_header, cellIndex) =>
                  `<td style="text-align:${alignments[cellIndex] ?? "left"}">${renderInline(row[cellIndex] ?? "", repositoryUrl, resolveHref)}</td>`,
              )
              .join("")}</tr>`,
        )
        .join("\n");
      html.push(
        `<div class="table-scroll" role="region" aria-label="可横向滚动的表格" tabindex="0"><table><thead><tr>${headerHtml}</tr></thead><tbody>${bodyHtml}</tbody></table></div>`,
      );
      continue;
    }

    if (/^>\s?/.test(line)) {
      const quoteLines = [];
      while (index < lines.length && /^>\s?/.test(lines[index])) {
        quoteLines.push(lines[index].replace(/^>\s?/, ""));
        index += 1;
      }
      blockquoteCount += 1;
      html.push(`<blockquote><p>${renderInline(quoteLines.join(" "), repositoryUrl, resolveHref)}</p></blockquote>`);
      continue;
    }

    const firstListItem = listMatch(line);
    if (firstListItem) {
      const ordered = Boolean(firstListItem.groups.ordered);
      const items = [];
      while (index < lines.length) {
        const match = listMatch(lines[index]);
        if (!match || Boolean(match.groups.ordered) !== ordered) {
          break;
        }
        items.push(match.groups.content);
        index += 1;
      }
      const tag = ordered ? "ol" : "ul";
      orderedListCount += ordered ? 1 : 0;
      unorderedListCount += ordered ? 0 : 1;
      listItemCount += items.length;
      const taskItems = ordered ? [] : items.filter((item) => /^\[[ xX]]\s+/.test(item));
      taskListItemCount += taskItems.length;
      const listClass = taskItems.length > 0 ? ' class="task-list"' : "";
      const itemsHtml = items
        .map((item) => {
          const task = ordered ? null : item.match(/^\[([ xX])]\s+(.+)$/);
          if (!task) {
            return `<li>${renderInline(item, repositoryUrl, resolveHref)}</li>`;
          }

          const checked = task[1].toLocaleLowerCase() === "x";
          const label = plainText(task[2]);
          return `<li class="task-list-item"><input type="checkbox" disabled${checked ? " checked" : ""} aria-label="${escapeHtml(label)}" /><span>${renderInline(task[2], repositoryUrl, resolveHref)}</span></li>`;
        })
        .join("");
      html.push(`<${tag}${listClass}>${itemsHtml}</${tag}>`);
      continue;
    }

    if (isHorizontalRule(line)) {
      html.push("<hr />");
      index += 1;
      continue;
    }

    const paragraphLines = [];
    while (index < lines.length && lines[index].trim() && !startsBlock(lines, index)) {
      paragraphLines.push(lines[index].trim());
      index += 1;
    }
    if (paragraphLines.length === 0) {
      throw new Error(`Unsupported Markdown block near line ${index + 1}: ${line}`);
    }
    html.push(`<p>${renderInline(paragraphLines.join(" "), repositoryUrl, resolveHref)}</p>`);
  }

  return {
    articleHtml: html.join("\n"),
    blockquoteCount,
    codeBlockCount,
    codeLanguages: Object.fromEntries([...codeLanguages].sort(([left], [right]) => left.localeCompare(right))),
    headings,
    listItemCount,
    metadata,
    orderedListCount,
    sourceLinks: extractMarkdownLinks(lines),
    tableCount,
    taskListItemCount,
    tocHtml: renderToc(headings),
    unorderedListCount,
  };
}

function markdownToPlainText(markdown) {
  const { body } = parseFrontMatter(markdown);
  return body
    .replace(/```[^\n]*\n?/g, " ")
    .replace(/\[([^\]]+)]\(([^)]+)\)/g, "$1 $2")
    .replace(/^#{1,6}\s+/gm, "")
    .replace(/^>\s?/gm, "")
    .replace(/^\s*(?:[-+*]|\d+[.)])\s+/gm, "")
    .replace(/[|*~`]/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

export { escapeHtml, markdownToPlainText, parseFrontMatter, plainText };
