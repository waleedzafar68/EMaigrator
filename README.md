# EMaigrator

**Non-destructive, idempotent, resumable email migration** between WorkMail, Microsoft 365, and Google
Workspace. EMaigrator copies a mailbox from one provider to another — **your source is never modified** —
and streams every message through memory so that **message bodies and attachments are never persisted**.

This repository is the free, self-hostable **open-core engine**, licensed under [Apache-2.0](LICENSE).

## Why it's safe by design

- **Read-only at the source.** Migration is a copy; nothing is deleted or rewritten on the origin mailbox.
- **No body persistence.** Messages stream source → destination through a metadata-only envelope
  (`CanonicalMessage`); only minimal metadata (subject, date, folder) is stored, briefly. This is enforced
  structurally *and* by a schema-introspection test gate.
- **Idempotent & resumable.** A per-message identity key + a ledger mean re-running a migration never
  duplicates messages and safely continues where it left off.
- **Reconcile mode.** For Microsoft Graph/Exchange destinations, EMaigrator can diff the source against the
  live destination and copy only what's missing (including backfilling missing attachments) — never duplicating.

v1 leads with **WorkMail → Microsoft 365** (IMAP source + Microsoft Graph destination); Gmail/Google
Workspace and IMAP connectors are included.

## Quickstart (Docker)

Prerequisites: **Docker Desktop** running.

```bash
# 1. Configure secrets (the compose file ships insecure dev defaults so it boots out of the box).
cp deploy/.env.example deploy/.env
#    Edit deploy/.env and set real values for POSTGRES_PASSWORD, RABBITMQ_PASSWORD,
#    SECRET_MASTER_KEY (openssl rand -base64 32), and JWT_SIGNING_KEY.

# 2. Bring up the whole stack (Postgres + RabbitMQ + Redis + API + workers + web UI).
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d

# 3. Open the app.
#    http://localhost:3000   (or http://localhost:${WEB_PORT})
```

The nginx `web` service serves the built SPA and reverse-proxies `/api` + `/hubs` to the API, so the app is
a single same-origin URL. To stop: `docker compose -f deploy/docker-compose.yml down`. (Without a
`deploy/.env`, plain `docker compose -f deploy/docker-compose.yml up -d` runs with the built-in defaults.)

> The compose file is **fully env-parameterized** — every published host port, image tag, secret, and the
> restart policy is an env var with a default, so no override files are needed. If a port clashes with
> another local stack, set the matching `*_PORT` in `deploy/.env` (e.g. `POSTGRES_PORT=5434`,
> `WEB_PORT=3002`) and re-run. Internal service-to-service traffic uses container DNS and is unaffected.

## Development

Prerequisites: **.NET 10 SDK**, **Node 24+**, and Docker Desktop (for the integration tests).

```bash
dotnet build src/EMaigrator.sln -c Release      # build the engine + API + CLI
dotnet test  src/EMaigrator.sln -c Release      # all .NET tests (Docker needed for integration suites)
npm --prefix web install                        # web deps
npm --prefix web run dev                        # SPA dev server
npm --prefix web run test -- --run              # web unit tests
```

There is also a CLI (`EMaigrator.Cli`) for running migrations without the web UI. See
[`docs/CONTRIBUTING.md`](docs/CONTRIBUTING.md) for build strictness, the DI composition seams, and
Windows-specific operational notes.

## Architecture

A migration is a job of mailbox pairs. The API enqueues work onto RabbitMQ (via MassTransit); workers stream
each message source → destination; per-tenant/-provider rate limits use Redis token buckets; state, ledger,
and audit live in PostgreSQL; the SPA shows live progress over a SignalR hub. Full detail in
[`DESIGN.md`](DESIGN.md) and [`ARCHITECTURE.md`](ARCHITECTURE.md).

| Doc | Contents |
|---|---|
| [`DESIGN.md`](DESIGN.md) | Architecture, data model, security, stack, scope, decisions |
| [`ARCHITECTURE.md`](ARCHITECTURE.md) | Execution & parallelism (queue, workers, token buckets) |
| [`UX-Guide.md`](UX-Guide.md) | Operator flows, screens, states, copy |
| [`FRONTEND-DESIGN.md`](FRONTEND-DESIGN.md) | Visual design system |
| [`docs/connectors/authoring-a-connector.md`](docs/connectors/authoring-a-connector.md) | How to add a new provider connector |
| [`docs/KNOWN-ISSUES.md`](docs/KNOWN-ISSUES.md) | Tracked non-blocking defects |

## Open-core

This repo is the free, self-hostable engine. Hosted-only concerns (billing/quotas, multi-tenant
orchestration, branded OAuth) are not part of it. Contributions to the engine are welcome — please keep
hosted-only features out of this repository.

## License

[Apache License 2.0](LICENSE). See [`NOTICE`](NOTICE) for third-party attribution, including QuestPDF's
revenue-gated Community License (used for the PDF migration report) — self-hosters and commercial deployers
are responsible for their own QuestPDF license compliance.
