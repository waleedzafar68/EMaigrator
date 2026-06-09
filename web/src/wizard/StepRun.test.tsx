import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StepRun } from "./StepRun";
import * as api from "../api/migrations";
import * as stream from "../api/useMigrationStream";

let outlet: { migration: { id: string; isBatch: boolean; mode?: string; from?: string; to?: string; status?: string } } = {
  migration: { id: "m1", isBatch: false },
};
vi.mock("react-router-dom", () => ({
  useOutletContext: () => outlet,
  Link: ({ to, children }: { to: string; children: React.ReactNode }) => <a href={String(to)}>{children}</a>,
}));

beforeEach(() => {
  outlet = { migration: { id: "m1", isBatch: false } };
  sessionStorage.clear();
});

function mockStream(over: Partial<stream.MigrationStream>) {
  vi.spyOn(stream, "useMigrationStream").mockReturnValue({
    connectionState: "connected",
    progress: { migrated: 2310, total: 3201, currentFolder: "/Archive/2023", msgPerMin: 412, status: "Running" },
    status: "Running",
    needsDecision: [],
    ...over,
  });
}

describe("StepRun", () => {
  afterEach(() => vi.restoreAllMocks());

  it("renders live progress with current folder and throughput", () => {
    mockStream({});
    render(<StepRun />);
    expect(screen.getByText(/2,310 \/ 3,201/)).toBeInTheDocument();
    expect(screen.getByText(/\/Archive\/2023/)).toBeInTheDocument();
    expect(screen.getByText(/412/)).toBeInTheDocument();
    expect(screen.getByText(/safe to close/i)).toBeInTheDocument();
  });

  it("shows a throttling chip when the throttled flag is set (not via a bogus status)", () => {
    vi.spyOn(stream, "useMigrationStream").mockReturnValue({
      connectionState: "connected", status: "Running", needsDecision: [],
      progress: { migrated: 5, total: 10, currentFolder: null, msgPerMin: 0, status: "Running", throttled: true },
    });
    render(<StepRun />);
    expect(screen.getByText(/slowing to respect limits/i)).toBeInTheDocument();
  });

  it("shows a reconnecting indicator without looking like failure", () => {
    mockStream({ connectionState: "reconnecting" });
    render(<StepRun />);
    expect(screen.getByText(/reconnecting/i)).toBeInTheDocument();
  });

  it("wires pause/resume/cancel controls", async () => {
    mockStream({});
    const pause = vi.spyOn(api, "pause").mockResolvedValue({} as never);
    render(<StepRun />);
    await userEvent.click(screen.getByRole("button", { name: /pause/i }));
    expect(pause).toHaveBeenCalledWith("m1");
  });

  it("shows Resume (not Pause) when paused", () => {
    mockStream({ status: "Paused" });
    render(<StepRun />);
    expect(screen.getByRole("button", { name: /resume/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /pause/i })).not.toBeInTheDocument();
  });

  it("renders folder-based reconcile progress + tiles, not the migrate %-block", () => {
    outlet = { migration: { id: "m1", isBatch: false, mode: "reconcile" } };
    vi.spyOn(stream, "useMigrationStream").mockReturnValue({
      connectionState: "connected", status: "Running", needsDecision: [],
      progress: {
        migrated: 318, total: 3158, currentFolder: "/Inbox", msgPerMin: 0, status: "Running",
        reconcile: { foldersDone: 3, folderTotal: 650, copied: 318, backfilled: 12, skipped: 2840 },
      },
    });
    render(<StepRun />);
    expect(screen.getByText(/folder 3 of/i)).toBeInTheDocument();
    expect(screen.getByText("318")).toBeInTheDocument();        // Copied
    expect(screen.getByText("12")).toBeInTheDocument();         // Attachments backfilled
    expect(screen.getByText(/2,840/)).toBeInTheDocument();      // Already-complete skipped
    // The migrate message-count ratio block must NOT render in reconcile mode.
    expect(screen.queryByText("318 / 3,158")).not.toBeInTheDocument();
  });

  it("reconcile mode without progress shows the Reconciling frame, not the migrate % view", () => {
    outlet = { migration: { id: "m1", isBatch: false, mode: "reconcile" } };
    vi.spyOn(stream, "useMigrationStream").mockReturnValue({
      connectionState: "connected", status: null, needsDecision: [], progress: null,
    });
    render(<StepRun />);
    expect(screen.getByRole("heading", { name: /reconciling/i })).toBeInTheDocument();
    expect(screen.getByText(/scanning folders/i)).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /migrating/i })).not.toBeInTheDocument();
  });

  it("rehydrates reconcile progress + activity from sessionStorage after a refresh", () => {
    outlet = { migration: { id: "m1", isBatch: false, mode: "reconcile" } };
    sessionStorage.setItem("em-run:m1", JSON.stringify({
      progress: {
        migrated: 4, total: 5, currentFolder: "SENT", msgPerMin: 0, status: "Running",
        reconcile: { foldersDone: 1, folderTotal: 650, copied: 4, backfilled: 0, skipped: 1 },
      },
      activity: ["SENT"],
    }));
    vi.spyOn(stream, "useMigrationStream").mockReturnValue({
      connectionState: "connected", status: null, needsDecision: [], progress: null,
    });
    render(<StepRun />);
    expect(screen.getByText(/folder 1 of/i)).toBeInTheDocument();
    expect(screen.getByText("SENT")).toBeInTheDocument(); // activity feed restored
  });

  it("reconcile shows a completion banner with a results link when the run finishes", () => {
    outlet = { migration: { id: "m1", isBatch: false, mode: "reconcile" } };
    vi.spyOn(stream, "useMigrationStream").mockReturnValue({
      connectionState: "connected", status: "Partial", needsDecision: [],
      progress: {
        migrated: 362, total: 363, currentFolder: null, msgPerMin: 0, status: "Partial",
        reconcile: { foldersDone: 650, folderTotal: 650, copied: 820, backfilled: 38, skipped: 276 },
      },
    });
    render(<StepRun />);
    expect(screen.getByText(/reconcile finished/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /view results/i })).toHaveAttribute("href", "/migrations/m1/results");
    expect(screen.queryByRole("button", { name: /pause/i })).not.toBeInTheDocument();
  });

  it("reconcile shows the terminal banner on a cold load (REST status, no stream)", () => {
    // After the run, no further events arrive — a reloaded page must learn "done" from the REST
    // migration status, not the stream.
    outlet = { migration: { id: "m1", isBatch: false, mode: "reconcile", status: "Completed" } };
    vi.spyOn(stream, "useMigrationStream").mockReturnValue({
      connectionState: "connected", status: null, needsDecision: [], progress: null,
    });
    render(<StepRun />);
    expect(screen.getByText(/reconcile complete/i)).toBeInTheDocument();
    expect(screen.queryByText(/scanning folders/i)).not.toBeInTheDocument();
  });
});
