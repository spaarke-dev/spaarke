# Task 099 — Save tab round 5: a focused flow (items 1, 2, 4, 5, 6, decision A, shared cleanGuid)

Owner UAT round 5 (2026-10-05). Record: `notes/042-uat-round5-2026-10-05.md`. Client-only (`src/client/office-addins/**`); no BFF change; item 3 (create fields) is task 100.

## 1. What shipped

| Item | Change |
|---|---|
| 1 Placeholder | `RelatedToPicker.tsx`: the lookup `Input` no longer has `contentBefore` (text-only "Look up related {Type}..."); the Search icon button ("Search") and Enter are unchanged. |
| 4 Collapse on select | `RelatedToPicker.tsx`: when `value !== null && !showCreate` the picker renders ONLY the selected record card (green check + x). Pills, lookup, results, "+ New" and suggestions are hidden. x (`clearSelection`) restores the full picker; query and results are component state, so the previous search is back with no new request. |
| 2 "+ New" | New prop `onCreatingChange(creating)` (called with `false` on unmount). While the create form is open the picker also hides results/suggestions/search error; `SaveFlow` (`relatedCreating`) hides the Document header, filed-to card, Profile, attachments, SaveModeSection and Cancel/Save (only when the picker is rendered, i.e. not version mode). After create the record is selected (collapsed); Cancel restores the previous view. |
| 5 Name in header | `SaveFlow.renderDocumentHeader` replaces `renderDocumentDetails` and the old "Document Info" block. The "Document" header (Outlook: "Email") holds the name control: editable (pencil/Input, Textarea on hosts without `canProvideDocumentName`), locked (hint + "Save as new document" link) or read-only. No "Document Details" header, no name card. A host that cannot supply a document name still shows the item name/sender/date rows above the name box. |
| 6 + decision A | Post-save view = confirmation (View Document, Copy Link unchanged) + header (locked) + Profile with **Refresh** (`DocumentProfileSection mode="refresh"`, accessible name "Refresh", no Generate Profile). The footer is gone: no Cancel, no Open Document, no gray "Saved". Save appears alone only when `hasUnsavedChanges` (detected document edit). Pre-save footer (Cancel / Open Document / Save) is unchanged. |
| cleanGuid | Local `shared/taskpane/utils/cleanGuid.ts` deleted. All 11 importers use `@spaarke/ui-components/guid`, an exact alias to `Spaarke.UI.Components/src/utils/guid.ts` (pure, zero imports) in `webpack.config.js`, `jest.config.js` and `tsconfig.json` paths (so `tsc` type-checks it directly). `cleanGuid.test.ts` now exercises the shared implementation. |

## 2. Decision A — how it is implemented

`hasUnsavedChanges = savedDocument !== null && canSaveNewVersion && (!canDetectDocumentChanges || contentChangedSinceSave)`. `renderFooter()` returns `null` after a save unless that is true, then renders only the primary **Save** (a new version of the saved document via the existing 094 path). The 088 "Generate Profile re-enables Save" trigger and the `profileRegenerated` bundle field were removed (no Generate Profile exists post-save). Outlook (`canSaveNewVersion` false) never gets Save after a save.

**Refresh**: the profile section re-reads the profile itself (`refetch`); `SaveFlow.handleRefreshSaved` also calls `onRetryDocumentIdentity`. When identity settles on the SAME document, the saved state's name and filed-to record are updated from it (header + confirmation bar). If the identity is unavailable the saved bar keeps what the save reported.

## 3. Focus management (ADR-021)

- Save completes: focus moves to the confirmation (`tabIndex=-1` wrapper).
- "+ New": focus into the name box; Cancel: back to the lookup box; select / create: onto the selected record's control; x: back into the lookup box.
- "Save as new document": focus lands on the now-editable name control.
- Refresh uses `disabledFocusable` so it keeps focus during the re-read.

## 4. Deviations / judgement calls

- **No detection => Save still offered after a save.** Decision A says no Save until an edit is detected, but the owner's earlier binding "never block a save" rule (094) wins where the host cannot detect changes (Word without WordApi 1.6); hiding Save there would make a second save impossible.
- **Outlook loses "Cancel / Save Another"** after a save (Cancel removed per the owner). A second save of the same email in the same pane session is not possible until the item changes. Flag for the owner.
- Creating hides the whole Document header (the name now lives there), not only the name field.
- `profileRegenerated` removed from `SavedDocumentPaneState` (dead after this change); fixtures in `SaveView.*` tests updated.
- Fluent v9's `ref` props type as `Ref<never>` in this package, so refs are callback refs.

## 5. Gates

See the task report in the session; tests: `SaveFlow.savedState` (rewritten), `RelatedToPicker.layout` (+5), `SaveFlow.round5` (new, gated), `DocumentProfileSection` (+3), `cleanGuid` (shared).

## 6. Live acceptance — OPEN

Needs a deploy: items 1, 2, 4, 5, 6 in a real Word pane (select collapses, x restores; "+ New" form only; header rename; post-save confirmation + Refresh and no footer; a document edit makes Save appear). Not verified against Office.js here.
