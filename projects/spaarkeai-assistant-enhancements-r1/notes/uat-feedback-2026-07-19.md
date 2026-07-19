# UAT Feedback R3 — 2026-07-19 (Ralph, dev, end-to-end)

> Captured after the P1 + P2 batch shipped. Two owner decisions gathered (see §Decisions). Cluster 1 shipped; the rest is scoped + ready to build.

## ✅ Shipped (cluster 1) — commit `4bcdc12cf`
- **Create Matter via card/chip**: suppress the raw draft JSON in the transcript (surface_launch dispositions no longer render `dispatched.result`) + carry the uploaded file into the wizard (threaded `getActiveSourceFile` → `useConsumerChips` → `launchSurface({fileIds,source,provenance})`, parity with the text path). Root cause: the new P1-7 "Create a matter" post-classify chip exercised the chip dispatch path, which (unlike `handleSurfaceLaunch`) rendered JSON + dropped the file.
- **"Insert into document"** per-message button REMOVED (ConversationPane no longer passes `onInsertToCompose`; the P1-3 length gate wasn't selective enough — it was noise after nearly every message). `useComposeInsertSuggestion()` stays subscribed for a future targeted affordance.

## Owner decisions (2026-07-19)
1. **Files & Context** → *"Own section in Assistant + Context on load"*: uploaded files get their own collapsible section in the Assistant pane (dropdown when they exceed one line), AND the **Context / Execution-Trace pane becomes the default pane on load and live-updates** with what the Assistant is doing (incl. the session files).
2. **Profile inputs** → *"Curated multi-select chips"*: replace the free-text Focus areas + Preferences boxes with curated selectable chips, each mapped to a **deterministic AI directive**. KEY INSIGHT: the profile's `sprk_focusareas` / `sprk_assistantpreferences` text fields are ALREADY injected into the agent turn via `ContextBinder.userFragment` (BFF) — so storing the selected directive phrases (comma-joined) in those existing fields carries them to the AI with **no BFF change**. Determinism comes from the curated chip→phrase mapping.

## Remaining backlog (scoped)

### My Assistant cluster (client-only rework of MyAssistantDialog + useMyAssistant + userProfileService)
- **MA-1 (no auto-open)**: `useMyAssistant` cold-start effect currently `setOpen(true)` when `sprk_profilecompletedon` is unset. STOP auto-opening; instead surface a dismissible "Complete your Assistant profile →" indicator (banner in the Assistant pane, or a badge on the ⋮ Tools menu) that opens the dialog on click. (UAT: auto-launch on load is jarring; also a "How can I help" flash on New — see CHAT-4.)
- **MA-2 (modal restyle)**: use the standard small modal size; give the text areas more edit space; fix the bottom nav; **remove "Clear my profile"** (owner: users can just edit + save — the GDPR erase can move elsewhere or be dropped from this flow).
- **MA-3 (Primary Work Location, #11)**: rename "Office Location" → **"Primary Work Location"**; replace the free-text Input with a dropdown populated from the **`sprk_workoffice`** table (records exist: Chicago `b8ecc5d7-8e83-f111-8076-7ced8ddc4a05`, New York `b5ecc5d7-…`, Tampa `49f343d0-…`; columns `sprk_workofficeid` + `sprk_name`). Store the selected office NAME in the existing `sprk_officelocation` text field (no schema change) — or add a proper `sprk_workoffice` lookup column if a relational link is required (owner to confirm if name-store is insufficient).
- **MA-4 (selectable chips, #12)**: replace Focus areas + Preferences textareas with curated multi-select chips. Propose a starter catalog (owner reacts):
  - **Preferences** (→ AI directive phrase): "Be concise" · "Always cite sources" · "Use bullet points" · "Show a summary first" · "Flag risks & deadlines explicitly" · "Plain-English (avoid legalese)".
  - **Focus areas**: practice-specific tags (M&A, Litigation, Employment, Commercial Real Estate, IP/Patents, Banking & Finance, …) — could reuse/derive from practice areas or a curated list.
  - Store the joined directive phrases in `sprk_assistantpreferences` / `sprk_focusareas`; the existing `ContextBinder.userFragment` injects them (verify).

### Chat UX cluster (SprkChat shared-lib + ConversationPane)
- **CHAT-4 (welcome suggestions, #6)**: an empty chat shows SprkChat's "No messages yet" instead of get-started suggestions. Root cause: `showWelcomePanel = chatSessionId===null && …` is FALSE for an active-but-empty restored session, so P1-1 `WelcomeStartCards` don't render. Fix: show the welcome cards whenever the transcript is empty (track message count via `onMessagesChange`), and/or replace SprkChat's empty state with the suggestions. Also the "How can I help" flash on New session.
- **CHAT-5 (composer, #7)**: increase the composer height + friendlier placeholder ("Let's get started…"). SprkChatInput has a `placeholder` prop; height needs a taller default/prop.
- **CHAT-6 (slash menu, #8)**: remove the slash-command Prompt button (the "sparkle" icon in the controls strip, `handlePromptMenuButtonClick` / `PromptRegular` in SprkChat.tsx ~2723-2733). Add a `hidePromptMenu?: boolean` prop (default false so other consumers are unaffected); ConversationPane passes it. "Future tools go at the top."

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
