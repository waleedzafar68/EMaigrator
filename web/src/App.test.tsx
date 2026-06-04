import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import App from "./App";
import * as api from "./api/migrations";

describe("App", () => {
  afterEach(() => vi.restoreAllMocks());
  it("renders the app shell with the Migrations header", async () => {
    vi.spyOn(api, "listMigrations").mockResolvedValue([]);
    render(<App />);
    expect(await screen.findByRole("heading", { name: /migrations/i })).toBeInTheDocument();
  });
});
