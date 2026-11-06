import { progress } from "../progress-store.js";
import { initializeProgressUI } from "../progress-ui.js";
import { chapters } from "../progress-catalog.js";
import { continueTarget, stepAnchor, stepLabels } from "../lesson-loop.js";
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

// The chapter to continue on carries the stopped mark in the directory.
function markStoppedUnit(pathname) {
  for (const row of unitRows) {
    const stopped = Boolean(pathname) && new URL(row.querySelector("a").href).pathname === pathname;
    if (stopped) row.setAttribute("aria-current", "true");
    else row.removeAttribute("aria-current");
    row.querySelector(".nb-stopped").hidden = !stopped;
  }
}

// Whether the reader has any record at all: with none, the banner offers the preface as the static page does.
function hasRecord(data) {
  return Boolean(data.lastReading) || data.experiments.length > 0 || Object.keys(data.assessments).length > 0 ||
    data.visited.length > 0 || data.sourceViews.length > 0 || Object.keys(data.runs).length > 0 || Object.keys(data.quizzes).length > 0;
}

// 接着做: the earliest chapter whose band has a 停在这里 step, read with the same rule as the band itself.
// A legacy heading key from the oldest site version still counts as a record of where the reader was.
function updateContinueReading() {
  const data = progress.snapshot();
  let legacyHeading;
  try { legacyHeading = localStorage.getItem(legacyHeadingStorageKey); } catch { /* Optional legacy fallback. */ }
  const legacyUrl = legacyHeading && routeMap[legacyHeading];
  const target = hasRecord(data) || legacyUrl ? continueTarget(data, chapters) : null;
  if (continueReading && target) {
    const route = new URL(`../${target.chapter.route}`, new URL("./", import.meta.url));
    continueReading.href = `${route.pathname}${stepAnchor(target.chapter, target.step)}`;
    continueReading.textContent = `第 ${target.chapter.number} 章 · ${stepLabels[target.step]}`;
    continueReading.classList.add("has-record");
    markStoppedUnit(route.pathname);
  } else if (continueReading && (hasRecord(data) || legacyUrl)) {
    continueReading.href = "./preface/";
    continueReading.textContent = "每一章的五步都完成了，从头复习";
    continueReading.classList.add("has-record");
    markStoppedUnit("");
  } else {
    markStoppedUnit("");
  }
  const stat = document.querySelector("#guide-stat-reading");
  if (stat) stat.textContent = readingFigure(data.lastReading);
  if (resumeGo && continueReading) {
    resumeGo.href = continueReading.getAttribute("href");
    resumeGo.textContent = continueReading.classList.contains("has-record") ? "继续学习" : "开始阅读";
  }
}

// The last reading as a short figure: 第 19 章, 前言 or 附录 A.
function readingFigure(reading) {
  const match = reading?.url.match(/^guide\/(preface|chapter-(\d+)|appendix-([a-d]))\//);
  if (!match) return "还没有";
  if (match[2]) return `第 ${Number.parseInt(match[2], 10)} 章`;
  if (match[3]) return `附录 ${match[3].toUpperCase()}`;
  return "前言";
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
