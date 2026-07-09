# Task 037 — Promise-cache the primary-contact lookup + fix the truncation comment

> **Date**: 2026-07-09 · **FR-C8** · notes/inbound-from-r7/03 items 4 + 5 · depends on 036 ✅

## File resolution (step 1 — both fixes are CLIENT-side, confirmed before editing)

The R7 note flagged item 5's location as "search codebase — needs confirmation." Resolved:

| Fix | File | Was |
|---|---|---|
| Primary-contact promise-cache (item 4) | `src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useInlineTodoCreate.ts` | `primaryContactRef` cached the **resolved value** (`string \| null \| undefined`) |
| Truncation comment (item 5) | `src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx:132` (`bulletToNotificationItem`) | comment said `sprk_todo.subject` — a column that does not exist |

Both client-only. **No BFF `.cs` changed** → no publish-size verification applies. (The collector was inspected per the note's "confirm which layer" instruction; the primary-contact caching debt is the client hook, not `DailyBriefingCollector`.)

## Fix 1 — promise-cache (item 4)

`primaryContactRef` now holds `Promise<string | null> | undefined` — the **in-flight lookup**, not the resolved value. First `createTodo` assigns the promise via `??=`; concurrent callers short-circuit the `??=` and `await` the same promise. So the `retrieveRecord('systemuser', …)` lookup fires **exactly once** per hook lifetime even when two `createTodo` calls race before the first resolves.

**Rejection semantics (constraint choice, stated):** the cached async lookup **catches internally and resolves to `null`** on a missing field or failed query — it never rejects. Caching it permanently therefore preserves the prior "cache null on failure, no retry for this hook's lifetime" behavior exactly (no permanently-cached *rejected* promise; a fail-soft resolves-to-null promise is cached, which is the same observable outcome as before). No retry was expected in the pre-existing code, so none is introduced.

## Fix 2 — truncation comment (item 5)

`DailyBriefingApp.tsx:132` comment corrected: the trimmed field is `sprk_todo.sprk_name` (the created To Do maps `item.title` → `sprk_name` in `useInlineTodoCreate`), not the non-existent `sprk_todo.subject`. Grep confirms **no remaining "subject"** in the comment. The `197` magic number is retained (constraint-permitted) with the `197 + "..." = 200` relationship now explained; no shared `MaxLength` constant exists for `sprk_name` (grep) and the constraint says not to add a metadata round-trip just for this.

## Guard (concurrency test)

New file `test/useInlineTodoCreate.promiseCache.test.ts`:
- `resolves the primary-contact lookup exactly once under two concurrent createTodo calls` — a **deferred** `retrieveRecord` stays pending while two `createTodo` calls start, then is released; asserts `retrieveRecord` called **once** and both todos bind the same `/contacts(contact-123)`. A resolved-value cache fails this test (two lookups); the promise cache passes.
- `caches the resolved contact across sequential creates — second create issues no new lookup` — pins the existing lifetime-cache behavior (1 lookup across 2 sequential creates).

Mocks `IWebApi` (the module boundary, per the existing `useInlineTodoCreate.test.ts` pattern) — not `HttpMessageHandler`. Maintain-class: guards a real concurrency defect (duplicate lookups) that would otherwise silently regress.

## Verification

- **Jest**: `useInlineTodoCreate` suites → **3 suites / 12 tests pass** (existing 6 + computeDueDate + 2 new concurrency tests). ✅
- **tsc**: the 4 pre-existing `@spaarke/*` peer-dependency module-resolution errors (standalone-typecheck limitation) remain; **my edits introduce zero new type errors** (the `??=` + closure narrowing typecheck clean; jest resolves the `@spaarke/*` mocks via `moduleNameMapper`).
- **Grep**: no "subject" in the truncation comment. ✅
- **Functional invariance**: same contact bound (`sprk_AssignedTo@odata.bind`), same 197-char truncation length — no briefing-output change.
- **BFF size / CVE**: client-only change → no BFF publish-size delta, no dependency change. ✅

## Placement (§10 / §11)

Client-only edits to an existing hook + an existing helper comment; one new test file (a test, not a shipped component — §11 new-component gate does not apply). No new service/package/DI/endpoint.
