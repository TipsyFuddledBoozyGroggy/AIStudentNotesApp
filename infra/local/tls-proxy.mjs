// LOCAL REHEARSAL ONLY (docker-compose.poc-prod.yml). A minimal HTTPS front for the web container, standing in for the
// Azure Container Apps ingress: the API in Production only accepts an https redirect address and only issues Secure
// `__Host-` cookies, and infra/scripts/smoke.sh only speaks https. It forwards every request, and WebSocket upgrades
// (/hubs, /ingest), unchanged to http://web:3000. Not hardened, not for any deployment.
import https from "node:https";
import http from "node:http";
import net from "node:net";
import fs from "node:fs";

const target = { host: process.env.TARGET_HOST ?? "web", port: Number(process.env.TARGET_PORT ?? 3000) };
const listenPort = Number(process.env.LISTEN_PORT ?? 3443);
const tlsDir = process.env.TLS_DIR ?? "/tls";

const server = https.createServer(
  { key: fs.readFileSync(`${tlsDir}/tls-key.pem`), cert: fs.readFileSync(`${tlsDir}/tls-cert.pem`) },
  (req, res) => {
    const upstream = http.request({ ...target, method: req.method, path: req.url, headers: req.headers }, (answer) => {
      res.writeHead(answer.statusCode ?? 502, answer.headers);
      answer.pipe(res);
    });
    upstream.on("error", () => {
      if (!res.headersSent) res.writeHead(502);
      res.end();
    });
    req.pipe(upstream);
  },
);

server.on("upgrade", (req, socket, head) => {
  const upstream = net.connect(target.port, target.host, () => {
    const lines = [`${req.method} ${req.url} HTTP/1.1`];
    for (let i = 0; i < req.rawHeaders.length; i += 2) lines.push(`${req.rawHeaders[i]}: ${req.rawHeaders[i + 1]}`);
    upstream.write(lines.join("\r\n") + "\r\n\r\n");
    if (head.length > 0) upstream.write(head);
    socket.pipe(upstream);
    upstream.pipe(socket);
  });
  upstream.on("error", () => socket.destroy());
  socket.on("error", () => upstream.destroy());
});

server.listen(listenPort, "0.0.0.0", () => console.log(`local TLS front on :${listenPort} -> http://${target.host}:${target.port}`));
