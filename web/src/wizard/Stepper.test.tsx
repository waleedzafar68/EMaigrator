import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { Stepper, canAdvanceTo } from "./Stepper";
import { stepsFor } from "./steps";

describe("Stepper", () => {
  it("marks the current step and disables steps past the gate", () => {
    render(<MemoryRouter><Stepper current={1} maxReached={1} migrationId="m1" /></MemoryRouter>);
    // The prepended "mode" step shifts every index by one → index 1 is now "From & To".
    expect(screen.getByText("From & To").closest("[aria-current]")).toHaveAttribute("aria-current", "step");
    const future = screen.getByText("Review & plan").closest("a,button,div")!;
    expect(future).toHaveAttribute("aria-disabled", "true");
  });

  it("renders the reconcile mode label and the reconcile step set", () => {
    render(
      <MemoryRouter>
        <Stepper current={0} maxReached={0} migrationId="m1" steps={stepsFor("reconcile")} mode="reconcile" />
      </MemoryRouter>,
    );
    expect(screen.getByText("Reconcile / repair")).toBeInTheDocument();
    // Reconcile omits the Review & plan step.
    expect(screen.queryByText("Review & plan")).not.toBeInTheDocument();
  });

  it("gates forward navigation", () => {
    expect(canAdvanceTo(2, 1)).toBe(true);
    expect(canAdvanceTo(3, 1)).toBe(false);
    expect(canAdvanceTo(0, 1)).toBe(true);
  });
});
