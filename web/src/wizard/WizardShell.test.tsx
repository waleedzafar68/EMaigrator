import { render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { WizardShell, NewMigrationRedirect } from "./WizardShell";
import { ApiError } from "../api/client";
import * as migrationsApi from "../api/migrations";
import * as providersApi from "../api/providers";

const nav = vi.fn();
vi.mock("react-router-dom", () => ({
  useParams: () => ({ id: "m1" }),
  useLocation: () => ({ pathname: "/migrations/m1/scope" }),
  useNavigate: () => nav,
  // Render the wizard context so tests can assert the computed canBatch flag.
  Outlet: ({ context }: { context: { canBatch: boolean } }) => (
    <div data-testid="ctx">canBatch={String(context.canBatch)}</div>
  ),
}));

vi.mock("./Stepper", () => ({
  Stepper: (props: { current: number; maxReached: number }) => (
    <div data-testid="stepper" data-current={props.current} data-max={props.maxReached} />
  ),
}));

const migration = {
  id: "m1", status: "Draft", wizardStep: 3, from: "imap", to: "graph",
  isBatch: false, scopeSummary: null, mailboxCount: 1, progress: null, createdAt: "2026-06-01T00:00:00Z",
};

describe("WizardShell", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    nav.mockReset();
  });

  it("renders an error state (not an infinite skeleton) when getMigration fails", async () => {
    vi.spyOn(migrationsApi, "getMigration").mockRejectedValue(
      new ApiError(500, "INTERNAL", "Server exploded", null, "trace-1"),
    );
    vi.spyOn(providersApi, "listProviders").mockResolvedValue([]);
    render(<WizardShell />);
    expect(await screen.findByRole("alert")).toHaveTextContent(/server exploded/i);
    expect(screen.queryByRole("status", { name: /loading/i })).not.toBeInTheDocument();
  });

  it("derives canBatch from the providers response (API authority)", async () => {
    vi.spyOn(migrationsApi, "getMigration").mockResolvedValue(migration as never);
    vi.spyOn(providersApi, "listProviders").mockResolvedValue([
      { id: "imap", canBeSource: true, canBeDestination: false, canBatch: false, supportedAuth: [] },
      // graph reports canBatch=false here even though the heuristic says true — proves API wins.
      { id: "graph", canBeSource: true, canBeDestination: true, canBatch: false, supportedAuth: [] },
    ]);
    render(<WizardShell />);
    await waitFor(() => expect(screen.getByTestId("ctx")).toHaveTextContent("canBatch=false"));
  });

  it("falls back to the heuristic when the providers fetch fails", async () => {
    vi.spyOn(migrationsApi, "getMigration").mockResolvedValue(migration as never);
    vi.spyOn(providersApi, "listProviders").mockRejectedValue(new Error("offline"));
    render(<WizardShell />);
    // to === graph → heuristic canBatch=true
    await waitFor(() => expect(screen.getByTestId("ctx")).toHaveTextContent("canBatch=true"));
  });

  it("highlights the step for the current ROUTE, not the server wizardStep", async () => {
    // Route is /scope (index 4 in the migrate step set: mode, from-to, connect/from, connect/to,
    // scope, review, run) while the server reports wizardStep=3 — the route must win, otherwise the
    // stepper sits on "Connect To" while the user is on Scope/Run.
    vi.spyOn(migrationsApi, "getMigration").mockResolvedValue(migration as never);
    vi.spyOn(providersApi, "listProviders").mockResolvedValue([]);
    render(<WizardShell />);
    await waitFor(() => expect(screen.getByTestId("stepper")).toHaveAttribute("data-current", "4"));
    expect(Number(screen.getByTestId("stepper").getAttribute("data-max"))).toBeGreaterThanOrEqual(4);
  });
});

describe("NewMigrationRedirect", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    nav.mockReset();
  });

  it("renders an error state when createMigration fails", async () => {
    vi.spyOn(migrationsApi, "createMigration").mockRejectedValue(
      new ApiError(500, "INTERNAL", "Could not create", null, null),
    );
    render(<NewMigrationRedirect />);
    expect(await screen.findByRole("alert")).toHaveTextContent(/could not create/i);
  });
});
