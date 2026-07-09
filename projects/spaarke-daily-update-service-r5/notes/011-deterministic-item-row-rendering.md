# Task 011 — Deterministic item-row rendering (FR-A1)

**Status**: implementation complete, verified. Task-status files (TASK-INDEX.md, current-task.md, the POML `<status>`) intentionally left untouched per the invoking instruction — the orchestrator will finalize those.

## Scope

`src/client/shared/Spaarke.DailyBriefing.Components/**` only. No server file touched.

## What changed

- **`src/components/NarrativeBullet.tsx`** — removed the `references?: NarrativeBulletReferenceResult[]` prop and its conditional branch (the R7 W12 "LLM narrative + citations" mechanism). The component now always renders through `NarrativeCitedText` for the narrative text, and renders its own separate regarding-name link only when `NarrativeCitedText`'s deterministic match (`hasInlineRegardingMention`) finds the regarding name is NOT textually present in the narrative — avoiding ever rendering the same entity's link twice for one row. `narrative` + `primaryEntityName`/`Type`/`Id` are documented as always originating from the SAME bullet/item.
- **`src/components/NarrativeCitedText.tsx`** — rewritten. `buildSegments` (the pure text-splitter, task 035) is byte-identical to preserve `test/NarrativeCitedText.buildSegments.test.ts`. The component's PROPS changed from an externally-supplied `references[]` array to three scalars (`regardingName`/`regardingEntityType`/`regardingId`) matching the item's own regarding fields — removing the theoretical vector for cross-item data to enter. Added `hasInlineRegardingMention` (exported) as the single source of truth for "was the name found inline," reused by `NarrativeBullet` to decide whether to render its own fallback link.
- **`src/components/ActivityNotesSection.tsx`** — stopped passing `references={bullet.references}`; removed the now-unused `references?` field from the local `ChannelNarrativeBullet` type. JSDoc updated to describe deterministic, item-sourced bullets (not "AI-generated").
- **`src/services/briefingService.ts`** — removed the unused `references?` field from `NarrativeBulletResult` (nothing client-side reads it since `BuildDeterministicBullet` never populates it server-side). Kept `NarrativeBulletReferenceResult` (still used internally by `buildSegments`'s type signature).
- **Tests**: extended `test/NarrativeBullet.test.tsx` with a new `describe` block (regarding-link click resolves to the row's own entity; inline-mention rendering; two-row cross-item-isolation test; source-scan proving no `references` consumption). Added `test/NarrativeCitedText.test.tsx` (component-level: plain text / inline-link / no-op click / two-instance isolation / dark-mode hex scan / `hasInlineRegardingMention` unit tests). Added `NarrativeCitedText.tsx` to the existing dark-mode hex-literal scan list in `NarrativeBullet.test.tsx`.

## Key implementation decision (deviation from a literal reading of the POML)

The POML's constraint text suggested `NarrativeCitedText` could render a trailing "[N]"-style citation for the not-inlined case (mirroring the old multi-mention design). Implementing that literally would have caused **the same entity's link to render twice** for one row (once from `NarrativeCitedText`'s own fallback, once from `NarrativeBullet`'s existing separate link line) — a correctness regression, not an improvement. Resolved by making `NarrativeCitedText` own ONLY the inline-mention case; the not-found fallback link remains solely `NarrativeBullet`'s responsibility (unchanged from pre-task visual behavior). This was verified empirically against the existing 20+ `NarrativeBullet.test.tsx` cases (all green) before being finalized.

## Verification

- `npm test` (Jest): 187 total, 177 passed, 3 failed (same 3 pre-existing failures — `test/legalWorkspaceSectionRegistry.test.ts` module-resolution + `test/ActivityNotesSection.callbacks.test.tsx` stale "Add to To Do" menu-item wording / TTL fixture bug, both unrelated to this task and confirmed unrelated by direct inspection of the failure output). No new failures.
- `npm run build` (`tsc --noEmit`): 4 pre-existing `@spaarke/ui-components` / `@spaarke/auth` module-resolution errors (unbuilt sibling workspace packages in this environment) — confirmed identical before/after this task's edits, and none in a file whose logic this task touched.
- ADR-021: all touched files use Fluent v9 semantic tokens exclusively; static hex-literal scan (existing + extended to include `NarrativeCitedText.tsx`) passes; dark-mode render tests pass in both `NarrativeBullet.test.tsx` and the new `NarrativeCitedText.test.tsx`.

## Note on workspace state

During verification a `git stash`/`stash pop` round-trip was needed to compare a `tsc` baseline; this incidentally surfaced pre-existing **uncommitted** changes in the worktree unrelated to this task (server-side `DailyBriefingEndpoints.cs`, `DailyBriefingCollector.cs`, `DailyBriefingNarrator.cs`, associated tests, and `notes/013-tldr-scaffolding-and-publish-size.md` — apparently in-flight task 013 work). These were restored exactly as found via `git stash pop`; nothing was reverted or altered. Not touched by this task.
