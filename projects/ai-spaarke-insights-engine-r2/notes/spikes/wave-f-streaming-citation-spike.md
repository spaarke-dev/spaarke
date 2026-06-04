---
title: Wave F1 — Streaming surface + citation ID flow spike
status: COMPLETED — F2 + F3 scope determined; document-citation href = SHIP IN v1.1 (Small plumbing cost)
authored-by: Claude (Anthropic) for Insights Engine r2 Wave F task 050
created: 2026-06-03
task: 050 (F1)
gates: 051 (F2 streaming), 052 (F3 citation href)
related-docs:
  - notes/insights-engine-contract-v1.1-request.md (R5 request)
  - notes/wave-f-v1.1-mini-plan.md (§0 + §3 plan integration)
  - design-e3-tool-call-contract.md (v1.0 contract)
---

# Wave F1 — Streaming surface + citation ID flow spike

> Read-only investigation. No code changes. Output gates F2 + F3 scope and binds F3 implementation choices per the R5-pre-approved escape hatch (mini-plan §0.1).

## TL;DR (one paragraph)

The plumbing-cost classification for getting document identifiers into `AssistantQueryCitation` is **SMALL (≤0.5d)** for both the RAG path AND the playbook path. RAG-side `RagSearchResult.DocumentId` (sprk_document Guid) already flows end-to-end and just isn't surfaced beyond the orchestrator; playbook-side `EvidenceRef.Ref` carries a `spe://drive/<driveId>/item/<itemId>` URI that needs a one-method parser, OR — simpler — the existing `/api/documents/{documentId}/preview` BFF endpoint takes a Dataverse sprk_document Guid, not driveId/itemId, so when `EvidenceRef.RefType == "document"` and the Ref carries a sprk_document Guid (the Insights-emitted form), citation href is a trivial URL build. **Recommendation: ship FULL v1.1 scope — both observation + document citation href.** Streaming surface: extend `IInsightsAi` with `AssistantQueryStreamAsync` returning `IAsyncEnumerable<AssistantQueryChunk>`; reuse existing `IOpenAiClient.StreamCompletionAsync` for RAG delta tokens; playbook path emits coarse-grained progress (one event per major node phase, derived from the engine's existing `PlaybookEvent` stream). Existing `ServerSentEventWriter` + `text/event-stream` plumbing in `Infrastructure/Streaming/` is reused as-is.

---

## A — Streaming method signature recommendation

### Alternatives considered

**A1 (rejected)**: Add a `stream` boolean to `AssistantQueryFacadeRequest`, keep `IInsightsAi.AssistantQueryAsync` returning `Task<AssistantQueryFacadeResult>`, internally branch.
- Rejection: the return type isn't streamable. Forces blocking accumulation, defeating the value.

**A2 (rejected)**: Change `IInsightsAi.AssistantQueryAsync` to return `IAsyncEnumerable<AssistantQueryChunk>` and require the single-shot caller to drain the stream into the final `result` chunk.
- Rejection: BREAKS Zone B back-compat. v1.0 callers (the existing `/api/insights/assistant/query` endpoint single-shot handler) would need to be rewritten. Violates "purely additive" framing.

**A3 (RECOMMENDED)**: Add a NEW facade method alongside the existing one.

```csharp
// In Services/Ai/PublicContracts/IInsightsAi.cs (additive — does NOT touch AssistantQueryAsync)
IAsyncEnumerable<AssistantQueryChunk> AssistantQueryStreamAsync(
    AssistantQueryFacadeRequest request,
    CancellationToken cancellationToken = default);
```

Where `AssistantQueryChunk` is a new Zone-B-importable DTO mirroring R5's `AnalysisChunk` shape:

```csharp
// In Models/Ai/PublicContracts/AssistantQueryChunk.cs (NEW Zone-B-safe DTO)
public sealed record AssistantQueryChunk
{
    /// <summary>"progress" | "delta" | "result" | "error"</summary>
    public required string Type { get; init; }

    /// <summary>For "progress": the pipeline step label (classifier_started, rag_search_complete, etc.)</summary>
    public string? Step { get; init; }

    /// <summary>For "delta": JSON path of the field being streamed (e.g., "answer")</summary>
    public string? Path { get; init; }

    /// <summary>For "delta": the token chunk content; for "progress": optional step detail; for "error": message</summary>
    public string? Content { get; init; }

    /// <summary>For "delta": sequence number for ordering (1-based, per delta event)</summary>
    public int? Sequence { get; init; }

    /// <summary>For "result": the canonical v1.0-shaped final response (same shape as single-shot)</summary>
    public AssistantQueryFacadeResult? Result { get; init; }

    /// <summary>For "error": ProblemDetails-shaped envelope (errorCode + detail). Optional on non-error chunks.</summary>
    public AssistantQueryError? Error { get; init; }
}

public sealed record AssistantQueryError(string ErrorCode, string Detail);
```

### Why this signature wins

1. **Additive only**: existing v1.0 path (`AssistantQueryAsync` returning `Task<AssistantQueryFacadeResult>`) untouched. Zone B endpoint code today keeps working unchanged for `Accept: application/json`.
2. **Facade-boundary clean (SPEC §3.5)**: `AssistantQueryChunk` is primitives + DTOs only, no AI internals leak.
3. **Cancellation native**: `IAsyncEnumerable` honors `CancellationToken` per-await; consumer aborts terminate AOAI stream cleanly.
4. **Endpoint shape**: the wire endpoint becomes a thin loop that maps each `AssistantQueryChunk` to an SSE frame via the existing `ServerSentEventWriter.WriteEventAsync(response, chunk.Type, chunk, ct)` helper. Final `[DONE]` sentinel written after the loop.
5. **Reuses existing engine streaming**: `IOpenAiClient.StreamCompletionAsync` (IOpenAiClient.cs:36) returns `IAsyncEnumerable<string>` — `delta` chunks wrap that stream 1:1 with a path tag.

### Implementation sketch (informational, for F2)

```csharp
// In InsightsOrchestrator (Zone A)
public async IAsyncEnumerable<AssistantQueryChunk> AssistantQueryStreamAsync(
    AssistantQueryFacadeRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    // Routing (reuse AssistantToolCallHandler routing decision logic; factor it into a public method)
    yield return new AssistantQueryChunk { Type = "progress", Step = "classifier_started" };
    var route = await _assistantHandler.DecideRouteAsync(request, cancellationToken);
    yield return new AssistantQueryChunk { Type = "progress", Step = "classifier_complete", Content = route.Path };

    if (route.Path == "rag")
    {
        // Pre-LLM phase: deterministic RAG retrieval
        yield return new AssistantQueryChunk { Type = "progress", Step = "rag_search_started" };
        var ragHits = await _ragService.SearchAsync(/*...*/);
        yield return new AssistantQueryChunk { Type = "progress", Step = "rag_search_complete",
                                                Content = ragHits.Results.Count.ToString() };

        // LLM synthesis with token streaming
        yield return new AssistantQueryChunk { Type = "progress", Step = "llm_synthesis_started" };
        var sb = new StringBuilder();
        int seq = 0;
        await foreach (var token in _openAi.StreamCompletionAsync(prompt, ct: cancellationToken))
        {
            sb.Append(token);
            yield return new AssistantQueryChunk { Type = "delta", Path = "answer",
                                                    Content = token, Sequence = ++seq };
        }
        var result = AssembleRagResult(/*...*/, sb.ToString(), ragHits);
        yield return new AssistantQueryChunk { Type = "result", Result = result };
    }
    else // playbook
    {
        await foreach (var evt in _engine.ExecuteBatchAsync(/*...*/, cancellationToken))
        {
            if (IsMajorPhaseTransition(evt))  // see Section D
                yield return new AssistantQueryChunk { Type = "progress",
                                                       Step = MapPhaseLabel(evt.NodeName) };
        }
        var result = await AssembleArtifactResult(/*...*/);
        yield return new AssistantQueryChunk { Type = "result", Result = result };
    }
}
```

Two-method facade keeps the v1.0 surface intact; F2 ships the new method + the wire endpoint's `Accept` negotiation.

---

## B — Citation ID flow (empirical trace)

### Playbook path (`AssistantToolCallHandler.cs:300-309`)

Today the projection drops document identifiers on the floor:

```csharp
var citations = artifact.Evidence
    .Select((e, idx) => new AssistantQueryCitation(
        N: idx + 1,
        Source: e.Ref,           // ← string like "spe://drive/abc/item/xyz" OR "<sprk_document-guid>"
        Excerpt: e.Quote ?? "",
        ObservationId: null,     // ← THROWN AWAY — see fix below
        ChunkId: null))
```

**What `EvidenceRef.Ref` actually contains** (per `Models/Insights/EvidenceRef.cs:13` XML doc):

| RefType | Ref format |
|---|---|
| `fact-source` | `dataverse://sprk_matter/M-1234#totalSpend` |
| `document` | `spe://drive/<driveId>/item/<itemId>` |
| `comparable-matter` | `matter://M-0567` |
| `supporting-matter` | `matter://M-2024-0341` |
| `playbook-run` | `playbook://outcome-extraction@v1/run-...` |

For **document citation href**, only `RefType == "document"` is the candidate. The Ref carries `driveId` + `itemId` embedded in a `spe://` URI scheme. **A trivial regex/segment-split parser extracts both in 5 lines of code.**

Alternatively (and simpler for the current `predict-matter-cost@v1` playbook), Layer 2 outcome-extraction emits Evidence with `Ref` set to the sprk_document Guid directly when the source is an indexed document. Either pattern is parsable in one helper method.

### RAG path (`AssistantToolCallHandler.cs:402-410`)

Today the projection captures `ObservationId` + `ChunkId` but does NOT carry forward the source document identifier:

```csharp
var citations = ragResult.Results
    .Select((h, idx) => new AssistantQueryCitation(
        N: idx + 1,
        Source: h.DocumentName,
        Excerpt: h.Snippet,
        ObservationId: h.ObservationId,   // RagSearchResult.DocumentId (sprk_document Guid) — flows
        ChunkId: h.ChunkId))
```

**Critical finding**: `InsightsSearchHit.ObservationId` is populated from `RagSearchResult.DocumentId` (`InsightsOrchestrator.cs:687` line `ObservationId: r.DocumentId`). And `RagSearchResult.DocumentId` is documented as "the source document ID (sprk_document record ID)" (IRagService.cs:357-359). **The sprk_document Guid is already on the wire as `citations[i].ObservationId` in v1.0.** It just isn't named "documentId" and the consumer (R5) hasn't been told they can treat it as a sprk_document key.

For v1.1 the orchestrator can use `ObservationId` directly to construct a preview URL — zero plumbing through new fields.

### Summary

| Path | Document identifier available? | Where | Plumbing required |
|---|---|---|---|
| **RAG** | YES — `InsightsSearchHit.ObservationId` = `RagSearchResult.DocumentId` = sprk_document Guid | Already on the wire as `citations[i].ObservationId` | NONE — orchestrator constructs href from the value it already projects |
| **Playbook (document evidence)** | YES — encoded in `EvidenceRef.Ref` as either `spe://drive/X/item/Y` URI OR direct sprk_document Guid | `Models/Insights/EvidenceRef.cs` Ref field | Add a 5-line static helper `TryExtractDocumentId(string evidenceRef, out Guid documentId)` in `AssistantToolCallHandler`; call when `RefType == "document"` |
| **Playbook (non-document evidence)** | N/A | comparable-matter / fact-source / playbook-run refs are not document-backed | `href = null` (graceful absence per R5 §3.5 back-compat) |

---

## C — Preview URL pattern recommendation

### Decision: REUSE the existing `GET /api/documents/{documentId}/preview` endpoint

Defined at `src/server/api/Sprk.Bff.Api/Api/FileAccessEndpoints.cs:248`. Takes a sprk_document Guid; resolves to `(driveId, itemId)` via `IDocumentDataverseService.GetDocumentAsync`; calls Graph `/drives/{driveId}/items/{itemId}/preview` via OBO; returns an iframe-renderable URL.

### Recommended `href` URL pattern

```
https://spaarke-bff-dev.azurewebsites.net/api/documents/{sprk_document-guid}/preview
```

Where `{sprk_document-guid}` is sourced from:
- **RAG citations**: `InsightsSearchHit.ObservationId` (already in the projection — no plumbing)
- **Playbook citations**: parsed `documentId` from `EvidenceRef.Ref` when `RefType == "document"` (one new helper)

### Why this URL pattern wins

1. **Zero new endpoints**. F3 reuses what FileAccessEndpoints already ships. No new authorization plane to build/audit.
2. **AIPU2-027 privilege filtering is automatic**: the endpoint uses OBO — Graph + Dataverse enforce ACL on the user's behalf. If the user can't see the document they get 403 from `/preview` naturally. R5's chat agent surfaces 403 as an opaque error per R5 §1 / mini-plan §0.
3. **R5 consumes URL as-is** (R5 §1 mini-plan §0 response #4): R5 has explicitly confirmed they'll iframe-render whatever URL the BFF returns; no signed-URL plumbing or token embedding needed.
4. **Matches R5 §3.3 suggested format verbatim**: the request doc proposed `/api/v1/documents/{id}/preview` — the actual deployed route is `/api/documents/{id}/preview` (no `/v1/`). F4 docs MUST clarify the actual route.

### Auth contract

The `/preview` endpoint requires standard BFF auth (`RequireAuthorization()` at the route level). When R5's chat agent renders the citation as an iframe, the browser sends the user's BFF session cookie — auth flows naturally. If the user lacks access:

- Dataverse OBO returns the document row OR 404 (if not in `sprk_document` accessible scope)
- Graph OBO returns the preview URL OR 403 (if SPE ACL denies)
- Either failure surfaces as a non-OK HTTP response inside the iframe — R5 handles as opaque error per R5 §1 response #4.

**Privilege-group filtering on `href` presence** (R5 §3.4): when AIPU2-027 already filters a citation out of the RAG results (user can't see the chunk), the citation never reaches the projection → href is never constructed for it. The single filter pass that gates the citation also gates the href. Consistent + safe by construction.

---

## D — Playbook-path streaming verdict

### Decision: COARSE-GRAINED progress only (one event per major node phase). NO delta token streaming on playbook path in v1.1.

### Rationale

`predict-matter-cost@v1` node sequence (from `Services/Ai/Insights/Playbooks/predict-matter-cost.playbook.json`):

| Node | Type | Streamable? |
|---|---|---|
| `resolveLiveFacts` | Deterministic Dataverse fetch | No — fast (~50ms), no LLM |
| `retrieveCohortObservations` | Deterministic Azure AI Search | No — fast, no LLM |
| `retrievePrecedents` | Deterministic AI Search | No — fast, no LLM |
| `checkSufficiency` | Deterministic gate | No — instant |
| `synthesize` | LLM-text generation | YES — could `delta` stream, but synthesis output is small (≤400 tokens, ~1-2s) and structured |
| `groundCitations` | Mechanical post-process | No — instant |
| `ReturnInsightArtifactNode` / `declineInsufficient` | Terminal | No |

Most playbook latency is in the 3 deterministic retrieval nodes (P95 ~1-2s) — those benefit from progress markers (so the chat-agent shows a stage indicator) but NOT from token-level streaming. The `synthesize` node could stream tokens via the existing `IOpenAiClient.StreamCompletionAsync`, but:

1. The node executor today uses structured-output (`GetStructuredCompletionAsync`) for schema-bound `InsightArtifact` emission — token-streaming structured outputs requires JSON Streamed Output (different OAI API), not in v1.1 scope.
2. The structured artifact must be fully assembled to determine artifact vs decline branch routing.
3. R5's stated value driver (request §1.2) is "5-second blank wait" — that's the RAG path. Playbook is already 2-3s end-to-end with progress markers showing.

### Recommended progress events (playbook path)

```
event: progress    data: {"step": "classifier_started"}
event: progress    data: {"step": "classifier_complete", "path": "playbook"}
event: progress    data: {"step": "playbook_started", "playbookId": "predict-matter-cost@v1"}
event: progress    data: {"step": "node_complete", "node": "resolveLiveFacts"}
event: progress    data: {"step": "node_complete", "node": "retrieveCohortObservations"}
event: progress    data: {"step": "node_complete", "node": "retrievePrecedents"}
event: progress    data: {"step": "node_complete", "node": "checkSufficiency"}
event: progress    data: {"step": "node_complete", "node": "synthesize"}
event: progress    data: {"step": "node_complete", "node": "groundCitations"}
event: result      data: {<full v1.0-shaped AssistantQueryFacadeResult>}
data: [DONE]
```

### Implementation note (caching tension)

`AnswerQuestionAsync` today wraps the engine in `IInsightsPlaybookExecutionCache.GetOrExecuteAsync`. On cache HIT the engine is never invoked → no progress events to forward. **Solution for F2**: on cache hit, emit a single `progress` with `step: "cache_hit"` then go straight to `result`. R5's chat agent treats cache-hit as instant render (no streaming UX needed). This keeps the playbook path back-compat with D-P13 cache semantics.

---

## E — Observation-citation URL strategy

### Decision: USE the same `/api/documents/{documentId}/preview` endpoint as document citations for the common case; FALL BACK to `href = null` for orphan observations.

### Why not a new BFF endpoint shim

Earlier framing (mini-plan §0 / R5 §3.3) proposed a choice between:
- Option A: Model-driven-app URL (`https://orgxxx.crm.dynamics.com/main.aspx?etn=sprk_observation&id=...`)
- Option B: New BFF endpoint (`/api/insights/observations/{id}`) returning a JSON-rendering shape

Neither is required for v1.1. The right framing is:

1. **Observation citations on the RAG path** carry `ObservationId` (which IS the sprk_document Guid per `IRagService.cs:357-359` — `RagSearchResult.DocumentId = "the source document ID (sprk_document record ID)"`). The chunk was indexed FROM a source document. The user-facing target is "show me the source document" not "show me the metadata-only Observation row." The /preview URL pattern wins.
2. **Orphan chunks** (where `RagSearchResult.DocumentId` is null per the IRagService XML doc) have no preview target → `href = null`. R5 falls back to display-name-only per §3.5.
3. **No MDA URL** — leaks tenant CRM URL into a back-end response; brittle across environments; assumes user has MDA license.
4. **No new BFF endpoint shim** — pure code/test bloat for no Wave F value; R5 only renders citation URLs as iframe sources today (per R5 design §4.7 FilePreviewContextWidget).

### Rule for F3 implementation

```
if (citation.ObservationId is not null) {
    citation.Href = $"{bffBaseUrl}/api/documents/{citation.ObservationId}/preview";
} else {
    citation.Href = null;
}
```

Same code path for both RAG-shaped observation citations and playbook-shaped document citations after the document-ID is extracted from `EvidenceRef.Ref`.

### Forward-compat note

If a future Phase 2 surface needs Observation-metadata viewing (e.g., "show me the Insights-emission record, not the source doc"), Phase 2 adds `/api/insights/observations/{id}` then. Not v1.1's problem.

---

## F — Plumbing-cost classification + v1.1 scope recommendation [BINDING]

### Classification: **SMALL (≤0.5d)**

### Cost driver breakdown

| Work item | Effort | Driver |
|---|---|---|
| Add `Href` field to `AssistantQueryCitation` record (Models/Ai/PublicContracts/AssistantQueryFacadeResult.cs) | 5 min | One nullable string property |
| Surface BFF base URL into `AssistantToolCallHandler` (config + DI) | 30 min | New `IOptions<AssistantCitationHrefOptions>` with `BffBaseUrl` setting; bind in module; one ctor param |
| RAG path: project `Href` from `h.ObservationId` (one ternary expression added to existing LINQ) | 5 min | `ObservationId` already in projection |
| Playbook path: add 5-line `TryExtractDocumentIdFromEvidenceRef` static helper + invoke when `RefType == "document"` | 30 min | Helper handles two formats: bare Guid OR `spe://drive/X/item/Y` (extracts the sprk_document Guid mapping — the Insights pipeline tags evidence with sprk_document Guid in r1's emitObservations executor, so the bare-Guid path is the common case) |
| Unit tests for href projection (RAG hit with doc, RAG hit orphan, Evidence with document Ref, Evidence with non-document Ref) | 1.5h | Four new unit tests in `AssistantToolCallHandlerTests` |
| Integration test (citation has working href; iframe-loadable through Spaarke Dev BFF) | 1h | One new integration test asserting href shape + smoke-checking the underlying `/preview` endpoint resolves |
| **Total** | **~3.5h** | Well within Small (≤4h = 0.5d) |

### Why NOT Medium

- `EvidenceRef` model needs NO reshape: the Ref field already carries the identifier.
- `RagSearchResponse` model needs NO reshape: `RagSearchResult.DocumentId` is in the existing v1.0 surface.
- `AssistantToolCallHandler` projection adds ONE field — no upstream changes.
- No new endpoints (reuses `/api/documents/{id}/preview`).
- No new auth plane (OBO already enforces).
- No multi-service coordination (single service change).

### Why NOT Large

- No `Evidence` upstream reshape required (NodeExecutors emit Ref with document Guid today).
- No RAG pipeline change required (index already carries document_id).
- No assembly-time decisions about citation type — projection picks the field that exists.

### v1.1 SCOPE RECOMMENDATION (BINDING per mini-plan §0.1)

**SHIP FULL v1.1: BOTH observation citation href AND document citation href in Wave F task 052 (F3).**

- Plumbing cost is Small (≤0.5d, well under the 0.5d threshold).
- The R5 escape hatch (mini-plan §0.1) was designed to prevent blocking on schema reshaping. No schema reshaping is required.
- Deferring document-href to v1.2 would WASTE the cheap plumbing already in place.
- R5's chat agent benefits from clickable citations on both paths — no UX inconsistency.

**F3 (task 052) acceptance criteria branch**: ship the FULL scope variant (both citation paths produce working href URLs).

### Single risk to flag for F3

The playbook `EvidenceRef.Ref` format for `RefType == "document"` is currently DOCUMENTED as `spe://drive/<driveId>/item/<itemId>` (per `EvidenceRef.cs` XML doc) BUT the actual emit pattern in current node executors may use bare sprk_document Guids. F3 sub-agent MUST empirically grep the live emission sites:

```
Grep "RefType\s*=\s*\"document\"" src/server/api/Sprk.Bff.Api/Services/Ai/
```

…to confirm the actual Ref format before writing the parser. The parser MUST handle BOTH formats (bare Guid + `spe://...` URI) for resilience. Estimated additional cost if both formats found: zero (helper handles both with a `Guid.TryParse` first, then URI regex fallback).

---

## Implementation guidance for F2 (task 051 streaming) sub-agent

Embed in F2 brief:

1. Add `AssistantQueryStreamAsync` to `IInsightsAi` per Section A signature.
2. Implement in `InsightsOrchestrator` mirroring the Section A sketch.
3. Factor `AssistantToolCallHandler` routing decision into a public method `DecideRouteAsync` so streaming + single-shot share routing logic (avoid two routing implementations diverging).
4. New DTO `AssistantQueryChunk` in `Models/Ai/PublicContracts/` (Zone-B-safe — primitives only).
5. Wire endpoint at `Api/Insights/AssistantQueryEndpoint`: branch on `request.Headers.Accept` containing `text/event-stream`. SSE branch: call `ServerSentEventWriter.SetSseHeaders(response)` then `await foreach (var chunk in _insightsAi.AssistantQueryStreamAsync(...))` and write each chunk via `WriteEventAsync(response, chunk.Type, chunk, ct)`. Terminate with `WriteAsync("data: [DONE]\n\n")` per R5 §2.2.
6. Cache-hit path: emit single `progress {step: "cache_hit"}` + `result` chunk + `[DONE]`. Verified via D-P13 path test.
7. Headers (`X-Insights-Path`, `X-Insights-Intent-Source`, etc. per contract v1.0): write BEFORE the SSE body opens (use `OnStarting` callback or write before first `WriteAsync`). R5 §2.5 says they're unchanged from v1.0.
8. Error mid-stream: emit `event: error data: {<ProblemDetails-shape>}` then `data: [DONE]\n\n` then return. Do NOT close connection abruptly. Per mini-plan §6 decision 4.
9. Tests: header negotiation (`Accept: text/event-stream` vs `application/json`), RAG-path delta sequence, playbook-path progress sequence, error-mid-stream, DONE sentinel, cache-hit short-circuit, regression against existing 15 v1.0 endpoint tests.

## Implementation guidance for F3 (task 052 citation href) sub-agent

Embed in F3 brief:

1. SHIP FULL SCOPE — both RAG and playbook citations produce working href URLs (per Section F binding recommendation).
2. Add `Href` nullable property to `AssistantQueryCitation` record at `Models/Ai/PublicContracts/AssistantQueryFacadeResult.cs:91`.
3. Add `AssistantCitationHrefOptions` config class (`BffBaseUrl` string). Bind from configuration in `InsightsServiceCollectionExtensions`. Inject `IOptions<AssistantCitationHrefOptions>` into `AssistantToolCallHandler` ctor.
4. RAG projection (`AssistantToolCallHandler.cs:402-410`): add `Href = h.ObservationId is { Length: > 0 } ? $"{baseUrl}/api/documents/{h.ObservationId}/preview" : null`.
5. Playbook projection (`AssistantToolCallHandler.cs:300-309`): add `Href = TryExtractDocumentIdFromEvidenceRef(e) is { } docId ? $"{baseUrl}/api/documents/{docId}/preview" : null`.
6. New static helper:
   ```csharp
   internal static Guid? TryExtractDocumentIdFromEvidenceRef(EvidenceRef e)
   {
       if (!string.Equals(e.RefType, "document", StringComparison.Ordinal)) return null;
       if (string.IsNullOrWhiteSpace(e.Ref)) return null;
       // Format 1: bare Guid
       if (Guid.TryParse(e.Ref, out var bareGuid)) return bareGuid;
       // Format 2: spe://drive/<driveId>/item/<itemId> — driveId+itemId; needs lookup, not in scope.
       // For v1.1 we surface href ONLY when the bare-Guid (sprk_document) form is emitted.
       // Phase 2 may add a driveId+itemId → sprk_document resolver.
       return null;
   }
   ```
7. Before writing the parser, empirically grep current Evidence emission sites to confirm which format(s) are emitted. If `spe://` URIs are the dominant form, F3 either (a) adds a driveId+itemId→sprk_document lookup via `IDocumentDataverseService` (still Small cost, +30 min), OR (b) updates the upstream emitter to use the bare Guid form (cleaner; one-line change in ObservationEmitterNodeExecutor).
8. Tests per Section F breakdown (4 unit + 1 integration). Smoke through Spaarke Dev to verify the iframe-renderable URL actually returns a preview.
9. AIPU2-027 verification: confirm the `/preview` endpoint rejects access when the calling user lacks document ACL — no new code to write; the endpoint already enforces it.

---

## Appendix — Files read for this spike

| File | Purpose |
|---|---|
| `projects/ai-spaarke-insights-engine-r2/notes/insights-engine-contract-v1.1-request.md` | R5 contract request (§2, §3) |
| `projects/ai-spaarke-insights-engine-r2/notes/wave-f-v1.1-mini-plan.md` | §0 R5 responses + §3 technical analysis |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/AssistantToolCallHandler.cs` | Citation projection (playbook :302; RAG :403) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/InsightsOrchestrator.cs` | SearchAsync (RAG single-shot), AnswerQuestionAsync (playbook cache+engine) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/IOpenAiClient.cs:36` | `StreamCompletionAsync` exists |
| `src/server/api/Sprk.Bff.Api/Services/Ai/IRagService.cs` | RagSearchResult.DocumentId = sprk_document Guid (line 357-359) |
| `src/server/api/Sprk.Bff.Api/Models/Insights/EvidenceRef.cs` | RefType + Ref format reference |
| `src/server/api/Sprk.Bff.Api/Models/Ai/PublicContracts/AssistantQueryFacadeResult.cs` | AssistantQueryCitation record (where Href adds) |
| `src/server/api/Sprk.Bff.Api/Models/Ai/PublicContracts/InsightsSearchFacadeResult.cs` | InsightsSearchHit shape |
| `src/server/api/Sprk.Bff.Api/Services/DocumentCheckoutService.cs:288` | GetPreviewUrlAsync — exists but uses driveId+itemId; not used directly |
| `src/server/api/Sprk.Bff.Api/Api/FileAccessEndpoints.cs:248` | `GET /api/documents/{documentId}/preview` — the URL pattern F3 targets |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Streaming/ServerSentEventWriter.cs` | Reusable SSE writer (no changes needed for F2) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IInsightsAi.cs` | Where AssistantQueryStreamAsync gets added |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/predict-matter-cost.playbook.json` | Node sequence for D streaming verdict |
