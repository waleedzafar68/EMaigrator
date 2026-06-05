import { http, HttpResponse } from "msw";

let draft = {
  id: "e2e-1", status: "Draft", wizardStep: 0, from: null as string | null, to: null as string | null,
  isBatch: false, scopeSummary: null as string | null, mailboxCount: 1, progress: null, createdAt: "2026-06-01T00:00:00Z",
};

const okTest = { ok: true, folderCount: 14, messageCount: 3201, errorCode: null, rawDetail: null };
// Matches the API PreflightPlanDto wire shape: only { issues, estimate } (no usage/scanning).
const cleanPlan = {
  issues: [],
  estimate: { mailboxCount: 1, folderCount: 14, messageCount: 3201, totalBytes: 262144000, estimatedDurationSeconds: 720 },
};

export const handlers = [
  http.post("/api/v1/migrations", () => { draft = { ...draft, status: "Draft", wizardStep: 0 }; return HttpResponse.json(draft); }),
  http.get("/api/v1/migrations", () => HttpResponse.json([])),
  http.get("/api/v1/migrations/:id", () => HttpResponse.json(draft)),
  http.patch("/api/v1/migrations/:id/endpoints", async ({ request }) => {
    const body = (await request.json()) as { from: string; to: string };
    draft = { ...draft, from: body.from, to: body.to, wizardStep: 1 };
    return HttpResponse.json(draft);
  }),
  http.put("/api/v1/migrations/:id/connection/:side", () => HttpResponse.json(draft)),
  http.post("/api/v1/migrations/:id/connection/:side/test", () => HttpResponse.json(okTest)),
  http.put("/api/v1/migrations/:id/scope", () => { draft = { ...draft, wizardStep: 4 }; return HttpResponse.json(draft); }),
  http.post("/api/v1/migrations/:id/preflight", () => new HttpResponse(null, { status: 202 })),
  http.get("/api/v1/migrations/:id/preflight", () => HttpResponse.json(cleanPlan)),
  http.post("/api/v1/migrations/:id/approve", () => { draft = { ...draft, status: "Running", wizardStep: 5 }; return HttpResponse.json(draft); }),
];
