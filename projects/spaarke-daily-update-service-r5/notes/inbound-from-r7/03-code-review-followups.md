# 03 — R7 code-review follow-ups

> **Priority**: MEDIUM — code quality
>
> **Source**: R7 W12 scoped `/code-review` on the 2026-06-30 diff (evening batch of operator feedback fixes)
>
> **Scope**: 5 items surfaced by code-review but not fixed in R7 to keep the batch focused. All are shipped-but-suboptimal — nothing broken, but each has a specific risk or debt worth resolving in R5.

## 1. Revert collector membership-resolver bypass

**Where**: `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs`

**What R7 did**: Introduced an Owner-only bypass path when the polymorphic-Owner attribute couldn't be resolved by `MembershipFieldDiscoveryService`. This was needed because `LookupAttributeMetadata` is sealed and `OwnerAttributeMetadata` doesn't inherit from it — the resolver silently returned zero rows on Owner-based membership fields.

**What R7 also did**: `MembershipFieldDiscoveryService.ProjectLookupAttributeRows` was fixed to synthesize Owner + Customer targets from base `AttributeMetadata`. Root-cause fix.

**Debt**: The bypass path in the collector is still in place. With the root-cause fix, Owner-only queries via the bypass silently lose collaborator scope (assigned attorneys, paralegals, etc.). A user who is not the record Owner but IS an assigned collaborator won't see the record in their Daily Briefing.

**R5 action**: Remove the bypass; rely on the fixed `MembershipFieldDiscoveryService`. Add a smoke test that a `sprk_assignedattorney1` user sees a matter they're assigned to (not owning).

**Severity**: Medium — silently under-scopes for the collaborator role. Not a critical failure but user-visible.

## 2. Author unit tests for new client-side surfaces

**What R7 shipped without tests**:

- `NarrativeCitedText.buildSegments` — the Perplexity-style inline citation segment splitter (regex-based overlap detection). Non-trivial logic; regression-prone.
- `HighPrioritySection.classifyDueDate` + `actionToBadge` helpers — the date classification (Overdue/DueToday/DueSoon/Recent) has date-boundary edge cases.
- `useBriefingRender.isEmptyResponse` — determines whether to render "no data" state; edge cases when partial data is present.
- `useInlineTodoCreate` primary-contact wiring — sequence of retrieveMultipleRecords → set `sprk_AssignedTo@odata.bind`; failure paths not tested.

**R5 action**: Add Jest tests for each surface. Follow the existing test conventions in `Spaarke.DailyBriefing.Components/__tests__/`. Aim for full branch coverage of the segment splitter and date classifier; smoke coverage of the hook.

**Severity**: Medium — no bugs known but the surfaces are complex enough that regression is a matter of time without test coverage.

## 3. Metadata-drive the 7 QueryHighPriority helpers

**Where**: `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs`

**What R7 shipped**: 7 helper methods — `QueryHighPriorityMattersAsync`, `QueryHighPriorityProjectsAsync`, `QueryHighPriorityInvoicesAsync`, etc. — each 20-30 lines of nearly identical code differing only in entity name, description column name, and name column.

**Debt**: Six near-identical helpers means every change (add a new field to the query, change ordering, adjust filter logic) has to be replicated seven times. First bug I introduced was a case-sensitivity fix that required editing seven near-identical call sites; this is the same debt in the collector.

**R5 action**: Collapse into a single `QueryHighPriorityAsync(HighPriorityEntitySpec spec)` method + `record HighPriorityEntitySpec(string EntityName, string NameColumn, string DescriptionColumn, string RegardingContext?)` array. Each entity registers as one row in the array.

**Severity**: Medium — code quality, no bug. But the debt compounds as the DailyBriefing feature grows.

## 4. Fix `useInlineTodoCreate` primary-contact lookup race

**Where**: `src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useInlineTodoCreate.ts`

**What R7 shipped**: `primaryContactRef` caches the resolved contact ID from `sprk_primarycontact` lookup so subsequent `createTodo` calls don't re-query. Cache is populated on first `createTodo` invocation (lazy).

**Bug**: The ref caches the **resolved value** (`string | null | undefined`). If two `createTodo` calls fire concurrently before the first resolves, both issue duplicate `retrieveMultipleRecords` lookups (both see `ref.current === undefined`).

**R5 action**: Cache a `Promise<string | null>` in the ref instead. First call sets the ref to the promise; subsequent calls await the same promise. Standard promise-caching pattern.

**Severity**: Low — worst case is duplicate lookups (wasted Web API calls). No incorrect behavior. Priority is code-quality + Web API budget hygiene.

## 5. Doc/code inconsistency: `bulletToNotificationItem` truncation comment

**Where**: (search codebase — was flagged in code-review, exact file needs confirmation)

**What R7 shipped**: A comment reading "truncated to 197 chars for `sprk_todo.subject` per max-length" — but the actual truncated field is `sprk_name`, not `subject` (there's no `subject` column on `sprk_todo`).

**R5 action**: Fix the comment. Even better, replace the magic number 197 with a call that reads `MaxLength` from `sprk_todo.sprk_name` metadata (via a metadata query or a shared constant sourced from schema).

**Severity**: Low — doc quality; misleading comment. Not a bug.

## Summary matrix

| # | Item | Severity | Est. effort |
|---|---|---|---|
| 1 | Revert collector membership-resolver bypass + smoke test | Medium | 2-3 hrs |
| 2 | Unit tests for 4 client-side surfaces | Medium | 4-6 hrs |
| 3 | Metadata-drive 7 QueryHighPriority helpers | Medium | 3-4 hrs |
| 4 | Primary-contact Promise cache | Low | 30 min |
| 5 | Fix truncation comment / read metadata max-length | Low | 30 min |

Total: ~10-14 hrs. Small enough to bundle as a "R5 tech-debt sweep" phase if design.md doesn't have a natural home for it.

## References

- R7 W12 shipping commit: `f6938617a`
- Code-review output: not persisted (ran interactively during R7 W12 evening session)
- Collector: [`src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs)
- Hook: [`src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useInlineTodoCreate.ts`](../../../../src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useInlineTodoCreate.ts)
- Section: [`src/client/shared/Spaarke.DailyBriefing.Components/src/components/HighPrioritySection.tsx`](../../../../src/client/shared/Spaarke.DailyBriefing.Components/src/components/HighPrioritySection.tsx)
