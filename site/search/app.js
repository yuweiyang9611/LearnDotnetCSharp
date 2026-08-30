const form = document.querySelector("#global-search-form");
const input = document.querySelector("#global-search-input");
const resultsTitle = document.querySelector("#results-title");
const status = document.querySelector("#search-status");
const resultsList = document.querySelector("#search-results-list");
const emptyState = document.querySelector("#search-empty");
let entries = [];

function normalize(value) {
  return String(value).normalize("NFKC").toLocaleLowerCase("zh-CN");
}

function queryTokens(query) {
  return [...new Set(normalize(query).split(/\s+/).filter(Boolean))];
}

function matchEntry(entry, tokens) {
  const title = normalize(`${entry.title} ${entry.subtitle ?? ""}`);
  const text = normalize(entry.text);
  const haystack = `${title} ${text}`;
  if (!tokens.every((token) => haystack.includes(token))) {
    return null;
  }

  let score = 0;
  for (const token of tokens) {
    if (title === token) score += 120;
    if (title.startsWith(token)) score += 70;
    if (title.includes(token)) score += 45;
    const position = text.indexOf(token);
    if (position >= 0) score += Math.max(2, 24 - Math.floor(position / 160));
  }
  if (entry.kind === "实验" && tokens.some((token) => normalize(entry.id ?? "") === token)) {
    score += 160;
  }
  return { entry, score };
}

function excerptFor(entry, tokens) {
  const text = entry.text.replace(/\s+/g, " ").trim();
  const normalizedText = normalize(text);
  const positions = tokens.map((token) => normalizedText.indexOf(token)).filter((position) => position >= 0);
  const first = positions.length > 0 ? Math.min(...positions) : 0;
  const start = Math.max(0, first - 70);
  const end = Math.min(text.length, start + 220);
  return `${start > 0 ? "…" : ""}${text.slice(start, end).trim()}${end < text.length ? "…" : ""}`;
}

function createResultCard(entry, tokens) {
  const article = document.createElement("article");
  article.className = "search-result-card";

  const meta = document.createElement("p");
  meta.className = "search-result-meta";
  meta.textContent = entry.kind + (entry.subtitle ? ` · ${entry.subtitle}` : "");

  const heading = document.createElement("h3");
  const link = document.createElement("a");
  link.href = entry.url;
  link.textContent = entry.title;
  heading.append(link);

  const excerpt = document.createElement("p");
  excerpt.className = "search-result-excerpt";
  excerpt.textContent = excerptFor(entry, tokens);

  const action = document.createElement("span");
  action.className = "search-result-action";
  action.textContent = "打开内容 →";

  article.append(meta, heading, excerpt, action);
  return article;
}

function render(query, updateAddress = true) {
  const trimmed = query.trim();
  const tokens = queryTokens(trimmed);
  resultsList.replaceChildren();
  emptyState.hidden = true;

  if (updateAddress) {
    const url = new URL(window.location.href);
    if (trimmed) url.searchParams.set("q", trimmed);
    else url.searchParams.delete("q");
    history.replaceState(null, "", url);
  }

  if (tokens.length === 0) {
    resultsTitle.textContent = "等待关键词";
    status.textContent = `索引已就绪，共 ${entries.length} 项内容`;
    return;
  }

  const matches = entries
    .map((entry) => matchEntry(entry, tokens))
    .filter(Boolean)
    .sort((left, right) => right.score - left.score || left.entry.title.localeCompare(right.entry.title, "zh-CN"));
  const visible = matches.slice(0, 60);
  resultsTitle.textContent = `“${trimmed}”`;
  status.textContent = `找到 ${matches.length} 项内容${matches.length > visible.length ? `，显示前 ${visible.length} 项` : ""}`;
  emptyState.hidden = visible.length !== 0;
  resultsList.append(...visible.map(({ entry }) => createResultCard(entry, tokens)));
}

form.addEventListener("submit", (event) => {
  event.preventDefault();
  render(input.value);
});
input.addEventListener("input", () => render(input.value));

for (const example of document.querySelectorAll("[data-search-example]")) {
  example.addEventListener("click", () => {
    input.value = example.dataset.searchExample;
    render(input.value);
    input.focus();
  });
}

document.addEventListener("keydown", (event) => {
  if (event.key === "/" && document.activeElement !== input) {
    event.preventDefault();
    input.focus();
  }
});

try {
  const response = await fetch("../search-index.json");
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  const payload = await response.json();
  if (!Array.isArray(payload.entries)) throw new Error("Invalid search index");
  entries = payload.entries;
  const initialQuery = new URLSearchParams(window.location.search).get("q") ?? "";
  input.value = initialQuery;
  render(initialQuery, false);
} catch {
  status.textContent = "搜索索引暂时无法载入，请从教材目录继续浏览。";
  input.disabled = true;
  form.querySelector("button").disabled = true;
}
