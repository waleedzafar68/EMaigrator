import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StepConnect } from "./StepConnect";
import * as api from "../api/migrations";

// The provider rendered is migration[side]; side is "from", so we vary migration.from per test.
const h = vi.hoisted(() => ({
  migration: { id: "m1", from: "graph", to: "imap" } as { id: string; from: string; to: string },
}));

vi.mock("react-router-dom", () => ({
  useNavigate: () => vi.fn(),
  useParams: () => ({ side: "from" }),
  useOutletContext: () => ({ migration: h.migration }),
}));

describe("StepConnect (OAuth providers)", () => {
  afterEach(() => vi.restoreAllMocks());

  it("Graph: collects tenant/client/account/secret and sends the GraphAppOAuth payload", async () => {
    h.migration = { id: "m1", from: "graph", to: "imap" };
    const put = vi.spyOn(api, "putConnection").mockResolvedValue({} as never);
    vi.spyOn(api, "testConnection").mockResolvedValue({ ok: true, folderCount: 9, messageCount: 100 } as never);
    render(<StepConnect />);

    await userEvent.type(screen.getByLabelText("Tenant ID"), "tid");
    await userEvent.type(screen.getByLabelText("Client ID"), "cid");
    await userEvent.type(screen.getByLabelText("Account email"), "user@contoso.com");
    await userEvent.type(screen.getByLabelText("Client secret"), "shh");
    await userEvent.click(screen.getByRole("button", { name: /test connection/i }));

    expect(put).toHaveBeenCalledWith("m1", "from", {
      auth: "GraphAppOAuth",
      settings: { tenantId: "tid", clientId: "cid", accountEmail: "user@contoso.com" },
      secret: "shh",
    });
    expect(await screen.findByText(/found 9 folders/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /continue/i })).toBeEnabled();
  });

  it("Gmail: collects account + service-account JSON and sends the GmailServiceAccountDwd payload", async () => {
    h.migration = { id: "m1", from: "gmail", to: "imap" };
    const put = vi.spyOn(api, "putConnection").mockResolvedValue({} as never);
    vi.spyOn(api, "testConnection").mockResolvedValue({ ok: true, folderCount: 12, messageCount: 50 } as never);
    render(<StepConnect />);

    await userEvent.type(screen.getByLabelText("Account email"), "user@biz.com");
    // fireEvent (not userEvent.type) for the JSON: userEvent treats { } as special key syntax.
    fireEvent.change(screen.getByLabelText("Service account JSON"), {
      target: { value: '{"type":"service_account"}' },
    });
    await userEvent.click(screen.getByRole("button", { name: /test connection/i }));

    expect(put).toHaveBeenCalledWith("m1", "from", {
      auth: "GmailServiceAccountDwd",
      settings: { accountEmail: "user@biz.com" },
      secret: '{"type":"service_account"}',
    });
  });

  it("Graph: surfaces a provider-specific failure (admin consent / secret), not the WorkMail copy", async () => {
    h.migration = { id: "m1", from: "graph", to: "imap" };
    vi.spyOn(api, "putConnection").mockResolvedValue({} as never);
    vi.spyOn(api, "testConnection").mockResolvedValue({
      ok: false, folderCount: 0, messageCount: 0, errorCode: "GRAPH_AUTH_FAILED", rawDetail: "graph:UNAUTHORIZED",
    } as never);
    render(<StepConnect />);

    await userEvent.type(screen.getByLabelText("Client secret"), "wrong");
    await userEvent.click(screen.getByRole("button", { name: /test connection/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/admin consent was granted for Mail\.ReadWrite/i);
    expect(screen.getByRole("button", { name: /continue/i })).toBeDisabled();
  });
});
