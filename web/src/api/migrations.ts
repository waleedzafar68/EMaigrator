import { apiFetch } from "./client";
import type {
  ApproveRequest,
  AuditEntryDto,
  ConnectionRequest,
  ConnectionSide,
  ConnectionTestResult,
  MigrationDto,
  MigrationMode,
  PreflightPlanDto,
  ResultsDto,
  ScopeRequest,
  SetEndpointsRequest,
} from "./types";

export const createMigration = () =>
  apiFetch<MigrationDto>("/migrations", { method: "POST", body: "{}" });

export const listMigrations = (q?: { status?: string; q?: string }) => {
  const params = new URLSearchParams();
  if (q?.status) params.set("status", q.status);
  if (q?.q) params.set("q", q.q);
  const qs = params.toString();
  return apiFetch<MigrationDto[]>(`/migrations${qs ? `?${qs}` : ""}`);
};

export const getMigration = (id: string) => apiFetch<MigrationDto>(`/migrations/${id}`);
export const deleteMigration = (id: string) =>
  apiFetch<void>(`/migrations/${id}`, { method: "DELETE" });

export const setEndpoints = (id: string, body: SetEndpointsRequest) =>
  apiFetch<MigrationDto>(`/migrations/${id}/endpoints`, { method: "PATCH", body: JSON.stringify(body) });

export const setMode = (id: string, mode: MigrationMode) =>
  apiFetch<MigrationDto>(`/migrations/${id}/mode`, { method: "PATCH", body: JSON.stringify({ mode }) });

export const putConnection = (id: string, side: ConnectionSide, body: ConnectionRequest) =>
  apiFetch<MigrationDto>(`/migrations/${id}/connection/${side}`, { method: "PUT", body: JSON.stringify(body) });

export const testConnection = (id: string, side: ConnectionSide) =>
  apiFetch<ConnectionTestResult>(`/migrations/${id}/connection/${side}/test`, { method: "POST" });

export const putScope = (id: string, body: ScopeRequest) =>
  apiFetch<MigrationDto>(`/migrations/${id}/scope`, { method: "PUT", body: JSON.stringify(body) });

export const startPreflight = (id: string) =>
  apiFetch<void>(`/migrations/${id}/preflight`, { method: "POST" });
export const getPreflight = (id: string) => apiFetch<PreflightPlanDto>(`/migrations/${id}/preflight`);

export const approve = (id: string, body: ApproveRequest) =>
  apiFetch<MigrationDto>(`/migrations/${id}/approve`, { method: "POST", body: JSON.stringify(body) });

export const pause = (id: string) => apiFetch<MigrationDto>(`/migrations/${id}/pause`, { method: "POST" });
export const resume = (id: string) => apiFetch<MigrationDto>(`/migrations/${id}/resume`, { method: "POST" });
export const cancel = (id: string) => apiFetch<MigrationDto>(`/migrations/${id}/cancel`, { method: "POST" });

export const getResults = (id: string) => apiFetch<ResultsDto>(`/migrations/${id}/results`);
export const getAudit = (id: string, q?: { q?: string; failuresOnly?: boolean }) => {
  const params = new URLSearchParams();
  if (q?.q) params.set("q", q.q);
  if (q?.failuresOnly) params.set("failuresOnly", "true");
  const qs = params.toString();
  return apiFetch<AuditEntryDto[]>(`/migrations/${id}/audit${qs ? `?${qs}` : ""}`);
};
export const rerun = (id: string) => apiFetch<MigrationDto>(`/migrations/${id}/rerun`, { method: "POST" });
export const reconcile = (id: string) =>
  apiFetch<MigrationDto>(`/migrations/${id}/reconcile`, { method: "POST" });
export const reportUrl = (id: string, format: "csv" | "pdf") =>
  `/api/v1/migrations/${id}/report?format=${format}`;
