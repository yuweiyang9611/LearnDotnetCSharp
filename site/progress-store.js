import { experiments } from "./catalog-data.js";
import { tasks, guideRoutes } from "./progress-catalog.js";

export const storageKey = "learn-dotnet-csharp-learning-data-v1";
export const backupFormat = "LearnDotnetCSharp.learning-backup";
export const maximumBackupBytes = 5 * 1024 * 1024;
const root = new URL("./", import.meta.url);
const knownExperiments = new Set(experiments.map(({ id }) => id));
const empty = () => ({ version: 1, experiments: [], assessments: {}, copies: [], lastReading: null });
const object = (value) => value !== null && typeof value === "object" && !Array.isArray(value);
const safeId = (id) => typeof id === "string" && /^[a-zA-Z0-9._-]{1,160}$/.test(id) && !["__proto__", "constructor", "prototype"].includes(id);
const same = (left, right) => JSON.stringify(left) === JSON.stringify(right);

export function readingPosition(value) {
  if (!object(value) || typeof value.url !== "string" || typeof value.title !== "string") return null;
  try {
    const url = new URL(value.url, root);
    if (url.origin !== root.origin || !url.pathname.startsWith(root.pathname)) return null;
    const relative = url.pathname.slice(root.pathname.length);
    if (!guideRoutes.includes(relative)) return null;
    return { title: value.title, url: relative + url.hash };
  } catch { return null; }
}

function assessment(value) {
  if (!object(value) || typeof value.revision !== "string" || typeof value.notes !== "string" ||
      typeof value.completed !== "boolean" || !Array.isArray(value.steps) || !Array.isArray(value.criteria) ||
      ![...value.steps, ...value.criteria].every((item) => typeof item === "boolean")) {
    throw new Error("章节记录格式无效，未修改当前数据。");
  }
  return { revision: value.revision, steps: [...value.steps], criteria: [...value.criteria], notes: value.notes, completed: value.completed };
}

function eligible(id, value) {
  const task = tasks[id];
  return Boolean(task && value.revision === task.revision && value.steps.length === task.steps &&
    value.criteria.length === task.criteria && value.steps.every(Boolean) && value.criteria.every(Boolean) && value.notes.trim().length >= 12);
}

function addCopy(data, taskId, record) {
  const copy = { taskId, record: assessment(record) };
  if (!data.copies.some((existing) => same(existing, copy))) data.copies.push(copy);
}

export function normalizeData(value) {
  if (!object(value) || value.version !== 1 || !Array.isArray(value.experiments) ||
      !value.experiments.every(safeId) || !object(value.assessments) || !Array.isArray(value.copies)) {
    throw new Error("备份数据格式或版本不受支持，未修改当前数据。");
  }
  const data = empty();
  data.experiments = [...new Set(value.experiments)].sort();
  for (const [id, raw] of Object.entries(value.assessments)) {
    if (!safeId(id)) throw new Error("章节标识无效。");
    const record = assessment(raw);
    if (tasks[id] && record.revision !== tasks[id].revision) addCopy(data, id, record);
    else data.assessments[id] = { ...record, completed: tasks[id] ? record.completed && eligible(id, record) : record.completed };
  }
  for (const copy of value.copies) {
    if (!object(copy) || !safeId(copy.taskId)) throw new Error("笔记副本格式无效。");
    addCopy(data, copy.taskId, copy.record);
  }
  data.lastReading = readingPosition(value.lastReading);
  return data;
}

export function mergeData(local, incoming) {
  const merged = normalizeData(local);
  const imported = normalizeData(incoming);
  merged.experiments = [...new Set([...merged.experiments, ...imported.experiments])].sort();
  for (const [id, record] of Object.entries(imported.assessments)) {
    const existing = merged.assessments[id];
    if (!existing) { merged.assessments[id] = record; continue; }
    if (existing.revision !== record.revision) { addCopy(merged, id, record); continue; }
    if (existing.notes && record.notes && existing.notes !== record.notes) addCopy(merged, id, record);
    const combined = {
      revision: existing.revision,
      steps: Array.from({ length: Math.max(existing.steps.length, record.steps.length) }, (_, i) => Boolean(existing.steps[i] || record.steps[i])),
      criteria: Array.from({ length: Math.max(existing.criteria.length, record.criteria.length) }, (_, i) => Boolean(existing.criteria[i] || record.criteria[i])),
      notes: existing.notes || record.notes,
      completed: existing.completed || record.completed,
    };
    if (tasks[id]) combined.completed &&= eligible(id, combined);
    merged.assessments[id] = combined;
  }
  for (const copy of imported.copies) addCopy(merged, copy.taskId, copy.record);
  merged.lastReading ??= imported.lastReading;
  return merged;
}

let memory = empty();
let dirty = false;
let lastError = "";
let unreadable = false;

function notify() { window.dispatchEvent(new CustomEvent("learning-data-change")); }
function legacyData(storage) {
  const data = empty();
  const read = (key, fallback) => {
    try { return JSON.parse(storage.getItem(key) ?? "null") ?? fallback; } catch { return fallback; }
  };
  const oldExperiments = read("learn-dotnet-csharp-progress-v1", []);
  if (Array.isArray(oldExperiments)) data.experiments = oldExperiments.filter(safeId);
  const oldAssessments = read("learn-dotnet-csharp-chapter-assessments-v1", {});
  if (object(oldAssessments)) {
    for (const [id, value] of Object.entries(oldAssessments)) {
      if (!safeId(id)) continue;
      try { data.assessments[id] = assessment(value); } catch { /* Preserve the original legacy key. */ }
    }
  }
  data.lastReading = readingPosition(read("learn-dotnet-csharp-guide-last-reading-v2", null));
  return normalizeData(data);
}

function load() {
  try {
    const storage = window.localStorage;
    const stored = storage.getItem(storageKey);
    if (stored !== null) {
      try { memory = normalizeData(JSON.parse(stored)); }
      catch (error) { unreadable = true; throw error; }
    } else {
      memory = legacyData(storage);
      persist(false);
    }
  } catch (error) { dirty = true; lastError = error.message; }
}

function refresh() {
  if (dirty) return;
  try {
    const stored = window.localStorage.getItem(storageKey);
    if (stored !== null) memory = normalizeData(JSON.parse(stored));
  } catch (error) { dirty = true; lastError = error.message; }
}

function persist(emit = true) {
  try {
    if (unreadable) throw new Error("已有学习数据无法读取，已保留原始记录；请先导出当前编辑。");
    window.localStorage.setItem(storageKey, JSON.stringify(memory));
    dirty = false;
    lastError = "";
  } catch (error) { dirty = true; lastError = error.message; }
  if (emit) notify();
  return !dirty;
}

export const progress = {
  snapshot() { return structuredClone(memory); },
  get saved() { return !dirty; },
  get error() { return lastError; },
  completedIds() { return memory.experiments.filter((id) => knownExperiments.has(id)); },
  assessment(id, revision) {
    const record = memory.assessments[id];
    return record?.revision === revision ? structuredClone(record) : null;
  },
  setExperiments(ids) {
    refresh();
    memory.experiments = [...new Set([...memory.experiments.filter((id) => !knownExperiments.has(id)), ...ids])].sort();
    return persist();
  },
  setAssessment(id, record) {
    refresh();
    const previous = memory.assessments[id];
    if (previous && previous.revision !== record.revision) addCopy(memory, id, previous);
    memory.assessments[id] = assessment(record);
    return persist();
  },
  setReading(value) { refresh(); memory.lastReading = readingPosition(value); return persist(); },
  retry() { return persist(); },
  merge(incoming) { refresh(); memory = mergeData(memory, incoming); return persist(); },
  backup() { return { format: backupFormat, version: 1, exportedAt: new Date().toISOString(), data: this.snapshot() }; },
  async readBackup(file) {
    if (file.size > maximumBackupBytes) throw new Error("备份超过 5 MiB，未修改当前数据。");
    let value;
    try { value = JSON.parse(await file.text()); } catch { throw new Error("备份不是有效 JSON，未修改当前数据。"); }
    if (value?.format !== backupFormat || value.version !== 1) throw new Error("不支持此备份格式或版本。");
    return normalizeData(value.data);
  },
  preview(incoming) {
    refresh();
    const merged = mergeData(memory, incoming);
    return {
      experiments: merged.experiments.filter((id) => !memory.experiments.includes(id) && knownExperiments.has(id)).length,
      chapters: Object.entries(merged.assessments).filter(([id, record]) => tasks[id] && !same(record, memory.assessments[id])).length,
      copies: merged.copies.length - memory.copies.length,
      unmatched: merged.experiments.filter((id) => !knownExperiments.has(id)).length + Object.keys(merged.assessments).filter((id) => !tasks[id]).length,
    };
  },
};

load();
window.addEventListener("storage", (event) => {
  if (event.key === storageKey || event.key === null) {
    if (!dirty) { memory = empty(); refresh(); }
    notify();
  }
});
