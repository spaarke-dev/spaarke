---
title: Wave F — Contract v1.1 Mini-Plan (SSE + clickable citations)
status: APPROVED — R5 responses integrated 2026-06-04; ready to execute after task 090
created: 2026-06-04
last-updated: 2026-06-04 (R5 responses received + integrated)
authored-by: Claude (Anthropic AI agent) on behalf of the Insights Engine r2 project
triggered-by: R5 contract change request (`notes/insights-engine-contract-v1.1-request.md`)
related-docs:
  - notes/insights-engine-contract-v1.1-request.md (R5 request)
  - notes/insights-engine-assistant-integration-brief.md (v1.0 contract)
  - design-e3-tool-call-contract.md (canonical v1.0 contract)
trigger-phrases:
  - "start wave f", "start contract v1.1", "implement r5 v1.1 request"
  - "review wave f mini-plan", "what's in wave f"
---

# Wave F — Contract v1.1 Mini-Plan

> Follow-on minor-version (v1.0 → v1.1) of the Spaarke Assistant tool-call contract, requested by R5 (`spaarke-ai-platform-unification-r5`). Strictly additive + back-compat. Sequenced AFTER task 090 (Phase 1.5 wrap-up).

---

## 0. R5 responses to Insights team feedback (2026-06-04) — INTEGRATED

R5 received the 7 feedback points in §1 below and responded. All 6 substantive points accepted; relevant decisions integrated into tasks 050 + 052 + mini-plan risks/scope sections.

| # | Insights feedback | R5 response | Plan impact |
|---|---|---|---|
| 1 | Effort 4.5d (not R5's 3-4d) | ✅ Accept 4.5d | Mini-plan §4 already reflects 4.5d; no change |
| 2 | Streaming surface at orchestrator (not IRagService) | ✅ Accept scoping | Task 051 already reflects this; no change |
| 3 | Citations spike required + plumbing-cost risk | ✅ Accept spike. **NEW escape hatch**: if spike finds doc-href plumbing cost is large (reshaping Evidence model upstream), Insights flags back; **R5 accepts v1.1 with observation-citation href ONLY**, deferring document-citation href to v1.2 rather than block | **Task 050 + 052 updated** (see §0.1 below) |
| 4 | Privilege filtering via authorized endpoint URL | ✅ Confirmed; R5 consumes URL as-is and handles 403 as opaque error | No change |
| 5 | Wave F sequenced after task 090 | ✅ Confirmed | No change |
| 6 | Don't bundle NullInsightsAi fix | ✅ Agree; separate scope/PR | No change |

### 0.1 Citation-plumbing escape hatch (binding)

Task 050 (spike) MUST produce a **plumbing-cost estimate** (Small ≤0.5d / Medium 0.5–1d / Large >1d) for getting `driveId` + `itemId` (or equivalent document identifiers) into `AssistantQueryCitation`:

- **Small or Medium cost** → Wave F ships BOTH observation + document citation href (the planned full scope)
- **Large cost** (e.g., requires reshaping `Evidence` model upstream or RAG response assembly) → Wave F ships **observation citation href ONLY**; document citation href deferred to v1.2. R5 has explicitly approved this fallback to prevent blocking on schema-reshaping work.

Task 052 acceptance criteria branches based on F1 output. F1's recommendation is binding for F2/F3 scope.

---

## 1. Decision summary

**Recommendation**: ACCEPT R5's v1.1 request with minor scoping adjustments. Effort estimate aligned at ~4.5 days vs R5's 3–4 day estimate (extra 0.5d for spike + 1d realistic SSE effort).

**Sequencing**: Wave F runs AFTER task 090 (Phase 1.5 wrap-up). The 090 task captures Phase 1.5 lessons-learned cleanly; Wave F is an additive follow-on, not a replacement.

**Bundling decision**: Wave F does NOT bundle the pre-existing `NullInsightsAi` asymmetric-registration warning (flagged by Wave E adr-check). That's a separate concern; keep Wave F scope tight to honor R5's "minor version, additive" framing.

---

## 2. Scope (binding)

Two additive changes to `POST /api/insights/assistant/query`:

1. **SSE streaming** via `Accept: text/event-stream` header negotiation (R5 §2 of request)
   - `Accept: application/json` or absent → existing v1.0 single-shot response (no change)
   - `Accept: text/event-stream` → new v1.1 SSE stream with `progress` / `delta` / `result` / `error` event types per R5 §2.2 schema
   - RAG path: stream LLM synthesis tokens via `delta` events on `answer` path (primary value)
   - Playbook path: emit `progress` events only (deterministic node assembly — no token-level streaming value)

2. **`citations[].href` optional field** (R5 §3 of request)
   - Adds clickable navigation to citation sources
   - SPE document citations → URL resolvable via existing preview-URL pipeline (see §3.2 risk below)
   - Observation citations → either model-driven-app URL OR BFF endpoint URL (decided in spike)
   - `null`/absent when source has no authorizable preview path

**Out of scope** (explicitly NOT v1.1; deferred to Phase 2 per the v1.0 contract):
- Bidirectional clarification (HTTP 422 with `clarification` envelope)
- Multi-turn conversation state on BFF
- `playbookHint` Assistant-supplied field
- Actionable citations (`citations[].action`)
- Cross-tenant federation
- `previousTurnSummary` consumption (BFF only logs it)

---

## 3. Technical analysis (grounded in current code)

### 3.1 ✅ SSE streaming is feasible without `IRagService` interface change

Verified:
- `IOpenAiClient.StreamCompletionAsync(...) → IAsyncEnumerable<string>` ALREADY exists ([`IOpenAiClient.cs:36`](../../src/server/api/Sprk.Bff.Api/Services/Ai/IOpenAiClient.cs#L36))
- `IRagService.SearchAsync` returns `Task<RagSearchResponse>` (single-shot search results — non-streaming, fast, deterministic)
- Only the LLM synthesis call in `InsightsOrchestrator.SearchAsync` needs to switch from `GetCompletionAsync` to `StreamCompletionAsync`

R5's request §2.1 implied `IRagService` might need interface changes. It does NOT. Streaming is added at the orchestrator + endpoint layer only. This **reduces the SSE work effort** compared to R5's framing.

### 3.2 ⚠️ Citations `href`: identifier flow needs validation

Current citation projection in [`AssistantToolCallHandler.cs:303–308`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Insights/AssistantToolCallHandler.cs#L303) (playbook path) and [`:404`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Insights/AssistantToolCallHandler.cs#L404) (RAG path) constructs `AssistantQueryCitation` from `Source` (display name only). It does NOT carry `driveId`, `itemId`, or document Guid.

For SPE-backed href URLs, we need to plumb document identifiers through:
- **Playbook path**: `artifact.Evidence[].Ref` — what format? Display name only? Guid? URL? → spike validates
- **RAG path**: `RagSearchResponse` hits — chunks carry document Guid via what field? → spike validates

Existing preview URL surface is [`DocumentCheckoutService.GetPreviewUrlAsync(driveId, itemId, ct)`](../../src/server/api/Sprk.Bff.Api/Services/DocumentCheckoutService.cs#L288), NOT `SpeFileStore.GetFilePreviewUrlAsync` as R5's request §3.3 implies.

**Spike output**: confirm citation ID flow + pick the right BFF URL pattern.

### 3.3 ✅ AIPU2-027 privilege filtering on href URLs is clean

Recommended approach: `href` points to an existing BFF endpoint (`/api/v1/documents/{id}/preview` or similar) that re-checks user authorization. If user can't access the source, the URL returns 403 — no URL signing or token embedding required. This is the path of least resistance and the safest privilege model.

If the citation's underlying source is filtered out by AIPU2-027 (user can't see it at search time), `citations[].href` is `null`/omitted — same filtering pass that drops the citation also drops the href.

### 3.4 ✅ Back-compat guarantee

- v1.0 clients (no `Accept: text/event-stream` header) get identical v1.0 single-shot JSON. Tested via existing 15 endpoint tests carried forward unchanged.
- v1.0 clients (which ignore unknown response fields) see `citations[]` without `href` (or with `href: null`) — no behavior change.
- v1.1 clients (R5) opt into both behaviors via header + response field check.

---

## 4. Task structure (4 tasks, ~4.5 days)

| ID | Wave-item | Title | Effort | Parallel-safe | Depends on |
|---|---|---|---|---|---|
| [050](../tasks/050-streaming-and-citation-spike.poml) | F1 | Spike: streaming surface + citation ID flow validation | 0.5d | — | 090 |
| [051](../tasks/051-sse-streaming-endpoint.poml) | F2 | SSE streaming on `POST /api/insights/assistant/query` | 3d | ❌ | 050 |
| [052](../tasks/052-citations-href-projection.poml) | F3 | `citations[].href` projection + URL resolution | 1d | ✅ (parallel with 051) | 050 |
| [053](../tasks/053-contract-v1.1-docs.poml) | F4 | Contract v1.1 docs + R5 coordination update | 0.5d | ❌ | 051, 052 |

### 4.1 Parallel dispatch plan (per `feedback-actual-deps-not-orchestration-groups` memory)

- **Round 1**: 050 serial (gates everything; spike output informs 051 + 052)
- **Round 2**: 051 + 052 in parallel sub-agents (different files, no overlap)
- **Round 3**: 053 serial (consumes outputs of 051 + 052)

Critical path: 050 (0.5d) → 051 (3d) → 053 (0.5d) = ~4 days end-to-end with parallelism. R5's estimate of "~3-4 days" is honored.

### 4.2 Branch + PR strategy

- Branch: `work/ai-spaarke-insights-engine-r2-wave-f` off post-090 master
- Single Wave F commit (or per-task per Wave D precedent — owner decision at execution time)
- Auto-merge PR after CI green (consistent with Wave D + E pattern)

---

## 5. Acceptance criteria for "Wave F done"

### Code

- [ ] `POST /api/insights/assistant/query` accepts `Accept: text/event-stream`; SSE response emits `progress`/`delta`/`result`/`error`/`[DONE]` per R5 §2.2 schema
- [ ] `Accept: application/json` / absent → identical v1.0 single-shot behavior (regression tested against existing 15 endpoint tests)
- [ ] RAG path streams `answer` field tokens via `delta` events
- [ ] Playbook path emits `progress` events for major node transitions (sanitizer, classify, gate, extract, ground, emit)
- [ ] `citations[].href` field present on response schema
- [ ] SPE document citations carry a working `href` URL resolvable to a preview (verified via Spaarke Dev smoke)
- [ ] Observation citations carry an `href` URL OR `null` if no preview path exists
- [ ] `href` URLs respect AIPU2-027 — no URL leaked for sources the calling user can't access
- [ ] All Wave E tests continue to pass (back-compat)
- [ ] New tests added for: header negotiation, SSE event sequence, delta-content accumulation matches `result` event's full content, href present on SPE citations, href absent/null when source unauthorized, `[DONE]` sentinel

### Quality

- [ ] §3.5 facade-boundary grep clean
- [ ] `dotnet format whitespace Spaarke.sln --verify-no-changes` clean (PR #336 lesson)
- [ ] code-review + adr-check skills run (both should pass; no new ADR violations)
- [ ] Publish size delta < +2 MB (no new NuGet packages expected)

### Docs

- [ ] `design-e3-tool-call-contract.md` bumped to v1.1 with changelog entry; SSE protocol + `href` field schema added to §3 + §4
- [ ] `notes/insights-engine-assistant-integration-brief.md` updated to reflect v1.1 endpoint behavior (§3 request, §4 response, §5 error model, §B sanity checks)
- [ ] R5 coordination doc (`spaarke-ai-platform-unification-r5/notes/insights-r2-coordination.md`) gets new §8 changelog entry: "v1.1 ships; touchpoints §4.4 + §4.6 closed"
- [ ] Update `current-task.md` and `TASK-INDEX.md` to reflect Wave F status

### Deploy

- [ ] PR merged to master
- [ ] BFF redeployed to `spaarke-bff-dev` with the change live on `/api/insights/assistant/query`
- [ ] R5 smoke test against Spaarke Dev BFF confirms SSE + clickable citation behavior

---

## 6. Decisions to make at execution time

1. **Branch strategy**: single-commit PR (like Wave E) OR per-task commits (like Wave D)? Owner decides at PR open.
2. **Playbook-path streaming detail**: emit `progress` per node? Or only at major phase boundaries (classify / extract / synthesis)? Spike clarifies.
3. **Observation citation URLs**: model-driven-app URL OR new BFF endpoint? Spike clarifies; owner picks.
4. **SSE error mid-stream**: emit `error` event then `[DONE]` OR connection-close? Recommend: explicit `error` event + `[DONE]` for cleaner client handling.

---

## 7. Risks + mitigations

| Risk | Probability | Mitigation |
|---|---|---|
| Citation document IDs not present in current `Evidence`/`RagSearchResponse` shape | Medium | Spike (050) validates upfront + produces explicit Small/Medium/Large cost estimate. **R5 has pre-approved a fallback** (per §0.1): if cost is Large, Wave F ships observation-href only, defers document-href to v1.2. No blocking on schema reshaping. |
| Azure OpenAI streaming has retry/error semantics that differ from single-shot | Low | Existing `IOpenAiClient.StreamCompletionAsync` already encapsulates this; reuse without modification |
| SSE adds CPU/memory pressure (long-lived connections) | Low | Single-shot remains default; SSE only on R5 opt-in. R5's projected QPS is low (chat-driven, not batch). |
| R5 ships v1.0 consumption before Wave F lands | Low | R5 timeline (2-3 weeks) > Wave F timeline (~1 week post-090); even if R5 ships v1.0 first, v1.1 is back-compat — R5 upgrades when convenient |
| Wave F slips into Phase 2 design discussions | Medium | Cap Wave F at the 4 tasks above; any Phase 2 follow-ons (additional decline rendering, action buttons, multi-turn state) are NOT in Wave F |

---

## 8. R5 coordination touch-points

| Item | Owner | Trigger |
|---|---|---|
| Acknowledge R5 request | Owner (Ralph) | After reviewing this mini-plan |
| Pick observation URL strategy (§3.4 of request) | Insights team (spike 050 surfaces options) | Spike output |
| Schedule R5 smoke test slot post-deploy | Owner ↔ R5 lead | Wave F PR merge |
| Update R5 coordination doc §8 changelog | Wave F task 053 | Wave F close |

---

## 9. Open questions for owner

1. ~~**Approve v1.1 work in principle?**~~ ✅ **APPROVED** 2026-06-04 (R5 responses §1–6 all accepted)
2. ~~**Sequence — v1.1 runs AFTER task 090?**~~ ✅ **CONFIRMED** 2026-06-04
3. **Bandwidth — start v1.1 immediately after 090 OR pause?** Owner-mediated; no R5 timeline pressure (per R5 §1 response — Phase 1 work proceeds in parallel regardless)
4. **Author Wave F via Claude sub-agents (Wave D + E precedent)?** ✅ Recommend yes; pending owner confirmation at execution time
5. ~~**Bundle NullInsightsAi cleanup?**~~ ❌ **CONFIRMED no** 2026-06-04 (R5 §6 agrees; separate scope)

**Net: 4 of 5 owner questions answered; 1 (bandwidth/timing) remains for execution-time decision.**

---

*Authored 2026-06-04 by Claude (Anthropic AI agent) in response to R5's contract v1.1 request. Pending owner approval before any task execution begins.*
