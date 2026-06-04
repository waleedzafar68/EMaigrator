import { execSync } from "node:child_process";
import { readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { join } from "node:path";

const webRoot = fileURLToPath(new URL("..", import.meta.url));
execSync("npm run build", { cwd: webRoot, stdio: "inherit" });

const assetsDir = join(webRoot, "dist", "assets");
const patterns = [
  /SigningKey/,
  /client_secret/i,
  /-----BEGIN PRIVATE KEY-----/,
  /AKIA[0-9A-Z]{16}/,
  /password\s*=\s*['"][^'"]+['"]/i,
];

let found = false;
for (const f of readdirSync(assetsDir).filter((n) => n.endsWith(".js"))) {
  const text = readFileSync(join(assetsDir, f), "utf8");
  for (const p of patterns) {
    if (p.test(text)) {
      console.error(`Secret-shaped pattern ${p} found in dist/assets/${f}`);
      found = true;
    }
  }
}
if (found) {
  console.error("bundle-scan FAILED — secret-shaped content in the bundle.");
  process.exit(1);
}
console.log("bundle-scan OK");
