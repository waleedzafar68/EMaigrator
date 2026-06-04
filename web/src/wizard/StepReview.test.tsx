import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StepReview } from "./StepReview";
import * as api from "../api/migrations";

const nav = vi.fn();
vi.mock("react-router-dom", () => ({ useNavigate: () => nav, useOutletContext: () => ({ migration: { id: "m1" } }) }));

const cleanPlan = { scanning: false, issues: [], estimate: { mailboxCount: 1, folderCount: 14, messageCount: 3201, totalBytes: 262144000, estimatedDurationSeconds: 720 }, usage: null };
const issuePlan = {
  scanning: false,
  issues: [{ issueType: "FolderDepth", affectedPaths: ["/a/b/c/d/e"], recommendedAction: "FlattenFolder", options: ["FlattenFolder", "RenameFolder", "SkipMessage"], severity: "Warning", description: "12 folders exceed Outlook's depth" }],
  estimate: { mailboxCount: 218, folderCount: 900, messageCount: 1200000, totalBytes: 0, estimatedDurationSeconds: 7800 },
  usage: { used: 188, quota: 200, overCapMailboxes: 2, capGb: 50 },
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

  it("shows bulk resolution dropdowns and blocks Start when over the cap", async () => {
    vi.spyOn(api, "getPreflight").mockResolvedValue(issuePlan as never);
    render(<StepReview />);
    expect(await screen.findByText(/exceed outlook's depth/i)).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: /resolution for folderdepth/i })).toBeInTheDocument();
    expect(screen.getByText(/exceed the 50 GB/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /approve plan & start/i })).toBeDisabled();
  });

  it("approves with the chosen resolutions when within quota", async () => {
    vi.spyOn(api, "getPreflight").mockResolvedValue({ ...issuePlan, usage: { used: 10, quota: 500, overCapMailboxes: 0, capGb: 50 } } as never);
    const approve = vi.spyOn(api, "approve").mockResolvedValue({} as never);
    render(<StepReview />);
    await screen.findByText(/exceed outlook's depth/i);
    await userEvent.click(screen.getByRole("button", { name: /approve plan & start/i }));
    await waitFor(() => expect(approve).toHaveBeenCalledWith("m1", { resolutions: { FolderDepth: "FlattenFolder" } }));
    expect(nav).toHaveBeenCalledWith("/migrations/m1/run");
  });
});
