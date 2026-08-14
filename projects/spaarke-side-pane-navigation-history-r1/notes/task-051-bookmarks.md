# Task 051 — Bookmarks: deviations + design decisions

Both gestures ("Pin this page" — captured, "+ Add bookmark" — manual) write
`sprk_type=pin` `sprk_navitem` rows via the task-050 `pinService.pin`/
`navItemRepository` write path. No new write surface was introduced; every
decision below is about how the EXISTING path was extended or reused.

## 1. Additive `navItemRepository.ts` extension (as anticipated by the task)

- `CreatePinItemInput.source?: number` — lets a caller override `sprk_source`
  (defaults to `Manual`, unchanged for every existing 041/050/052 caller).
  `bookmarkService.pinCurrentPage` is the first caller to pass `Captured`.
- `createWeblinkPinItem(input)` — a new, SEPARATE function (not a
  `CreatePinItemInput` variant) for a raw weblink pin: no
  `targetLogicalName`/`targetId` identity, `sprk_url` carries the URL. Kept
  separate rather than widening `CreatePinItemInput.targetLogicalName`/
  `targetId` to `string | null`, which would have weakened type-safety for
  every other `createPinItem` caller (they rely on those fields always being
  present, non-null strings).

Both changes are additive-only; `RecentTab.tsx`'s existing `createPinItem`
call site (task 041 star-promote) is untouched and behaves identically.

## 2. Records vs. Bookmarks group partition (PinnedTab.tsx)

The pre-051 `PinnedTab.tsx` docblock said Bookmarks would render in "its own
`<section>`, with its own load/render logic." In practice, the Bookmarks
group does **not** issue a second `Xrm.WebApi` query — `listPinItems`
already returns every `sprk_type=pin` row for the user regardless of
`sprk_pagetype`, and chip mapping for `EntityList`/`Custom`/`WebLink` rows
already existed (unused pre-051, since no gesture created those pagetypes
before now). The Bookmarks group reuses the SAME `rows` state, partitioned
client-side:

- **Records** = `sprk_pagetype === EntityRecord`. A star-pinned record and a
  "Pin this page"-captured record are the same thing (both ARE personal pins
  of a Dataverse record), so they share the Records group regardless of
  gesture/`sprk_source`.
- **Bookmarks** = everything else (`EntityList`/`Custom`/`WebLink`) — never a
  "record" in the Dataverse sense.

This is a smaller, simpler change than adding a second read, and it means a
target pinned via the star gesture, "Pin this page", or a pasted record URL
all converge on the SAME row (dedup via `pinService.pin`) if it's the same
target — no triplicated pins for the same record.

## 3. `navigateToRow`'s WebLink case: `openUrl` → `window.open(...,'noopener')`

`navigateToRow`'s `WebLink` case existed before 051 (dead branch — no
gesture created weblink rows yet) and called `xrm.Navigation.openUrl(url)`.
The task explicitly specifies `window.open(url, '_blank', 'noopener')` for
weblinks, so that branch was changed to match. This is scoped to the
`WebLink` pagetype only (checked via an early return before the
`Xrm.Navigation`-based switch) — every logical (Dataverse) target still
navigates exclusively via `Xrm.Navigation.navigateTo`, never a raw URL.

## 4. EntityList navigation now passes `viewId`

`navigateToRow`'s `EntityList` case previously called
`navigateTo({pageType:'entitylist', entityName})` with no `viewId` (never
exercised before 051, since no gesture produced `EntityList` pins). A
view-kind bookmark (task 051) stores the saved view's `viewid` as
`sprk_targetid` — mirroring `ViewsTab.tsx`'s `navigateToView`, this is now
passed through as `viewId` so clicking a view bookmark opens THAT view, not
just the generic entity list. Additive — `sprk_targetid` being absent
(`undefined`) on any pre-051 row is a no-op for `navigateTo`.

## 5. `urlParse.ts` decision order (closed, 4-branch)

1. Not a syntactically valid URL, or not `http(s)` → `reject`.
2. `viewid` param present → `view` (checked BEFORE `etn`+`id`, so a URL
   carrying both takes the view branch — real MDA entitylist URLs never
   carry `id` anyway).
3. `etn` + `id` both present → `record`.
4. Otherwise → `weblink` (raw URL fallback) — this INCLUDES a Dataverse-
   looking-but-incomplete URL (e.g. `pagetype=entityrecord&etn=...` with no
   `id`), which is intentional: it's still a valid, storable `http(s)` URL,
   just not one the parser can resolve to a labeled target.

## 6. `addBookmark`'s `view` branch without `etn` (rare edge case)

A `viewid` URL that somehow omits `etn` (real MDA entitylist links always
pair the two) has no clean target-entity identity to dedupe on. Rather than
inventing a placeholder `targetLogicalName`, this case falls back to
`createWeblinkPinItem` with the raw MDA URL — it still resolves and opens
correctly (as a weblink), just without a labeled logical target. This is
believed to be effectively unreachable in practice.

## 7. Record/view display-name resolution added during code-review pass

`addBookmark`'s `record` branch originally used a generic
`formatEntityFallbackLabel(etn)` label (e.g. every matter bookmarked via
paste would show as "Matter", indistinguishable from one another). Added
`resolveRecordDisplayName` (mirrors `navigatorCaptureService.ts`'s
`resolveDisplayName` — `getEntityMetadata` + `retrieveRecord`, never throws,
falls back to the generic label on any failure) so a manually-pasted record
URL bookmark shows the record's actual primary name.

The `view` branch (both `pinCurrentPage`'s captured entitylist case and
`addBookmark`'s manual view case) intentionally keeps the generic
`"{Entity} view"` fallback rather than adding a `ViewService.getViewById`
call: `getViewById` only resolves `savedquery` (system) views, not
`userquery` (personal) views — and this Navigator's own Views tab
(`ViewsTab.tsx`) is userquery-only, meaning the exact views a user is most
likely to bookmark would silently fail to resolve a name anyway, producing
inconsistent UX (system views get a name, personal views don't, for no
visible reason). Kept simple and predictable instead.
