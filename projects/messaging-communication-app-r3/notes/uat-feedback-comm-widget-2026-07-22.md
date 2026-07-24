# UAT Feedback — Communications Widget + Modal Code Page (2026-07-22)

> Source: operator UAT after the SpaarkeAi deploy that carried task 045. Applies to BOTH the
> `CommunicationsWorkspaceWidget` (SpaarkeAi/LegalWorkspace dashboard) AND the modal code page
> (`sprk_communicationconversationpage`) — they share `ConversationWorkspace` + `ConversationView`
> from `@spaarke/ui-components`, so shared-component fixes reach both surfaces.

## Items

1. **Widget not using full container** — content sits in a small box with large empty space below;
   should fill the container height (both widget + modal).

2. **Email message card layout wrong:**
   - font too small; layout too compressed
   - REMOVE the list of attachments from the card
   - ADD first ~100 chars of the email body (concat/preview snippet)
   - ADD sent/received date

3. **Conversation (right) pane resizes based on message width** — pane width must be stable, not
   reflow to the widest message.

4. **Message toolbar (email/message/inbox/search icons):**
   - right-align
   - more spacing
   - active/filter state: dark-blue background when ON, no background when OFF
     (mirror the Calendar widget filter button — reference screenshot)

5. **Threads (left) pane:**
   - `+` (new thread) button does NOT work — fix
   - add padding between thread rows
   - "1 new message" text → replace with an ICON (drop the words)
   - REMOVE "Mark as read" from the thread row → move it to the MESSAGE toolbar (as a tool)
   - pin icon → smaller

6. **Send button** — icon only (remove the "Send" word).

7. **Refresh** — move the refresh control into the toolbar (currently near the message input).

## File map (all shared — fixes reach BOTH widget + modal)

| # | Item | Edit site |
|---|------|-----------|
| 1 | Full container height | `Spaarke.Communication.Components/.../CommunicationsWorkspaceWidget.tsx` + `Spaarke.UI.Components/.../ConversationWorkspace/ConversationWorkspace.tsx` (root height:100%/flex) |
| 2 | Email card redesign | `Spaarke.UI.Components/.../ConversationView/subcomponents/EmailInFlowBlock.tsx` (font up; remove attachment list; add ~100-char body snippet; add sent/received date) |
| 3 | Pane resizes w/ msg width | `ConversationWorkspace.tsx` / `ConversationView.tsx` (fix right-pane min-width:0 / stable flex-basis so it doesn't grow to widest bubble) |
| 4 | Toolbar right-align + on/off bg | `ConversationView.tsx` toolbar (justify-end, gap; active=colorBrandBackground, inactive=transparent — mirror Calendar `CalendarFilterPane` active button) |
| 5 | Threads pane | `ConversationWorkspace/subcomponents/ThreadList.tsx` (+ `ConversationWorkspace.tsx` for the `+` handler; mark-as-read MOVES to `ConversationView.tsx` toolbar): fix `+` new-thread; row padding; "1 new message" text → unread-dot icon; REMOVE per-row "Mark as read"; smaller pin icon |
| 6 | Send icon only | `ConversationView.tsx` (`SendRegular` icon button; drop the "Send" label) |
| 7 | Refresh → toolbar | `ConversationView.tsx` (move `onRefresh` control from the input row into the top toolbar with the other tools) |

Reference for item 4 active-state: `@spaarke/events-components` `CalendarFilterPane` / the Calendar widget filter button (dark-blue when active).

Tests to update alongside: `ConversationView/__tests__/*`, `ConversationWorkspace/__tests__/*`.

## Status: IMPLEMENTED 2026-07-22 (all 7 items). Tests green (ui-components 97/97, communication-components 9/9). Deploy = coordinated rollout (HELD).

### What shipped per item
| # | Item | Resolution |
|---|------|-----------|
| 1 | Full container | ConversationView root `width:100% / minWidth:0`; ConversationWorkspace rightPane `flex:1 1 0%`. Height chain already correct (widget root/body → shell root all `height:100%`+`minHeight:0`). If the box persists it's the OUTER dashboard wrapper, not these files. |
| 2 | Email card | `EmailInFlowBlock`: subject base400, meta/snippet base300 (was 200); attachment list REMOVED; ~100-char HTML-stripped body snippet ADDED; "Sent/Received {date}" line ADDED (verb from `sprk_direction`). `onOpenAttachment` prop dropped from the block. |
| 3 | Pane resizes w/ msg width | Stabilized via rightPane `flex:1 1 0%` + ConversationView root `width:100%/minWidth:0`. |
| 4 | Toolbar right-align + on/off bg | Filter bar `justify-content:flex-end`, gap S; active toggle = `colorBrandBackground`/`colorNeutralForegroundOnBrand` class (dark-blue on, transparent off). |
| 5a | `+` new thread | Was inert (no host wired `onCreateThread`). `ConversationWorkspace` now OWNS the create flow: hosts inject `onSearchRecipients` → shell opens the existing `NewThreadModal` (task 024) → `startDirectThread` → selects + refreshes the list. |
| 5b | Thread rows | Row gap + M padding + rounded rows (divider line removed); "N new messages" text → compact brand **dot** (count in aria-label); pin icon smaller (`fontSizeBase200`). |
| 5c | Mark-as-read | Removed from thread rows; ADDED as a message-toolbar tool in `ConversationView`; plumbed back to the thread badge via the `renderConversation` seam (`onMarkThreadRead`). |
| 6 | Send icon-only | `SendRegular` icon button, "Send" text label removed. |
| 7 | Refresh → toolbar | Moved out of the compose input row into the message toolbar (still present on empty threads). |

### Files changed (all shared → reach BOTH widget + modal code page)
- `Spaarke.UI.Components`: `ConversationView/ConversationView.tsx` + `.types.ts`, `ConversationView/subcomponents/EmailInFlowBlock.tsx`, `ConversationWorkspace/ConversationWorkspace.tsx`, `ConversationWorkspace/subcomponents/ThreadList.tsx` (+ tests: filters, emailInFlow, ConversationWorkspace)
- `Spaarke.Communication.Components`: `CommunicationsWorkspaceWidget.tsx` (onSearchRecipients + onMarkThreadRead forward)
- `sprk_communicationconversationpage/src/App.tsx` (same host wiring)
- Rebuilt `Spaarke.UI.Components/dist` so consumer `.d.ts` picks up new props.
