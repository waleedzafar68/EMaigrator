import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, resolve } from "node:path";
import { afterEach, describe, expect, it, vi } from "vitest";
import { putConnection } from "../api/migrations";

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const p = join(dir, name);
    return statSync(p).isDirectory() ? walk(p) : [p];
  });
}

describe("no secrets persisted", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("never writes the connection secret to storage", async () => {
    const setLocal = vi.spyOn(Storage.prototype, "setItem");
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("{}", { status: 200 })));
    const secret = "super-secret-app-password-123";
    await putConnection("m1", "from", { auth: "ImapBasic", settings: { host: "h" }, secret });
    const wroteSecret = setLocal.mock.calls.some(([, v]) => typeof v === "string" && v.includes(secret));
    expect(wroteSecret).toBe(false);
  });

  it("contains no storage write of a token/secret/password", () => {
    const srcRoot = resolve(__dirname, "..");
    const re = /(local|session)Storage\.setItem\([^)]*(token|secret|password|jwt|credential)/i;
    const offenders = walk(srcRoot)
      .filter((f) => /\.(tsx?|jsx?)$/.test(f) && !f.includes("security"))
      .filter((f) => re.test(readFileSync(f, "utf8")));
    expect(offenders).toEqual([]);
  });
});
