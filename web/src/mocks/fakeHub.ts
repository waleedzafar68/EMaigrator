import { HubConnectionState, type HubConnection } from "@microsoft/signalr";
import type { MigrationProgressDto } from "../api/types";
import { getDraft } from "./handlers";

/**
 * Minimal fake SignalR hub for VITE_USE_MOCKS runs (the e2e suite). On Subscribe it pushes one Progress
 * event shaped for the current mock migration's mode — a reconcile run gets the nested folder-based
 * counts, a migrate run gets the message %-block — so the Run view renders live data without a real
 * backend. Only the members MigrationsHubClient touches are implemented.
 */
export function createFakeHub(): HubConnection {
  const handlers = new Map<string, ((...args: unknown[]) => void)[]>();
  let state: HubConnectionState = HubConnectionState.Disconnected;

  const fire = (method: string, ...args: unknown[]) =>
    (handlers.get(method) ?? []).forEach((f) => f(...args));

  function emit(id: string) {
    const draft = getDraft();
    const dto: MigrationProgressDto =
      draft.mode === "reconcile"
        ? {
            migrationId: id, migrated: 318, total: 3158, currentFolder: "/Inbox", msgPerMin: 0, status: "Running",
            reconcile: { foldersDone: 3, folderTotal: 650, copied: 318, backfilled: 12, skipped: 2840 },
          }
        : {
            migrationId: id, migrated: 1500, total: 3201, currentFolder: "/Inbox", msgPerMin: 412, status: "Running",
          };
    fire("Progress", dto);
    fire("StatusChanged", id, "Running");
  }

  const hub = {
    get state() { return state; },
    on(method: string, cb: (...args: unknown[]) => void) {
      handlers.set(method, [...(handlers.get(method) ?? []), cb]);
    },
    off() {},
    onreconnecting() {},
    onreconnected() {},
    onclose() {},
    start() { state = HubConnectionState.Connected; return Promise.resolve(); },
    stop() { state = HubConnectionState.Disconnected; return Promise.resolve(); },
    invoke(method: string, id: string) {
      if (method === "Subscribe") {
        // Defer so the hook's callbacks are registered and React has mounted before the first push.
        setTimeout(() => emit(id), 50);
      }
      return Promise.resolve();
    },
  };

  return hub as unknown as HubConnection;
}
