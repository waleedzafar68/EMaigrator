<!-- Thanks for contributing! Keep hosted-only concerns (billing, multi-tenancy, branded OAuth, AI) out
     of this engine repo — see CONTRIBUTING.md. -->

## What & why

<!-- What does this change do, and why? Link any related issue: "Closes #123". -->

## Type of change

- [ ] Bug fix
- [ ] New feature
- [ ] Refactor / tech-debt
- [ ] Docs
- [ ] CI / build

## Checklist

- [ ] Conventional Commit messages (`fix:` / `feat:` / `docs:` …)
- [ ] Added/updated tests
- [ ] `dotnet build src/EMaigrator.sln -c Release` passes (0 warnings — warnings are errors)
- [ ] Unit tests pass; integration suites run locally with Docker if the data/worker/API paths changed
- [ ] `npm --prefix web run test -- --run` + `npm --prefix web run build` pass (for web changes)
- [ ] No message bodies/attachments persisted; no hosted-only concerns added
- [ ] Docs updated if behavior or setup changed

## Notes for reviewers

<!-- Anything specific you want eyes on, trade-offs, follow-ups. -->
