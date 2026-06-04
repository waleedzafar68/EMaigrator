import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Dashboard, UsageWidget } from "./Dashboard";
import * as api from "../api/migrations";

// Auto-mock the migrations module; vi.mock is hoisted so Dashboard's import
// of listMigrations also gets the auto-mock.  Per-test return values are set
// with mockResolvedValue; vi.clearAllMocks resets call counts between tests.
vi.mock("../api/migrations");

const draft = { id: "d1", status: "Draft", wizardStep: 1, from: "imap", to: "graph", isBatch: false, scopeSummary: "1 mailbox", mailboxCount: 1, progress: null, createdAt: "2026-06-01T00:00:00Z" };
const running = { id: "r1", status: "Running", wizardStep: 5, from: "imap", to: "graph", isBatch: true, scopeSummary: "218 mailboxes", mailboxCount: 218, progress: { migratedCount: 126, total: 218, currentFolder: null, msgPerMin: 1402, status: "Running" }, createdAt: "2026-06-01T00:00:00Z" };

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

describe("UsageWidget", () => {
  it("renders used/quota with a bar and Upgrade link when usage is present", () => {
    render(
      <MemoryRouter>
        <UsageWidget usage={{ used: 128, quota: 200, overCapMailboxes: 0, capGb: 50 }} />
      </MemoryRouter>,
    );
    expect(screen.getByText(/128 \/ 200 mailboxes this month/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /upgrade/i })).toBeInTheDocument();
  });

  it("renders nothing when there is no usage (never a fabricated demo bar)", () => {
    const { container } = render(
      <MemoryRouter>
        <UsageWidget usage={null} />
      </MemoryRouter>,
    );
    expect(container).toBeEmptyDOMElement();
  });
});
