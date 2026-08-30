const mobileBreakpoint = window.matchMedia("(max-width: 820px)");
const navigationHeaders = [...document.querySelectorAll(".site-header")]
  .map((header, index) => {
    const navigation = header.querySelector(":scope > nav[aria-label]");
    if (!navigation) return null;

    const navigationId = navigation.id || `site-navigation-${index + 1}`;
    navigation.id = navigationId;

    const toggle = document.createElement("button");
    toggle.className = "navigation-toggle";
    toggle.type = "button";
    toggle.setAttribute("aria-controls", navigationId);
    toggle.setAttribute("aria-expanded", "false");
    toggle.setAttribute("aria-label", "打开导航菜单");

    const icon = document.createElement("span");
    icon.className = "navigation-toggle-icon";
    icon.setAttribute("aria-hidden", "true");
    const label = document.createElement("span");
    label.textContent = "菜单";
    toggle.append(icon, label);
    navigation.before(toggle);

    const setOpen = (open, restoreFocus = false) => {
      header.classList.toggle("is-navigation-open", open);
      toggle.setAttribute("aria-expanded", String(open));
      toggle.setAttribute("aria-label", open ? "关闭导航菜单" : "打开导航菜单");
      if (restoreFocus) toggle.focus();
    };

    toggle.addEventListener("click", () => {
      setOpen(toggle.getAttribute("aria-expanded") !== "true");
    });

    navigation.addEventListener("click", (event) => {
      if (event.target instanceof Element && event.target.closest("a")) setOpen(false);
    });

    return { header, setOpen, toggle };
  })
  .filter(Boolean);

if (navigationHeaders.length > 0) {
  document.documentElement.classList.add("has-mobile-navigation");

  document.addEventListener("click", (event) => {
    for (const navigationHeader of navigationHeaders) {
      if (!navigationHeader.header.contains(event.target)) navigationHeader.setOpen(false);
    }
  });

  document.addEventListener("keydown", (event) => {
    if (event.key !== "Escape") return;
    for (const navigationHeader of navigationHeaders) {
      if (navigationHeader.toggle.getAttribute("aria-expanded") === "true") {
        navigationHeader.setOpen(false, true);
      }
    }
  });

  mobileBreakpoint.addEventListener("change", ({ matches }) => {
    if (!matches) {
      for (const navigationHeader of navigationHeaders) navigationHeader.setOpen(false);
    }
  });
}
