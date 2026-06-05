# Hand-Off — Spaarke Assistant Team (E3 BFF-side Done)

> **Status**: BFF implementation complete + integration-tested (2026-06-03). Assistant-side integration is OUT OF Wave E3 scope per task 042 POML sub-task C — this doc enables the Assistant team to consume the new endpoint.
> **Authored by**: Insights Engine r2 Wave E3 task 042 sub-task C
> **For**: Spaarke Assistant project workstream

---

## 1. What's ready on the BFF side

A new unified tool-call endpoint is live on `Sprk.Bff.Api`:

```
POST /api/insights/assistant/query
```

This is the **single tool surface** the Assistant invokes. The BFF makes the routing decision (playbook vs RAG) internally via the Wave E2 intent classifier or an Assistant-supplied `forceMode` override. The response shape is uniform across both paths.

**Canonical contract**: `projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md` — load this first. Everything below is operational guidance referencing that contract.

---

## 2. Request / response shape (quick reference)

### Request

```json
POST /api/insights/assistant/query
Authorization: Bearer <user-jwt>
Content-Type: application/json
x-correlation-id: <guid>     // recommended

{
  "query": "What will this matter cost to complete?",
  "subject": "matter:11111111-2222-3333-4444-555555555555",
  "forceMode": null,                          // or "playbook" | "rag"
  "conversationContext": {                    // optional Phase 1.5 telemetry only
    "conversationId": "<assistant-conv-id>",
    "previousTurnSummary": "user previously asked about..."
  }
}
```

### Success response (200)

```json
{
  "path": "playbook",                         // or "rag"
  "answer": "Predicted cost ~$280k based on 12 matters.",
  "citations": [
    { "n": 1, "source": "Acme APA.pdf", "excerpt": "Cost: $282k", "observationId": "doc-A", "chunkId": "chunk-1" }
  ],
  "confidence": 0.74,
  "playbookId": "predict-matter-cost@v1",     // or null on RAG path
  "structuredResult": {
    "kind": "inference",                       // or "decline" | "observation"
    "envelope": { "...": "..." }               // rich, kind-specific payload
  },
  "diagnostics": {
    "intentSource": "classifier",              // or "forceMode" | "classifier-fallback"
    "classifierBelowThreshold": false,
    "elapsedMs": 1842,
    "cacheHit": false
  }
}
```

**Response headers** (always present unless noted):

```
X-Insights-Path: playbook | rag
X-Insights-Intent-Source: classifier | forceMode | classifier-fallback
X-Insights-Elapsed-Ms: <N>
X-Insights-Cache: true | false
X-Insights-Hit-Count: <N>                     // RAG path number of hits / playbook citations
```

---

## 3. Error handling (what to do when)

Errors are ProblemDetails (`application/problem+json`) with stable `errorCode` extension:

| HTTP | `errorCode` | What it means | Recommended Assistant action |
|---|---|---|---|
| 400 | `query.required` | Missing/empty `query` | Surface "I couldn't understand the question". Don't retry. |
| 400 | `subject.required` | Missing/empty `subject` | Same as above; Assistant likely has a wiring bug. |
| 400 | `subject.invalid` | Unknown scheme or malformed Guid in `subject` | Check that `subject` is `<scheme>:<guid>` where scheme is `matter` \| `project` \| `invoice`. |
| 400 | `forceMode.invalid` | `forceMode` is neither `"playbook"` nor `"rag"` | Assistant bug — fix the value. |
| 401 | (auth challenge) | Missing / invalid token, missing `tid` claim, missing `oid` claim | Re-authenticate; if persists, "Your session expired". |
| 429 | (rate-limit middleware) | `ai-context` policy budget (60/min/oid) | Honor `Retry-After`; surface "Slow down a moment". |
| 503 | `ai.insights.disabled` | Compound AI kill-switch OFF | Surface "Insights temporarily disabled". Log for ops. **Do NOT retry.** |
| 503 | `ai.rag.disabled` | RAG sub-gate OFF AND chosen path is RAG | Same as above. If you sent `forceMode: "playbook"`, retry would work. |
| 503 | `ai.intent-classification.disabled` | Classifier OFF AND no `forceMode` supplied | **Retry with `forceMode`** if you have any intent signal. Else surface generic "temporarily disabled". |
| 503 | `ai.assistant-default-playbook.unconfigured` | Deployment bug — `Insights:Playbooks:DefaultName` resolves to empty Guid | Surface generic error; **page ops**; do NOT retry. |
| 500 | `INSIGHTS_ASSISTANT_INTERNAL_ERROR` | Unexpected internal failure | Retry ONCE with 1s backoff; on second 500 surface "Something went wrong" + log. |

### Errors NEVER include

- Document content
- Prompt text
- LLM raw output
- Stack traces

These are stripped per ADR-019 + ADR-018. The `correlationId` extension field is the log lookup key for ops/support.

---

## 4. ForceMode — when to use it

The classifier costs ~300ms LLM round-trip per call. If the Assistant already KNOWS the user's intent (e.g., the user invoked a named tool like "Predict Cost"), supply `forceMode` to skip the classifier:

```json
{ "query": "...", "subject": "...", "forceMode": "playbook" }   // user invoked the "Predict Cost" tool
{ "query": "...", "subject": "...", "forceMode": "rag" }        // user invoked the "Search Documents" tool
```

When `forceMode` is null/omitted the BFF runs the classifier and routes per its output (with `BelowThreshold` → automatic RAG fallback per FR-05 safety).

**Important**: `forceMode` does NOT bypass kill-switches. If RAG is disabled, `forceMode: "rag"` still 503's. See contract §7 matrix.

---

## 5. Rate-limit + retry guidance

- **Aggregate budget**: `ai-context` policy applies across `/api/insights/ask` + `/api/insights/search` + `/api/insights/assistant/query` per `oid` (60 req/min sliding window).
- **Retry-After**: honor it. The BFF emits `Retry-After: <seconds>` on every 429.
- **Internal-error retry**: retry ONCE on 500 with 1s exponential backoff. On second 500, surface the error.
- **Idempotency**: all requests are idempotent (read-only Phase 1.5). The same `(query, subject, forceMode, oid, tid)` is cache-key-stable on the playbook path (5-min D-P13 cache TTL).

---

## 6. Telemetry headers to propagate

| Header | Required | Purpose |
|---|---|---|
| `x-correlation-id: <guid>` | **Strongly recommended** | BFF uses it for cross-service log correlation. Generate per Assistant request and propagate from BFF response → next BFF call. |
| `x-conversation-id: <opaque>` | Optional | Assistant-side conversation id. May ALSO appear in body `conversationContext.conversationId` — pick one source, BFF reads body first. |
| `Authorization: Bearer <jwt>` | **Required** | OBO user token per ADR-028 (Spaarke Auth v2). Service-principal tokens NOT accepted. |

---

## 7. Phase 1.5 vs Phase 2 boundary

| Feature | Phase 1.5 (now) | Phase 2 (later) |
|---|---|---|
| Tool invocation | ✅ Read-only | ✅ Read-only |
| Bidirectional clarification | ❌ Out of scope | ✅ 422 with `clarification` envelope (Assistant renders disambiguation) |
| Streaming response | ❌ Single shot only | ✅ SSE for long-running playbooks |
| Multi-turn conversation state | ❌ Telemetry only on BFF | ✅ BFF persists conversation context |
| Assistant-supplied `playbookHint` | ❌ Not in schema | ✅ Forward-compatible field add |
| Cross-tenant federation | ❌ Single-tenant | ✅ Multi-tenant aware |
| Actionable citations | ❌ Display-only | ✅ `citations[].action: { type, ... }` |

The Phase 1.5 contract is **forward-compatible** with Phase 2 additions — new optional fields the Assistant ignores will be silently accepted.

---

## 8. Open questions for the Assistant team

The following are NOT yet decided; please file feedback into the contract review log (§10 of `design-e3-tool-call-contract.md`):

1. **Streaming**: Does the Assistant need SSE for any Phase 1.5 use case? Today `predict-matter-cost@v1` runs in <2s typical; no need. But other planned playbooks may exceed 5s.
2. **Default playbook for `forceMode: "playbook"` without hint**: Today the BFF resolves via `Insights:Playbooks:DefaultName` config (defaults to `predict-matter-cost@v1`). Is the Assistant ever going to supply `forceMode: "playbook"` without ALSO knowing which playbook? If yes, we should add `playbookHint` to the request schema in Phase 1.5 itself.
3. **Decline rendering**: Phase 1.5 decline path returns `structuredResult.envelope.SuggestedActions` as plain strings. Is the Assistant prepared to render them as text? Or do we need action verbs / button hints in the schema?
4. **Citation display**: Are the `citations[].source` strings (document display names) sufficient, or does the Assistant need a stable URL/href? Today we return `observationId` + `chunkId` for client-side resolution.
5. **Confidence floor**: Below what `confidence` does the Assistant want to add a disclaimer? Today: classifier `BelowThreshold` (0.7 typical) triggers RAG fallback inside the BFF. The Assistant could add a UI-side floor as well.
6. **`previousTurnSummary` usage**: Phase 1.5 BFF logs but does not consume it. If the Assistant has summarization, do you want the BFF to pass it to the classifier prompt as additional context in Phase 1.5 (small change, low risk)?

---

## 9. Operational notes for the Assistant team

- **Deployment**: The endpoint is live on the BFF the moment Wave E task 042 is merged + deployed. Verify on Dev via `GET /swagger` → `Insights` tag → `InsightsAssistantQuery`.
- **Environment configuration**: The Assistant team does NOT need to add any new App Service / Key Vault settings on its side. All BFF-side config lives in `Sprk.Bff.Api` and is managed by Insights Engine project deploys.
- **Auth scope**: The user JWT must include `tid` + `oid` claims. The BFF rejects tokens missing these with 401 ProblemDetails (not 401 with empty body).
- **Local testing**: The Assistant team can hit the BFF locally with a real Entra ID test user token; no special "assistant tester" mode needed. Same OBO flow as PCFs and Code Pages.
- **Sample curl** (Dev):
  ```bash
  curl -X POST https://<bff-dev-host>/api/insights/assistant/query \
    -H "Authorization: Bearer <user-jwt>" \
    -H "Content-Type: application/json" \
    -H "x-correlation-id: test-001" \
    -d '{"query":"predicted cost","subject":"matter:da116923-d65a-f111-a825-3833c5d9bcb1","forceMode":"playbook"}'
  ```
  Where the subject is one of the Wave D7 synthetic test matter ids (per `current-task.md` § Key load-bearing technical anchors).

---

## 10. Review + sign-off

The canonical contract is `design-e3-tool-call-contract.md`. Please review with focus on:

- Field semantics (§3 + §4)
- Error codes + retry guidance (§5 + §3 above)
- The 12 open questions (§8 above)

Record review feedback in the contract's review log (§10 of the design doc). Wave E task 042 sub-task A.5 is the formal review sign-off; it remains PENDING until the Assistant team's review is logged. The BFF implementation is intentionally locked at v1.0 of the contract — schema changes after Phase 1.5 ship require a new minor version + backwards-compat plan.

---

*Authored 2026-06-03 — Wave E task 042 sub-task C (BFF-side handoff to Spaarke Assistant project).*
