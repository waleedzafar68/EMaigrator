import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Dashboard } from "./Dashboard";
import * as api from "../api/migrations";
import { setDefaultHubFactory } from "../api/signalr";

// Auto-mock the migrations module; vi.mock is hoisted so Dashboard's import
// of listMigrations also gets the auto-mock.  Per-test return values are set
// with mockResolvedValue; vi.clearAllMocks resets call counts between tests.
vi.mock("../api/migrations");

const draft = { id: "d1", status: "Draft", wizardStep: 1, from: "imap", to: "graph", isBatch: false, scopeSummary: "1 mailbox", mailboxCount: 1, progress: null, createdAt: "2026-06-01T00:00:00Z" };
const running = { id: "r1", status: "Running", wizardStep: 5, from: "imap", to: "graph", isBatch: true, scopeSummary: "218 mailboxes", mailboxCount: 218, progress: { migrated: 126, total: 218, currentFolder: null, msgPerMin: 1402 }, createdAt: "2026-06-01T00:00:00Z" };

function renderDash() {
  return render(<MemoryRouter><Dashboard /></MemoryRouter>);
}

describe("Dashboard", () => {
  afterEach(() => vi.clearAllMocks());

  it("shows the welcome state when there are no migrations", async () => {
    (api.listMigrations as ReturnType<typeof vi.fn>).mockResolvedValue([]);
    renderDash();
    expect(await screen.findByRole("button", { name: /start your first migration/i })).toBeInTheDocument();
  });

  it("lists migrations with status chips, context actions, and per-row progress", async () => {
    (api.listMigrations as ReturnType<typeof vi.fn>).mockResolvedValue([draft, running]);
    renderDash();
    expect(await screen.findByText(/218 mailboxes/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /resume/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /view/i })).toBeInTheDocument();
    expect(screen.getByText("58%")).toBeInTheDocument();
  });

  it("toggles cards/list density and persists the choice", async () => {
    (api.listMigrations as ReturnType<typeof vi.fn>).mockResolvedValue([running]);
    renderDash();
    await screen.findByText(/218 mailboxes/i);
    await userEvent.click(screen.getByRole("button", { name: /list view/i }));
    await waitFor(() => expect(localStorage.getItem("em-dash-layout")).toBe("list"));
  });
});

// A minimal fake SignalR hub (mirrors api/signalr.test.ts) injected via setDefaultHubFactory so the
// dashboard's single MigrationsHubClient never opens a real connection.
function makeFakeHub() {
  const handlers = new Map<string, (...args: unknown[]) => void>();
  return {
    handlers,
    state: "Disconnected",
    on: vi.fn((name: string, cb: (...a: unknown[]) => void) => handlers.set(name, cb)),
    invoke: vi.fn().mockResolvedValue(undefined),
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
    onreconnecting: vi.fn(),
    onreconnected: vi.fn(),
    onclose: vi.fn(),
    fire(name: string, ...args: unknown[]) { handlers.get(name)?.(...args); },
  };
}

describe("Dashboard live per-row updates", () => {
  afterEach(() => {
    vi.clearAllMocks();
    setDefaultHubFactory(null);
  });

  it("updates the in-flight row's progress and status from hub events", async () => {
    const hub = makeFakeHub();
    setDefaultHubFactory(() => hub as never);
    (api.listMigrations as ReturnType<typeof vi.fn>).mockResolvedValue([running]);
    renderDash();
    await screen.findByText(/218 mailboxes/i);
    expect(screen.getByText("58%")).toBeInTheDocument();

    // Subscribe must have been invoked for the non-terminal row over the single client.
    await waitFor(() => expect(hub.invoke).toHaveBeenCalledWith("Subscribe", "r1"));

    // A Progress event carrying the migrationId routes to that row.
    hub.fire("Progress", { migrationId: "r1", migrated: 218, total: 218, currentFolder: null, msgPerMin: 0, status: "Completed" });
    expect(await screen.findByText("100%")).toBeInTheDocument();

    // The status chip (jobStatusToChip("Completed") → "Migrated") updates in place.
    await waitFor(() => expect(screen.getByRole("status", { name: /migrated/i })).toBeInTheDocument());
  });

  it("ignores Progress events whose migrationId matches no row", async () => {
    const hub = makeFakeHub();
    setDefaultHubFactory(() => hub as never);
    (api.listMigrations as ReturnType<typeof vi.fn>).mockResolvedValue([running]);
    renderDash();
    await screen.findByText(/218 mailboxes/i);
    hub.fire("Progress", { migrationId: "other", migrated: 1, total: 218, currentFolder: null, msgPerMin: 0, status: "Running" });
    // Unchanged — still 58%.
    expect(screen.getByText("58%")).toBeInTheDocument();
  });
});
