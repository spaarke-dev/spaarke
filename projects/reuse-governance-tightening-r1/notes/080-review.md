# Task 080 — adversarial review of items 1–4

> One `adversarial-reviewer` launch (fable, effort high), 2026-11-15, at HEAD `bf82fba914` (after merging origin/master).
> Range A = `8c05f5b70f` (items 1–3, merged as PR #2751 without this pass). Range B = `origin/master...HEAD` minus task 040's three files (`.mcp.json`, `.claude/agents/researcher.md`, `knowledge/README.md`).
> Inputs: `spec.md` and the POMLs of tasks 001, 002, 010, 011, 012, 020, 021, 030–035. No second fable pass was run.

## Triage (orchestrator)

| # | Finding | Class | Action |
|---|---|---|---|
| 1 | `ComposeWorkspace.memoEmailHandlers.test.tsx` (arrived from master, #2812) mocks `@spaarke/ui-components` without `useDocumentActions` → 2 tests red; `compose-client-gate` would fail the item-4 PR | F4 | **Fixed**: added the same stub the six sibling suites use. The only instance in the tree (reviewer ran the full Compose, SpaarkeAi and SemanticSearch suites). |
| 2 | Sweep's 7-day changed-file list (`git log --since=7.days --name-only`) skips merge-commit landings (542 of 2,465 files missing on master) | F1 | **Fixed**: `--first-parent -m --no-renames`; comments and `docs/procedures/ci-cd-workflow.md` row updated. Measured on origin/master: 1,946 → 2,493 files; the one file dropped is net-zero in the window. |
| 3 | Instructions still name retired packages: `master-deploy/SKILL.md:113`, `src/client/shared/CLAUDE.md:11`, `docs/procedures/testing-and-code-quality.md:645` | F2 | **Fixed** (12 libs, names removed). |
| 4 | CAPABILITY-INDEX row 3 names `normalizeUrl` (it is `normalizeBffBaseUrl`, `bffClientConfig.ts:89`); row 27 names `MetricCard` (it is `VisualMetricCard`) | F2 / FR-01 F4 | **Fixed** both cells and the header note; `.claude/patterns/auth/bff-url-normalization.md:29` fixed too (same root cause). Remaining `normalizeUrl` mentions outside this project's files → **ISS-007 #2842**. |
| 5 | `code-page-deploy/SKILL.md:284` said `deploy-spaarke-ai.yml` covers the trees SpaarkeAi bundles; six are missing from its trigger | F2 (text) + pre-existing gap | **Fixed** the text; the trigger gap is pre-existing → **ISS-006 #2841**, reported to the owner. |
| 6 | Alert treats "file touched this week" as new duplication, so it will be open most weeks and can close without anything removed | K (design limit) | Matches FR-06 as written; the only real fix is a baseline, which design.md §10.7 cut. **Owner decision** at the OQ-8 four-week tuning point. |
| 7 | A job-level timeout skips the decide/alert steps, so nothing is posted | K2 | **Hardened** anyway (one line): `timeout-minutes: 10` on the jscpd step, so the existing "no readable report → failing" branch fires. |
| 8 | `hooks/index.ts` now pulls `useDocumentActions` (and `@spaarke/auth`) into the pcf-safe module graph | K4 | Recorded. Verified harmless: MatterHeader `build:prod` 67,327 bytes, no auth/msal strings (tree-shaken). Optional hardening only. |
| — | `src/solutions/LegalWorkspace/package-lock.json:207` keeps Compose's old `@spaarke/document-operations` edge | ~~K4~~ → **F4** | First recorded as K4 ("no failing path"). That was wrong: the "Client lint (type-aware ratchet)" job regenerates every lock with `npm install --package-lock-only --legacy-peer-deps` and fails on any diff, so PR #2843 failed it. **Fixed** (2026-11-15, PR branch `77bd866dd8`): lock regenerated, a one-line deletion; no other lock changed. |

**Fix verification (2026-11-15, commit `518b5bec8d`)**: full Compose jest 112/112 suites, 1457 tests; `SharedPackageCensus` ArchTests 8/8; `Test-InstructionPaths.ps1` new 0; workflow YAML parses (jscpd step `timeout-minutes: 10`, `continue-on-error: true`). Scoped re-check (one `adversarial-reviewer` on **sonnet**, fix diff + direct callers only, not a second full pass): **clean** — the new `git log` lists 2,497 files vs a 2,496-file ground-truth diff on origin/master (none missed, one harmless extra), a timed-out jscpd step reaches the "no readable report → failing" branch, the test stub matches the siblings and the hook's shape, and every name, count and path in the edited docs is true. No F-class finding remains open.

Requirements trace: FR-01 and task 033 were "not met" only through findings 4 and 1, both now fixed; every other item-1–4 criterion is met or not verifiable from a diff (process items, the first weekly cron on 2026-11-16, the item-4 PR that does not exist yet). Full trace below.

## Reviewer report (verbatim)

### Findings (most severe first)

1. **F4** — `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.memoEmailHandlers.test.tsx:47`. The test replaces `@spaarke/ui-components` wholesale without `useDocumentActions`, which `ComposeWorkspace.tsx:1712` now imports from that module. Failure: `TypeError: (0 , ui_components_1.useDocumentActions) is not a function`, 2 tests, reproducible in isolation. The test arrived from master (#2812, 2026-11-13 22:34), after task 033's verification. `compose-client-gate` in `ci-standalone-checks.yml` runs the full suite, so the item-4 PR goes red, and nightly `client-tests` will report compose-components as `fail` after merge. Fix: add `useDocumentActions: () => ({ openInWeb: jest.fn(), openInDesktop: jest.fn(), isActing: false }),` to that mock (same stub as the six sibling files). Only instance in the tree. Any in-flight branch with a full ui-components mock that renders `ComposeWorkspace` will break the same way; say so in the PR body.
2. **F1** — `.github/workflows/nightly-health.yml:1400`. `git log --since=7.days --name-only` filters by each commit's own date and prints nothing for merge commits. A branch whose commits are older than 7 days and lands by merge commit is invisible, so its duplicates read as "old" and never alert; with no baseline they are never seen later either. Measured on `origin/master`: 542 of 2,465 files landed in the window missing; 151 of 679 in-scope source files missing; 49 of 95 first-parent commits are merges (example: `SubmitInvoiceDialog.tsx`, committed 2026-11-03, landed 2026-11-11 via #2689). Fix: `git log --since=7.days --first-parent -m --no-renames --name-only --pretty=format:`; update the comments at lines 1329 and 1347 and the `ci-cd-workflow.md` row. FR-06 names the plain command, so this is a defect in the spec's mechanism, not a deviation.
3. **F2** — instructions still point at retired packages: `.claude/skills/master-deploy/SKILL.md:113` ("14" libs; "Also build … DocumentOperations … and LegalWorkspace"; "`tsc` failures in AI.Widgets and LegalWorkspace"); `src/client/shared/CLAUDE.md:11` (DocumentOperations README); `docs/procedures/testing-and-code-quality.md:645` (DocumentOperations in client-lint "Covered").
4. **F2** — two CAPABILITY-INDEX symbol cells wrong (header says every symbol Grep-verified 2026-11-11, FR-01): row 3 `normalizeUrl` does not exist in `src/client/shared` (it is `normalizeBffBaseUrl`, `Spaarke.Auth/src/bffClientConfig.ts:89`); row 27 `MetricCard` in `@spaarke/visuals` was renamed `VisualMetricCard` on 2026-10-03, and `MetricCard` is now a different component in `Spaarke.UI.Components/src/components/WorkspaceShell/MetricCard.tsx`. An agent citing row 27 imports a non-existent symbol or extends the wrong card. 126 paths and ~30 other symbols checked clean.
5. **F2** — `.claude/skills/code-page-deploy/SKILL.md:282-284` says the consumer's deploy trigger includes the source tree it bundles "(as `deploy-spaarke-ai.yml` does)". True only for LegalWorkspace: `vite.config.ts` also bundles Compose, Communication, Notifications, DocumentUploadWizard, FindSimilarCodePage and WorkspaceLayoutWizard, none in the workflow's `paths`. A Compose-only fix merged to master does not redeploy the Console. Reword in scope; the trigger gap is pre-existing — file and report (six `paths` lines).
6. **Design limit (needs §10.7 owner decision)** — `nightly-health.yml:1429-1461`. "New" means a file was touched, so the alert reports old duplication in any touched file (the branch dispatch listed 17 "new" clusters, all pre-existing). With ~2,000 files changing weekly the alert will be open most weeks; after one quiet week it closes "Recovered" although nothing was removed. Matches FR-06 as written; the only real fix is a baseline, which §10.7 cut. OQ-8's four-week tuning window is the place to judge it.
7. **K2** — `nightly-health.yml:1342`. A job-level timeout or failed checkout skips the decide and alert steps, so nothing is posted. Observed runtime 40 s vs a 20-minute cap. Fix: `timeout-minutes: 10` on the "Run jscpd" step.
8. **K4** — `src/client/shared/Spaarke.UI.Components/src/hooks/index.ts:22`. `pcf-safe.ts:47` and MatterHeader (`MatterHeaderView.tsx:67`) import the `./hooks` index, which now loads `useDocumentActions` and therefore `@spaarke/auth`; before the move the pcf-safe graph had no `@spaarke/auth` import. Verified harmless: MatterHeader `build:prod` succeeds at 67,327 bytes with no `open-links`, `msal` or `authenticatedFetch` strings; the hook uses only `useState`/`useCallback`. CommunicationConversationPanel (pcf-safe) not built; relies on the same tree-shaking. Optional hardening: export the hook from `src/index.ts` directly.

Found in passing, not caused by this change: `.claude/patterns/auth/bff-url-normalization.md:29` cites `normalizeUrl()` at `resolveRuntimeConfig.ts:388` (same root cause as 4); `src/solutions/LegalWorkspace/package-lock.json:207` still records Compose's old `@spaarke/document-operations` edge — no failing path (Vite build passes; task-033 note records a clean `npm install`).

Already-filed items: DEF-001..006 and ISS-001..005 are accurately scoped; spot-checked line references resolve. Row 30's "item 4 decides" wording is covered by DEF-005.

Capability duplication: no new surface duplicates an indexed capability. Row 29 was updated in the same change. The moved `AiPaneEvent`/`IChatSession` types duplicate SprkChat's types — pre-existing, no index row, filed as P-6 under DEF-006. `.jscpd.json` and the sweep reuse the existing `quality-alert` action.

### Requirements trace

| Criterion | Result |
|---|---|
| FR-01 / task 001 | Not met (F4) on "every cell verified": finding 4. Otherwise met: 32 rows, all 126 paths resolve, `Test-InstructionPaths.ps1` new 0, seeded-path proof in `notes/001-index-proof.md:17-20`, row 30 names two components, no new test or baseline. |
| FR-02 / task 002 | Met. Index cited in `task-create/SKILL.md:252,267`, `code-review/SKILL.md:377`, `patterns/INDEX.md`, root `CLAUDE.md` §11 (one line changed in place), `doc-drift-audit/SKILL.md:83,93`. No heading renumbered; `Validate-TaskPoml.ps1` untouched. |
| FR-03 / task 010 | Met. `notes/reuse-scan-dry-run.md` has all fields, token cost and a "go" decision. Owner sight before 011 not verifiable from the diff. |
| FR-04 / task 011 | Met. `design-to-spec/SKILL.md:161-183,203,214`: one `code-mapper` call naming model and effort, file contract, both lines rewritten, numbering unchanged, `code-mapper.md` unchanged. |
| FR-05 / task 012 | Met. `project-pipeline/SKILL.md:147-150,169`: single "missing file → full scan" path, stop in autonomous mode, ask in interactive mode, no delta re-scan. |
| FR-06 / task 020 | Met as written, with finding 2 against its mechanism. Dispatch run 43293619731 succeeded, uploaded the artifact and decided a status; 192 old clusters did not alert; jscpd `5.4.2` pinned; exclusions in `.jscpd.json`; sweep skipped on the 2026-11-15 daily cron. |
| FR-06 "opens or closes exactly one issue" | Not verifiable yet: first weekly cron on master is 2026-11-16; branch runs are dry-run. Action logic correct by reading. |
| FR-07 / task 021 | Met. Both checks present with the table's class names; `task-execute` Step 9.5 rule 3 passes the paths; verification run recorded in the 021 POML completion notes. |
| FR-08 / task 030 | Met. Nine sections with evidence, counts, findings and recommendation; owner decision per section; defect list at the top. `code-mapper` use not verifiable from the diff. |
| FR-09 / tasks 031, 032 | Met on the branch. Folders removed; zero hits in `src/`; census 8/8; SpaarkeAi builds; 137 suites pass. |
| FR-09 / task 033 | Not met (F4) on "existing tests pass": finding 1. Otherwise met: hook body unchanged, 17 tests moved, zero `@spaarke/document-operations` in `src/`, no `useTransition`/`useDeferredValue`. |
| FR-09 / task 034 | Met. Issues #2801–#2808 each pair with a `defer-issues.md` entry. |
| Task 035 | Met per `notes/035-smoke.md`. Deployed build `5e50dde611` predates the master merge. Delete and Open in Desktop not exercised (owner choice, recorded). |
| NFR-01 | Met: only `schedule` and `workflow_dispatch` triggers; finding 7 for the no-alert edge. |
| NFR-02, NFR-08 | Met. |
| NFR-03, NFR-04, NFR-06 | Not verifiable from the diff (process). |
| NFR-05 | Not verifiable: item-4 PR not yet open. |
| NFR-10 | Met for filed items; findings 1–5 are new. |

### Verified clean

`useDocumentActions.ts` and test, `aiContext.ts`, the ai-widgets barrel, SpaarkeAi `package.json`/`tsconfig.json`/`vite.config.ts`/`jest.config.ts`/lock, LegalWorkspace `package.json` exports and `vite.config.ts`, SemanticSearch configs, Compose `package.json`/`tsconfig.json`, `SharedPackageCensusTests.cs`, both ADR-012 files, `Build-AllClientComponents.ps1`, `ci-standalone-checks.yml`, `client-tests.yml`, `eslint.client.config.mjs`, `.jscpd.json`, `quality-alert/action.yml`, `adversarial-reviewer.md`, and the task-create, code-review, design-to-spec, project-pipeline and doc-drift-audit wiring.

### What the reviewer ran (at HEAD `bf82fba914`)

| Check | Result |
|---|---|
| `dotnet test tests/Spaarke.ArchTests --filter SharedPackageCensus` | 8 passed |
| ui-components `tsc --noEmit` | 0 errors |
| ui-components moved hook suite | 17/17 |
| ai-widgets `tsc --noEmit` | 0 errors (after rebuilding stale local ui-components `dist`) |
| ai-widgets provider + SSE suites | 49/49 |
| Compose `tsc --noEmit` | 0 errors |
| Compose full jest | 111/112 suites, 1455/1457 tests (finding 1) |
| SpaarkeAi `tsc-surface-gate` | 0 surface-owned errors; no retired-module TS2307 |
| SpaarkeAi full jest | 137 suites, 1226 tests |
| SpaarkeAi `vite build` | passes, 6.41 MB, one copy of the hook |
| SemanticSearch full jest | 21 suites, 531 tests |
| LegalWorkspace `vite build` | passes |
| MatterHeader PCF `build:prod` | passes |
| `Test-InstructionPaths.ps1` | new 0 |
| `npm view jscpd@5.4.2` | exists |

Not run by the reviewer: client-lint, SemanticSearch webpack build, PCF dev-mode build.
