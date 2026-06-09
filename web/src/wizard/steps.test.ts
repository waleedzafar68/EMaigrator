import { describe, expect, it } from "vitest";
import { stepsFor } from "./steps";

describe("stepsFor", () => {
  it("migrate keeps the full set with the mode step first and review present", () => {
    const s = stepsFor("migrate");
    expect(s).toHaveLength(7);
    expect(s[0].key).toBe("mode");
    expect(s.some((x) => x.key === "review")).toBe(true);
  });

  it("reconcile drops the review step (starts straight from scope)", () => {
    const s = stepsFor("reconcile");
    expect(s).toHaveLength(6);
    expect(s[0].key).toBe("mode");
    expect(s.some((x) => x.key === "review")).toBe(false);
  });
});
