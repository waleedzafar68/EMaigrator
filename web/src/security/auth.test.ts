import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { afterEach, describe, expect, it, vi } from "vitest";
import { apiFetch } from "../api/client";

describe("auth per API scheme (httpOnly cookie)", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("sends credentials include and no token-derived Authorization header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("{}", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    await apiFetch("/migrations");
    const init = fetchMock.mock.calls[0][1];
    expect(init.credentials).toBe("include");
    const headers = (init.headers ?? {}) as Record<string, string>;
    expect(Object.keys(headers).map((k) => k.toLowerCase())).not.toContain("authorization");
  });

  it("signalr withUrl uses no storage-reading accessTokenFactory", () => {
    const src = readFileSync(resolve(__dirname, "../api/signalr.ts"), "utf8");
    expect(src).not.toMatch(/accessTokenFactory[\s\S]*?(local|session)Storage/);
  });
});
