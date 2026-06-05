import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Results } from "./Results";
import { ApiError } from "../api/client";
import * as api from "../api/migrations";

vi.mock("react-router-dom", () => ({ useParams: () => ({ id: "m1" }) }));

// A genuinely Partial outcome: the API reports the job's real status, and an outstanding decision
// leaves reconciliation unmatched. The header now reflects data.status, with duration + log-retention
// deadline driven by the new ResultsDto fields.
const results = {
  counts: { migrated: 3180, skipped: 18, failed: 0 },
  reconciliation: { sourceCount: 3201, destCount: 3201, matched: false },
  needsDecision: [{ issueType: "FolderCollision", detail: "/Projects collision", options: ["RenameFolder", "MergeFolder"] }],
  status: "Partial",
  durationSeconds: 754,
  logDeletesAt: "2026-07-05T00:00:00Z",
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

  it("uses the API status for the header and shows the real duration", async () => {
    vi.spyOn(api, "getResults").mockResolvedValue({ ...results, status: "Completed", durationSeconds: 754 } as never);
    vi.spyOn(api, "getAudit").mockResolvedValue([] as never);
    render(<Results />);
    // status "Completed" reads as plain complete (no "— Partial" suffix despite unmatched reconciliation)
    expect(await screen.findByText(/^migration complete$/i)).toBeInTheDocument();
    expect(screen.getByText(/took 12:34/i)).toBeInTheDocument(); // 754s → 12:34
  });

  it("falls back to a reconciliation-derived header when status is absent", async () => {
    const noStatus = { counts: { migrated: 10, skipped: 0, failed: 0 }, reconciliation: { sourceCount: 10, destCount: 10, matched: true }, needsDecision: [], durationSeconds: null, logDeletesAt: null };
    vi.spyOn(api, "getResults").mockResolvedValue(noStatus as never);
    vi.spyOn(api, "getAudit").mockResolvedValue([] as never);
    render(<Results />);
    expect(await screen.findByText(/^migration complete$/i)).toBeInTheDocument();
  });

  it("shows the real log-retention deadline and hides duration when null", async () => {
    vi.spyOn(api, "getResults").mockResolvedValue({ ...results, durationSeconds: null, logDeletesAt: "2026-07-05T00:00:00Z" } as never);
    vi.spyOn(api, "getAudit").mockResolvedValue([] as never);
    render(<Results />);
    await screen.findByText(/migration complete — partial/i);
    expect(screen.getByText(/auto-deletes on/i)).toBeInTheDocument();
    expect(screen.queryByText(/^Took /)).not.toBeInTheDocument();
  });

  it("hides the log-retention line when logDeletesAt is null", async () => {
    vi.spyOn(api, "getResults").mockResolvedValue({ ...results, logDeletesAt: null } as never);
    vi.spyOn(api, "getAudit").mockResolvedValue([] as never);
    render(<Results />);
    await screen.findByText(/migration complete — partial/i);
    expect(screen.queryByText(/auto-deletes/i)).not.toBeInTheDocument();
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

  it("renders an error state (not an infinite skeleton) when getResults fails", async () => {
    vi.spyOn(api, "getResults").mockRejectedValue(new ApiError(500, "INTERNAL", "Results unavailable", null, "t1"));
    vi.spyOn(api, "getAudit").mockResolvedValue([] as never);
    render(<Results />);
    expect(await screen.findByRole("alert")).toHaveTextContent(/results unavailable/i);
    expect(screen.queryByRole("status", { name: /loading results/i })).not.toBeInTheDocument();
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
