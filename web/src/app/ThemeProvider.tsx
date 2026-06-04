import { useEffect, type ReactNode } from "react";
import { applyTheme, loadTheme } from "../lib/theme";

export function ThemeProvider({ children }: { children: ReactNode }) {
  useEffect(() => {
    applyTheme(loadTheme());
    const density = localStorage.getItem("em-density") ?? "comfortable";
    document.documentElement.dataset.density = density;
  }, []);
  return <>{children}</>;
}
