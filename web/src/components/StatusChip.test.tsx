import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StatusChip } from "./StatusChip";

describe("StatusChip", () => {
  it("shows an icon and a text label (never color alone)", () => {
    render(<StatusChip status="throttled" />);
    const chip = screen.getByRole("status");
    expect(chip).toHaveAccessibleName(/slowing to respect/i);
    expect(chip.querySelector("svg")).not.toBeNull();
  });

  it("labels success as Migrated and error as Failed", () => {
    const { rerender } = render(<StatusChip status="done" />);
    expect(screen.getByText(/migrated|done/i)).toBeInTheDocument();
    rerender(<StatusChip status="error" />);
    expect(screen.getByText(/failed/i)).toBeInTheDocument();
  });
});
