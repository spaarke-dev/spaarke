# Surface checklists — PCF, BFF API, deployment

Read this at Step 4c when the task's tags include `pcf`, `react` or `fluent-ui`; `bff-api`, `api` or `minimal-api`; or `deploy`. Apply the "Read" items during context loading (Steps 4–7) and tick the remaining items before you leave Step 9. More than one section can apply; apply each one that matches.

## PCF (`pcf`, `react`, `fluent-ui`)

- [ ] Read `src/client/pcf/CLAUDE.md`
- [ ] Read `docs/guides/PCF-DEPLOYMENT-GUIDE.md`
- [ ] Version bumped in every location the `pcf-deploy` skill's version table lists (ControlManifest.Input.xml, UI footer, Solution.xml, Solution ControlManifest.xml, pack.ps1)
- [ ] Build succeeds via `scripts/Invoke-PcfBuildProd.ps1` (`npm run build:prod` — NOT `npm run build`, per root CLAUDE.md §12 / FAILURE-MODES AP-1 — and `pcf-scripts` exits 0 even when the build fails)
- [ ] If deploying: solution import via the `pcf-deploy` skill (NEVER `pac pcf push`: it rebuilds in development mode)
- [ ] current-task.md updated with files modified

## BFF API (`bff-api`, `api`, `minimal-api`)

- [ ] Read `src/server/api/Sprk.Bff.Api/CLAUDE.md`
- [ ] Load `.claude/constraints/bff-extensions.md` and state the **Placement Justification** (root CLAUDE.md §10) in the PR/notes
- [ ] Follow ADR-001 Minimal API patterns
- [ ] Follow ADR-008 endpoint filter patterns
- [ ] Build succeeds: `dotnet build`
- [ ] Tests pass: `dotnet test` (Spaarke.sln) **and** `dotnet test tests/Spaarke.ArchTests/` (not in the solution)
- [ ] **Publish-size delta reported (root CLAUDE.md §10)**: per `.claude/rules/bff-hygiene.md` item 4 — against a fresh master build, never a recorded baseline; thresholds and procedure there
- [ ] No new HIGH CVE: `dotnet list package --vulnerable --include-transitive` (no upstream fix → `bff-hygiene.md` item 5 escalation)
- [ ] current-task.md updated with files modified

## Deployment (`deploy`)

- [ ] Read `.claude/skills/dataverse-deploy/SKILL.md`
- [ ] Read `docs/guides/PCF-DEPLOYMENT-GUIDE.md`
- [ ] Fresh build (delete bin/obj first)
- [ ] Version numbers correct
- [ ] PAC CLI authenticated
- [ ] Deployment verified
- [ ] current-task.md updated with deployment status
- [ ] Checkpoint after the deployment (Step 8.5, verbose)
