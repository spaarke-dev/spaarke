# AI Services Assessment and Remediation Plan (Pragmatic, Non-Rewrite)

## Overview
This document summarizes the assessment and provides a pragmatic remediation plan for `Sprk.Bff.Api.Services.Ai` with emphasis on performance, resilience, maintainability, and efficiency. The approach favors incremental refactors over a full rewrite unless explicitly warranted.

## Relative Grade (Senior Review)
**Overall: B-**

**Strengths**
- Clear layering and interface-driven design.
- Consistent logging and telemetry hooks.
- Clean separation between orchestration, RAG, tool handlers, and export services.

**Primary Weaknesses**
- Concurrency and state handling risks in in-memory stores.
- Cache invalidation and configuration routing bugs.
- Several Phase 1 stubs still in the execution path.
- Duplicated helper logic increases drift risk.
- Some export paths are not functionally correct (content-type mismatch).

## Key Findings (High Impact)
1. **Concurrency / Memory Safety**
   - `_analysisStore` and `_versionCounters` are plain dictionaries without locking or eviction. This can corrupt state under load and cause memory leaks.
   - Files: `AnalysisOrchestrationService.cs`, `WorkingDocumentService.cs`.

2. **Cache Invalidation Bug**
   - `KnowledgeDeploymentService.SaveDeploymentConfigAsync` invalidates `_clientCache` by tenant ID, but the cache key is `{tenantId}:{model}:{indexName}`; old clients remain.
   - File: `KnowledgeDeploymentService.cs`.

3. **Semantic Search Always Enabled**
   - `RagService.BuildSearchOptions` sets `QueryType=Semantic` even when `UseSemanticRanking=false`, which can break indexes lacking semantic config.
   - File: `RagService.cs`.

4. **DOCX/PDF Save Mismatch**
   - `ConvertToFormat` returns Markdown bytes for DOCX/PDF while setting DOCX/PDF content types.
   - File: `AnalysisOrchestrationService.cs`.

5. **Non-ASCII Control Character**
   - The DOCX footer includes a control char (BEL, `\a`), likely unintended.
   - File: `Export/DocxExportService.cs`.

## Redundancy / Overly Verbose Code
- Token estimation logic duplicated across multiple classes (`text.Length/4`).
- Chunking logic duplicated in entity/ clause analyzers.
- HTML stripping and paragraph parsing duplicated across export services.
- `BuildUserPromptAsync` uses `await Task.CompletedTask;` (no real async work).
- Ensure `IAnalysisToolHandler` is not duplicated on disk (was observed twice during review output).

## Pragmatic Remediation Plan (Non-Rewrite)

### Phase 0 — Guardrails & Metrics (1–2 days)
- Add targeted latency and error telemetry around RAG, OpenAI calls, exports, and tool handlers.
- Define baseline SLOs: P95 latency, memory footprint, throughput.

### Phase 1 — Correctness + Resilience Blockers (highest priority)
- Make analysis state thread-safe and bounded (cache with TTL/size).
- Fix cache invalidation in `KnowledgeDeploymentService`.
- Respect `UseSemanticRanking` in RAG options.
- Correct DOCX/PDF save behavior: either block unsupported formats or generate actual files.

### Phase 2 — Low-Risk Performance Wins (1–2 weeks)
- Parallelize tool chunk processing with bounded concurrency.
- Improve RAG query quality (use user input or summarized doc instead of first 500 chars).
- Reduce repeated allocations during prompt building/chunking.

### Phase 3 — Maintainability Improvements (1–2 weeks)
- Centralize helper utilities: token estimation, chunking, HTML stripping, JSON cleanup.
- Remove no-op async patterns.
- Replace Phase 1 stubs with feature flags or explicit “not implemented” responses.

### Phase 4 — Scale & Reliability Hardening (2–4 weeks)
- Replace in-memory analysis store with persistent storage (Dataverse/Redis) for horizontal scale.
- Add retry/backoff policies where external calls are not resilient.
- Support large email attachments via Graph upload sessions or enforce max size with clear errors.

## Testing / Verification
- Add tests for RAG query building and semantic on/off behavior.
- Add tests for search client routing and cache invalidation.
- Add tests for tool JSON parsing and aggregation.
- Add smoke tests for export formats (DOCX, PDF, Email) with large payloads.

## Rewrite Consideration
A rewrite is **not warranted** at this stage. Incremental changes will address the most critical performance, resilience, and correctness issues without destabilizing the system.
