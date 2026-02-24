# AIPL-010: RagQueryBuilder — DI Registrations (APPLIED 2026-02-23)

> **Task**: AIPL-010 — Implement RagQueryBuilder
> **ADR-010**: DI baseline is 89. New service MUST use feature module extension method pattern.
> **Target Module**: `Infrastructure/DI/AiModule.cs` (create if not exists) — `AddAiPlatformModule()`

## Registration Lines

```csharp
// AIPL-010 RagQueryBuilder
builder.Services.AddSingleton<RagQueryBuilder>();
```

## Notes

- `RagQueryBuilder` is a pure, stateless service with no constructor dependencies — `AddSingleton` is correct.
- Per ADR-010, this registration MUST go inside a feature module extension method (e.g., `AddAiPlatformModule()`),
  NOT inline in Program.cs.
- The module call (`builder.Services.AddAiPlatformModule()`) then goes in Program.cs as a single line.
- Also: `AnalysisOrchestrationService` now depends on `RagQueryBuilder` — ensure DI resolves it before
  `AnalysisOrchestrationService` is resolved (Singleton lifetime is compatible with its scoped container).

## DI Impact

| Service | Lifetime | Net New Lines |
|---------|----------|--------------|
| `RagQueryBuilder` | Singleton | +1 (inside module) |

DI count after AIPL-010: Still within existing module — no new line in Program.cs.
