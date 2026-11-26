# Reuse scan: specialist-legal-agents-r1, Phase 0 (design-to-spec Step 3)

> **Date:** 2026-11-25
> **Agent:** one read-only agent (Explore type, model sonnet, breadth medium). It used 99,067 tokens and 13 tool calls. It read `.claude/patterns/CAPABILITY-INDEX.md` first, searched **origin/master** (this branch is about 700 commits behind), and excluded `projects/`, `.claude/worktrees/` and `node_modules`.
> **Judged by:** the main session. The spec's §11 table cites this file.

## Scope of the scan

Phase 0 merges **no code to master**. The only code is dev-only spike tools on a branch that is never merged, like spike-v2's `spike_echo` / `spike_whoami`. The scan answers which existing services those spike tools wrap, so the spike measures real behaviour and Phase 1 can reuse the same seams.

Paths are under `src/server/api/Sprk.Bff.Api/` unless stated otherwise.

## Per capability

| Capability | Searched | Candidates | Verdict |
|---|---|---|---|
| **Caller → per-user profile** (oid → `systemuser` → `sprk_userprofile`) | `StatedProfileReader`, `sprk_userprofile`, `ICallerSystemUserResolver`, `ISystemUserIdentityResolver`, `UserOrgContextReader`, CAPABILITY-INDEX (no row) | `Services/Ai/Context/CallerSystemUserResolver.cs:46` (`ICallerSystemUserResolver.ResolveAsync`; returns Unresolved, never a guess; oid cached 10 min via `Services/Identity/SystemUserIdentityResolver.cs:41`). `Services/Ai/Context/StatedProfileReader.cs:39` (`IStatedProfileReader.ReadAsync`; null on no row or failure). DI `Infrastructure/DI/AnalysisServicesModule.cs:1029`. Entity `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_UserProfile/Entity.xml` | **Extend.** The G3 spike tool resolves the caller with `ICallerSystemUserResolver`. It keys the test profile by `systemuserid` and fails closed when the caller is unresolved |
| **Compiled profile block returned to a model** | `PromptSchemaOverrideMerger`, `ResolvePersonaForChatAsync`, `sprk_aipersona`, `PreferenceDirectiveProducer`, `userFragment`, `IContextBinder`, `ContextEnvelope`, `MemoryFactType.Preference`, `MemoryItemStore` | `Services/Ai/Context/ContextBinder.cs:645` (`ResolveUserFragmentAsync`: stated profile, then org context, then preference directives, then memory recall). `PublicContracts/ContextEnvelope.cs:60`. `Services/Ai/Context/PreferenceDirectiveProducer.cs:57` (closed allow-list). `Services/Ai/PromptSchemaOverrideMerger.cs:23` (per node, not per user). `Services/Ai/AnalysisPersonaService.cs:107` (persona per tenant and playbook) | **Spike: static test-profile JSON**, keyed by caller and rendered in the `userFragment` style (data-guarded block). **Phase 1:** the layer compiler with a ratchet is new (design §3.14). `PromptSchemaOverrideMerger` is **rejected** as the compiler, because a lower layer could loosen anything |
| **Server-side review with a profile** (G1 variant c) | `nda-review`, `NDA-REVIEW`, `NdaReviewDispatch` (0 hits), `agreement-review`, `IPlaybookOrchestrationService`, `IActionRunner`, `AnalysisOrchestrationService`, `infra/dataverse/actions` | `infra/dataverse/actions/agreement-review.action.json:2` (**generalises and renames the shipped `nda-review` Action**, agreements-r1 task 002; there is no `nda-review.action.json` on master). `Services/Ai/LinearConsumers/IActionRunner.cs:25` (`RunAsync(AnalysisAction, BoundInputs, LinearRunContext, ct)`, synchronous, structured JSON). `Services/Ai/IPlaybookOrchestrationService.cs:32` (`ExecuteAsync`, streams; canonical run, ADR-013 Invariant 1). `Services/Ai/Chat/AdvisoryCapabilityRunner.cs:23`. `tests/eval/fixtures/sample-nda-review-output.json` | **2 candidates, so it went to the owner (2026-11-25), then to a Fable code-level review (`notes/run-path-review-2026-11-25.md`).** **`IActionRunner` for the spike and the action-based model for Phase 1** (a `PublicContracts` facade; `ICodedWorkflow` for multi-step). `IPlaybookOrchestrationService` is **rejected**: it is the frozen node engine (ADR-039 MUST NOT; ADR-013 amended 2026-07-05), with open defects (PB-02, PB-08; #919) and no tier resolver. `sprk_playbookrun` requires a playbook, so the run record for Action runs is an open owner question |
| **MCP spike tool shape** | `spike_echo`, `spike_whoami`, "spike" in `Api/Mcp` and `McpModule` (0 hits on master), `McpToolCatalog`, `ShowDecisionsTool`, `McpEndpoints`, `mcp-tool-handler` skill | `Api/Mcp/McpToolCatalog.cs:34` (`McpToolDefinition`, `[McpArea]`). `Api/Mcp/LegalOperations/Decisions/ShowDecisionsTool.cs:47` (the skill's copy-from template). `Api/Mcp/McpEndpoints.cs:29` (`/mcp/legal-operations`; auth, connector token, rate limit, 256 KiB, kill switch). `Infrastructure/DI/McpModule.cs:56` (stateless) | **Copy** `ShowDecisionsTool` on the spike branch. Every spike tool is read-only (`readOnlyHint`) and model-visible. No spike tools exist on master |
| **Run or conversation key on the MCP path** (G3) | `Mcp-Session-Id`, `x-ms-conversation`, conversation id, correlation, `X-Correlation-Id` | `Infrastructure/DI/McpModule.cs:15` (doc: no session store, no `Mcp-Session-Id`). `Api/Mcp/LegalOperations/Decisions/DecisionToolCall.cs:90` (`TraceIdentifier` only). `Api/Mcp/McpToolResults.cs:22` (error `correlationId`) | **None found.** G3's spike logs the **names** of incoming request headers, with values redacted and `Authorization` never logged, to learn what Copilot sends |

## Plan-level duplication check

- **No new merged surface.** Every spike tool wraps an existing service.
- **One design correction:** design §9 names the `nda-review` Action, but master now has `agreement-review`, which generalises it. The spec uses `agreement-review`.
- **No second profile store.** The spike's test profiles are files on the spike branch. The production profile pair is Phase 1 (D-A18 (1)).

## Not covered by the scan

- How an Action is looked up by code (`IActionResolver`).
- Whether the `agreement-review` Action row is deployed in DEV Dataverse. Only the JSON seed was checked; the spike task checks the row.
- The bodies of `ContextBinder.ResolveUserFragmentAsync`, `ActionRunner` and `PlaybookRunEndpoints.ExecutePlaybook`.
- Tests and client widget code.
