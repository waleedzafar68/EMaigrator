import { useEffect, useState } from "react";
import { MigrationsHubClient, type ConnectionState } from "./signalr";
import type { MigrationProgressDto, NeedsDecisionDto } from "./types";

export interface MigrationStream {
  connectionState: ConnectionState;
  progress: MigrationProgressDto | null;
  status: string | null;
  needsDecision: NeedsDecisionDto[];
}

export function useMigrationStream(migrationId: string | null): MigrationStream {
  const [connectionState, setConnectionState] = useState<ConnectionState>("disconnected");
  const [progress, setProgress] = useState<MigrationProgressDto | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [needsDecision, setNeeds] = useState<NeedsDecisionDto[]>([]);

  useEffect(() => {
    if (!migrationId) return;
    const client = new MigrationsHubClient();
    const offs = [
      client.onStateChange(setConnectionState),
      client.onProgress(setProgress),
      client.onStatusChanged((_id, s) => setStatus(s)),
      client.onNeedsDecision((_id, dto) => setNeeds((prev) => [...prev, dto])),
    ];
    let cancelled = false;
    void (async () => {
      await client.start();
      if (!cancelled) await client.subscribe(migrationId);
    })();
    return () => {
      cancelled = true;
      offs.forEach((off) => off());
      void client.unsubscribe(migrationId).catch(() => {});
      void client.stop().catch(() => {});
    };
  }, [migrationId]);

  return { connectionState, progress, status, needsDecision };
}
