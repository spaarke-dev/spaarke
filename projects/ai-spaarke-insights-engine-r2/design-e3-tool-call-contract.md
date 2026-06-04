# Design — E3 Tool-Call Contract (Spaarke Assistant ↔ Insights Engine)

> **Status**: AUTHORED. **PENDING Assistant team review** (Sub-task A.5 — owner-mediated).
> **Version**: 1.1 (Phase 1.5 read-only + SSE streaming + clickable citations)
> **Author**: Wave E task 042 (E3) — 2026-06-03; v1.1 amended Wave F task 053 (F4) — 2026-06-03
> **Phase**: Phase 1.5 read-only tool-call. v1.1 adds SSE streaming + `citations[].href`. Phase 2 bidirectional clarification + multi-turn state explicitly deferred.
> **Source POML**: `projects/ai-spaarke-insights-engine-r2/tasks/042-spaarke-assistant-integration.poml` (v1.0); `tasks/050-streaming-and-citation-spike.poml` + `tasks/051-sse-streaming-endpoint.poml` + `tasks/052-citations-href-projection.poml` + `tasks/053-contract-v1.1-docs.poml` (v1.1)
> **Source spec**: `projects/ai-spaarke-insights-engine-r2/spec.md` FR-05 + AC-1 + Risk-6
> **v1.1 spike**: `projects/ai-spaarke-insights-engine-r2/notes/spikes/wave-f-streaming-citation-spike.md` (binding for §3.5 + §4.6)

---

## 0. Pending review

This document is the canonical contract between the **Spaarke Insights Engine BFF** and the **Spaarke Assistant** project (separate workstream). It was authored unilaterally by task 042 sub-task A per POML constraint AC-1 ("contract not pre-existing"); sub-task A.5 (Assistant team review + sign-off) is owner-mediated and **NOT** complete. Treat the contract as binding for the BFF side and tentative for the Assistant side until the review record is recorded (see §10).

---

## 1. Goal

Spaarke Assistant invokes Insights as a callable tool. A user asking "what's the predicted cost of matter X?" in Assistant chat triggers an Insights tool-call that routes to the appropriate path:

- **Playbook path** — for queries that map cleanly to a pre-authored playbook (e.g., `predict-matter-cost@v1`). Returns a typed, evidence-grounded answer or a structured decline.
- **RAG path** — for open-ended natural-language queries that don't match a registered playbook. Returns ranked retrievals + LLM-synthesized summary with grounded `[n]` citations.

The routing decision is made by the BFF (using the Wave E2 intent classifier or an Assistant-supplied `forceMode` override). The Assistant sees ONE unified tool surface; it doesn't have to know which underlying path was taken.

**Phase 1.5 scope**: Read-only. The tool answers the Assistant's question and returns. No bidirectional clarification, no streaming, no multi-turn state on the BFF side. Phase 2 expands per §11.

---

## 2. Endpoint

### 2.1 URL + method

```
POST /api/insights/assistant/query
```

Hosted in `Sprk.Bff.Api` (Zone B endpoint placement, per §3.5 facade boundary). Discoverable via the BFF's OpenAPI document (`/swagger`) under the `Insights` tag.

**Design alternative considered**: Two separate sub-endpoints (`/assistant/ask` + `/assistant/search`) mirroring the existing `/ask` + `/search`. Rejected: forces the Assistant to embed routing logic that duplicates the intent classifier. The unified endpoint moves routing into the BFF where it belongs.

### 2.2 Auth

- `RequireAuthorization()` — any authenticated tenant user
- Bearer token (Entra ID JWT) per ADR-028 (Spaarke Auth v2)
- Handler reads `tid` (tenant) + `oid` (user) claims; missing claims → 401 ProblemDetails
- The Assistant MUST propagate the originating user's token via OBO. Service-principal / app-only tokens are NOT accepted (the underlying RAG layer applies AIPU2-027 privilege-group filtering off `CallerPrincipal`)

### 2.3 Rate limit

- `RequireRateLimiting("ai-context")` — 60 req/min sliding window per caller `oid` (per ADR-016)
- Matches the existing `/ask` + `/search` policy; aggregate budget across all three endpoints for the same `oid`
- 429 returns ProblemDetails with `Retry-After` header

### 2.4 Content-Type

- Request: `application/json`
- Response: `application/json` (success) / `application/problem+json` (errors, per ADR-019)

---

## 3. Request schema

```json
{
  "query": "string (required, 1..500 chars)",
  "subject": "string (required, scheme-prefixed)",
  "forceMode": "string (optional: 'playbook' | 'rag' | null)",
  "conversationContext": {
    "conversationId": "string (optional)",
    "previousTurnSummary": "string (optional, ≤2000 chars)"
  }
}
```

### 3.1 Field semantics

| Field | Required | Format | Purpose |
|---|---|---|---|
| `query` | Yes | NL string, 1..500 chars | The user's natural-language question to answer |
| `subject` | Yes | `<scheme>:<guid>` | Constrains retrieval / playbook resolution to the entity. Phase 1.5 schemes: `matter:`, `project:`, `invoice:` (catalog in `Insights:Subject:Schemes`) |
| `forceMode` | No | `"playbook"` \| `"rag"` \| null | Optional Assistant-supplied intent override. When omitted, BFF invokes the intent classifier. When set, bypasses the classifier entirely |
| `conversationContext.conversationId` | No | Opaque string | Carried in telemetry for Assistant-side correlation. NOT used by BFF for state |
| `conversationContext.previousTurnSummary` | No | NL string, ≤2000 chars | Phase 1.5 hint to classifier prompt; phase 2 may inform RAG augmentation. Today: telemetry only |

### 3.2 `forceMode` semantics

The Assistant SHOULD send `forceMode` when it has high-confidence intent signal (e.g., the user invoked a tool by name) — this avoids redundant LLM classification + reduces latency.

| `forceMode` | BFF behavior |
|---|---|
| `null` (omitted) | Invoke `IInsightsIntentClassifier.ClassifyAsync(query, ...)`. Use the returned path; if `BelowThreshold=true`, fall back to RAG per FR-05 safety |
| `"playbook"` | Skip classifier. Invoke `IInsightsAi.AnswerQuestionAsync` with a default-resolved playbook id (see §3.3) |
| `"rag"` | Skip classifier. Invoke `IInsightsAi.SearchAsync` directly |
| Anything else | 400 with `errorCode: forceMode.invalid` |

### 3.3 Playbook id resolution

When the routing decision lands on the playbook path:

1. **Classifier path**: the classifier returns a `PlaybookId` canonical name (e.g., `predict-matter-cost@v1`). BFF resolves via `InsightsPlaybookNameMapOptions.ResolveOrDefault(name)` → per-env Guid.
2. **`forceMode="playbook"` without classifier hint**: BFF reads `Insights:Playbooks:DefaultName` from configuration (defaults to `predict-matter-cost@v1`). If unresolvable → 503 with `errorCode: ai.assistant-default-playbook.unconfigured`.

The Assistant does NOT supply playbook ids in Phase 1.5. (If we later allow Assistant to pass `playbookHint` for narrow scoping, that's a Phase 2 schema extension.)

### 3.4 Subject scheme support

Same catalog as `/api/insights/search` (the Wave D5 `ISubjectParser`). Unknown schemes → 400 with `errorCode: subject.invalid`. Malformed Guid → 400 with `errorCode: subject.invalid`.

### 3.5 SSE streaming option (NEW in v1.1)

**Added 2026-06-03 (Wave F task 051)**. Client opts in to Server-Sent Events streaming via the HTTP `Accept` header. Single-shot JSON remains the default — back-compatible with v1.0 callers.

#### 3.5.1 Accept-header negotiation

| Request `Accept` | BFF response | Body shape |
|---|---|---|
| `application/json` OR absent OR `*/*` | 200 OK with `Content-Type: application/json` | Single-shot `AssistantQueryFacadeResult` per §4 (v1.0 shape — unchanged) |
| `text/event-stream` | 200 OK with `Content-Type: text/event-stream; charset=utf-8` | SSE stream of `AssistantQueryChunk` events terminated by `data: [DONE]\n\n` |
| `text/event-stream` + classifier OR compound-AI kill-switch OFF | 503 ProblemDetails (BEFORE the stream body opens) | `application/problem+json` per §5 |

Header-negotiation reference: F1 spike §A (`notes/spikes/wave-f-streaming-citation-spike.md`).

#### 3.5.2 Event types

Mirrors R5's `AnalysisChunk` shape so the R5 chat agent's existing SSE parser consumes the stream with no protocol divergence.

| `Type` | When emitted | Other fields |
|---|---|---|
| `progress` | Pipeline milestone (e.g., classifier completed, RAG search complete, playbook node completed, cache hit) | `Step?` (label), `Path?` (e.g., `"playbook"` after routing), `Content?` (count or short detail) |
| `delta` | RAG path only: incremental LLM synthesis token | `Path` (always `"answer"` in v1.1), `Content` (token chunk), `Sequence` (1-based ordering) |
| `result` | Final canonical v1.0 `AssistantQueryFacadeResult` (same shape as single-shot response) | `Result` (the full envelope) |
| `error` | Mid-stream error after stream body opened (kill-switch errors return 503 BEFORE the stream) | `Error` = `{ ErrorCode, Detail }` (ProblemDetails shape) |

#### 3.5.3 `AssistantQueryChunk` payload shape

```json
{
  "type": "progress | delta | result | error",
  "step":     "<string?, set on progress>",
  "path":     "<string?, set on delta or progress.path>",
  "content":  "<string?, set on delta token or progress detail>",
  "sequence": <int?, set on delta>,
  "result":   { /* AssistantQueryFacadeResult — set on result chunk */ },
  "error":    { "errorCode": "<string>", "detail": "<string>" }
}
```

Wire JSON keys are lowercase camelCase per the existing `JsonSerializerOptions` (`PropertyNamingPolicy.CamelCase`). The terminating sentinel is `data: [DONE]\n\n` (no JSON wrapper — string literal).

#### 3.5.4 Response headers (telemetry)

The same telemetry headers documented in §6.2 (`X-Insights-Path`, `X-Insights-Intent-Source`, `X-Insights-Elapsed-Ms`, `X-Insights-Cache`, `X-Insights-Hit-Count`) are written **BEFORE** the SSE body opens. Clients can read them after `HEADERS_RECEIVED` regardless of whether they consume the stream or abort.

#### 3.5.5 Path-specific event sequences

**RAG path** (`forceMode: "rag"` or classifier picks RAG):

```
event: progress    data: {"type":"progress","step":"classifier_started"}
event: progress    data: {"type":"progress","step":"classifier_complete","path":"rag"}
event: progress    data: {"type":"progress","step":"rag_search_started"}
event: progress    data: {"type":"progress","step":"rag_search_complete","content":"5"}
event: progress    data: {"type":"progress","step":"llm_synthesis_started"}
event: delta       data: {"type":"delta","path":"answer","content":"Three ","sequence":1}
event: delta       data: {"type":"delta","path":"answer","content":"risk ","sequence":2}
event: delta       data: {"type":"delta","path":"answer","content":"themes...","sequence":3}
event: result      data: {"type":"result","result":{ /* full AssistantQueryFacadeResult */ }}
data: [DONE]
```

**Playbook path** (`forceMode: "playbook"` or classifier picks playbook):

Coarse-grained `progress` events only (one per major node phase). No `delta` token streaming — playbook synthesis uses structured output that must be fully assembled. Rationale: F1 spike §D.

```
event: progress    data: {"type":"progress","step":"classifier_complete","path":"playbook"}
event: progress    data: {"type":"progress","step":"playbook_started","content":"predict-matter-cost@v1"}
event: progress    data: {"type":"progress","step":"node_complete","content":"resolveLiveFacts"}
event: progress    data: {"type":"progress","step":"node_complete","content":"retrieveCohortObservations"}
event: progress    data: {"type":"progress","step":"node_complete","content":"retrievePrecedents"}
event: progress    data: {"type":"progress","step":"node_complete","content":"checkSufficiency"}
event: progress    data: {"type":"progress","step":"node_complete","content":"synthesize"}
event: progress    data: {"type":"progress","step":"node_complete","content":"groundCitations"}
event: result      data: {"type":"result","result":{ /* full AssistantQueryFacadeResult */ }}
data: [DONE]
```

**Cache-hit short-circuit** (D-P13 cache `GetOrExecuteAsync` hit on playbook path):

```
event: progress    data: {"type":"progress","step":"cache_hit"}
event: result      data: {"type":"result","result":{ /* cached AssistantQueryFacadeResult */ }}
data: [DONE]
```

#### 3.5.6 Mid-stream errors

If the stream body has opened (headers + first chunk written) and a downstream error occurs (e.g., AOAI synthesis fails after partial token streaming):

```
event: error    data: {"type":"error","error":{"errorCode":"INSIGHTS_ASSISTANT_INTERNAL_ERROR","detail":"<sanitized>"}}
data: [DONE]
```

The connection is NOT abruptly closed — the `[DONE]` sentinel still flushes for clean client-side handling. Errors that arise BEFORE the stream body opens (kill-switch / auth / validation) return standard `application/problem+json` per §5 (no SSE body emitted).

#### 3.5.7 Kill-switch interaction

The kill-switch matrix in §7 applies to the streaming endpoint identically. When a kill-switch returns 503, the response is `application/problem+json` (NOT a `text/event-stream` body), regardless of the `Accept` header. The `FeatureDisabledException` is raised before any SSE body bytes are written.

---

## 4. Response schema (success — 200 OK)

```json
{
  "path": "string ('playbook' | 'rag')",
  "answer": "string (the user-facing answer)",
  "citations": [
    {
      "n": 1,
      "source": "string (document/observation display name)",
      "excerpt": "string (snippet, ≤280 chars)",
      "observationId": "string (optional GUID)",
      "chunkId": "string (chunk identifier)",
      "href": "string (optional, v1.1+) — preview URL or null"
    }
  ],
  "confidence": 0.0,
  "playbookId": "string (optional, present when path='playbook')",
  "structuredResult": {
    "kind": "string ('inference' | 'decline' | 'observation')",
    "envelope": { "...": "playbook-specific shape" }
  },
  "diagnostics": {
    "intentSource": "string ('classifier' | 'forceMode' | 'classifier-fallback')",
    "classifierBelowThreshold": false,
    "elapsedMs": 0,
    "cacheHit": false
  }
}
```

### 4.1 Field semantics by path

| Field | Playbook path | RAG path |
|---|---|---|
| `path` | `"playbook"` | `"rag"` |
| `answer` | Plain-text summary derived from the playbook's `InsightArtifact.Inference` (or the decline's `Explanation`) | LLM-synthesized grounded summary with `[n]` citation tokens |
| `citations` | Derived from the playbook's `InsightArtifact.EvidenceRefs` (when present) — bridged for Assistant uniformity | Derived from `InsightsSearchFacadeResult.Results` |
| `confidence` | `1 - decline.ConfidenceInDecline` on the decline path, else 1.0 | Top hit's relevance score |
| `playbookId` | Canonical name (e.g., `predict-matter-cost@v1`) | `null` |
| `structuredResult.kind` | `"inference"` (artifact path) \| `"decline"` (decline path) | `"observation"` |
| `structuredResult.envelope` | The full `InsightArtifact` JSON or `DeclineResponse` JSON | `{ "results": [...], "summary": "..." }` |
| `diagnostics.intentSource` | One of `classifier`, `forceMode`, `classifier-fallback` | Same |
| `diagnostics.classifierBelowThreshold` | True if classifier triggered RAG fallback | Same |

### 4.2 Citations are uniform

Both paths return citations with the same shape. The Assistant renders them identically. This is the load-bearing UX simplification of the unified contract: the Assistant displays `[1]`, `[2]`, ... clickable references regardless of which path executed.

**Playbook path bridging**: `InsightArtifact.EvidenceRefs` (when present per the artifact schema) projects 1:1 to `citations`. Playbooks without evidence refs (Phase 1.5 `predict-matter-cost@v1` does have them via the IndexRetrieveNode output) return an empty `citations: []` array — the Assistant SHOULD render a "no inline citations; see structuredResult.envelope" hint.

### 4.3 Empty-results semantics

The RAG path returns an empty `citations: []` and `answer: ""` when retrieval finds zero hits (no fabricated summaries, per Wave E1 / FR-04 contract). The Assistant SHOULD render "I couldn't find anything about this in your matter / project / invoice." rather than passing the empty answer to the user verbatim.

### 4.4 Decline path on playbook

The playbook path returns 200 OK with:

- `answer` = the decline's `Explanation`
- `confidence` = `1 - ConfidenceInDecline` (high confidence in decline → low confidence in answer)
- `structuredResult.kind = "decline"`
- `structuredResult.envelope` = the full `DeclineResponse` JSON (Reason, MinimumEvidenceNeeded, SuggestedActions, ConfidenceInDecline)

The Assistant SHOULD surface the SuggestedActions to the user (Phase 1.5 the SuggestedActions are pre-templated strings; Phase 2 they become actionable buttons).

### 4.5 Telemetry response headers

See §6.2 (unchanged from v1.0).

### 4.6 `citations[].href` optional field (NEW in v1.1)

**Added 2026-06-03 (Wave F task 052)**. Each citation MAY carry an `href` URL string pointing to a preview of the source document. Strictly additive — v1.0 clients ignore the field; v1.1 clients render it as a clickable iframe-target.

#### 4.6.1 Schema

| Field | Type | Required | Format |
|---|---|---|---|
| `citations[].href` | `string?` | No | Absolute URL OR `null`. JSON key is lowercase `href`. |

The field is `null` (or absent if the serializer is configured to drop nulls — for `Sprk.Bff.Api` it serializes as explicit `null`) when no resolvable preview target exists.

#### 4.6.2 URL pattern

```
{Insights:CitationHref:BffBaseUrl}/api/documents/{sprk_document-guid}/preview
```

Where:

- `{Insights:CitationHref:BffBaseUrl}` — per-environment BFF base URL configured via the `Insights:CitationHref` config section (e.g., `https://spaarke-bff-dev.azurewebsites.net`). Bound in `AnalysisServicesModule` via `IOptions<AssistantCitationHrefOptions>`.
- `{sprk_document-guid}` — Dataverse `sprk_document` row Guid.
- The endpoint is the existing `GET /api/documents/{documentId}/preview` (see `Api/FileAccessEndpoints.cs`).

#### 4.6.3 Per-path sourcing

| Path | Source of `sprk_document-guid` | Notes |
|---|---|---|
| **RAG** | `InsightsSearchHit.ObservationId` (which IS the sprk_document Guid per `RagSearchResult.DocumentId` contract — F1 spike §B) | Zero plumbing — Guid already in the v1.0 projection |
| **Playbook** | `EvidenceRef.Ref` when `EvidenceRef.RefType == "document"` AND `Ref` parses as a Guid (bare-Guid form) | `TryExtractDocumentIdFromEvidenceRef` helper in `AssistantToolCallHandler`; the `spe://drive/X/item/Y` form is deferred to v1.2 (see §11) |
| **Playbook** (non-document evidence: comparable-matter, fact-source, etc.) | n/a | `href = null` |
| **Orphan RAG chunks** (no `RagSearchResult.DocumentId`) | n/a | `href = null` |

#### 4.6.4 Authorization (AIPU2-027)

The `/api/documents/{id}/preview` endpoint enforces AIPU2-027 via the existing OBO flow: the user's bearer token is exchanged for a Graph+Dataverse OBO token; if the user lacks ACL on the underlying document, the endpoint returns 403/404 naturally. **No URL signing, no token embedding** — the citation href is opaque-from-client and authorization-checked at click-time on the `/preview` endpoint. This is the path-of-least-resistance privilege model recommended by the F1 spike §C.

Citations that are filtered out of search results by AIPU2-027 never reach the projection, so `href` is never constructed for them. The filter pass that gates the citation also gates the href — safe by construction.

#### 4.6.5 Required configuration

`Insights:CitationHref:BffBaseUrl` MUST be set per environment. When unset (or empty), the projection emits `href: null` for ALL citations (safe-fallback behavior — no broken URLs). Operators verify in the `/swagger` smoke check (see §A in the integration brief).

#### 4.6.6 Back-compat semantics

- v1.0 clients ignore the unknown field — no behavioral change.
- v1.1 clients render `href` when non-null (iframe target / clickable link) and fall back to display-name only when `null`.

---

## 5. Error model (ADR-019 ProblemDetails)

All errors return `application/problem+json` with:

```json
{
  "type": "https://errors.spaarke.com/<error-key>",
  "title": "<short title>",
  "status": <HTTP status>,
  "detail": "<human-readable detail>",
  "errorCode": "<stable error code>",
  "correlationId": "<HttpContext.TraceIdentifier>"
}
```

### 5.1 Error codes (binding)

| HTTP | errorCode | When |
|---|---|---|
| 400 | `query.required` | `query` is missing or whitespace |
| 400 | `subject.required` | `subject` is missing or whitespace |
| 400 | `subject.invalid` | Unknown scheme, malformed Guid, or other subject parse failure |
| 400 | `forceMode.invalid` | `forceMode` is set but not `"playbook"` or `"rag"` |
| 400 | `conversationContext.invalid` | `conversationContext.previousTurnSummary` > 2000 chars |
| 401 | (no errorCode — auth filter response) | Missing/invalid bearer token, missing `tid` claim, missing `oid` claim |
| 429 | (rate-limit middleware default) | `ai-context` policy exceeded |
| 503 | `ai.insights.disabled` | Analysis kill-switch OFF — playbook path unavailable |
| 503 | `ai.rag.disabled` | RAG kill-switch OFF — RAG path unavailable |
| 503 | `ai.intent-classification.disabled` | Intent classifier OFF AND `forceMode` not supplied (the Assistant MUST send `forceMode` when classifier is disabled) |
| 503 | `ai.assistant-default-playbook.unconfigured` | `forceMode=playbook` AND `Insights:Playbooks:DefaultName` resolves to empty Guid (deployment bug; ops escalation) |
| 500 | `INSIGHTS_ASSISTANT_INTERNAL_ERROR` | Unexpected internal failure; correlationId for log lookup |

### 5.2 Assistant-side handling guidance

| HTTP | Recommended Assistant action |
|---|---|
| 400 | Surface error to user as "I couldn't understand that request" — don't retry |
| 401 | Re-authenticate; if persists, surface "Your session expired" |
| 429 | Honor `Retry-After`; surface "Slow down a moment" |
| 503 with `ai.*.disabled` errorCode | Surface "Insights is temporarily disabled for your tenant" — log for ops; do NOT retry |
| 503 with `ai.assistant-default-playbook.unconfigured` | Surface generic error; log critical; pinged ops |
| 500 | Surface generic "Something went wrong"; retry ONCE with exponential backoff (1s, then give up) |

---

## 6. Telemetry headers (request + response)

### 6.1 Request headers Assistant SHOULD send

| Header | Required | Purpose |
|---|---|---|
| `Authorization: Bearer <jwt>` | Yes | OBO user token per ADR-028 |
| `Content-Type: application/json` | Yes | Per §2.4 |
| `x-correlation-id: <guid>` | Recommended | Propagate Assistant's request correlation; BFF uses for cross-service log correlation |
| `x-conversation-id: <opaque>` | Optional | Assistant's conversation identifier (may also appear in body's `conversationContext`) |

### 6.2 Response headers BFF returns

| Header | Always | Purpose |
|---|---|---|
| `X-Insights-Elapsed-Ms: N` | Yes | Total BFF wall time (handler-to-response) |
| `X-Insights-Path: playbook \| rag` | Yes | Indicates the path actually taken (mirrors response body `path`) |
| `X-Insights-Intent-Source: classifier \| forceMode \| classifier-fallback` | Yes | How routing was decided |
| `X-Insights-Cache: true \| false` | Yes | Playbook D-P13 cache outcome (RAG path always `false`) |
| `X-Insights-Hit-Count: N` | RAG path only | Number of RAG retrievals (RAG path) |
| `Retry-After: N` (seconds) | On 429 | Standard rate-limit header |
| `WWW-Authenticate` | On 401 | Standard auth challenge |

---

## 7. Kill-switch matrix (ADR-032)

The BFF has THREE independent kill-switches that affect the assistant endpoint:

| Switch | Default | Affects |
|---|---|---|
| `Analysis:Enabled` + `DocumentIntelligence:Enabled` (compound AI gate) | `true` | All AI; turning OFF makes BOTH paths return 503 |
| RAG sub-gate (AI Search keys configured) | `true` (when configured) | RAG path only |
| `Insights:IntentClassifier:Enabled` (fine-grain) | `true` | Classifier only |

### 7.1 Decision matrix

| Compound AI ON | RAG ON | Classifier ON | `forceMode` | Outcome |
|---|---|---|---|---|
| Yes | Yes | Yes | null | Normal — classifier picks path |
| Yes | Yes | Yes | `playbook` | Playbook path |
| Yes | Yes | Yes | `rag` | RAG path |
| Yes | Yes | No | null | **503** `ai.intent-classification.disabled` |
| Yes | Yes | No | `playbook` | Playbook path (classifier bypassed) |
| Yes | Yes | No | `rag` | RAG path (classifier bypassed) |
| Yes | No | Yes | null + classifier picks `rag` | **503** `ai.rag.disabled` |
| Yes | No | Yes | null + classifier picks `playbook` | Playbook path |
| Yes | No | * | `rag` | **503** `ai.rag.disabled` |
| Yes | No | * | `playbook` | Playbook path |
| No | * | * | * | **503** `ai.insights.disabled` (compound gate dominates) |

**Rationale**: kill-switches dominate `forceMode`. The Assistant cannot bypass a disabled feature.

---

## 8. Worked examples

### 8.1 Cost prediction (playbook path via classifier)

**Request**:
```json
POST /api/insights/assistant/query
Authorization: Bearer <user-jwt>
Content-Type: application/json
x-correlation-id: 7b3c4d5e-...

{
  "query": "What will this matter cost to complete?",
  "subject": "matter:11111111-2222-3333-4444-555555555555"
}
```

**Response (success)**:
```json
HTTP/1.1 200 OK
X-Insights-Elapsed-Ms: 1842
X-Insights-Path: playbook
X-Insights-Intent-Source: classifier
X-Insights-Cache: false

{
  "path": "playbook",
  "answer": "Based on 12 comparable matters, predicted cost is $280,000 (P50). Range $210k-$340k (P25-P75).",
  "citations": [
    { "n": 1, "source": "Acme APA.pdf", "excerpt": "Final cost: $282k", "observationId": "doc-A", "chunkId": "chunk-1" },
    { "n": 2, "source": "Beta APA.pdf", "excerpt": "Final cost: $271k", "observationId": "doc-B", "chunkId": "chunk-2" }
  ],
  "confidence": 1.0,
  "playbookId": "predict-matter-cost@v1",
  "structuredResult": {
    "kind": "inference",
    "envelope": { "Inference": { "P50": 280000, "P25": 210000, "P75": 340000 }, "Method": "...", "EvidenceRefs": [...] }
  },
  "diagnostics": {
    "intentSource": "classifier",
    "classifierBelowThreshold": false,
    "elapsedMs": 1842,
    "cacheHit": false
  }
}
```

### 8.2 Decline path (insufficient evidence)

**Request**: same shape as 8.1 but for a matter with no comparable cohort.

**Response**:
```json
HTTP/1.1 200 OK
X-Insights-Path: playbook
X-Insights-Cache: false

{
  "path": "playbook",
  "answer": "Insufficient evidence to predict cost — fewer than 3 comparable matters were found in the index.",
  "citations": [],
  "confidence": 0.05,
  "playbookId": "predict-matter-cost@v1",
  "structuredResult": {
    "kind": "decline",
    "envelope": {
      "Reason": "insufficient-evidence",
      "Explanation": "Insufficient evidence to predict cost — fewer than 3 comparable matters were found in the index.",
      "MinimumEvidenceNeeded": { "comparableMatters": 3 },
      "SuggestedActions": ["Add comparable historical matters to the index", "Retry once 3+ matters with closed cost are indexed"],
      "ConfidenceInDecline": 0.95
    }
  },
  "diagnostics": { "intentSource": "classifier", "classifierBelowThreshold": false, "elapsedMs": 921, "cacheHit": false }
}
```

### 8.3 Open-ended summary (RAG path via classifier)

**Request**:
```json
{
  "query": "Summarize the key risks in the latest deal documents",
  "subject": "matter:11111111-2222-3333-4444-555555555555"
}
```

**Response**:
```json
HTTP/1.1 200 OK
X-Insights-Path: rag
X-Insights-Hit-Count: 5
X-Insights-Intent-Source: classifier

{
  "path": "rag",
  "answer": "Three risk themes appear in the deal documents: (1) indemnity exposure on warranties [1][2], (2) governing-law ambiguity in cross-border clauses [3], and (3) earn-out timing risk [4][5].",
  "citations": [
    { "n": 1, "source": "Acme APA.pdf", "excerpt": "Indemnity capped at $5M...", "observationId": "doc-A", "chunkId": "chunk-A-7" },
    { "n": 2, "source": "Beta APA.pdf", "excerpt": "Indemnity uncapped on IP...", "observationId": "doc-B", "chunkId": "chunk-B-3" },
    { "n": 3, "source": "Acme APA.pdf", "excerpt": "Governing law: New York (deals over $10M)...", "observationId": "doc-A", "chunkId": "chunk-A-12" },
    { "n": 4, "source": "Acme APA.pdf", "excerpt": "Earn-out measured at Q4 close...", "observationId": "doc-A", "chunkId": "chunk-A-15" },
    { "n": 5, "source": "Beta APA.pdf", "excerpt": "Earn-out subject to revenue milestones...", "observationId": "doc-B", "chunkId": "chunk-B-8" }
  ],
  "confidence": 0.87,
  "playbookId": null,
  "structuredResult": {
    "kind": "observation",
    "envelope": { "results": [...], "summary": "Three risk themes..." }
  },
  "diagnostics": { "intentSource": "classifier", "classifierBelowThreshold": false, "elapsedMs": 612, "cacheHit": false }
}
```

### 8.4 ForceMode override (Assistant knows intent)

**Request**:
```json
{
  "query": "predict cost",
  "subject": "matter:11111111-2222-3333-4444-555555555555",
  "forceMode": "playbook"
}
```

**Response**: same shape as 8.1, but `diagnostics.intentSource = "forceMode"` and `X-Insights-Intent-Source: forceMode`. No classifier LLM call → faster.

### 8.5 Classifier kill-switch + no `forceMode` (503)

**Request**: omit `forceMode`. Classifier is OFF (operator disabled it).

**Response**:
```json
HTTP/1.1 503 Service Unavailable
Content-Type: application/problem+json

{
  "type": "https://errors.spaarke.com/feature-disabled",
  "title": "Service Unavailable",
  "status": 503,
  "detail": "Insights intent classification requires Insights feature enabled (Analysis:Enabled + DocumentIntelligence:Enabled + Insights:IntentClassifier:Enabled).",
  "errorCode": "ai.intent-classification.disabled",
  "correlationId": "0HN..."
}
```

The Assistant SHOULD retry with `forceMode: "playbook"` or `forceMode: "rag"` if it has any intent signal at all (e.g., the user invoked a named tool).

---

## 9. Non-functional contract

| NFR | Target | Phase 1.5 measurement |
|---|---|---|
| Latency (classifier path, P95) | <3s total | RAG path 800ms + classifier 300ms typical; cache hit 50ms |
| Latency (forceMode path, P95) | <2s total | Skips classifier round-trip |
| Throughput per tenant | 60 req/min per `oid` (rate-limit) | ai-context policy aggregate across `/ask` + `/search` + `/assistant/query` |
| Concurrency safety | Stateless — handler can be Singleton | Verified by xunit test isolation + no per-request state |
| Observability | Every call logged with `correlationId`, `path`, `intentSource`, `elapsedMs`, `tenantId`, `oid` | Structured logging per ADR-019 |
| Telemetry | OpenTelemetry compatible — request span + child spans for classifier + path execution | Existing AI telemetry; classifier instrumented per task 041 |

---

## 10. Review log

| Date | Reviewer | Status | Notes |
|---|---|---|---|
| 2026-06-03 | E3 sub-task A author | DRAFT | Initial draft per POML constraint AC-1 |
| 2026-06-03 | E3 sub-task B implementer | SELF-VERIFIED | BFF implementation matches contract (handler + endpoint + tests) |
| TBD | Spaarke Assistant team | PENDING | Sub-task A.5 owner-mediated |
| TBD | Spaarke Insights owner | PENDING | Final sign-off |

**A.5 deferral rationale**: This sub-task requires direct coordination with the Spaarke Assistant project workstream, which is outside the autonomous capability of the task 042 execution agent. The BFF-side implementation (sub-task B) proceeds in parallel under the assumption that the contract is correct; review feedback from A.5 will be applied as a follow-up change set (no source-of-truth conflict — this doc remains the canonical contract). See `notes/e3-assistant-team-handoff.md` for the Assistant team's questions/checklist.

---

## 11. Phase 2 deferrals (explicit)

The following are EXPLICITLY out of Phase 1.5 scope; documented here so the Assistant team has a forward-roadmap. Strikethrough indicates items shipped in v1.1.

| Capability | Phase 2 design notes |
|---|---|
| **Bidirectional clarification** | Insights asks Assistant for clarification when subject is ambiguous (e.g., "which matter? you have 3 matching"). Likely a 422 ProblemDetails with a `clarification` envelope the Assistant renders. |
| ~~**Streaming response**~~ → **shipped in v1.1** (§3.5) | ~~Server-Sent Events on the response body for long-running playbooks (>3s).~~ **Shipped in v1.1 (Wave F task 051)**: `Accept: text/event-stream` negotiation, `progress`/`delta`/`result`/`error` event types, `[DONE]` terminator. RAG path streams `answer` tokens; playbook path emits coarse `node_complete` progress. |
| **Multi-turn conversation state** | BFF persists conversation context across calls (not just telemetry pass-through). Today `conversationContext` is logged but not stored. |
| **Assistant-supplied playbook hint** | `playbookHint: "predict-matter-cost@v1"` field — Assistant scopes to a specific playbook without forcing intent. Phase 2 extension; backwards-compatible (Phase 1.5 ignores unknown fields). |
| **Cross-tenant federation** | Assistant queries Insights across multiple tenants in a multi-tenant SaaS deployment. Today single-tenant per D-52; Phase 2 + deployment topology change. |
| ~~**Observation citation as actionable href**~~ → **shipped in v1.1 (Full scope)** (§4.6) | ~~`citations[].action: { type: "open-document", documentId: "..." }` — Assistant renders clickable buttons.~~ **Shipped in v1.1 (Wave F task 052)**: `citations[].href` carries the preview-URL form (`{bffBaseUrl}/api/documents/{sprk_document-guid}/preview`). Full scope (both observation + document citations) per F1 spike §F binding decision (escape hatch NOT triggered — plumbing cost was Small). |
| **Playbook-path `spe://drive/X/item/Y` evidence-ref href resolution** (v1.2) | When `EvidenceRef.RefType == "document"` AND `Ref` is a `spe://drive/<driveId>/item/<itemId>` URI (not a bare sprk_document Guid), v1.1 emits `href: null`. v1.2 will add an async `driveId+itemId → sprk_document` lookup via `IGenericEntityService` (the `DataverseObservationMirror.ResolveDocumentIdAsync` pattern), making citation projection async. F1 spike empirically confirmed the `spe://` form is the dominant production emit pattern from `FilesIndexIngestDocumentSource.cs:166` BUT the playbook citations through `predict-matter-cost@v1` predominantly hit the bare-Guid path or non-document evidence — so the v1.1 `null` fallback covers the minority case. Promoting this to v1.2 was the explicit per-spike trade-off to keep v1.1 synchronous. |

---

## 12. Changelog

| Date | Version | Change | Source |
|---|---|---|---|
| 2026-06-03 | 1.0 | Initial contract authored as Wave E task 042 sub-task A. Single-shot `POST /api/insights/assistant/query` with playbook/RAG routing via classifier or `forceMode`; 12 error codes; AIPU2-027 privilege filtering on RAG path. | task 042 (E3) |
| 2026-06-03 | 1.1 | **v1.0 → v1.1: SSE streaming added** (§3.5 — `Accept: text/event-stream` negotiation + `progress`/`delta`/`result`/`error`/`[DONE]` event schema, mirroring R5's `AnalysisChunk` shape). **`citations[].href` added** (§4.6 — optional lowercase-`href` field; URL pattern `{Insights:CitationHref:BffBaseUrl}/api/documents/{sprk_document-guid}/preview`; AIPU2-027 enforced via OBO at the `/preview` endpoint). Phase 2 deferrals (§11) updated: SSE streaming + actionable citations href both marked shipped; **NEW v1.2 deferral**: playbook-path `spe://drive/X/item/Y` evidence-ref href resolution (currently emits `href: null` for that subset; requires async sprk_document lookup). **No breaking changes** — v1.0 clients see identical single-shot JSON behavior; unknown `href` field is silently ignored by v1.0 clients. | tasks 050 (F1 spike), 051 (F2 SSE), 052 (F3 href), 053 (F4 docs) |

---

## 13. Cross-references

- Wave E1 (RAG endpoint): `src/server/api/Sprk.Bff.Api/Api/Insights/InsightsSearchEndpoint.cs` + `IInsightsAi.SearchAsync`
- Wave E2 (classifier): `Services/Ai/Insights/Routing/IInsightsIntentClassifier.cs`
- Wave E4 (decision tree doc): `docs/guides/INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md` — uses the same path terminology as this contract
- Wave F1 (streaming + citation ID flow spike): `projects/ai-spaarke-insights-engine-r2/notes/spikes/wave-f-streaming-citation-spike.md` — binding source for §3.5 + §4.6
- Wave F2 (SSE): `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IInsightsAi.cs` (`AssistantQueryStreamAsync`); `Models/Ai/PublicContracts/AssistantQueryChunk.cs`
- Wave F3 (citation href): `Models/Ai/PublicContracts/AssistantQueryFacadeResult.cs` (`AssistantQueryCitation.Href`); `Configuration/AssistantCitationHrefOptions.cs`
- ADR-013-refined (AI architecture / facade boundary): `.claude/adr/ADR-013-ai-architecture.md`
- ADR-019 (ProblemDetails): `.claude/adr/ADR-019-problem-details.md`
- ADR-028 (Auth v2): `.claude/adr/ADR-028-spaarke-auth-architecture.md`
- ADR-032 (Null-Object kill-switch): `.claude/adr/ADR-032-bff-nullobject-kill-switch.md`
- spec.md FR-05 + AC-1 + Risk-6
- POML: `projects/ai-spaarke-insights-engine-r2/tasks/042-spaarke-assistant-integration.poml` (v1.0); `tasks/050-streaming-and-citation-spike.poml` + `tasks/051-sse-streaming-endpoint.poml` + `tasks/052-citations-href-projection.poml` + `tasks/053-contract-v1.1-docs.poml` (v1.1)

---

*Authored 2026-06-03 as Wave E task 042 sub-task A deliverable. v1.1 amendment 2026-06-03 as Wave F task 053 (F4) deliverable.*
