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
