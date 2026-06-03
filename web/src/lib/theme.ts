export type Theme = "light" | "dark" | "system";
const KEY = "em-theme";

export function resolveTheme(theme: Theme): "light" | "dark" {
  if (theme === "system") {
    return typeof matchMedia !== "undefined" &&
      matchMedia("(prefers-color-scheme: dark)").matches
      ? "dark"
      : "light";
  }
  return theme;
}

export function applyTheme(theme: Theme): void {
  const resolved = resolveTheme(theme);
  const root = document.documentElement;
  root.classList.add("theme-anim-off");
  root.dataset.theme = resolved;
  localStorage.setItem(KEY, theme);
  if (typeof requestAnimationFrame !== "undefined") {
    requestAnimationFrame(() => root.classList.remove("theme-anim-off"));
  } else {
    root.classList.remove("theme-anim-off");
  }
}

export function loadTheme(): Theme {
  const v = localStorage.getItem(KEY);
  return v === "light" || v === "dark" || v === "system" ? v : "system";
}
