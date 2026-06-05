import { apiFetch } from "./client";
import type { ProviderCapabilityDto } from "./types";

/** GET /providers — the API-authoritative capability matrix (canBeSource/Destination/Batch, supportedAuth). */
export const listProviders = () => apiFetch<ProviderCapabilityDto[]>("/providers");
