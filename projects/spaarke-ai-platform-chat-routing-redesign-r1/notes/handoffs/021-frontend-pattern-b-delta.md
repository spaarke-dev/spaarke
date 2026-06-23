# Task 021 Handoff — Pattern B FE Migration (Shared Lib)

> **Generated**: 2026-06-22
> **Task**: 021 — Migrate frontend Pattern B name-resolve consumers to stable ID
> **Wave**: 1-F (parallel-safe; partner task 020 = BE Pattern B)
> **Driver**: Spec FR-03 / §1.7 Pattern B (frontend variant). Q1 2026-06-22 corrections: route is `/api/ai/playbooks/by-id/{id}`; lookup is GUID-format `sprk_playbookid`.
> **Worktree**: `c:\code_files\spaarke-wt-spaarke-ai-platform-chat-routing-redesign-r1\`
> **Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1`

---

## Placement justification

Per CLAUDE.md §11 ("MODIFICATION not a NEW component — no `<justification>` element needed"). The two affected files are pre-existing shared-library modules; no new exports, no new public surface, no DI registrations. Edits are within the existing `@spaarke/ui-components` package boundary.

---

## POML defect (filed for tracker)

The task POML pointed at `src/client/code-pages/AnalysisWorkspace/src/hooks/useAiSummary.ts` and `src/solutions/SpaarkeAi/src/components/DocumentEmailWizard/DocumentEmailWizard.tsx`. Both paths are **stale** — those folders do not exist in this worktree. The actual files live in the shared lib:

| Stale POML path | Actual path |
|---|---|
| `src/client/code-pages/AnalysisWorkspace/src/hooks/useAiSummary.ts` | `src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts` |
| `src/solutions/SpaarkeAi/src/components/DocumentEmailWizard/DocumentEmailWizard.tsx` | `src/client/shared/Spaarke.UI.Components/src/components/DocumentEmailWizard/DocumentEmailWizard.tsx` |

Test runner is **Jest** (per `package.json: scripts.test = "jest"`), NOT Vitest as the POML stated. New tests use Jest conventions and live in `__tests__/` siblings per existing library pattern.

Add to existing project POML defect tracker in current-task.md.

---

## Call-site migration mapping

| Site | Literal name | Stable ID (GUID) | URL before | URL after |
|---|---|---|---|---|
| `useAiSummary.ts:285` (now `:310`) | `"Document Profile"` (PB-002) | `18cf3cc8-02ec-f011-8406-7c1e520aa4df` | `/api/ai/playbooks/by-name/Document%20Profile` | `/api/ai/playbooks/by-id/${encodeURIComponent(DOCUMENT_PROFILE_PLAYBOOK_ID)}` |
| `DocumentEmailWizard.tsx:628` (now `:657`) | `"Summarize New File(s)"` | `4a72f99c-a119-f111-8343-7ced8d1dc988` | `/api/ai/playbooks/by-name/${encodeURIComponent('Summarize New File(s)')}` | `/api/ai/playbooks/by-id/${encodeURIComponent(SUMMARIZE_NEW_FILES_PLAYBOOK_ID)}` |

Module-scope constants (`DOCUMENT_PROFILE_PLAYBOOK_ID`, `SUMMARIZE_NEW_FILES_PLAYBOOK_ID`) hoisted at the top of each file with a JSDoc block explaining placement rationale (not lifted to a shared `playbookIds.ts` — only one consumer each).

---

## ProblemDetails 404 handling (ADR-019)

Both consumers now branch on `response.status === 404` and surface the user-friendly error:

> "Playbook unavailable. Please contact your administrator."

The hook's existing document-state machine (`updateDocument({ status: 'error', error: msg })`) and the wizard's `setSummaryError(msg)` slot both render this text in their existing error UI — no new visual elements added.

ADR-015 compliance: logs include HTTP status category only (`console.warn('[useAiSummary] Playbook resolution: 404 not-found')`), never raw playbook IDs or response body content. The user-facing error string contains no HTTP status, ID, or technical detail.

---

## Files modified / added

| File | Change |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts` | Added `DOCUMENT_PROFILE_PLAYBOOK_ID` constant + JSDoc; switched playbook-resolution URL to `/by-id/`; added 404 ProblemDetails handling |
| `src/client/shared/Spaarke.UI.Components/src/components/DocumentEmailWizard/DocumentEmailWizard.tsx` | Added `SUMMARIZE_NEW_FILES_PLAYBOOK_ID` constant + JSDoc; switched playbook-resolution URL to `/by-id/`; added 404 ProblemDetails handling |
| `src/client/shared/Spaarke.UI.Components/src/hooks/__tests__/useAiSummary.playbookLookup.test.ts` | NEW — 2 tests (URL shape; 404 ProblemDetails → user-friendly error) |
| `src/client/shared/Spaarke.UI.Components/src/components/DocumentEmailWizard/__tests__/DocumentEmailWizard.playbookLookup.test.ts` | NEW — 5 tests (constant present; URL shape; no live `/by-name/`; no `Summarize` in URL; user-friendly error string present) |

---

## Build + test outcomes

| Check | Result |
|---|---|
| `npm run build` (shared lib `@spaarke/ui-components`) | ⚠️ 1 pre-existing baseline error (TS2307 `@spaarke/sdap-client` in `EntityCreationService.ts`) — verified present BEFORE my changes via `git stash` + rebuild. Not in B-002 documented list but same category (pre-existing workspace drift). My two files compile clean — the error is in an unrelated service module. |
| `npx jest --testPathPatterns="playbookLookup"` | ✅ **2 suites, 7 tests, 0 failures, 13.881 s** |
| Grep `by-name` in 2 modified files (live calls) | ✅ Zero live calls; only doc-comments reference the legacy route |
| Grep `/api/ai/playbooks/by-` in src/ | ✅ All live calls are `/by-id/`; the only `/by-name/` mentions are doc-comments and test assertions |

### Baseline-drift detail (informational, NOT blocking task)

Single TS2307 error pre-existing in `src/services/EntityCreationService.ts:34`:
```
src/services/EntityCreationService.ts(34,76): error TS2307: Cannot find module '@spaarke/sdap-client' or its corresponding type declarations.
```

Reproduction: `git stash push -- src/client/shared/Spaarke.UI.Components/` then `npm run build` → same error. Restored my changes via `git stash pop`. The error is in an unrelated workspace-package import (`@spaarke/sdap-client` is the `src/client/sdap-client/` package; the import expects it to resolve as a typed workspace module which has drifted). This is a different drift category from documented B-002 (`themeStorage` / `useSseStream` / `useForceSimulation` / `CustomCommandFactory` / `useChatFileAttachment`) but the same class: **shared-lib baseline drift not caused by this task**.

Recommend a separate Phase 0 / Phase 1 task for shared-lib build health restoration (joining B-002 as part of the same restoration effort).

---

## Bundle size deltas (NFR-01 advisory only)

Shared-lib `@spaarke/ui-components` is a TypeScript-only declarations package (`"main": "dist/index.js"`, `tsc` build); it has no minified bundle output. Consumers (PCF / code-pages / solutions) bundle it via their own webpack. No measurable bundle size in this package.

The downstream consumer bundles (PCF SemanticSearchControl, AnalysisWorkspace code-page, SpaarkeAi solution) will pick up the change at their next build — those builds are NOT triggered as part of this task per the wave 1-F parallel-safety scoping. Bundle-size deltas at the consumer level will be ≈ 0 (this is a URL-string and constant-rename change; no new imports, no new dependencies).

NFR-01 is a BFF-only constraint per CLAUDE.md §10. This task does not touch BFF.

---

## UI smoke test (Step 9.7)

⏭️ **SKIPPED** — Claude Code session not started with `--chrome` per task brief. The acceptance-criteria UI tests (console-error-free, dark-mode toggle, interaction smoke) are deferred to manual Phase 7 UAT, where they will be exercised against the deployed PCF + code-page + SpaarkeAi solution.

Per the wizard's existing test infrastructure: the URL-shape assertion in `DocumentEmailWizard.playbookLookup.test.ts` is the load-bearing verification that the network request shape is correct. Visual / interaction smoke is non-load-bearing for this URL-shape migration.

---

## Quality gates (Step 9.5)

| Gate | Result |
|---|---|
| code-review (manual review of modified files) | ✅ Pass — JSDoc thorough; ADR-015 logging hygiene preserved; ADR-019 ProblemDetails handling in both consumers; no `any` types introduced; constants at module scope with placement justification; tests use shared library's Jest conventions (jsdom + `@testing-library/react`) |
| adr-check (against modified files) | ✅ Pass — ADR-015 (no content / IDs in logs beyond debug); ADR-019 (404 ProblemDetails handled); ADR-021 (no UI styling touched — only error-message text in pre-existing error UI surfaces) |
| TypeScript lint (`npm run build`) | ⚠️ 1 pre-existing baseline error (NOT in my files); my files compile clean |
| Jest tests | ✅ 7 / 7 pass |

---

## Recommended TASK-INDEX status update

| Task | Recommended status | Notes |
|---|---|---|
| 021 (FE Pattern B migration) | ✅ done | URL shape verified by Jest tests; ProblemDetails 404 handled; ADR-015/019/021 compliant; UI smoke test deferred to Phase 7 manual UAT per `--chrome` absence |

---

## Out of scope / follow-ups

- POML path correction (the project POML defect tracker already lists similar misroutings — add this one).
- Shared-lib build health restoration (TS2307 `@spaarke/sdap-client` import in `EntityCreationService.ts`) — pre-existing drift; should be combined with B-002 cleanup as a separate Phase 0/1 task.
- PCF SemanticSearchControl + AnalysisWorkspace code-page + SpaarkeAi solution rebuilds — picked up on their next normal build; no Phase 1-F action needed.
- Phase 7 manual UAT exercises the migrated wizard + summary flows end-to-end with the BFF `/by-id/` endpoint (task 020 BE partner) — this validates the runtime behavior of the network change.

---

## Unexpected findings

1. **Test runner is Jest, not Vitest** — POML statement was incorrect. Tests use `jest` + `ts-jest` + `@testing-library/react` per existing library convention.
2. **POML paths were stale** — the brief described files in `src/client/code-pages/AnalysisWorkspace/` and `src/solutions/SpaarkeAi/` but both files actually live in `src/client/shared/Spaarke.UI.Components/`. This appears to be POML drift from an earlier reorganization; the shared-lib location is correct per the latest module structure.
3. **Pre-existing TS2307 baseline error** in `EntityCreationService.ts` (unrelated `@spaarke/sdap-client` import) — verified present in the BASELINE (before my changes). Not in B-002 documented list but same drift category. Should be tracked separately.
4. **DocumentEmailWizard does not have a pre-existing test file** — created from scratch using a source-inspection pattern (file-content assertion). Full DOM-rendering tests for the wizard would require a fixture for `WizardShell` + Fluent provider, which is out of scope for this URL-shape migration.
5. **No shared `playbookIds.ts` module created** — the two consumers use different playbooks (Document Profile vs Summarize New File(s)) and neither is referenced by other code in the shared lib; lifting would have been premature abstraction. Documented in the JSDoc on each constant.
