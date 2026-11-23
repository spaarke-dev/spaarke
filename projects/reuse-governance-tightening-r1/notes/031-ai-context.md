# Task 031 — retire `@spaarke/ai-context`

> Owner decision (2026-11-13, "Approve all"): move the types into `@spaarke/ai-widgets`, then retire the package. Plan: `notes/package-reviews.md` §8.

## What changed

| Area | Change |
|---|---|
| Types | `EntityType`, `EntityContext`, `StreamingState`, `AiPaneEvent`, `StreamingCallbacks`, `IChatSession` copied unchanged into `src/client/shared/Spaarke.AI.Widgets/src/types/aiContext.ts` and exported (type-only) from the ai-widgets barrel. No name clashes with existing ai-widgets exports (checked). |
| Consumers | ai-widgets: `AiSessionProvider.tsx` and two test files now import from `../types/aiContext` / `../../types/aiContext`. SpaarkeAi: `ConversationPane.tsx` and `ThreePaneShell.tsx` import from `@spaarke/ai-widgets` (merged into their existing ai-widgets type imports). |
| Wiring | Removed the `@spaarke/ai-context` dependency (SpaarkeAi `package.json`, lock file regenerated, then the leftover "extraneous" lock entry removed by hand — `npm install` accepts the result), the SpaarkeAi tsconfig / vite (5 lines) / jest aliases, and the ai-widgets jest + tsconfig aliases. |
| Package | `src/client/shared/Spaarke.AI.Context/` deleted (7 files, 183 LOC, no tests). |
| Census | ADR-012 concise census row removed, plus a "Census changes since 2026-11-10" line; full ADR item 3 and a new "Census changes" table; `SharedPackageCensusTests` entry removed (the grandfathered list shrinks, as the ratchet allows). |
| Found during the grep (outside the POML's listed outputs) | `ci-standalone-checks.yml` built the deleted package in `client-typecheck` (would have failed CI) — steps removed. `deploy-spaarke-ai.yml` path trigger, `scripts/Build-AllClientComponents.ps1` shared-lib list (and its "14 packages" header → 13), `master-deploy` skill build order, and stale comments in `client-tests.yml`, `tsc-surface-gate.mjs`, `tsconfig.base.json`, `useSseStream.ts`. |

## Acceptance criteria

| Criterion | Result |
|---|---|
| Owner-approved action fully applied | Package folder removed; types live in ai-widgets. |
| SpaarkeAi builds; existing tests pass | See "Verification". |
| `SharedPackageCensusTests` passes; row and entry match | 8/8 pass; full ArchTests 2543/2543. |
| Negative: no `@spaarke/ai-context` in `src/` | Grep returns 0 (comments reworded to "the retired ai-context shared package"). |
| Cover the moved contract | The types are moved verbatim; the existing `AiSessionProvider` and `sse-event-routing` tests exercise them through the new import path. No new tests: the move adds no behaviour. |

## Verification

- `dotnet test tests/Spaarke.ArchTests/ --filter SharedPackageCensus`: 8 passed. Full suite: 2543 passed.
- `scripts/Build-AllClientComponents.ps1 -Component SharedLibs` on a clean worktree (exercises the edited lib list): 13/13 shared libraries SUCCESS, AI.Widgets included (real `tsc` emit). The script has no SpaarkeAi entry (`-Component SpaarkeAi` matched nothing), so SpaarkeAi was built directly.
- SpaarkeAi `npm run build` (html-reset check, `tsc-surface-gate`, Vite, ribbon): exit 0. SpaarkeAi `jest`: 137 suites, 1226 tests passed.
- ai-widgets `tsc --noEmit`: exit 0, no errors. ai-widgets `jest`: 47 suites, 792 tests passed.

## Quality gates (Step 9.5)

- code-review (inline, diff scope): no findings. Checked: type-only re-export has no name clash in the ai-widgets barrel and no consumer `export *`s ai-widgets next to another package; import merges keep one import per module; lock file still installs; no behaviour change.
- adr-check: ADR-012 compliant (consolidation into a sanctioned package, census row + test entry in the same change); ADR-022 n/a (ai-widgets is React 19, not PCF-safe; type-only move); ADR-021 / ADR-028 n/a.
- Adversarial pass: not run per task. NFR-03 sets one `adversarial-reviewer` pass over items 1–4, which is task 080.

## Deviations

- The POML listed three outputs. The repo-wide grep found live references in CI, a deploy trigger, a build script and a skill; leaving them would have broken `client-typecheck` and `Build-AllClientComponents.ps1` on the next run, so they are fixed here (NFR-10).
- Not touched: SprkChat (out of scope per §8); historical mentions in `notes/`, `docs/archive/` and other projects' folders.

## For the owner

- **Merging this to master auto-deploys SpaarkeAi to DEV** (`deploy-spaarke-ai.yml` runs on any master push that touches `src/solutions/SpaarkeAi/**`). Task 035's "DEV deploy needs the owner's OK" therefore applies to the merge of this change.
- PR #935 (draft, `work/code-quality-and-assurance-r4`, last updated 2026-09-08) still edits ADR-012 and `SharedPackageCensusTests.cs` and lists all 15 packages as sanctioned. It contradicts the approved retire/consolidate decisions and is stale against master.
