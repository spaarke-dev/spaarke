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

