import { apiFetch } from "./client";

export interface RegisterRequest {
  email: string;
  password: string;
  organizationName: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResult {
  accessToken: string;
  expiresAt: string;
}

/** POST /auth/register — creates the user + tenant. Does NOT sign in (no cookie set). */
export const register = (body: RegisterRequest) =>
  apiFetch<{ id: string; tenantId: string }>("/auth/register", {
    method: "POST",
    body: JSON.stringify(body),
  });

/** POST /auth/login — on success the API sets the HttpOnly auth cookie used by every later call. */
export const login = (body: LoginRequest) =>
  apiFetch<LoginResult>("/auth/login", { method: "POST", body: JSON.stringify(body) });

/** POST /auth/logout — clears the HttpOnly auth cookie (API answers 204). */
export const logout = () => apiFetch<void>("/auth/logout", { method: "POST" });
