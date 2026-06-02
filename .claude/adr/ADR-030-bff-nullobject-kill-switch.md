# ADR-030: BFF Null-Object Kill-Switch Pattern (Concise)

> **Status**: Accepted
> **Domain**: BFF API Architecture / DI Composition
> **Last Updated**: 2026-06-01
> **Source project**: `sdap.bff.api-test-suite-repair-r2` task 011 (closes RB-T028-03/04/05/06)
> **Cross-references**: extends ADR-018 (kill switches); reinforces ADR-010 (DI minimalism); codifies CLAUDE.md §10 bullet 6.

---

## Decision

For every BFF service `T` that is registered **conditionally** (inside a feature-gate `if (flag) { … }` in a `*Module.cs`) AND has at least one consumer in an **unconditionally-mapped** endpoint handler, a Null-Object impl of `T` MUST be registered in the corresponding `else` branch — OR the service MUST be promoted to unconditional registration if it has zero feature-gated transitive deps. This eliminates a class of startup metadata-gen aborts caused by minimal-API param-inference failing on unresolvable handler parameters when kill switches are off.

---

## Three Patterns

| Pattern | When to use | Behavior |
|---|---|---|
| **P1 Promote-to-unconditional** | Service has zero feature-gated transitive deps; current conditionality is misclassification | Move the registration outside the `if`-block. Real impl runs in both states. |
| **P2 Quiet no-op Null-Object** | Side-effecting fire-and-forget; absence semantically equals "nothing happened" | Methods return `Task.CompletedTask`, defaults, or empty enumerables. No exception. |
| **P3 Fail-fast Null-Object** | Query/computation service; silent empty would mislead consumers about kill-switch state | Methods throw `FeatureDisabledException("<feature>.disabled", "…")`. Endpoint catches and returns 503 per ADR-018/019. |

---

## Constraints

### ✅ MUST

- **MUST** register a Null-Object (or promote to unconditional) for any conditional service consumed by an unconditionally-mapped endpoint. Failure to do so causes ASP.NET endpoint metadata generation to abort at host startup when the gate is off (taking down the entire BFF, not just the disabled endpoint).
- **MUST** use one of the three patterns (P1/P2/P3). NO `IServiceProvider.GetService<T>()` runtime resolution in endpoint handler signatures — that violates ADR-010 and breaks param-inference.
- **MUST** throw `FeatureDisabledException` (NOT generic `InvalidOperationException`) for P3 Null-Objects, so endpoint catch sites can convert to 503 ProblemDetails uniformly via the shared `AsFeatureDisabled503()` helper.
- **MUST** keep Null-Object constructors minimal — typically only `ILogger<T>` — so the Null-Object itself has no feature-gated transitive deps that would defeat the purpose.
- **MUST** document the pattern choice in the module file's `///` XML comment near the `else` branch, e.g., `// P3 Null-Object: see ADR-030.`
- **MUST** verify that endpoint catches for `FeatureDisabledException` are placed BEFORE generic `catch (Exception)` blocks so 503 takes precedence over fall-through 500.

### ❌ MUST NOT

- **MUST NOT** introduce a new interface SOLELY for the Null-Object. If the production service is a concrete `sealed` class (per ADR-010), the Null-Object MUST be a subclass — which may require unsealing + marking specific methods `virtual` + adding a `protected` ctor that takes only `ILogger<T>` for Null-Object use.
- **MUST NOT** use Null-Object pattern to silently mask broken DI configuration. Null-Objects exist for INTENTIONAL kill-switch states only. Real registration mistakes (e.g., the r1 RB-T028 root cause: services placed inside feature-gated DI modules because AI features happen to consume them, but whose constructor deps are CRUD-only) MUST be repaired via Promote-to-unconditional (P1), not papered over with a Null-Object.
- **MUST NOT** allow Null-Object behavior to bypass authorization filters. Endpoint filters (`AddEndpointFilter<DocumentAuthorizationFilter>()`, `.RequireAuthorization()`) fire BEFORE the handler body — Null-Object only affects handler body execution. Unauthorized requests still get 401/403; only authorized requests see 503.
- **MUST NOT** register a P2 Quiet Null-Object for query/computation services. Returning empty results would mislead consumers (e.g., a UI rendering "no playbooks available" instead of "feature disabled"). Use P3 Fail-fast for these.

---

## Key patterns

### Concrete-class Null-Object (preferred per ADR-010)

```csharp
// Production class — remove `sealed`; mark methods that need override `virtual`;
// add protected ctor for Null subclass use.
public class SprkChatAgentFactory
{
    protected SprkChatAgentFactory(ILogger<SprkChatAgentFactory> logger) { _logger = logger; }
    public SprkChatAgentFactory(/* production deps */) { … }
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

// In AnalysisServicesModule (compound-OFF else-branch):
services.AddSingleton<SprkChatAgentFactory>(sp =>
    new NullSprkChatAgentFactory(sp.GetRequiredService<ILogger<SprkChatAgentFactory>>()));
```

### Endpoint catch + 503 ProblemDetails

```csharp
try
{
    var result = await ragService.SearchAsync(query, ct);
    return TypedResults.Ok(result);
}
catch (FeatureDisabledException ex)
{
    return ex.AsFeatureDisabled503();  // shared helper — stable type URI + errorCode extension
}
```

### Promote-to-unconditional (P1)

```csharp
// BEFORE — service inside AddPlaybookServices (compound-gated), but has zero AI deps:
services.AddSingleton<NotificationService>();  // ← misregistered

// AFTER — moved to AddUnconditionalChatAndNotificationServices (top-level):
services.AddSingleton<NotificationService>();  // CRUD-only deps; safe unconditional.
```

### `FeatureDisabledException`

```csharp
namespace Sprk.Bff.Api.Configuration;

public sealed class FeatureDisabledException : InvalidOperationException
{
    public string ErrorCode { get; }
    public FeatureDisabledException(string errorCode, string detail) : base(detail)
        => ErrorCode = errorCode ?? throw new ArgumentNullException(nameof(errorCode));
}
```

Lives at `src/server/api/Sprk.Bff.Api/Configuration/FeatureDisabledException.cs`. Endpoint catch sites use the shared `Sprk.Bff.Api.Configuration.FeatureDisabledResults.AsFeatureDisabled503()` extension. ErrorCode strings follow `ai.<feature>.disabled` (e.g., `ai.chat.disabled`, `ai.rag.disabled`, `ai.briefing.disabled`).

---

## Rationale

The r1 ledger entries RB-T028-03/04/05/06 framed the issue as "conditional service registration + unconditional endpoint mapping" but captured only the first symptom layer. r2 task 011's Phase 1a inventory + Phase 1c quality-gate scan together revealed the full surface: **15 services** required the pattern across 4 DI modules (10 P3 Null-Objects + 5 Promote-to-unconditional). The Phase 1c iterative discovery (`d932f355`, `43ca4f9b`, `dbd3888e`, `56e74b84`) of 3 + 2 latent residuals demonstrates the asymmetric-registration anti-pattern is easy to introduce by accident — it requires an explicit ADR + reviewer discipline to prevent.

---

## When NOT to use this pattern

| Scenario | Action |
|---|---|
| Service is genuinely optional and consumers already use `IServiceProvider.GetService<T>()` with null-tolerance (not in endpoint param signatures) | Leave as-is. Optional-via-`GetService` is valid for services NEVER consumed by minimal-API param-inference. |
| Service is consumed only inside another conditional service (transitively conditional) and never injected into an unconditional endpoint | Leave as-is. Outer conditional registration is sufficient. |
| Feature flag is environment-only (e.g., Redis on/off where both branches register a valid `IDistributedCache`) | Leave as-is. Already a symmetric registration. |
| Background hosted service / job handler (no metadata-gen participation) | Leave conditional. Fail-fast on dequeue under kill switch; Service Bus retry/DLQ handles it per ADR-018. |

---

## Anti-patterns

```csharp
// ❌ DON'T: [FromServices] + nullable + null-check inline (forces every endpoint to repeat)
private static async Task<IResult> Handler([FromServices] IBriefingAi? briefingAi = null) {
    if (briefingAi is null) return Results.Problem(503, ...);
}

// ❌ DON'T: P2 quiet for query services (misleading)
public sealed class NullRagService : IRagService {
    public Task<RagResponse> SearchAsync(...) => Task.FromResult(RagResponse.Empty);
}

// ❌ DON'T: extract a new interface SOLELY to enable a Null-Object (violates ADR-010)
public interface ISprkChatAgentFactory { ... }  // new interface SOLELY for Null-Object
```

---

## Integration with other ADRs

| ADR | Relationship |
|---|---|
| ADR-001 (Minimal API) | Null-Object preserves minimal-API param-inference compatibility — no `IServiceProvider` injection workarounds in handler signatures |
| ADR-007 (Facade pattern) | Null-Object preserves facade-pattern compliance — endpoints depend on domain services, not Azure SDK directly |
| ADR-008 (Endpoint filters) | Auth + rate-limit filters fire BEFORE Null-Object handler body — Null-Object does NOT bypass them |
| ADR-010 (DI minimalism) | Null-Object MUST NOT introduce new interfaces; concrete-class subclass is the preferred pattern when production class is sealed |
| ADR-018 (Kill switches) | ADR-018 says "disabled features return 503 ProblemDetails"; ADR-030 specifies HOW to achieve that at the DI-composition layer |
| ADR-019 (ProblemDetails) | All Null-Object-triggered 503 responses use `Results.Problem(...)` with stable `type=https://errors.spaarke.com/feature-disabled` and `extensions["errorCode"]` |
| ADR-029 (Publish hygiene) | Null-Object net code addition (~700-1500 LOC across the migration) is well within publish-size budget |
| CLAUDE.md §10 (BFF Binding Governance) | This ADR is the canonical mechanism by which §10 bullet 6 ("endpoints that map unconditionally must have unconditional service registration") is satisfied when a service must remain feature-gated |

---

## PR review checklist (governance enforcement — Phase 5 of source project codifies in `docs/procedures/testing-and-code-quality.md`)

Before merging a PR that adds a new service registration to a `*Module.cs` DI helper:

1. Is the new registration inside a feature-gate `if (flag) { ... }` block? If YES:
2. Does the service have any consumer in an endpoint handler signature (parameter type without `[FromServices] T? = null` defensive nullable)?
3. Is that endpoint mapped UNCONDITIONALLY (e.g., `app.MapXxxEndpoints()` called outside any `if (flag)`)?
4. If YES to all three: apply ADR-030. Choose P1/P2/P3 per Decision table. Document choice in `else`-branch comment.

Static-scan grep recipe (for code-review automation):
```bash
# For each conditional service S:
rg -t cs -n "[\s,(]S\s+\w+[,)]" src/server/api/Sprk.Bff.Api/Api/   # find endpoint param injection
# Cross-check that the consuming endpoint's MapXxx() is itself unconditional in EndpointMappingExtensions.cs.
```

---

## References

- Full ADR (background, rationale history, migration log): `docs/adr/ADR-030-bff-nullobject-kill-switch.md`
- Source project execution: `projects/sdap.bff.api-test-suite-repair-r2/` (task 011)
- Asymmetric-registration inventory (15 services): `projects/sdap.bff.api-test-suite-repair-r2/baseline/asymmetric-registration-inventory-2026-06-01.md`
- Per-service design (10 P3 + 5 P1 choices): `projects/sdap.bff.api-test-suite-repair-r2/decisions/D-09-nullobject-design.md`
- r1 ledger entries closed: RB-T028-03/04/05/06
- Fix commits (task 011 Phase 1b + 1c): `d207ae93`, `1cfac08c`, `5613b8ad`, `d932f355`, `43ca4f9b`, `dbd3888e`, `56e74b84`, `08343e32`
