# Task 148 — Final ADR-Check Pass (Phase 7 Wave 7-F)

**Date**: 2026-06-25
**Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1` @ `a730d9092`
**Scope**: Branch diff vs `origin/master` (38 commits ahead)
**Methodology**: `.claude/skills/adr-check/SKILL.md` (applied manually — sub-agents can't invoke Skill tool)
**Rigor**: STANDARD (quality-gate, pre-UAT)

---

## 1. Special-Case Verifications (6 binding checks)

| # | Check | Result | Evidence |
|---|---|---|---|
| **(a)** | ADR-030 v2 `memory` channel — ADR amended + implementation deferred consistently | **PASS** | `.claude/adr/ADR-030-pane-event-bus.md` lines 3, 14, 35, 43, 131–158 carry the v2 amendment. `PaneEventTypes.ts` channel union (line 30) is `'workspace' \| 'context' \| 'conversation' \| 'safety'` (no `'memory'`) because Phase 4 implementation tasks 060–064 were DEFERRED per TASK-INDEX.md line 323 ("`memory` channel (5): 060…064 — defer; ADR-030 v2 amendment already landed"). 0 hits for `workspace.memory` namespacing anti-pattern. Consistent: ADR ahead of code is the intended state. |
| **(b)** | ADR-033 streaming preservation — no edits to `IncrementalJsonParser` / `StreamStructuredCompletionAsync` | **PASS** | `git diff origin/master..HEAD --stat \| grep -iE "(IncrementalJsonParser\|StreamStructuredCompletionAsync)"` returned 0 hits. Both files exist on disk (`Services/Ai/Streaming/IncrementalJsonParser.cs`, `Services/Ai/OpenAiClient.cs` for `StreamStructuredCompletionAsync`) — neither modified by this branch. |
| **(c)** | ADR-029 publish-size discipline — no `<PublishTrimmed>` / `<PublishAot>` | **PASS** | `Sprk.Bff.Api.csproj` grep returned 0 hits for both. Current compressed publish size: **47.13 MB** (task 144 handoff) — well under 60 MB hard ceiling, +1.48 MB vs Phase 5 Outcome A baseline (45.65 MB) attributed across multiple parallel projects in May–June window. |
| **(d)** | ADR-013 facade boundary — no `IOpenAiClient` / `IPlaybookService` injection into CRUD code | **PASS** | All `IPlaybookService` injections are inside `Services/Ai/` (allowed AI-internal). CRUD-side `WorkspaceFileEndpoints.cs` correctly consumes `IConsumerRoutingService` from `Services/Ai/PublicContracts/` namespace (task 028c migration), NOT `IPlaybookService` directly. `IWorkspaceStateService.cs:42` + `PinnedMemoryEndpoints.cs:56` carry explicit "MUST NOT inject" doc-comments enforcing the rule. |
| **(e)** | ADR-015 tier-1 logging safety — no user content / file content / recall results in logs | **PASS** | New telemetry sites audited: `GetWorkspaceTabContentHandler.cs:294,307,329` logs IDs + tab id + section presence flag ONLY (explicit ADR-015 banner comments at lines 294, 305). `PlaybookDispatcher.cs:225,247,283,322` logs `userMessage.Length`, candidate counts, scores, elapsed time — NEVER raw message body. `OrchestratorPromptBuilder.cs:107,122,128,191,199` logs token counts only. `ConsumerRoutingService.cs:116,143` logs identifiers + outcome with explicit "ADR-015: log identifiers + outcome only" comment. ZERO Tier-1 violations found. |
| **(f)** | ADR-032 symmetric DI — no asymmetric-registration anti-pattern | **PASS** | New DI modules (`RoutingModule.cs`, `AiChatModule.cs` 7 unconditional registrations, `ConfigurationModule.cs` typed options) are all UNCONDITIONAL. Pre-existing `if (…Enabled)` blocks in `AnalysisServicesModule.cs:100,121,992,1051,1068`, `AiModule.cs:196,305`, `FinanceModule.cs:36,66,158,181`, `MembershipModule.cs:145,186,243`, `CacheModule.cs:18` are NOT modified by this project and each pairs with a Null peer (P3 pattern per ADR-032). No new asymmetric pattern introduced. |

**6 / 6 special-case checks PASS.**

---

## 2. Per-ADR Compliance Summary

| ADR | Concise rules | Result | Notes |
|---|---|---|---|
| **ADR-001 Minimal API** | New endpoints use Minimal API patterns | **PASS** | New endpoint `WorkspaceFileEndpoints` migration uses static methods + parameter injection (no `IServiceProvider.GetService<T>()` in signatures). Memory `/by-code/`, `/promotions/{id}/{approve\|reject}` endpoints DEFERRED (Phase 4 cut). |
| **ADR-008 Endpoint filters** | Tenant scoping via filters | **PASS** | No NEW endpoints added that lack filters. `WorkspaceFileEndpoints` continues to use existing `AddEndpointFilter<DocumentAuthorizationFilter>` pattern (no removal). |
| **ADR-010 DI minimalism** | No `IServiceProvider.GetService<T>()` in endpoint signatures; concrete-class Null-Object | **PASS** | 0 hits in `Api/*.cs` files (only existing webhook filter uses `Func<IServiceProvider, string?>` — unrelated). New modules well under 15-line per-module cap. `AiChatModule` 7 registrations, `RoutingModule` 2 registrations. |
| **ADR-013 AI facade boundary** | No direct injection of `IOpenAiClient` / `IPlaybookService` into CRUD code | **PASS** | See (d) above. |
| **ADR-014 AI caching** | 5-min TTL on `/by-code/` resolution; tenant-scoped | **PASS-with-MINOR** | `ConsumerRoutingService.cs:58` sets `CacheDuration = TimeSpan.FromMinutes(5)`. Cache key (line 420–428) scopes by env + consumerType + consumerCode + mime + docType — tenant scoping is implicit (single-tenant per env today). Doc comments (lines 37, 47) claim "tenant id" included but explicit tenant id is NOT in the cache key. Per ADR-014 binding "tenant-scoped" intent — this is a MINOR doc-vs-code discrepancy (effectively scoped by env). Backlog. |
| **ADR-015 AI Data Governance (Tier 1)** | NEVER log user content / file content / recall results / memory facts | **PASS** | See (e) above. Every new telemetry site carries an explicit "ADR-015: …" comment. |
| **ADR-018 Typed options** | `WorkspaceOptions.SummarizePlaybookCode` typed | **PASS** | `ConfigurationModule.cs:121–155` adds `PlaybookSelectorOptions` + `IntentRerankerOptions` with `Bind` + `ValidateDataAnnotations` + `ValidateOnStart`. `WorkspaceOptionsValidator` adds deprecation warning for legacy env-var path (FR-1R-06 deprecation window). |
| **ADR-019 ProblemDetails** | 404 ProblemDetails shape on `/by-code/` | **PASS (vacuous)** | `/by-code/` endpoint deferred along with Phase 4. Existing endpoints unmodified return existing ProblemDetails shapes. |
| **ADR-029 BFF Publish Hygiene** | No `<PublishTrimmed>` / `<PublishAot>`; ≤60 MB ceiling | **PASS** | See (c) above. 47.13 MB current. |
| **ADR-030 v2 PaneEventBus** | `memory` channel added; memory events dispatch via `memory.*` NOT `workspace.memory.*` | **PASS** | See (a) above. ADR amended; implementation deferred — no anti-pattern code shipped. |
| **ADR-032 BFF Null-Object Kill-Switch** | Feature-gated services use P1/P2/P3; no asymmetric-registration anti-pattern | **PASS** | See (f) above. `RoutingModule.cs:34–46` comment explicitly states "No Null-Object peer is registered: routing is always-on … graceful-degrade per ADR-032 quiet-no-op semantics applied in-method rather than via a Null peer". Confirmed via `ConsumerRoutingService.cs:138–150` exception path. |
| **ADR-033 Streaming chat-tool side channel** | Path 3 streaming preserved — no edits to `IncrementalJsonParser` / `StreamStructuredCompletionAsync` | **PASS** | See (b) above. |
| **ADR-037 Multi-node Output composition (AUTHORED BY THIS PROJECT)** | Section-name-keyed; preserve FieldDelta backward compat; preserve ADR-033 Path 3 | **PASS** | `.claude/adr/ADR-037-multinode-output-composition.md` + `docs/adr/ADR-037-multinode-output-composition.md` exist; concise version lines 33–48 list MUST/MUST NOT rules. Implementation: `Services/Ai/Nodes/DeliverCompositeNodeExecutor.cs` exists. `Services/Ai/Chat/SseEventTypes/SectionStreamSseEvents.cs` defines three new event types. `PaneEventTypes.ts` lines 241–243 add `'section_started' \| 'section_data' \| 'section_completed'` as additive discriminants on workspace channel (per ADR-030 additive rule). |

**12 / 12 ADRs PASS** (one MINOR doc-vs-code discrepancy on ADR-014; non-blocking).

---

## 3. Findings Table

| Severity | ADR | File:Line | Description | Recommendation |
|---|---|---|---|---|
| **MINOR** | ADR-014 | `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerRoutingService.cs:37,47,420–428` | XML doc comments claim cache key "includes tenant id" but `BuildCacheKey` does not include explicit tenant id. Effective tenant scoping today is via single-tenant-per-env deployment; correct for current deployment model but documentation overstates explicit field-level scoping. | Update doc comment to read "scoped by environment (single-tenant per env)" OR add explicit `tenantId` parameter to `BuildCacheKey` when multi-tenant per env is supported. Defer to R7 — no behavior change today. |

**No CRITICAL violations. No MAJOR violations. 1 MINOR.**

---

## 4. Critical Fixes Applied

None. No critical violations found.

---

## 5. Exit-0 Readiness Statement

**PASS — Project surface complies with all 12 applicable ADRs.**

- All 6 special-case binding verifications PASS.
- 12 / 12 ADRs PASS (1 MINOR doc-discrepancy on ADR-014 — non-blocking).
- ADR-015 Tier-1 logging audit: ZERO user content / file content / recall result exposures across all new telemetry sites.
- ADR-029 publish size: 47.13 MB (well under 60 MB ceiling).
- ADR-030 v2 amendment correctly recorded in `.claude/adr/`; Phase 4 implementation deferred consistently.
- ADR-033 streaming files untouched (binary diff confirms).
- ADR-037 authored + implementation consistent.
- 0 CRITICAL violations remain unfixed.

**Phase 7 Wave 7-G (UAT) is unblocked from an ADR-compliance standpoint.**

---

## 6. Methodology Notes

- Sub-agent could NOT invoke the `adr-check` Skill tool (sub-agent boundary). Methodology applied manually per `.claude/skills/adr-check/SKILL.md`:
  1. Read concise ADR files for each applicable ADR.
  2. Identified MUST / MUST NOT rules.
  3. Greped project surface for violations.
  4. Categorized findings.
- Branch diff scope: 38 commits, ~190 changed files (~70 in BFF, ~30 in SpaarkeAi/Spaarke.AI.Widgets, plus docs/ADRs/configs).
- Time spent: ~1 hour (well under 2h estimate).
