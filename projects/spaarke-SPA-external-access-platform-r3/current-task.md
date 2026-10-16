# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-13 (A9 tasks created; check-2 fixes 061–063 created)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | ADR-028 **A9** build (owner-approved design `notes/a9-bff-design.md`, Teams included; ADR amended `63b7c575b`). R3 owns it end to end. Owner check-2 fixes 061–063 |
| **Step** | 2026-10-13: A9 tasks created (044–048, 058, 059, 074–076); 060 cancelled; 012/056 superseded-in-part notes; 071 widened (9 Teams runs); check-2 fixes 061/062 (startable), 063 (blocked: owner decision on app-only enqueue rule); 002 gained grid-config labels (issue 2). Drift check + POML validator green. SPA deployed to dev (run `38420637859`), S2a pass; owner checks 1–2 done |
| **Status** | Commits after #1810 (`5bfedfe3d`, notes, `63b7c575b`, task creation) are on the branch, not on master: next PR needs `/conflict-check` (word-add-in PR #1815 may edit `.claude/constraints/auth.md` line 69, adjacent to ours) |
| **Next Action** | 2026-10-16. **Dev BFF live = R3 `2c2fed38d`** (047 plus the UAC #2005 fixes, master `709101aa6`), deployed 12:56-12:59Z on the owner's go. Before: every BFF-deploying session confirmed; the owner said spaarke-2d is closed. After: hash-verified, healthz 200, the ontology Writer MI and its two app settings verified. Notices were sent. **Dev external SPA = R3 `2c2fed38d`**: run `38906042329` at 13:13Z, dispatched from the R3 branch (pushed). It carries UAC's 185 invite-code SPA work and R3's 066 work. Local vitest: 1030 total, all pass (23 timed out under load and pass one file at a time). UAC was warned that a master-dispatched SPA run would now remove R3's SPA changes. Option 100000012 was applied on spaarkedev1 at 11:32Z. Registration still needs task 059 (Entra) before it can be used on dev. **Deploy rule (owner 2026-10-15):** before any dev BFF deploy, read the live DLL ProductVersion (Kudu), run `git merge-base --is-ancestor <live> <deploy>`, get every BFF-deploying session to confirm its work is in the build (SPE admin especially), and get fresh owner approval. Rebuild the TEST project after a merge (`dotnet build src/server/api/Sprk.Bff.Api/` does not rebuild it, and `--no-build` then runs stale tests). **046: DONE** (on dev). **047: DONE** (merged `176857143`; full suite on the merge: 3 TaskCanceled, which pass when rerun alone). The schema script's label lookup and the solution labels were fixed (`69eed60c7`; the agreement test now compares labels, seeded). K items: ISS-013 #2001 (removeUnverifiedEmailClaim) is owned by provisioning task 264; the raw tid/oid K item needs no change (auth.md:173). NOT on dev. UAC review #2005 (2026-10-16), done so far:
- Master 709101aa6 merged as 344cb0cb3 (F4).
- F1 + K2 fixed in 988579709 (the home bind consumes the invite hash; email reads log a label). Census entry added in 9e860c692.
- UAC's re-check: F1 and K2 hold. F3 (the census Seam needed a guard) and the K2 error-body leak are fixed in 4eacd5934; UAC is re-checking that diff.
- K1 and K3 close when BindHomeOidAsync folds onto UAC PR #2007's store-level fallback at the next master merge.
- K1 removeUnverifiedEmailClaim is a stated deploy prerequisite in the 047 notes.

UAC's re-check of 4eacd5934 came back CLEAN (2026-10-16), so 047 is clear to deploy to dev from UAC's side. **Option 100000012 was applied on spaarkedev1 at 11:32Z** with owner approval: -Apply DONE (it also published contact and systemuser), -Verify PASS, and the label reads back. Rollback: DeleteOptionValue, safe while no contact carries the value. **047 dev deploy:** the owner approved starting coordination (the final go is still needed). Live is eb5ba4011, an ancestor of 7f88d5e65. Master a8c3182ec's 15 newer commits touch no src/server or tests. The round was sent at about 11:35Z to SPE admin, UAC, ontology, provisioning, word-add-in, ebilling and spaarke-2d; b4 and assistant-enhancements hold messages for the owner. Next: collect the confirmations, ask the owner for the go, deploy with start and finish notices, then verify the version, healthz and the ontology Writer MI. Ontology turned on its 144/148 writes by an app-setting change (owner-approved, 02:26Z); the code stays `eb5ba4011`. ISS-012 #1930 filed. Uncommitted researcher agent-memory files await the owner. Then 048, 058, 075, 076, 074; 070-072; the R3 PR. |

### Critical Context
- **Parallel pattern:** BFF tasks run in isolated worktrees under `C:\code_files\spaarke\.claude\worktrees\agent-*` (`git reset --hard <R3 sha>` first), commit there, main session merges `--no-ff`; after a merge re-verify ledger line cites (the guard only checks format). SPA tasks run in this worktree, uncommitted; the main session commits by explicit path. Agents never edit TASK-INDEX, this file, POML status or `.claude/`.
- **Review policy:** auth/security/tenant-isolation changes get two fable passes; other FULL tasks one. Fix rounds sonnet; a fix that changes approach gets a scoped re-check. Max 4 agents.
- **Bookkeeping edits:** python via PowerShell here-strings (sed breaks on the status emoji). Check with `pwsh -NoProfile -File scripts/check-task-status-drift.ps1`.
- **Do not run the full BFF unit suite under load**; use filtered runs. Bash is very slow on this machine: prefer PowerShell, Grep, Read.

## Owner answers 2026-10-12 (recorded in project CLAUDE.md §3)
Option B; no delete after close; invoice upload = invoice rights only; team-access and renewal bell items wanted; contact owned by the customer's primary BU; intake (department from contact, Invention partners behind a flag, incidents simple, fixed lists, NDA review + issue); grid views from Dataverse views; 060 shared service OK if Model 1 allows; dev dispatch "ok"; 060 tenant-wide group permission approved if it cannot be scoped. Deferred: DEF-001 #1736 NDA generation, DEF-002 #1737 incident alerting.

## Open owner questions
1. Owner deletes probe message `b38f0ab7-03c7-f111-a05c-0022482913fc` by hand.
2. Owner test for 067: create a service request; expect an RQST number other than RQST-0001 (dev next value read 3).
5. 065 DONE: merged `bbcb80659`. Owner decisions D1-D9 are in notes/task-065-notes.md. ISS-009 #1859 is closed. ISS-010 #1864 (Internal Only not enforced by Dataverse security for licensed users flagged external) is filed for an owner/UAC decision. Task 068 (the switch) is now startable.
4. 065 leftover: (d) the ACS endpoint has a read-only check before deploy.
6. 044: option A; the owner approved the dev dry run 2026-10-14 and it was clean (it would create 1 option, 5 columns, 1 key and FLS on 3 columns; nothing written). Next: the owner approves -Apply, then -Verify, then notes/task-044-schema-record.md.
3. Owner decisions 2026-10-13 (resolved, recorded in POMLs): 045 = option A (contact-grants reachable; census pins routes); 065 = explicit false for emails too, fix Message→Email mapping (explain how the Message path works); 062 = createdby only when a person; 067 = run (done).

## Owner check 3 (CIAM partner, 2026-10-13): findings
- #1 documents "could not be loaded": `sprk_documenttype` is a choice (number) but `DocumentRow` maps it as a string, so any typed document fails the whole list (App Insights: 4x InvalidOperationException on `$.value[n].sprk_documenttype`) → **task 064**.
- #2/#3 no preview / no download in the Documents grid: grid config 3af4102c has `selectionMode: none`; no download action → **task 066**; preview → **DEF-003 #1828** (R4).
- #4 "+New": record detail is read-only by design; Invoice create → **DEF-004 #1829** (invoice intake project, ontology-r1).
- #5 partner does not see MDA messages: the MDA send leaves `sprk_isinternalonly` null, and a null flag is hidden from contacts (fail closed) → **task 065** (owner: default No, sender can tick Internal only; old dev rows not corrected).
- Upload 409s in the logs = same-name file already exists (ConflictBehavior.Fail by design). Message POST 403 on an invoice = the access level lacks Create (by design).
- Found in the logs: **ISS-006 #1830**, UAC's JIT revocation sweep logs one failing record every 5 min on dev. UAC triaged it as non-urgent and not a security gap (per-record catch verified at `SpeContainerMembershipSync.cs:356-377`).

## Option B known limits (notes/task-039-notes.md §12.11) (notes/task-039-notes.md §12.11)
Messages and feed give no "incomplete" signal; `Truncated` may over-report; a project pair naming a deleted matter stays secure permanently; `IGenericEntityService` filing seam is DI-supplied (drop = over-hide, not leak).

## Next actions (in order)
1. Commit + push task creation; PR the post-#1810 commits (after `/conflict-check`).
2. Owner answers above; then A9 Wave 1: 044 (owner-gated dev import) + 045 (opus, 2 adversarial passes); CHK2: 061 + 062.
3. A9 Wave 2: 046 (rebase onto UAC 185 if merged) + 059 (live, owner-gated).
4. 047 → 048 → 058 → 075 → 076 → 074; then 070/071/072.

## Dependencies tracked elsewhere
- UAC task 185 = PR #1827 (head `df4ce6825`, one fix pending). Changes `ContactBindingDecision`, `ContactIdentityStore` (`BindOidAndConsumeInviteAsync`), `ContactIdentityBinder`, `CallerPrincipalResolver` (CIAM invite-code header), `CorsModule`; SPA `invite-code.ts`, `bff-client.ts`, `BffAccessState`, `WorkspaceHomePage`, `App.tsx`. Merge master into R3 when it lands, BEFORE 046/047/058 start. UAC will notify.
- Provisioning PR #1823 (`ef24853ec`) adds `sprk_decisionrecord` / `sprk_decisionrecords` / `sprk_regardingservicerequest` to `ExternalDataService.ServiceRequestRelations` (SpaarkeMaster 1.2.2.0 1:N `sprk_decisionrecord_regsvcreq`; pinned test counts 8 -> 9). Merge master into R3 after #1823 merges; re-run `ExternalDeleteServiceRequestTests` and the relationship pin test.
- ISS-001 (#1623), ISS-002 (#1633), ISS-003 (#1636), ISS-004, ISS-005 (#1673).
- Copilot agent still uses `access_as_user` on `1e40baad`.
- Operator: `mailbox-central@spaarke.com` + Exchange RBAC scoping for the stamp UAMI (H14a); BFF app user needs `prvDelete` on `sprk_servicerequest` (verify live).
