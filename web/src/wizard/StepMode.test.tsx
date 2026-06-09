import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "vitest-axe";
import { describe, expect, it, vi } from "vitest";
import { StepMode } from "./StepMode";

const setModeMock = vi.fn().mockResolvedValue({ id: "m1", mode: "reconcile" });
const nav = vi.fn();
vi.mock("react-router-dom", () => ({
  useNavigate: () => nav,
  useOutletContext: () => ({ migration: { id: "m1", mode: "migrate" } }),
}));
vi.mock("../api/migrations", () => ({ setMode: (...args: unknown[]) => setModeMock(...args) }));

describe("StepMode", () => {
  it("gates Continue until a mode is picked", () => {
    render(<StepMode />);
    expect(screen.getByRole("button", { name: /continue/i })).toBeDisabled();
  });

  it("sets reconcile mode and advances to from-to", async () => {
    render(<StepMode />);
    await userEvent.click(screen.getByRole("radio", { name: /reconcile/i }));
    await userEvent.click(screen.getByRole("button", { name: /continue/i }));
    expect(setModeMock).toHaveBeenCalledWith("m1", "reconcile");
    expect(nav).toHaveBeenCalledWith("/migrations/m1/from-to");
  });

  it("has no detectable a11y violations", async () => {
    const { container } = render(<StepMode />);
    const results = await axe(container);
    expect(results).toHaveNoViolations();
  });
});
