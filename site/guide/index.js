import { progress } from "../progress-store.js";
import { initializeProgressUI } from "../progress-ui.js";
const legacyHeadingStorageKey = "learn-dotnet-csharp-guide-last-heading-v1";
const routeMapElement = document.querySelector("#guide-route-map");
const continueReading = document.querySelector("#continue-reading");
const guideRoot = new URL("./", window.location.href);

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
    continueReading.firstChild.textContent = "继续上次阅读 ";
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
    const status = card.querySelector("[data-guide-task-status]");
    if (status) status.textContent = isComplete ? "已验收" : "待验收";
    completed += isComplete ? 1 : 0;
  }
  const summary = document.querySelector("#assessment-progress");
  if (summary) summary.textContent = `${completed} / ${cards.length} 章验收`;
}

updateAssessmentProgress();
window.addEventListener("learning-data-change", updateAssessmentProgress);
initializeProgressUI(document.querySelector("[data-learning-backup]"));
