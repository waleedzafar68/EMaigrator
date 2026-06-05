import { describe, expect, it } from "vitest";
import { formatBytes, formatDuration, formatElapsed } from "./format";

describe("format", () => {
  it("formats bytes to human units", () => {
    expect(formatBytes(262144000)).toBe("250 MB");
    expect(formatBytes(1073741824)).toBe("1.0 GB");
  });
  it("formats duration conservatively in minutes/hours", () => {
    expect(formatDuration(720)).toBe("~12 min");
    expect(formatDuration(7800)).toBe("~2h 10m");
  });
  it("formats exact elapsed time as mm:ss, h:mm:ss past an hour", () => {
    expect(formatElapsed(754)).toBe("12:34");
    expect(formatElapsed(9)).toBe("0:09");
    expect(formatElapsed(3661)).toBe("1:01:01");
    expect(formatElapsed(-5)).toBe("0:00");
  });
});
