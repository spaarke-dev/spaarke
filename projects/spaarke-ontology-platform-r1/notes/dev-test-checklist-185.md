# Dev testing checklist: task 185 (side-pane lookups and the shared email composer)

> Written 2026-11-15. Deployed to dev from master `acc96e1927` between 14:48Z and 14:52Z: the Console (auto-deploy), 9 wizard code pages,
> SmartTodo, SPE Admin, Document Relationship Viewer, and the PCFs Semantic Search 1.1.86, Communication Actions 1.3.22 and
> Tracking Field Trio 1.0.64. **Hard-reload (Ctrl+F5) each page first.** Run each check in light mode, then once in dark mode.

**"Side pane OK" means:** clicking **Select** opens the Dataverse side pane above the wizard. A pick shows as a chip. Reopening and
pressing Cancel keeps the earlier pick. Everything already typed in the wizard is kept. There are no white surfaces in dark mode.

## A. Lookups open the side pane

| # | Where | Field | Expect |
|---|---|---|---|
| A1 | Console → Create Project → Next steps → **Assign Work** | Assigned Attorney (and Paralegal) | Side pane OK |
| A2 | Console → Create Matter → Next steps → **Assign Work** | Assigned Outside Counsel | Side pane OK |
| A3 | Console → **Create Event** | Assigned To | Side pane OK. The pick is saved on the event. |
| A4 | Console → **Create To Do** | Assigned To | Side pane OK |
| A5 | Console → **Create Invoice** | Vendor Organization | Side pane OK |
| A6 | Console → **Create Work Assignment** | Law Firm, then Law Firm Attorney | Side pane OK. The attorney list holds only that firm's people. |
| A7 | Console → Rule Review → **New rule set** | Firm | Side pane OK |
| A8 | Console → Ontology admin → **new rule** | Limit to one matter | Side pane OK |
| A9 | SPE Admin → business-unit picker | Business unit | Side pane OK |
| A10 | Close a wizard after A1–A6 and open it again | — | The earlier picks are cleared. Nothing carries over from the last run. |

## B. Send Email in the create wizards (one email, sent at Finish)

| # | Do | Expect |
|---|---|---|
| B1 | Create Project → Next steps → **Send Email** | The step shows From, To, Cc, Bcc, attachments, the template picker and AI draft. It has **no Send button**. |
| B2 | From = My mailbox; two To recipients and one Bcc picked through the pane; attach a file; apply a template; use AI draft | Each control works. The template and the AI draft fill the body. |
| B3 | Back, then Next | Every value is still there. |
| B4 | Finish | **Exactly one** email goes from the chosen mailbox to To, Cc and Bcc with the attachment, filed on the new project. |
| B5 | Repeat B1–B4 once in Create Matter **opened as a code page** (from a form or ribbon, not the Console) | Same result. |

## C. Email Documents (Semantic Search and the Document Relationship Viewer)

| # | Do | Expect |
|---|---|---|
| C1 | Open a matter form with the Semantic Search control, select a document, choose **Email**, pick a recipient in the pane, send | The composer opens inside the control's dialog. The email is sent and filed as before. |
| C2 | Same from the **Document Relationship Viewer** | Same result. |
| C3 | Either host: let your sign-in expire, or send while offline | The message says the email was **not** sent and tells you how to retry. You never see a silent success. |

## D. Send Email dialog and the other composer hosts

| # | Where | Expect |
|---|---|---|
| D1 | Console → Quick Start → **Send Email**: template, AI draft, attach a file, send to yourself | The template and AI draft fill the body. The attachment arrives. |
| D2 | A record's **Email Members** | The template picker, AI draft and attachment upload are there and work. |
| D3 | The **Communication Actions** control on a communication form: compose | Same as D2. |
| D4 | **Tracking Field Trio**: compose | Same as D2. |

## E. Not covered by this deploy

- The **Communication page** (`sprk_communicationpage`) and the standalone **Rule Review page** (`sprk_rulereviewpage.html`) are not
  on dev, so they were not deployed. A7 runs inside the Console.

## Report back

For any failure, send a screenshot, the time (to the minute) and the host. Note anything in the browser console (F12) that is red.
