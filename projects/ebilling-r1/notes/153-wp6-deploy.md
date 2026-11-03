# 153 — WP6 deploy and end-to-end: notes

## The AP mailbox (dev)

- The eBilling settings row on dev (`sprk_ebillingsetting` 92acd624-c6c8-f111-a05c-7c1e52154cae) sends Email to AP to `sprk_apexportemail` = **ap@finance.spaarke.com**. It was set 2026-11-03 at 15:42:32 and read back. The delivery service reads it live, so no deploy is needed.
- Earlier the same day, uac-r2 set it to invoices@finance.spaarke.com for a one-off test. The owner then created a dedicated AP mailbox and approved the switch, so the test email never reached the future intake mailbox.
- **Standing rule:** the AP address must never be a receive-enabled (monitored) communication account. If it becomes one, Email to AP refuses with 409 `ebilling.export.ap_email_recipient_monitored`, and nothing is sent. That is by design: the backup poller would otherwise file the AP email as an inbound communication.
- **invoices@finance.spaarke.com** is planned as the shared intake mailbox. Don't set it as the AP address once intake is on.

## First Email-to-AP test (2026-11-03, owner-approved)

- **Invoices:** Garrison and Ellison INV-2025-05166 (9723a5aa-3f01-6c5e-1ece-f3902a625006) and Ravenscroft and Vance INV #2025023029 (7c200dd1-57f6-8b24-4c67-feb3794841fb).
- **Eligibility:** the owner is an eligible approver on both through Assigned To Internal, the settings' default approver role (the matters have no role of their own).
- **Approvals:** done by a script the owner runs with their own token. The review page (110) is not deployed yet.
  - The script calls GET review, then POST lines/decisions (accept the flagged lines), then POST approve, which accepts the clean lines in bulk, then POST export-batches.
  - No app-only calls and no Dataverse writes.
- **Then:** the owner opens the batch, checks there is no New button on the AP emails grid (Add Existing may still show; known limit K1), and clicks Email to AP.
- **Check:** uac-r2 looks in App Insights for the route status, the server-only send line, no OWNERSHIP-REFUSED, and no inbound filing of the message.
- Result: (pending)
