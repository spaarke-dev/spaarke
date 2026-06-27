# AI & Document Intelligence Patterns Index

> **Domain**: AI Tool Framework, Document Analysis
> **Last Updated**: 2026-03-31
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When to Load
Load these patterns when implementing AI analysis features, document extraction, streaming endpoints, or Azure OpenAI integration.

## Available Patterns

| Pattern | Purpose | Last Reviewed | Status |
|---------|---------|---------------|--------|
| [streaming-endpoints.md](streaming-endpoints.md) | SSE streaming to clients | 2026-04-05 | Verified |
| [text-extraction.md](text-extraction.md) | Multi-format document extraction | 2026-04-05 | Verified |
| [analysis-scopes.md](analysis-scopes.md) | Actions, Skills, Knowledge prompt assembly | 2026-04-05 | Verified |
| [indexing-pipeline.md](indexing-pipeline.md) | RAG indexing contract: documentId, parentEntity, GUID case, observability | 2026-05-22 | Verified |
| [public-contracts-facade.md](public-contracts-facade.md) | Spaarke Public-Contracts Facade DI Fascia — facade + Null peer pattern for AI features | 2026-06-05 | Verified |
| [endpoint-di-symmetry.md](endpoint-di-symmetry.md) | Endpoint↔DI Registration Conditionality Symmetry Rule — banned anti-pattern + runtime detection | 2026-06-05 | Verified |
| [node-executor-authoring.md](node-executor-authoring.md) | Adding a new playbook node executor — Singleton+Scoped DI pattern, canvas↔server symmetry checklist | 2026-06-21 | Verified |

## Canonical Source Files
`src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` · `Services/Ai/AnalysisOrchestrationService.cs` · `Services/Ai/AnalysisContextBuilder.cs` · `Services/Ai/OpenAiClient.cs` · `Services/Ai/TextExtractorService.cs` · `Services/Ai/ScopeResolverService.cs` · `Services/Ai/ITextExtractor.cs` · `Configuration/DocumentIntelligenceOptions.cs`
