import { describe, expect, it } from "vitest";
import { parsePairsCsv } from "./csv";

describe("parsePairsCsv", () => {
  it("parses pairs with a header row", () => {
    const { pairs, errors } = parsePairsCsv("source_mailbox,destination_mailbox\na@x.com,a@y.com\nb@x.com,b@y.com");
    expect(pairs).toEqual([
      { sourceMailbox: "a@x.com", destMailbox: "a@y.com" },
      { sourceMailbox: "b@x.com", destMailbox: "b@y.com" },
    ]);
    expect(errors).toHaveLength(0);
  });
  it("parses without a header and trims", () => {
    const { pairs } = parsePairsCsv(" a@x.com , a@y.com ");
    expect(pairs[0]).toEqual({ sourceMailbox: "a@x.com", destMailbox: "a@y.com" });
  });
  it("reports malformed rows", () => {
    const { errors } = parsePairsCsv("a@x.com\nb@x.com,b@y.com");
    expect(errors[0]).toMatch(/line 1/i);
  });
});
