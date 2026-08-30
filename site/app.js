import {
  categories,
  experiments,
  learningStages,
  repositoryUrl,
  resources,
} from "./catalog-data.js";
import { experimentGuideUrls } from "./guide-routes.js";

document.documentElement.classList.add("home-js");

const storageKey = "learn-dotnet-csharp-progress-v1";
const appProject = "src/LearnDotnetCSharp.App";
const validExperimentIds = new Set(experiments.map(({ id }) => id));

const elements = {
  catalogCount: document.querySelector("#catalog-count"),
  catalogFilters: document.querySelector("#catalog-filters"),
  continueAction: document.querySelector("#continue-action"),
  emptyState: document.querySelector("#empty-state"),
  experimentGrid: document.querySelector("#experiment-grid"),
  filterSummary: document.querySelector("#filter-summary"),
  pathGrid: document.querySelector("#path-grid"),
  progressBar: document.querySelector("#progress-bar"),
  progressCount: document.querySelector("#progress-count"),
  progressPercent: document.querySelector("#progress-percent"),
  resetProgress: document.querySelector("#reset-progress"),
  resourcesGrid: document.querySelector("#resources-grid"),
  search: document.querySelector("#catalog-search"),
  toast: document.querySelector("#toast"),
};

let completedIds = loadProgress();
let activeCategories = null;
let activeFilterLabel = "全部主题";
let searchTerm = "";
let toastTimer;

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function loadProgress() {
  try {
    const stored = JSON.parse(localStorage.getItem(storageKey) ?? "[]");
    if (!Array.isArray(stored)) {
      return new Set();
    }

    return new Set(stored.filter((id) => validExperimentIds.has(id)));
  } catch {
    return new Set();
  }
}

function saveProgress() {
  try {
    localStorage.setItem(storageKey, JSON.stringify([...completedIds]));
  } catch {
    showToast("浏览器未允许保存进度");
  }
}

function showToast(message) {
  window.clearTimeout(toastTimer);
  elements.toast.textContent = message;
  elements.toast.classList.add("is-visible");
  toastTimer = window.setTimeout(() => elements.toast.classList.remove("is-visible"), 2200);
}

function getStageProgress(stage) {
  const stageExperiments = experiments.filter(({ category }) => stage.categories.includes(category));
  return {
    completed: stageExperiments.filter(({ id }) => completedIds.has(id)).length,
    total: stageExperiments.length,
  };
}

function renderLearningStages() {
  elements.pathGrid.innerHTML = learningStages
    .map((stage) => {
      const progress = getStageProgress(stage);
      const percent = progress.total === 0 ? 0 : Math.round((progress.completed / progress.total) * 100);
      return `
        <article class="path-card${percent === 100 ? " is-complete" : ""}">
          <div class="path-card-topline">
            <span class="step-number">${stage.number}</span>
            <span class="stage-progress">${progress.completed}/${progress.total}</span>
          </div>
          <p>${escapeHtml(stage.label)}</p>
          <h3>${escapeHtml(stage.title)}</h3>
          <p class="path-description">${escapeHtml(stage.description)}</p>
          <div class="mini-progress" aria-label="${escapeHtml(stage.label)}完成 ${percent}%">
            <span style="width: ${percent}%"></span>
          </div>
          <button class="path-action" type="button" data-stage="${stage.number}">
            查看本阶段 <span aria-hidden="true">→</span>
          </button>
        </article>`;
    })
    .join("");
}

function renderFilters() {
  const filterEntries = Object.entries(categories);
  elements.catalogFilters.innerHTML = [
    `<button class="filter-chip is-active" type="button" data-category="all" aria-pressed="true">全部 <span>${experiments.length}</span></button>`,
    ...filterEntries.map(([key, category]) => {
      const count = experiments.filter((experiment) => experiment.category === key).length;
      return `<button class="filter-chip" type="button" data-category="${key}" aria-pressed="false">${escapeHtml(category.label)} <span>${count}</span></button>`;
    }),
  ].join("");
}

function matchesFilters(experiment) {
  const categoryMatch = !activeCategories || activeCategories.includes(experiment.category);
  const searchable = `${experiment.id} ${experiment.title} ${experiment.summary} ${categories[experiment.category].label}`
    .toLocaleLowerCase("zh-CN");
  return categoryMatch && searchable.includes(searchTerm);
}

function renderCatalog() {
  const visibleExperiments = experiments.filter(matchesFilters);
  elements.catalogCount.textContent = String(visibleExperiments.length);
  elements.filterSummary.textContent = searchTerm
    ? `${activeFilterLabel} · 搜索“${elements.search.value.trim()}”`
    : activeFilterLabel;
  elements.emptyState.hidden = visibleExperiments.length !== 0;

  elements.experimentGrid.innerHTML = visibleExperiments
    .map((experiment) => {
      const category = categories[experiment.category];
      const completed = completedIds.has(experiment.id);
      const command = `dotnet run --project ${appProject} -- run ${experiment.id}`;
      const sourceUrl = `${repositoryUrl}/blob/main/${experiment.source}`;
      return `
        <article class="experiment-card${completed ? " is-complete" : ""}" id="experiment-${experiment.id}">
          <div class="experiment-topline">
            <span class="category-badge tone-${category.tone}">${escapeHtml(category.label)}</span>
            <button
              class="complete-toggle"
              type="button"
              data-complete-id="${experiment.id}"
              aria-pressed="${completed}"
              aria-label="${completed ? "取消完成" : "标记完成"}：${escapeHtml(experiment.title)}"
            >
              <span aria-hidden="true">${completed ? "✓" : ""}</span>
              ${completed ? "已完成" : "标记完成"}
            </button>
          </div>
          <code class="experiment-id">${experiment.id}</code>
          <h3>${escapeHtml(experiment.title)}</h3>
          <p>${escapeHtml(experiment.summary)}</p>
          <div class="experiment-actions">
            <a href="${experimentGuideUrls[experiment.id]}">阅读讲解 <span aria-hidden="true">→</span></a>
            <a href="${sourceUrl}">查看源码 <span aria-hidden="true">↗</span></a>
            <button type="button" data-copy="${escapeHtml(command)}">复制运行命令</button>
          </div>
        </article>`;
    })
    .join("");
}

function renderProgress() {
  const completed = completedIds.size;
  const percent = Math.round((completed / experiments.length) * 100);
  elements.progressCount.textContent = `${completed} / ${experiments.length}`;
  elements.progressPercent.textContent = `${percent}%`;
  elements.progressBar.style.width = `${percent}%`;
  elements.progressBar.parentElement.setAttribute("aria-valuenow", String(percent));

  const nextExperiment = experiments.find(({ id }) => !completedIds.has(id));
  if (nextExperiment) {
    elements.continueAction.textContent = completed === 0 ? "从第一个实验开始" : "继续下一个实验";
    elements.continueAction.dataset.experimentId = nextExperiment.id;
  } else {
    elements.continueAction.textContent = "全部完成，再看一遍";
    elements.continueAction.dataset.experimentId = experiments[0].id;
  }

  elements.resetProgress.disabled = completed === 0;
}

function renderResources() {
  elements.resourcesGrid.innerHTML = resources
    .map(
      (resource) => `
        <a class="resource-card" href="${resource.href}"${resource.download ? " download" : ""}>
          <span>${escapeHtml(resource.type)}</span>
          <h3>${escapeHtml(resource.title)}</h3>
          <p>${escapeHtml(resource.description)}</p>
          <strong>${escapeHtml(resource.action)} <span aria-hidden="true">${resource.download ? "↓" : "→"}</span></strong>
        </a>`,
    )
    .join("");
}

function renderAll() {
  renderLearningStages();
  renderCatalog();
  renderProgress();
}

function setActiveCategories(categoryKeys, label, categoryKey = null) {
  activeCategories = categoryKeys;
  activeFilterLabel = label;
  elements.catalogFilters.querySelectorAll("[data-category]").forEach((button) => {
    const isActive = categoryKey
      ? categoryKey === button.dataset.category
      : activeCategories === null && button.dataset.category === "all";
    button.classList.toggle("is-active", isActive);
    button.setAttribute("aria-pressed", String(isActive));
  });
  renderCatalog();
}

async function copyText(text, successMessage = "已复制运行命令") {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    const input = document.createElement("textarea");
    input.value = text;
    input.style.position = "fixed";
    input.style.opacity = "0";
    document.body.append(input);
    input.select();
    document.execCommand("copy");
    input.remove();
  }
  showToast(successMessage);
}

elements.catalogFilters.addEventListener("click", (event) => {
  const button = event.target.closest("[data-category]");
  if (!button) {
    return;
  }

  const categoryKey = button.dataset.category;
  if (categoryKey === "all") {
    setActiveCategories(null, "全部主题", "all");
    return;
  }

  setActiveCategories([categoryKey], categories[categoryKey].label, categoryKey);
});

elements.pathGrid.addEventListener("click", (event) => {
  const button = event.target.closest("[data-stage]");
  if (!button) {
    return;
  }

  const stage = learningStages.find(({ number }) => number === button.dataset.stage);
  if (!stage) {
    return;
  }

  elements.search.value = "";
  searchTerm = "";
  setActiveCategories(stage.categories, `阶段 ${stage.number} · ${stage.label}`);
  document.querySelector("#catalog").scrollIntoView({ behavior: "smooth", block: "start" });
});

elements.search.addEventListener("input", () => {
  searchTerm = elements.search.value.trim().toLocaleLowerCase("zh-CN");
  renderCatalog();
});

elements.experimentGrid.addEventListener("click", (event) => {
  const completeButton = event.target.closest("[data-complete-id]");
  if (completeButton) {
    const id = completeButton.dataset.completeId;
    if (completedIds.has(id)) {
      completedIds.delete(id);
    } else {
      completedIds.add(id);
    }
    saveProgress();
    renderAll();
    requestAnimationFrame(() => {
      const restoredButton = [...elements.experimentGrid.querySelectorAll("[data-complete-id]")]
        .find((button) => button.dataset.completeId === id);
      restoredButton?.focus();
    });
    return;
  }

  const copyButton = event.target.closest("[data-copy]");
  if (copyButton) {
    copyText(copyButton.dataset.copy);
  }
});

document.addEventListener("click", (event) => {
  const copyButton = event.target.closest("[data-global-copy]");
  if (copyButton) {
    copyText(copyButton.dataset.globalCopy, "快速开始命令已复制");
  }
});

elements.continueAction.addEventListener("click", () => {
  const id = elements.continueAction.dataset.experimentId;
  elements.search.value = "";
  searchTerm = "";
  setActiveCategories(null, "全部主题", "all");
  requestAnimationFrame(() => {
    document.querySelector(`#experiment-${CSS.escape(id)}`)?.scrollIntoView({ behavior: "smooth", block: "center" });
  });
});

elements.resetProgress.addEventListener("click", () => {
  if (!window.confirm("确认清空这台设备上的全部学习进度吗？")) {
    return;
  }

  completedIds = new Set();
  saveProgress();
  renderAll();
  showToast("学习进度已清空");
});

renderFilters();
renderResources();
renderAll();
