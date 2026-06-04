import { Link, Outlet, useLocation } from "react-router-dom";
import { LayoutDashboard, Plus } from "lucide-react";
import { ThemeToggle } from "./ThemeToggle";

export function AppShell() {
  const { pathname } = useLocation();
  return (
    <div className="flex min-h-screen bg-bg text-fg">
      <aside className="w-[230px] shrink-0 border-r border-border bg-surface p-4" aria-label="Primary">
        <div className="mb-6 font-semibold">EMaigrator</div>
        <nav className="space-y-1">
          <Link to="/" aria-current={pathname === "/" ? "page" : undefined}
            className="flex items-center gap-2 rounded-[6px] px-3 py-2 hover:bg-surface-2">
            <LayoutDashboard size={16} aria-hidden /> Dashboard
          </Link>
        </nav>
      </aside>
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-10 flex h-[58px] items-center justify-between border-b border-border bg-bg/80 px-6 backdrop-blur">
          <h1 className="text-[length:var(--fs-h2)] font-semibold">Migrations</h1>
          <div className="flex items-center gap-3">
            <Link to="/migrations/new"
              className="inline-flex items-center gap-1.5 rounded-[8px] bg-accent px-3 py-1.5 text-accent-fg">
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
