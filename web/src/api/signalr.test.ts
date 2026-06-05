import { describe, expect, it, vi } from "vitest";
import { MigrationsHubClient } from "./signalr";

function makeFakeHub() {
  const handlers = new Map<string, (...args: unknown[]) => void>();
  const lifecycle: Record<string, () => void> = {};
  return {
    handlers,
    lifecycle,
    state: "Disconnected",
    on: vi.fn((name: string, cb: (...a: unknown[]) => void) => handlers.set(name, cb)),
    invoke: vi.fn().mockResolvedValue(undefined),
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
    onreconnecting: vi.fn((cb: () => void) => (lifecycle.reconnecting = cb)),
    onreconnected: vi.fn((cb: () => void) => (lifecycle.reconnected = cb)),
    onclose: vi.fn((cb: () => void) => (lifecycle.close = cb)),
    fire(name: string, ...args: unknown[]) { handlers.get(name)?.(...args); },
  };
}

describe("MigrationsHubClient", () => {
  it("registers the three contract event handlers by exact name", async () => {
    const hub = makeFakeHub();
    const client = new MigrationsHubClient(() => hub as never);
    await client.start();
    expect(hub.on).toHaveBeenCalledWith("Progress", expect.any(Function));
    expect(hub.on).toHaveBeenCalledWith("StatusChanged", expect.any(Function));
    expect(hub.on).toHaveBeenCalledWith("NeedsDecision", expect.any(Function));
  });

  it("invokes Subscribe/Unsubscribe with the migration id", async () => {
    const hub = makeFakeHub();
    const client = new MigrationsHubClient(() => hub as never);
    await client.start();
    await client.subscribe("m1");
    await client.unsubscribe("m1");
    expect(hub.invoke).toHaveBeenCalledWith("Subscribe", "m1");
    expect(hub.invoke).toHaveBeenCalledWith("Unsubscribe", "m1");
  });

  it("reflects reconnecting state transitions", async () => {
    const hub = makeFakeHub();
    const client = new MigrationsHubClient(() => hub as never);
    const states: string[] = [];
    client.onStateChange((s) => states.push(s));
    await client.start();
    hub.lifecycle.reconnecting!();
    hub.lifecycle.reconnected!();
    hub.lifecycle.close!();
    expect(states).toContain("connected");
    expect(states).toContain("reconnecting");
    expect(states).toContain("disconnected");
  });

  it("forwards Progress events to listeners", async () => {
    const hub = makeFakeHub();
    const client = new MigrationsHubClient(() => hub as never);
    const seen: unknown[] = [];
    client.onProgress((dto) => seen.push(dto));
    await client.start();
    hub.fire("Progress", { migrationId: "m1", migrated: 5, total: 10, currentFolder: null, msgPerMin: 0, status: "Running" });
    expect(seen).toEqual([{ migrationId: "m1", migrated: 5, total: 10, currentFolder: null, msgPerMin: 0, status: "Running" }]);
  });

  it("does not read auth tokens from storage", async () => {
    const getItem = vi.spyOn(Storage.prototype, "getItem");
    const hub = makeFakeHub();
    const client = new MigrationsHubClient(() => hub as never);
    await client.start();
    expect(getItem).not.toHaveBeenCalledWith(expect.stringMatching(/token|auth|jwt/i));
  });

  it("forwards StatusChanged events to listeners", async () => {
    const hub = makeFakeHub();
    const client = new MigrationsHubClient(() => hub as never);
    const seen: [string, string][] = [];
    client.onStatusChanged((id, s) => seen.push([id, s]));
    await client.start();
    hub.fire("StatusChanged", "m1", "Completed");
    expect(seen).toEqual([["m1", "Completed"]]);
  });

  it("forwards NeedsDecision events to listeners", async () => {
    const hub = makeFakeHub();
    const client = new MigrationsHubClient(() => hub as never);
    const seen: [string, unknown][] = [];
    client.onNeedsDecision((id, dto) => seen.push([id, dto]));
    await client.start();
    const dto = { issueType: "X", detail: "d", options: [] };
    hub.fire("NeedsDecision", "m1", dto);
    expect(seen).toEqual([["m1", dto]]);
  });

  it("unsubscribe remover removes only the targeted listener", async () => {
    const hub = makeFakeHub();
    const client = new MigrationsHubClient(() => hub as never);
    const calls1: unknown[] = [];
    const calls2: unknown[] = [];
    const remove1 = client.onProgress((dto) => calls1.push(dto));
    client.onProgress((dto) => calls2.push(dto));
    await client.start();
    remove1();
    hub.fire("Progress", { migrationId: "m1", migrated: 3, total: 10, currentFolder: null, msgPerMin: 0, status: "Running" });
    expect(calls1).toHaveLength(0);
    expect(calls2).toHaveLength(1);
  });

  it("start() guard skips hub.start when already Connected", async () => {
    const hubConnected = makeFakeHub();
    hubConnected.state = "Connected";
    const clientConnected = new MigrationsHubClient(() => hubConnected as never);
    await clientConnected.start();
    expect(hubConnected.start).not.toHaveBeenCalled();

    const hubDisconnected = makeFakeHub();
    const clientDisconnected = new MigrationsHubClient(() => hubDisconnected as never);
    await clientDisconnected.start();
    expect(hubDisconnected.start).toHaveBeenCalledOnce();
  });
});
