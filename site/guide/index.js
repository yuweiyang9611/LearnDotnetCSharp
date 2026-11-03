import { progress } from "../progress-store.js";
import { initializeProgressUI } from "../progress-ui.js";
const legacyHeadingStorageKey = "learn-dotnet-csharp-guide-last-heading-v1";
const routeMapElement = document.querySelector("#guide-route-map");
const continueReading = document.querySelector("#continue-reading");
const resumeGo = document.querySelector("#guide-resume-go");
const guideRoot = new URL("./", window.location.href);
const unitRows = [...document.querySelectorAll("[data-unit-route]")];

function readRouteMap() {
  try {
    return JSON.parse(routeMapElement?.textContent ?? "{}");
  } catch {
    return {};
  }
}

function currentHashId() {
  if (!window.location.hash) {
    return "";
  }
  try {
    return decodeURIComponent(window.location.hash.slice(1));
  } catch {
    return window.location.hash.slice(1);
  }
}

const routeMap = readRouteMap();
const hashId = currentHashId();
if (hashId && routeMap[hashId]) {
  window.location.replace(routeMap[hashId]);
}

// The unit the owner was last reading carries the stopped mark in the directory.
function markStoppedUnit(pathname) {
  let figure = "还没有";
  for (const row of unitRows) {
    const stopped = Boolean(pathname) && new URL(row.querySelector("a").href).pathname === pathname;
    if (stopped) row.setAttribute("aria-current", "true");
    else row.removeAttribute("aria-current");
    row.querySelector(".nb-stopped").hidden = !stopped;
    if (stopped) figure = row.querySelector(".nb-card-meta > span")?.textContent ?? figure;
  }
  const stat = document.querySelector("#guide-stat-reading");
  if (stat) stat.textContent = figure;
}

function updateContinueReading() {
  const stored = progress.snapshot().lastReading;
  let legacyHeading;
  try { legacyHeading = localStorage.getItem(legacyHeadingStorageKey); } catch { /* Optional legacy fallback. */ }
  const legacyUrl = legacyHeading && routeMap[legacyHeading];
  const storedUrl = stored?.url ? new URL(stored.url, new URL("../", import.meta.url))
    : legacyUrl ? new URL(legacyUrl, guideRoot) : null;
  if (continueReading && storedUrl?.origin === window.location.origin &&
      storedUrl.pathname.startsWith(guideRoot.pathname) && storedUrl.pathname !== guideRoot.pathname) {
    continueReading.href = `${storedUrl.pathname}${storedUrl.hash}`;
    continueReading.textContent = stored?.title ? `继续阅读：${stored.title.replace(/^#\s*/, "")}` : "继续上次阅读";
    continueReading.classList.add("has-record");
    markStoppedUnit(storedUrl.pathname);
  } else {
    markStoppedUnit("");
  }
  if (resumeGo && continueReading) {
    resumeGo.href = continueReading.getAttribute("href");
    resumeGo.textContent = continueReading.classList.contains("has-record") ? "继续阅读" : "开始阅读";
  }
}
updateContinueReading();
window.addEventListener("learning-data-change", updateContinueReading);

function updateAssessmentProgress() {
  const assessments = progress.snapshot().assessments;
  const cards = [...document.querySelectorAll("[data-chapter-task-id]")];
  let completed = 0;
  for (const card of cards) {
    const task = assessments[card.dataset.chapterTaskId];
    const isComplete = Boolean(task?.completed && task.revision === card.dataset.chapterTaskRevision);
    card.classList.toggle("is-task-complete", isComplete);
    if (isComplete) card.dataset.record = "done";
    else card.removeAttribute("data-record");
    const status = card.querySelector("[data-guide-task-status]");
    if (status) status.textContent = isComplete ? "已验收" : "待验收";
    completed += isComplete ? 1 : 0;
  }
  const summary = document.querySelector("#assessment-progress");
  if (summary) summary.textContent = `${completed} / ${cards.length} 章验收`;
  document.querySelector("#assessment-track")?.style.setProperty("--nb-fraction", String(cards.length ? completed / cards.length : 0));
  document.querySelector("#assessment-track")?.setAttribute("aria-valuenow", String(completed));
  const verifiedStat = document.querySelector("#guide-stat-verified");
  if (verifiedStat) verifiedStat.textContent = String(completed);
  const doneStat = document.querySelector("#guide-stat-done");
  if (doneStat) doneStat.textContent = String(progress.completedIds().length);
  // Each part's band carries the verified share of its chapters.
  for (const part of document.querySelectorAll(".guide-part-group")) {
    const chapters = [...part.querySelectorAll("[data-chapter-task-id]")];
    if (chapters.length === 0) continue;
    const done = chapters.filter((card) => card.dataset.record === "done").length;
    part.querySelector("[data-part-state]").textContent = `${done} / ${chapters.length} 章已验收`;
    const bar = part.querySelector("[data-part-bar]");
    bar.style.setProperty("--nb-fraction", String(done / chapters.length));
    bar.setAttribute("aria-valuenow", String(done));
  }
}

updateAssessmentProgress();
window.addEventListener("learning-data-change", updateAssessmentProgress);
initializeProgressUI(document.querySelector("[data-learning-backup]"));
