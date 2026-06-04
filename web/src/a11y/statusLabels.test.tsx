import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StatusChip, type ChipStatus } from "../components/StatusChip";

describe("status is never color alone", () => {
  it("every status chip exposes a text label", () => {
    const statuses: ChipStatus[] = ["done", "running", "throttled", "warning", "error", "queued"];
    for (const s of statuses) {
      const { unmount } = render(<StatusChip status={s} />);
      expect(screen.getByRole("status").textContent?.trim().length ?? 0).toBeGreaterThan(0);
      unmount();
    }
  });
});
