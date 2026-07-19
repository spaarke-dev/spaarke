# UAT Feedback R3 — 2026-07-19 (Ralph, dev, end-to-end)

> Captured after the P1 + P2 batch shipped. Two owner decisions gathered (see §Decisions). Cluster 1 shipped; the rest is scoped + ready to build.

## ✅ Shipped (cluster 1) — commit `4bcdc12cf`
- **Create Matter via card/chip**: suppress the raw draft JSON in the transcript (surface_launch dispositions no longer render `dispatched.result`) + carry the uploaded file into the wizard (threaded `getActiveSourceFile` → `useConsumerChips` → `launchSurface({fileIds,source,provenance})`, parity with the text path). Root cause: the new P1-7 "Create a matter" post-classify chip exercised the chip dispatch path, which (unlike `handleSurfaceLaunch`) rendered JSON + dropped the file.
- **"Insert into document"** per-message button REMOVED (ConversationPane no longer passes `onInsertToCompose`; the P1-3 length gate wasn't selective enough — it was noise after nearly every message). `useComposeInsertSuggestion()` stays subscribed for a future targeted affordance.

## Owner decisions (2026-07-19)
1. **Files & Context** → *"Own section in Assistant + Context on load"*: uploaded files get their own collapsible section in the Assistant pane (dropdown when they exceed one line), AND the **Context / Execution-Trace pane becomes the default pane on load and live-updates** with what the Assistant is doing (incl. the session files).
2. **Profile inputs** → *"Curated multi-select chips"*: replace the free-text Focus areas + Preferences boxes with curated selectable chips, each mapped to a **deterministic AI directive**. KEY INSIGHT: the profile's `sprk_focusareas` / `sprk_assistantpreferences` text fields are ALREADY injected into the agent turn via `ContextBinder.userFragment` (BFF) — so storing the selected directive phrases (comma-joined) in those existing fields carries them to the AI with **no BFF change**. Determinism comes from the curated chip→phrase mapping.

## Remaining backlog (scoped)

### ✅ My Assistant cluster — SHIPPED (commit `46525cb24`, deployed to dev 2026-07-19)
Client-only rework of MyAssistantDialog + useMyAssistant + userProfileService (+ ConversationPane + AssistantToolMenu). 52/52 assistant tests pass; no BFF change.
- **MA-1 (no auto-open)** ✅ — `useMyAssistant` no longer `setOpen(true)`; exposes `needsProfile`. ConversationPane renders a dismissible "Personalize your assistant" MessageBar nudge (Set up / dismiss) + a red-dot `CounterBadge` on the ⋮ Tools trigger and the "My Assistant" menu item. Dismissal is session-scoped.
- **MA-2 (modal restyle)** ✅ — modal `maxWidth: 480px`; nav simplified to Cancel · Back · Next/Save; **"Clear my profile" removed** from the flow (the `eraseMyAssistantProfile` service + hook `onErase` remain in code for a future GDPR surface, just unwired from UI).
- **MA-3 (Primary Work Location)** ✅ — "Office location" free-text → "Primary work location" `Dropdown` from `sprk_workoffices` (Active only, alpha; +"Not specified"). Selected office NAME stored in existing `sprk_officelocation` (no schema change). New `listWorkOffices()` port method + `WorkOffice` type.
- **MA-4 (selectable chips)** ✅ — Focus areas + Preferences textareas → curated `ToggleButton` multi-select chips. `PREFERENCE_CHIPS` (8) + `FOCUS_AREA_CHIPS` (12) with `{id,label,phrase}`; `encodeChipSelection`/`decodeChipSelection` round-trip via `phrase`. Selected directive phrases stored newline-joined in existing `sprk_assistantpreferences` / `sprk_focusareas` → injected by `ContextBinder.userFragment` (NO BFF change). **Catalog is a STARTER proposal — owner to react/reword during UAT.**

### ✅ Chat UX cluster — SHIPPED (commit `1176b4125`, deployed to dev 2026-07-19)
SprkChat + SprkChatInput gained three additive, default-off, context-agnostic props (ADR-012 — no existing consumer changes). 49/49 SprkChat+Input tests pass; shared-lib tsc clean.
- **CHAT-4 (welcome suggestions)** ✅ — `WelcomeStartCards` now render whenever the transcript is EMPTY (any session state, incl. restored-but-empty). ConversationPane tracks live message count via `onMessagesChange` (`chatMessageCount`) → `showWelcomeCards`; passes `hideEmptyState` to SprkChat to suppress the built-in "No messages yet" while our cards show. NOTE: the "How can I help" flash on New session may be reduced (fresh/empty now shows cards) — re-check during UAT; if it persists, file the specific repro.
- **CHAT-5 (composer)** ✅ — new `inputPlaceholder` + `inputMinRows` props (→ SprkChatInput `minRows` → Textarea `rows`). ConversationPane greets with "Let's get started…" on an empty transcript, reverts to "Type a message…" once the conversation starts; `minRows=3` (taller default, still user-resizable).
- **CHAT-6 (slash menu)** ✅ — `hidePromptMenu` prop suppresses the toolbar-strip "Prompt" (slash-command) button; slash menu still reachable by typing `/`. ConversationPane passes it unconditionally. Attach button unaffected.

### Upload feedback
- **UP-10 (#10)**: while the composer is locked during attach/classify, show explicit progress in the chat — "Attaching file…" then "Classifying file…" with a small circular loader — so the user knows to wait. (The lock already exists via `inputBusy`; add the visible status messages driven by `isPromoting` / `isEventInFlight`.)

### Files + Context pane (#9, per decision 1)
- Uploaded files → their own collapsible section in the Assistant pane (dropdown when >1 line); Context/Execution-Trace pane default on load + live-updating with session files + Assistant activity. (Larger; touches ConversationPane layout + Context pane.)

### Draft a response (#13)
- Card "Draft a response" (draft-correspondence, Informational) renders raw JSON (`{subject, body, recipients_suggestion, cited_refs}`) in chat + no Compose tab. Fix: render the draft readably AND/OR route it to a Compose tab (owner wants a Compose tab with the drafted response). Likely needs draft-correspondence output formatting + a compose route.

## Key facts / anchors
- Chip dispatch path: `useConsumerChips.runBindingDispatch` (surface_launch handling fixed in cluster 1).
- My Assistant data-access fix (prior): `getProfileByUser`/`upsertProfileByUser` use `$filter=_sprk_systemuser_value eq …` (lookup-alt-key 400 fix).
- AI injection of profile free-text: `Services/Ai/Context/ContextBinder.cs` `userFragment` (MA-4 relies on this).
- Deploy: `pwsh scripts/Deploy-SpaarkeAi.ps1` (sprk_spaarkeai). Catalog chips = live PATCH, no deploy.
