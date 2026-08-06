# Task 022 — subDomain envelope: cold-load + open-existing legs — execution notes

> Rigor: FULL · Model tier: sonnet @ high. Spec FR-09 explicit-path plumbing; hub A3 deferred legs.
> Deps: 001 (registry mirror). Cross-project seam with hub-r1 (Q1 RESOLVED — this slice is ours).

## Step 0 — Notify + double-build grep

Wrote `notes/NOTIFY-hub-r1-deep-threading-legs-2026-07-31.md` (courtesy notice for the orchestrating
session to relay to the hub owner — no live cross-worktree channel from this session).

`git log --oneline bd64a69d4..HEAD -- launch-resolver.ts main.tsx WorkspacePane.tsx ThreePaneShell.tsx`
→ **empty**. `grep subDomain` across `src/solutions/SpaarkeAi/src` confirmed the field exists only in
(a) the already-shipped `main.tsx` workspace-renderer seed carry (A3-core, `bd64a69d4`) and (b) task
021's classifier-gate consumer code (`ConversationPane.tsx`/`useAgreementReviewGate.ts`/
`agreementReviewRouting.ts`) — neither leg's plumbing (`SpaarkeAiLaunchParams`, the URL-parse block,
`AnalysisLaunchContextValue`, or `WorkspacePane`'s by-analysis `$expand`) had landed. **Escalation
trigger did not fire** — proceeded to build both legs.

**Empirical schema check** (Dataverse MCP `describe('tables/sprk_analysis')`): confirmed the lookup
attribute logical name is `sprk_agreementtype` (→ `sprk_agreementtype` table), matching this task's
binding naming directive. Also surfaced a **hub-side finding**: `CreateAnalysisWizardWidget.tsx:806`
(A1's create-bind `discoverNavProps` check) still tests `columnName === 'sprk_agreementtypeid'`, which
does not match the live schema — the hub's PART 3 correction (`_sprk_agreementtypeid_value` →
`_sprk_agreementtype_value`) does not appear to have landed in this worktree's copy of that file.
Flagged in the NOTIFY doc; **out of scope for 022** (A1 is hub-owned per Q1 — not touched here).
Similarly, `src/client/shared/Spaarke.UI.Components/src/types/sprkAnalysis.ts` (`ISprkAnalysisRecord` /
`SPRK_ANALYSIS_SELECT`) still carries the old `_sprk_agreementtypeid_value` field name too (task 001's
own file, ✅ already complete) — this file's own header comment already self-flags this as "not yet
merged as of this addition." Neither file was touched by this task (not in scope; `WorkspacePane.tsx`
builds its own inline query string, independent of that constant).

## Step 1 — Cold-load door (leg a)

Mirrored the existing `analysisId`/`worktype`/`regarding` thread exactly, one field at a time:

1. `launch-resolver.ts` — `subDomain?: string` added to `SpaarkeAiLaunchParams` (JSDoc documents the
   ONE envelope contract + priority rule); `buildLaunchUrl()` emits it unencoded/unstripped (a slug,
   not a GUID — ADR-044 note: brace-stripping intentionally NOT applied, unlike `analysisId`/
   `entityId`/`regarding`).
2. `main.tsx` — read `subDomainParam` from `searchParams`/`dataParams` alongside the existing
   `analysisId`/`worktype`/`regarding` block (~line 637+); forwarded to `<App subDomain={subDomainParam}>`.
3. `App.tsx` — `subDomain?: string` added to `AppProps` (Analysis entry-matrix section); forwarded to
   `<ThreePaneShell subDomain={props.subDomain}>`.
4. `ThreePaneShell.tsx` — `subDomain?: string` added to `ThreePaneShellProps` AND
   `AnalysisLaunchContextValue`; destructured in the component; the `analysisLaunch` `useMemo` now
   spreads `subDomain` additively onto BOTH the `'existing'` and `'new'` branches (conditional spread —
   never a fabricated default; the memo's dep array gained `subDomain`).

**Scope decision (noted, not a gap)**: `AnalysisLaunchContextValue.subDomain` is threaded generically to
both `mode='new'` and `mode='existing'`, but `WorkspacePane`'s `mode==='new'` effect only auto-installs
the `analysis-hub` grid tab (no compose seed) — the wizard's own `defaultSubDomain` hint mechanism
(`CreateAnalysisWizardWidget.tsx:193,475`) is fed via a SEPARATE `open_create_analysis_wizard` /
`createAnalysisModal` path, not the cold-load auto-install effect, and is hub-owned (A1). Completing
that wire was NOT required by any acceptance criterion (all four criteria concern the `'existing'`
door + the wizard-finish door + the negative case) and would require touching hub-owned mount code —
left as-is; the field is present on the context for future use but has no `'new'`-mode consumer in this
task's scope.

## Step 2 — Open-existing door (leg b)

`WorkspacePane.tsx`'s by-analysis reopen effect (mode `'existing'`, the block resolving
`GET /ai/chat/sessions/by-analysis/{id}` then `analysisWizardDataService.retrieveRecord('sprk_analysis', ...)`):

- Extended `$select` with `_sprk_agreementtype_value`; extended `$expand` with a second comma-separated
  clause `sprk_agreementtype($select=sprk_key)` alongside the existing `sprk_documentid(...)` — valid
  OData v4 multi-expand syntax, and the exact attribute name empirically confirmed against the live
  schema (Step 0).
- Derivation: `const agreementType = (rec["sprk_agreementtype"] ?? {}) as Record<string, unknown>;
  const derivedSubDomain = agreementType["sprk_key"] as string | undefined;` — `?? {}` handles both an
  omitted key and an explicit `null` (unset lookup), mirroring the existing `doc` destructure pattern
  in the same function.
- **Priority** (ONE envelope contract): `const subDomain = analysisLaunch.subDomain ?? derivedSubDomain;`
  — the cold-load door's explicit value (leg a) wins; the derivation fills only when explicit is
  absent; neither present → `subDomain` stays `undefined`, spread conditionally
  (`...(subDomain ? { subDomain } : {})`) into `widgetData.compose` so the key is genuinely ABSENT on
  the dispatched event, never a fabricated default.
- This is also the point where legs (a) and (b) **converge** — a cold-load deep link with BOTH
  `analysisId` and `subDomain` present reaches this exact code path (mode `'existing'`), so the explicit
  value set in `ThreePaneShell`'s context flows straight through to the SAME dispatch this leg modifies.

**Zero `main.tsx` change needed for the reader to see leg (b)'s value**: the dispatched
`workspace/widget_load` `compose` seed lands in `main.tsx`'s existing `SpaarkeAiWorkspaceRenderer`
(`launchData.compose.subDomain` — already read since `bd64a69d4`), which is the SAME code path task
021's classifier gate reads via `useComposeLaunch()?.subDomain`. Confirmed by code inspection: `bd64a69d4`
already added `subDomain: seed.subDomain` to all three of that renderer's `ComposeLaunchContextValue`
branches (draft/upload/stored-document) — leg (b) only needed to make `WorkspacePane` populate
`seed.subDomain` in the first place.

## Step 3 — Contract test evidence (all three doors → one reader)

**Door 1 (wizard-finish, shipped `bd64a69d4`) — regression-protected, not re-tested**: `git diff`
confirms zero changes to `CreateAnalysisWizardWidget.tsx` or `composeLaunchContext.ts`; the
`SpaarkeAiWorkspaceRenderer` seed-parsing block in `main.tsx` (lines ~255-354) is untouched (my only
`main.tsx` edit is the separate URL-param block for the cold-load door, ~30 lines away). Full green
regression suites (conversation 479/479, workspace 120/120, shell 9/9) prove no behavioral change.

**Door 2 (cold-load) + Door 3 (open-existing) — new `WorkspacePane.subdomain-envelope.test.tsx`**
(4 tests), mocking `useAnalysisLaunch()` directly (the established convention in this codebase — see
`WorkspacePane.analysis-entry.test.tsx`, which mocks the same hook rather than rendering
`ThreePaneShell`) and a real `retrieveRecord` jest mock (the sibling analysis-entry test's
`createXrmDataService` stub has no `retrieveRecord` at all, so its `'existing'` test never reaches the
Compose dispatch — this file supplies a working one):

| Test | analysisLaunch.subDomain | DB `sprk_agreementtype.sprk_key` | Dispatched `compose.subDomain` |
|---|---|---|---|
| open-existing derivation | absent | `'employment'` | `'employment'` |
| cold-load explicit override | `'nda'` | `'lease'` (deliberately different) | `'nda'` (explicit wins) |
| negative (both absent) | absent | absent (no `sprk_agreementtype` key) | **key absent** (`not.toHaveProperty`) |
| explicit-only | `'vendor'` | (unset lookup) | `'vendor'` |

Also asserts the query shape directly: `$select` contains `_sprk_agreementtype_value`, `$expand`
contains `sprk_agreementtype($select=sprk_key)`, and does **not** contain `sprk_agreementtypeid`
(binding naming rule).

**`launch-resolver.test.ts`** (4 new tests): `subDomain` omitted when absent (back-compat); emitted
alongside `analysisId`; emitted alongside `worktype`; no brace-stripping on a slug value; plus one
`openSpaarkeAi` UI-test-mirroring test (`subDomain=nda` reaches the URL data blob, matching the POML's
own "Deep-link door" `ui-tests` entry).

## Build / test results (exact)

- `npm run typecheck` (SpaarkeAi's `tsc-surface-gate.mjs`): **1 pre-existing error**, unrelated to this
  task — `src/components/conversation/__tests__/ConversationPane.agreement-review-gate.e2e.test.tsx:85`
  (`'init' is declared but its value is never read`). Confirmed via `git blame` this line has been
  unchanged since task 021's own commit `98bf344d1` (Wave 3) — it predates this task and is NOT in any
  file this task touched. It sits inside `src/components/conversation/**`, which this task's HARD
  BOUNDARIES explicitly forbid touching this wave (task 031's territory) — not fixed here; flagged for
  the orchestrating session / task 031. Zero errors in any of the 5 files this task modified or the 2
  test files it added — confirmed by the gate report naming exactly 1 surface-owned error, in a file
  outside this task's diff.
- `npx vite build` (bypassing the typecheck gate to isolate the bundler from the pre-existing issue
  above): **succeeds** — 3999 modules transformed, 0 errors. Confirms the 5 modified files are
  syntactically/semantically sound at the bundler level.
- `npx jest src/utils/__tests__/launch-resolver.test.ts src/components/workspace/__tests__/WorkspacePane.subdomain-envelope.test.tsx src/components/workspace/__tests__/WorkspacePane.analysis-entry.test.tsx` →
  **3 suites / 32 tests, all passing**.
- `npx jest src/components/workspace` (full workspace suite, touched module) → **18 suites / 120 tests,
  all passing**.
- `npx jest src/components/conversation` (full conversation suite — task 021's territory, unaffected by
  this task per boundary but checked for regression per the task's BUILD/TEST instruction) →
  **51 suites / 479 tests, all passing**.
- `npx jest src/components/shell` (ThreePaneShell's own suite) → **1 suite / 9 tests, all passing**.

**Full `npm run build` was NOT run to green** — it fails at the `tsc-surface-gate` step on the
pre-existing, out-of-scope error above. `vite build` (the actual bundling/production-build step) was
run standalone and succeeds; this is reported honestly rather than silently declared "build green."

## Acceptance criteria

| # | Criterion | Result | Evidence |
|---|---|---|---|
| 1 | Deep-link `subDomain=nda` opens the three-pane with the envelope populated (cold-load door) | **PASS** | `launch-resolver.test.ts` (URL emission) + `WorkspacePane.subdomain-envelope.test.tsx` "cold-load explicit override"/"explicit-only" (envelope delivery, mocking the exact shape the main.tsx→App→ThreePaneShell chain produces) + code trace for the untested middle plumbing layer (consistent with this codebase's existing convention — the sibling `analysisId`/`worktype`/`mode` fields aren't independently unit-tested at the ThreePaneShell layer either) |
| 2 | Existing Analysis with lookup=employment yields `subDomain='employment'` in the envelope (derivation door) | **PASS** | `WorkspacePane.subdomain-envelope.test.tsx` "open-existing derivation" |
| 3 | Wizard-finish door still works unchanged (regression on `bd64a69d4`) | **PASS** | zero diff to `CreateAnalysisWizardWidget.tsx`/`composeLaunchContext.ts`/the seed-parsing block in `main.tsx`; conversation (479/479) + workspace (120/120) + shell (9/9) all green |
| 4 | Negative: absent subDomain + no lookup → field absent, no fabricated default | **PASS** | `WorkspacePane.subdomain-envelope.test.tsx` "negative" (`not.toHaveProperty('subDomain')`) |

**UI-tests deferred** (per task assignment, per POML `<ui-tests>` + root note "UI-tests (live deep-link,
live reopen) deferred to 060/061 — note it"): the two POML `ui-tests` entries ("Deep-link door",
"Open-existing door") require a live Xrm/Dataverse environment and are explicitly deferred to tasks
060/061 (deploy + e2e). Covered here at the unit/contract level as tabulated above.

## Files modified/created

**Code**:
- `src/solutions/SpaarkeAi/src/utils/launch-resolver.ts` — `subDomain` on `SpaarkeAiLaunchParams` +
  `buildLaunchUrl` emission + file-header doc.
- `src/solutions/SpaarkeAi/src/main.tsx` — URL-param read (`subDomainParam`) + forward to `<App>`.
- `src/solutions/SpaarkeAi/src/App.tsx` — `subDomain` on `AppProps` + forward to `<ThreePaneShell>`.
- `src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx` — `subDomain` on
  `ThreePaneShellProps` + `AnalysisLaunchContextValue`; `analysisLaunch` memo spreads it additively.
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` — `$expand`/`$select` extension +
  explicit-over-derived priority + conditional spread into the compose `widget_load` payload.

**Tests**:
- `src/solutions/SpaarkeAi/src/utils/__tests__/launch-resolver.test.ts` — 4 new tests (extended).
- `src/solutions/SpaarkeAi/src/components/workspace/__tests__/WorkspacePane.subdomain-envelope.test.tsx` —
  new file, 4 tests (the three-door contract + negative).

**Docs**:
- `projects/ai-advanced-capabilities-agreements-r1/notes/NOTIFY-hub-r1-deep-threading-legs-2026-07-31.md` — new.
- `projects/ai-advanced-capabilities-agreements-r1/notes/022-execution-notes.md` — this file.

## Quality gates (Step 9.5, self-run — FULL rigor)

**Code review** (self-run against the 5 modified files + 2 test files):
- *Security*: no secrets/tokens; `subDomain` is a plain slug string, never used in a raw HTML sink, SQL,
  or shell context — no injection surface. No new auth surface.
- *Performance*: one additional `$expand` clause on an EXISTING single-record `retrieveRecord` call —
  no new round trip, no N+1, no loop.
- *AI code smells*: no new interface-with-single-implementation; no new try/catch-log-rethrow (the
  surrounding try/catch is pre-existing, untouched in shape); no null-check-on-non-nullable (all new
  optional chaining is on genuinely optional `string | undefined` fields); comments added explain WHY
  (naming footgun, priority ordering, cross-door convergence), not restating WHAT; no new
  multi-responsibility method (the by-analysis effect was already multi-purpose pre-existing — my
  delta is a small additive slice within it, flagged here as a pre-existing size/complexity note, not a
  new issue this task introduced).
- *Style*: additive-only field placement consistent with the sibling `analysisId`/`worktype`/`regarding`
  precedent throughout (same interfaces, same JSDoc conventions, same conditional-spread idiom already
  used for `activeWorkType`/`sprkDocumentId` in the same functions).
- **Verdict: Clean.** No Critical or Warning findings. One Suggestion (pre-existing, not
  introduced): the by-analysis effect in `WorkspacePane.tsx` is already a long multi-purpose effect
  (session restore + document surfacing + now sub-domain derivation) — a future refactor could split it,
  but doing so here would expand this task's diff well beyond its scope.

**ADR check**:
- **ADR-030 (PaneEventBus)** — Compliant. No new channel (still the 5-member closed union unchanged);
  no new event-type discriminant on the bus; `subDomain` is an additive optional field on the EXISTING
  `workspace.widget_load` event's loosely-typed `compose` payload (the same additive-field convention
  `activeWorkType` already established) and on the EXISTING `AnalysisLaunchContext`/`ComposeLaunchContext`
  React contexts (neither of which is part of the PaneEventBus's 5-channel union) — no new side channel
  introduced anywhere in this diff.
- **ADR-044 (GUID canonicalization)** — Compliant. `subDomain` is explicitly documented at every call
  site as "a plain slug, not a GUID — no brace-stripping"; `buildLaunchUrl` does NOT apply
  `.replace(/^\{|\}$/g, "")` to it (correctly, unlike `analysisId`/`entityId`/`regarding`, which do);
  no GUID is mishandled by this change.
- **§11 Component Justification** — N/A: no new component/service/interface/endpoint/DI
  registration/package/Dataverse column introduced; every change is an additive optional field on an
  EXISTING interface or an additive clause on an EXISTING query string.
- **Verdict: 0 Violations, 0 Warnings.**

## Deviations / escalations

**No escalation fired.** The step-0 double-build grep was clean (neither leg had landed since
`bd64a69d4`).

**One judgment call, documented (not a deviation from instructions)**: `AnalysisLaunchContextValue.subDomain`
is threaded generically to both the `'new'` and `'existing'` `analysisLaunch` branches for shape
symmetry, but is only ACTED ON in the `'existing'` branch (`WorkspacePane`'s by-analysis effect). The
`'new'`-mode wizard-hint completion (`CreateAnalysisWizardWidget`'s `defaultSubDomain`) is a separate,
hub-owned mount path (`createAnalysisModal`/`open_create_analysis_wizard`), not the cold-load
auto-install effect this task extends, and is not required by any of the four acceptance criteria —
left unwired, noted above under Step 1.

**One pre-existing, out-of-scope finding surfaced (not fixed here)**: `npm run typecheck` currently
reports 1 surface-owned error in `ConversationPane.agreement-review-gate.e2e.test.tsx:85` (task 021's
own file, `conversation/**`, off-limits this wave per this task's HARD BOUNDARIES). Confirmed via
`git blame` to predate this task (unchanged since `98bf344d1`). This blocks a clean `npm run build`
today for reasons entirely unrelated to task 022; `vite build` alone (the actual bundler step) succeeds
cleanly. Flagged for the orchestrating session / task 031 (which owns `ConversationPane` this wave) to
fix — a one-line unused-parameter removal.

**Also surfaced (hub coordination, not fixed here)**: `CreateAnalysisWizardWidget.tsx:806`'s
`discoverNavProps` columnName check still reads `'sprk_agreementtypeid'`, which does not match the live
Dataverse schema (`sprk_agreementtype`, confirmed via MCP `describe`) — the hub's PART 3 correction
does not appear to have landed in this worktree. Noted in the NOTIFY doc; out of scope (A1 is hub-owned).

**Boundaries honored**: did NOT touch `src/solutions/SpaarkeAi/src/components/conversation/**` (task
031's territory), `src/client/shared/Spaarke.Compose.Components/**` (read-only), `infra/**`, or
`src/server/**`. Did NOT touch `.claude/**`. Did NOT touch `current-task.md`/`TASK-INDEX.md` (task 021's
own convention, followed here — the orchestrating session applies root CLAUDE.md §7's transition
steps). No git commit/push.

## Task status

POML `022-subdomain-envelope-consumer-and-deferred-legs.poml` `<status>` set to `completed` with an
inline completion summary. Per HARD BOUNDARIES, `TASK-INDEX.md` and `current-task.md` were NOT
touched — the orchestrating session/human applies root CLAUDE.md §7's transition steps after reviewing
this report.
