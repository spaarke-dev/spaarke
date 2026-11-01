# Current Task State

> **Last Updated**: 2026-11-01 ~14:30Z (context-handoff). origin/work/ebilling-r1 = d566e5f7fd. Local work = d566e5f7fd + merge of origin/master (c9072272dd, clean) + EB-D-182 (43b0d02b90), NOT pushed. The full gate on that tip is re-running.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Running** | (1) Background full gate `bhq1yzfxq` on the master-merged tip: every test project, the full unit suite with no filter. The first run was invalid because a locked compiler left stale binaries (8 ArchTest and 12 unit failures, all from the stale build); `dotnet build-server shutdown`, then rebuilt. (2) Implementer (sonnet) `task/ebill-151c-email-ap`, resumed with the EB-D-182 decisions; it waits for the pushed work sha to rebase onto. |
| **Next action** | (1) When the gate is green: push work, send the sha to the 151c agent, then open the master PR (merge-to-master skill, Path A). The PR body names that it carries 097b AND 097c together (ontology-r1 condition) and lists 097a–f, 101, 104, 150, 151a, 151b, 152, 191, 192, the resubmissionof field security and 049 seed owner, with the full integration counts. Request ontology-r1's review and send the PR number to word-add-in. If the gate fails for real, fix it before pushing. (2) When 151c reports: review, merge, gate. Then three schema runs on dev, each after notices: AP export email on `sprk_ebillingsetting`; `sprk_senttoapdate` on `sprk_billingexport`; `sprk_regardingbillingexport` on `sprk_communication` (tell uac-r2 and field-mapping). The 151b dev import needs the owner's OK. |
| **Master PR** | Not open yet. ontology-r1 reviews it. |

## Owner decisions this stretch
- EB-D-182 (2026-11-01): Email to AP uses server-only byte attachments on the send-as-user path; the email is linked regarding the batch (new lookup on sprk_communication); one "Sent to AP Date" column on the batch, set on the first successful email or by "Mark Sent to AP", with no per-invoice copy (invoice views show it through the batch link).
- 192 filedUnder approved; 191 reader preflight approved; dev reader setting, 049 seed, resubmissionof field security, 015 delete: all done on dev.

## Dev state
- BFF fcf01480f4 (R3, 14:03Z), our six keys intact. The ApExport code page and the 151b ribbons/views are NOT deployed.

## Peer state
- word-add-in: client PRs #2501 (097f, merged) and #2503 (192). It waits for our master PR number (097b/c/e/f, 191, 192).
- field-mapping #2504 (039): no objection sent.
- ontology-r1: all ontology-file changes OK'd (incl. 50c9c63ccc); the 049 dev seed confirmed.

## Open items
- Older notes name the owner in plain text (003, 004, 011, 029, 091). Clean up in a docs pass.
- 151b: 053 must add `sprk_trackingnumber` to `sprk_Invoice/Entity.xml` before any non-dev import.
- 050a §9 items 2–4 (owner).

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK; schema runs need notices (warn of ~1 min of invalid-column faults on column deletes). StoredFileReconcileJob stays off until F4 merges and DEF-044 is fixed. After merges: 5 builds, ArchTests, broad filter before pushing (ISS-005 flaky, #2469). If builds report a locked file, run `dotnet build-server shutdown` and rebuild. Max 4 concurrent subagents; every agent launch states a model. No bare jq, no grep -P.
