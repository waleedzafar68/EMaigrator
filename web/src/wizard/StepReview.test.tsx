import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StepReview } from "./StepReview";
import * as api from "../api/migrations";

const nav = vi.fn();
vi.mock("react-router-dom", () => ({ useNavigate: () => nav, useOutletContext: () => ({ migration: { id: "m1" } }) }));

const cleanPlan = { scanning: false, issues: [], estimate: { mailboxCount: 1, folderCount: 14, messageCount: 3201, totalBytes: 262144000, estimatedDurationSeconds: 720 } };
const issuePlan = {
  scanning: false,
  issues: [{ issueType: "FolderDepth", affectedPaths: ["/a/b/c/d/e"], recommendedAction: "FlattenFolder", options: ["FlattenFolder", "RenameFolder", "SkipMessage"], severity: "Warning", description: "12 folders exceed Outlook's depth" }],
  estimate: { mailboxCount: 218, folderCount: 900, messageCount: 1200000, totalBytes: 0, estimatedDurationSeconds: 7800 },
};
const blockerPlan = {
  scanning: false,
  issues: [{ issueType: "FolderDepth", affectedPaths: ["/a/b/c/d/e"], recommendedAction: "FlattenFolder", options: ["FlattenFolder", "RenameFolder", "SkipMessage"], severity: "Blocker", description: "a blocker that must be fixed" }],
  estimate: { mailboxCount: 218, folderCount: 900, messageCount: 1200000, totalBytes: 0, estimatedDurationSeconds: 7800 },
};

describe("StepReview", () => {
  beforeEach(() => { vi.spyOn(api, "startPreflight").mockResolvedValue(undefined as never); });
  afterEach(() => vi.restoreAllMocks());

  it("starts the preflight scan then polls for the plan", async () => {
    vi.spyOn(api, "getPreflight").mockResolvedValue(cleanPlan as never);
    render(<StepReview />);
    await screen.findByText(/ready to migrate/i);
    expect(api.startPreflight).toHaveBeenCalledWith("m1");
    expect(api.getPreflight).toHaveBeenCalledWith("m1");
  });

  it("shows a clean Ready card when there are no issues", async () => {
    vi.spyOn(api, "getPreflight").mockResolvedValue(cleanPlan as never);
    render(<StepReview />);
    expect(await screen.findByText(/ready to migrate/i)).toBeInTheDocument();
    expect(screen.getByText(/~12 min/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /start migration/i })).toBeEnabled();
  });

  it("shows the Reviewing state while scanning, then renders the plan once the scan finishes", async () => {
    // First poll: the background scan is still running (scanning:true, empty estimate). Second poll: done.
    const scanningPlan = { scanning: true, issues: [], estimate: { mailboxCount: 0, folderCount: 0, messageCount: 0, totalBytes: 0, estimatedDurationSeconds: 0 } };
    vi.spyOn(api, "getPreflight")
      .mockResolvedValueOnce(scanningPlan as never)
      .mockResolvedValue(cleanPlan as never);
    render(<StepReview />);
    expect(await screen.findByRole("status", { name: /reviewing your mailboxes/i })).toBeInTheDocument();
    // The 1.5s re-poll resolves to the stored plan; the Reviewing state is replaced by the Ready card.
    expect(await screen.findByText(/ready to migrate/i, undefined, { timeout: 3000 })).toBeInTheDocument();
    expect(api.getPreflight).toHaveBeenCalledTimes(2);
  });

  it("surfaces a fetch failure as an error (not an infinite spinner)", async () => {
    vi.spyOn(api, "getPreflight").mockRejectedValue(new Error("preflight unavailable"));
    render(<StepReview />);
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(screen.queryByRole("status", { name: /reviewing your mailboxes/i })).not.toBeInTheDocument();
  });

  it("shows bulk resolution dropdowns and keeps Start enabled for non-blocking issues", async () => {
    vi.spyOn(api, "getPreflight").mockResolvedValue(issuePlan as never);
    render(<StepReview />);
    expect(await screen.findByText(/exceed outlook's depth/i)).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: /resolution for folderdepth/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /approve plan & start/i })).toBeEnabled();
  });

  it("blocks Start when a Blocker-severity issue is present", async () => {
    vi.spyOn(api, "getPreflight").mockResolvedValue(blockerPlan as never);
    render(<StepReview />);
    expect(await screen.findByText(/a blocker that must be fixed/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /approve plan & start/i })).toBeDisabled();
  });

  it("approves with the chosen resolutions", async () => {
    vi.spyOn(api, "getPreflight").mockResolvedValue(issuePlan as never);
    const approve = vi.spyOn(api, "approve").mockResolvedValue({} as never);
    render(<StepReview />);
    await screen.findByText(/exceed outlook's depth/i);
    await userEvent.click(screen.getByRole("button", { name: /approve plan & start/i }));
    await waitFor(() => expect(approve).toHaveBeenCalledWith("m1", { resolutions: { FolderDepth: "FlattenFolder" } }));
    expect(nav).toHaveBeenCalledWith("/migrations/m1/run");
  });
});
