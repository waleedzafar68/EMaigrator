import { render } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { axe } from "vitest-axe";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Dashboard } from "../routes/Dashboard";
import * as api from "../api/migrations";

describe("axe a11y", () => {
  afterEach(() => vi.restoreAllMocks());
  it("dashboard has no detectable violations", async () => {
    vi.spyOn(api, "listMigrations").mockResolvedValue([
      { id: "r1", status: "Running", wizardStep: 5, from: "imap", to: "graph", isBatch: true, scopeSummary: "218 mailboxes", mailboxCount: 218, progress: { migratedCount: 1, total: 2, currentFolder: null, msgPerMin: 1, status: "Running" }, createdAt: "2026-06-01T00:00:00Z" },
    ] as never);
    const { container, findByText } = render(<MemoryRouter><Dashboard /></MemoryRouter>);
    await findByText(/218 mailboxes/i);
    const results = await axe(container);
    expect(results).toHaveNoViolations();
  });
});
