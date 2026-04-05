# AI & Document Intelligence Patterns Index

> **Domain**: AI Tool Framework, Document Analysis
> **Last Updated**: 2026-03-31
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When to Load
Load these patterns when implementing AI analysis features, document extraction, streaming endpoints, or Azure OpenAI integration.

## Available Patterns

| Pattern | Purpose |
|---------|---------|
| [streaming-endpoints.md](streaming-endpoints.md) | SSE streaming to clients |
| [text-extraction.md](text-extraction.md) | Multi-format document extraction |
| [analysis-scopes.md](analysis-scopes.md) | Actions, Skills, Knowledge prompt assembly |

## Canonical Source Files
`src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` · `Services/Ai/AnalysisOrchestrationService.cs` · `Services/Ai/AnalysisContextBuilder.cs` · `Services/Ai/OpenAiClient.cs` · `Services/Ai/TextExtractorService.cs` · `Services/Ai/ScopeResolverService.cs` · `Services/Ai/ITextExtractor.cs` · `Configuration/DocumentIntelligenceOptions.cs`
