import {
  CircleCheckIcon,
  InfoIcon,
  Loader2Icon,
  OctagonXIcon,
  TriangleAlertIcon,
} from "lucide-react";
import { useEffect, useState } from "react";
import { Toaster as Sonner, type ToasterProps } from "sonner";
import { loadTheme, resolveTheme } from "@/lib/theme";

/** Reads the persisted theme and resolves "system" via matchMedia. */
function useResolvedTheme(): "light" | "dark" | "system" {
  const [theme, setTheme] = useState<"light" | "dark" | "system">(() =>
    resolveTheme(loadTheme()),
  );

  useEffect(() => {
    const root = document.documentElement;
    const observer = new MutationObserver(() => {
      const t = root.dataset.theme as "light" | "dark" | undefined;
      setTheme(t ?? resolveTheme(loadTheme()));
    });
    observer.observe(root, { attributes: true, attributeFilter: ["data-theme"] });
    return () => observer.disconnect();
  }, []);

  return theme;
}

const Toaster = ({ ...props }: ToasterProps) => {
  const theme = useResolvedTheme();

  return (
    <Sonner
      theme={theme as ToasterProps["theme"]}
      className="toaster group"
      icons={{
        success: <CircleCheckIcon className="size-4" />,
        info: <InfoIcon className="size-4" />,
        warning: <TriangleAlertIcon className="size-4" />,
        error: <OctagonXIcon className="size-4" />,
        loading: <Loader2Icon className="size-4 animate-spin" />,
      }}
      style={
        {
          "--normal-bg": "var(--surface-raised)",
          "--normal-text": "var(--fg)",
          "--normal-border": "var(--border)",
          "--border-radius": "var(--radius)",
        } as React.CSSProperties
      }
      {...props}
    />
  );
};

export { Toaster };
