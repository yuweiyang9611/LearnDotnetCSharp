import { createReadStream } from "node:fs";
import { stat } from "node:fs/promises";
import { createServer } from "node:http";
import { extname, join, normalize, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

const port = Number.parseInt(process.argv[2] ?? process.env.STUDY_SITE_PORT ?? "4173", 10);
const defaultRoot = fileURLToPath(new URL("../artifacts/study-site", import.meta.url));
const root = resolve(process.argv[3] ?? defaultRoot);
const basePath = process.argv[4] ?? "/";
if (!basePath.startsWith("/") || !basePath.endsWith("/")) throw new Error("Base path must begin and end with '/'.");
const contentTypes = new Map([
  [".css", "text/css; charset=utf-8"],
  [".html", "text/html; charset=utf-8"],
  [".js", "text/javascript; charset=utf-8"],
  [".json", "application/json; charset=utf-8"],
  [".png", "image/png"],
  [".pdf", "application/pdf"],
]);

const server = createServer(async (request, response) => {
  let pathname;
  try { pathname = decodeURIComponent(new URL(request.url ?? "/", "http://localhost").pathname); }
  catch { response.writeHead(400).end("Bad request"); return; }
  if (!pathname.startsWith(basePath)) { response.writeHead(404).end("Not found"); return; }
  pathname = "/" + pathname.slice(basePath.length);
  const requestedPath = pathname.endsWith("/") ? `${pathname}index.html` : pathname;
  const filePath = normalize(join(root, requestedPath));

  if (filePath !== root && !filePath.startsWith(root + sep)) {
    response.writeHead(403).end("Forbidden");
    return;
  }

  try {
    const info = await stat(filePath);
    if (info.isDirectory() && !pathname.endsWith("/")) {
      response.writeHead(308, { Location: `${basePath}${pathname.slice(1)}/` }).end();
      return;
    }
    if (!info.isFile()) {
      throw new Error("Not a file");
    }

    response.writeHead(200, {
      "Cache-Control": "no-store",
      "Content-Type": contentTypes.get(extname(filePath)) ?? "application/octet-stream",
    });
    createReadStream(filePath).pipe(response);
  } catch {
    response.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" }).end("Not found");
  }
});

server.listen(port, "127.0.0.1", () => {
  console.log(`Study site preview: http://127.0.0.1:${port}/ (${root})`);
});
