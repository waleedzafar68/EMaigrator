import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { listProviders } from "./providers";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

describe("providers api", () => {
  let fetchMock: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
  });
  afterEach(() => vi.restoreAllMocks());

  it("GETs /providers and returns the capability matrix", async () => {
    fetchMock.mockResolvedValue(
      jsonResponse([
        { id: "imap", canBeSource: true, canBeDestination: false, canBatch: false, supportedAuth: ["ImapBasic"] },
        { id: "graph", canBeSource: true, canBeDestination: true, canBatch: true, supportedAuth: ["GraphAppOAuth"] },
      ]),
    );
    const out = await listProviders();
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/providers");
    expect(out.find((p) => p.id === "imap")?.canBatch).toBe(false);
    expect(out.find((p) => p.id === "graph")?.canBatch).toBe(true);
  });
});
