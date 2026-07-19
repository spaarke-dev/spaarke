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

### ✅ Upload feedback — SHIPPED (commit `43d2d64a1`, deployed 2026-07-19)
- **UP-10 (#10)** ✅ — a live "Attaching file… / Classifying file…" row with a spinner renders above the composer while it's locked during the ingest window (`UploadProgressIndicator` in ConversationPaneChrome, driven by `attachments.isPromoting` / `eventBatch.isEventInFlight`).

### Draft a response (#13) — PARTIAL (commit `43d2d64a1`)
- ✅ **Readable render**: `draft-correspondence` output (`{subject, body, recipients_suggestion, cited_refs}`) no longer dumps raw JSON — `formatCorrespondenceDraft` (DocumentUploadedEventStream.ts) renders **Draft response** / Subject / body / Suggested recipients / Sources. Grounded (ADR-039).
- ⏳ **OPEN — route to Compose tab**: owner wants the draft pre-loaded into a Compose tab. Blocked on the compose widget accepting SEED content (it currently opens blank via `widget_load{widgetType:"compose"}`). NEEDS DECISION: (a) add seed-content support to the compose widget so `widget_load` can carry `{subject, body}`, then dispatch it from the draft path; or (b) accept the in-chat readable draft as sufficient. Deliberately NOT hacked as a misleading blank-compose launch.

### Files + Context pane (#9, per decision 1)
- ✅ **Part A — Files own section** (commit `e5f447fec`, deployed 2026-07-19): the "N files attached" one-liner is now a collapsible section — single file shows inline; 2+ files collapse into a dropdown (default collapsed) that expands to a per-file list. `FilesAttachedIndicator` evolved in ConversationPaneChrome; ConversationPane passes `files={attachments.attachmentChips}`.
- ✅ **Part B — Context pane opens on Execution Trace; quick-start removed** (commit `9f78b2874`, deployed 2026-07-19). Owner: "we are removing the quick start from the Context - you can remove it." `DEFAULT_TOOL`→`execution-trace`; quick-start removed from `ContextToolId`/menu/`ContextPaneController` (branch + handler + 8 imports + styles); execution-trace still yields to a server-pushed context widget during active analysis. ComposeTraceHost stubbed in the provider-less test suite; 24/24 Context tests pass. The Assistant-pane ⋮ → Quick Start modal is a SEPARATE surface (unchanged). Finding: the Context pane was ALREADY visible on load (right 25% col) — the real change was its default CONTENT.
- ⏳ **Part B.2 — session files live IN the Context pane** (OPTIONAL): `context.files_staged` is dispatched (ids only) but the Context pane has no consumer. Lower priority now — files already show in the Assistant pane (Part A) and the trace shows Assistant activity. Would need `files_staged` enriched with filenames + a render home in the trace view.

### Draft-a-response → Compose tab (owner: YES, 2026-07-19)
- Route the drafted `{subject, body}` into a pre-filled Compose tab. Needs the compose widget to accept SEED content via `widget_load` widgetData, then dispatch it from the draft path. IN PROGRESS.

## Key facts / anchors
- Chip dispatch path: `useConsumerChips.runBindingDispatch` (surface_launch handling fixed in cluster 1).
- My Assistant data-access fix (prior): `getProfileByUser`/`upsertProfileByUser` use `$filter=_sprk_systemuser_value eq …` (lookup-alt-key 400 fix).
- AI injection of profile free-text: `Services/Ai/Context/ContextBinder.cs` `userFragment` (MA-4 relies on this).
- Deploy: `pwsh scripts/Deploy-SpaarkeAi.ps1` (sprk_spaarkeai). Catalog chips = live PATCH, no deploy.
