import { progress } from "../progress-store.js";
import { initializeProgressUI } from "../progress-ui.js";
import { copyText } from "../clipboard.js";
import { experiments, learningStages } from "../catalog-data.js";
document.documentElement.classList.add("guide-js");

// The band at the top of a chapter carries the labs-done share of the chapter's route.
function renderRouteBand() {
  const band = document.querySelector("[data-route-band]");
  const stage = band && learningStages.find(({ number }) => number === band.dataset.routeBand);
  if (!stage) return;
  const members = experiments.filter(({ category }) => stage.categories.includes(category));
  const completed = new Set(progress.completedIds());
  const done = members.filter(({ id }) => completed.has(id)).length;
  band.querySelector("[data-route-state]").textContent = done === members.length ? `${done} / ${members.length} 已完成` : `${done} / ${members.length} 实验`;
  const bar = band.querySelector("[data-route-bar]");
  bar.style.setProperty("--nb-fraction", String(members.length ? done / members.length : 0));
  bar.setAttribute("aria-valuenow", String(done));
}

const tocLinks = [...document.querySelectorAll("[data-toc-id]")];
const headings = [...document.querySelectorAll("[data-guide-heading]")];
const searchInputs = [...document.querySelectorAll("[data-toc-search]")];
const backToTop = document.querySelector("#back-to-top");
const toast = document.querySelector("#guide-toast");
let toastTimer;
let scrollFrame;

function showToast(message) {
  if (!toast) {
    return;
  }
  window.clearTimeout(toastTimer);
  toast.textContent = message;
  toast.classList.add("is-visible");
  toastTimer = window.setTimeout(() => toast.classList.remove("is-visible"), 2200);
}

// Only book units are reading positions. A resource page would otherwise clear the place the owner stopped.
const isBookUnit = document.body.classList.contains("chapter-page");

function saveReadingPosition(id) {
  if (!isBookUnit) return;
  const heading = document.getElementById(id);
  const title = heading ? [...heading.childNodes].filter((node) => !node.classList?.contains("heading-anchor")).map((node) => node.textContent).join("").trim() : "";
  progress.setReading({ title: title || document.title, url: `${window.location.pathname}#${encodeURIComponent(id)}` });
}

function setActiveHeading(id, updateAddress = false) {
  if (!id || !document.getElementById(id)) {
    return;
  }

  tocLinks.forEach((link) => {
    const isActive = link.dataset.tocId === id;
    link.classList.toggle("is-active", isActive);
    if (isActive) {
      link.setAttribute("aria-current", "location");
    } else {
      link.removeAttribute("aria-current");
    }
  });
  saveReadingPosition(id);

  let currentHash = "";
  try {
    currentHash = decodeURIComponent(window.location.hash.slice(1));
  } catch {
    currentHash = window.location.hash.slice(1);
  }
  if (updateAddress && currentHash !== id) {
    history.replaceState(null, "", `#${encodeURIComponent(id)}`);
  }
}

function updateScrollState() {
  backToTop?.classList.toggle("is-visible", window.scrollY > 900);
  scrollFrame = undefined;
}

function scheduleScrollUpdate() {
  if (scrollFrame === undefined) {
    scrollFrame = window.requestAnimationFrame(updateScrollState);
  }
}


function initializeAssessment() {
  const section = document.querySelector("[data-chapter-assessment]");
  if (!section) return;
  const taskId = section.dataset.taskId;
  const taskRevision = section.dataset.taskRevision;
  const stepInputs = [...section.querySelectorAll("[data-assessment-step]")];
  const criterionInputs = [...section.querySelectorAll("[data-assessment-criterion]")];
  const notes = section.querySelector("[data-assessment-notes]");
  const completeButton = section.querySelector("[data-assessment-complete]");
  const stateLabel = section.querySelector("[data-assessment-state]");
  const status = section.querySelector("[data-assessment-status]");
  const savedState = progress.assessment(taskId, taskRevision);
  let state = savedState?.revision === taskRevision
    ? savedState
    : { completed: false, criteria: [], notes: "", revision: taskRevision, steps: [] };

  stepInputs.forEach((input, index) => { input.checked = Boolean(state.steps?.[index]); });
  criterionInputs.forEach((input, index) => { input.checked = Boolean(state.criteria?.[index]); });
  notes.value = typeof state.notes === "string" ? state.notes : "";

  function readyToComplete() {
    return stepInputs.every((input) => input.checked) &&
      criterionInputs.every((input) => input.checked) &&
      notes.value.trim().length >= 12;
  }

  function renderAssessmentState() {
    const ready = readyToComplete();
    section.classList.toggle("is-complete", Boolean(state.completed));
    // Each ticked step or criterion turns its stretch of the record line solid.
    for (const input of [...stepInputs, ...criterionInputs]) {
      const row = input.closest("li");
      if (input.checked) row.dataset.record = "done";
      else row.removeAttribute("data-record");
    }
    status.classList.toggle("is-unsaved", !progress.saved);
    stateLabel.textContent = state.completed ? "已完成验收" : ready ? "证据齐全" : "尚未验收";
    completeButton.disabled = !ready && !state.completed;
    completeButton.textContent = state.completed ? "重新验收" : "标记为已验收";
    status.textContent = !progress.saved
      ? "尚未保存：请重试保存或导出备份保留当前笔记。"
      : state.completed
      ? "本章验收已保存在当前浏览器；修改任一项会重新打开验收。"
      : ready
        ? "步骤、标准和证据记录已齐全，可以完成本章验收。"
        : "完成全部步骤与标准，并填写至少 12 个字符的证据记录。";
  }

  function persist(completed = false) {
    state = {
      completed,
      criteria: criterionInputs.map((input) => input.checked),
      notes: notes.value,
      revision: taskRevision,
      steps: stepInputs.map((input) => input.checked),
    };
    progress.setAssessment(taskId, state);
    renderAssessmentState();
  }

  [...stepInputs, ...criterionInputs].forEach((input) => input.addEventListener("change", () => persist(false)));
  notes.addEventListener("input", () => persist(false));
  completeButton.addEventListener("click", () => persist(state.completed ? false : readyToComplete()));
  window.addEventListener("learning-data-change", () => {
    const latest = progress.assessment(taskId, taskRevision);
    if (latest) {
      state = latest;
      stepInputs.forEach((input, index) => { input.checked = Boolean(state.steps[index]); });
      criterionInputs.forEach((input, index) => { input.checked = Boolean(state.criteria[index]); });
      if (notes.value !== state.notes) notes.value = state.notes;
    }
    renderAssessmentState();
  });
  renderAssessmentState();
}

document.addEventListener("click", async (event) => {
  const taskCommandButton = event.target.closest("[data-copy-task-command]");
  if (taskCommandButton) {
    if (!await copyText(taskCommandButton.dataset.copyTaskCommand)) { showToast("复制失败，请手动选择命令复制"); return; }
    const originalText = taskCommandButton.textContent;
    taskCommandButton.textContent = "已复制";
    showToast("验收命令已复制");
    window.setTimeout(() => { taskCommandButton.textContent = originalText; }, 1600);
    return;
  }

  const copyButton = event.target.closest("[data-copy-code]");
  if (copyButton) {
    const code = copyButton.closest(".code-block")?.querySelector("pre code")?.textContent;
    if (code) {
      if (!await copyText(code)) { showToast("复制失败，请手动选择代码复制"); return; }
      const originalText = copyButton.textContent;
      copyButton.textContent = "已复制";
      showToast("代码已复制");
      window.setTimeout(() => {
        copyButton.textContent = originalText;
      }, 1600);
    }
    return;
  }

  const tocLink = event.target.closest("[data-toc-id]");
  if (tocLink) {
    setActiveHeading(tocLink.dataset.tocId);
    const mobileToc = tocLink.closest("details");
    if (mobileToc) {
      mobileToc.open = false;
    }
  }
});

for (const input of searchInputs) {
  input.addEventListener("input", () => {
    const scope = input.closest(".guide-sidebar, .mobile-toc");
    if (!scope) {
      return;
    }
    const scopedLinks = [...scope.querySelectorAll("[data-toc-id]")];
    const searchTerm = input.value.trim().normalize("NFKC").toLocaleLowerCase("zh-CN");
    let visibleCount = 0;
    for (const link of scopedLinks) {
      const visible = !searchTerm || link.textContent.normalize("NFKC").toLocaleLowerCase("zh-CN").includes(searchTerm);
      link.hidden = !visible;
      visibleCount += visible ? 1 : 0;
    }

    const status = scope.querySelector("[data-toc-search-status]");
    if (status) {
      status.textContent = searchTerm ? `找到 ${visibleCount} 个目录项` : "";
    }
  });
}

if ("IntersectionObserver" in window) {
  const observer = new IntersectionObserver(
    (entries) => {
      const visible = entries.filter(({ isIntersecting }) => isIntersecting);
      if (visible.length === 0) {
        return;
      }
      visible.sort((left, right) => left.boundingClientRect.top - right.boundingClientRect.top);
      setActiveHeading(visible[0].target.id, true);
    },
    { rootMargin: "-12% 0px -76% 0px", threshold: 0 },
  );
  headings.forEach((heading) => observer.observe(heading));
}

for (const details of document.querySelectorAll(".mobile-toc")) {
  details.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && details.open) {
      details.open = false;
      details.querySelector("summary")?.focus();
    }
  });
}

backToTop?.addEventListener("click", () => {
  const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  window.scrollTo({ top: 0, behavior: reducedMotion ? "auto" : "smooth" });
});

window.addEventListener("scroll", scheduleScrollUpdate, { passive: true });
window.addEventListener("resize", scheduleScrollUpdate);
updateScrollState();
initializeProgressUI(document.querySelector("[data-learning-backup]"));
initializeAssessment();
renderRouteBand();
window.addEventListener("learning-data-change", renderRouteBand);

if (window.location.hash) {
  try {
    setActiveHeading(decodeURIComponent(window.location.hash.slice(1)));
  } catch {
    setActiveHeading(window.location.hash.slice(1));
  }
} else if (headings[0]) {
  saveReadingPosition(headings[0].id);
}
