export async function enableMocksIfRequested(): Promise<void> {
  if (import.meta.env.VITE_USE_MOCKS !== "1") return;
  const { worker } = await import("./browser");
  await worker.start({ onUnhandledRequest: "bypass" });
  // Swap the SignalR hub for a fake that emits mode-appropriate progress (no real backend in mock runs).
  const [{ setDefaultHubFactory }, { createFakeHub }] = await Promise.all([
    import("../api/signalr"),
    import("./fakeHub"),
  ]);
  setDefaultHubFactory(createFakeHub);
}
