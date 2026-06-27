# ADR-018: Feature Flags and Kill Switches (Concise)

> **Status**: Proposed
> **Domain**: Configuration/Operations
> **Last Updated**: 2025-12-18

---

## Decision

Use **options-based feature flags** with typed validation. Disabled features return `503 ProblemDetails`. Flags never bypass authorization.

**Rationale**: Reliable kill switches enable quick mitigation. Consistent patterns ensure predictable behavior across endpoints and handlers.

---

## Constraints

### ✅ MUST

- **MUST** represent flags in typed options classes with startup validation
- **MUST** return `503` ProblemDetails when feature is disabled
- **MUST** check same flag in both endpoints and async handlers
- **MUST** document default value and environment behavior for each flag

### ❌ MUST NOT

- **MUST NOT** use flags to bypass authorization checks
- **MUST NOT** produce partial behavior when disabled (fail cleanly)
- **MUST NOT** allow hidden/undocumented kill switches

---

## Implementation Patterns

### Options Class

```csharp
public class AnalysisOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxDocumentsPerBatch { get; set; } = 50;
}
```

### Endpoint Check

```csharp
app.MapPost("/api/ai/analysis/execute", async (
    IOptions<AnalysisOptions> options,
    ...) =>
{
    if (!options.Value.Enabled)
        return Results.Problem(
            statusCode: 503,
            title: "Feature Disabled",
            extensions: new { errorCode = "ai.analysis.disabled" });

    // ... proceed with feature
});
```

### Handler Check

```csharp
public async Task<JobResult> HandleAsync(AnalysisJob job, CancellationToken ct)
{
    if (!_options.Value.Enabled)
        return JobResult.Failed("ai.analysis.disabled", "Feature disabled");

    // ... proceed with job
}
```

**See**: [Feature Flag Pattern](../patterns/config/feature-flags.md)

---

## Flag Scope Discipline (added 2026-06-03 from R5 design review)

### Principle

Feature flags exist at **product-capability boundaries**, not at **individual-service boundaries**. One end-user capability = one flag covering all of its supporting services.

### Why this matters

Each feature-flag-gated service interacting with an unconditionally-mapped endpoint must have a Null-Object impl (per ADR-032) — that's real boilerplate (a `NullSubclass` + `virtual` methods on the production class + endpoint catch sites for `FeatureDisabledException`). Sprawl-by-default ("every new service gets its own flag just in case") multiplies this cost without proportional operational benefit; you end up able to disable individual implementation details without being able to disable the user-visible capability cleanly.

### The rule

When introducing a new BFF capability (a feature a user can identify in the UI), evaluate flag placement:

1. **Capability already has an upstream flag** (e.g., a new sub-flow within `Chat`, `Analysis`, `Briefing`, `Insights`) → **No new flag.** Sub-services are unconditionally registered; the existing capability flag is the kill switch. The chat agent factory already returns `Null` when `Analysis:Enabled=false` — anything downstream of that inherits the disabled state without needing its own flag.

2. **Capability is new and stands alone** (a new top-level feature with its own UI surface, e.g., a new `/api/ai/insights/*` family) → **One new flag** (e.g., `InsightsOptions.Enabled`). All supporting services either register unconditionally OR register conditionally under that single flag with ADR-032 patterns. Not one flag per service.

3. **Service is genuinely optional independent of a capability** (rare — e.g., embedding model A vs B for the same capability) → flag the **selection**, not the service. One config option that swaps the registration, not two flags.

### ✅ MUST

- **MUST** scope feature flags to product capabilities the user can name
- **MUST** prefer inheriting an existing capability flag over introducing a new one for sub-services
- **MUST** justify any new flag in the originating ADR or design.md (one paragraph: what user-visible thing this kills, why an existing flag is insufficient)

### ❌ MUST NOT

- **MUST NOT** add a flag per service "just in case" — this multiplies ADR-032 boilerplate without operational benefit
- **MUST NOT** add a flag for an internal implementation choice (use config selection instead)
- **MUST NOT** treat the absence of a flag as a problem to fix — services without flags are unconditionally available, which is the default and correct state for most code

### How this interacts with ADR-032

ADR-032's Null-Object pattern is the mechanism for kill-switch states. This section defines WHERE the kill switches go. The two work together: ADR-018 sets the policy ("flags at capability boundaries"); ADR-032 sets the implementation ("when a flag exists and gates a service consumed by an unconditional endpoint, use P1/P2/P3"). When ADR-018's scope discipline is followed, ADR-032's overhead is bounded.

---

## Current Flags

| Options Class | Flag | Default | Purpose |
|--------------|------|---------|---------|
| `AnalysisOptions` | `Enabled` | `true` | Kill switch for analysis |
| `DocumentIntelligenceOptions` | `Enabled` | `true` | Kill switch for doc intel |

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-010](ADR-010-di-minimalism.md) | Options registration |
| [ADR-013](ADR-013-ai-architecture.md) | AI feature flags |
| [ADR-019](ADR-019-problemdetails.md) | Error response format |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-018-feature-flags-and-kill-switches.md](../../docs/adr/ADR-018-feature-flags-and-kill-switches.md)

---

**Lines**: ~95

