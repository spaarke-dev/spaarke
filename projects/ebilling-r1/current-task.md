# Current Task State

> **Last Updated**: 2026-10-27 (context checkpoint). The 095 path is urgent (EB-D-158); word-add-in-r1 waits on 095 working end to end on dev.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Branch** | work/ebilling-r1 (draft PR #1911), tip e1ba2e27d9 + later docs. Gate on e03283c381: broad 9,472 / Arch 1,557; full integration 199/8/0 and 353/23/0 (uac-r2 wants full integration counts in the master PR body; re-run on the final tip). |
| **In flight (main session)** | **086 live steps on spaarkedev1** (owner pre-approved EB-D-158; notices to uac-r2 + R3 SENT before). Probe DONE: HTTP 403 0x80040299 "...is missing prvReadsprk_StoredFile privilege (Id=ef068f7b-6a82-4c37-bf25-95c0129e0b47) on OTC=11029 for entity 'sprk_storedfile' (LocalizedName='Stored File')", privilegeCount=43, no row. NEXT: quote it into config/secure-record-owner-role.json evidence (replace PENDING-LIVE-PROBE) on task/ebill-086-storedfile-secure-child in worktree C:\code_files\spaarke\.claude\worktrees\agent-a14f0a37a4f5512d5, commit+push; then from that worktree `scripts/Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` (dry, expect 1 add), `-Apply`, `-Verify` (expect 40 tables); then `scripts/Set-ChildRecordCreatorPersonSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -OntologyWriterApplicationId 69040982-612e-469e-a85f-26d5172367c5` dry, `-Apply`, `-Verify`; notices after to uac-r2 + R3. |
| **Running agents (4)** | **094** (task/ebill-094-submit-invoice; told: merge 098 now; own 092-review K2 §9.1 undo for all channels and K4 decide by channel not null sprk_document; fill 095's ISubmitInvoiceFromEmail seam). Adversarial reviews: **098** (786d1ea17a..2f50cddec9), **086 pass 1 of 2** (22b803e1a5..6482277b9b + the merge), **095** (work...96681b8825). |
| **Next actions** | (1) Finish 086 live steps above. (2) 092: word-add-in-r1 OK'd `ebilling.submission.file_scan_timeout` → add it to §9 outcomeReasonCode table (Failed) in notes/095-from-email-contract.md; quick scoped re-check of the fix diff bbf9cd04e8..3add11454c; merge 092 into work + gate; update `.claude/constraints/provisioning.md:269` (retired Finance__EbillingOutputContainerId). (3) Reply word-add-in-r1 with the exact errorCode/reasonCode of 095's no_message_id body-shape 400 (read 095 notes/contract tests). (4) As reviews report: route fixes to their agents, send review requests, merge. 086 needs a 2nd adversarial pass. (5) 089 (built, live done): resume agent when uac sends 194b: merge 194b, answer sprk_policy primary parent, BRR immutability test (ontology), NFR-05 guarded-child must-fire evidence (ontology challenge), POML Q10 Restrict→RemoveLink; then 2 adversarial passes; merge (expect 7 uac-file conflicts — reuse 086's resolution 22b803e1a5). (6) After 086 + 092 merge: launch 123. (7) When uac #2348 lands: add sprk_invoicelineitem to TheInvoiceFamily_FollowsAnOrdinaryMove_ThroughTheInvoice. |

## Branch status
| Task | Branch @ SHA | State |
|---|---|---|
| 092 | task/ebill-092-submission-files @ 3add11454c | Review done; F1 (scan wait, `file_scan_timeout`) + K6 fixed; K2/K4 → 094; deploy precondition: 091 + 079 FileStore settings live first. Ready to merge after re-check. |
| 089 | task/ebill-089-assignments @ 8f4920ce0c | Live on dev (role 38/38, probes recorded). Waits for uac 194b. |
| 086 | task/ebill-086-storedfile-secure-child @ 6482277b9b | Built on 089's tip; live steps in progress; review pass 1 running. |
| 098 | task/ebill-098-external-marker @ 2f50cddec9 | Review running; uac-r2 must accept `AppOnlyInvoiceFamilyHiding`. |
| 095 | task/ebill-095-from-email @ 96681b8825 | Review running; 503 until 094 fills the seam; wire unchanged (word-add-in-r1 confirmed). uac-r2 review requested. |
| 094 | task/ebill-094-submit-invoice | Agent running. |

## Owner decisions 2026-10-27
EB-D-153..161 (see notes/decisions-log.md); **162** Billing Admin Read Global → Deep (DONE: merged e03283c381, EB-D-150 exception removed per uac-r2, live on dev, -Verify PASS); **163** retire Procedure Library SPE container → new task 124 (after 116, 092); customer-provisioning told (they built nothing; #2232 is their only eBilling item, waits for #1911 on master; they have the Finance__FileStore__* settings).

## Open owner questions
- **Malware scanning off on a customer stamp** (refuse / allow with sign-off / allow): owner asked for a fuller explanation — answer pending. Provisioning (#2232) derives `Finance__FileStore__MalwareScanningEnabled` from `enableMalwareScanning`; H13 fails both mismatch directions; no refusal until the owner rules.
- **OP-5** (095): may a user re-attempt after ANOTHER user's Failed attempt? Today 409 document_already_submitted; word-add-in-r1 fine either way.
- Explicit re-check button (R2) into R1?

## Peer state
- **uac-r2:** reviews owed: 086, 089 (on 194b head), 094, 095, 098, 117, 123, 091 items. Will send 194b head.
- **ontology-r1:** agreed 089 items 1-3 (BRR immutable — needs a test), no objection to EB-D-162.
- **word-add-in-r1:** OK'd file_scan_timeout (wants it in contract §9); always sends X-Idempotency-Key; #2359 merged (inert until manifest redeploy); release waits for end-to-end on dev.
- **R3:** #2360 merged (078f62bcad); dev stays on de38296499 until contact security schema applied; no master-built BFF to dev before that.

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK; schema runs need notices (uac-r2, R3, ontology as relevant). StoredFileReconcileJob is not enabled anywhere before F4 merges and DEF-044 is fixed. After merges: build the 5 test projects, broad filter + ArchTests before pushing. Remove worktrees only with Remove-WorktreeSafe.ps1. Max 4 concurrent subagents.
