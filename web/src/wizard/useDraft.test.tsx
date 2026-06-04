import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useDraft } from "./useDraft";
import * as api from "../api/migrations";

const dto = { id: "m1", status: "Draft", wizardStep: 0, from: null, to: null, isBatch: false, scopeSummary: null, mailboxCount: 0, progress: null, createdAt: "2026-06-01T00:00:00Z" };

describe("useDraft", () => {
  beforeEach(() => {
    vi.spyOn(api, "getMigration").mockResolvedValue(dto as never);
    vi.spyOn(api, "setEndpoints").mockResolvedValue({ ...dto, from: "imap", to: "graph", wizardStep: 1 } as never);
  });
  afterEach(() => vi.restoreAllMocks());

  it("loads the migration by id", async () => {
    const { result } = renderHook(() => useDraft("m1"));
    await waitFor(() => expect(result.current.migration?.id).toBe("m1"));
  });

  it("saves endpoints and advances the step", async () => {
    const { result } = renderHook(() => useDraft("m1"));
    await waitFor(() => expect(result.current.migration).not.toBeNull());
    await act(async () => { await result.current.saveEndpoints("imap", "graph"); });
    expect(api.setEndpoints).toHaveBeenCalledWith("m1", { from: "imap", to: "graph" });
    expect(result.current.migration?.wizardStep).toBe(1);
  });
});
