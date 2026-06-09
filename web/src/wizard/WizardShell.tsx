import { useEffect, useRef, useState } from "react";
import { Outlet, useLocation, useNavigate, useParams } from "react-router-dom";
import { RotateCcw } from "lucide-react";
import { createMigration, deleteMigration, getMigration } from "../api/migrations";
import { listProviders } from "../api/providers";
import type { MigrationDto, ProviderCapabilityDto } from "../api/types";
import { ErrorAlert } from "../components/ErrorAlert";
import { errorAlertProps } from "../components/states/fromApiError";
import { Stepper } from "./Stepper";
import { stepsFor } from "./steps";

export function NewMigrationRedirect() {
  const navigate = useNavigate();
  const started = useRef(false);
  const [error, setError] = useState<unknown>(null);
  useEffect(() => {
    if (started.current) return;
    started.current = true;
    void createMigration()
      .then((m) => navigate(`/migrations/${m.id}/mode`, { replace: true }))
      .catch((e: unknown) => setError(e)); // 401 redirects globally; show anything else
  }, [navigate]);
  if (error) return <ErrorAlert {...errorAlertProps(error)} />;
  return <div role="status" aria-label="Creating migration" className="h-24 animate-pulse rounded bg-surface-2" />;
}

/** Heuristic fallback used only when the API providers call is unavailable or `to` is unknown. */
export function canBatchFor(migration: MigrationDto): boolean {
  return migration.to === "graph" || migration.to === "gmail";
}

export function WizardShell() {
  const { id = "" } = useParams();
  const location = useLocation();
  const navigate = useNavigate();
  const [migration, setMigration] = useState<MigrationDto | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [providers, setProviders] = useState<ProviderCapabilityDto[] | null>(null);
  const [confirmReset, setConfirmReset] = useState(false);

  useEffect(() => {
    let active = true;
    void getMigration(id)
      .then((m) => { if (active) setMigration(m); })
      .catch((e: unknown) => { if (active) setError(e); }); // 401 redirects globally; show anything else
    return () => { active = false; };
  }, [id, location.pathname]); // re-fetch on each step so children see fresh server state

  useEffect(() => {
    let active = true;
    // Providers are advisory: on failure we silently fall back to the heuristic below.
    void listProviders()
      .then((p) => { if (active) setProviders(p); })
      .catch(() => { if (active) setProviders(null); });
    return () => { active = false; };
  }, []);

  if (error) return <ErrorAlert {...errorAlertProps(error)} />;
  if (!migration) return <div role="status" aria-label="Loading" className="h-24 animate-pulse rounded bg-surface-2" />;

  // API authority for canBatch; fall back to the heuristic if providers are unavailable or `to` is unknown.
  const apiCanBatch = providers?.find((p) => p.id === migration.to)?.canBatch;
  const canBatch = apiCanBatch ?? canBatchFor(migration);

  // Mode-derived step set + label flow through the Stepper and the Outlet context so each step renders
  // its mode-appropriate variant.
  const mode = migration.mode ?? "migrate";
  const steps = stepsFor(mode);

  // The server's wizardStep no longer maps onto step indexes (the prepended mode step shifts every
  // index, and reconcile drops Review) — highlight the step for the route the user is actually on,
  // and let wizardStep only bound how far ahead the stepper allows jumping.
  const routeIdx = steps.findIndex((s) => location.pathname.endsWith(`/${s.path}`));
  const current = routeIdx >= 0 ? routeIdx : 0;
  const maxReached = Math.max(current, Math.min(migration.wizardStep, steps.length - 1));

  return (
    <div className="mx-auto max-w-[760px]">
      <Stepper current={current} maxReached={maxReached} migrationId={id} steps={steps} mode={mode} />
      <Outlet context={{ migration, canBatch, mode }} />
      <div className="mt-10 border-t border-border pt-4">
        <button
          type="button"
          className={`inline-flex items-center gap-1.5 text-sm transition-colors ${confirmReset ? "text-error" : "text-fg-muted hover:text-fg"}`}
          onClick={() => {
            if (confirmReset) void deleteMigration(id).then(() => navigate("/"));
            else setConfirmReset(true);
          }}
        >
          <RotateCcw size={14} aria-hidden />
          {confirmReset ? "Click again to discard this migration" : "Reset / Start over"}
        </button>
      </div>
    </div>
  );
}
