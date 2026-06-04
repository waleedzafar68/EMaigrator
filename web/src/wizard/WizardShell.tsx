import { useEffect, useRef, useState } from "react";
import { Outlet, useLocation, useNavigate, useParams } from "react-router-dom";
import { createMigration, deleteMigration, getMigration } from "../api/migrations";
import type { MigrationDto } from "../api/types";
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
  const location = useLocation();
  const navigate = useNavigate();
  const [migration, setMigration] = useState<MigrationDto | null>(null);
  useEffect(() => {
    let active = true;
    void getMigration(id).then((m) => { if (active) setMigration(m); });
    return () => { active = false; };
  }, [id, location.pathname]); // re-fetch on each step so children see fresh server state
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
