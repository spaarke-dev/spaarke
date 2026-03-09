# Pending DI Registrations — AIPL-011 SemanticDocumentChunker

> **Task**: AIPL-011
> **ADR**: ADR-010 — DI Minimalism
> **Module**: Must go into `AddAiPlatformModule()` feature extension method (do NOT add inline to Program.cs)
> **DI Budget**: Baseline 89 non-framework registrations; each new Workstream A/B/C service must use feature modules

## Status

COMPLETED — registration added to `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs`
(AiModule.cs already existed from AIPL-012).

## Registration (already applied)

```csharp
// AIPL-011 SemanticDocumentChunker
// In: src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs
services.AddSingleton<SemanticDocumentChunker>();
```

## Notes

- `SemanticDocumentChunker` is a **singleton** — it is stateless and thread-safe.  All input arrives
  via the `AnalyzeResult` parameter; the service holds no per-request state.
- **No interface** is registered per ADR-010: there is only one implementation of document chunking
  for `AnalyzeResult` input.  `TextChunkingService` operates on plain text strings and is a separate
  concern (different input type, different interface).
- AiModule DI count updated: 89 (baseline) + 3 (LlamaParseClient HttpClient + DocumentParserRouter
  + SemanticDocumentChunker) = 92 total non-framework registrations.
- Using statement was already present in AiModule.cs:
  ```csharp
  using Sprk.Bff.Api.Services.Ai;
  ```
