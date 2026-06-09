import { Link, Outlet, useLocation, useNavigate } from "react-router-dom";
import { LayoutDashboard, LogOut, Mailbox, Plus } from "lucide-react";
import { logout } from "../api/auth";
import { buttonVariants } from "./ui/button";
import { ThemeToggle } from "./ThemeToggle";

export function AppShell() {
  const { pathname } = useLocation();
  const navigate = useNavigate();

  async function onSignOut() {
    try {
      await logout();
    } catch {
      // Ignore — clearing the cookie is best-effort; we still send the user to /login.
    } finally {
      navigate("/login");
    }
  }

  const isDashboard = pathname === "/";

  return (
    <div className="flex min-h-screen bg-bg text-fg">
      <aside className="flex w-[230px] shrink-0 flex-col border-r border-border bg-surface p-4" aria-label="Primary">
        <div className="mb-6 flex items-center gap-2 px-1 font-semibold tracking-tight">
          <span className="flex h-7 w-7 items-center justify-center rounded-md bg-accent text-accent-fg">
            <Mailbox size={16} aria-hidden />
          </span>
          EMaigrator
        </div>
        <nav className="space-y-1">
          <Link
            to="/"
            aria-current={isDashboard ? "page" : undefined}
            className={`flex items-center gap-2 rounded-[var(--radius)] px-3 py-2 text-sm transition-colors ${
              isDashboard
                ? "bg-accent-subtle font-medium text-accent"
                : "text-fg-muted hover:bg-surface-2 hover:text-fg"
            }`}
          >
            <LayoutDashboard size={16} aria-hidden /> Dashboard
          </Link>
        </nav>
        <button
          type="button"
          onClick={() => void onSignOut()}
          className="mt-auto flex items-center gap-2 rounded-[var(--radius)] px-3 py-2 text-left text-sm text-fg-muted transition-colors hover:bg-surface-2 hover:text-fg"
        >
          <LogOut size={16} aria-hidden /> Sign out
        </button>
      </aside>
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-10 flex h-[58px] items-center justify-between border-b border-border bg-bg/80 px-6 backdrop-blur">
          <h1 className="text-[length:var(--fs-h2)] font-semibold">Migrations</h1>
          <div className="flex items-center gap-3">
            <Link to="/migrations/new" className={buttonVariants({ size: "sm" })}>
              <Plus size={16} aria-hidden /> New Migration
            </Link>
            <ThemeToggle />
          </div>
        </header>
        <main className="flex-1 overflow-auto p-6"><Outlet /></main>
      </div>
    </div>
  );
}
