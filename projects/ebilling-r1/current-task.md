# Current Task State

> **Last Updated**: 2026-11-03 (after compaction). 151c is on master (#2526, 5796774091). The dev BFF has been on 5796774091 since 13:29Z (SPA-r3 deployed it) and `/healthz` reports Healthy. This session is ebilling-r1-7b; the duplicate 9c session should stay idle or be closed.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **State** | Dev intake is unblocked: the owner created `sprk_invoice.sprk_trackingnumber` on dev at about 13:45Z, and it is in SpaarkeMaster in dev. Source fix 21aec9a10b puts the attribute in `Entities/sprk_Invoice/Entity.xml`. The ApExport UI is fully on dev: code page, ribbons, the batch Active view and the 3 invoice views. uac-r2's after-notice checks 1–5 passed. |
| **Next action** | (1) The owner resubmits the invoice from Outlook web; it should become INV-792345.2. Then the correction test. Then ebilling verifies the server side by tracking number: the old invoice is Superseded (659490005); `sprk_resubmissionof` and `sprk_supersedesinvoice` are set; the old invoice's Signals are closed as Superseded; the old submission shows Accepted with "replaced by". Diagnostics: App Insights `6a76b012-46d9-412f-b4ab-4905658a9559`. (2) When the owner decides on scope for leave-and-come-back (the pane's (a)/(b), or the server-side (c)), write the ebilling task: `GET /api/finance/invoice-submissions?internetMessageId=…` plus a final-status notification. |

## Known on dev (not defects)
- `ai.invoice.binding_missing` for invoice-codeset-match, and for invoice-line-code too: dev has only the invoice-classify Binding. Seeding those Actions and Bindings is task 053 steps 2–3, which haven't run. It isn't fatal: code-set matching falls back to embedding ranking, and the proposals are marked unreviewed. Seeding them on dev is a live write and needs the owner's OK.

## Filed
- #2529: the submission job retries a permanent schema fault 5 times. That is why the owner's run took over a minute; a good run takes about 15 s.

## Waiting on
- word-add-in: the fix for Outlook desktop "Request Too Long" (new Outlook reports OfficeOnline, `naa=false`, so sign-in uses a popup with oversized cookies). Their researcher is checking NAA support; the auth change needs the owner's go. The `%3E/suggestions` 404 is the expected answer for an uncaptured email, not a bug.
- The click-through check that the AP emails grid shows no New button (on the owner's checklist through uac-r2).
- The owner deletes `C:\w15p` (my rm was blocked).
- K3 (owner's call): no Office Add In User role has Read on `sprk_billingexport`. It fails closed. A grant would need all six BU copies, the provisioning step and the deployment guide.

## Open items
- Task 153 step 4 conflicts with the rule that only SPA-r3 deploys the dev BFF: amend the POML. The rest of 153 waits for 136.
- 151d: the invoice form shows the batch's Sent to AP Date.
- Task 014 note §7: the next `sprk_invoice` RibbonDiff import must decide on the 7 Activate/Deactivate hides, which are source-only.
- Older notes name the owner in plain text (003, 004, 011, 029, 091): needs a docs pass.
- 050a §9 items 2–4 (owner).

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK. Schema runs need notices before and after, and the publish is entity-scoped only. StoredFileReconcileJob stays off. ebilling never deploys the dev BFF (SPA-r3 does). Master PRs: ontology-r1 review, then auto-merge, then the SHA to peers. Max 4 subagents, and every agent launch states a model. No bare jq, no grep -P.
