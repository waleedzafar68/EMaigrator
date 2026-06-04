export interface WizardStepDef { key: string; label: string; path: string; }

export const STEPS: WizardStepDef[] = [
  { key: "from-to", label: "From & To", path: "from-to" },
  { key: "connect-from", label: "Connect From", path: "connect/from" },
  { key: "connect-to", label: "Connect To", path: "connect/to" },
  { key: "scope", label: "Scope", path: "scope" },
  { key: "review", label: "Review & plan", path: "review" },
  { key: "run", label: "Run", path: "run" },
];
