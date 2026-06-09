import type { MigrationMode } from "../api/types";

export interface WizardStepDef { key: string; label: string; path: string; }

export const STEPS: WizardStepDef[] = [
  { key: "mode", label: "Mode", path: "mode" },
  { key: "from-to", label: "From & To", path: "from-to" },
  { key: "connect-from", label: "Connect From", path: "connect/from" },
  { key: "connect-to", label: "Connect To", path: "connect/to" },
  { key: "scope", label: "Scope", path: "scope" },
  { key: "review", label: "Review & plan", path: "review" },
  { key: "run", label: "Run", path: "run" },
];

/**
 * Mode-derived step set. Migrate uses the full set; reconcile skips "Review & plan" (a reconcile starts
 * immediately from Scope — there is no approval gate to review against the live destination).
 */
export const stepsFor = (mode: MigrationMode): WizardStepDef[] =>
  mode === "reconcile" ? STEPS.filter((s) => s.key !== "review") : STEPS;
