# 106 — Send in the toolbar (UAT round 8, item 1)

Owner (2026-10-06): "the 'send email should be in the tool bar next to Find (the lebel can be the email icon and Send' - user knows its email)".

## What changed

| Host | Before | After |
|---|---|---|
| Outlook | Full-width "Send Email" button at the top of the pane body (any tab), once a document/record is linked | Toolbar: Save · To Do · Find · **Send** (mail icon, subtle small button, tooltip "Email the document and record links"). Still an **action** that opens Outlook's native compose (task 036; "Outlook unchanged", task 096) — not a tab. |
| Word | Email **tab** after Find, labelled "Email" | Same tab, labelled **Send** — both hosts read the same. |

Unchanged: gating (`resolveSendEmailAffordance`, capability-only, NFR-10); absent rather than disabled when nothing is linked; spinner + disabled while the compose window opens; the error MessageBar and the live-region announcement stay in the pane body (the bar shows only on error).

## Files

- `TaskPaneToolbar.tsx` — optional `onSendEmail` / `isSendingEmail`; the button renders right after the `TabList` (the tabs container is now a flex row so it is not pushed right).
- `TaskPaneShell.tsx` — passes them through.
- `App.tsx` — supplies them when `canSendEmail`; body button removed (`Button` / `MailRegular` imports dropped).
- `TaskPaneNavigation.tsx` — Email tab label → "Send".
- `TaskPaneNavigation.test.tsx` — label assertions → "Send"; new "Send action (task 106)" block: after Find and not a tab, click calls the handler, absent without a handler, disabled while sending. Suite already in `ci-gated-suites.txt`.

## Gates

- jest `shared/taskpane`: 66 suites / 893 tests green; full suite 82 suites / 1,123 tests green.
- lint 0; typecheck 0 production errors.
- Code review: no findings beyond the added tooltip. ADR check: ADR-021 (Fluent v9 only) and NFR-10 (capability gating) hold.
- Live check (next UAT): Outlook after a save shows Send after Find and opens compose; Word's tab reads Send.
