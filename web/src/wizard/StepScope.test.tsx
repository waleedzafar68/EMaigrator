import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { StepScope } from "./StepScope";

const save = vi.fn().mockResolvedValue(undefined);
const nav = vi.fn();
let ctx = { migration: { id: "m1", from: "imap", to: "graph", isBatch: false }, canBatch: true };
vi.mock("react-router-dom", () => ({ useNavigate: () => nav, useOutletContext: () => ctx }));
vi.mock("./useDraft", () => ({ useDraft: () => ({ saveScope: save, migration: ctx.migration }) }));

describe("StepScope", () => {
  it("disables Batch with an explanation when single-only creds", () => {
    ctx = { migration: { id: "m1", from: "imap", to: "graph", isBatch: false }, canBatch: false };
    render(<StepScope />);
    const batch = screen.getByRole("button", { name: /batch/i });
    expect(batch).toBeDisabled();
    expect(screen.getByText(/reconnect using admin access/i)).toBeInTheDocument();
  });

  it("imports a CSV into the pair table in batch mode", async () => {
    ctx = { migration: { id: "m1", from: "imap", to: "graph", isBatch: true }, canBatch: true };
    render(<StepScope />);
    await userEvent.click(screen.getByRole("button", { name: /batch/i }));
    const file = new File(["a@x.com,a@y.com\nb@x.com,b@y.com"], "pairs.csv", { type: "text/csv" });
    await userEvent.upload(screen.getByLabelText(/import csv/i), file);
    expect(await screen.findByText("a@x.com")).toBeInTheDocument();
    expect(screen.getByText("b@y.com")).toBeInTheDocument();
  });

  it("keeps Advanced collapsed by default", () => {
    ctx = { migration: { id: "m1", from: "imap", to: "graph", isBatch: false }, canBatch: true };
    render(<StepScope />);
    expect(screen.queryByLabelText(/include folders/i)).not.toBeInTheDocument();
  });
});
