# D-09 — NullObject + Promote-to-Unconditional Designs (RB-T028 Cluster)

> **Project**: `sdap.bff.api-test-suite-repair-r2`
> **Task**: 011 — Phase 1a output (NullObject design phase)
> **Decision date**: 2026-06-01
> **Authored by**: task-execute (Claude Code, Opus 4.7) under STANDARD rigor
> **Cross-references**:
> - Escalation [`E-01`](../escalations/E-01-rb-t028-cluster-scope-expansion.md) (root scope)
> - Inventory [`asymmetric-registration-inventory-2026-06-01.md`](../baseline/asymmetric-registration-inventory-2026-06-01.md) (the 13 services this design covers)
> - r1 ledger entries [RB-T028-03/04/05/06](../../sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md) (the 4 closed by this design)
> - ADR-010 (DI minimalism), ADR-018 (kill switches), ADR-007 (facade pattern)

---

## 1. Design Principles

Per E-01's Option B resolution, all 13 in-scope services from the inventory (8 BLOCKING + 5 LATENT) get a kill-switch-tolerant treatment. Per ADR-010 (DI minimalism) the design AVOIDS adding new interfaces gratuitously: a Null-Object is registered with the SAME interface/concrete-class the real impl uses. Per ADR-018 (kill switches), Null-Object behavior MUST be:

- **Predictable**: same kill-switch return shape per consumer pattern (503 ProblemDetails, empty result, or no-op).
- **Fail-clean**: NO partial behavior. Either no-op, or fail-fast with `FeatureDisabledException` → endpoint converts to 503.
- **Authorization-preserving**: ADR-018 forbids kill switches bypassing auth — Null-Objects MUST NOT short-circuit auth filters; endpoints still get rate-limit + auth filter, only handler returns 503 / empty.

The design distinguishes **3 patterns**:

| Pattern | When to use | Behavior |
|---|---|---|
| **P1: Promote-to-unconditional** | Service has zero AI deps; current conditionality is misregistration | Move the registration OUT of the `if`-block. Real behavior in both states. |
| **P2: Quiet Null-Object** | Service is a side-effecting operation; absence is silently equivalent to a no-op | Methods return `Task.CompletedTask`, default-constructed result, or empty enumerable. No exception. |
| **P3: Fail-fast Null-Object** | Service is a query/computation; absence means caller is using a feature that doesn't exist; silent empty would mislead | Methods throw `FeatureDisabledException("ai.<feature>.disabled")`. Endpoint converts to 503 ProblemDetails per ADR-018. |

The choice per service is captured below with rationale.

---

## 2. Per-Service Designs (8 BLOCKING + 5 LATENT)

### B1 — `NotificationService` → **P1 Promote-to-unconditional**

- **Reg site**: `AnalysisServicesModule.cs:108` (inside `AddPlaybookServices`, gated on compound)
- **Action**: Move the `services.AddSingleton<NotificationService>()` line out of `AddPlaybookServices` and into the top of `AddAnalysisServicesModule` (or extract a small new `AddNotificationServices(services)` helper called UNCONDITIONALLY at the top).
- **Rationale**: `NotificationService` has zero AI deps (`IGenericEntityService` + `ILogger`). The current conditional registration is a misclassification — it was placed in `AddPlaybookServices` because it's USED by playbooks, but its DEPENDENCIES are CRUD-only. ADR-010 explicitly favors unconditional registration of services with no feature-gated deps.
- **Effort**: ~5 minutes; 2-line move.
- **ADR-018 stance**: This is NOT a real kill switch for `NotificationService` — notifications are a CRUD feature that's always present. The current conditional is technical debt, not policy.

### B2 — `SprkChatAgentFactory` → **P3 Fail-fast Null-Object**

- **Reg site**: `AiModule.cs:217` (inside the AiModule, which is itself only called inside the compound block)
- **Action**: Create `Services/Ai/Chat/NullSprkChatAgentFactory.cs` that derives from / replaces `SprkChatAgentFactory` (concrete class, no current interface — keep concrete-class registration per ADR-010). Register the Null-Object in a new `else` branch of the compound-gate `if`-block:
  ```csharp
  if (analysisEnabled && documentIntelligenceEnabled) { /* existing AddAiModule */ }
  else { services.AddSingleton<SprkChatAgentFactory>(sp => new NullSprkChatAgentFactory(sp.GetRequiredService<ILogger<SprkChatAgentFactory>>())); }
  ```
- **Behavior**: All public methods (`CreateAgentAsync`, etc.) throw `new FeatureDisabledException("ai.chat.disabled", "AI chat requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.")`. `ChatEndpoints.SendMessageAsync` already has a guard for SSE response, but the call path runs `agentFactory.CreateAgentAsync(...)` later — that throw becomes a 500 (uncaught). To convert to 503 cleanly, ChatEndpoints needs a `try { } catch (FeatureDisabledException ex) { return Problem(503, ...); }` wrapper. (One-line per handler, ~3 handlers in ChatEndpoints.)
- **Rationale**: Chat without AI is meaningless; silent empty SSE stream would mislead the UI. Fail-fast.
- **Why concrete-class Null-Object (not interface)**: `SprkChatAgentFactory` is a concrete class (no interface, per ADR-010). The Null-Object must be a subclass that overrides the public methods. The class is currently `sealed` — Phase 1b must `unseal` it (`public class SprkChatAgentFactory` → also non-sealed) OR extract a small interface `ISprkChatAgentFactory`. **Decision: unseal** (smaller blast radius, ADR-010 favors no-new-interfaces).
- **Effort**: 1-2 hours including unseal + Null subclass + endpoint try/catch.

### B3 — `PendingPlanManager` → **P3 Fail-fast Null-Object**

- **Reg site**: `AiModule.cs:274`
- **Action**: Same pattern as B2 — unseal `PendingPlanManager`, create `NullPendingPlanManager` subclass, register in `else` branch.
- **Behavior**: `StoreAsync` / `GetAsync` / `DeleteAsync` throw `FeatureDisabledException("ai.chat.compound-intent.disabled", ...)`.
- **Rationale**: Pending plans gate compound-intent multi-tool chains — silently returning null would let chat believe no plan is pending and execute single-tool defaults, masking the disabled state. Fail-fast at the endpoint layer.
- **Effort**: ~30 min including unseal + Null subclass.

### B4 — `ChatSessionManager` → **P1 Promote-to-unconditional**

- **Reg site**: `AiModule.cs:238`
- **Action**: Move the factory registration out of the conditional AiModule call. Specifically:
  - Promote `IChatDataverseRepository` + `ChatDataverseRepository` (lines 230-231 of AiModule) to unconditional — they only depend on `IDataverseService` (unconditional) per the constructor.
  - Promote `ChatSessionManager` (lines 238-242) to unconditional — its deps (`IDistributedCache` unconditional, `IChatDataverseRepository` now unconditional, optional `ISessionPersistenceService`) become all-unconditional.
- **Rationale**: `ChatSessionManager` is a Dataverse-CRUD wrapper that manages session metadata in Redis + Dataverse. It has NO AI deps in its constructor. Moving it unconditional means even with AI off, you can list/delete sessions (CRUD operations) — the AI-specific operations (creating an agent for the session) go through `SprkChatAgentFactory` (B2 above, which fail-fasts). Clean separation.
- **ADR-010 stance**: Adds 3 registrations to the unconditional path (the chat-CRUD bundle). Aligns with the "feature module extensions" principle — chat-CRUD is a module, AI-chat-agent is a module.
- **Effort**: ~30 min (move 3 lines; verify tests).

### B5 — `ChatHistoryManager` → **P1 Promote-to-unconditional**

- Same pattern as B4. `ChatHistoryManager` (AiModule.cs:247) depends on `ChatSessionManager` + `IChatDataverseRepository` (both now unconditional) + optional `IChatClient` (for summarization — null-tolerant). Promote.
- **Effort**: ~10 min (1-line move).

### B6 — `IPlaybookService` → **P3 Fail-fast Null-Object**

- **Reg site**: `AnalysisServicesModule.cs:103` (in `AddPlaybookServices`, behind compound gate)
- **Action**: Create `Services/Ai/NullPlaybookService.cs` implementing `IPlaybookService`. Register in `else` branch.
- **Behavior**: All methods throw `FeatureDisabledException("ai.playbook.disabled", ...)`.
- **Why fail-fast (not quiet)**: `IPlaybookService` returns playbook records — if it returned an empty list, the chat agent would silently report "no playbooks available" instead of communicating that the AI feature is off. Fail-fast surfaces the actual config state.
- **Why not Promote**: `IPlaybookService` is a typed HttpClient that targets a configured AI backend URL. With AI off, that URL isn't configured. Promote-to-unconditional would require the HttpClient base URL to be optional — adds complexity. Null-Object is cleaner.
- **Effort**: ~30 min.

### B7 — `IRagService` → **P3 Fail-fast Null-Object**

- **Reg site**: `AnalysisServicesModule.cs:270` (in `AddRagServices`, gated on compound + non-empty AI Search keys)
- **Action**: Create `Services/Ai/NullRagService.cs` implementing `IRagService`. Register in `else` branch.
- **Behavior**: All retrieval methods throw `FeatureDisabledException("ai.rag.disabled", ...)`. `KnowledgeBaseEndpoints` + `RagEndpoints` catch and return 503.
- **Why fail-fast**: Empty search result would mislead consumers into believing the knowledge base is empty. Fail-fast clarifies the kill-switch state.
- **Effort**: ~1 hour (interface has multiple methods).

### B8 — `SearchIndexClient` (Azure SDK) → **Refactor + Null-Object via `IRagService`**

- **Reg site**: `AnalysisServicesModule.cs:261` (Azure SDK type, not ours)
- **Action**: Refactor `KnowledgeBaseEndpoints` to STOP injecting `SearchIndexClient` directly. Instead, route the calls through `IRagService` (which becomes a Null-Object per B7 above). Specifically:
  - `KnowledgeBaseEndpoints.GetIndexHealth` (line 115) — currently calls `searchIndexClient.GetIndexAsync(...)`. Move that call inside `IRagService.GetIndexHealthAsync()`.
  - `KnowledgeBaseEndpoints.GetIndexedDocuments` (line 179) — move into `IRagService.GetIndexedDocumentsAsync()`.
  - `KnowledgeBaseEndpoints.DeleteIndexedDocument` (line 284) — move into `IRagService.DeleteDocumentAsync()`.
- **Rationale**: Per ADR-007 (facade pattern), endpoints SHOULD consume domain services, not Azure SDK clients directly. The fact that they currently do is incidental debt that the Null-Object work is a good moment to clean up. Once endpoints depend only on `IRagService`, the Null-Object solves both B7 and B8.
- **Alternative (rejected)**: Register a "null" `SearchIndexClient` pointing to a dummy endpoint. Rejected because Azure SDK clients are not easy to neutralize without weird URI tricks.
- **Effort**: ~2 hours (refactor 3 handler methods + add 3 methods to `IRagService`).

### L1 — `IBriefingAi` → **P3 Fail-fast Null-Object**

- **Reg site**: `AnalysisServicesModule.cs:137` (in `AddPublicContractsFacade`, behind compound gate)
- **Action**: Create `Services/Ai/PublicContracts/NullBriefingAi.cs` implementing `IBriefingAi`. Register in `else` branch.
- **Behavior**: `GenerateNarrativeAsync` throws `FeatureDisabledException("ai.briefing.disabled", ...)`. Consumers (`WorkspaceMatterEndpoints.HandleAiSummary`, `DailyBriefingEndpoints`) catch and return 503 SSE / 503 ProblemDetails.
- **Important**: The current `WorkspaceMatterEndpoints.HandleAiSummary` already has the `if (briefingAi is null) return 503` pattern — we keep that as a NULL CHECK, but now `briefingAi` is NEVER null because Null-Object is always registered. Replace the null-check with a `FeatureDisabledException` catch.
- **Effort**: ~30 min.

### L2 — `IInvoiceSearchService` → **P2 Quiet Null-Object** OR **P3 Fail-fast**

- **Reg site**: `FinanceModule.cs:68`
- **Action**: Create `Services/Finance/NullInvoiceSearchService.cs` implementing `IInvoiceSearchService`. Register in `else` branch.
- **Pattern choice**: **P3 Fail-fast** — `SearchInvoices` returning empty `InvoiceSearchResponse` would silently render "no invoices match" in the Finance UI, masking the kill-switch state. Throw `FeatureDisabledException("ai.finance.search.disabled", ...)`. `FinanceEndpoints.SearchInvoices` catches → 503.
- **Effort**: ~20 min.

### L3 — `IPlaybookOrchestrationService` → **P3 Fail-fast Null-Object**

- **Reg site**: `AnalysisServicesModule.cs:106`
- **Action**: Create `Services/Ai/NullPlaybookOrchestrationService.cs` implementing the interface. Register in `else` branch.
- **Pattern**: P3 — same rationale as B6 and L1.
- **Effort**: ~45 min (interface has several methods).

### L4 — `ITextExtractor` → **P3 Fail-fast Null-Object**

- **Reg site**: `AnalysisServicesModule.cs:27` (gated on `DocumentIntelligence:Enabled` only — NOT compound)
- **Action**: Create `Services/Ai/NullTextExtractor.cs` implementing `ITextExtractor`. Register in the `else` branch of the `documentIntelligenceEnabled` outer block (lines 21-33).
- **Pattern**: P3 — silently returning empty extracted text would mislead WorkspaceFileEndpoints into "uploading 0-char document" path. Throw `FeatureDisabledException("ai.text-extraction.disabled", ...)`.
- **Effort**: ~30 min.

### L5 — `StandaloneChatContextProvider` + `AnalysisChatContextResolver` → **P1 Promote-to-unconditional**

- **Reg sites**: `AiModule.cs:266, 261`
- **Action**: Move both registrations out of the conditional AiModule. Both have only `IGenericEntityService` (unconditional) + `IDistributedCache` (unconditional) as deps.
- **Rationale**: These resolve chat-context CONFIGURATION (which playbooks are available for which entity/page). They are Dataverse-CRUD readers; their results may LIST AI playbook IDs but they DON'T invoke AI. Safe to be unconditional. When AI is off, they return an empty list (because no playbooks are configured) — that's the correct semantic.
- **Effort**: ~15 min for both (move 2 lines).

---

## 3. New Type: `FeatureDisabledException`

Per ADR-018 ("disabled features return 503 ProblemDetails"), all P3 Null-Object impls throw a common exception type. The exception carries an `errorCode` (e.g., `"ai.chat.disabled"`) and a default `detail` message.

**New file**: `src/server/api/Sprk.Bff.Api/Configuration/FeatureDisabledException.cs`

```csharp
namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Thrown by Null-Object service implementations when a feature is disabled
/// via a kill-switch flag (Analysis:Enabled, DocumentIntelligence:Enabled).
/// Endpoints catch this and convert to 503 ProblemDetails per ADR-018.
/// </summary>
public sealed class FeatureDisabledException : InvalidOperationException
{
    public string ErrorCode { get; }

    public FeatureDisabledException(string errorCode, string detail)
        : base(detail)
    {
        ErrorCode = errorCode ?? throw new ArgumentNullException(nameof(errorCode));
    }
}
```

**New helper** (optional but recommended): a single extension on `IResult` / `Results` that converts a caught `FeatureDisabledException` to a 503 ProblemDetails — DRY across all endpoint catch sites.

---

## 4. Implementation Order (Phase 1b)

Ordered by RISK and DEPENDENCY. Lower tiers first.

### Tier 1 — Promote-to-unconditional (LOW RISK, ~1.5h)

These are pure DI moves. No new files. No interface changes. Highest leverage.

1. **B1**: Move `NotificationService` registration to unconditional. (5 min)
2. **B4**: Move `ChatDataverseRepository` + `ChatSessionManager` to unconditional. (20 min)
3. **B5**: Move `ChatHistoryManager` to unconditional. (10 min)
4. **L5**: Move `StandaloneChatContextProvider` + `AnalysisChatContextResolver` to unconditional. (15 min)

After Tier 1: Re-run build. Expect ~13 of 36 Skipped tests to flip Pass (the Auth tests + the KB tests that hit only the NotificationService param-inference path).

### Tier 2 — Fail-fast Null-Object for facade services (MEDIUM RISK, ~5h)

Add `FeatureDisabledException` type and use it across:

5. **Add `FeatureDisabledException` + endpoint-extension helper**. (~30 min)
6. **L1**: `NullBriefingAi`. (~30 min)
7. **L2**: `NullInvoiceSearchService` + FinanceEndpoints catch. (~30 min)
8. **L3**: `NullPlaybookOrchestrationService`. (~45 min)
9. **L4**: `NullTextExtractor`. (~30 min)
10. **B6**: `NullPlaybookService` + ChatEndpoints catch. (~30 min)
11. **B7**: `NullRagService` + KnowledgeBase/Rag endpoints catch. (~1h)

After Tier 2: Re-run build. Expect ~15-20 more tests to flip Pass.

### Tier 3 — Null-Object for concrete (sealed) classes + endpoint refactor (HIGHER RISK, ~5h)

12. **B2**: Unseal `SprkChatAgentFactory` + `NullSprkChatAgentFactory` + ChatEndpoints catch. (~2h)
13. **B3**: Unseal `PendingPlanManager` + `NullPendingPlanManager` + ChatEndpoints catch. (~30 min)
14. **B8**: Refactor `KnowledgeBaseEndpoints` to use `IRagService` instead of direct `SearchIndexClient`. (~2h)

After Tier 3: Re-run build. Expect remaining ~6-10 tests to flip Pass. Total target: 36 Skip→Pass.

### Total Phase 1b effort estimate

| Tier | Items | Hours |
|---|---:|---:|
| Tier 1 | 4 | 1.5 |
| Tier 2 | 7 | 5 |
| Tier 3 | 3 | 5 |
| Build / per-fix triple-run between tiers | — | 1.5 |
| Step 9.5 (code-review + adr-check) | — | 1 |
| Buffer (handler catch-block adjustments, surprises) | — | 1 |
| **Total** | **14** | **~15h** |

This is at the upper end of E-01's 10-15h estimate (because of Tier 3's refactor and the per-tier triple-run discipline) but still WITHIN the original 17-25h Phase 1b allowance from E-01.

---

## 5. Expected Test Outcomes

The Phase 1b production work flips at least 36 Skip→Pass:

| Test file | Skipped count | Flips after |
|---|---:|---|
| `Api/Ai/KnowledgeBaseEndpointsTests.cs` | 13 | B1 (Tier 1), B7+B8 (Tier 2+3) |
| `Api/Ai/ChatEndpointsTests.cs` | 11 | B4+B5 (Tier 1), B6 (Tier 2), B2+B3 (Tier 3) |
| `Api/Ai/ReAnalysisFlowTests.cs` | 8 | same as Chat (re-analysis routes through chat) |
| `AuthorizationIntegrationTests.cs` | 4 | unblocks after ANY Tier 1 success (startup metadata-gen abort root cause) |
| **Total target** | **36** | |

Note: r1 ledger estimated 37; the actual integration test count of currently-Skipped RB-T028-tagged tests is 36 (13+11+8+4). The off-by-one comes from how the cluster was originally framed (Auth as 5 vs actual 4). Phase 1c will verify the exact count.

---

## 6. ADR Compliance Sign-Off

| ADR | Compliance |
|---|---|
| ADR-001 (Minimal API) | ✓ Endpoint signatures unchanged; mapping pattern unchanged |
| ADR-007 (SpeFileStore facade pattern) | ✓ B8 refactor aligns with this — endpoints depend on facade not Azure SDK |
| ADR-008 (Endpoint filters) | ✓ Auth filters fire on all endpoints regardless of Null-Object — handler-level 503 only |
| ADR-010 (DI minimalism) | ✓ Null-Object adds ~7 conditional `else`-branch registrations (matches gate count of real services). Net +0 unconditional registrations beyond Tier 1 promotions. NOT introducing new interfaces gratuitously. |
| ADR-013 (AI extends BFF / facade pattern) | ✓ Null-Objects respect the PublicContracts facade boundary |
| ADR-018 (kill switches) | ✓ THIS IS THE CANONICAL implementation of the kill-switch policy. Amendment (D-10 / ADR-030 draft) codifies the Null-Object pattern explicitly. |
| ADR-019 (ProblemDetails) | ✓ All 503 returns use `Results.Problem(...)` with `errorCode` extension |
| ADR-028 (Spaarke Auth v2) | ✓ Auth unaffected; collateral RB-T028-06 resolves passively |
| ADR-029 (BFF publish hygiene) | ✓ Net code change ~1500 LOC (well within publish-size budget) |

---

## 7. Test fixture stance

**Per NFR-01**, the 4 fixture files (`KnowledgeBaseEndpointsTests.cs`, `ChatEndpointsTests.cs`, `ReAnalysisFlowTests.cs`, `AuthorizationIntegrationTests.cs`) are NOT modified. They continue to set `DocumentIntelligence:Enabled=false` and `Analysis:Enabled=false`. After Phase 1b:
- The BFF starts cleanly under those flags (Null-Objects everywhere)
- The 36 tests' assertions either get 200/204/202 (CRUD-only endpoints) OR get 503 (AI endpoints that should be killed) — the tests must already expect this OR be flipping Skip→Pass with the existing assertion model

Phase 1c will execute the per-test Skip→Pass transition (remove `Skip="..."` attribute, change `[Trait("status","real-bug-pending-fix")]` → `[Trait("status","repaired")]`, verify assertion passes against the now-running BFF).

---

## 8. Risks (Phase 1b)

| Risk | Likelihood | Mitigation |
|---|---|---|
| Sealed-class unseal (B2/B3) breaks downstream type checks | LOW | None known — sealed is recent (ADR-010 prefers concrete singletons, didn't require sealed). Verify build. |
| `IPlaybookService` typed HttpClient — Null-Object as HttpClient is awkward | LOW | Decision: Null-Object as plain class implementing `IPlaybookService`, NOT registered via `AddHttpClient`. Just plain `services.AddSingleton<IPlaybookService>(new NullPlaybookService())`. |
| Endpoint `try/catch (FeatureDisabledException)` proliferation | MEDIUM | DRY via a single `IResult` extension method `.AsFeatureDisabled503()` to convert. ~1 line per catch block. |
| Test assertions expect 200 but get 503 | MEDIUM | Per-test verification in Tier-by-Tier triple-run; surface any mismatch as ledger entry (not absorbed into Phase 1b). |
| B8 endpoint refactor introduces a regression in production-mode | MEDIUM | Step 9.5 code-review must verify the `IRagService` impl methods that take over from direct `SearchIndexClient` calls have identical behavior. |

---

## 9. Decision Authority

This design is the work product of Phase 1a of task 011. Phase 1b implementation begins after this decision record is committed. No further owner approval needed for Phase 1b execution — Option B was already approved 2026-06-01 per E-01.

---

*Authored 2026-06-01 by task-execute under STANDARD rigor. Production code unchanged in this phase. Phase 1b implementation begins on commit of this design.*
