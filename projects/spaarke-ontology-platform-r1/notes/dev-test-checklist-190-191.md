# Dev testing checklist: tasks 190 and 191 (Budget inquiry Cc/Bcc, failed-send recovery)

> Written 2026-11-13 for the owner. Both tasks are on dev: the BFF has master `c2f366bf52` plus the mcp spike (uac-r2 deploy),
> and the SpaarkeAi Console was built from `c2f366bf52`. Tasks 180, 182 and 188 are still in progress; this list does not cover them.

**Setup.** Use a fresh `zz-` inbound communication triaged as fee/scope on a matter that has an active budget and no revision.
Its Signal ("Fee or scope change, no budget revision") appears after the nightly run. Open its Work Item and choose
**Send budget inquiry**. Ask me if you want the `zz-` row created for you; that is a Dataverse write and needs your go.

## A. Cc and Bcc on Send budget inquiry (task 190)

| # | Do | Expect |
|---|---|---|
| A1 | Open the email step | The old warning "Cc and Bcc are not sent with this step…" is gone. Cc and Bcc fields are there. |
| A2 | Send with one internal To, one internal Cc and one internal Bcc | All three receive the email. Cc shows in the headers; Bcc does not. |
| A3 | Look at the sent email's footer (A2 recipients are all internal) | The matter name shows, the same as before. |
| A4 | Send again with an **external** address in Cc only (To internal) | The footer shows no matter name: one external recipient anywhere makes the footer external-safe. |
| A5 | Same as A4, but with the external address in Bcc | Same result as A4. |
| A6 | Enter a malformed address in Cc, then try to finish | The step refuses before anything is written, the same way it does for a bad To. |
| A7 | Open the communication record for A2 | To, Cc and Bcc are recorded; the regarding matter and service request are set; there is one `.eml` on the matter. |

## B. Normal send still sends once (task 191)

| # | Do | Expect |
|---|---|---|
| B1 | Send a budget inquiry normally | Exactly **one** email arrives, with no duplicate. Before 191, a slow send could be retried silently and arrive twice. |
| B2 | Finish the decision | You get a Decision Record, the Signal closes as Acted, and there is one service request. |

## C. Failed-send recovery (task 191)

You only see these screens when the mail server fails a send. That happens on its own during Graph outages (as on 2026-11-12).
On a normal day you cannot force it from the app. **If one comes up, check these:**

| # | Situation | Expect |
|---|---|---|
| C1 | "The email was not sent" (definitely not sent) | **Retry** sends once and reuses the service request already created. You do not get a second service request. |
| C2 | "We can't tell whether the email was sent" | You must pick **Send again** or **Record without sending**. Neither happens on its own. |
| C3 | C2, then **Record without sending** | The decision is recorded, the status text says the email was not sent, and no email goes out. |
| C4 | Close the wizard in C1 or C2, then reopen the Work Item | The same unfinished review opens at the email step, with the same choice. A new review does not start. |
| C5 | While C4 is unfinished, a **second user** opens the same Signal and starts a decision | That user is refused, and the message names who started the decision and when. |
| C6 | Any send failure | You see an error on screen. A silent retry or a silent success is a defect. |

If C1–C6 never come up during your testing, that is fine. The code paths have unit tests. Tell me, and I will keep them as a later check.

## D. Report back

For any failure, send a screenshot plus the time (to the minute) and the Work Item or communication name. I find the trace from those.
Don't delete anything to recover; I will propose the clean-up.
