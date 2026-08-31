# ISSUE — `recordHeader.integration.test.tsx` has been red since v1.0.10

> **Diagnosed**: 2026-08-25 by task **015**, confirmed in main session
> **Status**: ✅ **RESOLVED 2026-08-26 by task 034** — suite rewritten, **11/11 green**
> **Severity**: medium — a permanently-red suite trains people to ignore the gate
> **File**: `src/client/shared/Spaarke.UI.Components/src/__tests__/recordHeader.integration.test.tsx`

---

## ✅ Resolution (2026-08-26, task 034)

Rewritten to the post-v1.0.10 contract and **maintained, not deleted** — as this note recommended.
**2/10 → 11/11.** Repo-wide known-red drops from 9 suites to **8**.

The diagnosis below was correct but **incomplete**: the suite carried **five** stale contracts, not
one. The four additional ones only surfaced once the first was fixed:

| # | asserted | shipped reality |
|---|---|---|
| 1 | `sparklePopoverOpen` / `sparklePopoverContent` | removed at v1.0.10 (this note's diagnosis) |
| 2 | badges read `@odata.count` | `Xrm.WebApi` strips it — `useRelatedCount` counts `entities.length` |
| 3 | `pageInput.name` | `webresourceName` |
| 4 | `pageInput.data` is an object | URL-encoded **string** |
| 5 | sparkle named "AI Summary" | **"View AI summary"** |

The through-line is worth keeping: in **every** case the source was right, had already been
corrected once, and **documented its own correction in a comment** — #2 and #4 inside the very file
the test was testing. The tests were believing stale prose over adjacent code. Two misleading
comments were deleted as part of the fix so the next reader cannot repeat it.

Full write-up: [`notes/decisions/034-sparkle-existence-gate.md`](../decisions/034-sparkle-existence-gate.md).

---

## Original diagnosis (retained)

---

## Why this is filed, not fixed

Three Wave-1/2 agents independently proved this suite is **not** a regression from R2:

- Task **014** reverted `TextField.tsx` via `git stash` and reproduced the identical 8/10 failures.
- Task **013** swapped in the pre-R2 `OptionSetField.tsx` (blob `801bca67e`) and reproduced them again.
- Task **015** then found the actual cause (below) rather than stopping at "not us".

That is the useful distinction: *isolation* proved innocence; *diagnosis* found the defect.

## Root cause

The suite asserts a hook contract that was removed at **v1.0.10**:

1. It destructures `sparklePopoverOpen` and `sparklePopoverContent` from
   `useRecordHeaderToolbarActions`. The hook's own JSDoc documents both as removed at v1.0.10 —
   it now returns **only** `{ toolbarProps }`.
2. It expects an "AI Summary" toolbar button. `HeaderToolbar.tsx:157` gates that button on an
   `aiSummary` prop, and `git show HEAD:...useRecordHeaderToolbarActions.ts | grep -c aiSummary`
   → **0**. The hook does not supply it.

The architecture moved deliberately: the **consumer** now composes `aiSummary` and merges it into
`toolbarProps` (see `MatterHeaderView.tsx`'s `toolbarPropsWithSparkle`). The sparkle is no longer the
hook's concern. The test was never updated to follow.

So it is not asserting a broken product — it is asserting an obsolete design.

## Why it should not be silently deleted

The integration coverage it *intends* to provide is real: it is the only suite exercising the
shell + toolbar + renderer composition end to end. Deleting it would remove genuine coverage to make
a number go green. Per ADR-038 the question is build-vs-maintain, and the honest answer is
**maintain, after rewriting to the post-v1.0.10 contract** — not delete.

## Recommended owner: task 034

[`034-sparkle-recordsummary-wiring.poml`](../../tasks/034-sparkle-recordsummary-wiring.poml) is the
task that wires the sparkle to `sprk_recordsummary`. It is already the place where the
consumer-composes-`aiSummary` contract gets touched, so it is the natural point to rewrite this
suite against the real shape. Doing it earlier would mean guessing at a contract task 034 is about
to settle.

**Until then**: treat this suite as a known-red. It is excluded from the "wave is green" judgment,
and every wave report should say so explicitly rather than quietly reporting a lower total.

## Current state (2026-08-25, post Wave 2)

Full shared-lib suite: **2851 passed / 22 failed across 10 suites**. After correcting two genuinely
stale assertions in `toolbarLaunchDefaults.test.ts` (see below), 9 suites remain red. All nine are
outside R2's file scope:

`recordHeader.integration` · `TimelineComposeBox` · `ConversationView.forward` ·
`ConversationView.emailInFlow` · `RichFilePreview` · `buildDynamicWorkspaceConfig` ·
`EntityCreationService.cascade` · `surfaceLaunchRegistry` · `todoScoreMappings`

All nine RecordHeader suites pass (**232/232**), including the 91 new contract tests.

## Related — two stale assertions already corrected

Not the same defect, but the same *class*, and fixed in Wave 2 because they sat in a file task 024
already owned:

| Assertion | Test expected | Reality | Evidence |
|---|---|---|---|
| `NOTEPAD_MODAL` | 70% × 80% | **25% × 35%** | R1 v1.0.7 deliberately shrank it; documented in `MatterHeaderView.tsx` |
| `NOTEPAD_WEBRESOURCE_NAME` | `sprk_notepad_page` | **`sprk_notepad`** | Live query of `spaarkedev1`: `sprk_notepad` ("Notepad HTML"); the `_page` suffix was assumed, never shipped |

The second is precisely the trap
[`pcf-build-scaffold.md` gotcha #9](../../../../.claude/patterns/pcf/xrm-webapi-related-count.md)
warns about — the SmartTodo name in the very same file carries a comment recording the identical
mistake. Verify webresource names against Dataverse; never infer them.
