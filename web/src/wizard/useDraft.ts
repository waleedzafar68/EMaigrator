import { useCallback, useEffect, useState } from "react";
import { getMigration, putScope, setEndpoints } from "../api/migrations";
import type { MigrationDto, ProviderId, ScopeRequest } from "../api/types";

export function useDraft(id: string) {
  const [migration, setMigration] = useState<MigrationDto | null>(null);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    let cancelled = false;
    void getMigration(id)
      .then((m) => { if (!cancelled) setMigration(m); })
      .catch((e) => { if (!cancelled) setError(e); });
    return () => { cancelled = true; };
  }, [id]);

  const saveEndpoints = useCallback(async (from: ProviderId, to: ProviderId) => {
    const next = await setEndpoints(id, { from, to });
    setMigration(next);
    return next;
  }, [id]);

  const saveScope = useCallback(async (scope: ScopeRequest) => {
    const next = await putScope(id, scope);
    setMigration(next);
    return next;
  }, [id]);

  return { migration, error, saveEndpoints, saveScope, setMigration };
}
