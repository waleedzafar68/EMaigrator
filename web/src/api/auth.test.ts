import { afterEach, describe, expect, it, vi } from "vitest";
import { setUnauthorizedHandler } from "./client";
import { login, logout, register } from "./auth";

describe("auth api", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    setUnauthorizedHandler(null);
  });

  it("login POSTs credentials to /auth/login and returns the result", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ accessToken: "t", expiresAt: "2099-01-01T00:00:00Z" }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    const out = await login({ email: "a@b.com", password: "secret-passw0rd" });

    expect(out.accessToken).toBe("t");
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/v1/auth/login");
    expect(init.method).toBe("POST");
    expect(init.credentials).toBe("include");
    expect(JSON.parse(init.body as string)).toEqual({
      email: "a@b.com",
      password: "secret-passw0rd",
    });
  });

  it("register POSTs to /auth/register with the org name", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: "u1", tenantId: "t1" }), {
        status: 201,
        headers: { "Content-Type": "application/json" },
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    await register({ email: "a@b.com", password: "passwordpassword", organizationName: "Acme" });

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/v1/auth/register");
    expect(JSON.parse(init.body as string)).toEqual({
      email: "a@b.com",
      password: "passwordpassword",
      organizationName: "Acme",
    });
  });

  it("logout POSTs to /auth/logout and resolves on 204", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(logout()).resolves.toBeUndefined();

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/v1/auth/logout");
    expect(init.method).toBe("POST");
    expect(init.credentials).toBe("include");
  });

  it("fires the unauthorized handler on a 401 and still rejects", async () => {
    const onUnauthorized = vi.fn();
    setUnauthorizedHandler(onUnauthorized);
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("{}", { status: 401 })));

    await expect(login({ email: "a@b.com", password: "x" })).rejects.toBeDefined();
    expect(onUnauthorized).toHaveBeenCalledOnce();
  });
});
