import { Monitor, Moon, Sun } from "lucide-react";
import { useState, type ReactNode } from "react";
import { applyTheme, loadTheme, type Theme } from "../lib/theme";

const OPTS: { value: Theme; icon: ReactNode; label: string }[] = [
  { value: "light", icon: <Sun size={15} aria-hidden />, label: "Light" },
  { value: "dark", icon: <Moon size={15} aria-hidden />, label: "Dark" },
  { value: "system", icon: <Monitor size={15} aria-hidden />, label: "System" },
];

export function ThemeToggle() {
  const [theme, setTheme] = useState<Theme>(loadTheme());
  return (
    <div role="group" aria-label="Theme" className="flex rounded-[8px] border border-border">
      {OPTS.map((o) => (
        <button key={o.value} type="button" aria-label={o.label} aria-pressed={theme === o.value}
          onClick={() => { applyTheme(o.value); setTheme(o.value); }}
          className={`flex h-8 w-9 items-center justify-center ${theme === o.value ? "text-accent" : "text-fg-muted"}`}>
          {o.icon}
        </button>
      ))}
    </div>
  );
}
