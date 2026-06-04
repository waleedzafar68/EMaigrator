import type { AxeResults } from "axe-core";

// Augment vitest's Assertion interface with vitest-axe matchers.
// vitest-axe@0.1.0 targets the old Vi namespace; this declaration
// covers the @vitest/expect Assertion interface used by vitest v3.
declare module "@vitest/expect" {
  interface Assertion<T = unknown> {
    toHaveNoViolations(): T extends AxeResults ? void : never;
  }
  interface AsymmetricMatchersContaining {
    toHaveNoViolations(): void;
  }
}
