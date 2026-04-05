# Configuration Constraints

> **Domain**: Feature Flags, Versioning, Configuration
> **Source ADRs**: ADR-018, ADR-020
> **Last Updated**: 2026-04-05
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current (broken config pattern links replaced with R2 architecture docs)

---

## When to Load This File

Load when:
- Adding feature flags or kill switches
- Making breaking changes to APIs or packages
- Versioning job payloads or cache keys
- Implementing configuration options

---

## MUST Rules

### Feature Flags (ADR-018)

- ✅ **MUST** represent flags in typed options classes with startup validation
- ✅ **MUST** return `503` ProblemDetails when feature is disabled
- ✅ **MUST** check same flag in both endpoints and async handlers
- ✅ **MUST** document default value and environment behavior

### Versioning (ADR-020)

- ✅ **MUST** use SemVer for client packages
- ✅ **MUST** implement tolerant readers for payloads
- ✅ **MUST** include explicit version input for evolving contracts
- ✅ **MUST** require ADR update for breaking changes
- ✅ **MUST** provide deprecation window before removal

---

## MUST NOT Rules

### Feature Flags (ADR-018)

- ❌ **MUST NOT** use flags to bypass authorization
- ❌ **MUST NOT** produce partial behavior when disabled
- ❌ **MUST NOT** allow hidden/undocumented kill switches

### Versioning (ADR-020)

- ❌ **MUST NOT** make silent breaking changes
- ❌ **MUST NOT** rename fields without versioning
- ❌ **MUST NOT** break job payloads without new JobType

---

## Quick Reference Patterns

### Options Class

```csharp
public class AnalysisOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxDocumentsPerBatch { get; set; } = 50;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
}
```

### Feature Check in Endpoint

```csharp
if (!options.Value.Enabled)
    return Results.Problem(
        statusCode: 503,
        title: "Feature Disabled",
        extensions: new { errorCode = "ai.analysis.disabled" });
```

### Feature Check in Handler

```csharp
if (!_options.Value.Enabled)
    return JobResult.Failed("ai.analysis.disabled", "Feature disabled");
```

### Tolerant Reader

```csharp
public record AnalysisPayload
{
    public string DocumentId { get; init; } = "";
    public string AnalysisType { get; init; } = "summary"; // default
    public int PayloadVersion { get; init; } = 1;
}
```

---

## Versioning by Surface

| Surface | Strategy |
|---------|----------|
| APIs | Prefer additive; version URL if breaking |
| Jobs | `payloadVersion` field; new JobType for breaking |
| Packages | SemVer; major for breaking |
| Cache Keys | Include version in key (ADR-014) |

---

## Current Feature Flags

| Options Class | Flag | Default | Purpose |
|--------------|------|---------|---------|
| `AnalysisOptions` | `Enabled` | `true` | Kill switch for analysis |
| `DocumentIntelligenceOptions` | `Enabled` | `true` | Kill switch for doc intel |

---

## Pattern Files (Complete Examples)

- [Configuration Architecture](../../docs/architecture/configuration-architecture.md) — 21 options classes, validators, Key Vault integration
- [Configuration Matrix](../../docs/guides/CONFIGURATION-MATRIX.md) — Complete reference of all BFF API settings

---

## Source ADRs (Full Context)

| ADR | Focus | When to Load |
|-----|-------|--------------|
| [ADR-018](../adr/ADR-018-feature-flags.md) | Feature flags | New features |
| [ADR-020](../adr/ADR-020-versioning.md) | Versioning | Breaking changes |

---

**Lines**: ~115
**Purpose**: Single-file reference for configuration constraints

