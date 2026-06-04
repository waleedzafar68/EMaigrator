import { render } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { StepFromTo } from "../wizard/StepFromTo";

vi.mock("react-router-dom", () => ({ useNavigate: () => vi.fn(), useOutletContext: () => ({ migration: { id: "m1" } }) }));
vi.mock("../wizard/useDraft", () => ({ useDraft: () => ({ saveEndpoints: vi.fn(), migration: { id: "m1" } }) }));

describe("keyboard nav on From & To", () => {
  it("tabs to the provider radios", async () => {
    render(<StepFromTo />);
    await userEvent.tab();
    expect(document.activeElement?.getAttribute("role")).toBe("radio");
  });
});
