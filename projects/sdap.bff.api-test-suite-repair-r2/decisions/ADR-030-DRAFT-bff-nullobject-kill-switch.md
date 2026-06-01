# ADR-030 (DRAFT): BFF Null-Object Kill-Switch Pattern

> **Status**: DRAFT — pending main-session sign-off + write to `.claude/adr/ADR-030-*.md`
> **Domain**: API Architecture / DI Composition
> **Authored**: 2026-06-01 by task-execute (Claude Code, Opus 4.7) — Phase 1a of task 011
> **Cross-references**:
> - Supersedes parts of [ADR-018](../../.claude/adr/ADR-018-feature-flags.md) (kill switches) — extends with composition-root binding requirement
> - Reinforces [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) (DI minimalism) — Null-Objects do NOT add new interfaces
> - Codifies the workflow rule introduced in [`CLAUDE.md §10`](../../CLAUDE.md) bullet 6 ("Endpoints that map unconditionally must have unconditional service registration") — explains HOW to satisfy that rule when a service must remain feature-gated
> - Resolves r1 ledger entries [RB-T028-03/04/05/06](../../projects/sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md)
> - Originates from escalation [`E-01`](../escalations/E-01-rb-t028-cluster-scope-expansion.md) (5-layer asymmetric-binding cascade)

---

## 1. Context

The `Sprk.Bff.Api` BFF has feature-gated AI capabilities (kill switches `DocumentIntelligence:Enabled` and `Analysis:Enabled`). Per ADR-018, these flags MAY disable AI features at startup — services like `IOpenAiClient`, `IRagService`, `IBriefingAi`, `SprkChatAgentFactory` are not registered when the flag is off.

However, the BFF's minimal-API endpoints are registered in `EndpointMappingExtensions.MapDomainEndpoints` and ASP.NET Core performs ENDPOINT METADATA GENERATION at startup. This pipeline fails on the FIRST unresolvable handler parameter — taking down the entire `WebApplication` regardless of whether the failing endpoint was even going to be hit. The r1 ledger captured 4 entries (RB-T028-03/04/05/06) tracing to this symptom; the r2 project's E-01 escalation surfaced that the actual surface is wider (8 BLOCKING + 5 LATENT pairs across ~13 services).

ADR-018 in its current form is INSUFFICIENT: it says "Disabled features return 503 ProblemDetails" at the endpoint layer but does NOT say what happens at the DI-composition layer when an endpoint declares a parameter whose type is conditionally registered. Without a binding pattern, the choice is made ad hoc per service — leading to the cluster bug r2 closes.

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

The choice MUST be documented in the corresponding module file's `///` XML comment near the `else` branch. For Phase 1b of project `sdap.bff.api-test-suite-repair-r2`, the per-service choices are enumerated in [D-09](D-09-nullobject-design.md).

---

## 3. Constraints

### ✅ MUST

- **MUST** register a Null-Object (or promote-to-unconditional) for any conditional service consumed by an unconditionally-mapped endpoint. CLAUDE.md §10 bullet 6 codifies the "endpoints that map unconditionally must have unconditional service registration" rule; this ADR is the canonical implementation pattern.
- **MUST** use one of the 3 patterns (P1/P2/P3). NO other patterns (e.g., `IServiceProvider.GetService<T>()` runtime resolution in endpoint handler) — that violates ADR-010 and breaks param-inference.
- **MUST** throw `FeatureDisabledException` (NOT generic `InvalidOperationException`) for P3 Null-Objects, so endpoint catch sites can convert to 503 ProblemDetails uniformly.
- **MUST** keep the Null-Object's constructor signature compatible with consumers that don't change. The Null-Object impl typically takes ONLY `ILogger<T>` (or nothing) — no other conditional deps.
- **MUST** mention the pattern choice in the module file's XML comment for the `else` branch, e.g., `// P3 Null-Object: see ADR-030.`

### ❌ MUST NOT

- **MUST NOT** introduce a new interface SOLELY for the Null-Object. If the service is currently a concrete class (per ADR-010), the Null-Object SHOULD be a subclass (which may require unsealing) — NOT a new `IService` extracted just for testability.
- **MUST NOT** use Null-Object pattern to silently mask broken DI configuration. Null-Objects exist for INTENTIONAL kill-switch states only. Real registration mistakes (the r1 ledger RB-T028 root cause) should be repaired via Promote-to-unconditional (P1).
- **MUST NOT** allow Null-Object to bypass authorization filters. Null-Object only neutralizes the SERVICE behavior; ASP.NET Core endpoint filters (auth, rate limit) fire unchanged.
- **MUST NOT** register a quiet no-op (P2) for query/computation services. The silent-empty result misleads consumers.

---

## 4. Implementation Patterns

### 4.1 Concrete-class Null-Object (when service has no interface, per ADR-010)

```csharp
public class SprkChatAgentFactory  // NOTE: NOT sealed — Null-Object derives
{
    public virtual Task<ISprkChatAgent> CreateAgentAsync(...) { /* real impl */ }
}

internal sealed class NullSprkChatAgentFactory : SprkChatAgentFactory
{
    private readonly ILogger<SprkChatAgentFactory> _logger;
    public NullSprkChatAgentFactory(ILogger<SprkChatAgentFactory> logger) => _logger = logger;

    public override Task<ISprkChatAgent> CreateAgentAsync(...)
        => throw new FeatureDisabledException(
            "ai.chat.disabled",
            "Chat requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.");
}

// In AiModule.AddAiModule:
if (analysisEnabled && documentIntelligenceEnabled)
{
    services.AddSingleton<SprkChatAgentFactory>();  // real
}
else
{
    services.AddSingleton<SprkChatAgentFactory>(sp =>
        new NullSprkChatAgentFactory(sp.GetRequiredService<ILogger<SprkChatAgentFactory>>()));
}
```

### 4.2 Interface Null-Object (when service exposes an interface)

```csharp
internal sealed class NullBriefingAi : IBriefingAi
{
    public Task<string> GenerateNarrativeAsync(string prompt, int? maxOutputTokens = null, CancellationToken ct = default)
        => throw new FeatureDisabledException("ai.briefing.disabled", "Briefing requires AI.");
}

// In AnalysisServicesModule.AddPublicContractsFacade else-branch:
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
        return Results.Problem(
            statusCode: 503,
            title: "Feature Disabled",
            detail: ex.Message,
            extensions: new Dictionary<string, object?> { ["errorCode"] = ex.ErrorCode });
    }
}
```

### 4.4 Promote-to-unconditional (P1)

```csharp
// BEFORE (in AddPlaybookServices, inside compound gate):
private static void AddPlaybookServices(IServiceCollection services)
{
    services.AddHttpClient<IPlaybookService, PlaybookService>();
    services.AddSingleton<NotificationService>();  // ← misregistered
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

---

## 5. The `FeatureDisabledException` Type

A single common exception type for all P3 Null-Objects. Lives in `src/server/api/Sprk.Bff.Api/Configuration/FeatureDisabledException.cs`. Endpoint catch sites use a shared `Results.FeatureDisabled503(...)` helper.

---

## 6. When NOT to use this pattern

| Scenario | Action |
|---|---|
| Service is genuinely optional (e.g., Cosmos persistence) and consumers already use `IServiceProvider.GetService<T>()` with null-tolerance | Leave as-is. Optional-via-`GetService` is a valid ADR-010 pattern for services that are NEVER consumed by minimal-API param-inference. |
| Service is consumed ONLY inside another conditional service (transitively conditional) and never injected directly into an endpoint | Leave as-is. The outer conditional registration is enough. |
| Feature flag is a true environment-only toggle (e.g., Redis on/off where both branches register a valid `IDistributedCache`) | Leave as-is. That's already a symmetric registration. |

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
| [ADR-010](.claude/adr/ADR-010-di-minimalism.md) | Null-Objects MUST NOT introduce new interfaces (DI minimalism). Concrete-class Null-Object via inheritance is the preferred pattern. |
| [ADR-018](.claude/adr/ADR-018-feature-flags.md) | ADR-018 says "disabled features return 503 ProblemDetails." ADR-030 specifies HOW to achieve that at the DI-composition layer (Null-Object), in service of the same outcome. |
| [ADR-019](.claude/adr/ADR-019-problemdetails.md) | All Null-Object-triggered 503 responses use ProblemDetails per ADR-019. |
| [ADR-001](.claude/adr/ADR-001-minimal-api.md) | Null-Object preserves minimal-API param-inference compatibility — no `IServiceProvider` injection workarounds in handler signatures. |
| [ADR-007](.claude/adr/ADR-007-spefilestore.md) | Facade pattern (e.g., `SpeFileStore`) precedent for the P3 Null-Object treatment of public-contracts facades (`IBriefingAi`, `IInvoiceAi`, etc.). |
| [ADR-008](.claude/adr/ADR-008-endpoint-filters.md) | Null-Object does NOT bypass endpoint filters; auth + rate limit run as normal. |
| [CLAUDE.md §10 bullet 6](../../CLAUDE.md) (BFF Hygiene — Binding Governance) | This ADR is the canonical mechanism by which the "endpoints that map unconditionally must have unconditional service registration" rule is satisfied when a service must remain conditional. |

---

## 9. Test Update Obligation

Per `.claude/constraints/bff-extensions.md § F`, every change to BFF service registration MUST be accompanied by test coverage. For Null-Object impls, the test obligation is implicitly satisfied by the existing fixtures (which already set `Analysis:Enabled=false`) — the Skip→Pass transitions of the 36 RB-T028-tagged tests in r2 task 011 Phase 1c IS the test evidence.

For NEW Null-Object impls added after r2 closes, a unit test SHOULD verify the Null-Object throws `FeatureDisabledException` with the expected `ErrorCode` (one xUnit `[Fact]` per Null-Object class).

---

## 10. Open Questions (resolved or punted)

1. **Q**: Should `FeatureDisabledException` derive from `InvalidOperationException` or be its own root?
   **A**: Derive from `InvalidOperationException`. Maintains compatibility with existing endpoint catch-all-exceptions middleware while allowing endpoint-specific filtering by type.

2. **Q**: Should the Null-Object pattern apply to background hosted services (e.g., `PlaybookSchedulerService`)?
   **A**: NO. Hosted services that depend on conditional types should remain conditional (no `.AddHostedService<NullHostedService>()` worker). Hosted services don't participate in endpoint metadata generation.

3. **Q**: Should we add a CI rule to ENFORCE this pattern?
   **A**: NOT in r2 (per design.md §5.5 explicit non-CI enforcement). Enforcement is PR template + code review + reviewer judgment. Future project could add NetArchTest rule.

---

## 11. Migration Note

The 13 services identified in r2's [asymmetric-registration-inventory](../baseline/asymmetric-registration-inventory-2026-06-01.md) are the migration backlog for THIS adoption. After r2 task 011 Phase 1c closes, all 13 are converted per [D-09](D-09-nullobject-design.md). Future PRs adding NEW conditional BFF services MUST apply this pattern preemptively per the test update obligation.

---

## 12. References

- Project: `sdap.bff.api-test-suite-repair-r2`
- Initial escalation: [`E-01`](../escalations/E-01-rb-t028-cluster-scope-expansion.md)
- Inventory: [`asymmetric-registration-inventory-2026-06-01.md`](../baseline/asymmetric-registration-inventory-2026-06-01.md)
- Per-service design: [`D-09-nullobject-design.md`](./D-09-nullobject-design.md)
- r1 ledger entries: RB-T028-03, RB-T028-04, RB-T028-05, RB-T028-06

---

*This is a DRAFT ADR. Main session will write the final ADR to `.claude/adr/ADR-030-bff-nullobject-kill-switch.md` after owner sign-off, and add a corresponding full-history entry to `docs/adr/`. Per CLAUDE.md §3 (sub-agent write boundary), this task-execute agent CANNOT write to `.claude/` paths.*
