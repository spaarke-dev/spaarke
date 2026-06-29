# R7 Backlog — chat-routing-redesign-r1 Carry-Over Items

> **Purpose**: Non-blocking findings from Phase 7 quality gates (tasks 147 + 148) and project execution. Items here are intentionally deferred to a successor project — they do not block the current project's exit-0.

---

## R7 backlog from task 148 (adr-check)

- [ ] (MINOR) ADR-014 `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerRoutingService.cs:37,47,420–428`: XML doc comments claim cache key includes "tenant id" but `BuildCacheKey` does not include explicit tenant id. Effective tenant scoping today is via single-tenant-per-env deployment. Either (a) update doc comment to "scoped by environment (single-tenant per env)" OR (b) add explicit `tenantId` parameter to `BuildCacheKey` when multi-tenant per env is supported.

## R7 backlog from task 147 (code-review)

- [ ] (MINOR) `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs:784`: `userPrompt = $"User message: \"{userMessage}\"..."` embeds user message verbatim in LLM prompt without escaping internal `"` quotes. With JSON-output-shaped LLM responses this can occasionally trip the LLM into unbalanced quoting. Defensive escape (e.g., `userMessage.Replace("\"", "\\\"")`) would harden the path.
- [ ] (MINOR) `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/IntentRerankerService.cs:335`: same verbatim user-message embedding pattern as above. Reranker uses JSON-schema response so partial mitigation, but defensive escape would still be cleaner.
- [ ] (MINOR) `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookCandidateSelector.cs:93–101`: `ContributingFileCount` increments by 1 per occurrence of the playbook in `fileResult.Candidates`, but does not guard against a single file emitting the same playbook twice. Upstream Phase B produces unique per-file hits in practice, so this is latent only; a `HashSet<fileIndex>` per playbook would be exact.
- [ ] (MINOR) `src/solutions/SpaarkeAi/src/components/conversation/{CommandRouter.ts, ConversationPane.tsx, SoftSlashRouter.ts, HardSlashExecutor.ts}` (+ tests): comments still reference the retired `CapabilityRouter` C# type. The slash-routing wire format is preserved (`intentHint`), but the BFF receiver is now `PlaybookDispatcher` Phase B with intent-bias. Comments should be refreshed to point at `PlaybookDispatcher.RunPhaseBManifestAbsentAsync` / `intentHint` query prefix.
- [ ] (MAJOR / DEFER) `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs:85`: `static readonly SemaphoreSlim AiConcurrencyLimiter = new(10, 10)` is process-wide. Per-instance dispatchers all share 10 permits. Under burst load this could become a thundering-herd bottleneck. Capacity-plan / load-test; consider tenant-scoped semaphore or hosted-service-managed permit budget.
- [ ] (MAJOR / DEFER) `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/DirectOpenAiAgent.cs:235–240` + `OrchestratorPromptContext`: `MatterName` and `ActivePlaybookName` are always `null` from `BuildPromptContext`. The matter-isolation enrichment and per-playbook personalisation paths in `OrchestratorPromptBuilder` are never exercised. Either prune the dead paths or wire `AgentRequest` to carry these values.

---

## R7 backlog from R6 handoff (architectural debt — chat ↔ workspace write-side unification)

**Source**: [`notes/chat-workspace-write-side-unification-r6-handoff.md`](chat-workspace-write-side-unification-r6-handoff.md) (authored 2026-06-25 by R6 surface-completion diagnostic)

**Verified against source** (2026-06-25):
- ✅ `UpdateWorkspaceTabHandler.cs:91-95` + `SendWorkspaceArtifactHandler.cs:80-83` cite ADR-030 as justification for not emitting SSE — language matches handoff quote
- ✅ `PlaybookOutputHandler.cs:581+` `EmitWorkspaceTabOpenAndStreamAsync` does emit `workspace.tab_open` SSE — divergence is real
- ✅ ADR-030 v2 §"Event-type discriminants" explicitly permits additive event types on existing channels — the chat-handler "no SSE" pattern is implementation choice, not ADR constraint

### The architectural debt

Two parallel code paths mutate workspace tabs via the same `IWorkspaceStateService` but diverge in frontend notification:

- **Path A (LLM-initiated chat tool)**: `SendWorkspaceArtifactHandler` / `UpdateWorkspaceTabHandler` / `CloseWorkspaceTabHandler` / `GetWorkspaceTabContentHandler` → write state, NO SSE. Frontend reconciles via poll (~500ms–2s perceived latency).
- **Path B (playbook output destination=workspace)**: `PlaybookOutputHandler.HandleWorkspaceOutputAsync` → write state + emit `workspace.tab_open` SSE. Frontend mounts widget immediately.

### Proposed shape (suggestive, not prescriptive)

Shared `IWorkspaceMutationPipeline` service. Chat handlers + `PlaybookOutputHandler` both become thin wrappers. `ChatInvocationContext` gains a nullable `WorkspaceSseWriter` delegate (same pattern as ADR-033 streaming side-channel). Event payload extends `workspace` channel via additive event types (ADR-030-compliant).

### Effort + risk

- **Effort**: ~2-3 working days (4 hr investigation + 1-2 days implementation + tests)
- **Risk**: LOW backward-compat (additive ADR-030 events, existing 32 handler tests provide regression coverage), LOW performance (one indirection layer), MODERATE test surface (pipeline tests + handler refactor + ChatInvocationContext extension)

### Scope decision (2026-06-25): **R7-backlog** with conditional escalation gate

**Why R7 not WP3-ext or WP7 within R1**:
1. **§11 Component Justification (binding)**: Handoff itself classifies as "Not blocking — polish gap that erodes perception." Cannot articulate a concrete contract/behavior failure per §11 question 3.
2. **Project closeout discipline**: R1 is at task 146 + 150 with all phases complete. Adding a 2-3 day WP post-spec-closure violates spec discipline (no FR covers chat-handler SSE emission).
3. **No coordination pressure**: R6 committed to NOT touch the 4 handler source files during sprint or closeout — no "use it or lose it" timing window.

**Conditional escalation gate**: IF task 146 UAT regression surfaces chat-path tab-mount latency as a P0/P1 user-perceptible failure (graduation blocker), THEN escalate to WP3 extension within R1 before task 150 wrap-up. **Task 146 UAT criteria MUST include**: "verify chat-handler tab-mount latency does not exceed user-perceptible threshold (target: ≤200ms from tool-call completion to tab visible in workspace pane)."

**R7 dependency analysis** (does NOT block on):
- R1 task 146 UAT completion (independent test surface)
- AI Search restoration (orthogonal — workspace state writes don't touch retrieval)
- Redis flip-on (orthogonal — `IWorkspaceStateService` uses in-memory fallback per CacheModule)

**R7 MUST wait for**:
- R6 closeout (commitment: no handler source touches during sprint/closeout)
- R1 task 150 project wrap-up (formal scope closure)

### Source files touched by this work (R7 scope)

- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/SendWorkspaceArtifactHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/UpdateWorkspaceTabHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/CloseWorkspaceTabHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GetWorkspaceTabContentHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookOutputHandler.cs:581+` (`HandleWorkspaceOutputAsync`)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatInvocationContext.cs` (add `WorkspaceSseWriter` nullable delegate)
- New: `IWorkspaceMutationPipeline` + `WorkspaceMutationPipeline` (greenfield)
- `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` (additive event types on `workspace` channel)

### References

- Handoff doc: [`notes/chat-workspace-write-side-unification-r6-handoff.md`](chat-workspace-write-side-unification-r6-handoff.md)
- ADR-030 v2 amendment: [`.claude/adr/ADR-030-pane-event-bus.md`](../../../.claude/adr/ADR-030-pane-event-bus.md)
- R6 origin doc: `projects/spaarke-ai-platform-unification-r6/notes/tier-c-diagnostic.md`
- Read-side precedent: Phase 5R task 118b (`GetWorkspaceTabContentHandler`)
