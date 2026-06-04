import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { Stepper, canAdvanceTo } from "./Stepper";

describe("Stepper", () => {
  it("marks the current step and disables steps past the gate", () => {
    render(<MemoryRouter><Stepper current={1} maxReached={1} migrationId="m1" /></MemoryRouter>);
    expect(screen.getByText("Connect From").closest("[aria-current]")).toHaveAttribute("aria-current", "step");
    const future = screen.getByText("Review & plan").closest("a,button,div")!;
    expect(future).toHaveAttribute("aria-disabled", "true");
  });

  it("gates forward navigation", () => {
    expect(canAdvanceTo(2, 1)).toBe(true);
    expect(canAdvanceTo(3, 1)).toBe(false);
    expect(canAdvanceTo(0, 1)).toBe(true);
  });
});
