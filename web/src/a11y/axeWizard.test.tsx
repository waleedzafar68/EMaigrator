import { render } from "@testing-library/react";
import { axe } from "vitest-axe";
import { describe, expect, it, vi } from "vitest";
import { StepFromTo } from "../wizard/StepFromTo";

vi.mock("react-router-dom", () => ({ useNavigate: () => vi.fn(), useOutletContext: () => ({ migration: { id: "m1" } }) }));
vi.mock("../wizard/useDraft", () => ({ useDraft: () => ({ saveEndpoints: vi.fn(), migration: { id: "m1" } }) }));

describe("axe a11y — wizard step", () => {
  it("From & To step has no detectable violations", async () => {
    const { container } = render(<StepFromTo />);
    const results = await axe(container);
    expect(results).toHaveNoViolations();
  });
});
