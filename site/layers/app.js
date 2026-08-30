document.documentElement.classList.add("layers-js");

const controls = document.querySelector("#focus-controls");
const summary = document.querySelector("#focus-summary");
const runtime = document.querySelector("#artifact-runtime");
const platform = document.querySelector("#artifact-platform");
const hash = document.querySelector("#artifact-hash");
const assemblyCaption = document.querySelector("#assembly-caption");
const methodSignature = document.querySelector("#method-signature");
const methodToken = document.querySelector("#method-token");
const methodRva = document.querySelector("#method-rva");
const methodMaxStack = document.querySelector("#method-max-stack");
const methodIlBytes = document.querySelector("#method-il-bytes");
const methodEhCount = document.querySelector("#method-eh-count");
const error = document.querySelector("#layer-error");
const copyButton = document.querySelector("#copy-layer-command");
const stageElements = Object.fromEntries(
  ["highLevel", "lowLevel", "cil", "assembly"].map((stage) => [stage, document.querySelector(`#stage-${stage === "highLevel" ? "high" : stage === "lowLevel" ? "low" : stage === "assembly" ? "assembly" : "cil"}-code`)]),
);
const stackPrevious = document.querySelector("#stack-previous");
const stackNext = document.querySelector("#stack-next");
const stackPosition = document.querySelector("#stack-position");
const stackInstruction = document.querySelector("#stack-instruction");
const stackExplanation = document.querySelector("#stack-explanation");
const stackBefore = document.querySelector("#stack-before");
const stackAfter = document.querySelector("#stack-after");
let payload;
let activeFocus;
let stackIndex = 0;

function normalize(value) { return String(value).normalize("NFKC").toLocaleLowerCase("zh-CN"); }

function renderStage(stage, code, patterns) {
  const element = stageElements[stage];
  element.replaceChildren();
  const normalizedPatterns = patterns.map(normalize);
  for (const line of code.replaceAll("\r\n", "\n").split("\n")) {
    const row = document.createElement("span");
    row.className = "code-line";
    row.textContent = line || " ";
    if (normalizedPatterns.some((pattern) => pattern && normalize(line).includes(pattern))) row.classList.add("is-focus");
    element.append(row);
  }
}

function selectFocus(id) {
  activeFocus = payload.focuses.find((focus) => focus.id === id) ?? payload.focuses[0];
  for (const button of controls.querySelectorAll("button")) button.setAttribute("aria-pressed", String(button.dataset.focus === activeFocus.id));
  summary.textContent = activeFocus.explanation;
  for (const [stage, code] of Object.entries(payload.stages)) renderStage(stage, code, activeFocus.patterns[stage] ?? []);
}

function renderStack() {
  const step = payload.stackSteps[stackIndex];
  stackPosition.textContent = `${stackIndex + 1} / ${payload.stackSteps.length}`;
  stackInstruction.textContent = step.instruction;
  stackExplanation.textContent = step.explanation;
  stackBefore.textContent = step.before;
  stackAfter.textContent = step.after;
  stackPrevious.disabled = stackIndex === 0;
  stackNext.disabled = stackIndex === payload.stackSteps.length - 1;
}

function wirePayload(data) {
  payload = data;
  runtime.textContent = data.environment.runtime;
  platform.textContent = `${data.environment.os} · ${data.environment.architecture}`;
  hash.textContent = `源码 ${data.sourceSha256.slice(0, 12)} · PE ${data.peSha256.slice(0, 12)}`;
  assemblyCaption.textContent = `${data.environment.architecture} · ${data.environment.captureMode}`;
  methodSignature.textContent = data.method.signature;
  methodToken.textContent = data.method.metadataToken;
  methodRva.textContent = data.method.relativeVirtualAddress;
  methodMaxStack.textContent = String(data.method.maxStack);
  methodIlBytes.textContent = String(data.method.ilByteCount);
  methodEhCount.textContent = String(data.method.exceptionRegionCount);
  controls.replaceChildren(...data.focuses.map((focus, index) => {
    const button = document.createElement("button");
    button.type = "button";
    button.dataset.focus = focus.id;
    button.setAttribute("aria-pressed", String(index === 0));
    button.textContent = focus.label;
    button.addEventListener("click", () => selectFocus(focus.id));
    return button;
  }));
  selectFocus(data.focuses[0].id);
  renderStack();
}

stackPrevious.addEventListener("click", () => { stackIndex = Math.max(0, stackIndex - 1); renderStack(); });
stackNext.addEventListener("click", () => { stackIndex = Math.min(payload.stackSteps.length - 1, stackIndex + 1); renderStack(); });
copyButton.addEventListener("click", async () => {
  const command = "dotnet run --project src/LearnDotnetCSharp.App --configuration Release -- export-code-layers site/layers/artifacts.json";
  try {
    await navigator.clipboard.writeText(command);
  } catch {
    const input = document.createElement("textarea");
    input.value = command;
    input.style.position = "fixed";
    input.style.opacity = "0";
    document.body.append(input);
    input.select();
    document.execCommand("copy");
    input.remove();
  }
  copyButton.textContent = "已复制制品生成命令";
  window.setTimeout(() => { copyButton.textContent = "复制制品生成命令"; }, 1600);
});

try {
  const response = await fetch("./artifacts.json");
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  wirePayload(await response.json());
} catch (reason) {
  error.hidden = false;
  error.textContent = `制品快照载入失败：${reason instanceof Error ? reason.message : "未知错误"}`;
  summary.textContent = "请从第 19、20 章继续阅读。";
}
