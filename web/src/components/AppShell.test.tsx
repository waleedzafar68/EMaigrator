import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AppShell } from "./AppShell";
import * as auth from "../api/auth";

const nav = vi.fn();
vi.mock("react-router-dom", () => ({
  useLocation: () => ({ pathname: "/" }),
  useNavigate: () => nav,
  Link: ({ to, children, ...rest }: { to: string; children: React.ReactNode }) => (
    <a href={to} {...rest}>{children}</a>
  ),
  Outlet: () => null,
}));

describe("AppShell sign out", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    nav.mockReset();
  });

  it("calls the logout endpoint and navigates to /login", async () => {
    const logout = vi.spyOn(auth, "logout").mockResolvedValue(undefined);
    render(<AppShell />);
    await userEvent.click(screen.getByRole("button", { name: /sign out/i }));
    expect(logout).toHaveBeenCalledOnce();
    await waitFor(() => expect(nav).toHaveBeenCalledWith("/login"));
  });

  it("still navigates to /login when logout fails", async () => {
    vi.spyOn(auth, "logout").mockRejectedValue(new Error("boom"));
    render(<AppShell />);
    await userEvent.click(screen.getByRole("button", { name: /sign out/i }));
    await waitFor(() => expect(nav).toHaveBeenCalledWith("/login"));
  });
});
