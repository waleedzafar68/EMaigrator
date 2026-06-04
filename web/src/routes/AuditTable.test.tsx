import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AuditTable } from "./AuditTable";

const entries = [
  { subject: "Re: invoice 4521", messageDate: "2024-03-12T00:00:00Z", sourceFolder: "/Archive", destFolder: "/Archive", status: "migrated" as const },
  { subject: "<script>alert('xss')</script>", messageDate: "2024-01-08T00:00:00Z", sourceFolder: "/Sent", destFolder: "/Sent", status: "skipped" as const },
];

describe("AuditTable", () => {
  it("renders subjects as escaped text, never as HTML", () => {
    const { container } = render(<AuditTable entries={entries} />);
    expect(screen.getByText("Re: invoice 4521")).toBeInTheDocument();
    expect(screen.getByText("<script>alert('xss')</script>")).toBeInTheDocument();
    expect(container.querySelector("script")).toBeNull();
  });
});
