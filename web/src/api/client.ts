export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | null,
    message: string,
    readonly technicalDetail: string | null,
    readonly traceId: string | null,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

const BASE = "/api/v1";

let onUnauthorized: (() => void) | null = null;

/**
 * Register a callback fired whenever the API answers 401 (e.g. redirect to the login page).
 * The rejection is still thrown so individual callers can handle it too.
 */
export function setUnauthorizedHandler(handler: (() => void) | null): void {
  onUnauthorized = handler;
}

export async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  const hasBody = init.body !== undefined && init.body !== null;
  const res = await fetch(`${BASE}${path}`, {
    ...init,
    credentials: "include", // httpOnly auth cookie; no token in localStorage
    headers: {
      ...(hasBody ? { "Content-Type": "application/json" } : {}),
      ...(init.headers ?? {}),
    },
  });
  if (!res.ok) {
    const err = await toApiError(res);
    if (res.status === 401) onUnauthorized?.();
    throw err;
  }
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

async function toApiError(res: Response): Promise<ApiError> {
  let body: Record<string, unknown> = {};
  try {
    const t = await res.text();
    if (t) body = JSON.parse(t) as Record<string, unknown>;
  } catch {
    /* non-JSON error body */
  }
  const traceId =
    res.headers.get("X-Trace-Id") ??
    res.headers.get("traceparent") ??
    (typeof body.traceId === "string" ? body.traceId : null);
  const code =
    typeof body.errorCode === "string"
      ? body.errorCode
      : typeof body.code === "string"
        ? body.code
        : null;
  const message =
    typeof body.message === "string" ? body.message : `Request failed (${res.status})`;
  const technicalDetail =
    typeof body.rawDetail === "string"
      ? body.rawDetail
      : typeof body.detail === "string"
        ? body.detail
        : null;
  return new ApiError(res.status, code, message, technicalDetail, traceId);
}
