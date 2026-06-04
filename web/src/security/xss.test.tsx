import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, resolve } from "node:path";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AuditTable } from "../routes/AuditTable";

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const p = join(dir, name);
    return statSync(p).isDirectory() ? walk(p) : [p];
  });
}

describe("XSS-safe rendering", () => {
  it("renders a script-payload subject and onerror folder as escaped text", () => {
    const { container } = render(
      <AuditTable entries={[
        { subject: "<script>alert('xss')</script>", messageDate: "2024-01-08T00:00:00Z", sourceFolder: "<img src=x onerror=alert(1)>", destFolder: "/Sent", status: "skipped" },
      ]} />,
    );
    expect(screen.getByText("<script>alert('xss')</script>")).toBeInTheDocument();
    expect(screen.getByText("<img src=x onerror=alert(1)>")).toBeInTheDocument();
    expect(container.querySelector("script")).toBeNull();
    expect(container.querySelector("img")).toBeNull();
  });

  it("uses no dangerouslySetInnerHTML anywhere in src", () => {
    const srcRoot = resolve(__dirname, "..");
    const offenders = walk(srcRoot)
      .filter((f) => /\.(tsx?|jsx?)$/.test(f) && !f.includes(`${"security"}`))
      .filter((f) => readFileSync(f, "utf8").includes("dangerouslySetInnerHTML"));
    expect(offenders).toEqual([]);
  });
});
