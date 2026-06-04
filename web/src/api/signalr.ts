import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import type { MigrationProgressDto, NeedsDecisionDto } from "./types";

export type ConnectionState = "connected" | "reconnecting" | "disconnected";

export function createHub(): HubConnection {
  return new HubConnectionBuilder()
    .withUrl("/hubs/migrations") // auth via httpOnly cookie the browser sends automatically
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
}

type ProgressFn = (dto: MigrationProgressDto) => void;
type StatusFn = (migrationId: string, status: string) => void;
type NeedsFn = (migrationId: string, dto: NeedsDecisionDto) => void;
type StateFn = (state: ConnectionState) => void;

export class MigrationsHubClient {
  private readonly hub: HubConnection;
  private progressCbs: ProgressFn[] = [];
  private statusCbs: StatusFn[] = [];
  private needsCbs: NeedsFn[] = [];
  private stateCbs: StateFn[] = [];

  constructor(factory: () => HubConnection = createHub) {
    this.hub = factory();
    this.hub.on("Progress", (dto: MigrationProgressDto) =>
      this.progressCbs.forEach((f) => f(dto)),
    );
    this.hub.on("StatusChanged", (id: string, s: string) =>
      this.statusCbs.forEach((f) => f(id, s)),
    );
    this.hub.on("NeedsDecision", (id: string, dto: NeedsDecisionDto) =>
      this.needsCbs.forEach((f) => f(id, dto)),
    );
    this.hub.onreconnecting(() => this.emit("reconnecting"));
    this.hub.onreconnected(() => this.emit("connected"));
    this.hub.onclose(() => this.emit("disconnected"));
  }

  private emit(s: ConnectionState) {
    this.stateCbs.forEach((f) => f(s));
  }

  onProgress(cb: ProgressFn): () => void {
    this.progressCbs.push(cb);
    return () => {
      this.progressCbs = this.progressCbs.filter((f) => f !== cb);
    };
  }

  onStatusChanged(cb: StatusFn): () => void {
    this.statusCbs.push(cb);
    return () => {
      this.statusCbs = this.statusCbs.filter((f) => f !== cb);
    };
  }

  onNeedsDecision(cb: NeedsFn): () => void {
    this.needsCbs.push(cb);
    return () => {
      this.needsCbs = this.needsCbs.filter((f) => f !== cb);
    };
  }

  onStateChange(cb: StateFn): () => void {
    this.stateCbs.push(cb);
    return () => {
      this.stateCbs = this.stateCbs.filter((f) => f !== cb);
    };
  }

  async start(): Promise<void> {
    if (this.hub.state !== HubConnectionState.Disconnected) return;
    await this.hub.start();
    this.emit("connected");
  }

  async stop(): Promise<void> {
    await this.hub.stop();
    this.emit("disconnected");
  }

  subscribe(id: string): Promise<void> {
    return this.hub.invoke("Subscribe", id);
  }

  unsubscribe(id: string): Promise<void> {
    return this.hub.invoke("Unsubscribe", id);
  }
}
