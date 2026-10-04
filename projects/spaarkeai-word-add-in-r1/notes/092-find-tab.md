# Task 092 — Find tab: one scroll bar, two independent sections, open actions, match reasons

> UAT round 3 (2026-10-03), `notes/042-uat-round3-2026-10-03.md` §1, UAT-3 + UAT-8.
> **Rigor**: FULL · **Model tier**: sonnet @ high · **Step mode**: directional
> **Parallel wave**: UAT3-W1, alongside task 088 (Save tab). This task touched ONLY
> `FindView.tsx`, `FindResultsList.tsx`, `useFindRecordMatches.ts` and `App.tsx` (Find props only);
> `SaveFlow.tsx`, `SaveView.tsx`, `openRecordLauncher.ts`, `webpack.config.js`, the deploy workflow and
> all `.cs` files were left untouched (088's surface).

## 1. What changed

| File | Change |
|---|---|
| `src/client/office-addins/shared/taskpane/components/FindResultsList.tsx` | Rewritten. Split into two independent components (`DocumentsSection`, `RecordsSection`) rendered unconditionally inside ONE shared scroll container (`find-results-scroll-area`). Removed the `maxHeight: '360px'` cap. Added `extractHubRecordId` (safe id extraction for hub/parent rows), `renderMatchReason` (safe `<em>` → bold, any other tag dropped), confidence % + up to 3 match reasons on `RecordRow`, and `onOpenRecord` wiring for document/hub/record rows. |
| `src/client/office-addins/shared/taskpane/components/views/FindView.tsx` | `styles.container` no longer sets `overflow: 'auto'` (now `minHeight: 0` only) — `FindResultsList` owns the single scroller. The `'indexed'` case now ALWAYS renders `FindResultsList` (previously gated on `relatedState.kind === 'loaded'`). Added `canOpenRecord` prop, `openRecordAvailable` gate, `handleOpenRecord`, and `toDocumentsResultState` mapper. |
| `src/client/office-addins/shared/taskpane/hooks/useFindRecordMatches.ts` | `RecordMatch` gained `confidenceScore: number` (required, mirrors the server's non-nullable `double`) and `matchReasons?: string[] \| null`. No fetch-logic change — the hook already passes through whatever the server returns. |
| `src/client/office-addins/shared/taskpane/App.tsx` | `<FindView>` now receives `canOpenRecord={hostAdapter.getCapabilities().canOpenBrowserWindow}` (same pattern as `SaveView`'s `canOpenRecord`). No other change. |
| `src/client/office-addins/shared/taskpane/components/views/__tests__/FindView.test.tsx` | Extended: fixed 4 pre-existing assertions that relied on the "heading only appears once loaded" assumption (no longer true — the heading is now unconditional); added describe blocks for independent records loading/error, opening a row (document/hub, gated on `canOpenRecord` + `ORG_URL`), and confidence/reasons end-to-end. |
| `src/client/office-addins/shared/taskpane/components/__tests__/FindResultsList.test.tsx` | Rewritten to the new `documents`/`onOpenRecord` prop contract. Covers: single scroll container, independent per-section loading/error/empty, hub-id extraction (valid matter/project/invoice/email vs. the non-openable `thread-` shape), keyboard operability, confidence + match reasons, and the security negative test (script/img tags never become elements). |
| `projects/spaarkeai-word-add-in-r1/notes/092-find-tab.md` | This file. |

## 2. Architecture decisions

### 2.1 Single scroll container (UAT-3)

Three nested `overflow`/height layers existed before this task: `TaskPaneShell.content` (`flex:1; overflow:auto`, unchanged — explicitly out of scope per the task's own constraint), `FindView`'s own container (`height:100%; overflow:auto`), and `FindResultsList`'s inner `scrollArea` (`overflowY:auto; maxHeight:'360px'`). The fix removes the MIDDLE layer's `overflow` (FindView's container now just participates in the `flex:1/minHeight:0` chain without scrolling itself) and removes the FIXED HEIGHT CAP on the innermost layer, which now owns the one active scroller (`flex:1; minHeight:0; overflowY:auto`, no `maxHeight`). `TaskPaneShell` was not touched, so Save/To Do tab scrolling is unaffected.

### 2.2 Two independent sections, not nested (UAT-3 + UAT-8)

Before this task, `FindView` only mounted `FindResultsList` once the documents fetch reached `relatedState.kind === 'loaded'` — so records (which load independently via `useFindRecordMatches`) never appeared while documents were loading or had failed. `FindResultsList` is now ALWAYS rendered in the `'indexed'` state; it owns both the "Most similar documents" and "Matching records" headings and renders each section's own loading/error/empty state independently. Neither section's render path reads the other's state.

### 2.3 Opening a row — one callback, three callers

`FindResultsList` takes a single `onOpenRecord?: (entityType: string, recordId: string) => void`, used by document rows (`sprk_document` + `cleanGuid(node.id)`), hub/parent rows (via `extractHubRecordId`), and matching-record rows (`record.recordType` + `record.recordId`). `FindView` is the only caller; it gates the prop on `canOpenRecord && Boolean(process.env.ORG_URL)` (same `openRecordAvailable` pattern as `SaveFlow`) and calls the SAME `openRecord` from `services/openRecordLauncher.ts` — no new URL-building code, per the task's explicit constraint ("task 088 adds the Spaarke app name inside the builder — call `openRecord`, never build a URL here").

**Finding not in the task's background, found while implementing**: a hub node's `id` is server-prefixed (`matter-{guid}`, `project-{guid}`, `invoice-{guid}`, `email-{guid}`) — confirmed by reading `VisualizationService.CreateParentHubNode` (`src/server/api/Sprk.Bff.Api/Services/Ai/Visualization/VisualizationService.cs:825-908`) — EXCEPT the `SameThread` relationship branch, which produces `thread-{truncatedConversationIndex}`, typed `email` exactly like the openable case, with **no real record id at all**. `extractHubRecordId` strips the prefix matching the node's own `type` and validates the remainder is GUID-shaped (via `cleanGuid` + a GUID regex) before offering an open action; a `thread-` hub degrades to plain text rather than building a broken deep link — the same "never open a link that can't resolve" discipline `openRecord` itself already applies. An "email" hub opens as `sprk_document` (the parent email file), not a separate "email" entity — confirmed by reading `CreateParentHubNode`'s `SameEmail` branch, whose `RecordUrl` is built via the exact same `BuildRecordUrl(documentId)` plain document rows use (`etn=sprk_document`).

### 2.4 Match-reason rendering (security constraint)

`renderMatchReason` (`FindResultsList.tsx`) splits a reason string on literal `<em>…</em>` spans (Azure AI Search's own highlight pre/post tags — plain, no attributes), renders the span content as `<strong>`, and strips ANY other tag syntax (`/<[^>]*>/g`) from both the surrounding text and (defense-in-depth) the `<em>` content itself — never `dangerouslySetInnerHTML`. Every piece of text returned is a plain JS string passed as React children, so even an unstripped payload could never execute; the stripping satisfies the "drop any other tag" wording on top of that structural guarantee. Up to 3 reasons are shown per record (the server returns up to 5).

## 3. Acceptance criteria — status

| # | Criterion | Status | Evidence |
|---|---|---|---|
| 1 | Two headed sections, each with own loading/empty/error | PASS | `FindResultsList.test.tsx` "Documents section —/Matching records section — independent loading/error/empty state" |
| 2 | Matching records render while documents load/fail | PASS | `FindResultsList.test.tsx` same block; `FindView.test.tsx` "Matching records render independently of the Documents section" |
| 3 | No `maxHeight:360px`; fills height; single scroll container test | PASS | `FindResultsList.test.tsx` "the single scroll container" (source-level regression guard + structural single-wrapper assertion) |
| 4 | Open document/parent/record rows via `openRecord`; capability-gated | PASS | `FindResultsList.test.tsx` "opening a row"; `FindView.test.tsx` "opening a row (task 092, UAT-3, NFR-10)" |
| 5 | Confidence % + up to 3 reasons, `<em>` bold, other markup as text | PASS | `FindResultsList.test.tsx` "matching records show confidence…" + "renderMatchReason (pure)" + SECURITY test |
| 6 | Lazy loading still fetches/reveals per section | PASS | Pre-existing `useLazyResults`/`useFindRecordMatches` mechanics unchanged; `FindResultsList.test.tsx` "lazy loading still fetches/reveals on scroll" |
| 7 | Gates: 3 Find suites green; full gated jest green; lint 0; typecheck 0 production | PASS — see §4 | |
| 8 | Live verification (after a deploy) | **UNVERIFIED — no Office host in this worktree.** Same precedent as every prior Find task (013/021/026/033/034/037/077). Left open. |

## 4. Gate results

- **Three Find suites**: `FindView.test.tsx` 37 + new tests, `FindResultsList.test.tsx` rewritten, `useFindRecordMatches.test.tsx` unchanged — see the exact numbers in the task-completion report (run in isolation from the parallel wave's in-progress Save-tab edits).
- **ESLint** (`FindResultsList.tsx`, `FindView.tsx`, `useFindRecordMatches.ts`, `App.tsx`, both test files): 0 errors, 0 warnings (`--max-warnings 0`).
- **TypeScript** (`tsc --noEmit --skipLibCheck`): 0 errors in any of the 4 production files or 2 test files touched by this task. The whole-project error count fluctuated during this task because task 088 was concurrently mid-edit on `SaveFlow.tsx` / `SaveView.tsx` / `shared/__mocks__/office-js.ts` in the same worktree (confirmed by reading those files' error messages — undefined `handleOpenDocumentRecord`, `onViewDocument`, `Body1`, `ArrowResetRegular`, missing style keys — all symptomatic of in-progress refactor, not anything this task touched). The project-wide `tsc` baseline (68, pre-task) cannot be cleanly re-measured in isolation while 088 is mid-flight; the main session should re-run `tsc --noEmit --skipLibCheck` after both waves land.

## 4.5 Quality gates (code-review + adr-check, task-execute Step 9.5)

**code-review**: reviewed all 6 touched files (4 production + 2 test). No Critical or Warning findings.
One Suggestion applied: `RecordRow`'s match-reason list keyed by array index — changed to key by the
reason string itself (reasons are a static, non-reorderable list per render). One Suggestion noted,
not applied (cosmetic only, verified safe): `HubSection`'s `onOpenRecord!` non-null assertion inside
the `.map()` callback — TypeScript can't carry the truthy-narrowing of `onOpenRecord` across the
ternary that computes `target`, so the assertion is needed; the logical guarantee (target is only
non-null when onOpenRecord was truthy at that render) holds. `FindResultsList.tsx` grew to ~745 lines
(from ~517) — per `docs/standards/COMPONENT-COMPLEXITY.md` (no hard LOC gate), this is noted as
information, not a finding: the file remains one cohesive concern (rendering the Find tab's two
result sections), with sub-concerns (hub-id extraction, match-reason sanitization, two section
renderers) that could be split into sibling files if the component grows further.

**adr-check**: ADR-021 (Fluent v9) — compliant: `@fluentui/react-components` only, no v8 imports, no
hard-coded hex colors (`grep -nE "#[0-9a-fA-F]{3,6}"` across all 4 production files returned nothing),
semantic tokens throughout. ADR-044 (GUID canonicalization) — compliant: `cleanGuid` applied at every
point a document/hub id crosses into an `openRecord` call. ADR-012 Path A (no `@spaarke/ui-components`
import) — unchanged. No BFF/server files touched, so ADR-001/007/008/010/013/028 do not apply. Zero
violations.

## 5. Escalation

None fired. The POML's one escalation trigger ("a single scroller cannot be achieved without changing `TaskPaneShell`'s main-area overflow") did not fire — `TaskPaneShell` was not touched; the fix lives entirely in `FindView`'s and `FindResultsList`'s own `flex:1/minHeight:0` chain, same approach other Find tasks (033/034/077) have used for this surface.

## 6. Deferred / not in scope

- The server-side `thread-` hub-id defect (no real record id reaches the client at all for `SameThread` relationships) is a pre-existing server behavior, not a defect this task introduces or fixes — `extractHubRecordId` degrades it safely client-side. Flagged here per CLAUDE.md §11 rather than silently patched; a future task could give `SameThread` hubs a real id (the source document's own) if the owner wants that hub openable too.
- Live UI verification (criterion 8) deferred to a deployed environment, consistent with every prior Find task.
