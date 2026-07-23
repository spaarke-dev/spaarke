# UAT Feedback R4 — 2026-07-19 (Ralph, dev, end-to-end after the R3 batch)

> Captured after MA cluster + Chat-UX + UP-10 + Draft-render + Files-section + Context-execution-trace + Draft→Compose shipped. Confirmed WORKING in screenshots: Draft→Compose (letter opened in Compose tab), execution-trace Context pane ("1 tool call from the session ledger" / SYS-Recall_Session_File), files-attached tray ("1 file attached … (1 indexed)"), "Let's get started…" placeholder, get-started cards.

## Backlog (R4)

### Initial Assistant load
- **R4-1 (cards whitespace)**: the get-started cards need whitespace to separate them from the Assistant chat output above/below.
- **R4-2 (More card)**: add a "…More" card to the welcome cards that opens the Quick Start modal (the 7-card grid).
- **R4-3 (composer height ×2)**: the conversation text box needs ~2× the height.

### Attaching files
- **R4-4 (revert auto-Compose on attach)**: attaching 2 files auto-opened 2 Compose tabs. REVERT — do NOT open Compose immediately on attach. Instead make **"Revise the file"** a next-action CARD; clicking it opens the file(s) in Compose. Multiple files → separate Compose tabs on demand (or a smarter card if there's a better idea).
- **R4-5 (files not in tray)**: the attached files show as composer chips (bottom) but are NOT reflected in the "file tray" (FilesAttachedIndicator) section — expected to see both uploaded files in the tray.

### Draft a response
- **R4-6 (post-Draft actions)**: after "Draft a response" opens the Compose tab, the Assistant should surface a NEW set of next-action cards: **Send as email**, **Save to document**, **Create a matter**.

### New session
- **R4-7 (empty "Actions available")**: header reads "Actions available for The dispute arises from…" (from the file loaded in Compose) but NO actions are listed beneath — confusing empty state.
- **R4-8 (no history logged)**: started a new session; History shows nothing logged.
- **R4-9 (Context inconsistent)**: Context/Execution-Trace pane is not consistently loaded; unclear what gets logged there.

### Summarize files
- **R4-10 (summarize spinner)**: add a progress spinner while summaries are being generated.
- **R4-11 (post-summarize cards)**: after summaries return, the only card is "Summarize again" — which doesn't make sense. Offer more useful next actions.

### Quick Start ("More") wizards
- **R4-12 (wizard context)**: opening a wizard from Quick Start carries NO conversation context; the user expects the current context (e.g. the 2 uploaded files) to be included in the wizard.

## Status
| Item | State |
|---|---|
| R4-1 cards whitespace | ✅ shipped `02a7bcd65` |
| R4-2 More card → Quick Start | ✅ shipped `02a7bcd65` |
| R4-3 composer ×2 height | ✅ shipped `02a7bcd65` (inputMinRows 3→6) |
| R4-4 revert auto-Compose + "Revise in Compose" tray action | ✅ shipped `db2473558` |
| R4-5 files in tray | ✅ RESOLVED — owner confirmed both files show in the tray ("2 files attached … (2 indexed)"). Not a bug. |
| R4-6 post-Draft next-action cards (Send email / Save / Create matter) | ✅ shipped 2026-07-20 — **Create a matter** rides the `draft-correspondence` binding's `sprk_chiptransitions` (live catalog PATCH, no deploy → 89cd91f6…); **Send as email** + **Save to document** are client-only local-action chips (`localActionChips.ts`) reusing the existing `draft-email` / `add-to-dms` editor bridges. Appended after "Draft a response" opens the Compose tab. |
| R4-7 empty "Actions available" header | ⏳ deferred — owner will re-capture the repro next session |
| R4-8 no history | ✅ shipped `1888646ab` (BFF) — root cause: `GET /api/ai/chat/sessions` was a STUB returning `new List<>()`. Implemented `ListRecentSessionsAsync` (Cosmos query, tenant partition, ORDER BY lastActivity DESC, projected title) + wired the endpoint to return a top-level array. BFF deployed to dev + hash-verified + health passed; merged to master. |
| R4-9 Context inconsistent | ⏳ deferred — owner will re-capture the repro next session |
| R4-10 summarize spinner | ✅ shipped `5a75e0b42` — `useConsumerChips.dispatching` drives a "Working…" spinner + composer lock across any chip capability (Summarize included) |
| R4-11 post-summarize cards ("Summarize again" only) | ✅ shipped 2026-07-20 — owner-chosen set **Create a matter · Draft a response · Ask about these files**. Create-a-matter + Draft-a-response are live `chat-summarize` `sprk_chiptransitions` (catalog PATCH, no deploy → 89cd91f6…/ f7dc4a00…); "Ask about these files" is a local prompt-nudge chip (files already attached → the next chat turn is grounded). Replaces the self-referential "Summarize again". |
| R4-12 wizard context (uploaded files) | ✅ shipped 2026-07-20 — `QuickStartModal.getFileContext` threads ALL promoted session files (`fileIds`/`source.sessionId`/`provenance.sourceFiles`) into `launchSurface` for **create-matter** + **create-project** (both read `initialFileRefs`). Zero shared-lib change — the handoff envelope + wizard read-side already existed. create-project switched from the file-less Path-B launcher to `launchSurface`. Summarize/other Quick Start cards use a separate file mechanism (documentIds URL) — not threaded this round. |

## R4 round-2 close-out (2026-07-20)

Owner directive: "build both now" (R4-6 + R4-12) + R4-11 card set. Delivered as **client-only (SpaarkeAi) + two live catalog PATCHes — NO BFF deploy**:
- **Catalog (live, no deploy):** `chat-summarize` → [Create a matter, Draft a response]; `draft-correspondence` → [Create a matter]. Verified by read-back. Note: `ConsumerRoutingService` may cache bindings — if UAT still shows old chips, a BFF restart clears the cache.
- **New client module** `localActionChips.ts`: `local:*` sentinel chips routed by `useConsumerChips.handleConsumerChipClick` → `onLocalChipAction` (never a fake Binding dispatch); post-Draft (Send/Save) + post-Summarize (Ask) companions injected via chip augmentation.
- **Tests:** 23 pass across useConsumerChips.surface-launch (+3 local-chip tests), QuickStartModal (+R4-12 file-threading test), ConversationPane.consumer-chips (stale "More"→playbook test realigned to Quick Start). Also fixed a pre-existing stale test.
- **Deployed:** `sprk_spaarkeai` to dev (2026-07-20).
- **Still open:** R4-7 / R4-9 (owner re-capture repro).
