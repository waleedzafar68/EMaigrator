import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

describe("CSP", () => {
  it("index.html ships a Content-Security-Policy meta with safe directives", () => {
    const html = readFileSync(resolve(__dirname, "../../index.html"), "utf8");
    expect(html).toMatch(/http-equiv=["']Content-Security-Policy["']/i);
    for (const directive of [
      "default-src 'self'", "connect-src 'self'", "img-src 'self' data:",
      "object-src 'none'", "base-uri 'self'", "frame-ancestors 'none'",
    ]) {
      expect(html).toContain(directive);
    }
  });
});
