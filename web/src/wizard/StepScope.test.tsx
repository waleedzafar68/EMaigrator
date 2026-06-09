import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { StepScope } from "./StepScope";

const save = vi.fn().mockResolvedValue(undefined);
const reconcileMock = vi.fn().mockResolvedValue(undefined);
const nav = vi.fn();
let ctx = { migration: { id: "m1", from: "imap", to: "graph", isBatch: false }, canBatch: true, mode: "migrate" };
vi.mock("react-router-dom", () => ({ useNavigate: () => nav, useOutletContext: () => ctx }));
vi.mock("./useDraft", () => ({ useDraft: () => ({ saveScope: save, migration: ctx.migration }) }));
vi.mock("../api/migrations", () => ({ reconcile: (...args: unknown[]) => reconcileMock(...args) }));

beforeEach(() => {
  nav.mockClear();
  save.mockClear();
  save.mockResolvedValue(undefined);
  reconcileMock.mockClear();
  reconcileMock.mockResolvedValue(undefined);
});

describe("StepScope", () => {
  it("disables Batch with an explanation when single-only creds", () => {
    ctx = { migration: { id: "m1", from: "imap", to: "graph", isBatch: false }, canBatch: false, mode: "migrate" };
    render(<StepScope />);
    const batch = screen.getByRole("button", { name: /batch/i });
    expect(batch).toBeDisabled();
    expect(screen.getByText(/reconnect using admin access/i)).toBeInTheDocument();
  });

  it("imports a CSV into the pair table in batch mode", async () => {
    ctx = { migration: { id: "m1", from: "imap", to: "graph", isBatch: true }, canBatch: true, mode: "migrate" };
    render(<StepScope />);
    await userEvent.click(screen.getByRole("button", { name: /batch/i }));
    const file = new File(["a@x.com,a@y.com\nb@x.com,b@y.com"], "pairs.csv", { type: "text/csv" });
    await userEvent.upload(screen.getByLabelText(/import csv/i), file);
    expect(await screen.findByText("a@x.com")).toBeInTheDocument();
    expect(screen.getByText("b@y.com")).toBeInTheDocument();
  });

  it("keeps Advanced collapsed by default", () => {
    ctx = { migration: { id: "m1", from: "imap", to: "graph", isBatch: false }, canBatch: true, mode: "migrate" };
    render(<StepScope />);
    expect(screen.queryByLabelText(/include folders/i)).not.toBeInTheDocument();
  });

  it("reconcile mode shows Match by and hides Advanced; migrate shows Advanced", () => {
    ctx = { migration: { id: "m1", from: "imap", to: "graph", isBatch: false }, canBatch: true, mode: "reconcile" };
    render(<StepScope />);
    expect(screen.getByText(/match by/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^advanced$/i })).not.toBeInTheDocument();
  });

  it("migrate mode shows Advanced and no Match by", () => {
    ctx = { migration: { id: "m1", from: "imap", to: "graph", isBatch: false }, canBatch: true, mode: "migrate" };
    render(<StepScope />);
    expect(screen.getByRole("button", { name: /^advanced$/i })).toBeInTheDocument();
    expect(screen.queryByText(/match by/i)).not.toBeInTheDocument();
  });

  it("surfaces an error alert when saveScope rejects (no silent no-op)", async () => {
    ctx = { migration: { id: "m1", from: "imap", to: "graph", isBatch: false }, canBatch: true, mode: "migrate" };
    save.mockRejectedValueOnce(new Error("boom"));
    render(<StepScope />);
    await userEvent.click(screen.getByRole("button", { name: /continue/i }));
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(nav).not.toHaveBeenCalled();
  });

  it("disables Continue in batch mode until a valid pair exists", async () => {
    ctx = { migration: { id: "m1", from: "imap", to: "graph", isBatch: true }, canBatch: true, mode: "migrate" };
    render(<StepScope />);
    await userEvent.click(screen.getByRole("button", { name: /batch/i }));
    expect(screen.getByRole("button", { name: /continue/i })).toBeDisabled();
  });

  it("reconcile Continue saves scope, starts reconcile, and goes to run", async () => {
    ctx = { migration: { id: "m1", from: "imap", to: "graph", isBatch: false }, canBatch: true, mode: "reconcile" };
    render(<StepScope />);
    await userEvent.click(screen.getByRole("button", { name: /start reconcile/i }));
    expect(save).toHaveBeenCalled();
    expect(reconcileMock).toHaveBeenCalledWith("m1");
    expect(nav).toHaveBeenCalledWith("/migrations/m1/run");
  });
});
