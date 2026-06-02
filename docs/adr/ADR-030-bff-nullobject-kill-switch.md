# ADR-030: BFF Null-Object Kill-Switch Pattern

> **Status**: Accepted
> **Domain**: BFF API Architecture / DI Composition
> **Date Accepted**: 2026-06-01
> **Last Updated**: 2026-06-01
> **Concise version**: [`.claude/adr/ADR-030-bff-nullobject-kill-switch.md`](../../.claude/adr/ADR-030-bff-nullobject-kill-switch.md)
> **Cross-references**:
> - Supersedes parts of [ADR-018](.../../.claude/adr/ADR-018-feature-flags.md) (kill switches) — extends with composition-root binding requirement
> - Reinforces [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) (DI minimalism) — Null-Objects do NOT add new interfaces
> - Codifies the workflow rule from CLAUDE.md §10 bullet 6 ("Endpoints that map unconditionally must have unconditional service registration") — explains HOW to satisfy that rule when a service must remain feature-gated
> - Resolves r1 ledger entries RB-T028-03/04/05/06
> - Originates from escalation `E-01` (5-layer asymmetric-binding cascade)

---

## 1. Context

The `Sprk.Bff.Api` BFF has feature-gated AI capabilities via two kill switches: `DocumentIntelligence:Enabled` and `Analysis:Enabled`. Per ADR-018, these flags MAY disable AI features at startup — services like `IOpenAiClient`, `IRagService`, `IBriefingAi`, `SprkChatAgentFactory` are not registered when the flag is off.

However, the BFF's minimal-API endpoints are registered in `EndpointMappingExtensions.MapDomainEndpoints` and ASP.NET Core performs **endpoint metadata generation** at startup. This pipeline fails on the FIRST unresolvable handler parameter — taking down the entire `WebApplication` regardless of whether the failing endpoint was even going to be hit.

The r1 ledger captured 4 entries (RB-T028-03/04/05/06) tracing to this symptom:
- RB-T028-03: KnowledgeBase endpoints — 13 tests skipped
- RB-T028-04: Chat endpoints — 11 tests skipped
- RB-T028-05: Re-analysis flow — 8 tests skipped
- RB-T028-06: Auth filter collateral — 4 tests skipped

The r2 project's E-01 escalation surfaced that the actual surface is wider:

- Phase 1a inventory identified **13 services** requiring treatment (8 BLOCKING + 5 LATENT pairs)
- Phase 1c iterative discovery flushed **3 additional residuals** (ChatContextMappingService, DocxExportService, IWorkingDocumentService) that the static inventory missed because their endpoint param-injection wasn't exercised by the immediate test fixtures
- Step 9.5 latent-bug scan flushed **2 more residuals** (IVisualizationService, IFileIndexingService) hidden by Moq stubs in 5 test fixtures
- **Total: 18 services migrated** across r2 task 011 (10 P3 Null-Objects + 8 P1 promote-to-unconditional)

ADR-018 in its current form is INSUFFICIENT: it says "Disabled features return 503 ProblemDetails" at the endpoint layer but does NOT specify what happens at the DI-composition layer when an endpoint declares a parameter whose type is conditionally registered. Without a binding pattern, the choice was made ad hoc per service — leading to the cluster bug r2 closes.

This ADR codifies the **Null-Object kill-switch pattern**: every conditionally-registered service that has an unconditionally-mapped endpoint consumer MUST have a Null-Object impl registered in the `else` branch of the gate.

---

## 2. Decision

For every BFF service `T` that is registered conditionally (inside an `if (flag) { ... }` block in any `*Module.cs` file) AND has at least one consumer in an unconditionally-mapped endpoint handler:

> **A Null-Object implementation of `T` MUST be registered in the corresponding `else` branch of the gate.**

The Null-Object MUST be one of three behavioral patterns, chosen per service:

| Pattern | When to use | Behavior contract |
|---|---|---|
| **P1: Promote-to-unconditional** | Service has zero feature-gated transitive deps | Move the registration outside the `if`-block. Real impl runs in both states. |
| **P2: Quiet no-op Null-Object** | Service is a side-effecting fire-and-forget operation where absence equals "nothing happened" | Methods return `Task.CompletedTask`, default-constructed values, or empty enumerables. No exception. |
| **P3: Fail-fast Null-Object** | Service returns data/computation; silent empty would mislead consumers about kill-switch state | Methods throw `FeatureDisabledException("<feature>.disabled", "<feature> requires X:Enabled=true …")`. Endpoint handler catches and returns 503 ProblemDetails per ADR-018. |

The choice MUST be documented in the corresponding module file's `///` XML comment near the `else` branch. For r2 task 011, the per-service choices are enumerated in `projects/sdap.bff.api-test-suite-repair-r2/decisions/D-09-nullobject-design.md`.

---

## 3. Constraints

### ✅ MUST

- **MUST** register a Null-Object (or promote-to-unconditional) for any conditional service consumed by an unconditionally-mapped endpoint. CLAUDE.md §10 bullet 6 codifies the "endpoints that map unconditionally must have unconditional service registration" rule; this ADR is the canonical implementation pattern.
- **MUST** use one of the 3 patterns (P1/P2/P3). NO other patterns (e.g., `IServiceProvider.GetService<T>()` runtime resolution in endpoint handler) — that violates ADR-010 and breaks param-inference.
- **MUST** throw `FeatureDisabledException` (NOT generic `InvalidOperationException`) for P3 Null-Objects, so endpoint catch sites can convert to 503 ProblemDetails uniformly.
- **MUST** keep the Null-Object's constructor signature compatible with consumers that don't change. The Null-Object impl typically takes ONLY `ILogger<T>` (or nothing) — no other conditional deps.
- **MUST** mention the pattern choice in the module file's XML comment for the `else` branch, e.g., `// P3 Null-Object: see ADR-030.`
- **MUST** place `catch (FeatureDisabledException ex)` BEFORE generic `catch (Exception ex)` in endpoint handlers, so 503 takes precedence over fall-through 500.
- **MUST** use the canonical `errorCode` naming convention `ai.<feature>.disabled` (e.g., `ai.chat.disabled`, `ai.rag.disabled`, `ai.briefing.disabled`, `ai.text-extraction.disabled`, `ai.visualization.disabled`, `ai.file-indexing.disabled`).

### ❌ MUST NOT

- **MUST NOT** introduce a new interface SOLELY for the Null-Object. If the service is currently a concrete class (per ADR-010), the Null-Object SHOULD be a subclass (which may require unsealing) — NOT a new `IService` extracted just for testability.
- **MUST NOT** use Null-Object pattern to silently mask broken DI configuration. Null-Objects exist for INTENTIONAL kill-switch states only. Real registration mistakes (the r1 ledger RB-T028 root cause: services placed inside feature-gated DI modules because AI features happen to consume them, but whose constructor deps are CRUD-only) should be repaired via Promote-to-unconditional (P1).
- **MUST NOT** allow Null-Object to bypass authorization filters. Null-Object only neutralizes the SERVICE behavior; ASP.NET Core endpoint filters (auth, rate limit) fire unchanged.
- **MUST NOT** register a quiet no-op (P2) for query/computation services. The silent-empty result misleads consumers.

---

## 4. Implementation Patterns

### 4.1 Concrete-class Null-Object (when service has no interface, per ADR-010)

```csharp
public class SprkChatAgentFactory  // NOTE: NOT sealed — Null-Object derives
{
    // Production ctor — takes all the AI deps.
    public SprkChatAgentFactory(IOpenAiClient openAiClient, IPlaybookService playbookService, ILogger<SprkChatAgentFactory> logger) { … }

    // Null-Object ctor — minimal deps, only ILogger.
    protected SprkChatAgentFactory(ILogger<SprkChatAgentFactory> logger)
    {
        _logger = logger;
    }

    public virtual Task<ISprkChatAgent> CreateAgentAsync(...) { /* real impl */ }
}

internal sealed class NullSprkChatAgentFactory : SprkChatAgentFactory
{
    public NullSprkChatAgentFactory(ILogger<SprkChatAgentFactory> logger) : base(logger) { }

    public override Task<ISprkChatAgent> CreateAgentAsync(...)
        => throw new FeatureDisabledException(
            "ai.chat.disabled",
            "AI chat requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.");
}

// In AnalysisServicesModule.AddNullObjectsForCompoundOff:
services.AddSingleton<SprkChatAgentFactory>(sp =>
    new NullSprkChatAgentFactory(sp.GetRequiredService<ILogger<SprkChatAgentFactory>>()));
```

### 4.2 Interface Null-Object (when service exposes an interface)

```csharp
internal sealed class NullBriefingAi : IBriefingAi
{
    private readonly ILogger<NullBriefingAi> _logger;
    public NullBriefingAi(ILogger<NullBriefingAi> logger) => _logger = logger;

    public Task<string> GenerateNarrativeAsync(string prompt, int? maxOutputTokens = null, CancellationToken ct = default)
    {
        _logger.LogDebug("NullBriefingAi.GenerateNarrativeAsync invoked — AI disabled");
        throw new FeatureDisabledException("ai.briefing.disabled", "Briefing requires AI.");
    }
}

// In AnalysisServicesModule else-branch:
services.AddScoped<IBriefingAi, NullBriefingAi>();
```

### 4.3 Endpoint catch + 503 ProblemDetails

```csharp
private static async Task<IResult> SearchInvoices(
    string query, IInvoiceSearchService searchService, ...)
{
    try
    {
        var result = await searchService.SearchAsync(query, ...);
        return TypedResults.Ok(result);
    }
    catch (FeatureDisabledException ex)
    {
        return ex.AsFeatureDisabled503();  // shared helper — see §5
    }
    catch (Exception ex)  // generic catch placed AFTER
    {
        logger.LogError(ex, "Unexpected error in SearchInvoices");
        return Results.Problem(statusCode: 500, ...);
    }
}
```

### 4.4 Promote-to-unconditional (P1)

```csharp
// BEFORE (in AddPlaybookServices, inside compound gate):
private static void AddPlaybookServices(IServiceCollection services)
{
    services.AddHttpClient<IPlaybookService, PlaybookService>();
    services.AddSingleton<NotificationService>();  // ← misregistered (CRUD-only deps)
}

// AFTER:
private static void AddPlaybookServices(IServiceCollection services)
{
    services.AddHttpClient<IPlaybookService, PlaybookService>();
    // NotificationService promoted to unconditional — has zero AI deps (per ADR-030).
}

// At top of AddAnalysisServicesModule (UNCONDITIONAL):
services.AddSingleton<NotificationService>();
```

### 4.5 SSE error chunk (when response already committed)

For SSE handlers where the response stream has been committed (HTTP headers written + content-type `text/event-stream`), a 503 ProblemDetails JSON cannot be returned. Instead, emit an error chunk:

```csharp
try
{
    await foreach (var chunk in agent.StreamAsync(ct))
        await WriteChatSseAsync("token", chunk, response, ct);
    await WriteChatSseAsync("done", "", response, ct);
}
catch (FeatureDisabledException ex)
{
    await WriteChatSseAsync("error", $"[{ex.ErrorCode}] {ex.Message}", response, ct);
}
```

Clients consuming SSE MUST handle `event: error` chunks. The `[errorCode]` prefix in the data payload allows symbolic detection.

---

## 5. The `FeatureDisabledException` Type + Helper

A single common exception type for all P3 Null-Objects. Lives in `src/server/api/Sprk.Bff.Api/Configuration/FeatureDisabledException.cs`:

```csharp
namespace Sprk.Bff.Api.Configuration;

public sealed class FeatureDisabledException : InvalidOperationException
{
    public string ErrorCode { get; }

    public FeatureDisabledException(string errorCode, string detail) : base(detail)
        => ErrorCode = errorCode ?? throw new ArgumentNullException(nameof(errorCode));
}
```

The shared 503 helper at `src/server/api/Sprk.Bff.Api/Configuration/FeatureDisabledResults.cs`:

```csharp
public static class FeatureDisabledResults
{
    public const string TypeUri = "https://errors.spaarke.com/feature-disabled";

    public static IResult AsFeatureDisabled503(this FeatureDisabledException ex)
        => Results.Problem(
            title: "Feature Disabled",
            detail: ex.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable,
            type: TypeUri,
            extensions: new Dictionary<string, object?> { ["errorCode"] = ex.ErrorCode });
}
```

---

## 6. When NOT to use this pattern

| Scenario | Action |
|---|---|
| Service is genuinely optional (e.g., Cosmos persistence) and consumers already use `IServiceProvider.GetService<T>()` with null-tolerance — and that consumer is NOT a minimal-API handler param | Leave as-is. Optional-via-`GetService` is a valid ADR-010 pattern for services NEVER consumed by minimal-API param-inference. |
| Service is consumed ONLY inside another conditional service (transitively conditional) and never injected directly into an endpoint | Leave as-is. The outer conditional registration is enough. |
| Feature flag is a true environment-only toggle (e.g., Redis on/off where both branches register a valid `IDistributedCache`) | Leave as-is. That's already a symmetric registration. |
| Background hosted service (`IHostedService`) or job handler (`IJobHandler<T>`) | Leave conditional. These don't participate in endpoint metadata generation. Under kill switch, fail-fast on dequeue; existing Service Bus retry/DLQ machinery handles per ADR-018. |

---

## 7. Anti-Patterns

```csharp
// ❌ DON'T: use [FromServices] + nullable + null-check inline in endpoint
private static async Task<IResult> Handler([FromServices] IBriefingAi? briefingAi = null) {
    if (briefingAi is null) return Results.Problem(503, ...);  // forces every endpoint to repeat
}

// ❌ DON'T: register Null-Object that silently returns empty data (quiet for query services)
public sealed class NullRagService : IRagService {
    public Task<RagResponse> SearchAsync(...) => Task.FromResult(RagResponse.Empty);  // misleading!
}

// ❌ DON'T: extract a new interface just to Null-Object a concrete sealed class
public interface ISprkChatAgentFactory { ... }  // new interface SOLELY for Null-Object — violates ADR-010
public sealed class NullSprkChatAgentFactory : ISprkChatAgentFactory { ... }
public sealed class SprkChatAgentFactory : ISprkChatAgentFactory { ... }

// ✅ DO: throw FeatureDisabledException, catch at endpoint, return 503
internal sealed class NullRagService : IRagService {
    public Task<RagResponse> SearchAsync(...)
        => throw new FeatureDisabledException("ai.rag.disabled", "RAG search requires AI.");
}
```

---

## 8. Integration with Other ADRs

| ADR | Relationship |
|---|---|
| ADR-001 (Minimal API) | Null-Object preserves minimal-API param-inference compatibility — no `IServiceProvider` injection workarounds in handler signatures. |
| ADR-007 (SpeFileStore facade) | Facade pattern precedent for the P3 Null-Object treatment of public-contracts facades (`IBriefingAi`, `IInvoiceAi`, etc.). Also: the B8 refactor in r2 task 011 Phase 1b Tier 3 (`KnowledgeBaseEndpoints` → `IRagService` instead of direct `SearchIndexClient` injection) was incidental ADR-007 cleanup made possible by the Null-Object work. |
| ADR-008 (Endpoint filters) | Null-Object does NOT bypass endpoint filters; auth + rate limit run as normal. |
| ADR-010 (DI minimalism) | Null-Objects MUST NOT introduce new interfaces (DI minimalism). Concrete-class Null-Object via inheritance is the preferred pattern. |
| ADR-018 (Feature flags / kill switches) | ADR-018 says "disabled features return 503 ProblemDetails." ADR-030 specifies HOW to achieve that at the DI-composition layer (Null-Object), in service of the same outcome. |
| ADR-019 (ProblemDetails) | All Null-Object-triggered 503 responses use ProblemDetails per ADR-019 (stable `type` URI, `extensions["errorCode"]`). |
| ADR-029 (BFF publish hygiene) | Net code addition for r2 task 011 (10 Null-Objects + helper + exception + endpoint catches) was ~1900 LOC across 34 files — well within publish-size budget. |
| CLAUDE.md §10 (BFF Hygiene — Binding Governance) | This ADR is the canonical mechanism by which "endpoints that map unconditionally must have unconditional service registration" is satisfied when a service must remain conditional. |

---

## 9. Test Update Obligation

Per `.claude/constraints/bff-extensions.md § F`, every change to BFF service registration MUST be accompanied by test coverage. For Null-Object impls, the test obligation is implicitly satisfied by the existing fixtures (which already set `Analysis:Enabled=false`) — the Skip→Pass transitions of 37 RB-T028-tagged integration tests in r2 task 011 Phase 1c IS the test evidence.

For NEW Null-Object impls added after r2 closes, a unit test SHOULD verify the Null-Object throws `FeatureDisabledException` with the expected `ErrorCode` (one xUnit `[Fact]` per Null-Object class).

---

## 10. PR review checklist (governance enforcement)

Phase 5 of source project codifies this checklist into `docs/procedures/testing-and-code-quality.md` for ongoing enforcement. Before merging any PR that adds a new service registration to a `*Module.cs` DI helper:

1. Is the new registration inside a feature-gate `if (flag) { ... }` block? If YES:
2. Does the service have any consumer in an endpoint handler signature (parameter type without `[FromServices] T? = null` defensive nullable)?
3. Is that endpoint mapped UNCONDITIONALLY (e.g., `app.MapXxxEndpoints()` called outside any `if (flag)`)?
4. If YES to all three: apply ADR-030. Choose P1/P2/P3 per Decision table. Document choice in `else`-branch comment.

Static-scan recipe (for code-review automation):

```bash
# For each conditional service S:
rg -t cs -n "[\s,(]S\s+\w+[,)]" src/server/api/Sprk.Bff.Api/Api/   # find endpoint param injection
# Cross-check that the consuming endpoint's MapXxx() is itself unconditional in EndpointMappingExtensions.cs.
```

---

## 11. Open Questions (resolved or punted)

1. **Q**: Should `FeatureDisabledException` derive from `InvalidOperationException` or be its own root?
   **A**: Derive from `InvalidOperationException`. Maintains compatibility with existing endpoint catch-all-exceptions middleware while allowing endpoint-specific filtering by type.

2. **Q**: Should the Null-Object pattern apply to background hosted services (e.g., `PlaybookSchedulerService`)?
   **A**: NO. Hosted services that depend on conditional types should remain conditional (no `.AddHostedService<NullHostedService>()` worker). Hosted services don't participate in endpoint metadata generation. Under kill switch, fail-fast on dequeue; existing Service Bus retry/DLQ machinery handles per ADR-018.

3. **Q**: Should we add a CI rule to ENFORCE this pattern?
   **A**: NOT in r2 (per design.md §5.5 explicit non-CI enforcement). Enforcement is PR template + code review + reviewer judgment. Future project could add NetArchTest rule.

4. **Q**: Why didn't the Phase 1a inventory catch all 18 services?
   **A**: The inventory methodology focused on "find conditionally-registered services in DI module files" and "find unconditionally-mapped endpoints in `EndpointMappingExtensions`" but did not systematically cross-reference all CONSUMERS of conditional services across the full endpoint surface. The 5 residuals (3 caught at Phase 1c iteration + 2 at Step 9.5 latent-bug scan) were registered conditionally AND consumed by unconditional endpoints, but the consumption sites weren't enumerated until tests surfaced the failure or the proactive scan ran. Lesson: Phase 5 governance update codifies the static-scan recipe in §10 above so this can be done preemptively.

---

## 12. Migration Note

The 13 services identified in r2's initial asymmetric-registration inventory + the 5 residuals discovered during execution = **18 services migrated** through r2 task 011 Phase 1b. Per-service patterns:

- **P1 Promote-to-unconditional** (8 services): NotificationService, IChatDataverseRepository + ChatDataverseRepository, ChatSessionManager, ChatHistoryManager, AnalysisChatContextResolver, StandaloneChatContextProvider, ChatContextMappingService, DocxExportService, IWorkingDocumentService — all had zero AI deps in their constructors; their conditional registration was misclassification.
- **P3 Fail-fast Null-Object** (10 services): NullBriefingAi, NullInvoiceSearchService, NullPlaybookOrchestrationService, NullTextExtractor, NullPlaybookService, NullRagService, NullSprkChatAgentFactory (subclass via unseal), NullPendingPlanManager (subclass via unseal), NullVisualizationService, NullFileIndexingService.
- **P2 Quiet no-op**: zero services qualified in r2.

Future PRs adding NEW conditional BFF services MUST apply this pattern preemptively per the test update obligation. See `projects/sdap.bff.api-test-suite-repair-r2/decisions/D-09-nullobject-design.md` for the per-service rationale.

---

## 13. References

- Concise version: `.claude/adr/ADR-030-bff-nullobject-kill-switch.md`
- Source project: `projects/sdap.bff.api-test-suite-repair-r2/`
- Initial escalation: `projects/sdap.bff.api-test-suite-repair-r2/escalations/E-01-rb-t028-cluster-scope-expansion.md`
- Inventory: `projects/sdap.bff.api-test-suite-repair-r2/baseline/asymmetric-registration-inventory-2026-06-01.md`
- Per-service design: `projects/sdap.bff.api-test-suite-repair-r2/decisions/D-09-nullobject-design.md`
- r1 ledger entries closed: RB-T028-03, RB-T028-04, RB-T028-05, RB-T028-06
- Fix commits (task 011 Phase 1b + 1c): `d207ae93`, `1cfac08c`, `5613b8ad`, `d932f355`, `43ca4f9b`, `dbd3888e`, `56e74b84`, `08343e32`
- Triple-run evidence: `projects/sdap.bff.api-test-suite-repair-r2/baseline/per-fix-triple-run-rb-t028-cluster-2026-06-01.md`
