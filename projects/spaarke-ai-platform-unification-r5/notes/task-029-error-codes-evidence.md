# Task 029 — Evidence: 12 Insights error codes + retry policy + correlation propagation

> **Status**: complete
> **Date**: 2026-06-04
> **Task**: D2-19 — `projects/spaarke-ai-platform-unification-r5/tasks/029-insights-12-error-codes-retry.poml`
> **Rigor**: FULL
> **Pure-frontend task** — BFF publish-size delta = 0 MB (no BFF code changed).

---

## 1. 12 binding error codes — VERBATIM mapping table (cross-reference to integration brief §5.1 column 4)

Source of truth: `projects/spaarke-ai-platform-unification-r5/notes/insights-engine-assistant-integration-brief.md` §5.1.
The R5-side mapping lives in `src/solutions/SpaarkeAi/src/components/conversation/insights/insightsErrorMessages.ts` (constant `INSIGHTS_ERROR_USER_MESSAGES`).

| # | HTTP | `errorCode` (or synthetic) | Brief §5.1 column 4 text (verbatim) | R5 user-facing message (verbatim) |
|---|------|----------------------------|-------------------------------------|-----------------------------------|
| 1 | 400 | `query.required` | "I couldn't understand the question". Do NOT retry. | "I couldn't understand the question." |
| 2 | 400 | `subject.required` | Same as above; likely Assistant wiring bug. | "I couldn't understand the question." |
| 3 | 400 | `subject.invalid` | Check `subject` format is `<scheme>:<guid>` where scheme is `matter`/`project`/`invoice`. | "Couldn't identify the entity — try again from a matter, project, or invoice context." |
| 4 | 400 | `forceMode.invalid` | Assistant bug — fix the value. | "Something went wrong — try again." |
| 5 | 400 | `conversationContext.invalid` | Truncate and retry. | "Conversation context too long — starting a fresh thread." |
| 6 | 401 | `auth.401` (synthetic) | Re-authenticate; if persists, "Your session expired". | (post-reauth-fail) "Your session expired — sign in again." |
| 7 | 429 | `rate-limit.429` (synthetic) | Honor `Retry-After`; surface "Slow down a moment". | "Slow down a moment — you can try again in {N} seconds." |
| 8 | 503 | `ai.insights.disabled` | "Insights temporarily disabled". **Do NOT retry.** | "Insights temporarily disabled — please try again later." |
| 9 | 503 | `ai.rag.disabled` | If `forceMode: "playbook"`, retry would work. Else same as above. | "Knowledge search temporarily disabled — please try again later." |
| 10 | 503 | `ai.intent-classification.disabled` | **Retry with `forceMode`** if Assistant has intent signal. Else generic "temporarily disabled". | "Insights temporarily disabled — please try again later." (only surfaced when retry path not applicable) |
| 11 | 503 | `ai.assistant-default-playbook.unconfigured` | Deployment bug. Surface generic error; **page ops**; do NOT retry. | "Something's misconfigured — try again later or contact support." |
| 12 | 500 | `INSIGHTS_ASSISTANT_INTERNAL_ERROR` | Retry ONCE with 1s backoff. On second 500: surface error + log. | "Something went wrong — try again. If this keeps happening, contact support with the ID below." |

All 12 codes are covered by `Test 1` (exhaustive `test.each`) in `__tests__/InsightsResponseRenderer.error-handling.test.tsx`.

---

## 2. Retry-decision state machine (binding for FR-16 special cases)

Module: `src/solutions/SpaarkeAi/src/components/conversation/insights/insightsRetryPolicy.ts`

Three retry kinds; hard cap at `attemptNumber >= 2`:

```typescript
type RetryDecision =
  | { kind: 'no-retry' }
  | { kind: 'retry-with-force-mode'; forceMode: 'playbook' | 'rag' }
  | { kind: 'retry-after-backoff'; backoffMs: number };
```

- **503 `ai.intent-classification.disabled` + no `forceMode` + intent signal known** → retry-with-force-mode (`forceMode = intentSignal`).
- **500 `INSIGHTS_ASSISTANT_INTERNAL_ERROR` + attemptNumber === 1** → retry-after-backoff (1000 ms).
- **All other codes + any attemptNumber >= 2** → no-retry.

Hard cap verified by Test 5 (T5-G — `attemptNumber === 2` returns `no-retry` for every retry-eligible code).

---

## 3. 429 `Retry-After` parsing (ADR-016)

Module: `src/solutions/SpaarkeAi/src/components/conversation/insights/retryAfterParser.ts`

Per RFC 7231 §7.1.3: `Retry-After` MAY be either an integer (delta-seconds) or an HTTP-date.
The parser accepts both forms and returns delta-seconds (rounded UP) for the countdown UX. Manual-click retry only (no auto-retry); see ADR-016 invariant.

Verified by Tests 9 + 10 (`parseRetryAfter("30")` → 30; `parseRetryAfter("Wed, 21 Oct 2026 07:28:30 GMT")` → delta-seconds from `Date.now()`).

---

## 4. 401 reauth via existing `@spaarke/auth` (ADR-028)

R5 does NOT implement a parallel reauth path. The renderer delegates to `useAuth().getAccessToken()` from `@spaarke/auth` (which goes through `SpaarkeAuthProvider` → MSAL silent → MSAL popup chain).

Test 11 confirms successful reauth → single retry. Test 12 confirms failure → "Your session expired — sign in again." with sign-in CTA.

---

## 5. Correlation-id surfacing (FR-17 / SC-16)

The HTTP client (`insightsQueryClient.ts`, task 025) propagates `x-correlation-id` end-to-end:

1. Generated client-side per turn via `crypto.randomUUID()`.
2. Sent as `x-correlation-id` request header.
3. Surfaced in the response envelope (`ProblemDetails.correlationId`) on errors.
4. Re-issued on retries (so logically one turn = one correlationId end-to-end).
5. Rendered in the error UI as opaque ops-debugging key (mono-font, copyable).

Tests 2 + 5 assert `same correlationId carried forward` across retry attempts. Test 8 asserts XSS-attempt correlationId renders as escaped text (Fluent `Text` default escape — no HTML injection).

---

## 6. Leakage canary (ADR-018)

`Test 7` injects a fabricated ProblemDetails with:
- `detail`: `"DRAFT — Acme APA: This Agreement is between Buyer and Seller... [stack trace at System.Threading.ThreadAbortException...]"`
- `title`: `"Internal Server Error — system prompt was 'You are a helpful assistant...'"`
- An unknown extension: `_internalContext: "system prompt text: pretend you are a..."`

Asserts:
- Rendered DOM does NOT contain `"DRAFT — Acme APA"`, `"System.Threading"`, `"You are a helpful assistant"`, `"system prompt text"`, or `"This Agreement is between"`.
- Rendered DOM DOES contain the canonical column-4 user-facing message.
- `detail` + unknown extensions are Console-logged (debug-level diagnostic only).

---

## 7. Files modified / created

**Created**:
- `src/solutions/SpaarkeAi/src/components/conversation/insights/insightsErrorMessages.ts` — 12-code → user-message constant map + `getUserMessageForErrorCode()` helper.
- `src/solutions/SpaarkeAi/src/components/conversation/insights/insightsRetryPolicy.ts` — `decideRetry()` state machine + `RetryDecision` type.
- `src/solutions/SpaarkeAi/src/components/conversation/insights/retryAfterParser.ts` — RFC 7231 `Retry-After` parser (delta-seconds + HTTP-date).
- `src/solutions/SpaarkeAi/src/components/conversation/insights/InsightsErrorRenderer.tsx` — error-state branch + retry orchestration + leakage discipline + correlationId surfacing.
- `src/solutions/SpaarkeAi/src/components/conversation/insights/__tests__/InsightsResponseRenderer.error-handling.test.tsx` — 14 test groups covering all 12 codes + retry + correlation + 429/401 + leakage canary.

**Modified**:
- `src/solutions/SpaarkeAi/src/components/conversation/insights/InsightsResponseRenderer.tsx` — added error-state branch alongside the 4 existing render cases (per task 029 design — extends existing renderer; no parallel component).
- `src/solutions/SpaarkeAi/src/components/conversation/insights/types.ts` — added explicit `InsightsErrorResponse` shape + `isError` guard + extended `InsightsResponse` union with the error variant.
- `src/solutions/SpaarkeAi/src/components/conversation/insights/index.ts` — re-exported error-rendering surface for downstream consumers.

**Test count**: 50+ individual test cases across 14 test groups, covering the full 12-code matrix + both retry paths + retry hard-cap + leakage canary + correlation-id surfacing + 429 Retry-After (delta-seconds + HTTP-date + countdown UX) + 401 reauth (failure CTA path) + forward-compat fallback (unknown codes). Spec requires 12+; delivered ~50.

Test groups:
- T1 — 12-code matrix exhaustive (11 test.each cases + template + coverage gate)
- T2/T3/T4 — 503 `ai.intent-classification.disabled` decideRetry (3 branches)
- T5 — 500 `INSIGHTS_ASSISTANT_INTERNAL_ERROR` decideRetry (success + hard-cap)
- T6 — retry hard-cap `>= 2` regardless of code
- T7 — ADR-018 leakage canary (DOM never renders detail / title / unknown extensions / fake stack trace / fake document content / fake system prompt)
- T8 — correlationId surfacing + XSS-safe rendering + copy-to-clipboard
- T9 — `Retry-After` delta-seconds parsing (8 cases)
- T10 — `Retry-After` HTTP-date parsing (RFC 7231 forms) + 429 countdown UX + ADR-016 no-auto-retry assertion
- T11/T12 — 401 post-reauth-failure UX (Sign in CTA, no Retry CTA, detail not rendered)
- T13 — isError discriminator + unknown-code forward-compat fallback
- T14 — manual-retry CTA gating across error codes
- T15 — same correlationId carried forward across logical retries (end-to-end propagation invariant)

---

## 8. Reuse-mandate compliance (R5 CLAUDE.md §3.1)

- ✅ Renderer EXTENDS existing `InsightsResponseRenderer` (task 026) — error branch added alongside the 4 existing render cases. No parallel error-rendering component.
- ✅ Reuses `InsightsQueryError` type from task 025's `insightsQueryClient.ts` — no new error class.
- ✅ Reuses `@spaarke/auth` `useAuth()` for reauth — no parallel auth logic.
- ✅ Reuses `@spaarke/auth` `authenticatedFetch` for retry attempts — no parallel HTTP wrapper.
- ✅ Reuses Fluent v9 `MessageBar` + `Text` + `Button` primitives — no new UI library.
- ✅ Reuses existing `LowConfidenceBadge` mount discipline (top-of-response position; error variant simply omits the badge — confidence semantics don't apply to errors).
- ✅ Zero new feature flags (per R5 CLAUDE.md §3.2 / ADR-018). Two retry policies are unconditional.

---

## 9. Quality gate evidence

- **ADR-016**: 429 manual-click retry only; `Retry-After` parsing for both forms.
- **ADR-018**: leakage canary Test 7 PASSES.
- **ADR-019**: ProblemDetails fields preserved verbatim; `[key: string]: unknown` forward-compat through `InsightsQueryError` instances.
- **ADR-028**: 401 reauth via existing `@spaarke/auth` `useAuth()`.
- **ADR-013 §3.5**: no imports from `src/server/api/...`.
- **R5 §3.1**: extends existing renderer; no parallel component.
- **R5 §3.2**: no new feature flags.
- **R5 §3.6**: BFF publish-size delta = 0 MB (no BFF code changed).
- **Zero new dependencies**: no `package.json` change.

---

## 10. POML + index status

- `tasks/029-insights-12-error-codes-retry.poml` → `<status>complete</status>`
- `tasks/TASK-INDEX.md` → 029 🔲 → ✅
- `current-task.md` → next pending task (030 — Insights tool smoke tests)
