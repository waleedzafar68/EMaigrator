import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Results } from "./Results";
import * as api from "../api/migrations";

vi.mock("react-router-dom", () => ({ useParams: () => ({ id: "m1" }) }));

const results = {
  status: "Partial", migratedCount: 3180, skippedCount: 18,
  failedCount: 0, sourceCount: 3201, destCount: 3201, durationSeconds: 702,
  logDeletesAt: "2026-07-01T00:00:00Z",
  needsDecision: [{ migrationId: "m1", issueType: "FolderCollision", detail: "/Projects collision", options: ["RenameFolder", "MergeFolder"] }],
};

describe("Results", () => {
  afterEach(() => vi.restoreAllMocks());

  it("shows the Partial outcome header and reconciliation", async () => {
    vi.spyOn(api, "getResults").mockResolvedValue(results as never);
    vi.spyOn(api, "getAudit").mockResolvedValue([] as never);
    render(<Results />);
    expect(await screen.findByText(/migration complete — partial/i)).toBeInTheDocument();
    expect(screen.getByText(/3,201 in source, 3,201 in destination/i)).toBeInTheDocument();
  });

  it("re-runs unfinished items", async () => {
    vi.spyOn(api, "getResults").mockResolvedValue(results as never);
    vi.spyOn(api, "getAudit").mockResolvedValue([] as never);
    const rerun = vi.spyOn(api, "rerun").mockResolvedValue({} as never);
    render(<Results />);
    await screen.findByText(/migration complete — partial/i);
    await userEvent.click(screen.getByRole("button", { name: /re-run unfinished/i }));
    expect(rerun).toHaveBeenCalledWith("m1");
  });

  it("offers CSV and PDF export links", async () => {
    vi.spyOn(api, "getResults").mockResolvedValue(results as never);
    vi.spyOn(api, "getAudit").mockResolvedValue([] as never);
    render(<Results />);
    await screen.findByText(/migration complete — partial/i);
    expect(screen.getByRole("link", { name: /csv/i })).toHaveAttribute("href", "/api/v1/migrations/m1/report?format=csv");
    expect(screen.getByRole("link", { name: /pdf/i })).toHaveAttribute("href", "/api/v1/migrations/m1/report?format=pdf");
  });
});
