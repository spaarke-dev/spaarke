# Pending DI Registrations — AIPL-013

> **Task**: AIPL-013 — Implement RagIndexingPipeline
> **Created**: 2026-02-23
> **Status**: Registered — `AddSingleton<RagIndexingPipeline>()` added to `AiModule.cs` (NOT Program.cs inline per ADR-010)

---

## Summary

`RagIndexingPipeline` has been registered in
`src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs`
inside the `AddAiModule()` extension method. No changes to `Program.cs` are required
beyond ensuring `AddAiModule(builder.Configuration)` is already called (done by AIPL-012).

---

## Registration Added to AiModule.cs

```csharp
// RagIndexingPipeline — concrete singleton per ADR-010 (AIPL-013).
// Orchestrates the full indexing pipeline: chunk → embed → index into both
// the knowledge index (512-token) and discovery index (1024-token).
// Called by RagIndexingJobHandler (task AIPL-014) via Service Bus.
// Requires: ITextChunkingService, IRagService, SearchIndexClient,
//           IOpenAiClient, IOptions<AiSearchOptions>.
services.AddSingleton<RagIndexingPipeline>();
```

This is the sixth non-framework singleton in `AiModule`. Total after AIPL-013: **94 registrations**.

---

## Prerequisites (Must Be Registered Before AddAiModule)

| Dependency | Where Registered | Notes |
|------------|-----------------|-------|
| `ITextChunkingService` | `Program.cs` | Registered via `TextChunkingService` singleton |
| `IRagService` | `Program.cs` | Registered when `DocumentIntelligence:Enabled = true` |
| `SearchIndexClient` | `Program.cs` | Registered when `DocumentIntelligence:AiSearchEndpoint/Key` are set |
| `IOpenAiClient` | `Program.cs` | Registered when analysis services are enabled |
| `IOptions<AiSearchOptions>` | `Program.cs` | Added by AIPL-004 via `Configure<AiSearchOptions>` |

---

## IndexingResult Naming Note

`Sprk.Bff.Api.Models.Ai.IndexingResult` (new, created by AIPL-013) conflicts with
`Azure.Search.Documents.Models.IndexingResult` (Azure SDK type).

Files that reference both types need a using alias:

```csharp
// In any file that imports both Sprk.Bff.Api.Models.Ai AND Azure.Search.Documents.Models:
using AzureSdkIndexingResult = Azure.Search.Documents.Models.IndexingResult;
using PipelineIndexingResult = Sprk.Bff.Api.Models.Ai.IndexingResult;
```

Files already fixed:
- `src/server/api/Sprk.Bff.Api/Services/Ai/RagIndexingPipeline.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/RagServiceTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/RagIndexingPipelineTests.cs`

If build failures appear in other files after this task, add the same alias.

---

## ADR-010 DI Count Impact

| Before AIPL-013 | Registrations Added | After AIPL-013 |
|-----------------|---------------------|----------------|
| 93              | +1 (RagIndexingPipeline) | 94 |

---

## Files Created / Modified by AIPL-013

| File | Change |
|------|--------|
| `src/server/api/Sprk.Bff.Api/Models/Ai/IndexingResult.cs` | **Created** — pipeline result record |
| `src/server/api/Sprk.Bff.Api/Services/Ai/RagIndexingPipeline.cs` | **Created** — indexing orchestrator |
| `src/server/api/Sprk.Bff.Api/Services/Ai/DocumentIntelligenceService.cs` | **Modified** — added MIME-type overload + improved docs |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs` | **Modified** — added `AddSingleton<RagIndexingPipeline>()` |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/RagIndexingPipelineTests.cs` | **Created** — 8 unit tests (all passing) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/RagServiceTests.cs` | **Modified** — added `AzureSdkIndexingResult` alias to fix ambiguity |
