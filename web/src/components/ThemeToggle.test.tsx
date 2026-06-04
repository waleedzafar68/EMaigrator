import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ThemeToggle } from "./ThemeToggle";

describe("ThemeToggle", () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute("data-theme");
    vi.stubGlobal("matchMedia", (q: string) => ({ matches: false, media: q, addEventListener: () => {}, removeEventListener: () => {} }));
  });
  afterEach(() => vi.unstubAllGlobals());

  it("applies and persists dark via keyboard activation", async () => {
    render(<ThemeToggle />);
    const dark = screen.getByRole("button", { name: /dark/i });
    dark.focus();
    await userEvent.keyboard("{Enter}");
    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(localStorage.getItem("em-theme")).toBe("dark");
    expect(dark).toHaveAttribute("aria-pressed", "true");
  });
});
