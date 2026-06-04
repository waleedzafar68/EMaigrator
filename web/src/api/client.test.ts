import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, apiFetch } from "./client";

describe("apiFetch", () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => vi.restoreAllMocks());

  it("calls /api/v1 with credentials include and parses JSON", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: "m1", status: "Draft" }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );
    vi.stubGlobal("fetch", fetchMock);
    const out = await apiFetch<{ id: string; status: string }>("/migrations/m1");
    expect(out.id).toBe("m1");
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/v1/migrations/m1");
    expect(init.credentials).toBe("include");
  });

  it("maps a non-2xx response to ApiError with trace id", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({ message: "We couldn't sign in.", errorCode: "AUTH_FAILED", traceId: "4f9c-21a8" }),
          { status: 401, headers: { "Content-Type": "application/json", "X-Trace-Id": "4f9c-21a8" } },
        ),
      ),
    );
    await expect(apiFetch("/migrations/m1")).rejects.toMatchObject({
      status: 401,
      code: "AUTH_FAILED",
      traceId: "4f9c-21a8",
    } satisfies Partial<ApiError>);
  });

  it("never touches localStorage for auth", async () => {
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    const getItem = vi.spyOn(Storage.prototype, "getItem");
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("{}", { status: 200 })));
    await apiFetch("/migrations");
    expect(setItem).not.toHaveBeenCalledWith(expect.stringMatching(/token|auth|jwt/i), expect.anything());
    expect(getItem).not.toHaveBeenCalledWith(expect.stringMatching(/token|auth|jwt/i));
  });
});
