import { useEffect, useRef } from "react";
import { Outlet, useNavigate, useParams } from "react-router-dom";
import { createMigration, deleteMigration } from "../api/migrations";
import type { MigrationDto } from "../api/types";
import { useDraft } from "./useDraft";
import { Stepper } from "./Stepper";

export function NewMigrationRedirect() {
  const navigate = useNavigate();
  const started = useRef(false);
  useEffect(() => {
    if (started.current) return;
    started.current = true;
    void createMigration().then((m) => navigate(`/migrations/${m.id}/from-to`, { replace: true }));
  }, [navigate]);
  return <div role="status" aria-label="Creating migration" className="h-24 animate-pulse rounded bg-surface-2" />;
}

export function canBatchFor(migration: MigrationDto): boolean {
  return migration.to === "graph" || migration.to === "gmail";
}

export function WizardShell() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const { migration } = useDraft(id);
  if (!migration) return <div role="status" aria-label="Loading" className="h-24 animate-pulse rounded bg-surface-2" />;
  return (
    <div className="mx-auto max-w-[760px]">
      <Stepper current={migration.wizardStep} maxReached={migration.wizardStep} migrationId={id} />
      <Outlet context={{ migration, canBatch: canBatchFor(migration) }} />
      <div className="mt-8 border-t border-border pt-4">
        <button type="button" className="text-sm text-fg-muted"
          onClick={() => { void deleteMigration(id).then(() => navigate("/")); }}>
          Reset / Start over
        </button>
      </div>
    </div>
  );
}
