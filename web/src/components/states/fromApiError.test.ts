import { describe, expect, it } from "vitest";
import { ApiError } from "../../api/client";
import { errorAlertProps } from "./fromApiError";

describe("errorAlertProps", () => {
  it("maps an ApiError to plain message + technical detail + trace id", () => {
    const e = new ApiError(401, "AUTH_FAILED", "We couldn't sign in to WorkMail.", "IMAP NO [AUTHENTICATIONFAILED]", "4f9c-21a8");
    const props = errorAlertProps(e);
    expect(props.message).toBe("We couldn't sign in to WorkMail.");
    expect(props.technicalDetail).toContain("AUTHENTICATIONFAILED");
    expect(props.traceId).toBe("4f9c-21a8");
  });
  it("falls back to a generic message for unknown errors", () => {
    const props = errorAlertProps(new Error("boom"));
    expect(props.message).toMatch(/something went wrong/i);
    expect(props.technicalDetail).toBeNull();
  });
});
