import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { StepFromTo } from "./StepFromTo";

const save = vi.fn().mockResolvedValue(undefined);
const nav = vi.fn();
vi.mock("react-router-dom", () => ({ useNavigate: () => nav, useOutletContext: () => ({ migration: { id: "m1" } }) }));
vi.mock("./useDraft", () => ({ useDraft: () => ({ saveEndpoints: save, migration: { id: "m1" } }) }));

describe("StepFromTo", () => {
  it("gates Continue until both providers chosen and updates the summary", async () => {
    render(<StepFromTo />);
    const cont = screen.getByRole("button", { name: /continue/i });
    expect(cont).toBeDisabled();
    await userEvent.click(screen.getByRole("radio", { name: /from amazon workmail|from workmail/i }));
    await userEvent.click(screen.getByRole("radio", { name: /to microsoft 365/i }));
    expect(screen.getByText(/from workmail to microsoft 365/i)).toBeInTheDocument();
    expect(cont).toBeEnabled();
  });

  it("saves endpoints and advances on Continue", async () => {
    render(<StepFromTo />);
    await userEvent.click(screen.getByRole("radio", { name: /from workmail/i }));
    await userEvent.click(screen.getByRole("radio", { name: /to google/i }));
    await userEvent.click(screen.getByRole("button", { name: /continue/i }));
    expect(save).toHaveBeenCalledWith("imap", "gmail");
    expect(nav).toHaveBeenCalledWith("/migrations/m1/connect/from");
  });
});
