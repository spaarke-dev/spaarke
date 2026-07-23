# Current Task State — messaging-communication-app-r3

> **Last Updated**: 2026-07-22 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. This file is self-contained — you can resume from it alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active work** | UAT refinement of the Communications widget + modal (conversation UI). NOT a POML task — direct UAT iteration. |
| **Status** | **IMPLEMENTED 2026-07-22 — all 7 UAT items done.** Tests green (ui-components 97/97, communication-components 9/9), both consumer packages typecheck clean. Not yet committed. |
| **Next Action** | Commit the UAT batch (5 shared files + 2 hosts + tests + rebuilt dist), then HOLD deploy for coordinated rollout. See `notes/uat-feedback-comm-widget-2026-07-22.md` for the per-item resolution table. |
| **Branch** | `work/messaging-communication-app-r3` — synced with master (a few notes commits ahead since last sync). |
| **Deploy** | HELD for the operator's **coordinated cross-project rollout** — do NOT deploy without the user sequencing it. |
| **Re-UAT watch** | Item 1 (full-height): if the "small box" persists after deploy, the cause is the OUTER dashboard wrapper (`WorkspaceWidgetRegistry` / `communications.registration.ts`), not these shared files — the shell's own height/width chain is now correct. |

### Critical Context (3 sentences)
Task 045 (FR-22 notification awareness) is DONE + merged to master (Option A register-only wiring; consumer-only; no second producer). SpaarkeAi was just deployed carrying it. The current work is a 7-item UAT batch on the **shared** conversation components (`@spaarke/ui-components` `ConversationWorkspace`/`ConversationView` + `@spaarke/communication-components` `CommunicationsWorkspaceWidget`) — fixes reach BOTH the dashboard widget AND the modal code page (`sprk_communicationconversationpage`) because they share these components.

---

## UAT Work Item — Communications widget + modal (2026-07-22)

Full checklist + file map also at `notes/uat-feedback-comm-widget-2026-07-22.md`. **All 5 files are shared — each fix reaches BOTH surfaces.** Update the matching `__tests__` alongside each change. Screenshots in the chat that produced this (operator UAT after the SpaarkeAi deploy).

### Batch A — `Spaarke.UI.Components/src/components/ConversationView/ConversationView.tsx` (~1106 LOC)
- **(4) Toolbar** (email/message/inbox/search icons): right-align; more spacing; **active state = dark-blue background (`tokens.colorBrandBackground` / brand), inactive = transparent** — mirror the Calendar widget filter button (`@spaarke/events-components` `CalendarFilterPane` active-button styling).
- **(6) Send** = icon only (`SendRegular`), drop the "Send" text label.
- **(7) Refresh** — move the `onRefresh` control OUT of the message-input row INTO the top toolbar with the other tools.
- **(5c) Mark-as-read** — moves HERE: add "mark as read" as a MESSAGE toolbar tool (it is removed from thread rows in Batch C).

### Batch B — `ConversationView/subcomponents/EmailInFlowBlock.tsx` (the email card)
- **(2)** Email card redesign: **font too small/compressed → increase**; **REMOVE the attachment list**; **ADD ~first-100-char body snippet** (concat/preview); **ADD sent/received date**.

### Batch C — `ConversationWorkspace.tsx` (373 LOC) + `ConversationWorkspace/subcomponents/ThreadList.tsx`
- **(1) Full container height** — widget/modal content must fill the container (currently a small box w/ empty space below). Root `height:100%` / flex-fill through `CommunicationsWorkspaceWidget.tsx` → `ConversationWorkspace.tsx`.
- **(3) Right pane resizes with message width** — stabilize: right pane `min-width:0` + fixed/stable flex-basis so it doesn't grow to the widest bubble.
- **(5a) `+` new-thread button does NOT work** — fix the handler (`onCreateThread`/`+`) in `ConversationWorkspace.tsx`.
- **(5b)** Thread rows: add **padding between rows**; "1 new message" text → **unread-dot ICON** (drop words); **smaller pin icon**.
- **(5c) REMOVE per-row "Mark as read"** (moves to the message toolbar — Batch A).

### Reference
- Active-filter styling: Calendar widget filter button (dark-blue when on). `@spaarke/events-components` `CalendarFilterPane`.
- Component roles: `ConversationWorkspace` = two-pane (threads + conversation); `ConversationView` = right pane (messages + toolbar + send + refresh); `ThreadList` = left pane rows; `EmailInFlowBlock`/`MessageBubble` = message cards.

---

## Full State (Detailed)

### DONE + on master this session
- **Auth popup-loop fix** (server config): created the missing `sprk_TenantId` env-var value record in spaarkedev1 (`a221a95e-…`) → tenant-specific MSAL authority (was `/organizations` → popup loop). Also added a `/api/config/client` tenant fallback in `@spaarke/auth` `resolveRuntimeConfig.ts`.
- **by-regarding 500 fix**: read path selected the broken `sprk_sentbyname` column (`IsValidODataAttribute=false` in-env) → Dataverse 400 → 500 whenever a thread had messages. Fixed by sourcing `SentByName` from the `sprk_sentby` lookup's FormattedValue annotation (impersonated read now requests annotations). Verified 200.
- **Task 045 (FR-22)**: Option A register-only wiring (seam `communicationArrivalsSeam.ts`; SpaarkeAi `initNotificationsClient()` binds the ONE shared client; removed rogue `new NotificationsClient()`). PR #682 merged. 9/9 tests pass.

### DEPLOYED to spaarkedev1
- BFF (by-regarding + sender-name fixes), conversation code page (tenant fallback), SpaarkeAi (task 045 — just deployed by operator), PCF v1.1.0.
- `@spaarke/auth` tenant-fallback reaches SpaarkeAi only on its next rebuild (SpaarkeAi doesn't hit the loop — Xrm gives it the tenant).

### PENDING (operator-coordinated)
1. **Spine runtime config** for live communication badges: Azure SignalR (Tier 1) + `systemuser.sprk_isexternal` backfill (Tier 2) per `NOTIFICATIONS-AND-SUGGESTIONS-USER-GUIDE.md` (in `spaarke-wt-spaarke-notification-spine-r1`).
2. **Coordinated deploy** of the UAT batch once implemented.

### Scope boundaries (do NOT drift)
- Notification awareness = **SpaarkeAi/LegalWorkspace workspace widget ONLY**. The Matter-form conversation **PCF + Messages modal do NOT consume the spine** (separate host) — extending them is follow-on scope, not requested.
- **No second producer** — the spine's `CommunicationArrivedProducer` is canonical (ADR-047).
- Shared components: ADR-012 context-agnostic + ADR-021 Fluent v9 semantic tokens (NO hard-coded colors — use tokens for the toolbar active-state).

### Deploy discipline (operator memory)
Before ANY env deploy: commit → `/push-to-github` → `/worktree-sync` → deploy via dedicated skills (`/bff-deploy`, `/code-page-deploy`, `/pcf-deploy` — hand over the full PCF zip path). Deploy currently HELD for cross-project coordination.

### Key commits this session
- by-regarding 500 fix `324f94d61` · sender-name + tenant-fallback `5fff139f0` (PR #680) · task 045 Option A `b11863c8b` (PR #682) · UAT capture `5b885f480`.

### Files modified this session (all committed)
- `Services/Communication/CommunicationThreadReadService.cs`, `Spaarke.Dataverse/DataverseWebApiService.cs` (annotation request)
- `@spaarke/auth/resolveRuntimeConfig.ts` (tenant fallback)
- `@spaarke/communication-components/.../CommunicationsWorkspaceWidget/{communicationArrivalsSeam.ts (new), useCommunicationArrivals.ts, CommunicationsWorkspaceWidget.tsx, index.ts, useCommunicationArrivals.test.tsx}`; deleted `createNotificationsClient.ts` + `types/spaarke-notifications.d.ts` + dead `@spaarke/notifications` dep/mock/mapper
- `SpaarkeAi/src/services/notificationsBootstrap.ts` (seam wiring)
- `tasks/045-notification-awareness.poml` + `tasks/TASK-INDEX.md` (gate cleared) + `notes/uat-feedback-comm-widget-2026-07-22.md`
