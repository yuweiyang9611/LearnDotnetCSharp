// The running head collapses its links behind 菜单 below 56rem; the kit draws it, this wires it.
const wideScreen = window.matchMedia("(min-width: 56.0625rem)");
const heads = [...document.querySelectorAll(".nb-head")]
  .map((head) => {
    const toggle = head.querySelector(".nb-nav-toggle");
    const navigation = head.querySelector(".nb-nav--collapsible");
    if (!toggle || !navigation) return null;

    const setOpen = (open, restoreFocus = false) => {
      navigation.classList.toggle("open", open);
      toggle.setAttribute("aria-expanded", String(open));
      toggle.textContent = open ? "收起" : "菜单";
      if (restoreFocus) toggle.focus();
    };

    toggle.addEventListener("click", () => setOpen(toggle.getAttribute("aria-expanded") !== "true"));
    navigation.addEventListener("click", (event) => {
      if (event.target instanceof Element && event.target.closest("a")) setOpen(false);
    });
    return { head, setOpen, toggle };
  })
  .filter(Boolean);

document.addEventListener("click", (event) => {
  for (const { head, setOpen } of heads) {
    if (!head.contains(event.target)) setOpen(false);
  }
});

document.addEventListener("keydown", (event) => {
  if (event.key !== "Escape") return;
  for (const { setOpen, toggle } of heads) {
    if (toggle.getAttribute("aria-expanded") === "true") setOpen(false, true);
  }
});

wideScreen.addEventListener("change", ({ matches }) => {
  if (matches) for (const { setOpen } of heads) setOpen(false);
});
