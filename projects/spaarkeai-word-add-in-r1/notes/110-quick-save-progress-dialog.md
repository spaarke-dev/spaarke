# Task 110 — Quick Save progress dialog + link to the new document record

UAT round 11, item 2 (`042-uat-round11-2026-10-07.md`): clicking Quick Save opens the Spaarke dialog at once with a
progress state; the same dialog then shows success (with an "Open in Spaarke" link to the `sprk_document` record) or
the error.

## What changed

| File | Change |
|---|---|
| `word/commands/quickSaveDialog.ts` (new) | Dialog controller: open in progress → update/reopen with the result → open-link + close handling → completes the command once. |
| `word/commands/index.ts` | `quickSave` opens the dialog first (before bootstrap / save), shows results through it, `dialog.finish()` in `finally` replaces `event.completed()`. `notify()` is unchanged (still used by `openSpaarke`). |
| `word/commands/notify.html` | States `progress` (spinner, no buttons) / `success` (message + link + Close, never auto-closes) / `error` (8 s, Close, no link) / `info` (legacy 2.2 s). Office.js loaded lazily and OPTIONAL. |
| `shared/taskpane/services/quickSaveHelpers.ts` | `buildQuickSaveRecordLink` (wraps the existing `buildOpenRecordUrl` for `sprk_document`) and `isUrlOnConfiguredOrigin`. |

## Host-support decisions

- **Update in place**: `dialog.messageChild` when `DialogApi 1.2` is supported, the dialog object has `messageChild`, AND
  the page has said `ready` (a `messageParent {type:'ready'}` after it registered its handler — a message sent earlier is
  lost). Waits up to 3 s for `ready`.
- **Fallback**: close the dialog and open ONE new one with the final state in the URL (`status`, `message`, `linkUrl`)
  — used for hosts without DialogApi 1.2, a page whose Office.js did not load, or a `messageChild` that throws.
- **Link**: the page `messageParent`s `{type:'open-link', url}`; the command validates the origin against `ORG_URL`
  (https/http, same origin) and opens it with `openFileUrl` (`Office.context.ui.openBrowserWindow` when
  `OpenBrowserWindowApi 1.1`, else `window.open`). Without Office.js in the page the anchor's own `target=_blank` opens it.
  The page also refuses non-http(s) hrefs. `ORG_URL` unset (or id not a GUID) → no link, nothing said about it.
- **`event.completed()`**: exactly once, when the save is over AND the dialog is gone — user close (`DialogEventReceived`),
  page `close` message (Close button / the error's 8 s timer), or a 2-minute cap after the result is shown (the cap closes
  the dialog) — or immediately if no dialog could be opened. A dialog closed WHILE saving does not complete early: the
  command waits for the save to finish first (keeps the ribbon runtime alive for the POST), then completes.
- Single window size (30 x 35 %, `promptBeforeOpen:false`) for all states so an in-place update never clips an error.
- Success status is now `success` (was `info`); `info` remains the page's short transient notice.

## Live checks still to do (Word desktop + Word on the web)

1. Dialog appears immediately on click with spinner + "Saving to Spaarke…", then flips to success in place (no flicker/reopen).
2. "Open in Spaarke" opens the `sprk_document` record (desktop: `openBrowserWindow`; web: pop-up not blocked).
3. Closing the dialog ends the ribbon button spinner; leaving it open past 2 min closes it.
4. Whether `dialog.close()` programmatic or page `window.close()` raise `DialogEventReceived` (cap + `close` message cover both).
5. Behaviour on a host without DialogApi 1.2 (reopen path), and with the CDN (appsforoffice.microsoft.com) blocked.
6. Closing the progress dialog mid-save: the save still finishes and the ribbon command completes afterwards.
