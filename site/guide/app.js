import { progress } from "../progress-store.js";
import { initializeProgressUI } from "../progress-ui.js";
import { copyText } from "../clipboard.js";
import { experiments, learningStages } from "../catalog-data.js";
import { chapters } from "../progress-catalog.js";
import { chapterSteps } from "../lesson-loop.js";
document.documentElement.classList.add("guide-js");

// The loop band: five steps read from the record, never from a tick on the band itself. The chapter's shape
// (its labs, its quiz) comes from the generated catalog; the states follow lesson-loop.js.
const stepsBand = document.querySelector("[data-lesson-steps]");
const bandChapter = stepsBand ? chapters.find(({ id }) => id === stepsBand.dataset.lessonSteps) : null;

function renderLessonSteps() {
  if (!stepsBand || !bandChapter) return;
  for (const { step, state, count } of chapterSteps(progress.snapshot(), bandChapter)) {
    const item = stepsBand.querySelector(`[data-step="${step}"]`);
    if (!item) continue;
    item.setAttribute("data-state", state);
    const counter = item.querySelector(".nb-steps-count");
    if (counter && (step === "code" || step === "run")) {
      counter.textContent = count;
      counter.hidden = !count;
    }
  }
}

// What the reader did on the page: a panel tab clicked, a run finished, a command copied (lab-source.js).
document.addEventListener("lesson-step", ({ detail }) => {
  if (!detail?.id) return;
  if (detail.step === "code") progress.markSourceView(detail.id);
  if (detail.step === "run") progress.markRun(detail.id, detail.mode === "local" ? "local" : "browser");
});

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

// The chapter's self-test: every question answered, all correct once records the pass with the revision.
function initializeQuiz() {
  const section = document.querySelector("[data-chapter-quiz]");
  if (!section) return;
  const quizId = section.dataset.quizId;
  const revision = section.dataset.quizRevision;
  const form = section.querySelector("[data-quiz-form]");
  const questions = [...section.querySelectorAll("[data-quiz-question]")];
  const stateLabel = section.querySelector("[data-quiz-state]");
  const status = section.querySelector("[data-quiz-status]");
  const submit = form.querySelector('button[type="submit"]');

  function renderQuizState() {
    const passed = progress.quiz(quizId, revision);
    section.classList.toggle("is-passed", Boolean(passed));
    if (passed) section.dataset.record = "done";
    else section.removeAttribute("data-record");
    stateLabel.textContent = passed ? `已通过 · ${passed.passedAt.slice(0, 10)}` : "未作答";
  }

  form.addEventListener("submit", (event) => {
    event.preventDefault();
    const data = new FormData(form);
    let correct = 0;
    let answered = 0;
    questions.forEach((question, index) => {
      const chosen = data.get(`${quizId}-q${index + 1}`);
      const feedback = question.querySelector("[data-quiz-feedback]");
      if (chosen === null) { feedback.hidden = true; question.removeAttribute("data-result"); return; }
      answered += 1;
      const right = chosen === question.dataset.quizAnswer;
      correct += right ? 1 : 0;
      question.dataset.result = right ? "right" : "wrong";
      feedback.hidden = false;
      feedback.textContent = right ? "答对了。" : "再想想：这一题答错了。";
    });
    if (answered < questions.length) {
      status.textContent = `还有 ${questions.length - answered} 题没有选答案。`;
      return;
    }
    if (correct === questions.length) {
      const saved = progress.setQuizPassed(quizId, revision);
      status.textContent = saved ? `${correct} / ${questions.length} 全对，本章自测通过。` : "全对，但尚未保存：可重试保存或导出备份。";
      submit.textContent = "再做一次";
    } else {
      status.textContent = `${correct} / ${questions.length} 答对。改一改答错的题再提交。`;
    }
    renderQuizState();
  });
  window.addEventListener("learning-data-change", renderQuizState);
  renderQuizState();
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
initializeQuiz();
renderRouteBand();
// Opening a chapter is reading it: the visit is the band's first step.
if (isBookUnit) progress.markVisited(document.body.dataset.readingRoute ?? "");
renderLessonSteps();
window.addEventListener("learning-data-change", () => { renderRouteBand(); renderLessonSteps(); });

if (window.location.hash) {
  try {
    setActiveHeading(decodeURIComponent(window.location.hash.slice(1)));
  } catch {
    setActiveHeading(window.location.hash.slice(1));
  }
} else if (headings[0]) {
  saveReadingPosition(headings[0].id);
}
