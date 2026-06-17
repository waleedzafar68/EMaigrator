# Security Policy

EMaigrator handles mailbox credentials and message data, so we take security seriously. Thank you for
helping keep the project and its users safe.

## Supported versions

The project is pre-1.0. Security fixes are applied to the `main` branch (the current release line). Please
test against the latest `main` before reporting.

## Reporting a vulnerability

**Please do not open a public GitHub issue for security problems.** Report privately by either:

- **GitHub Private Vulnerability Reporting** — use the **"Report a vulnerability"** button under the repo's
  **Security** tab (Security → Advisories), or
- **Email** the maintainer at **waleedzafar.68@gmail.com** with subject `SECURITY: EMaigrator`.

Please include:
- a description of the issue and its impact,
- steps to reproduce (a proof-of-concept if possible),
- affected version/commit, and
- any suggested remediation.

## What to expect

- **Acknowledgement** within 5 business days.
- An assessment and, where accepted, a fix plan with a target timeline.
- Credit in the release notes / advisory if you'd like it (let us know your preference).

Please give us a reasonable window to remediate before any public disclosure (coordinated disclosure).

## Security properties worth knowing

These are core invariants — a violation of any is a security bug worth reporting:

- **Message bodies and attachments are never persisted.** Content streams source → destination through
  memory; only minimal metadata is stored, briefly.
- **Provider credentials are encrypted at rest** and decrypted only in memory by the workers.
- **Tenancy isolation** is enforced at the data layer; cross-tenant data access is a security bug.
