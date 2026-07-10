# End-to-End Completion Audit — spaarke-ai-architecture-redesign-r2

> **Date**: 2026-07-10 (evening) · **Directed by**: operator, after compose-r2 found tasks marked ✅ that were shims / not wired / not functional
> **Method**: 6 parallel adversarial audit agents (opus) — one per subsystem cluster — instructed to REFUTE each ✅ claim with file:line evidence, against the POML acceptance criteria as ground truth. Plus main-session: full local test-suite run (all 4 projects), live-environment verification (Dataverse catalog, Cosmos, healthz), and CI-gate forensics.
> **Scope**: all 59 completed tasks (Phase 0, A0 contracts, A-infra, D-F0, Wave J, Phase E, memory wave M, hardening H) + the CI/eval gates they relied on.

---

## Headline verdict

**No fabricated completions.** The compose-r2 failure class (shim behind a real-looking surface, mocks-proving-mocks, Null-Object masquerading as the real impl) was hunted explicitly and **not found in any cluster**. The gate engine, memory store, dispatch spine, contracts, trace surface, ack loop, and progressive render are all genuinely built, DI-wired, and reached from production paths.

**But the audit found 4 substantive "built-but-not-load-bearing" gaps** (2 undisclosed, 2 under-disclosed), **1 systemic CI defect** that had been hiding 6 deterministic test failures + 5 ADR violations on master behind green checkmarks, and a tail of minor items. Register below.

---

## Findings register

### F-1 🔴 ContextEnvelope is telemetry-only — renderer has ZERO production callers (task 053 → PARTIAL)
`ContextEnvelopeRenderer.Render` is never called outside its own tests. `ContextBinder.BindAsync` runs per-turn on interactive (`ChatEndpoints.cs:642-675`) and dispatch (`SessionDispatchOrchestrator.cs:389-409`), but the returned envelope is **discarded** — only the counts-only fingerprint persists. The Business/User/Memory/Organizational slices reach **no LLM prompt on any path**. Interactive record-memory recall actually works via the legacy `PlaybookChatContextProvider.AppendRecordMemoryAsync` (`:277,:389,:876-888`) direct prompt append — NOT via the Binder.
- Dispatch side: disclosed (PE-D8/#619). **Interactive-side discard: undisclosed** (only implicitly covered by the "prompt bytes unchanged" parity ruling).
- Corollary: 055's resolved `CallerContactId` and 060's organizational refs are computed but unconsumed (they ride the dropped envelope).
- **Consequence**: "one ContextEnvelope every AI turn consumes" (the 053/ADR-043 posture) is true at the shared-producer-function level, not at prompt-assembly level. Memory recall itself DOES work end-to-end (legacy path), so no user-visible feature is broken — the architecture convergence is incomplete.
- **Recommended disposition**: widen PE-D8/#619 to explicitly cover the interactive-side renderer wiring (or file a dedicated issue) + decide whether r2 wires it or a follow-on does.

### F-2 🔴 User-scope memory has NO recall path (tasks 050/057 adjacent — undisclosed)
`memory.write scope=user` persists (`MemoryItemStore.UpsertAsync`), is reviewable/deletable via `GET/DELETE /api/memory/user`, but **no code reads user-scope memory into any prompt or envelope**. `GetForUserAsync` is consumed ONLY by the governance endpoints (`MemoryGovernanceEndpoints.cs:127,199`). Both recall sites are Record-scope only (`ToRecordPromptFragmentAsync` / `GetForRecordAsync`). The ContextBinder User slice carries only CallerContactId. The 057 eval exercises record-scope recall only, so this was never caught.
- **Consequence**: the User-scope half of the "automatic memory" value prop is capture-only. An AI-captured user preference is never seen again by the AI.
- **Recommended disposition**: wire a user-memory prompt fragment (mirror the record-scope fragment at the same provider site) in r2, OR file explicitly. Not currently in defer-issues.md nor ADR-042's deferred boundary.

### F-3 🟠 Job-aware completion is dormant — NFR-12 not enforced on any live path (task 036 → PARTIAL; 035 claim overstated)
`CompletionEngine.ComposeJobAware` → `JobAwareOutcomeProjection.ForJobAwareOutcome` has **zero production callers**. Contract + `EnsureIngestionParity` fail-loud guard + `ToOutcomeStatus` (only fully-Completed → Succeeded) are all correct and tested (`JobAwareOutcomeProjection.cs:53-176`) — but any real async-job-backed side effect routed through `OutputRouter.RouteAsync` gets `ComposeForRoutedOutput`'s hardcoded `OutcomeStatus.Succeeded` (`CompletionEngine.cs:74`). The "doc-create card can't render done while indexing queued" invariant is contract-tested, not live.
- 035's sync paths ARE fully wired (all 3 choke-points: `OutputRouter.cs:223+`, `TypedHandlerResumeExecutor.cs:372`, `SideEffectGateAIFunction.cs:478`).
- **Recommended disposition**: either wire a real job-backed side effect (document-create/indexing) through `ComposeJobAware`, or re-classify 036 as a published-for-Compose seam with live wiring deferred (file it).

### F-4 🟠 Next-step chips ship visible-but-dead in SpaarkeAi (task 062 — precursor-scope defensible, activation untracked)
`TargetBindingId` is threaded C# → SSE → TS → `onNextStep` (`OutcomeCard.cs:308`, `SprkChat.tsx:2460`), but the shipped host never passes the callback: `ConversationPane.tsx:413-443` omits `onNextStep`, and `OutcomeCard.tsx:352` disables chips when it's absent. Every `invoke_capability` chip renders inert in production.
- **Recommended disposition**: one-step activation — `ConversationPane` passes `onNextStep={(chip) => dispatchConsumer(chip.targetBindingId, …)}` (dispatcher already imported there). Either do it in r2 or track as an explicit activation item.

### F-5 🔴 SYSTEMIC (repo-level, not r2): CI test gate reads ONE overwritten TRX — failures in 3 of 4 test projects are invisible
`Build & Test` runs `dotnet test` solution-wide with `--logger "trx;LogFileName=pass1.trx"`; each test project **overwrites** the same file (CI log: repeated "WARNING: Overwriting results file"). `classify-and-retry.ps1` then finds "1 TRX file" (the last project = `Sprk.Bff.Api.Tests`) and declared "PASS 1 CLEAN: 0 failures" on master run 29108634079 — a run whose own logs show **9 test failures** across `Sprk.Bff.Api.IntegrationTests` (4) and `Spe.Integration.Tests` (3, of which 1 = #621) and 5 ADR ArchTest failures. This defeats the entire two-pass classifier design (ci-cd-unit-test-remediation-r1).
- **Fix is small**: unique TRX per project (drop `LogFileName` → auto-named TRX, or per-project results subdirs); the classifier already searches recursively.
- Additionally: Code Quality's NetArchTest step is `continue-on-error: true` by design (advisory) — 5 ADR-007/009/010 failures currently live on master unaddressed (see F-6).
- **Recommended disposition**: URGENT repo-level fix + issue; affects every project's merge confidence, both AI projects relied on "CI green" all week.

### F-6 🟠 Pre-existing deterministic test failures on master (NOT r2 regressions — all R1-era, exposed by this audit's local full-suite run)
| Test | Cause | Since |
|---|---|---|
| `CanvasServerMappingDriftTests` ×2 | Guard walks repo for `playbookNodeSync.ts` — deleted by redesign-r1 task 053 (`df874a910`, PlaybookBuilder de-scoped) | r1 |
| `Phase1StableIdMigrationSuite` Consumer06/07 | Assert `AppOnlyAnalysisService.EmailAnalysisPlaybookId`/`DocumentProfilePlaybookId` consts — removed when r1 migrated consumers onto Bindings (`2d61b1c`) | r1 |
| `AnalysisEndpointsIntegrationTests.ExecuteAnalysis_WithSoftFailure_ReturnsPartialStorageTrue` | `doneChunk.PartialStorage` null (expected true) | r1-era (machinery last touched `8c48d0145`/`f08f77d70`) |
| `AnalysisEndpointsIntegrationTests.ExecuteAnalysis_UsesFullUACAuthorization` | Fixture's `AuthorizationFilterCalled` false — filter not invoked in test host | r1-era |
| 5 × ADR ArchTests (ADR-007 Graph isolation, ADR-009 IDistributedCache, ADR-010 ×3) | Advisory job (`continue-on-error`), violations accumulated | unknown |
- All invisible to CI because of F-5. **Recommended disposition**: file as repo-debt issues (stale guards likely want update-or-delete per ADR-038 test-diet logic; ExecuteAnalysis pair needs behavioral root-cause; ADR violations need triage).

### F-7 🟡 Live per-turn budget doesn't measure the volatile tail (task 054 — disclosed-adjacent)
`EnvelopeBudget.Evaluate(envelope)` is invoked with `conversationTokens=0, recordMemoryTokens=0` (`ContextBinder.cs:504`) — the structurally-unbounded ~8k Conversation tail (the exact task-002 escalation) is caught only by the eval gate, never measured on a live turn. Author-disclosed as PE-D8-adjacent tracker follow-on; confirm acceptable or fold into F-1's issue.

### F-8 🟡 Gate "ambiguity" input is a plumbed-but-unfed seam (task 044 — transparently documented in code)
`_dispatchUncertaintyProbe` and `ContentSafetyFlagged`/`SafetyPerimeterDegraded` are honestly `false` in production (`SideEffectGateAIFunction.cs:381-405`) — no producer threads a real signal, so ConfirmDialog is reachable only via fail-closed routes (unparseable/absent risk profile) or Elicit (incomplete args). Layer-1 clarify-when-torn covers ambiguity today. Anti-shim integration test injects a real probe. Follow-on: thread a real uncertainty/content-safety producer.

### F-9 🟡 021 regression guard doesn't protect the EntityInfoWidget fix
The `timeZone:'UTC'` prod fix is genuine (`EntityInfoWidget.tsx:145`), but the covering test (`EntityInfoWidget.test.tsx:129-137`) isn't TZ-pinned — on a UTC CI runner it passes even if the fix is reverted. Add a `TZ=America/New_York` regression pin.

### F-10 🟡 070 harness CVE scan is informational-only and can false-clean
`dotnet list package --vulnerable` result never gates classification, no `$LASTEXITCODE` check; recorded baseline shows `cve_check:"HIGH SEVERITY FOUND"` beside `classification:"OK"`. If §10 bullet-5 is meant to be harness-enforced, wire it as a hard-stop; today enforcement is reviewer discipline only.

### F-11 ⚪ Minor / cosmetic
- Stale comment `SprkChatAgentFactory.cs:662-673` still describes pre-044 always-suspend semantics (functional code correct).
- PE-D7 (#618) widened again: a THIRD `AuditLogServiceTests` test (`PartitionsByTenantAndMonthBucket_NotBareTenantId`) flakes under full-suite parallelism; 16/16 in class isolation (re-verified this audit). `[Collection]`-serialize the class.
- 064 note's call-site line numbers drifted; coverage is actually BROADER than claimed (3rd site `SideEffectGateAIFunction.cs:645`).
- 076 "grep-zero" overstated by one dead explanatory comment (`AiModule.cs:258`).
- 2 residual client-TS JSDoc mentions of retired workspace-tab tools (known, cosmetic).

---

## What was confirmed genuinely wired (highlights)

- **044 gate wiring** — `ConfirmationPolicyEngine` IS the single live decider (`SideEffectGateAIFunction.cs:250-268`); old always-suspend-by-class outcome path is gone; Silent leg is a real engine decision from catalog DATA (memory.write row declares tier-1/reversible → Execute), NOT a bypass; email leg drafts-never-sends (`:515`); `action_outcome` SSE consumed + rendered by the mounted SpaarkeAi client (`useSseStream.ts:374`, `SprkChat.tsx:1007`).
- **Memory capture→recall (Record scope)** — full trace verified: catalog row → assembly-scanned `MemoryWriteHandler` → gate-wrapped → engine Silent → `UpsertAsync` (deterministic-id supersession + subject-key normalization at ALL 5 public store methods) → recall via generalized any-entity provider (`PlaybookChatContextProvider.cs:876-888`). Governance endpoints mapped + authorized + 403-before-store-query; ttl genuinely written; audit events genuinely emitted.
- **Dispatch spine (Phase E)** — DispositionRoutability is genuinely single-source (one table, three consumers, drift guard); coded-workflow branch real end-to-end incl. 422 legs and acting-user email resolution; `## Input` single-producer convergence real (no residual replicas).
- **020 triple-twin** — parity KEEP test compares real JSON source ↔ real compiled metadata (not self-to-self); health-check drift dimension registered and Unhealthy-wired.
- **074/075** — audit writes cut over to `audit-partitioned` (zero legacy writers); retirement kept-legs all alive (state endpoint mapped, service registered, ack loop survived, seed tombstones byte-equal).
- **037/038/039** — ack loop full-circle (server frameId → client POST after tab materializes → 8s TimeProvider timeout honest-fail); trace endpoint + widget genuinely mounted; progressive reveal default-on over stored payloads.
- **Contracts (010–016)** — all real, all consumed in production (none is a published-but-unused seam), tests exercise the real types.

## Live-environment verification (spaarkedev1, this session)
- `/healthz` = 200 Healthy.
- `SYS-Memory Write` row `2172b721-…` **Active** (the 057 "live seed deferred" note in code is superseded — seeding happened at the #620 deploy).
- 3 retired workspace-tab rows **Inactive** (Get/Close/Update); `SYS-Send Workspace Artifact` correctly **Active**.
- Cosmos `spaarke-ai` db: `memory-items` + `audit-partitioned` live alongside untouched legacy `memory` + `audit`.

## Test-suite adjudication (local full run, all 4 projects)
- `Sprk.Bff.Api.Tests` 8135/8237 pass, 1 fail = PE-D7 flake (16/16 in isolation). Eval gate 83/83 (in CI too).
- `Sprk.Bff.Api.IntegrationTests` 4 fail = F-6 pre-existing (Canvas ×2, StableId ×2).
- `Spe.Integration.Tests` 3 fail = F-6 ExecuteAnalysis ×2 + #621 SessionCleanup (known).
- `Spaarke.Scheduling.Tests` clean.
- **Zero failures attributable to r2 code.**

---

## Recommended remediation slate (operator to direct)
| Priority | Item | Size | Owner suggestion |
|---|---|---|---|
| P0 | F-5 CI TRX-overwrite fix + issue | small (workflow + script) | repo-level, immediate |
| P1 | F-2 user-memory recall wiring OR explicit deferral | small-medium | r2 (before 069 UAT would be ideal — UAT script exercises user memory) |
| P1 | F-1 interactive envelope-render disclosure + decision (fold into/widen #619) | decision + doc now; wiring later | r2 files; wiring = follow-on |
| P2 | F-3 job-aware wiring or reclassify+file | decision | r2 files |
| P2 | F-4 onNextStep host wiring | tiny (1 prop) | r2 or follow-on |
| P2 | F-6 repo-debt issues (4 stale/broken tests + ExecuteAnalysis root-cause + ADR-violation triage) | filing + separate fixes | repo-level |
| P3 | F-7..F-10 + F-11 items | small each | r2 close or backlog |
