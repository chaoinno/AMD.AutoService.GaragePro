import { createReadStream } from "node:fs";
import { stat } from "node:fs/promises";
import { createServer } from "node:http";
import path from "node:path";
import { fileURLToPath } from "node:url";

const rootDir = path.dirname(fileURLToPath(import.meta.url));
const host = process.env.HOST || "127.0.0.1";
const port = Number.parseInt(process.env.PORT || "3040", 10);
const launcher = "GaragePro Launcher.dc.html";

const mimeTypes = new Map([
  [".html", "text/html; charset=utf-8"],
  [".js", "text/javascript; charset=utf-8"],
  [".png", "image/png"],
  [".jpg", "image/jpeg"],
  [".jpeg", "image/jpeg"],
  [".gif", "image/gif"],
  [".svg", "image/svg+xml"],
  [".webp", "image/webp"],
  [".ico", "image/x-icon"]
]);

function sendText(response, statusCode, body) {
  response.writeHead(statusCode, { "Content-Type": "text/plain; charset=utf-8" });
  response.end(body);
}

function resolvePublicFile(urlPath) {
  let decodedPath;
  try {
    decodedPath = decodeURIComponent(urlPath);
  } catch {
    return null;
  }

  const relativePath = decodedPath === "/" ? launcher : decodedPath.replace(/^\/+/, "");
  const extension = path.extname(relativePath).toLowerCase();
  const isTopLevelAsset = !relativePath.includes("/") && (extension === ".html" || relativePath === "support.js");
  const isUploadedImage = relativePath.startsWith("uploads/") && mimeTypes.has(extension) && extension !== ".html" && extension !== ".js";

  if (!isTopLevelAsset && !isUploadedImage) return null;

  const absolutePath = path.resolve(rootDir, relativePath);
  if (!absolutePath.startsWith(`${rootDir}${path.sep}`)) return null;
  return { absolutePath, extension };
}

const server = createServer(async (request, response) => {
  response.setHeader("X-Content-Type-Options", "nosniff");
  response.setHeader("Referrer-Policy", "strict-origin-when-cross-origin");
  response.setHeader("X-Frame-Options", "SAMEORIGIN");

  if (request.method !== "GET" && request.method !== "HEAD") {
    response.setHeader("Allow", "GET, HEAD");
    sendText(response, 405, "Method Not Allowed\n");
    return;
  }

  const requestUrl = new URL(request.url || "/", `http://${request.headers.host || "localhost"}`);
  const publicFile = resolvePublicFile(requestUrl.pathname);
  if (!publicFile) {
    sendText(response, 404, "Not Found\n");
    return;
  }

  try {
    const fileStat = await stat(publicFile.absolutePath);
    if (!fileStat.isFile()) throw new Error("Not a file");

    response.writeHead(200, {
      "Content-Type": mimeTypes.get(publicFile.extension) || "application/octet-stream",
      "Content-Length": fileStat.size,
      "Cache-Control": publicFile.extension === ".html" ? "no-cache" : "public, max-age=3600"
    });

    if (request.method === "HEAD") {
      response.end();
      return;
    }

    createReadStream(publicFile.absolutePath).pipe(response);
  } catch {
    sendText(response, 404, "Not Found\n");
  }
});

server.listen(port, host, () => {
  console.log(`GaragePro Service Ops listening on http://${host}:${port}`);
});

function shutDown(signal) {
  console.log(`${signal} received; shutting down`);
  server.close(() => process.exit(0));
}

process.on("SIGINT", () => shutDown("SIGINT"));
process.on("SIGTERM", () => shutDown("SIGTERM"));
