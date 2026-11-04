# Current Task State

> **Last Updated**: 2026-11-04 ~16:30Z. Session ebilling-r1-c7 (authoritative; d4 is the duplicate). Work head is pushed. The dev BFF is still master d763dc120e: **none of this work is on master or the dev BFF yet**.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Parallel task-execute agents, at most 4. Merge each finished branch into work. One adversarial review per change set, two for auth or tenant work. Owner 2026-11-04: "proceed with the work items including working in parallel where possible". |
| **Done today** | 089c (merged 593c70f2b5), 095c-5 (db1f70a605; dev `sprk_firmhold` -Apply/-Verify PASS 14:16Z), 089d option R (743bf32010; dev role "Billing Rule Reader" bed10b10 created, -Verify PASS 15:55Z, uac-r2 read-back matches; provisioning-b8 has the per-customer steps and is waiting for the master SHA). |
| **Running agents** | 082 procedure resolver (`task/ebilling-082`, opus) and 092 submission on files (`task/ebilling-092`, opus). Both also edit `InvoiceSubmissionPersister.cs`; merge them one at a time. Then 2 adversarial passes each. |
| **Background gate** | Full unit suite (Category!=Flaky), then full ArchTests on the work tip (started 09:46 local, output in scratchpad `gate-db1f70a605.txt`). The 3 integration test projects already built with 0 errors. Then measure publish size against a fresh master. Then ONE master PR. |
| **095d** | STOPPED by design (notes/095c §16). Owner decisions in THIS session: **order = option 1, apply then record** (ontology-r1 and uac-r2 conditions in §16 to add), **renumber = everything** (all names carry the new number). uac-r2 relayed further owner guidance from their session: re-run the AI only where a rule's outcome depends on it (list each rule with AI yes/no); a final review before merge = my passes + uac-r2's full review + ontology's K-3 view. Next: write §16.4 decisions, then split into 095d-1/2/3 POMLs (095d-2 after 082 and 092 merge), then launch 095d-1. |
| **089c follow-up (owner, this session)** | (1) Close old base-rule Signals as **Superseded** when a change is saved. (2) Replace the record-scoped-set 404 with a descriptive, trackable error, e.g. 409 `ruleset.record_scoped` pointing to the override routes, given only AFTER the caller's Read check passes, so an unreadable set stays 404 and existence is not leaked. (3) "provide edit rights as required": confirm the Billing Administrator role holds Write on `sprk_policy` (the change needs it); add it in the role source if missing. A live role change needs notices to uac-r2 and ontology-r1. Next: create task 089e and launch it. |
| **Open owner items** | 089d live proof (a test child BU under BU1): no go yet. Email to AP: the owner should open the Completed batch 14680609 (the other 2 batches are Failed, so no buttons is correct). word-add-in notice (095c-5 prefill 503) expired undelivered; resend on request. Deploy-EbillingFormsViews.ps1 needs its own go after the PR. Also: K6, the Files rule, refused-bell link, six guideline gaps, firm-name check, 161 spend, stash 1e84ffafed. Publish trees C:\wt95c4m/b/x can be deleted. |
| **Master PR checklist** | Gate green; publish size against fresh master; ONE PR (reviewers ontology-r1, uac-r2 whole diff, R3); auto-merge; send PR# and SHA to uac-r2 with a deploy request (Stored File count, 095c-5 pre-check, #2550); SHA to provisioning-b8 and word-add-in. |

## Hard rules (carry over)

`Set-ChildRecordCreatorPersonSchema.ps1 -Verify` exits 0 before any BFF deploy. `Ebilling:StoredFileReconcileEnabled=false` until uac-r2's gate passes. Ebilling never deploys the dev BFF (uac-r2 does). No live writes without the owner's go typed in this session, plus notices to uac-r2. Public repo: defect plus location only. Agent worktrees are removed only with `scripts/worktree/Remove-WorktreeSafe.ps1`.
