import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StepConnect } from "./StepConnect";
import * as api from "../api/migrations";

const nav = vi.fn();
vi.mock("react-router-dom", () => ({
  useNavigate: () => nav,
  useParams: () => ({ side: "from" }),
  useOutletContext: () => ({ migration: { id: "m1", from: "imap", to: "graph" } }),
}));

describe("StepConnect (IMAP from / WorkMail)", () => {
  afterEach(() => vi.restoreAllMocks());

  it("shows the region dropdown and a server host preview that tracks the region", async () => {
    render(<StepConnect />);
    expect(screen.getByLabelText(/region/i)).toBeInTheDocument();
    expect(screen.getByText(/imap\.mail\.us-east-1\.awsapps\.com/i)).toBeInTheDocument();
  });

  it("disables Continue until a test connection succeeds", async () => {
    vi.spyOn(api, "putConnection").mockResolvedValue({} as never);
    vi.spyOn(api, "testConnection").mockResolvedValue({ ok: true, folderCount: 14, messageCount: 3201 } as never);
    render(<StepConnect />);
    expect(screen.getByRole("button", { name: /continue/i })).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/username/i), "old@biz.com");
    await userEvent.type(screen.getByLabelText(/password/i), "app-pw");
    await userEvent.click(screen.getByRole("button", { name: /test connection/i }));
    expect(await screen.findByText(/found 14 folders, 3,?201 messages/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /continue/i })).toBeEnabled();
  });

  it("renders a catalog-driven error with expandable technical details on failure", async () => {
    vi.spyOn(api, "putConnection").mockResolvedValue({} as never);
    vi.spyOn(api, "testConnection").mockResolvedValue({
      ok: false, folderCount: 0, messageCount: 0,
      errorCode: "AUTH_FAILED", rawDetail: "IMAP NO [AUTHENTICATIONFAILED]",
    } as never);
    render(<StepConnect />);
    await userEvent.type(screen.getByLabelText(/username/i), "old@biz.com");
    await userEvent.type(screen.getByLabelText(/password/i), "wrong");
    await userEvent.click(screen.getByRole("button", { name: /test connection/i }));
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    await userEvent.click(screen.getByText(/technical details/i));
    expect(screen.getByText(/AUTHENTICATIONFAILED/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /continue/i })).toBeDisabled();
  });
});
