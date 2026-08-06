# UAT Feedback Round 2 — Communications Workspace Widget (2026-07-23)

> Source: operator UAT after the 2026-07-22 batch deployed (SpaarkeAi + conversationpage + PCF v1.2.0).
> Surface: SpaarkeAi/LegalWorkspace **workspace widget** (screenshot). Fixes land in shared
> `@spaarke/ui-components` (`ConversationWorkspace`/`ConversationView`/`ThreadList`/`NewThreadModal`)
> so they reach the widget, the modal code page, AND the Matter-form PCF.

## Items

1. **Thread pane width 20% / conversation 80%** — left pane too wide; set thread≈20%, conversation≈80%.
2. **Thread pane width adjustable** — use the SAME method as the SpaarkeAi code page pane-width adjust (draggable splitter / pane fracs).
3. **Click thread-pane header to collapse the thread pane** — use the SAME open/close method as the SpaarkeAi panes.
4. **Colors** — thread pane light-grey background; conversation surface white.
5. **Thread pane title** — add a "Threads" title/header.
6. **Blue dot (unread)** — if it means unread, move the dot to the LEFT of the row (before the name).
7. **Pin icon** — reduce size further.
8. **Move the `+` (new thread) to the LEFT** of the thread pane (currently right).
9. **New Thread modal — two sections:**
   - **New Thread**: a **name** field + **associate-to-record** control = a dropdown of the record TYPE → opens the right-pane record lookup for that entity (SAME pattern as the wizard "associate" step).
   - **Message**: the message body field. NOTE: message is likely PLAIN TEXT (not rich text) → **remove the format/rich-text buttons** if so.
10. **Conversation toolbar filter semantics + style** — default = show ALL (messages + emails). Click the **email** icon → filter to ONLY emails; click the **message** icon → filter to ONLY messages. When a filter is ON: **blue background, white icon**.
11. **Remove toolbar icons** — remove **mark-as-read** and **only-show-unread** icons from the conversation toolbar.

## Complexity / open questions
- **Items 2 + 3** need the SpaarkeAi pane mechanism (WorkspaceShell pane fracs + collapse). Reuse, don't reinvent.
- **Item 9** conflicts with the current create model: `NewThreadModal` → `startDirectThread` is **participant-based** (1:1 direct thread keyed on `otherParticipantSystemUserId`). The requested model is **name + regarding** (no participant). Need to confirm whether a named/regarding thread-create path exists or if this is a backend change → possible ADR/escalation.
- **Item 10** changes filter semantics from additive AND-of-toggles to "click = solo filter" (radio-like).

## Status: IMPLEMENTED 2026-07-23 (all 11 items). Full stack. Deploy = BFF + 2 code pages + PCF.

### Resolution per item
| # | Item | Resolution |
|---|------|-----------|
| 1 | Thread pane 20/80 | New PCF-safe left-pane resize hook (`useThreadPaneLayout`) defaults thread pane to 20% of container; conversation flexes. |
| 2 | Adjustable width | Reused the shared **`PanelSplitter`** grip (same as SpaarkeAi) + `useThreadPaneLayout` drag/persist (localStorage). The shared `useTwoPanelLayout` collapses the RIGHT pane (inverted for a left pane), so this is its left-oriented mirror — PCF-safe (React-16 hooks only). |
| 3 | Click header to collapse | "Threads" header + chevron collapse to a thin re-expand rail (mirrors SpaarkeAi collapsed strip). |
| 4 | Colors | Thread pane `colorNeutralBackground2` (grey); conversation `colorNeutralBackground1` (white, now explicit on rightPane). |
| 5 | "Threads" title | Added in the thread-pane header. |
| 6 | Unread dot left | Moved the brand unread dot to the start of the row (before the name). |
| 7 | Smaller pin | Pin icon `fontSizeBase100` (~10px). |
| 8 | `+` to the left | Create control moved to the left of the header (subtle icon). |
| 9 | New Thread modal | **New BFF endpoint** `POST /api/communications/threads` (create named, record-anchored thread; owner=caller; no participant) + `ThreadResolver.CreateRecordThreadAsync` + 4 contract tests. **NewThreadModal redesigned**: section 1 = Name + `AssociateToStep` record picker (the wizard-associate pattern); section 2 = **plain-text** Message (rich-text buttons removed). Hosts inject `createXrmNavigationService()`. |
| 10 | Toolbar solo filters | Email/Message are now single-select solo filters (default = all; click = solo; click active = back to all); active = brand background + white icon. |
| 11 | Remove icons | Unread-only + Mark-as-read icons removed from the conversation toolbar. |

### §10 BFF hygiene (item 9)
- Publish size: **47.48 MB compressed** (≤60 MB ceiling; ~0 delta vs ~49.6 MB baseline — only small `.cs` added).
- CVE: no NEW HIGH (zero package/csproj changes; the pre-existing `System.Security.Cryptography.Xml` HIGH is owned by the CVE-cleanup effort).
- Placement: Communication domain (`CommunicationEndpoints` + `ThreadResolver`); reuses `IThreadResolver`/`IGenericEntityService`/`ICallerSystemUserResolver`; no new DI/package.
- Tests: 46 Communication BFF tests pass (incl. 4 new create-thread contract tests); ui-components 93 conversation tests pass; comm-components 9/9.

### Deploy targets (all 4)
BFF (new endpoint) · SpaarkeAi code page · sprk_communicationconversationpage code page · CommunicationConversationPanel PCF (v1.3.0).
