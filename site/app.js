import { progress } from "./progress-store.js";
import { initializeProgressUI } from "./progress-ui.js";
import { copyText as copyToClipboard } from "./clipboard.js";
import { categories, experiments, learningStages } from "./catalog-data.js";
import { experimentGuideUrls } from "./guide-routes.js";
import { tasks } from "./progress-catalog.js";

document.documentElement.classList.add("home-js");

const appProject = "src/LearnDotnetCSharp.App";
const command = (id) => `dotnet run --project ${appProject} -- run ${id}`;

const elements = {
  assessmentCount: document.querySelector("#assessment-count"),
  catalogCount: document.querySelector("#catalog-count"),
  catalogFilters: document.querySelector("#catalog-filters"),
  continueLink: document.querySelector("#continue-learning"),
  emptyState: document.querySelector("#empty-state"),
  experimentGrid: document.querySelector("#experiment-grid"),
  filterSummary: document.querySelector("#filter-summary"),
  nextCommand: document.querySelector("#next-lab-command"),
  nextCopy: document.querySelector("#next-lab-copy"),
  nextLink: document.querySelector("#next-lab-link"),
  nextSummary: document.querySelector("#next-lab-summary"),
  nextTitle: document.querySelector("#next-lab-title"),
  pathGrid: document.querySelector("#path-grid"),
  progressBar: document.querySelector("#progress-bar"),
  progressCount: document.querySelector("#progress-count"),
  progressTrack: document.querySelector("#progress-track"),
  resetProgress: document.querySelector("#reset-progress"),
  search: document.querySelector("#catalog-search"),
  toast: document.querySelector("#toast"),
};

const rows = new Map(experiments.map(({ id }) => [id, document.getElementById(`experiment-${id}`)]));
let completedIds = loadProgress();
let activeCategories = null;
let activeFilterLabel = "全部主题";
let searchTerm = "";
let toastTimer;

function loadProgress() { return new Set(progress.completedIds()); }
function saveProgress() { return progress.setExperiments([...completedIds]); }

function showToast(message) {
  window.clearTimeout(toastTimer);
  elements.toast.textContent = message;
  elements.toast.classList.add("is-visible");
  toastTimer = window.setTimeout(() => elements.toast.classList.remove("is-visible"), 2200);
}

function verifiedChapters() {
  const { assessments } = progress.snapshot();
  return Object.entries(tasks).filter(([id, task]) => assessments[id]?.completed && assessments[id].revision === task.revision).length;
}

// Solid green reaches as far as the record; a row with no record keeps only the dashed line.
function setRecord(row, fraction) {
  row.style.setProperty("--nb-fraction", String(fraction));
  if (fraction >= 1) row.dataset.record = "done";
  else row.removeAttribute("data-record");
}

function renderDesk(nextExperiment) {
  const completed = completedIds.size;
  const total = experiments.length;
  const chapters = Object.keys(tasks).length;
  elements.progressCount.textContent = `${completed} / ${total}`;
  elements.assessmentCount.textContent = `${verifiedChapters()} / ${chapters}`;
  elements.progressBar.style.transform = `scaleX(${completed / total})`;
  elements.progressTrack.setAttribute("aria-valuenow", String(completed));

  const reading = progress.snapshot().lastReading;
  const link = elements.continueLink;
  if (reading) {
    link.href = reading.url;
    // Older saves kept the heading's # anchor in the title.
    link.textContent = `继续阅读：${reading.title.replace(/^#\s*/, "")}`;
  } else if (completed > 0) {
    const target = nextExperiment ?? experiments[0];
    link.href = experimentGuideUrls[target.id];
    link.textContent = nextExperiment ? `下一个实验：${target.title}` : "全部实验已完成，从头复习";
  } else {
    link.href = "./guide/preface/";
    link.textContent = "从前言开始阅读";
  }
  link.classList.toggle("has-record", Boolean(reading) || completed > 0);

  const lab = nextExperiment ?? experiments[0];
  elements.nextTitle.textContent = !nextExperiment ? "全部实验已完成" : completed === 0 ? "先跑第一个实验" : "下一个实验";
  elements.nextLink.href = experimentGuideUrls[lab.id];
  elements.nextLink.textContent = lab.title;
  elements.nextSummary.textContent = lab.summary;
  elements.nextCommand.textContent = command(lab.id);
  elements.nextCopy.dataset.globalCopy = command(lab.id);
}

function renderStages() {
  for (const stage of learningStages) {
    const row = elements.pathGrid.querySelector(`[data-stage-row="${stage.number}"]`);
    if (!row) continue;
    const members = experiments.filter(({ category }) => stage.categories.includes(category));
    const done = members.filter(({ id }) => completedIds.has(id)).length;
    setRecord(row, members.length ? done / members.length : 0);
    const state = row.querySelector("[data-stage-state]");
    state.textContent = done === members.length ? `${done} / ${members.length} 已完成` : `${done} / ${members.length} 个实验`;
    state.classList.toggle("has-record", done > 0);
  }
}

function renderExperiments(nextExperiment) {
  const stoppedId = completedIds.size > 0 ? nextExperiment?.id : null;
  for (const [id, row] of rows) {
    const done = completedIds.has(id);
    setRecord(row, done ? 1 : 0);
    row.querySelector("[data-complete-id]").checked = done;
    const stopped = id === stoppedId;
    if (stopped) row.setAttribute("aria-current", "true");
    else row.removeAttribute("aria-current");
    row.querySelector(".nb-stopped").hidden = !stopped;
  }
}

function render() {
  const nextExperiment = experiments.find(({ id }) => !completedIds.has(id));
  renderDesk(nextExperiment);
  renderStages();
  renderExperiments(nextExperiment);
  elements.resetProgress.disabled = completedIds.size === 0;
}

function matchesFilters(experiment) {
  const categoryMatch = !activeCategories || activeCategories.includes(experiment.category);
  const searchable = `${experiment.id} ${experiment.title} ${experiment.summary} ${categories[experiment.category].label}`
    .toLocaleLowerCase("zh-CN");
  return categoryMatch && searchable.includes(searchTerm);
}

function applyFilters() {
  let visible = 0;
  for (const experiment of experiments) {
    const shown = matchesFilters(experiment);
    rows.get(experiment.id).hidden = !shown;
    visible += shown ? 1 : 0;
  }
  elements.catalogCount.textContent = String(visible);
  elements.filterSummary.textContent = searchTerm
    ? `${activeFilterLabel} · 搜索“${elements.search.value.trim()}”`
    : activeFilterLabel;
  elements.emptyState.hidden = visible !== 0;
}

function setActiveCategories(categoryKeys, label, categoryKey = null) {
  activeCategories = categoryKeys;
  activeFilterLabel = label;
  elements.catalogFilters.querySelectorAll("[data-category]").forEach((button) => {
    const isActive = categoryKey
      ? categoryKey === button.dataset.category
      : activeCategories === null && button.dataset.category === "all";
    button.setAttribute("aria-pressed", String(isActive));
  });
  applyFilters();
}

async function copyText(text, button) {
  const copied = await copyToClipboard(text);
  showToast(copied ? "已复制运行命令" : "复制失败，请手动选择命令复制");
  if (copied && button) {
    const original = button.textContent;
    button.textContent = "已复制";
    window.setTimeout(() => { button.textContent = original; }, 1600);
  }
}

elements.catalogFilters.addEventListener("click", (event) => {
  const button = event.target.closest("[data-category]");
  if (!button) return;
  const categoryKey = button.dataset.category;
  if (categoryKey === "all") setActiveCategories(null, "全部主题", "all");
  else setActiveCategories([categoryKey], categories[categoryKey].label, categoryKey);
});

// A stage title is a link to the catalog; with scripts it also narrows the catalog to that stage.
elements.pathGrid.addEventListener("click", (event) => {
  const link = event.target.closest("[data-stage]");
  const stage = link && learningStages.find(({ number }) => number === link.dataset.stage);
  if (!stage) return;
  elements.search.value = "";
  searchTerm = "";
  setActiveCategories(stage.categories, `阶段 ${stage.number} · ${stage.label}`);
});

elements.search.addEventListener("input", () => {
  searchTerm = elements.search.value.trim().toLocaleLowerCase("zh-CN");
  applyFilters();
});

elements.experimentGrid.addEventListener("change", (event) => {
  const box = event.target.closest("[data-complete-id]");
  if (!box) return;
  if (box.checked) completedIds.add(box.dataset.completeId);
  else completedIds.delete(box.dataset.completeId);
  saveProgress();
  render();
});

elements.experimentGrid.addEventListener("click", (event) => {
  const copyButton = event.target.closest("[data-copy]");
  if (copyButton) copyText(copyButton.dataset.copy, copyButton);
});

document.addEventListener("click", (event) => {
  const copyButton = event.target.closest("[data-global-copy]");
  if (copyButton) copyText(copyButton.dataset.globalCopy, copyButton);
});

document.addEventListener("keydown", (event) => {
  if (event.key !== "/" || event.ctrlKey || event.metaKey || event.altKey) return;
  const active = document.activeElement;
  if (active instanceof HTMLElement && (active.matches("input, textarea, select") || active.isContentEditable)) return;
  if (document.querySelector("dialog[open]")) return;
  event.preventDefault();
  elements.search.focus();
});

elements.resetProgress.addEventListener("click", () => {
  if (!window.confirm("确认清空实验完成记录吗？章节笔记会保留。")) return;
  completedIds = new Set();
  const saved = saveProgress();
  render();
  showToast(saved ? "实验完成记录已清空" : "清空操作尚未保存，请重试");
});

render();
applyFilters();

initializeProgressUI(document.querySelector("[data-learning-backup]"));
window.addEventListener("learning-data-change", () => { completedIds = loadProgress(); render(); });
