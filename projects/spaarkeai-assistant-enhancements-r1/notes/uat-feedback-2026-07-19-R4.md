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
| R4-6 post-Draft next-action cards (Send email / Save / Create matter) | ⏳ TODO — needs the 3 actions to exist as chips/bindings; medium |
| R4-7 empty "Actions available" header | ⏳ deferred — owner will re-capture the repro next session |
| R4-8 no history | ✅ shipped `1888646ab` (BFF) — root cause: `GET /api/ai/chat/sessions` was a STUB returning `new List<>()`. Implemented `ListRecentSessionsAsync` (Cosmos query, tenant partition, ORDER BY lastActivity DESC, projected title) + wired the endpoint to return a top-level array. BFF deployed to dev + hash-verified + health passed; merged to master. |
| R4-9 Context inconsistent | ⏳ deferred — owner will re-capture the repro next session |
| R4-10 summarize spinner | ✅ shipped `5a75e0b42` — `useConsumerChips.dispatching` drives a "Working…" spinner + composer lock across any chip capability (Summarize included) |
| R4-11 post-summarize cards ("Summarize again" only) | ⏳ NEXT — the next-step chips come from the summarize binding's `sprk_chiptransitions` (server catalog). Needs a decision on the replacement set (proposed: Create a matter · Draft a response · Ask about these files) then a live PATCH — no code deploy |
| R4-6 post-Draft next-action cards (Send email / Save / Create matter) | ⏳ NEXT — Create-a-matter exists (surface-launch); Send-as-email + Save-to-document need to exist as actions/bindings first. Medium |
| R4-12 wizard context (uploaded files) | ⏳ NEXT (larger) — thread the session's uploaded files into the wizard handoff seed so Quick Start wizards start with the current context. Owner: important (we offer the option, so it must be integrated) |
