// The loop band's reading of the record: five steps per chapter, each done because the reader did it,
// one 停在这里 at the first step that is not, and the chapter to continue on the index from the same rule.
// Chapter shapes (labs, quiz revision) come from the generated progress-catalog.js; this module is pure.

export const stepOrder = Object.freeze(["read", "code", "run", "quiz", "note"]);
export const stepLabels = Object.freeze({ read: "读", code: "看代码", run: "跑", quiz: "自测", note: "记一笔" });

// The chapter number the last reading position reached: 0 before the book, 31 past the last chapter.
export function readingReach(lastReading) {
  const match = lastReading?.url?.match(/^guide\/(preface|chapter-(\d+)|appendix-[a-d])\//);
  if (!match) return 0;
  if (match[2]) return Number.parseInt(match[2], 10);
  return match[1] === "preface" ? 0 : 31;
}

// The raw states before 停在这里 is placed: done, none or pending, with a count where the step has several parts.
export function stepFacts(data, chapter) {
  const labs = chapter.labs ?? [];
  const sourceViews = new Set(data.sourceViews ?? []);
  const runs = data.runs ?? {};
  const viewed = labs.filter((id) => sourceViews.has(id)).length;
  const ran = labs.filter((id) => Boolean(runs[id])).length;
  const quiz = chapter.quiz ? data.quizzes?.[chapter.id] : null;
  const notes = data.assessments?.[chapter.id]?.notes ?? "";
  const read = (data.visited ?? []).includes(chapter.route) || readingReach(data.lastReading) >= chapter.number;
  return {
    read: { done: read },
    code: labs.length === 0 ? { none: true } : { done: viewed > 0, count: labs.length > 1 ? `${viewed} / ${labs.length}` : "" },
    run: labs.length === 0 ? { none: true } : { done: ran > 0, count: labs.length > 1 ? `${ran} / ${labs.length}` : "" },
    quiz: chapter.quiz ? { done: Boolean(quiz) && quiz.revision === chapter.quiz.revision, count: `${chapter.quiz.questions} 题` } : { none: true },
    note: { done: notes.trim().length > 0 },
  };
}

// The band: exactly one now (the first step that is neither done nor absent), pending after it, none kept dashed.
export function chapterSteps(data, chapter) {
  const facts = stepFacts(data, chapter);
  let placed = false;
  return stepOrder.map((step) => {
    const fact = facts[step];
    let state = "pending";
    if (fact.none) state = "none";
    else if (fact.done) state = "done";
    else if (!placed) { state = "now"; placed = true; }
    return { step, state, count: fact.count ?? "" };
  });
}

// Where to continue: the earliest chapter with a now step, or null when every chapter is complete.
export function continueTarget(data, chapters) {
  for (const chapter of chapters) {
    const now = chapterSteps(data, chapter).find(({ state }) => state === "now");
    if (now) return { chapter, step: now.step };
  }
  return null;
}

// The anchor on the chapter page that does a step.
export function stepAnchor(chapter, step) {
  if (step === "code" || step === "run") return chapter.labs?.length ? `#source-${chapter.labs[0].replaceAll(".", "-")}` : "";
  if (step === "quiz") return `#${chapter.id}-quiz`;
  if (step === "note") return `#${chapter.id}-notes`;
  return `#${chapter.heading}`;
}
