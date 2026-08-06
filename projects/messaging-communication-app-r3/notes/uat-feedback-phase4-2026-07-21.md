# Phase 4 UAT feedback — 2026-07-21 (spaarkedev1, Matter pilot)

Operator UAT on the deployed surfaces (030 PCF preview + the record-filtered modal). Captured verbatim-ish for the next refinement iteration. Grouped by surface. Screenshots referenced: PCF preview (2 states), the modal, and a Teams chat as the layout target.

## A. PCF preview (`CommunicationConversationPanel`) — 8 items
1. **Add a `Title` PCF property** (manifest input property) — default **`MESSAGES`**.
2. **Title format**: 14px Segoe UI, weight 600, color `#242424`; row height 20px.
3. **Thread row**: message count in a **gray circle, right-aligned**; if there are new/unread messages, put the word **`New`** to the LEFT of the count.
4. **Remove the divider line** (the horizontal rule under the thread row).
5. **Open icon**: use the **box-with-arrow** glyph (the same "open" icon used everywhere else); **icon only, no "Open" label**.
6. **No bottom line**; add a little more padding to the `1 of 1 · 2 messages` footer.
7. **Channel pill to the LEFT**: message = green pill, email = light-blue pill; **no icon inside the pill** (text only).
8. **Sender name**: Segoe UI 14px, **not bold**; show **date + time received**.

## B. Record-filtered modal (hosts shared `ConversationWorkspace` + `ConversationView`) — Teams-like redesign
- **Q (answer in report)**: does it follow our modal standard design shell? Is it a Dataverse OOB modal or our custom modal? → It's our **custom Fluent v9 `Dialog`** (task 030 `ConversationModal.tsx`), NOT OOB; currently a plain `Dialog`, NOT the `RecordNavigationModalShell`. Decide whether to adopt the standard shell.
1. **Move the `x`** to the upper-right corner.
2. **Modal title** = `Messages`.
3. **Adapt layout to resemble Teams** (see attached Teams screenshot).
4. **Thread pane**: remove the "Filter threads" text filter (not needed).
5. **`+ New`** → just **`+`**.
6. **Side (thread) pane**: subtle contrast background vs the message pane.
7. **Chat-area toolbar**: Email icon-only filter, Message icon-only filter, **Unread** icon filter, and a **search icon** that reveals a text filter of messages. (Replaces the current Email/Message toggle buttons + "All messages" word dropdown with an icon toolbar.)
8. **No right-side scrollbar** — use a **circle down-arrow** affordance instead.
9. **Message blocks (Teams-style)**: OUTSIDE/above the bubble put the **type pill, name, date/time**; then the bubble body — **light gray** for others, **light blue** for the current user.

## C. Send path — BUG (blocker for FR-06 compose)
Sending a message from the modal compose bar fails:
```
Unexpected error: Denied by the resource provider. Status: 401 (Unauthorized)
ErrorCode: Denied  Content: {"error":{"code":"Denied","message":"Denied by the resource provider."}}
```
- Symptom = ACS/resource **401 Denied** on the send (not a client bug per se). Likely a **spaarkedev1 BFF/ACS config or auth** issue (managed identity / ACS connection / the BFF's ACS resource token), OR the BFF messaging endpoint isn't wired in this env. **Investigate**: is the BFF deployed + ACS-configured in spaarkedev1? Is the caller's OBO token reaching ACS? This is env/backend, separate from the UI polish above.

## Scope note
Items A + B are a **UI refinement iteration** touching the PCF (`CommunicationConversationPanel`) AND the shared `ConversationWorkspace`/`ConversationView`/`MessageBubble` (so they also change the 031 widget + 032 code page — same shared components). Item C is a backend/env investigation. To be planned as the next wave (tasks TBD) after the SpaarkeAi widget deploy the operator requested.
