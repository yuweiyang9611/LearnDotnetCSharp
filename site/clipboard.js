export async function copyText(text) {
  try { await navigator.clipboard.writeText(text); return true; } catch { /* Try the legacy clipboard path. */ }
  const input = document.createElement("textarea");
  input.value = text;
  input.style.position = "fixed";
  input.style.opacity = "0";
  document.body.append(input);
  input.select();
  try { return document.execCommand("copy") === true; }
  catch { return false; }
  finally { input.remove(); }
}
