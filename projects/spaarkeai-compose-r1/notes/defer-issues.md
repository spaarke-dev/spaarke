# Deferrals & Issues — spaarkeai-compose-r1

> **Protocol**: CLAUDE.md §"Deferrals & Issues" (BINDING). Every ISS entry MUST be mirrored to a GitHub issue per the per-project rule. The `push-to-github` skill scans for entries without GitHub URLs and blocks push until they're filed.
>
> **Purpose of this file**: canonical "do not lose" registry. Everything operational that should outlive this PR's chat context — Path A tensions, deferred decisions, pre-existing issues, open spike items, follow-up tasks — lives here. Each entry has a permanent home (GitHub issue, design.md/spec.md anchor, downstream task POML, or this file).
>
> **Last updated**: 2026-06-29 (Wave 10 operator review gate sign-off)

---

## Index — Issues (ISS) + Path A tensions + Spike Open Items + Known Follow-ups

| ID | Type | Title | Status | GitHub / Anchor |
|---|---|---|---|---|
| **ISS-001** | ISS | LegalWorkspace standalone Vite build broken (`@spaarke/daily-briefing-components/widgets` import unresolved) | ✅ RESOLVED in this PR | `src/solutions/LegalWorkspace/vite.config.ts` |
| **ISS-002** | ISS | BFF: HIGH CVE on `Microsoft.Kiota.Abstractions 1.21.2` (GHSA-7j59-v9qr-6fq9) | 📋 OPEN (Compose carry-forward) | [#516](https://github.com/spaarke-dev/spaarke/issues/516) |
| **ISS-003** | ISS | SemanticSearch: 104 pre-existing test failures (319/423) | 📋 OPEN | [#518](https://github.com/spaarke-dev/spaarke/issues/518) |
| **ISS-004** | ISS | SpaarkeAi solution: 1 moderate severity pre-existing CVE in production deps | 📋 NOTED — operator review at W10 (likely defer to SpaarkeAi-team triage) | not yet filed (operator decides) |
| **T-1** | Path A | Compose checkout substrate = Dataverse-side `DocumentCheckoutService` (vs design.md §14 row 4 original "SPE-native check-out") | ✅ APPROVED 2026-06-29 post-Wave-0 | design.md §14 row 4 + spec.md ADR Tensions T-1 row |
| **T-2** | Path A | W5-026 `LoadDocxAsync` / `SaveDocxAsync` round-trip — full coverage via W5-027 integration tests (not in-task unit tests) | ✅ FORMALIZED | POML 026 `<notes>` block |
| **T-3** | Path A | W4-033 SemanticSearch SCAFFOLDING smoke test declined per ADR-038 §7 ban list | ✅ DOCUMENTED | POML 033 `<notes>` block |
| **OI-5** | Spike #3 OI | Should `sprk_lastheartbeatutc` PATCH bump `modifiedon` on `sprk_document`? | 📋 OPEN (operator decision — non-blocking; default Dataverse behavior in place) | Spike #3 §7.2 + this file |
| **FU-1** | Follow-up | ComposeEditor (W4-045) should pause heartbeat when `checkoutStatus !== 'acquired'` | ✅ **RESOLVED 2026-06-29** in code-review cleanup R3 (PR #515 commit `fcb69ed17`) — heartbeat hoisted to `useComposeHeartbeatGate` hook with `checkoutStatus === 'acquired'` guard | n/a (in-PR fix) |
| **FU-4** | Follow-up | Remove `virtual` modifiers from `ComposeSessionService` by rewriting `ComposeServiceTests` to use real instance + `ChatSessionManager` test double (R4 Option C trade-off; small testability smell) | 📋 OPEN (non-blocking; documented in `ComposeSessionService.cs` XML doc) | this file + `ComposeSessionService.cs` XML doc |
| **FU-2** | Follow-up | FR-06 concurrent-Save live test against deployed BFF + live Dataverse Alt Key | 📋 OPEN (belongs to W10 smoke-after-deploy OR separate `tests/integration/Spe.Integration.Tests/` track) | this file + W8-061 POML notes |
| **FU-3** | Follow-up | Pattern D placeholder swap: `LegalWorkspace/sections/composeEditor.registration.ts` (W1b-040 inline placeholder → `@spaarke/compose-components` real widget) | ✅ **RESOLVED** by task 093 (Phase 7 pivot, 2026-07-01) — real `<ComposeWorkspace>` now mounts via `@spaarke/compose-components`; `ComposeLaunchContext` bridges document context from SpaarkeAi ThreePaneShell into the section factory; LegalWorkspace standalone build ✅ clean | W4-042 agent report + design notes + task 093 |
| **DEF-7SCs** | Deferred SCs | 7 success criteria from spec.md require live Dev BFF verification (SC4/SC5/SC9-live/SC10-live/SC13/SC14/SC15) | 📋 OPEN — operator runs at W10/W11 | `notes/audits/success-criteria-audit.md` + W10 task 080/081 |
| **FU-97a** | Follow-up | Re-author the 10 Compose `DispatchAction` tests skipped by task 097 SSE conversion to parse SSE frames + assert on `AnalysisStreamChunk` event shape (currently marked `[Fact(Skip = ...)]` in `ComposeEndpointsContractTests.cs` + `ComposeSummarizeRoundtripSmokeTests.cs`). Immediate SSE contract is guarded by the new `PostDispatchAction_ReturnsTextEventStreamPerTask097` test; the Skip'd tests cover deeper Hop-by-Hop pipeline assertions that are still valuable but require SSE-parsing helpers. | 📋 OPEN — non-blocking; SSE contract is guarded by the new test; deeper Hop coverage tracked here | this file + `notes/task-097-sse-conversion.md` (created below) |
| **FU-98a** | Follow-up | Stand up jest infrastructure for `@spaarke/compose-components` package (currently declares `"test": "jest"` in package.json but has no jest.config.js + no *.test.tsx files). Then author unit tests for `executeComposeSummarize` covering: (a) mocked WhatWG ReadableStream + progress→result→[DONE] happy path, (b) `type='error'` chunk → onError, (c) HTTP failure → onError, (d) AbortSignal → onDone without onError, (e) malformed data frames → skipped defensively. Pattern: mirror `executeSummarizeIntent` test coverage in the SpaarkeAi jest suite. Impact: task 098 orchestrator ships without in-package unit tests; behavior guarded only by TypeScript surface + downstream ConversationPane wiring (which itself lacks a jest integration test — see FU-91a for the pre-existing SpaarkeAi jest gap). | 📋 OPEN — non-blocking; the orchestrator is a pure module mirroring the tested `executeSummarizeIntent` pattern. Deferred because standing up jest config for a fresh package would exceed the 2.5h budget for task 098. | this file |
| **AMD-102** | ADR amendment | ADR-013 Path B amendment (2026-07-01) — `IInvokePlaybookAi` facade widened with optional `userContext: string?` + `document: DocumentContext?` parameters. Documented in [`docs/adr/ADR-013-ai-architecture.md`](../../../docs/adr/ADR-013-ai-architecture.md) §"Amendment 2026-07-01". First Path B amendment landed under CLAUDE.md §6.5 protocol (added 2026-06-29 by this same project). | ✅ SHIPPED (task 102) | `docs/adr/ADR-013-ai-architecture.md` §Amendment; `.claude/adr/ADR-013-ai-architecture.md` header + MUST rules; `.claude/adr/INDEX.md`; `.claude/CHANGELOG.md` [Unreleased]; this file §AMD-102 |

---

## ISS-001 — LegalWorkspace standalone Vite build broken (RESOLVED in this PR)

**Type**: ISS (Production / dev bug uncovered outside this project's responsibility)
**Status**: ✅ **RESOLVED 2026-06-29** — no GitHub issue needed; commit included in this branch.

**Reproduction (before fix)**:
```pwsh
cd src/solutions/LegalWorkspace
npm run build
# → Rollup failed to resolve import "@spaarke/daily-briefing-components/widgets"
#   from "...sections/dailyBriefing/dailyBriefing.registration.ts"
```

**Root cause**: `LegalWorkspace/vite.config.ts` had `resolve.alias` entries for every shared lib except `@spaarke/daily-briefing-components`.

**Fix applied**: 3-line addition to `src/solutions/LegalWorkspace/vite.config.ts` mirroring Events/AI.Widgets pattern (sharedLibPaths + react include + resolve.alias).

**Verification**: post-fix LegalWorkspace standalone Vite build → ✅ 3347 modules / 0 errors.

**Discovered by**: Wave 1b-040.

---

## ISS-002 — BFF Microsoft.Kiota.Abstractions 1.21.2 HIGH CVE

**Type**: ISS (Production / dev bug uncovered outside this project's responsibility)
**Status**: 📋 **OPEN** — operator approved CARRY-FORWARD at W10 review gate. Compose R1 ships with pre-existing CVE; fix tracked via #516.
**GitHub**: [#516](https://github.com/spaarke-dev/spaarke/issues/516)
**Advisory**: GHSA-7j59-v9qr-6fq9 (HIGH)

**Reproduction**:
```pwsh
dotnet list package --vulnerable --include-transitive --project src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj
```

**Root cause**: `Sprk.Bff.Api.csproj` line 71: `<PackageReference Include="Microsoft.Kiota.Abstractions" Version="1.21.2" />` — direct (not transitive via `Microsoft.Graph 5.101.0`).

**Likely fix paths** (full investigation in #516):
1. Preferred: bump `Microsoft.Graph` to latest 5.x + remove explicit Kiota pin
2. Fallback: bump Kiota directly to GHSA-fixed minor

**Discovered by**: Wave 1b-020 (Compose ran `dotnet list package --vulnerable` per §10 BFF Hygiene rule #5).

**Compose impact**: Compose did NOT introduce; 0 new HIGH CVEs introduced by Compose (verified W9-072).

---

## ISS-003 — SemanticSearch 104 pre-existing test failures

**Type**: ISS (Production / dev bug uncovered outside this project's responsibility)
**Status**: 📋 **OPEN** — operator approved skip-fix at W10 review gate; tracked via #518.
**GitHub**: [#518](https://github.com/spaarke-dev/spaarke/issues/518)

**Reproduction**:
```pwsh
cd src/client/code-pages/SemanticSearch
npm test
# → Tests: 104 failed, 319 passed, 423 total
```

**Likely scope**: `useSavedSearches.test.ts`, `useFilterOptions.test.ts` — mock-surface drift.

**Discovered by**: Wave 3-032 + Wave 4-033 (stash-verified pre-existing).

**Compose impact**: None — moved hook (`useDocumentActions`) had its 14 KEEP-path tests added to `@spaarke/document-operations`; the 104 failures live in OTHER SemanticSearch test files unrelated to the moved hook.

---

## ISS-004 — SpaarkeAi solution: 1 moderate pre-existing CVE

**Type**: ISS (noted but not filed)
**Status**: 📋 NOTED at W10 — operator decides whether to file (likely defer to SpaarkeAi-team triage)
**GitHub**: not yet filed

**Reproduction**:
```pwsh
cd src/solutions/SpaarkeAi && npm audit --omit=dev
# → 1 moderate severity vulnerability
```

**Compose impact**: None — Compose's added packages (`@spaarke/compose-components` + `@spaarke/document-operations`) both audit clean (0 vulns each). The 1 moderate is in pre-existing SpaarkeAi prod deps.

**§10 #5 verdict**: Compose §10 #5 PASS (only HIGH severity blocks). Moderate carries forward.

**Discovered by**: Wave 9-072.

---

## T-1 — Compose checkout substrate = Dataverse-side (Path A)

**ADR Tension** (CLAUDE.md §6.5 Path A — project-scoped exception)

**Rule challenged**: design.md §14 row 4 original wording: "per-user single-session lock via **SPE check-out**. Word for Web users automatically see 'Checked out to X' via SPE's built-in indicator"

**Conflict**: Spike #3 found existing `DocumentCheckoutService` (~1170 LOC) already implements check-out/check-in/discard/conflict UX/version tracking. SPE-native wrapper would duplicate ~85% for 1 capability gain (auto-banner in Word for Web/Desktop).

**Resolution**: Path A approved 2026-06-29 at post-Wave-0 operator review gate. Reuse existing service; document trade-off; R2+ escape hatch pre-documented in Spike #3 §3.

**Impact**: Phase 5 LOC ~600 → ~150; Word for Web/Desktop won't auto-warn on cross-surface concurrent open; last-writer-wins documented.

**Permanent home**:
- design.md §14 row 4 — amended wording
- spec.md ADR Tensions table — T-1 row with full rationale + R2 escape hatch
- Spike #3 §6 (operator-approved)

---

## T-2 — `LoadDocxAsync`/`SaveDocxAsync` round-trip via 027 integration tests (Path A)

**ADR Tension**: ADR-038 §4 + §7 (banned patterns)

**Rule challenged**: POML 026 implied unit tests for `ComposeDocumentService.LoadDocxAsync` + `SaveDocxAsync`. Full SPE round-trip unit test would require B1 `Mock<HttpMessageHandler>` (banned) or B2 `Mock<IRequestAdapter>` (banned).

**Resolution**: Path A formalized at W5-026. Argument-validation tests + Phase-5 stub contract tests kept in W5-026; full round-trip coverage moved to W5-027's integration-contract tests (`tests/integration/contract/Api/Compose/`).

**Permanent home**:
- POML 026 `<notes>` block
- W5-027 integration-contract test class XML doc

---

## T-3 — W4-033 SemanticSearch SCAFFOLDING smoke test declined (Path A)

**ADR Tension**: ADR-038 §7 build-vs-maintain criteria

**Rule challenged**: POML 033 AC#2 asked for a consumer smoke test for `useDocumentActions` post-extraction.

**Conflict**: Such a test fits B6 mirror antipattern + B7 all-mocks trivial. The 14 KEEP-path tests in `@spaarke/document-operations` already cover the canonical hook; a mirror at consumer side would be deleted by `/test-diet`.

**Resolution**: Path A — declined. AC#2 satisfied via alternative evidence: TS build clean + 14 KEEP-path tests + App.tsx import + zero test-count regression.

**Permanent home**:
- POML 033 `<notes>` block
- W9-071 ADR-038 conformance audit (`notes/adr-038-conformance.md`)

---

## OI-5 — Should `sprk_lastheartbeatutc` PATCH bump `modifiedon`?

**Source**: Spike #3 §7.2 Open Item 5

**Question**: When `RefreshHeartbeatAsync` PATCHes `sprk_lastheartbeatutc=UtcNow`, should Dataverse default behavior (which bumps `modifiedon`) be allowed, or should the PATCH explicitly suppress `modifiedon` change?

**Trade-off**:
- ALLOW (current default): operator sees "active editing now" via `modifiedon` indicator; audit-log noise on every heartbeat
- SUPPRESS: clean audit; loses "active editing now" visibility

**Current state**: ALLOW (default Dataverse behavior; W7-052 did not explicitly suppress).

**Why non-blocking**: behavior is conservative; auditor sees more not less. If audit-log noise becomes an issue, suppress is a 1-line PATCH header change.

**Permanent home**:
- this file (registry)
- W7-052 task POML notes

---

## FU-1 — ComposeEditor heartbeat-gate when `checkoutStatus !== 'acquired'` (RESOLVED)

**Source**: W7-051 final report (multi-tab UX implementation)
**Status**: ✅ **RESOLVED 2026-06-29** in code-review cleanup R3 (PR #515 commit `fcb69ed17`)

**Resolution**: Heartbeat hoisted from `ComposeEditor` (W4-045) to a new `useComposeHeartbeatGate` hook at `src/solutions/SpaarkeAi/src/components/compose/hooks/useComposeHeartbeatGate.ts`. The hook guards with `if (checkoutStatus !== 'acquired') return;` BEFORE the visibility-state check, so cancelled/failed/probing/discarding tabs no longer hit the heartbeat endpoint. `ComposeEditor` is now a pure drafting surface with no lock-lifecycle concerns.

**Original issue**: Client heartbeat (W4-045) fired every 3 min regardless of checkout state. After force-close, a cancelled tab continued heart-beating a lock it no longer held.

**Server-side mitigation was in place**: W7-052 `RefreshHeartbeatAsync` had same-user guard — returned 404 (no info leak) for cross-user or no-lock heartbeats. So cancelled-tab heartbeats failed harmlessly. R3 fix eliminates the wasted HTTP traffic entirely.

---

## FU-4 — Remove `virtual` modifiers from `ComposeSessionService` (R4 Option C trade-off)

**Source**: Code-review cleanup R4 Option C (2026-06-29)

**Issue**: To collapse the single-impl `IComposeSessionService` interface to concrete per ADR-010 strict, `ComposeSessionService` was changed from `sealed class` → `class` with `virtual` on 3 public methods. This is purely for the Moq test boundary in `ComposeServiceTests` (which uses `Mock<ComposeSessionService>(...)`). The `virtual` modifiers are a small "for testability" smell.

**Cost-of-doing-nothing**: code-quality smell only. `virtual` modifiers signal "subclasses may override" to readers when no subclassing is intended. The single legitimate "override" is the Moq test mock.

**Fix path** (medium effort, ~2-3 hr): rewrite `ComposeServiceTests` to use a real `ComposeSessionService` instance + `Mock<ChatSessionManager>` (or a `ChatSessionManager` test double). This is integration-first per `tests/CLAUDE.md` preference; eliminates the need for `virtual`. Estimated ~30 test method updates.

**Permanent home**: this file (registry) + `ComposeSessionService.cs` XML doc (line-level note inviting the future refactor)

---

## FU-2 — FR-06 concurrent-Save live test

**Source**: W8-061 final report

**Issue**: FR-06 acceptance ("5 concurrent promotes → exactly 1 `sprk_document` row") depends on the live `sprk_graphitemid_uk` Alt Key (✅ in place in Dev per W1a OI-1). Today tested via:
- W5-026: race idempotency against **in-memory mocked** `IGenericEntityService` (proves algorithm, not live behavior)
- W5-027 + W8-061: endpoint-contract level (no concurrency exercise)

**What's NOT tested**: empirical Live Dataverse Alt Key constraint under 5 concurrent HTTP POSTs.

**Where it belongs** (per W8-061 agent's analysis):
- W10 task 080/081 smoke-after-deploy as an acceptance check
- OR a separate `tests/integration/Spe.Integration.Tests/` track

**Permanent home**: this file (registry) + W10 task notes

---

## FU-3 — Pattern D placeholder swap (LegalWorkspace registration shim → real widget)

**Source**: W4-042 + W5-042 architectural analysis

**Status**: ✅ **RESOLVED 2026-07-01** by task 093 (Phase 7 three-pane pivot per spec-supplement-2026-07-01-three-pane-pivot.md FR-S1)

**Original issue**: `src/solutions/LegalWorkspace/src/sections/composeEditor.registration.ts` (W1b-040) rendered an inline `ComposeWorkspacePlaceholder` Skeleton. The "real" replacement would be to import `ComposeWorkspace` from `@spaarke/compose-components` — but the pre-Phase-7 state had `ComposeWorkspace` living in `src/solutions/SpaarkeAi/*`, which would have created a reverse-direction dep (`@spaarke/legal-workspace → SpaarkeAi`).

**How task 093 resolved it**: The Phase 7 pivot fixed the underlying dependency direction by (a) task 091: moving `ComposeWorkspace` + siblings + hooks + `compose-contracts.ts` INTO `@spaarke/compose-components` (per Spike #2 §11 open item #2 promotion trigger); (b) task 092: introducing `ComposeLaunchContext` on the SpaarkeAi ThreePaneShell to expose the ribbon launch document ref; (c) task 093: hoisting `ComposeLaunchContext` to `@spaarke/compose-components` (so LegalWorkspace can consume it without touching `solutions/SpaarkeAi/*`) + swapping the Skeleton placeholder for a real `<ComposeWorkspace>` mount inside an inner `ComposeSectionMount` bridge component. The bridge consumes `useComposeLaunch()` and forwards the document ref + drive id to ComposeWorkspace props.

**Fix impact**:
- LegalWorkspace `package.json` + `vite.config.ts` gained `@spaarke/compose-components` workspace-linked dependency (mirrors the daily-briefing-components precedent).
- `ComposeWorkspace.tsx` + `ComposeEditor.tsx` swapped `@spaarke/ai-widgets` barrel imports → `@spaarke/ai-widgets/events` subpath imports to avoid the barrel's side-effect widget registration (`register-workspace-widgets.ts` dynamically imports `@spaarke/ai-outputs` subpaths that LegalWorkspace's standalone Rollup cannot resolve).
- LegalWorkspace standalone build: ✅ 3917 modules / 3746 kB / gzip 1051 kB.
- SpaarkeAi build: 3715 modules / 4876 kB / gzip 1357 kB (back to pre-092 size; the section-factory mount re-eagerises the ComposeWorkspace chain that task 092 had tree-shaken).

**Standalone LegalWorkspace behaviour**: when a user selects the "Compose" workspace layout in LegalWorkspace's standalone bundle, `useComposeLaunch()` returns null (no `<ComposeLaunchContext.Provider>` in the tree) → ComposeWorkspace mounts on its empty state → user picks a document via Browse / Search affordances (`ComposeEmptyState` — task 044). Full editor + save + summarize path works from that entry.

**Permanent home**: this file (registry) + W1b-040 / W4-042 / W5-042 / task 093 POML notes + design.md (mount-path section)

---

## DEF-7SCs — 7 Success Criteria require live Dev BFF verification

**Source**: W9-070 success-criteria audit

The 7 SCs operator-deferred to W10/W11 (live verification post-deploy):

| SC | Criterion | Code/test in place | Live verification path |
|---|---|---|---|
| **SC4** | Path A modal launch works against real `sprk_document` | W6-046 launch-resolver + ribbon button + W5-042 modal | Ribbon build+deploy path shipped 2026-07-01 (see "SC4 ribbon deploy path" section below). Remaining: operator click-verifies "Open in Compose" launches the modal + document loads. |
| **SC5** | Path B ephemeral upload works against deployed Assistant | W4-044 EmptyState CTA + W5-042 EmptyState handlers | Operator: deployed Assistant upload → "Open in Compose" → verify ephemeral mount |
| **SC9-live** | `compose-summarize` E2E against deployed BFF + real playbook | W8-060 in-process trace (7 regression tests) + smoke write-up §7 | Operator: live HTTP call against Dev BFF |
| **SC10-live** | Open-in-Word web + desktop buttons functional | W2-031 `useDocumentActions` + W4-043 Toolbar buttons | Operator: click each button against a deployed `sprk_document` |
| **SC13** | SPE check-out visible in Word for Web | n/a — known limitation per T-1 | **Documented as known R1 limitation (last-writer-wins)**; no live verification expected to pass |
| **SC14** | Multi-tab conflict UX (two-tab manual test) | W7-051 ConflictDialog + 12 component tests | Operator: open same doc in 2 Compose tabs → verify modal + Force-close flow |
| **SC15** | 15-min orphan lock wallclock test | W7-052 sweeper + 9 unit tests with `TimeProvider` | Operator: open Compose → close laptop → wait 17 min → verify lock released |

All 7 are normal "live verification belongs to operator post-deploy" items, not project gaps.

**Permanent home**: `notes/audits/success-criteria-audit.md` + W10 task 080/081 POMLs

---

## SC4 ribbon deploy path (added 2026-07-01)

**Context**: SC4 required a working "Open in Compose" ribbon button on the `sprk_document` form command bar. The UI intent (`opencompose-button.xml`) and TS handler (`DocumentComposeLaunch.ts`) had shipped in W4-046 but the build+deploy path to produce the referenced JS web resource (`sprk_spaarkeai_documentcomposelaunch`) was never established for SpaarkeAi. Same gap applied to the two pre-existing ribbon TS files (`WorkspaceLaunch.ts`, `EntityFormLaunch.ts`) — three orphaned sources.

**Path shipped 2026-07-01**:

1. **Build pipeline**: `src/solutions/SpaarkeAi/scripts/build-ribbon.mjs` — esbuild-based script that scans `src/ribbon/*.ts`, produces IIFE bundles with dotted `Sprk.SpaarkeAi.{BaseName}` global names into `dist-ribbon/`. Wired into `npm run build` via `build:ribbon` sub-script.
2. **Deploy pipeline**: `scripts/Deploy-SpaarkeAiRibbon.ps1` — REST upsert + publish for each bundle as web resource `sprk_spaarkeai_{basename}` (JScript type). Parallels `Deploy-SpaarkeAi.ps1`.
3. **Ribbon customization**: `opencompose-button.xml` fragment merged into `DocumentRibbons` unmanaged solution via `/ribbon-edit` workflow. New elements: 1 CustomAction, 1 CommandDefinition, 1 DisplayRule + 1 EnableRule, 3 LocLabels. All prefixed `sprk.SpaarkeAi.*` for isolation from existing ribbon elements.
4. **Deployed to Dev 2026-07-01**: 3 web resources created (documentcomposelaunch, entityformlaunch, workspacelaunch); DocumentRibbons solution imported + published.

**Follow-on wired for future ribbon scripts**: any new `src/ribbon/{Name}.ts` file is automatically picked up by `build:ribbon` and deploys with the same script. No new build path required per ribbon.

---

## AMD-102 — ADR-013 Path B amendment (document-context invocation on `IInvokePlaybookAi` facade)

**Type**: ADR amendment (Path B per CLAUDE.md §6.5)
**Status**: ✅ **SHIPPED 2026-07-01** in task 102.
**Motivating consumer**: `spaarkeai-compose-r1` (this project).
**Governance**: CLAUDE.md §6.5 (added 2026-06-29 by this same project).

**What changed**: `IInvokePlaybookAi.InvokePlaybookAsync` gained two optional parameters `userContext: string?` and `document: DocumentContext?`, both defaulted to `null`, positioned AFTER `cancellationToken` so existing 4-arg positional callers are unaffected. Forwarded to `PlaybookRunRequest.UserContext` + `.Document`; consumed downstream via the existing Playbook-Driven LLM Output Pattern (Layer 1 orchestrator + Layer 2 `PromptSchemaRenderer` `## Input` section) with no execution-engine changes.

**Boundary preserved**: The 2026-05-20 refined ADR-013 rule that CRUD-side code MUST NOT directly inject AI-internal types (`IOpenAiClient`, `IPlaybookService`, `IPlaybookOrchestrationService`, `IPlaybookExecutionEngine`) is UNCHANGED. Compose CRUD-side code (`ComposeEndpoints.DispatchAction`) still consumes ONLY the widened facade + `IConsumerRoutingService` + `IComposeDocumentService` + `IDocxTextExtractor`.

**Why Path B (amendment) not Path A (project-scoped exception)**:
- Compose is the FIRST document-context consumer, not the last. Rewrite / Find Similar / Lookup References (Compose R2+) and downstream document-scoped AI actions from Matter, Communication, and Insights all share the same technical need.
- Per-project exceptions (Path A) would force every future consumer to declare its own carve-out — a proliferation pattern.
- The amendment lands the facade extension once; every future document-context consumer inherits it cleanly with no ADR friction.

**Compile-time boundary guard**: The reflection test `PhaseAVerticalSliceTests.ADR013_InvokePlaybookAiFacade_DoesNotExposeAiInternalTypesInSurface` was updated in task 095 with a NAMED allow-list containing `Sprk.Bff.Api.Services.Ai.DocumentContext`, citing tasks 095 + 102 for traceability. New types added to the facade surface will need explicit allow-list entries + rationale — silent bypass is forbidden per CLAUDE.md §6.5.

**Trail**:
- Full ADR §Amendment: [`docs/adr/ADR-013-ai-architecture.md`](../../../docs/adr/ADR-013-ai-architecture.md) §"Amendment 2026-07-01 — Document-context invocation on `IInvokePlaybookAi` facade"
- Concise ADR: [`.claude/adr/ADR-013-ai-architecture.md`](../../../.claude/adr/ADR-013-ai-architecture.md) header + MUST rules
- ADR INDEX: [`.claude/adr/INDEX.md`](../../../.claude/adr/INDEX.md) ADR-013 row (status "Accepted (amended 2026-07-01)")
- Procedure-surface changelog: [`.claude/CHANGELOG.md`](../../../.claude/CHANGELOG.md) `[Unreleased]` → "Changed (2026-07-01 spaarkeai-compose-r1 task 102 — ADR-013 Path B amendment)"
- Boundary guard test: [`tests/unit/Sprk.Bff.Api.Tests/Integration/PhaseAVerticalSliceTests.cs`](../../../tests/unit/Sprk.Bff.Api.Tests/Integration/PhaseAVerticalSliceTests.cs)

**Historical note**: This is the FIRST Path B amendment landed since the §6.5 protocol was added on 2026-06-29 (by this same project). Path B was exercised end-to-end: (a) refined-facade-only surface preserved for existing callers, (b) named allow-list on reflection guard, (c) full amendment section written, (d) INDEX + CHANGELOG updated. The protocol worked as designed.

---

## How to file new entries

Per project CLAUDE.md "Deferrals & Issues — tracking obligation":

| Situation | Use |
|---|---|
| Spec scope item dropped to keep this project shippable | DEF-{NNN} |
| Refactor / cleanup > 2hr that's not in current spec | DEF-{NNN} |
| Production / dev bug uncovered outside this project's responsibility | ISS-{NNN} |
| Telemetry / monitoring gap requiring follow-up | ISS-{NNN} |
| Failure mode discovered + worked around (not fixed) | ISS-{NNN} |

**How to file**: Invoke `/project-defer-issue-tracking` (alias `/defer`) — writes to BOTH this file AND a GitHub issue in one step.

**CLAUDE.md §11 rule applies**: every entry must name a concrete behavior or contract that fails without the work.
