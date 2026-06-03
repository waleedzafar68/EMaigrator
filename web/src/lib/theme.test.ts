import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { applyTheme, loadTheme, resolveTheme } from "./theme";

describe("theme", () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute("data-theme");
    vi.stubGlobal("matchMedia", (q: string) => ({
      matches: q.includes("dark"),
      media: q,
      addEventListener: () => {},
      removeEventListener: () => {},
    }));
  });
  afterEach(() => vi.unstubAllGlobals());

  it("defaults to system when nothing persisted", () => {
    expect(loadTheme()).toBe("system");
  });
  it("applies and persists an explicit theme", () => {
    applyTheme("dark");
    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(localStorage.getItem("em-theme")).toBe("dark");
    expect(loadTheme()).toBe("dark");
  });
  it("resolves system to the media-query result", () => {
    expect(resolveTheme("system")).toBe("dark");
    expect(resolveTheme("light")).toBe("light");
  });
});
