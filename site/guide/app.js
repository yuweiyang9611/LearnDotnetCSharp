document.documentElement.classList.add("guide-js");

const lastHeadingStorageKey = "learn-dotnet-csharp-guide-last-heading-v1";
const tocLinks = [...document.querySelectorAll("[data-toc-id]")];
const headings = [...document.querySelectorAll("[data-guide-heading]")];
const searchInputs = [...document.querySelectorAll("[data-toc-search]")];
const progressBar = document.querySelector("#reading-progress-bar");
const continueReading = document.querySelector("#continue-reading");
const backToTop = document.querySelector("#back-to-top");
const toast = document.querySelector("#guide-toast");
let toastTimer;
let scrollFrame;

function showToast(message) {
  window.clearTimeout(toastTimer);
  toast.textContent = message;
  toast.classList.add("is-visible");
  toastTimer = window.setTimeout(() => toast.classList.remove("is-visible"), 2200);
}

function setActiveHeading(id, updateAddress = false) {
  if (!id) {
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

  try {
    localStorage.setItem(lastHeadingStorageKey, id);
  } catch {
    // Reading works without browser storage.
  }

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

function restoreContinueLink() {
  try {
    const id = localStorage.getItem(lastHeadingStorageKey);
    const heading = id ? document.getElementById(id) : null;
    if (!heading || id === "前言") {
      return;
    }

    continueReading.href = `#${id}`;
    continueReading.firstChild.textContent = "继续上次阅读 ";
  } catch {
    // Keep the default start link.
  }
}

function updateScrollState() {
  const scrollable = document.documentElement.scrollHeight - window.innerHeight;
  const percent = scrollable <= 0 ? 0 : Math.min(100, (window.scrollY / scrollable) * 100);
  progressBar.style.width = `${percent}%`;
  backToTop.classList.toggle("is-visible", window.scrollY > 900);
  scrollFrame = undefined;
}

function scheduleScrollUpdate() {
  if (scrollFrame === undefined) {
    scrollFrame = window.requestAnimationFrame(updateScrollState);
  }
}

async function copyText(text) {
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
}

document.addEventListener("click", async (event) => {
  const copyButton = event.target.closest("[data-copy-code]");
  if (copyButton) {
    const code = copyButton.closest(".code-block")?.querySelector("pre code")?.textContent;
    if (code) {
      await copyText(code);
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
    const scopedLinks = [...scope.querySelectorAll("[data-toc-id]")];
    const searchTerm = input.value.trim().toLocaleLowerCase("zh-CN");
    let visibleCount = 0;
    for (const link of scopedLinks) {
      const visible = !searchTerm || link.textContent.toLocaleLowerCase("zh-CN").includes(searchTerm);
      link.hidden = !visible;
      visibleCount += visible ? 1 : 0;
    }

    const status = scope.querySelector("[data-toc-search-status]");
    status.textContent = searchTerm ? `找到 ${visibleCount} 个目录项` : "";
  });
}

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

for (const details of document.querySelectorAll(".mobile-toc")) {
  details.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && details.open) {
      details.open = false;
      details.querySelector("summary")?.focus();
    }
  });
}

backToTop.addEventListener("click", () => {
  const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  window.scrollTo({ top: 0, behavior: reducedMotion ? "auto" : "smooth" });
});

window.addEventListener("scroll", scheduleScrollUpdate, { passive: true });
window.addEventListener("resize", scheduleScrollUpdate);
restoreContinueLink();
updateScrollState();

if (window.location.hash) {
  try {
    setActiveHeading(decodeURIComponent(window.location.hash.slice(1)));
  } catch {
    setActiveHeading(window.location.hash.slice(1));
  }
}
