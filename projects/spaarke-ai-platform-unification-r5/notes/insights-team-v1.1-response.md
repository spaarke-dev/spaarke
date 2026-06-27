---
title: R5 response to Insights team feedback on v1.1 request
audience: ai-spaarke-insights-engine-r2 team
from: spaarke-ai-platform-unification-r5
date: 2026-06-03 (late)
status: ready-to-send
purpose: Point-by-point response to the 7 feedback items raised by the Insights team on R5's v1.1 contract change request
---

# R5 response to Insights team feedback on v1.1 request

Thanks for the review. All 7 points landed cleanly — accepting most as-is, with one operator-decision item left open. Updated v1.1 request doc at `notes/insights-engine-contract-v1.1-request.md` reflects the negotiated state (see §0a "Negotiation Outcome" + §9 changelog).

Point-by-point:

## #1 — Effort estimate (4.5 days)

**Accepted.** 4.5d is more realistic than my 3-4d. Polly retry semantics for SSE + AOAI streaming error handling (`content_filter` / `length` finish reasons mid-stream) are legitimate engineering concerns I under-weighted. v1.1 request doc §0a + §4 updated to 4.5d. R5 has no timeline impact — Phase 1 platform extensions proceed in parallel regardless.

## #2 — Streaming surface placement

**Accepted, and this is strictly better than my original framing.** Adding the streaming variant at `IInsightsAi.SearchStreamAsync` (facade overload) + `InsightsOrchestrator` synthesis layer — with `IRagService` interface UNCHANGED and `IOpenAiClient.StreamCompletionAsync` reused — is the right scope. My request implied wider blast radius than necessary. v1.1 request doc §2.1 updated with the clarification.

## #3 — Citations spike + URL helper correction

**Both corrections accepted; spike included in v1.1 scope.**

- Correct URL helper is `DocumentCheckoutService.GetPreviewUrlAsync(driveId, itemId, ct)`, not `SpeFileStore.GetFilePreviewUrlAsync`. My error — I conflated two facades. v1.1 request doc §3.3 corrected.
- Schema-plumbing risk on `AssistantQueryCitation` carrying `Source string` only is real. The 0.5d spike is the right way to confirm. Added as §3.4 in v1.1 request doc.
- **R5 fallback if spike reveals large plumbing cost**: Wave F ships v1.1 with `href` only on observation citations; document-citation `href` defers to v1.2. R5 lives with display-name-only document citations in v1.1 (back-compat rendering path already handles `href: null` gracefully — no R5 code change needed for the fallback). v1.1 request doc §0a + §3.3 + §4 capture this.

## #4 — Privilege filtering via authorized endpoint

**Confirmed.** `href` points to an existing authorized endpoint (e.g., `/api/v1/documents/{id}/preview` for documents; existing Dataverse-record-view URL for observations). No URL signing. The endpoint enforces authorization at request time. If a citation that came back in the response is for a document the user lost access to between the response and the click, the endpoint returns 403 — R5 handles it as a standard HTTP error in the citation-click UX (already covered by the §4.12 error-handling work in R5 design.md).

## #5 — Sequencing (Wave F after task 090)

**Confirmed.** R5's original request explicitly said "after task 090 wrap-up" (v1.1 request doc §6). Glad the Insights team's reading matches. No change needed.

## #6 — Do not bundle `NullInsightsAi` cleanup

**Agreed.** R5 has no opinion on the architectural cleanup — it's an internal Insights concern. Bundling it with Wave F would dilute scope and risk PR review complexity. Keep Wave F focused on SSE + citations. The `NullInsightsAi` cleanup is a separate ticket, separate PR, separate review whenever it gets prioritized.

## #7 — Bandwidth (~1 week between Phase 1.5 close and Phase 2 outline)

**Open — operator decision.**

R5's perspective: the ~1 week of Insights work doesn't block R5 IF done in parallel with R5 Phase 1. Parallel-execution math:

| Week | R5 | Insights |
|---|---|---|
| W1 | Phase 1 platform extensions (session indexing, FieldDelta, ChatSession manifest, cleanup job) | Task 090 wrap-up + Wave F kickoff (spike) |
| W2 | Phase 1 finishing + Phase 2 start (Summarize vertical slice) | Wave F implementation (SSE + citations) |
| W3 | Phase 2 continues (Summarize complete; Insights tool integration starts) | Wave F deploy to Spaarke Dev; v1.1 live |
| W4 | Phase 2 Insights tool integration completes against v1.1 | Phase 2 (Insights) outline / R6 backlog start |

R5 Phase 2 Insights tool consumption isn't until W3+, by which time v1.1 should be live.

R5 fallback if Wave F slips or is declined: R5 Phase 2 ships consuming v1.0 (single-shot, display-name-only citations). UX degraded but functional. v1.1 consumption becomes a follow-up. R5 design.md §4.12 documents this gracefully.

**R5 recommendation to operator**: approve Wave F if Insights team has the engineering hours. The parallel-execution math works. R5 isn't blocked either way.

---

## Net negotiated v1.1 scope

| Item | Status |
|---|---|
| SSE on `/api/insights/assistant/query` via `Accept: text/event-stream` | ✅ Agreed |
| Streaming surface placement | `IInsightsAi.SearchStreamAsync` overload at facade; `InsightsOrchestrator` synthesis layer streams; `IRagService` unchanged |
| `delta` event schema mirroring R5's `FieldDelta` (path + content + sequence) | ✅ Agreed |
| `citations[].href` for clickable navigation | ✅ Agreed pending spike |
| Document citation URL via | `DocumentCheckoutService.GetPreviewUrlAsync(driveId, itemId, ct)` |
| Observation citation URL via | Insights team's choice (MDA URL or BFF endpoint) |
| Privilege filtering | Via authorized endpoint (no URL signing) |
| Polly retry semantics + AOAI streaming error handling | ✅ Documented in acceptance criteria |
| Spike for schema plumbing | 0.5d included |
| Documentation update | 0.5d included |
| Total Wave F estimate | ~4.5 days |
| Sequencing | Wave F runs after task 090 wrap-up |
| Bundle with `NullInsightsAi` cleanup | ❌ — separate ticket |

## What R5 needs from Insights team next

1. **Operator's bandwidth decision** on whether Wave F is approved (~4.5d of engineering capacity between task 090 close and Phase 2 outline)
2. If approved: Wave F PR opens after task 090; deploy to Spaarke Dev; R5 smoke tests against v1.1
3. If declined or deferred: notify R5; R5 ships Phase 2 with v1.0 consumption; v1.1 consumption becomes a follow-up project

## Status updates we'll capture

- v1.1 request doc updated (`projects/spaarke-ai-platform-unification-r5/notes/insights-engine-contract-v1.1-request.md`) reflects all negotiated agreements
- R5 design.md §4.12 + §8.2 updated to reflect the spike-outcome contingency for document-citation `href`
- Coordination doc `notes/insights-r2-coordination.md` §8 changelog will get a Wave F start/finish entry when Wave F kicks off

Thanks for the careful review. Ping back on bandwidth decision when ready.

— R5 team
