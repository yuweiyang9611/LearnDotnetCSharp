const form = document.querySelector("#global-search-form");
const input = document.querySelector("#global-search-input");
const resultsTitle = document.querySelector("#results-title");
const status = document.querySelector("#search-status");
const resultsList = document.querySelector("#search-results-list");
const emptyState = document.querySelector("#search-empty");
const typeButtons = [...document.querySelectorAll("[data-search-type]")];
const validTypes = new Set(["all", "guide", "resource", "experiment"]);
let entries = [];
let activeType = "all";
let debounceTimer;
let lastStatusMessage = "正在载入站内索引…";

document.documentElement.classList.add("has-search");

function normalize(value) {
  return String(value).normalize("NFKC").toLocaleLowerCase("zh-CN");
}

function queryTokens(query) {
  return [...new Set(normalize(query).split(/\s+/).filter(Boolean))];
}

function entryType(entry) {
  if (entry.kind === "实验") return "experiment";
  const kind = String(entry.kind);
  if (kind.includes("教材") || kind.includes("章节") || kind.includes("附录") || String(entry.url).startsWith("../guide/")) {
    return "guide";
  }
  return "resource";
}

function matchEntry(entry, tokens) {
  const title = normalize(`${entry.title} ${entry.subtitle ?? ""} ${entry.id ?? ""}`);
  const text = normalize(entry.text);
  const haystack = `${title} ${text}`;
  if (!tokens.every((token) => haystack.includes(token))) return null;

  let score = 0;
  for (const token of tokens) {
    if (title === token) score += 120;
    if (title.startsWith(token)) score += 70;
    if (title.includes(token)) score += 45;
    const position = text.indexOf(token);
    if (position >= 0) score += Math.max(2, 24 - Math.floor(position / 160));
  }
  if (entry.kind === "实验" && tokens.some((token) => normalize(entry.id ?? "") === token)) score += 160;
  return { entry, score };
}

function excerptFor(entry, tokens) {
  const text = String(entry.text ?? "").replace(/\s+/g, " ").trim();
  const normalizedText = normalize(text);
  const positions = tokens.map((token) => normalizedText.indexOf(token)).filter((position) => position >= 0);
  const first = positions.length > 0 ? Math.min(...positions) : 0;
  const start = Math.max(0, first - 70);
  const end = Math.min(text.length, start + 220);
  return `${start > 0 ? "…" : ""}${text.slice(start, end).trim()}${end < text.length ? "…" : ""}`;
}

function appendHighlightedText(element, value, tokens) {
  const text = String(value);
  const escapedTokens = tokens
    .filter(Boolean)
    .sort((left, right) => right.length - left.length)
    .map((token) => token.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"));

  if (escapedTokens.length === 0) {
    element.textContent = text;
    return;
  }

  const expression = new RegExp(escapedTokens.join("|"), "giu");
  let cursor = 0;
  for (const match of text.matchAll(expression)) {
    const index = match.index ?? 0;
    if (index > cursor) element.append(document.createTextNode(text.slice(cursor, index)));
    const highlight = document.createElement("mark");
    highlight.textContent = match[0];
    element.append(highlight);
    cursor = index + match[0].length;
  }
  if (cursor < text.length) element.append(document.createTextNode(text.slice(cursor)));
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
  appendHighlightedText(link, entry.title, tokens);
  heading.append(link);

  const excerpt = document.createElement("p");
  excerpt.className = "search-result-excerpt";
  appendHighlightedText(excerpt, excerptFor(entry, tokens), tokens);

  const action = document.createElement("span");
  action.className = "search-result-action";
  action.textContent = "打开内容 →";
  action.setAttribute("aria-hidden", "true");

  article.append(meta, heading, excerpt, action);
  return article;
}

function updateStatus(message) {
  if (message === lastStatusMessage) return;
  lastStatusMessage = message;
  status.textContent = message;
}

function updateAddress(query) {
  const url = new URL(window.location.href);
  if (query) url.searchParams.set("q", query);
  else url.searchParams.delete("q");
  if (activeType === "all") url.searchParams.delete("type");
  else url.searchParams.set("type", activeType);
  history.replaceState(null, "", url);
}

function render(query, updateUrl = true) {
  const trimmed = query.trim();
  const tokens = queryTokens(trimmed);
  resultsList.replaceChildren();
  emptyState.hidden = true;
  if (updateUrl) updateAddress(trimmed);

  const typedEntries = activeType === "all" ? entries : entries.filter((entry) => entryType(entry) === activeType);
  if (tokens.length === 0) {
    resultsTitle.textContent = "等待关键词";
    updateStatus(`索引已就绪，当前类型共 ${typedEntries.length} 项内容`);
    return;
  }

  const matches = typedEntries
    .map((entry) => matchEntry(entry, tokens))
    .filter(Boolean)
    .sort((left, right) => right.score - left.score || left.entry.title.localeCompare(right.entry.title, "zh-CN"));
  const visible = matches.slice(0, 60);
  resultsTitle.textContent = `“${trimmed}”`;
  updateStatus(`“${trimmed}”找到 ${matches.length} 项内容${matches.length > visible.length ? `，显示前 ${visible.length} 项` : ""}`);
  emptyState.hidden = visible.length !== 0;
  resultsList.append(...visible.map(({ entry }) => createResultCard(entry, tokens)));
}

function renderAfterPause() {
  window.clearTimeout(debounceTimer);
  debounceTimer = window.setTimeout(() => render(input.value), 200);
}

function selectType(type, updateUrl = true) {
  activeType = validTypes.has(type) ? type : "all";
  for (const button of typeButtons) {
    button.setAttribute("aria-pressed", String(button.dataset.searchType === activeType));
  }
  render(input.value, updateUrl);
}

form.addEventListener("submit", (event) => {
  event.preventDefault();
  window.clearTimeout(debounceTimer);
  render(input.value);
});

input.addEventListener("input", renderAfterPause);

for (const example of document.querySelectorAll("[data-search-example]")) {
  example.addEventListener("click", () => {
    window.clearTimeout(debounceTimer);
    input.value = example.dataset.searchExample;
    render(input.value);
    input.focus();
  });
}

for (const button of typeButtons) {
  button.addEventListener("click", () => selectType(button.dataset.searchType));
}

document.addEventListener("keydown", (event) => {
  if (event.key !== "/" || event.ctrlKey || event.metaKey || event.altKey) return;
  const activeElement = document.activeElement;
  const isTyping = activeElement instanceof HTMLElement
    && (activeElement.matches("input, textarea, select") || activeElement.isContentEditable);
  if (isTyping) return;
  event.preventDefault();
  input.focus();
});

try {
  const response = await fetch("../search-index.json");
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  const payload = await response.json();
  if (!Array.isArray(payload.entries)) throw new Error("Invalid search index");
  entries = payload.entries;
  const parameters = new URLSearchParams(window.location.search);
  const initialQuery = parameters.get("q") ?? "";
  input.value = initialQuery;
  selectType(parameters.get("type") ?? "all", false);
} catch {
  updateStatus("搜索索引暂时无法载入，请从教材目录继续浏览。");
  input.disabled = true;
  form.querySelector("button").disabled = true;
  for (const button of typeButtons) button.disabled = true;
}
