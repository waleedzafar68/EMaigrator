import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

const tokens = readFileSync(resolve(__dirname, "tokens.css"), "utf8");

describe("design tokens", () => {
  it("defines light accent + surfaces", () => {
    expect(tokens).toContain("--accent: #0d9488");
    expect(tokens).toContain("--bg: #ffffff");
    expect(tokens).toContain("--fg: #0f172a");
    expect(tokens).toContain("--fg-muted: #64748b");
  });
  it("defines distinct semantic status colors (success != accent)", () => {
    expect(tokens).toContain("--success: #16a34a");
    expect(tokens).toContain("--throttled: #d97706");
    expect(tokens).toContain("--warning: #ca8a04");
    expect(tokens).toContain("--error: #dc2626");
  });
  it("overrides accent + bg under dark theme", () => {
    expect(tokens).toMatch(/\[data-theme="dark"\][\s\S]*--accent: #2dd4bf/);
    expect(tokens).toMatch(/\[data-theme="dark"\][\s\S]*--bg: #0b1120/);
  });
  it("defines density-driven sizing vars and AA hit target", () => {
    expect(tokens).toMatch(/\[data-density="comfortable"\][\s\S]*--hit: 44px/);
    expect(tokens).toContain("--control-h");
    expect(tokens).toContain("--body-scale");
  });
});
