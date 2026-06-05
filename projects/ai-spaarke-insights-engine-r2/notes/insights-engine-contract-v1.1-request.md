---
title: Insights Engine — Contract v1.1 Request (SSE + clickable citations)
audience: ai-spaarke-insights-engine-r2 (BFF-side; follow-on to Wave E3 task 042)
requestor: spaarke-ai-platform-unification-r5 (R5)
requestor-contact: R5 lead via PR / coordination doc §8 changelog
status: AUTHORED — pending Insights team review + bandwidth confirmation
contract-base: v1.0 (POST /api/insights/assistant/query) shipped via PR #337 on 2026-06-03
contract-target: v1.1 (additive, back-compatible)
created: 2026-06-03
last-updated: 2026-06-03
related-docs:
  - notes/insights-engine-assistant-integration-brief.md (v1.0 contract spec)
  - notes/insights-r2-coordination.md (cross-project coordination)
  - design-e3-tool-call-contract.md (Insights project — canonical v1.0 contract)
trigger-phrases:
  - "implement insights v1.1 request"
  - "add SSE to insights assistant endpoint"
  - "add clickable citations to insights endpoint"
  - "review R5 contract change request"
  - "respond to R5 v1.1 request"
---

# Insights Engine — Contract v1.1 Request (SSE + clickable citations)

> **READ THIS FIRST** if you've been tasked with implementing R5's contract minor-version request. This document is self-contained: rationale, two requested additions, suggested API shape, acceptance criteria, back-compat plan, coordination notes. Both changes are **purely additive** to `POST /api/insights/assistant/query` v1.0 — no breaking changes.

---

## 0. TL;DR (one paragraph)

R5 (spaarke-ai-platform-unification-r5) is implementing the chat-agent consumer side of `POST /api/insights/assistant/query` per `notes/insights-engine-assistant-integration-brief.md`. During R5 design review (2026-06-03 late), the operator chose to **request a v1.1 minor-version** of the contract that adds (1) optional SSE streaming on the endpoint, and (2) optional `citations[].href` field on the response. Both are framed as **back-compatible additive changes**: v1.0 clients see no behavior change; v1.1-aware clients (R5's chat agent) opt into the new behavior via standard HTTP content negotiation. Rationale: R5 is already building structured-field SSE infrastructure (`FieldDelta` events in the `AnalysisChunk` protocol) for the Summarize tool; extending the Insights endpoint to use the same protocol gives the chat agent a uniform streaming UX across all AI tools (currently Summarize + Insights, future N+ tools), and clickable citations close a major trust gap when users want to verify cited sources. Effort estimate (Insights side): ~2-3 days for SSE + ~1 day for href = ~3-4 days total + coordination. Effort estimate (R5 side): ~1 day SSE consumption + ~0.5 day citation wiring = ~1.5 days total.

---

## 1. Context — why R5 is asking

### 1.1 R5's relevant scope (for context only)

R5 ships a chat-driven Summarize playbook with **ChatGPT/Claude-style token streaming** — the user uploads files in the Assistant pane, invokes `/summarize`, and watches the structured summary populate progressively in a new Workspace tab as the LLM emits tokens. To support this, R5 is extending the existing `AnalysisChunk` SSE protocol with a new `FieldDelta` event variant:

```csharp
record AnalysisChunk(
  string Type,        // existing: "progress" | "result" | "complete" | "error" | NEW: "delta"
  string? Content,
  bool Done,
  string? Summary,
  DocumentAnalysisResult? Result,
  string? Error,
  FieldDelta? Delta);  // NEW (nullable)

record FieldDelta(
  string Path,        // JSON path, e.g., "tldr", "fileHighlights[0].summary"
  string Content,     // token chunk for this field
  int Sequence);      // for ordering correctness
```

The R5 chat agent then renders BOTH the `summarize` tool AND the `insights.query` tool's responses in the same multi-tool chat surface. **Today (v1.0) the `insights.query` tool returns single-shot JSON**; R5 renders it via a static path. R5's question to the Insights team: can we unify the streaming UX so both tools feel the same?

### 1.2 Why this isn't just R5 wanting parity for parity's sake

Three operational reasons:

1. **RAG path latency**: when the Insights endpoint routes to RAG, total latency = classifier (~300ms) + RAG retrieval (~500ms-1s) + LLM synthesis (variable, 1-5s+). The LLM synthesis dominates user-perceived wait time and benefits significantly from token streaming. Single-shot rendering means a 5-second blank wait followed by a sudden chunk of text — exactly the UX problem ChatGPT/Claude solved years ago.

2. **Cross-tool UX consistency**: R5's chat agent will surface multiple AI tools over time (Summarize, Insights, future analyze/compare/extract/translate). If Summarize streams and Insights doesn't, the agent feels inconsistent. Users learn "some tools stream, others freeze" — that's worse than no streaming at all.

3. **Source verifiability** (citations[].href): RAG-path responses include citations to source documents. Today `citations[].source` is a display name only ("Acme APA.pdf"). Without clickable navigation, users can't verify the citation — defeating much of the RAG trust value. Most users will treat un-verifiable citations as decorative prose.

### 1.3 The integration brief explicitly anticipates this

From `notes/insights-engine-assistant-integration-brief.md` §6 (decisions R5 owns):

| # | Question | Default if R5 doesn't answer |
|---|---|---|
| 1 | Streaming (SSE) for Phase 1.5? | No SSE (single-shot only) |
| 4 | Citation display — display names enough? | `citations[].source` is a display name |

Both questions explicitly leave room for R5 to request additions. The brief §10 also documents the coordination protocol:

> "Schema change requests post-1.0: Insights team minor-versions the contract + provides back-compat plan"

This document is R5 exercising that protocol.

### 1.4 Why now, not Insights Phase 2

Insights team's stated plan defers SSE + actionable citations to Phase 2. Three reasons R5 is asking to pull these forward to v1.1 (modest additive changes) rather than wait for Phase 2:

- R5 is building the `FieldDelta` SSE infrastructure NOW. Pulling Insights forward leverages already-paid infrastructure cost.
- R5 ships in ~2-3 weeks. If Insights Phase 2 lands months later, R5 ships with inconsistent streaming UX for months.
- The two requested additions are genuinely small (~3-4 days combined) and explicitly additive — no risk of breaking v1.0 clients.

---

## 2. Request 1 — Optional SSE streaming on `POST /api/insights/assistant/query`

### 2.1 Summary

Add **optional SSE response mode** to `POST /api/insights/assistant/query`. Negotiation via the `Accept` request header:
- `Accept: application/json` (or absent) → existing v1.0 single-shot JSON response (no change)
- `Accept: text/event-stream` → new v1.1 SSE stream

### 2.2 Suggested SSE event protocol

Align with R5's `AnalysisChunk` + `FieldDelta` protocol so a single client-side stream reader handles both tools. Events emitted as SSE frames per the existing convention:

```
data: {"type": "progress", "step": "classifier_started"}\n\n
data: {"type": "progress", "step": "classifier_complete", "intentSource": "classifier", "path": "rag"}\n\n
data: {"type": "progress", "step": "rag_search_started"}\n\n
data: {"type": "progress", "step": "rag_search_complete", "hitCount": 3}\n\n
data: {"type": "progress", "step": "llm_synthesis_started"}\n\n
data: {"type": "delta", "path": "answer", "content": "The closing conditions", "sequence": 1}\n\n
data: {"type": "delta", "path": "answer", "content": " include [1] regulatory", "sequence": 2}\n\n
data: {"type": "delta", "path": "answer", "content": " approval...", "sequence": 3}\n\n
data: {"type": "result", "content": "<full JSON of v1.0 response shape>"}\n\n
data: [DONE]\n\n
```

### 2.3 Event type semantics

| Event type | Purpose | Required? | Notes |
|---|---|---|---|
| `progress` | Pipeline step transitions (classifier, RAG retrieval, LLM synthesis, etc.) | Optional but recommended | Lets client show a "thinking" indicator with stage detail |
| `delta` | Incremental content chunks tagged by JSON path | Required for streaming value | R5 client appends to a buffer keyed by path |
| `result` | Final complete response (same shape as v1.0 single-shot) | Required | Marks the canonical response; client uses for state finalization + restoration |
| `error` | Error during streaming | Required if errors are possible mid-stream | Stream terminates after `error` event |
| `[DONE]` sentinel | Stream end | Required | Standard SSE convention |

### 2.4 Which paths benefit from `delta` events

| Insights internal path | Streamable? | Recommendation |
|---|---|---|
| Classifier (Wave E2) | No | Emit `progress` only — classifier returns a small JSON payload |
| Playbook (predict-matter-cost@v1) | Marginally | Emit `progress` per node; `delta` only if the final synthesis step uses LLM-text generation. Most playbook output is deterministic node assembly. |
| **RAG path (LLM synthesis)** | **Yes — primary value** | Emit `delta` events for `answer` field as tokens stream from Azure OpenAI. This is where streaming UX matters most. |

For Phase 1.5 v1.1, **streaming the RAG-path `answer` field is the must-have**. Playbook-path streaming is nice-to-have but lower priority — most playbook latency is in deterministic nodes.

### 2.5 Headers (no change from v1.0)

`X-Insights-Path`, `X-Insights-Intent-Source`, `X-Insights-Elapsed-Ms`, `X-Insights-Cache`, `X-Insights-Hit-Count` all sent at response start (before SSE body starts). No change.

### 2.6 Back-compat (binding)

- v1.0 clients (no `Accept: text/event-stream` header) get the existing single-shot JSON response. Zero behavior change.
- v1.1 clients (R5's chat agent) opt in via header. Response body is SSE.
- v1.0 contract response shape is fully embedded in the final `result` event's `content` field — v1.1 clients can ignore intermediate `progress`/`delta` events and still get a valid v1.0-shaped response from the `result` event.

### 2.7 Acceptance criteria

- [ ] Endpoint accepts `Accept: text/event-stream` and responds with SSE; `Accept: application/json` (or absent) responds with single-shot (no change)
- [ ] SSE response emits `progress` events for at least: `classifier_started`, `classifier_complete`, `rag_search_started` (RAG path), `playbook_started` (playbook path), `llm_synthesis_started` (RAG path)
- [ ] SSE response emits `delta` events for RAG-path `answer` field as Azure OpenAI streams tokens
- [ ] SSE response emits one final `result` event with the same JSON shape as v1.0 single-shot
- [ ] SSE response terminates with `data: [DONE]\n\n`
- [ ] Existing v1.0 clients (e.g., `swagger` UI tests, any v1.0 smoke tests) continue to work unchanged
- [ ] New SSE-mode tests added covering: header negotiation, RAG-path delta sequence, playbook-path progress sequence, error-mid-stream, DONE sentinel
- [ ] Update `notes/insights-engine-assistant-integration-brief.md` §3 (request) + §4 (response) to document v1.1 SSE option
- [ ] Update `design-e3-tool-call-contract.md` to v1.1 with changelog entry

---

## 3. Request 2 — Optional `citations[].href` field

### 3.1 Summary

Add **optional `href` field** to each citation object. URL points to the source document/observation in a form R5 can open in the Context-pane file preview widget (`FilePreviewContextWidget`). Field is purely additive — v1.0 clients ignore unknown fields; v1.1-aware clients (R5) render citations as clickable.

### 3.2 Suggested response schema

```json
{
  "n": 1,
  "source": "string (existing — display name)",
  "excerpt": "string (existing — snippet, ≤280 chars)",
  "observationId": "string (existing — optional GUID)",
  "chunkId": "string (existing — chunk identifier)",
  "href": "string (NEW v1.1 — URL to open the source)"
}
```

### 3.3 URL construction guidance

The `href` value depends on the citation type:

| Citation source | Suggested `href` format |
|---|---|
| Document stored in SPE (file-backed citation) | URL that resolves to a file preview — e.g., `https://spaarke-bff-dev.azurewebsites.net/api/v1/documents/{documentId}/preview` (delegates to existing `SpeFileStore.GetFilePreviewUrlAsync`). R5's `FilePreviewContextWidget` (§4.7 of R5 design.md) consumes this URL via iframe rendering. |
| Observation record (Dataverse-backed citation) | URL to view the observation — either a model-driven-app URL (`https://orgxxx.crm.dynamics.com/main.aspx?etn=sprk_observation&id={observationId}&pagetype=entityrecord`) OR a BFF endpoint returning a JSON-rendering shape (`https://spaarke-bff-dev.azurewebsites.net/api/insights/observations/{observationId}`). Insights team picks based on what's simpler. |
| Inline RAG hit with no persistent source | `href: null` — R5 falls back to display-name-only rendering (back-compat behavior) |

### 3.4 Privilege filtering / authorization

`href` URLs MUST respect the AIPU2-027 privilege-group filtering already applied to the citation's underlying data. If the user can't see the source document/observation, the `href` should either:
- Point to a URL that itself enforces authorization (existing endpoints do this naturally — they return 403 if user lacks access), OR
- Be omitted (`null`) when the underlying source is private to a privilege group the calling user doesn't belong to

The contract MUST NOT leak URLs to sources the user can't access. Verification: same authorization layer that filters `citations[]` itself filters `citations[].href` consistently.

### 3.5 Back-compat (binding)

- v1.0 clients receive `citations[]` entries without `href` field (or with `href: null`) — no behavior change
- v1.1-aware clients (R5) check for `href` presence and render clickable citations when present; fall back to display-name-only rendering when absent or `null`
- Field is OPTIONAL in the schema — Insights team can omit it for citation types where URL construction is not yet implemented (e.g., observation records may take longer than document citations)

### 3.6 Acceptance criteria

- [ ] Response schema includes optional `href` field on each citation
- [ ] Document-backed citations (SPE files) receive a working `href` URL that R5 can render in an iframe via `FilePreviewContextWidget`
- [ ] Observation-backed citations either receive a working `href` URL OR omit the field gracefully
- [ ] `href` URLs respect AIPU2-027 privilege filtering — no URL leaked for sources the user can't access
- [ ] Existing v1.0 clients (which ignore unknown fields) continue to work unchanged
- [ ] New tests added covering: document citation with href, observation citation with/without href, citation where user lacks access (href absent or returns 403)
- [ ] Update `notes/insights-engine-assistant-integration-brief.md` §4 (response schema) to document `href` field
- [ ] Update `design-e3-tool-call-contract.md` to v1.1 with changelog entry

---

## 4. Combined v1.1 contract version bump

Both requests are bundled into a single contract version: **v1.0 → v1.1**.

Per the integration brief §10:
> "Schema change requests post-1.0: Insights team minor-versions the contract + provides back-compat plan"

R5's expectation:
- Insights team creates a v1.1 branch from master post-PR-#337-merge
- Both additions implemented + tested as a single PR
- `design-e3-tool-call-contract.md` updated with v1.1 changelog entry documenting both additions
- `notes/insights-engine-assistant-integration-brief.md` updated to reflect v1.1 endpoint behavior
- Deploy to Spaarke Dev for R5 smoke testing
- R5 then consumes v1.1 in its `insights.query` tool

---

## 5. What R5 will do on its side (to make integration smooth)

R5 has corresponding work to consume v1.1 cleanly. This is informational so the Insights team knows R5 isn't asking for one-sided effort:

| R5 work | Effort | Description |
|---|---|---|
| SSE consumption on `insights.query` tool path | ~1 day | Extend R5's `insights.query` HTTP client to opt into SSE via `Accept: text/event-stream`; reuse R5's `FieldDelta`-aware SSE parser (already built for Summarize); progressive rendering of `answer` field |
| Click-citation wiring to `FilePreviewContextWidget` | ~0.5 day | When `citations[].href` is present, render the citation as a clickable button; click dispatches `context.context_update` PaneEventBus event with the URL; `FilePreviewContextWidget` (R5 §4.7) opens the URL in the Context pane via iframe |
| Confidence badge (D5 from design review) | ~0.25 day | When response confidence < 0.6, render Fluent v9 `Badge` or `MessageBar` with "Low confidence — verify before relying" text. Pure client-side; no contract dependency. |

Total R5 effort: ~1.75 days. Included in R5 Phase 2 scope.

---

## 6. Coordination

| Item | Owner | When |
|---|---|---|
| Acknowledge request (or push back / negotiate) | Insights team lead | Reply to operator within ~1 business day of receiving this doc |
| Bandwidth confirmation | Insights team lead | Confirm engineering bandwidth for ~3-4 days of v1.1 work; if not available, propose alternative timing |
| Implementation | Insights team (Claude-driven preferred per Wave E precedent) | After acknowledgement; sequence as a follow-on task after task 090 wrap-up |
| Deploy to Spaarke Dev | Insights team | After PR merge to master |
| R5 smoke test against v1.1 | R5 (operator) | After Spaarke Dev deploy completes |
| Update integration brief | Insights team | Concurrent with v1.1 PR |
| Close coordination doc touchpoint §4.4 (SSE) + §4.6 (citations) | R5 (operator) | After v1.1 ships + R5 verification |

---

## 7. References

### 7.1 R5-side context
- `notes/insights-engine-assistant-integration-brief.md` — v1.0 contract spec (this request extends)
- `notes/insights-r2-coordination.md` — cross-project coordination doc + §8 changelog with v1.0 ship history
- `design.md` §4.12 — R5 work item for Insights tool integration
- `design.md` §8.2 — R5's contract-review decisions (D1 + D4 = this request)

### 7.2 Insights-side canonical artifacts
- `projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md` — v1.0 contract; receives v1.1 changelog
- `projects/ai-spaarke-insights-engine-r2/spec.md` — Phase 1.5 scope (v1.1 is a follow-on minor-version, not Phase 2)

### 7.3 Architecture references
- ADR-019 (ProblemDetails) — error format unchanged for v1.1
- ADR-028 (Spaarke Auth v2) — auth unchanged for v1.1
- ADR-032 (BFF Null-Object Kill-Switch) — kill-switches apply identically; v1.1 SSE returns 503 events when relevant kill-switches are OFF
- AIPU2-027 — privilege-group filtering on RAG search results (applies to `citations[].href` URL construction)

### 7.4 Existing infrastructure to leverage
- R5's `FieldDelta` event variant in `AnalysisChunk` SSE protocol (R5 design §4.3 — being built now)
- Existing SSE response pattern in `POST /api/workspace/files/summarize` (Insights team can mirror)
- Existing `SpeFileStore.GetFilePreviewUrlAsync` for SPE document preview URLs

---

## 8. Open questions for Insights team to answer

| # | Question | R5's preferred answer |
|---|---|---|
| 1 | Bandwidth available for ~3-4 days v1.1 work in the near-term (within 1-2 weeks of receiving this request)? | Yes preferred; if not, R5 ships Phase 2 with v1.0 consumption and adds v1.1 consumption in a follow-on |
| 2 | Any objection to the suggested SSE event protocol shape (§2.2) — particularly the `delta` event schema mirroring R5's `FieldDelta`? | No objection preferred (uniformity benefits both projects); alternatives welcome with rationale |
| 3 | Should `href` URLs use a BFF-managed redirect (more control, more code) or direct SPE / Dataverse URLs (simpler, less indirection)? | Insights team's preference — both work for R5 consumption |
| 4 | Should v1.1 ship in a single PR or split SSE + citations into two PRs? | Single PR preferred (one minor-version bump); Insights team decides based on review-load |
| 5 | Is there a deployment-time concern with v1.1 (e.g., feature flag for SSE mode in case of issues)? | Insights team decides; R5 will consume whatever the team ships |

---

*Authored 2026-06-03 by R5 Claude (Anthropic AI agent) on behalf of the spaarke-ai-platform-unification-r5 project. Mirror this file to the Insights project's notes/ folder if convenient. Insights team's response gets recorded in `notes/insights-r2-coordination.md` §8 (changelog) when accepted/declined/modified.*
