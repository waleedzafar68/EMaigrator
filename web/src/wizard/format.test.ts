import { describe, expect, it } from "vitest";
import { formatBytes, formatDuration } from "./format";

describe("format", () => {
  it("formats bytes to human units", () => {
    expect(formatBytes(262144000)).toBe("250 MB");
    expect(formatBytes(1073741824)).toBe("1.0 GB");
  });
  it("formats duration conservatively in minutes/hours", () => {
    expect(formatDuration(720)).toBe("~12 min");
    expect(formatDuration(7800)).toBe("~2h 10m");
  });
});
