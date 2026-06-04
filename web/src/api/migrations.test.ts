import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createMigration, listMigrations, testConnection } from "./migrations";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

describe("migrations api", () => {
  let fetchMock: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
  });
  afterEach(() => vi.restoreAllMocks());

  it("POSTs to /migrations to create a draft", async () => {
    fetchMock.mockResolvedValue(jsonResponse({ id: "m1", status: "Draft", wizardStep: 0 }));
    const dto = await createMigration();
    expect(dto.status).toBe("Draft");
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/v1/migrations");
    expect(init.method).toBe("POST");
  });

  it("lists migrations with status + q query", async () => {
    fetchMock.mockResolvedValue(jsonResponse([{ id: "m1", status: "Running" }]));
    await listMigrations({ status: "Running", q: "work" });
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/migrations?status=Running&q=work");
  });

  it("POSTs the test-connection route for a side", async () => {
    fetchMock.mockResolvedValue(jsonResponse({ ok: true, folderCount: 14, messageCount: 3201 }));
    const r = await testConnection("m1", "from");
    expect(r.ok).toBe(true);
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/migrations/m1/connection/from/test");
    expect(fetchMock.mock.calls[0][1].method).toBe("POST");
  });
});
