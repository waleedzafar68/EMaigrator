import { describe, expect, it } from "vitest";
import { imapDefaults, workmailHost, WORKMAIL_REGIONS } from "./connectPresets";

describe("connect presets", () => {
  it("builds the WorkMail host from region", () => {
    expect(workmailHost("us-east-1")).toBe("imap.mail.us-east-1.awsapps.com");
    expect(workmailHost("eu-west-1")).toBe("imap.mail.eu-west-1.awsapps.com");
  });
  it("exposes exactly the three supported regions", () => {
    expect(WORKMAIL_REGIONS).toEqual(["us-east-1", "us-west-2", "eu-west-1"]);
  });
  it("defaults to secure IMAP on 993", () => {
    expect(imapDefaults.port).toBe(993);
    expect(imapDefaults.ssl).toBe(true);
  });
});
