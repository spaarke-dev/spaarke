# Spaarke Linear AI Consumer Architecture

> **Version**: 1.0
> **Created**: 2026-07-02
> **Status**: Proposed (pending R7 W12 Doc Upload conversion as reference implementation)
> **Companion**: [SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md](SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md) (the Playbook Engine path)

## Purpose

Spaarke has TWO orthogonal execution paths for AI features. This document defines the **Linear AI Consumer** path — the simpler of the two, appropriate for straight-line workflows that resolve to a single LLM call with deterministic pre- and post-processing.

The other path — the **Playbook Engine** — is documented in [SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md](SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md). Both paths coexist; each consumer sits on exactly one.

## Historical context

Through R7 the platform aimed for "all AI runs through `PlaybookOrchestrationService`" as a unification goal. Wave 11 UAT exposed the cost of that goal for narrative consumers — a data-driven interpreter over a workflow that doesn't need interpretation adds latency, bug surface, and debug friction with no offsetting value. **Daily Briefing** was migrated to a code-defined narrator (Wave 11 POC → Wave 12) with ~10× less runtime code and 0 vs ~6 bug classes. Operator decision, 2026-06-30:

> "config-table-with-rules IS an interpreter"

R7 Wave 12 Doc Upload UAT reproduced the same class of failures — this time on a document-processing flow. The bug classes traced to: metadata resolution asymmetries, template context shape mismatches, generic-vs-typed field coercion, JSON escaping of substituted values, nested-configJson handling. Every one of those is an *interpreter tax* — none of them is essential to the flow's actual work. The linear AI consumer pattern eliminates this tax for flows that don't need branching / tool selection / fan-out.

## The classifier — Linear vs Playbook

The single question that assigns a consumer to a path:

> *Does the LLM (or the runtime) need to make control-flow decisions?*

If NO, the consumer is Linear: fixed graph, single LLM call, deterministic surrounding steps.
If YES, the consumer is Playbook: dynamic step selection, branching, fan-out, tool calling.

Detailed rubric:

| Trait | Linear | Playbook |
|---|---|---|
| Number of LLM calls per run | 1 | 0 to N |
| Branching / conditional path | None (or one early return) | Yes |
| Fan-out / iteration over collections | None | Yes |
| Tool calling / dynamic step selection | No | Yes |
| Streaming multi-token to client | Optional (SSE progress or final-only) | Yes (chat) |
| Author-facing tunable surface | The Action JPS (prompt, schema, model) | Same, plus playbook graph + node configs |
| Runtime shape | 4 primitives + 1 typed service class | Orchestrator + executors + template engine + dispatch |

**Consumers on the Linear path today** (target state after R7 W12 migration):

- Document Upload / Profile Document (Route 1 of `sdap-document-processing-architecture.md`)
- File Summarize (workspace file summarize wizard)
- Matter Prefill wizard
- Project Prefill wizard
- Work Assignment Prefill wizard
- Document Create Profile wizard
- Daily Briefing narration (Wave 11; independent per-channel pipeline)

**Consumers on the Playbook Engine path** (unchanged):

- Chat / conversational sessions
- Insight Engine (Insights)
- Other multi-node analysis flows added later

## Component model — the Linear library

Four small primitives plus a typed service class per consumer. No orchestrator. No template engine. No dispatch registry.

```
┌──────────────────────────────────────────────────────────────────────┐
│                     LINEAR AI CONSUMER STRUCTURE                      │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  Endpoint                                                            │
│    │                                                                 │
│    ▼                                                                 │
│  Consumer Service (typed, per consumer)                              │
│    │  composes 4 shared primitives:                                  │
│    │                                                                 │
│    ├─▶ IActionResolver                                               │
│    │     Resolves ConsumerType → sprk_analysisaction row             │
│    │     Uses IConsumerRoutingService + env-var fallback             │
│    │                                                                 │
│    ├─▶ IDocumentTextSource                                           │
│    │     Extracts text from IFormFile OR from a persisted document   │
│    │     Wraps AnalysisDocumentLoader + ITextExtractor + SPE download│
│    │                                                                 │
│    ├─▶ IActionRunner                                                 │
│    │     Given an Action + document text + optional overrides,       │
│    │     calls IOpenAiClient.GetStructuredCompletionRawAsync         │
│    │     Returns raw JsonElement of the AI's structured output       │
│    │                                                                 │
│    ├─▶ Typed persistence / effect services (per consumer)            │
│    │     For Doc Profile: IDocumentDataverseService + IJobEnqueue    │
│    │     For Prefills: return-to-client only                         │
│    │     For File Summarize: SSE emitter                             │
│    │                                                                 │
│    ▼                                                                 │
│  Typed Result DTO (per consumer)                                     │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

Every primitive is a Singleton service registered once. Each consumer service is Scoped (or Singleton if truly stateless). All boundaries are typed.

### Shared primitives — contract sketch

Actual signatures to be finalized during implementation; sketch here defines intent.

```csharp
public interface IActionResolver
{
    Task<AnalysisAction> ResolveAsync(string consumerType, CancellationToken ct);
    Task<AnalysisAction?> TryResolveAsync(string consumerType, CancellationToken ct);
}

public interface IDocumentTextSource
{
    Task<DocumentText> ExtractFromFileAsync(IFormFile file, CancellationToken ct);
    Task<DocumentText> ExtractFromDocumentIdAsync(Guid documentId, LinearRunContext ctx);
}

public interface IActionRunner
{
    Task<JsonElement> RunAsync(AnalysisAction action, DocumentText text, LinearRunContext ctx);
}

public sealed record LinearRunContext(
    HttpContext Http,
    string CorrelationId,
    Guid? UserId,
    string TenantId,
    CancellationToken CancellationToken);

public sealed record DocumentText(
    string Content,
    long CharCount,
    string? MimeType,
    Guid? DocumentId,
    string? DriveId,
    string? ItemId);
```

`AnalysisAction` is the existing DTO already used by `IScopeResolverService`.

### Consumer service pattern — reference shape

Every Linear consumer service follows this outline. Deviations should be explained inline as comments.

```csharp
public sealed class DocumentProfileService(
    IActionResolver actions,
    IActionRunner runner,
    IDocumentTextSource docText,
    IDocumentDataverseService documents,     // typed writes (existing service)
    IJobEnqueueService jobs,                 // RAG indexing enqueue (existing service)
    ILogger<DocumentProfileService> logger)
{
    public async Task<DocumentProfileResult> ExecuteAsync(
        Guid documentId, LinearRunContext ctx)
    {
        var action = await actions.ResolveAsync(ConsumerTypes.DocumentProfile, ctx.CancellationToken);
        var text   = await docText.ExtractFromDocumentIdAsync(documentId, ctx);
        var ai     = await runner.RunAsync(action, text, ctx);
        var fields = DocumentProfileFieldMap.FromAiOutput(ai);
        await documents.UpdateProfileAsync(documentId, fields, ctx.CancellationToken);
        await jobs.EnqueueRagIndexingAsync(documentId, ctx);
        return new DocumentProfileResult(documentId, fields);
    }
}
```

Notable properties:

- **No template rendering**. The AI's JSON output is deserialized into a typed intermediate (via a per-consumer `*.FromAiOutput` static factory), then mapped to Dataverse fields via typed properties. No Handlebars. No `{{X}}` strings anywhere.
- **No metadata queries at runtime**. Typed persistence services know their target entities at compile time.
- **No config JSON parsing**. The Action row's JPS supplies prompt + schema + model deployment; nothing else needs interpretation.
- **No dispatch registry**. The endpoint calls the service; the service calls its collaborators. Full path is visible in a stack trace.
- **Typed errors**. Exceptions surface with the consumer's own error semantics. No "node X failed" translations.
- **Unit-testable end-to-end**. Mocking 4-6 dependencies gives full coverage; no orchestrator startup, no fixtures for playbook graphs.

### Data model — what stays, what retires

For each Linear consumer:

| Dataverse row | Status | Purpose |
|---|---|---|
| `sprk_analysisaction` | **KEEP** | JPS prompt, output schema, model deployment. The maker-tunable surface. |
| `sprk_playbookconsumer` | **KEEP** | Consumer-type routing table. Points a ConsumerType at an Action code. |
| `sprk_analysisplaybook` | **RETIRE (per consumer)** | Playbook wrapper — dead data for Linear consumers |
| `sprk_playbooknode` | **RETIRE (per consumer)** | Playbook nodes — dead data for Linear consumers |

Retirement is a data-only operation performed after cutover. Rows are deactivated (not deleted) to preserve audit history.

For Playbook Engine consumers (Chat, Insights): all four row types remain load-bearing.

## Registered consumers today (post R7 W12 migration)

To be maintained as the source of truth for which path each consumer is on.

| Consumer | Path | Entry point | Reference doc |
|---|---|---|---|
| Document Upload / Profile Document | Linear | `POST /api/ai/analysis/execute` (Doc Profile playbook id) | This doc |
| File Summarize | Linear | `POST /api/workspace/files/summarize` (SSE) | This doc |
| Matter Prefill | Linear | `POST /api/workspace/matters/pre-fill` | This doc |
| Project Prefill | Linear | `POST /api/workspace/projects/pre-fill` | This doc |
| Work Assignment Prefill | Linear | `POST /api/workspace/matters/pre-fill` (aliased) | This doc |
| Document Create Profile | Linear | (endpoint TBD during migration) | This doc |
| Daily Briefing narration | Linear | `POST /api/ai/daily-briefing/render` | [SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md](SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md) |
| Chat / conversational sessions | Playbook | `POST /api/ai/chat/sessions/{id}/messages` | [SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md](SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md) |
| Insight Engine | Playbook | Insight Engine endpoints | [SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md](SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md) |

## Building a new Linear consumer

Six steps. Should take a couple of hours end-to-end, most of which is the typed field mapping.

1. **Define the consumer type constant** in `Services/Ai/PublicContracts/ConsumerTypes.cs`
2. **Add a `sprk_playbookconsumer` row** in Dataverse routing the ConsumerType at the Action code
3. **Ensure the `sprk_analysisaction` row exists** (JPS prompt, output schema, model deployment) — this is the maker-tunable surface
4. **Write the consumer service** — composes the four shared primitives + typed persistence
5. **Write a typed intermediate DTO** deserialized from the AI's structured output (usually mirrors the JPS output schema)
6. **Write endpoint route** — thin, delegates to the service; response DTO is the wire contract

Detailed step-by-step tutorial: [`docs/guides/BUILD-A-NEW-LINEAR-AI-CONSUMER.md`](../guides/BUILD-A-NEW-LINEAR-AI-CONSUMER.md) (to be authored after Doc Upload conversion ships as the reference implementation).

## Future: Playbook-to-code compilation

For Playbook Engine consumers (Chat, Insights) the data→code translation complexity that manifests as interpreter bugs will eventually surface there too. When it does, the strategy is to *compile* the playbook definition to a code-defined service class at build time — retaining the maker-facing tunable surface (Actions + routing) while eliminating the runtime interpreter. Not in R7 scope; captured for R8+.

## Failure modes — how to debug a Linear consumer

Because each consumer service is a plain method call graph, debugging is direct:

| Symptom | First place to look | Second |
|---|---|---|
| Client sees 500 | Service's logger — every failure is caught and logged with a correlation id | If exception rethrown, endpoint's `try/catch` logs it |
| Empty AI output | Log `IActionRunner.RunAsync` — the raw `JsonElement` is logged before deserialization | Compare Action's `sprk_outputschemajson` to the JSON received |
| Field not written | Typed persistence service's own log ("Updated X record" line) | Dataverse plugin trace if a plugin ran |
| Wrong prompt used | `IActionResolver` logs which Action row it resolved | `sprk_playbookconsumer` routing row content |

No template context shapes. No node output resolution graphs. No dispatch. Every layer has a stack frame and a log line.

## Testing pattern

Each consumer service takes 4-6 injected dependencies. Unit tests mock them; end-to-end coverage in one file.

```csharp
public class DocumentProfileServiceTests
{
    [Fact]
    public async Task Execute_HappyPath_WritesFieldsAndEnqueuesIndexing()
    {
        // Arrange — mock 5 collaborators
        // Act — call ExecuteAsync
        // Assert — Dataverse update called with correct payload, job enqueued
    }

    [Fact]
    public async Task Execute_ActionNotConfigured_ReturnsFailureResult() { ... }

    [Fact]
    public async Task Execute_LlmFails_PropagatesException() { ... }
}
```

Integration tests exist per consumer but the unit tests are the primary safety net. This is a real shift from the playbook engine tests which needed orchestrator + node registry + template fixtures for meaningful coverage.

## Coexistence guardrails (MUST NOT break)

While migrating consumers to the Linear path:

- **Chat sessions** — MUST continue to work via `PlaybookOrchestrationService` unchanged
- **Insight Engine** — MUST continue to work via `PlaybookOrchestrationService` unchanged
- **Daily Briefing narration** — MUST continue to work (it's already Linear-shaped; formal migration to shared primitives is a follow-on refactor)
- **`sprk_analysisaction` schema** — MUST NOT change. Both paths depend on this row structure.
- **`sprk_playbookconsumer` schema** — MUST NOT change. Both paths depend on this routing table.

## Related documents

- [SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md](SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md) — Playbook Engine path (chat, insights, and the reference two-layer LLM output pattern)
- [sdap-document-processing-architecture.md](sdap-document-processing-architecture.md) — Historical Doc Upload architecture (the code-defined pattern this doc formalizes as the target)
- [ai-guide-consumer-wiring.md](../guides/ai-guide-consumer-wiring.md) — How to author a `sprk_playbookconsumer` row + wire an Action
- [BUILD-A-NEW-NARRATIVE-OUTPUT-CONSUMER.md](../guides/BUILD-A-NEW-NARRATIVE-OUTPUT-CONSUMER.md) — Daily Briefing narrator tutorial (companion pattern for narrative-shaped Linear consumers)

## Change log

| Date | Change |
|---|---|
| 2026-07-02 | Initial version — Linear path formalized in response to R7 W12 Doc Upload interpreter-tax failures |
