import { http, HttpResponse } from "msw";

let draft = {
  id: "e2e-1", status: "Draft", wizardStep: 0, mode: "migrate" as "migrate" | "reconcile",
  from: null as string | null, to: null as string | null,
  isBatch: false, scopeSummary: null as string | null, mailboxCount: 1, progress: null, createdAt: "2026-06-01T00:00:00Z",
};

/** Current mock migration — read by the fake SignalR hub to emit mode-appropriate progress. */
export function getDraft() {
  return draft;
}

const okTest = { ok: true, folderCount: 14, messageCount: 3201, errorCode: null, rawDetail: null };
// An all-zero estimate, the shape the API serializes while the background scan is still in flight.
const emptyEstimate = { mailboxCount: 0, folderCount: 0, messageCount: 0, totalBytes: 0, estimatedDurationSeconds: 0 };
// Matches the API PreflightPlanDto wire shape: { issues, estimate, scanning } (usage is hosted-only).
const scanningPlan = { issues: [], estimate: emptyEstimate, scanning: true };
const cleanPlan = {
  issues: [],
  estimate: { mailboxCount: 1, folderCount: 14, messageCount: 3201, totalBytes: 262144000, estimatedDurationSeconds: 720 },
  scanning: false,
};

// The async preflight: the first GET after starting the scan reports scanning:true (the SPA shows the
// "Reviewing your mailboxes…" state and polls), and the stored plan lands on the next poll.
let preflightStarted = false;

export const handlers = [
  http.post("/api/v1/migrations", () => { draft = { ...draft, status: "Draft", wizardStep: 0, mode: "migrate" }; preflightStarted = false; return HttpResponse.json(draft); }),
  http.get("/api/v1/migrations", () => HttpResponse.json([])),
  http.get("/api/v1/migrations/:id", () => HttpResponse.json(draft)),
  http.patch("/api/v1/migrations/:id/mode", async ({ request }) => {
    const body = (await request.json()) as { mode: "migrate" | "reconcile" };
    draft = { ...draft, mode: body.mode, wizardStep: Math.max(draft.wizardStep, 2) };
    return HttpResponse.json(draft);
  }),
  http.patch("/api/v1/migrations/:id/endpoints", async ({ request }) => {
    const body = (await request.json()) as { from: string; to: string };
    draft = { ...draft, from: body.from, to: body.to, wizardStep: 1 };
    return HttpResponse.json(draft);
  }),
  http.put("/api/v1/migrations/:id/connection/:side", () => HttpResponse.json(draft)),
  http.post("/api/v1/migrations/:id/connection/:side/test", () => HttpResponse.json(okTest)),
  http.put("/api/v1/migrations/:id/scope", () => { draft = { ...draft, wizardStep: 4 }; return HttpResponse.json(draft); }),
  http.post("/api/v1/migrations/:id/preflight", () => { preflightStarted = false; return new HttpResponse(null, { status: 202 }); }),
  http.get("/api/v1/migrations/:id/preflight", () => {
    if (!preflightStarted) { preflightStarted = true; return HttpResponse.json(scanningPlan); }
    return HttpResponse.json(cleanPlan);
  }),
  http.post("/api/v1/migrations/:id/approve", () => { draft = { ...draft, status: "Running", wizardStep: 5 }; return HttpResponse.json(draft); }),
  http.post("/api/v1/migrations/:id/reconcile", () => { draft = { ...draft, status: "Running" }; return HttpResponse.json(draft); }),
];
