# Task 147 — Final Code-Review Pass

> **Project**: chat-routing-redesign-r1
> **Phase**: 7 (WP4 Retirement + Project Closeout)
> **Rigor**: STANDARD
> **Date**: 2026-06-25
> **Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1` at `a730d9092`
> **Scope**: 309 files changed vs `origin/master 8579d6536` (+24,663 / -23,197 LOC)

---

## Scope

### Triage tiers applied

| Tier | Approach | Files reviewed |
|---|---|---|
| **Tier 1** — line-level | Every method, every branch | 10 files (SprkChatAgentFactory.cs ~2700 LOC, OrchestratorPromptBuilder.cs 371 LOC, DirectOpenAiAgent.cs 293 LOC, PlaybookCandidateSelector.cs 208 LOC, IntentRerankerService.cs 425 LOC, PlaybookDispatcher.cs 1067 LOC, PlaybookOptionsEventBuilder.cs 239 LOC, DeliverCompositeNodeExecutor.cs 360 LOC, GetWorkspaceTabContentHandler.cs 675 LOC, ConsumerRoutingService.cs 439 LOC) |
| **Tier 2** — pattern-level | Public surface, key wiring, integration boundaries | DI module diffs (Program.cs, AiChatModule.cs, EndpointMappingExtensions.cs, RoutingModule.cs), SprkChatMessageRenderer.tsx (FR-49 link buttons), ConversationPane.tsx (task 117b), StructuredOutputStreamWidget.tsx (task 114b) |
| **Tier 3** — orphan grep | Phase-7 retirement integrity | `CapabilityRouter` / `ICapabilityRouter` / `CapabilityManifest` / `CapabilityValidator` / `DataverseCapabilityManifestLoader` / `ManifestRefreshService` / `CapabilityClassificationPromptBuilder` searched across `src/` + `tests/`. No code references; comment references only (logged MINOR). |

### Cross-cutting checks performed

- **ADR-013 facade boundary**: grepped `IOpenAiClient` / `IPlaybookService` across CRUD-side directories (`Services/Workspace/`). Only comment claims of compliance found; no real injections. PASS.
- **ADR-029 publish hygiene**: `git diff --name-only` confirms `Sprk.Bff.Api.csproj` is NOT in the diff. No `<PublishTrimmed>` / `<PublishAot>` introduced. PASS.
- **ADR-033 streaming hot-path**: `IncrementalJsonParser` / `StreamStructuredCompletionAsync` filenames not in diff. PASS.
- **Phase 7 retirement integrity**: all `CapabilityRouter`-family classes deleted; no orphaned consumers in compiled code (`*.cs`); only stale comments in unrelated `SpaarkeAi/src/components/conversation/*.ts` files (logged as MINOR for R7).

---

## Findings

### Severity counts

| Severity | Total | Fixed in-line | Logged for R7 |
|---|---|---|---|
| **CRITICAL** | 0 | 0 | 0 |
| **MAJOR**    | 3 | 1 (cache key) | 2 |
| **MINOR**    | 6 | 1 (dead conditional) | 5 |

### Critical fixes applied (in-line)

**None** — no defects rose to CRITICAL because:
- The tenant-leak bug found in `OrchestratorPromptBuilder` (MAJOR) is **latent** — `ISprkAgent` is registered (Phase-2 scaffold) but has zero in-tree consumers. Verified via `grep ISprkAgent src/`. Will activate when the agent is wired in Phase 3, so I patched defensively now to prevent a future regression.

### MAJOR findings

| # | File:Line | Description | Disposition |
|---|---|---|---|
| M1 | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/OrchestratorPromptBuilder.cs:148–162` (before fix) | Singleton `MemoryCache` keyed on `ActivePlaybookName ?? "_default_"`. Cached prefix string embeds `OrchestratorPromptContext.TenantId` via `AppendStandingInstructions`. First tenant's `TenantId` persists in cached prompt and would leak into every other tenant's system prompt. Latent (no `ISprkAgent` consumer yet), but a Phase-3 wire-up would activate it silently. | **FIXED in-line** — cache key now `${tenantKey}|${playbookKey}`. Build passes; 34 reranker + prompt-builder tests pass. |
| M2 | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs:85` | `static readonly SemaphoreSlim AiConcurrencyLimiter = new(10, 10)` is process-wide across ALL dispatcher instances. With 2-second total timeout and burst load this could become a thundering herd. Per-instance is fine for single-tenant deploy; multi-tenant needs tenant-scoped permit budget. | **R7 backlog (DEFER)** — works for current single-tenant model. |
| M3 | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/DirectOpenAiAgent.cs:235–240` | `BuildPromptContext` hard-codes `MatterName: null, ActivePlaybookName: null`. Two enrichment paths in `OrchestratorPromptBuilder` (`AppendEntityEnrichment`, persona personalisation) are unreachable. Either prune the dead paths or thread the fields through `AgentRequest`. | **R7 backlog (DEFER)** — not a defect, but design rot. |

### MINOR findings

| # | File:Line | Description | Disposition |
|---|---|---|---|
| m1 | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/IntentRerankerService.cs:172–174` (before fix) | Conditional `timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested ? "timeout-graceful-degrade" : "timeout-graceful-degrade"` — both arms identical string. | **FIXED in-line** — collapsed to `const string reason = "timeout-graceful-degrade"`. |
| m2 | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs:784` | User message verbatim-embedded in LLM prompt without escaping `"` quotes. Could trip JSON output parsing on edge cases. | R7 backlog |
| m3 | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/IntentRerankerService.cs:335` | Same as m2. JSON-schema response mitigates partially. | R7 backlog |
| m4 | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookCandidateSelector.cs:93–101` | `ContributingFileCount` double-counts if same playbook appears twice in one file's candidates. Latent (upstream Phase B doesn't emit dupes). | R7 backlog |
| m5 | `src/solutions/SpaarkeAi/src/components/conversation/*.ts` | Stale `CapabilityRouter` references in comments after Phase 7 retirement; wire format (`intentHint`) preserved but BFF receiver is now `PlaybookDispatcher`. | R7 backlog |
| m6 | Phase-7 retirement integrity check | Test files retain accurate retirement-history comments (e.g., `SprkChatAgentFactoryTests.cs:143,200`). Intentional — does not need change. | No action |

---

## Critical fixes — commit message line items

For main session's project-closeout commit (task 149/150):

```
- task 147 code-review fix(M1): OrchestratorPromptBuilder cache key now scopes by tenant
  to prevent cross-tenant prompt prefix leak when ISprkAgent is wired in Phase 3.
- task 147 code-review fix(m1): IntentRerankerService timeout reason — collapsed dead
  conditional ternary; both branches were identical string.
```

---

## Exit-0 readiness

**PASS** — no CRITICAL findings remain. UAT (task 146) may proceed.

- All CRITICAL: 0
- All MAJOR: 3 (1 fixed, 2 deferred with owner-acceptable rationale; both are latent / capacity concerns, not active defects in single-tenant deploy)
- All MINOR: 6 (1 fixed, 5 logged for R7)
- Build: `dotnet build src/server/api/Sprk.Bff.Api/` — 0 errors, 17 pre-existing warnings unchanged
- Tests: 34 `OrchestratorPromptBuilder` + `IntentReranker` tests PASS after fix

---

## Honest reflection on triage discipline

- **Tier 1 (10 files) consumed ~70% of effort.** SprkChatAgentFactory (2700 LOC) and PlaybookDispatcher (1067 LOC) carried the bulk; both are well-engineered with extensive XML doc and FR-tag traceability. The MAJOR/MINOR findings concentrated in the smaller files (OrchestratorPromptBuilder, IntentReranker, PlaybookCandidateSelector).
- **Tier 2 (~20% of effort)** was diff-driven for the DI modules + frontend renderer wiring. Found no defects — the FE/BE contracts (`playbook_options` SSE → `IPlaybookOptionsResponse` → link buttons) are clean and traceable.
- **Tier 3 (~10% of effort)** was the orphan grep. Phase 7 retirement is clean at the code level; stale comments in frontend are the only artifact, logged as MINOR.
- **Skipped (deliberately)**: 100+ test-file diffs not deep-reviewed (test code quality has lower payoff in a closeout review; the green test results provide the signal). The summarize-session, playbook-orchestration-streaming, and email-analysis integration tests were sampled but not line-traced.
- **The one bug I'm genuinely glad I caught**: M1 (OrchestratorPromptBuilder tenant-cache leak). It is the kind of latent multi-tenant-isolation hole that survives green test suites because the tests don't exercise concurrent tenants against the same singleton. The fix is 5 LOC; missing it costs Phase-3 a security incident.

---

*Report authored by Phase 7 task 147 quality-gate sub-agent. See `r7-backlog.md` for the appended MINOR items. See task 148 handoff for the parallel adr-check pass.*
