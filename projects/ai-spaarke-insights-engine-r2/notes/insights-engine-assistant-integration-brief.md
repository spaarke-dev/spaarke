---
title: Insights Engine — Assistant Integration Brief
audience: spaarke-ai-platform-unification-r5 (consuming side)
authoring-project: ai-spaarke-insights-engine-r2 (BFF-side; Wave E3 task 042 sub-task C)
status: AUTHORED — pending Assistant team contract review (sub-task A.5)
contract-version: 1.0 (Phase 1.5 read-only)
created: 2026-06-03
last-updated: 2026-06-03
related-docs:
  - notes/insights-r2-coordination.md (R5↔r2 coordination snapshot)
  - notes/ground-truth-spaarkeai-state.md (SpaarkeAI state baseline)
trigger-phrases:
  - "implement insights tool", "add insights to assistant", "wire insights query"
  - "what tool does the assistant call for insights"
  - "insights tool-call contract", "review insights contract"
---

# Insights Engine — Assistant Integration Brief

> **READ THIS FIRST** if your task is integrating Insights into the Spaarke Assistant. This document is self-contained: contract, request/response shapes, error codes, decisions to make, acceptance criteria. All cross-worktree references are mirrored inline.

---

## 0. TL;DR (one-paragraph)

The Spaarke Insights Engine (project `ai-spaarke-insights-engine-r2`, Phase 1.5) shipped a single BFF endpoint built specifically for the Assistant to call as a tool: `POST /api/insights/assistant/query`. The endpoint accepts a natural-language `query` + entity `subject` (matter/project/invoice) and returns a uniform response carrying either a structured playbook result OR RAG-style cited prose. The BFF makes the playbook-vs-RAG routing decision internally via an LLM intent classifier (or honors an optional `forceMode` override from the Assistant). The R5 project's job is everything Assistant-side: tool registration, subject resolution from chat context, HTTP client, two-path response renderer, error handling per the contract's stable `errorCode` matrix, and telemetry/correlation propagation. **Phase 1.5 is read-only and single-shot — no streaming, no bidirectional clarification, no multi-turn state on the BFF.**

---

## 1. What just shipped on the BFF side (Insights Engine r2 Wave E)

| Wave E task | Deliverable | Surface for R5? |
|---|---|---|
| 040 (E1) | `POST /api/insights/search` + `IInsightsAi.SearchAsync` facade + 19 tests | Internal to BFF — R5 does NOT call this directly. The Assistant endpoint wraps it. |
| 041 (E2) | `InsightsIntentClassifier` (gpt-4o-mini, JSON-schema-constrained) + ADR-032 P3 Null-Object + `forceMode` plumbing | Internal — R5 just sets `forceMode` on requests when intent is known. |
| **042 (E3)** | **`POST /api/insights/assistant/query`** + `AssistantToolCallHandler` + canonical contract + 15 integration tests | **YES — this is the R5 surface.** |
| 043 (E4) | Decision-tree guide (when playbook vs RAG) | Reference reading for R5's UX team. |

PR #337 (Wave E) is currently in CI auto-merge. After merge, the endpoint is live on `https://spaarke-bff-dev.azurewebsites.net/api/insights/assistant/query` once the next BFF deploy runs.

**What R5 does NOT need to build** (already done in BFF):

- ❌ Intent classifier (runs server-side)
- ❌ Playbook vs RAG routing decision
- ❌ Authentication / OBO exchange (uses existing `@spaarke/auth`)
- ❌ Rate limiting (BFF enforces; R5 honors 429)
- ❌ Knowledge retrieval / search / synthesis
- ❌ Grounding / citation generation
- ❌ Subject parsing into Dataverse lookups
- ❌ Kill-switch handling (BFF returns 503s; R5 just renders them)

---

## 2. Endpoint summary

```
POST https://spaarke-bff-dev.azurewebsites.net/api/insights/assistant/query
Authorization: Bearer <user OBO JWT>
Content-Type: application/json
x-correlation-id: <guid>   # recommended
```

- **Auth**: `RequireAuthorization()` — Entra ID JWT per [ADR-028 Spaarke Auth v2](#a-references). Handler reads `tid` + `oid` claims; missing claims → 401 ProblemDetails. Service-principal / app-only tokens are NOT accepted (RAG layer applies AIPU2-027 privilege-group filtering off `CallerPrincipal`).
- **Rate limit**: `ai-context` policy, 60 req/min sliding window per `oid` (aggregate budget across `/ask` + `/search` + `/assistant/query`).
- **Content-Type**: Request `application/json`; response `application/json` (success) / `application/problem+json` (errors per [ADR-019](#a-references)).
- **Discoverability**: `/swagger` → `Insights` tag → `InsightsAssistantQuery`.

---

## 3. Request schema (binding contract v1.0)

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
| `query` | **Yes** | NL string, 1..500 chars | The user's natural-language question to answer |
| `subject` | **Yes** | `<scheme>:<guid>` | Constrains retrieval / playbook resolution to the entity. Phase 1.5 schemes: `matter:`, `project:`, `invoice:` |
| `forceMode` | No | `"playbook"` \| `"rag"` \| null | Optional Assistant-supplied intent override. When omitted, BFF invokes the intent classifier. When set, bypasses the classifier entirely |
| `conversationContext.conversationId` | No | Opaque string | Carried in telemetry for Assistant-side correlation. NOT used by BFF for state |
| `conversationContext.previousTurnSummary` | No | NL string, ≤2000 chars | Phase 1.5: telemetry only. Phase 2 may inform classifier prompt or RAG augmentation |

### 3.2 `forceMode` semantics

The Assistant SHOULD send `forceMode` when it has high-confidence intent signal (e.g., user invoked a tool by name). This avoids redundant LLM classification + reduces latency by ~300ms.

| `forceMode` value | BFF behavior |
|---|---|
| `null` / omitted | Invoke intent classifier. If `BelowThreshold=true`, fall back to RAG per FR-05 safety |
| `"playbook"` | Skip classifier. Resolve playbook via `Insights:Playbooks:DefaultName` config (default `predict-matter-cost@v1`) |
| `"rag"` | Skip classifier. Invoke RAG directly |
| Anything else | 400 with `errorCode: forceMode.invalid` |

**Important**: `forceMode` does NOT bypass kill-switches. If RAG is disabled, `forceMode: "rag"` still returns 503.

### 3.3 Subject parsing

Same catalog as `/api/insights/search`. Unknown schemes or malformed Guids → 400 with `errorCode: subject.invalid`. The Assistant MUST resolve the current entity from chat context (e.g., active matter ID from HostContext) and format it as `matter:<guid>` / `project:<guid>` / `invoice:<guid>` before calling.

### 3.4 Sample request

```json
{
  "query": "What will this matter cost to complete?",
  "subject": "matter:da116923-d65a-f111-a825-3833c5d9bcb1",
  "forceMode": null,
  "conversationContext": {
    "conversationId": "assistant-conv-987",
    "previousTurnSummary": "User asked about scope of the APA."
  }
}
```

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
      "chunkId": "string (chunk identifier)"
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
| `answer` | Plain-text summary derived from playbook's `InsightArtifact.Inference` (or decline's `Explanation`) | LLM-synthesized grounded summary with `[n]` citation tokens |
| `citations` | Derived from `InsightArtifact.EvidenceRefs` (when present) | Derived from RAG search results |
| `confidence` | `1 - decline.ConfidenceInDecline` on decline; else 1.0 | Top hit's relevance score (0..1) |
| `playbookId` | Canonical name (e.g., `predict-matter-cost@v1`) | `null` |
| `structuredResult.kind` | `"inference"` (artifact) \| `"decline"` (gate-fail) | `"observation"` |
| `structuredResult.envelope` | Full `InsightArtifact` OR `DeclineResponse` JSON | `{ "results": [...], "summary": "..." }` |

### 4.2 Response headers (always present)

```
X-Insights-Path: playbook | rag
X-Insights-Intent-Source: classifier | forceMode | classifier-fallback
X-Insights-Elapsed-Ms: <N>
X-Insights-Cache: true | false
X-Insights-Hit-Count: <N>
```

### 4.3 Citations are uniform across paths

Both paths return citations with the **same shape**. The Assistant renders them identically. This is the load-bearing UX simplification of the unified contract — render `[1]`, `[2]`, ... clickable references regardless of which path executed.

### 4.4 Empty-results semantics (anti-hallucination guarantee)

The RAG path returns `citations: []` and `answer: ""` when retrieval finds zero hits (no fabricated summaries per FR-04 contract). The Assistant MUST render a "couldn't find anything" hint rather than passing the empty `answer` to the user verbatim.

### 4.5 Decline path (playbook returned a structured no)

The playbook path returns 200 OK (not an error) when the playbook's evidence-sufficiency gate fails:

- `answer` = decline's `Explanation`
- `confidence` = `1 - ConfidenceInDecline` (high decline confidence → low answer confidence)
- `structuredResult.kind = "decline"`
- `structuredResult.envelope` = full `DeclineResponse` JSON (`Reason`, `MinimumEvidenceNeeded`, `SuggestedActions`, `ConfidenceInDecline`)

The Assistant SHOULD surface `SuggestedActions` to the user. Phase 1.5 they're pre-templated strings; Phase 2 they may become actionable buttons (see §6 open question 3).

### 4.6 Sample success responses

**Playbook (inference)**:
```json
{
  "path": "playbook",
  "answer": "Predicted cost ~$280k based on 12 similar matters with comparable scope.",
  "citations": [
    { "n": 1, "source": "Acme APA.pdf", "excerpt": "Estimated cost: $282k", "observationId": "doc-A", "chunkId": "chunk-1" }
  ],
  "confidence": 0.92,
  "playbookId": "predict-matter-cost@v1",
  "structuredResult": {
    "kind": "inference",
    "envelope": { "predictedCost": 280000, "currency": "USD", "comparableMatters": 12, "...": "..." }
  },
  "diagnostics": { "intentSource": "classifier", "classifierBelowThreshold": false, "elapsedMs": 1842, "cacheHit": false }
}
```

**RAG**:
```json
{
  "path": "rag",
  "answer": "The closing conditions include [1] regulatory approval, [2] a tail-policy update, and [3] employee retention agreements.",
  "citations": [
    { "n": 1, "source": "Closing Memo v3.docx", "excerpt": "Closing subject to regulatory approval...", "observationId": "doc-B", "chunkId": "chunk-2" },
    { "n": 2, "source": "Closing Memo v3.docx", "excerpt": "Seller to update tail policy...", "observationId": "doc-B", "chunkId": "chunk-5" },
    { "n": 3, "source": "Acme APA.pdf", "excerpt": "Key employees to sign retention agreements...", "observationId": "doc-A", "chunkId": "chunk-9" }
  ],
  "confidence": 0.81,
  "playbookId": null,
  "structuredResult": { "kind": "observation", "envelope": { "results": [/* ... */], "summary": "..." } },
  "diagnostics": { "intentSource": "forceMode", "classifierBelowThreshold": false, "elapsedMs": 943, "cacheHit": true }
}
```

---

## 5. Error model (binding — ADR-019 ProblemDetails)

All errors return `application/problem+json`:

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

### 5.1 Error code matrix

| HTTP | `errorCode` | When | Recommended Assistant action |
|---|---|---|---|
| 400 | `query.required` | `query` missing/whitespace | Surface "I couldn't understand the question". Do NOT retry. |
| 400 | `subject.required` | `subject` missing/whitespace | Same as above; likely Assistant wiring bug. |
| 400 | `subject.invalid` | Unknown scheme, malformed Guid | Check `subject` format is `<scheme>:<guid>` where scheme is `matter`/`project`/`invoice`. |
| 400 | `forceMode.invalid` | `forceMode` not `"playbook"`/`"rag"`/null | Assistant bug — fix the value. |
| 400 | `conversationContext.invalid` | `previousTurnSummary` > 2000 chars | Truncate and retry. |
| 401 | (no `errorCode`; auth challenge) | Missing/invalid token, missing `tid`/`oid` | Re-authenticate; if persists, "Your session expired". |
| 429 | (rate-limit default) | `ai-context` budget (60/min/oid) exceeded | Honor `Retry-After`; surface "Slow down a moment". |
| 503 | `ai.insights.disabled` | Compound AI kill-switch OFF | "Insights temporarily disabled". **Do NOT retry.** |
| 503 | `ai.rag.disabled` | RAG sub-gate OFF AND chosen path is RAG | If `forceMode: "playbook"`, retry would work. Else same as above. |
| 503 | `ai.intent-classification.disabled` | Classifier OFF AND no `forceMode` | **Retry with `forceMode`** if Assistant has intent signal. Else generic "temporarily disabled". |
| 503 | `ai.assistant-default-playbook.unconfigured` | `forceMode=playbook` AND `Insights:Playbooks:DefaultName` empty | Deployment bug. Surface generic error; **page ops**; do NOT retry. |
| 500 | `INSIGHTS_ASSISTANT_INTERNAL_ERROR` | Unexpected internal failure | Retry ONCE with 1s backoff. On second 500: surface error + log. |

### 5.2 Errors NEVER include

- Document content
- Prompt text
- LLM raw output
- Stack traces

Stripped per [ADR-018](#a-references) (no information leakage) + [ADR-019](#a-references) (ProblemDetails). The `correlationId` field is the ops/support log-lookup key.

---

## 6. Decisions R5 owns (open questions from contract review)

These are NOT yet decided. R5's contract review MUST record answers in §10 of `design-e3-tool-call-contract.md` (BFF-side artifact). The BFF can make small forward-compat changes if R5's answers require them.

| # | Question | Default if R5 doesn't answer | R5 decision needed |
|---|---|---|---|
| 1 | **Streaming (SSE)** for Phase 1.5? | No SSE (single-shot only) | Need it for any UX flow where latency > 3s degrades experience? |
| 2 | **`forceMode: "playbook"` without hint** — will R5 ever send this without knowing the playbook? | BFF resolves to configured default (`predict-matter-cost@v1`) | If yes, request `playbookHint` field added to Phase 1.5 schema |
| 3 | **Decline rendering** — plain strings or action verbs? | Plain strings in `SuggestedActions` | If actionable buttons needed, request schema change |
| 4 | **Citation display** — display names enough? | `citations[].source` is a display name (e.g., "Acme APA.pdf") | If clickable URLs needed, request `href`/`url` field |
| 5 | **Confidence floor** for UI disclaimer? | None | Pick a threshold (e.g., `< 0.6` → "low-confidence" badge) |
| 6 | **`previousTurnSummary`** — pass to classifier in Phase 1.5? | Logged only | If R5 has turn summarization + wants classifier context, BFF can wire small change |

---

## 7. R5's scope of work (binding)

### 7.1 Required (Phase 1.5 acceptance bar)

| # | Work item | Layer |
|---|---|---|
| 1 | Register Insights as a callable tool in Assistant's tool/skill registry under a stable name (suggested: `insights.query`) | Tool registry |
| 2 | Subject resolution from chat context → format as `<scheme>:<guid>` | Context resolver |
| 3 | HTTP client using existing `@spaarke/auth` `useAuth()` + `authenticatedFetch` (no custom auth) | Transport |
| 4 | Response renderer — TWO paths: playbook (structured envelope) + RAG (citation-grounded prose with `[n]` tokens) | UI |
| 5 | Error handling for ALL 12 error codes in §5.1; per-code user messaging from §5.1 column 4 | UI / orchestration |
| 6 | `x-correlation-id` generation per Assistant turn + propagation | Telemetry |
| 7 | `forceMode` set when user explicitly invoked a named tool/skill; omitted otherwise | Orchestration |
| 8 | Render empty-results case (RAG returns `answer: ""` + `citations: []`) with appropriate hint | UI |
| 9 | Render decline case (playbook returns `structuredResult.kind = "decline"` with 200 OK) showing `SuggestedActions` | UI |

### 7.2 NOT in R5 scope (Phase 2 deferrals)

Phase 1.5 contract is **forward-compatible** — new optional response fields R5 ignores will be silently accepted in Phase 2.

| Feature | Phase 1.5 | Phase 2 |
|---|---|---|
| Tool invocation | ✅ Read-only | ✅ Read-only |
| Bidirectional clarification | ❌ | ✅ BFF returns 422 with `clarification` envelope |
| SSE streaming | ❌ | ✅ For long-running playbooks (>3s) |
| Multi-turn conversation state on BFF | ❌ Telemetry only | ✅ BFF persists context |
| `playbookHint` from Assistant | ❌ Not in schema | ✅ Optional field |
| Actionable citations (`citations[].action`) | ❌ Display-only | ✅ |
| Cross-tenant federation | ❌ Single-tenant | ✅ |

---

## 8. Acceptance criteria for "R5 Insights integration done"

- [ ] Contract review (§10 of `design-e3-tool-call-contract.md`) signed off by R5 lead — this closes Insights Engine task 042 sub-task A.5
- [ ] Tool registered in Assistant's tool/skill registry under stable name
- [ ] Both response paths render correctly: playbook (structured envelope) + RAG (citation-grounded prose)
- [ ] All 12 error codes from §5.1 handled with appropriate user messaging — no raw stack traces, no document content leaking
- [ ] `x-correlation-id` propagated end-to-end (verifiable via App Insights / Kusto log correlation)
- [ ] `forceMode` correctly set when user invokes named tools; omitted otherwise
- [ ] Empty-results case + decline case both render with appropriate hints
- [ ] Smoke test passes against Spaarke Dev BFF using one of these Wave D7 synthetic test entities:
  - Matter: `da116923-d65a-f111-a825-3833c5d9bcb1`
  - Project: `27845394-8e5f-f111-a825-70a8a59455f4`
  - Invoice: `05c8ef8d-8e5f-f111-a825-70a8a59455f4`
- [ ] UX walkthrough with ≥1 legal-ops SME on Spaarke Dev tenant — 5 realistic questions per practice area (CTRNS contracts/transactional, IPPAT IP/patents, BNKF banking/finance) — responses usable

---

## 9. Sample curl (Spaarke Dev)

```bash
# Playbook path (forceMode)
curl -X POST https://spaarke-bff-dev.azurewebsites.net/api/insights/assistant/query \
  -H "Authorization: Bearer <user-jwt>" \
  -H "Content-Type: application/json" \
  -H "x-correlation-id: r5-smoke-001" \
  -d '{
    "query": "What is the predicted cost of this matter?",
    "subject": "matter:da116923-d65a-f111-a825-3833c5d9bcb1",
    "forceMode": "playbook"
  }'

# RAG path (classifier)
curl -X POST https://spaarke-bff-dev.azurewebsites.net/api/insights/assistant/query \
  -H "Authorization: Bearer <user-jwt>" \
  -H "Content-Type: application/json" \
  -H "x-correlation-id: r5-smoke-002" \
  -d '{
    "query": "What closing conditions are in this matter?",
    "subject": "matter:da116923-d65a-f111-a825-3833c5d9bcb1"
  }'

# Subject scheme error
curl -X POST https://spaarke-bff-dev.azurewebsites.net/api/insights/assistant/query \
  -H "Authorization: Bearer <user-jwt>" \
  -H "Content-Type: application/json" \
  -d '{
    "query": "test",
    "subject": "unknown:not-a-guid"
  }'
# → 400 { "errorCode": "subject.invalid", ... }
```

---

## 10. Coordination protocol with Insights Engine project

| Channel | Use for |
|---|---|
| Contract review session (BLOCKING) | First-sprint sync — R5 lead reviews `design-e3-tool-call-contract.md`, records sign-off + answers to §6 questions in the contract's §10 review log |
| Wrong response shape / contract violation | File against `projects/ai-spaarke-insights-engine-r2/` |
| Wrong rendering / tool registration / subject parsing | File against R5 (yours) |
| Wrong answer content / hallucinated citations | File against Insights Engine; provide `correlationId` for log lookup |
| Auth / OBO issues | File against Spaarke Auth v2 ([ADR-028](#a-references)) — likely a token claim missing |
| Schema change requests post-1.0 | Insights team minor-versions the contract + provides back-compat plan |

**No new BFF / Azure config required on R5 side.** All deployment knobs live in Insights Engine's `Sprk.Bff.Api` config.

---

## 11. Operational notes

- **Deployment timing**: Endpoint goes live the moment PR #337 (Wave E) merges to master + the BFF redeploy runs. Verify on Spaarke Dev: `GET /swagger` → `Insights` tag → `InsightsAssistantQuery`.
- **Kill-switches that affect R5**:
  - `Compound:AI:Enabled=false` → all 3 Insights endpoints return 503 `ai.insights.disabled`
  - `Insights:Rag:Enabled=false` → RAG path 503 `ai.rag.disabled`
  - `Insights:IntentClassifier:Enabled=false` → classifier path 503 `ai.intent-classification.disabled`; `forceMode` requests still work
- **Aggregate rate budget**: 60/min/oid across `/ask` + `/search` + `/assistant/query`. R5 SHOULD treat all three as one budget.
- **Idempotency**: All requests are idempotent (read-only Phase 1.5). Same `(query, subject, forceMode, oid, tid)` is cache-key-stable on playbook path (5-min TTL).
- **Auth scope**: User JWT MUST include `tid` + `oid` claims. BFF rejects missing claims with 401 ProblemDetails.
- **Local testing**: Hit BFF locally with real Entra ID test user token — no special "Assistant tester" mode needed. Same OBO flow as PCFs and Code Pages.

---

## A. References

### A.1 Canonical artifacts (Insights Engine r2 project — read on master after PR #337 merge)

| Artifact | Path (post-merge to master) | What it contains |
|---|---|---|
| Contract v1.0 | `projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md` | Full canonical contract; §10 is the review log R5 records sign-off in |
| Operational handoff | `projects/ai-spaarke-insights-engine-r2/notes/handoffs/e3-assistant-team-handoff.md` | Operator guide; §8 is the open-questions list |
| Decision-tree guide | `docs/guides/INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md` | Background — when playbook vs RAG applies; 15 worked examples across CTRNS/IPPAT/BNKF |
| Insights spec | `projects/ai-spaarke-insights-engine-r2/spec.md` | FR-04, FR-05, SC-04, SC-05; Phase 1.5 acceptance bar |
| R5 ↔ r2 coordination | `notes/insights-r2-coordination.md` (this project) | Earlier coordination snapshot — predates Wave E ship |

### A.2 ADRs (Spaarke architecture)

| ADR | Title | Relevance to R5 integration |
|---|---|---|
| ADR-008 | Endpoint-filter authorization | Endpoint uses `RequireAuthorization()`; R5 sends OBO bearer token |
| ADR-013 (refined 2026-05-20) | AI architecture / §3.5 facade boundary | Endpoint is Zone B; R5 consumes contract only |
| ADR-016 | Rate limiting | `ai-context` policy; R5 honors `Retry-After` on 429 |
| ADR-018 | Information-leakage prevention | Errors never leak content/prompts/stack traces |
| ADR-019 | ProblemDetails error format | All errors are `application/problem+json` with `errorCode` extension |
| ADR-028 | Spaarke Auth v2 | Function-based contract; R5 uses `@spaarke/auth` `useAuth()` + `authenticatedFetch` |
| ADR-029 | BFF publish hygiene | (Internal to Insights Engine; not R5-facing) |
| ADR-032 | BFF Null-Object Kill-Switch Pattern | Source of the 503 `ai.*.disabled` error codes |

### A.3 Key code paths (BFF — for debugging only; do NOT couple to)

| Component | Path | Purpose |
|---|---|---|
| Endpoint | `src/server/api/Sprk.Bff.Api/Api/Insights/InsightsAssistantEndpoint.cs` | Zone B endpoint binding |
| Handler | `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/AssistantToolCallHandler.cs` | Zone A dispatch logic (classifier or forceMode) |
| Facade | `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IInsightsAi.cs` | `AssistantQueryAsync` method |
| Classifier | `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Routing/InsightsIntentClassifier.cs` | LLM-based intent classification |
| Wire DTOs | `src/server/api/Sprk.Bff.Api/Models/Insights/InsightsAssistantQueryRequest.cs` | Zone B request shape |

---

## B. Quick sanity checks

If something doesn't work, check these first:

1. **404** → endpoint not deployed yet (PR #337 not merged or BFF not redeployed). Check `/swagger`.
2. **401 with no errorCode** → token missing or `tid`/`oid` claim missing. Check the user JWT.
3. **400 `subject.invalid`** → R5 sent a subject that didn't match `<scheme>:<guid>` where scheme is `matter`/`project`/`invoice`.
4. **503 `ai.intent-classification.disabled`** + no `forceMode` → ops disabled classifier; R5 should send `forceMode` based on best-guess intent.
5. **Empty `citations` + empty `answer` on RAG path** → working as designed (no hits found). R5 SHOULD render a "couldn't find anything" hint, NOT show the empty answer.
6. **Empty `citations` on playbook path** → playbook doesn't expose `EvidenceRefs`. Render `structuredResult.envelope` directly + show a "no inline citations; see details" hint.
7. **`structuredResult.kind = "decline"` with 200 OK** → playbook returned a structured no. Render `answer` (the explanation) + surface `envelope.SuggestedActions`.

---

*Authored 2026-06-03 by Insights Engine r2 Wave E task 042 (sub-task C). Mirror of `e3-assistant-team-handoff.md` with R5-project-specific framing + full inline contract for self-contained reading. Future updates: as the Insights Engine team minor-versions the contract, this brief MUST be updated to reflect schema changes.*
