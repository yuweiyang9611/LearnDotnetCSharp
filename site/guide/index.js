const lastReadingStorageKey = "learn-dotnet-csharp-guide-last-reading-v2";
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

try {
  const stored = JSON.parse(localStorage.getItem(lastReadingStorageKey) ?? "null");
  const legacyHeading = localStorage.getItem(legacyHeadingStorageKey);
  const legacyUrl = legacyHeading && routeMap[legacyHeading] ? routeMap[legacyHeading] : null;
  const storedUrl = stored?.url
    ? new URL(stored.url, window.location.origin)
    : legacyUrl
      ? new URL(legacyUrl, guideRoot)
      : null;
  if (
    continueReading &&
    storedUrl?.origin === window.location.origin &&
    storedUrl.pathname.startsWith(guideRoot.pathname) &&
    storedUrl.pathname !== guideRoot.pathname
  ) {
    continueReading.href = `${storedUrl.pathname}${storedUrl.hash}`;
    continueReading.firstChild.textContent = "继续上次阅读 ";
  }
} catch {
  // The directory and old hash redirects remain usable without storage.
}
