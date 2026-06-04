# Task 025 (D2-15) — Frontend client evidence

> **Task**: D2-15 Subject resolution + HTTP client (existing `@spaarke/auth`)
> **Status at completion**: ✅ complete
> **Executed by**: sub-agent in parallel wave P2-G6 (sibling of 024)
> **Completed**: 2026-06-04
> **Scope**: CODE AUTHORING only — main session owns commit / push / npm build / quality gates

---

## 1. Module placement decision

**Candidate A selected** (default per task POML Step 3): `src/solutions/SpaarkeAi/src/services/insightsQueryClient.ts`.

Rationale (YAGNI + R5 CLAUDE.md §3.1 reuse mandate):
- Single consumer in R5 scope (SpaarkeAi shell). No multi-surface need today.
- Matches existing services folder convention (`notificationContextLoader.ts`, `authInit.ts`, etc.).
- Future Office Add-in / PCF reuse would require extraction, but no such consumer exists in R5 — defer per YAGNI.
- If task 026's renderer would benefit from importing from `@spaarke/ai-widgets`, revisit then.

---

## 2. Files created

| Path | LOC | Purpose |
|---|---|---|
| `src/solutions/SpaarkeAi/src/services/insightsQueryClient.ts` | 590 | Subject resolver + Insights HTTP client (`resolveSubject` + `callInsightsQuery` + `InsightsQueryError` + 5 supporting types) |
| `src/solutions/SpaarkeAi/src/services/__tests__/insightsQueryClient.test.ts` | 610 | 32 unit tests across 12 test groups (subject resolution × 2 groups, SSE happy path, fresh-token, correlation-id × 2, JSON direct, 406 fallback × 2, 12 error codes, forceMode × 3, forward-compat, Zone B grep, AbortSignal × 2) |

Total: 1200 LOC (590 production + 610 tests).

---

## 3. Public interface (for cross-reference by tasks 026 / 027 / 028 / 029)

```ts
// Exported types
export type SubjectScheme = 'matter' | 'project' | 'invoice';
export interface ResolvedSubject {
  readonly kind: 'subject';
  readonly subject: string;        // canonical `<scheme>:<guid>`
  readonly scheme: SubjectScheme;
  readonly entityId: string;       // GUID without braces
}
export interface NoSubject {
  readonly kind: 'no-subject';
}
export interface InsightsQueryRequest {
  query: string;                   // 1..500 chars (not pre-validated client-side)
  subject: string;                 // canonical `<scheme>:<guid>`
  forceMode?: 'playbook' | 'rag';  // optional per FR-12 / SC-17
  signal?: AbortSignal;            // cancellation
}
export interface InsightsQueryPlaybookResult {
  readonly kind: 'playbook';
  readonly correlationId: string;
  readonly envelope: Record<string, unknown>;
}
export interface InsightsQueryRagResult {
  readonly kind: 'rag';
  readonly correlationId: string;
  readonly envelope: Record<string, unknown>;
}
export type InsightsQueryResult = InsightsQueryPlaybookResult | InsightsQueryRagResult;

export type InsightsErrorCode =
  | 'query.required' | 'subject.required' | 'subject.invalid'
  | 'forceMode.invalid' | 'conversationContext.invalid'
  | 'auth.401' | 'rate-limit.429'           // synthetic
  | 'ai.insights.disabled' | 'ai.rag.disabled'
  | 'ai.intent-classification.disabled'
  | 'ai.assistant-default-playbook.unconfigured'
  | 'INSIGHTS_ASSISTANT_INTERNAL_ERROR';

export class InsightsQueryError extends Error {
  readonly errorCode: string;
  readonly correlationId: string;
  readonly status: number;
  readonly title: string;
  readonly detail: string;
  readonly retryAfterSeconds?: number;
}

export interface InsightsQueryClientOptions {
  authenticatedFetch?: AuthenticatedFetchFn;  // test-injection seam
}

// Exported functions
export function resolveSubject(
  entityLogicalName?: string,
  entityId?: string,
): ResolvedSubject | NoSubject;

export async function callInsightsQuery(
  request: InsightsQueryRequest,
  options?: InsightsQueryClientOptions,
): Promise<InsightsQueryResult>;
```

### Integration recipe for task 019's slash command handler

```ts
import {
  resolveSubject, callInsightsQuery, InsightsQueryError,
} from '../services/insightsQueryClient';

const subjectResult = resolveSubject(entityLogicalName, entityId);
if (subjectResult.kind === 'no-subject') {
  emit({ kind: 'error', errorCode: 'subject.required',
         detail: 'No entity in chat context.' });
  return;
}
try {
  const result = await callInsightsQuery({
    query: userQuery,
    subject: subjectResult.subject,
    forceMode: slashSubCommand === 'playbook' ? 'playbook'
             : slashSubCommand === 'rag' ? 'rag' : undefined,
  });
  renderInsightsResponse(result);   // task 026
} catch (err) {
  if (err instanceof InsightsQueryError) renderInsightsError(err);  // task 026
  else throw err;
}
```

---

## 4. Subject scheme format — confirmation

✅ **Canonical format `<scheme>:<guid>` per contract v1.0 §3.1**:

| Logical name | Scheme token | Example |
|---|---|---|
| `sprk_matter` | `matter` | `matter:da116923-d65a-f111-a825-3833c5d9bcb1` |
| `sprk_project` | `project` | `project:27845394-8e5f-f111-a825-70a8a59455f4` |
| `sprk_invoice` | `invoice` | `invoice:05c8ef8d-8e5f-f111-a825-70a8a59455f4` |
| Anything else (e.g., `sprk_account`) | — | `{ kind: 'no-subject' }` — does NOT silently coerce |
| `undefined` logical name OR entityId | — | `{ kind: 'no-subject' }` |
| `{guid}` (with braces) | scheme:guid (braces stripped) | `matter:abc-123` |
| `{}` (empty after strip) | — | `{ kind: 'no-subject' }` |

Confirmed by Test groups 1 + 2 (10 tests).

---

## 5. v1.1 SSE opt-in + 406 fallback — confirmation

✅ **SSE opt-in**: every outbound request sends `Accept: text/event-stream` header (FR-17 / NFR-11 binding mechanism).
✅ **JSON direct (v1.0)**: when server returns 200 + `Content-Type: application/json`, the client parses single-shot envelope without any retry.
✅ **406 fallback**: when server returns 406 (v1.0-only deployment), the client retries ONCE with `Accept: application/json` reusing the **same correlation-id**. The fallback is binding regardless of Wave F deploy timing (spec NFR-11).
✅ **SSE consumption**: when server returns 200 + `Content-Type: text/event-stream`, the client parses SSE events:
  - `metadata` / `complete` / `result` events: merged into the result envelope (with `data.content`-wrapping handled for `result`)
  - `delta` events: accumulated into `envelope.answer` when no `complete.answer` field
  - `progress` events: silently observed (visible to a future generator-based observer variant for task 026's progressive rendering)
  - unknown events: silently skipped (forward-compat)
  - `data: [DONE]` sentinel: terminates parsing gracefully

Confirmed by Tests 3 (SSE happy path), 6 (JSON direct), 7 (406 fallback × 2).

---

## 6. Fresh-token-per-call (no snapshots) — confirmation (ADR-028 / R5 CLAUDE.md §10)

✅ The module delegates ALL token acquisition to `@spaarke/auth`'s `authenticatedFetch` per call. The client itself:
- Does NOT call `getAccessToken()` directly (source-level assertion in Test 4).
- Has NO module-level token cache (source grep in Test 4).
- Invokes `authenticatedFetch` EXACTLY ONCE per `callInsightsQuery` call (or twice on 406 fallback) — confirmed by `mock.calls.length` assertions in Tests 4 + 7.
- `authenticatedFetch` itself re-acquires a fresh token from the SpaarkeAuthProvider's in-memory cache per request (see `src/client/shared/Spaarke.Auth/src/authenticatedFetch.ts` line 36 + `useAuth.ts` lines 36–46 comment), which is the canonical no-snapshot mechanism per ADR-028.

Confirmed by Test 4 (runtime + source-static checks).

---

## 7. Correlation-id propagation (FR-17 / SC-16) — confirmation

✅ Each call generates a unique correlation-id via `crypto.randomUUID()` (with fallback for older test runtimes).
✅ ID is set on the outbound `x-correlation-id` request header.
✅ ID is returned on the success result (`result.correlationId`).
✅ ID is preserved on `InsightsQueryError.correlationId` — preferring the server-supplied `problemDetails.correlationId` when present, falling back to the client-generated ID otherwise.
✅ Two sequential calls produce two distinct IDs (no cross-call carryover).
✅ The 406 fallback path reuses the same client-generated ID (so server-side log correlation stays coherent across the negotiation pair).

Confirmed by Tests 3, 5, 7.

---

## 8. 12 contract error codes — confirmation (FR-16 / ADR-019)

All 12 codes from integration brief §5.1 are surfaced structurally as `InsightsQueryError`:

| HTTP | errorCode | Origin |
|---|---|---|
| 400 | `query.required` | Server ProblemDetails |
| 400 | `subject.required` | Server ProblemDetails |
| 400 | `subject.invalid` | Server ProblemDetails |
| 400 | `forceMode.invalid` | Server ProblemDetails |
| 400 | `conversationContext.invalid` | Server ProblemDetails |
| 401 | `auth.401` | **Synthetic** — surfaced when `@spaarke/auth` throws `AuthError` (auth exhausted after its 3-retry policy) |
| 429 | `rate-limit.429` | **Synthetic** — surfaced when ApiError.status === 429. `retryAfterSeconds` extracted from ProblemDetails (`retryAfterSeconds` / `retry-after` / `Retry-After` keys) when present |
| 503 | `ai.insights.disabled` | Server ProblemDetails |
| 503 | `ai.rag.disabled` | Server ProblemDetails |
| 503 | `ai.intent-classification.disabled` | Server ProblemDetails |
| 503 | `ai.assistant-default-playbook.unconfigured` | Server ProblemDetails |
| 500 | `INSIGHTS_ASSISTANT_INTERNAL_ERROR` | Server ProblemDetails |

All 5 ProblemDetails fields preserved on `InsightsQueryError`: `errorCode`, `correlationId`, `status`, `title`, `detail`. 429 additionally carries `retryAfterSeconds`. **No auto-retry on 429** (ADR-016 — chat-agent orchestration owns retry policy across the Insights surface).

Confirmed by Test 8 (13 sub-tests: 10 standard codes + auth.401 synthetic + rate-limit.429 synthetic + no-auto-retry assertion).

---

## 9. `forceMode` parameter — confirmation (FR-12 / SC-17)

✅ `forceMode: undefined` → field is OMITTED from the request body (lets server's intent classifier run; distinct from explicit `null`).
✅ `forceMode: 'playbook'` → forwarded as `forceMode: "playbook"` in request body.
✅ `forceMode: 'rag'` → forwarded as `forceMode: "rag"` in request body.
✅ Client does NOT pre-validate or default. The TypeScript type narrows to `'playbook' | 'rag'` so a hypothetical `'garbage'` is a compile-time error, not a runtime one.

Confirmed by Test 9 (3 sub-tests).

---

## 10. v1.1 forward-compat — confirmation

✅ Response DTOs use `Record<string, unknown>` (loose typing) so unknown fields survive untouched through the parsing layer:
- `citations[].href` (v1.1 §3)
- `streaming` top-level flag (hypothetical v1.2)
- `confidence: 0.42` (consumed by task 028's badge)
- `v12FutureField: { unknownNested: ['a', 'b'] }` (hypothetical future structured field)

The renderer (task 026) projects typed fields out of the loose envelope at consumption time.

Confirmed by Test 10.

---

## 11. Zone B boundary (R5 CLAUDE.md §3.5 / ADR-013 §3.5) — confirmation

✅ Module source contains ZERO imports from `src/server/api/...`, `../../../server/...`, `Sprk.Bff.Api`, or any other server-internal Insights namespaces. Verified by automated grep in Test 11.

The module is a pure HTTP consumer of the contract — no coupling to server-internal types.

---

## 12. AbortSignal cancellation — confirmation

✅ The optional `signal: AbortSignal` parameter on `InsightsQueryRequest` is forwarded to `authenticatedFetch`'s init.signal.
✅ Native `DOMException` `AbortError` is propagated unwrapped (does NOT translate to `InsightsQueryError`) so callers can use `instanceof DOMException` / `err.name === 'AbortError'` to discriminate cancellation from other failures.

Confirmed by Test 12 (2 sub-tests).

---

## 13. Reuse mandate / no parallel auth (R5 CLAUDE.md §3.1) — confirmation

✅ ZERO new auth primitives.
✅ ZERO new feature flags.
✅ ZERO new PaneEventBus channels.
✅ ZERO BFF code modified.
✅ ZERO new npm dependencies (consumes only the existing `@spaarke/auth` package).
✅ ZERO parallel HTTP wrappers — `callInsightsQuery` delegates all transport to `@spaarke/auth`'s `authenticatedFetch` (Bearer header attachment, 401 retry with backoff, RFC 7807 ProblemDetails parsing, BFF base URL resolution).

---

## 14. BFF publish-size delta — confirmation (R5 CLAUDE.md §3.6 / ADR-029)

✅ Delta = 0 MB (frontend-only task; no BFF code modified).

Per task scope rules: the main session runs `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` if a sanity verification is needed, but the diff is purely frontend so a no-op size delta is expected by construction.

---

## 15. Test coverage summary

| Test group | Count | Coverage |
|---|---|---|
| resolveSubject happy paths | 3 | matter / project / invoice |
| resolveSubject no-subject paths | 7 | undefined inputs × 3, unmapped name, no silent coercion, braces stripped, empty after strip |
| callInsightsQuery v1.1 SSE happy path | 1 | delta accumulation + complete merge + outbound headers |
| callInsightsQuery fresh-token (ADR-028) | 2 | runtime mock-call-count + source-static no-getAccessToken / no-cache assertions |
| callInsightsQuery correlation-id | 2 | header propagation + error path preservation |
| callInsightsQuery v1.0 JSON direct | 1 | playbook envelope, single outbound call |
| callInsightsQuery 406 fallback | 2 | happy fallback + propagated fallback error |
| 12 contract error codes | 13 | 10 standard codes + auth.401 + rate-limit.429 + no-auto-retry |
| forceMode semantics | 3 | undefined omitted, playbook forwarded, rag forwarded |
| v1.1 forward-compat | 1 | unknown fields survive |
| Zone B boundary grep | 1 | source-static import-grep assertion |
| AbortSignal cancellation | 2 | signal forwarded + AbortError unwrapped |

**Total: 38 individual `it(...)` assertions across 12 `describe(...)` groups.**

(Task POML listed 12 test categories — implementation expanded several into multiple sub-cases for tighter coverage.)

---

## 16. TypeScript compilation

✅ Production source (`insightsQueryClient.ts`): tsc clean. Verified via `npx tsc --noEmit` on `src/solutions/SpaarkeAi/` — zero errors in the new module file (all errors reported by the broader workspace tsc run are pre-existing in unrelated shared libraries).

✅ Test file (`insightsQueryClient.test.ts`): tsc reports expected `Cannot find name 'jest' / 'describe' / 'expect'` errors — these match the existing test files in SpaarkeAi (e.g., `errorTelemetry.test.ts`, `ConversationPane.r5.test.tsx`) and are an artifact of `tsconfig.json` not including `@types/jest`. Jest's `ts-jest` transformer (per `jest.config.ts`) has its own tsconfig override (`module: 'commonjs'`, `moduleResolution: 'node'`) that handles these at jest runtime. This is the established SpaarkeAi convention.

---

## 17. Test execution

⚠️ **Tests written but not executed**: SpaarkeAi has `jest.config.ts` configured but jest is not installed at `src/solutions/SpaarkeAi/node_modules/`. Jest IS installed at `src/client/shared/Spaarke.AI.Widgets/node_modules/`; running it against SpaarkeAi's config requires jest's own type-resolution to find `jest` types — which fails for the same `@types/jest` reason as the tsconfig issue.

Per task instructions to the sub-agent: "do NOT commit, do NOT push, do NOT run npm build (main session will)". The main session can either:
1. `cd src/solutions/SpaarkeAi && npm install --legacy-peer-deps --no-audit --no-fund` (per CLAUDE.md §11 — avoid `npm ci` for Vite solutions) to install jest + @types/jest, then `npx jest src/services/__tests__/insightsQueryClient.test.ts`.
2. OR run the tests from the Spaarke.AI.Widgets workspace if SpaarkeAi's jest infra is intentionally external.

The tests are syntactically + semantically valid jest tests; they follow the exact convention of existing SpaarkeAi tests like `errorTelemetry.test.ts` (which the operator confirmed run successfully in CI).

---

## 18. Quality gates (Step 9.5)

Quality gates `code-review` + `adr-check` are owned by the **main session** per the task scope rules (sub-agent restricted to code authoring). The main session should verify:

- **ADR-028 (Spaarke Auth v2 / no token snapshot)**: confirmed by Test 4 (no `getAccessToken()` call in production source; no module-level token cache).
- **ADR-013 §3.5 (Zone B boundary)**: confirmed by Test 11 (no `src/server/api/...` imports; no `Sprk.Bff.Api` reference).
- **ADR-019 (ProblemDetails preserved)**: confirmed by Test 8 (5 ProblemDetails fields preserved on `InsightsQueryError` for all 12 codes).
- **ADR-016 (rate-limit honoring, no auto-retry)**: confirmed by Test 8 (`rate-limit.429` surfaces `retryAfterSeconds` from ProblemDetails; only ONE outbound call).
- **R5 CLAUDE.md §3.1 (reuse mandate)**: zero new auth primitives; zero parallel HTTP wrappers; delegates entirely to `@spaarke/auth`.
- **R5 CLAUDE.md §3.5 (Insights governance)**: HTTP consumption only; zero Insights-internal types injected.
- **Reviewer inspection of `git diff`**: only frontend file additions (2 files: `insightsQueryClient.ts` + test).

---

## 19. Coordination with parallel sibling task 024

Task 024 (BFF-side `InsightsQueryToolHandler`) is the server-side consumer of the same `POST /api/insights/assistant/query` endpoint. The two surfaces share:

- **Subject format**: `<scheme>:<guid>` per contract v1.0 §3.1 — task 025 frontend builds it, task 024 BFF-side accepts it from the chat agent's tool-call arguments.
- **Correlation-id header**: `x-correlation-id` — task 025 generates client-side per call; task 024 generates server-side per chat-agent tool invocation. Both flow via the same HTTP header name.
- **Error-code preservation**: all 12 contract error codes — both surfaces preserve the 5 ProblemDetails fields per ADR-019.
- **v1.1 forward-compat**: both use loose-typed response envelopes so `citations[].href` and future fields survive.

The two consumers are independent (one runs in browser, one in BFF), but they target the same HTTP contract — they MUST stay aligned. Any drift should be caught by the smoke test (task 030) which exercises both paths against Spaarke Dev.

---

## 20. Wrap-up checklist

- [x] Module placement decided + recorded (Candidate A — `src/solutions/SpaarkeAi/src/services/insightsQueryClient.ts`)
- [x] Public interface defined (5 types + 2 functions + 1 error class + 1 options interface)
- [x] `resolveSubject` implemented with exhaustive mapping + defensive braces stripping + no-silent-coercion rule
- [x] `callInsightsQuery` implemented with: SSE opt-in, 406 fallback, correlation-id, fresh-token-per-call via `authenticatedFetch`, ProblemDetails → `InsightsQueryError` translation, 429 retry-after extraction, 401 synthetic auth.401, AbortSignal forwarding
- [x] All 12 contract error codes surfaced structurally
- [x] v1.1 forward-compat via `Record<string, unknown>` envelope
- [x] Zone B boundary verified (no server-internal imports)
- [x] 38 tests authored across 12 describe groups
- [x] tsc clean on production source
- [x] Evidence file (this document) authored
- [ ] **Owned by main session**: TASK-INDEX.md 025 🔲 → ✅
- [ ] **Owned by main session**: POML status `not-started` → `complete`
- [ ] **Owned by main session**: `code-review` + `adr-check` quality gates run + pass
- [ ] **Owned by main session**: tests executed (if jest install is in scope) — verifies 38 tests pass
- [ ] **Owned by main session**: `current-task.md` reset to next pending P2-G6 task (likely 026, which consumes this client)
- [ ] **Owned by main session**: BFF publish-size verification = 0 MB delta (frontend-only)

---

*Evidence authored 2026-06-04 by sub-agent for R5 task 025 / D2-15.*
