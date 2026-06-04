import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { EmptyState } from "./EmptyState";
import { ErrorState } from "./ErrorState";
import { ReconnectingIndicator } from "./ReconnectingIndicator";
import { Skeleton } from "./Skeleton";

describe("global states", () => {
  it("Skeleton announces loading and is never blank", () => {
    render(<Skeleton label="Loading migrations" />);
    const el = screen.getByRole("status");
    expect(el).toHaveAttribute("aria-busy", "true");
    expect(el).toHaveAccessibleName(/loading migrations/i);
  });

  it("EmptyState shows a single primary action", () => {
    render(<EmptyState title="No migrations yet" actionLabel="Start" onAction={() => {}} />);
    expect(screen.getByRole("button", { name: /start/i })).toBeInTheDocument();
  });

  it("ErrorState retries", async () => {
    const onRetry = vi.fn();
    render(<ErrorState message="It broke" onRetry={onRetry} />);
    await userEvent.click(screen.getByRole("button", { name: /retry/i }));
    expect(onRetry).toHaveBeenCalled();
  });

  it("ReconnectingIndicator only renders while reconnecting", () => {
    const { rerender, queryByText } = render(<ReconnectingIndicator state="connected" />);
    expect(queryByText(/reconnecting/i)).toBeNull();
    rerender(<ReconnectingIndicator state="reconnecting" />);
    expect(queryByText(/reconnecting/i)).not.toBeNull();
  });
});
