import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { reconcile } from "./migrations";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

describe("reconcile api wrapper", () => {
  let fetchMock: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
  });
  afterEach(() => vi.restoreAllMocks());

  it("POSTs to /migrations/{id}/reconcile via apiFetch (cookie auth, no raw fetch)", async () => {
    fetchMock.mockResolvedValue(jsonResponse({ id: "m1", status: "Running" }));

    const dto = await reconcile("m1");

    expect(dto.status).toBe("Running");
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/v1/migrations/m1/reconcile");
    expect(init.method).toBe("POST");
    // apiFetch attaches cookie auth on every call (credentials:include), never a bearer token.
    expect(init.credentials).toBe("include");
  });
});
