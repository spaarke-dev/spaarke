# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-17 (048 complete and on dev; dev BFF = d5401cf2d)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | ADR-028 **A9** build (`notes/a9-bff-design.md`). 044–048 done; next **058** (SPA multitenant sign-in) |
| **Step** | 048 complete (`notes/task-048-notes.md`: two Opus passes, code-review and adr-check, publish size +0.06 MB). Master `e702c3401` merged as `d5401cf2d`. The route ledger count is 131 (master 129 + R3 047/048) |
| **Status** | **Dev BFF live = R3 `28720f997`.** Deployed 2026-10-17 06:01:43–06:03:49Z, owner-approved (the owner pre-approved it "when #2035 lands"). Contents: master `1d435e695` (UAC #2052 `901c7eeed`, #2025 `bed14dd67`, #2035 `1d435e695`; ontology `e702c3401`) plus R3 048 and the #1576-guard marker `fb89bd12d`. Checks: SHA-256 verified, `/healthz` 200, ontology Writer MI still assigned, no app settings changed. Before the deploy the live build was `d5401cf2d`, an ancestor. Confirmations: ontology OK, word-add-in OK, UAC (owner of the change). Held or expired: SPE-admin-r2 and assistant-enhancements. No reply: ebilling (it reported nothing live earlier). UAC imports TrackingFieldTrio 1.0.50 after this. The route ledger count is 132. Dev external SPA = R3 `88697cfc1` (run `39004032735`, 2026-10-17 04:51Z, owner-approved; UAC, ontology and provisioning confirmed; local typecheck, build and check:dist pass; 19 vitest timeouts under load, and all 10 files pass with a 300 s timeout). Master `901c7eeed` (UAC #2052) is merged and waits for the next BFF deploy, together with UAC #2025 and #2035 once they merge. **Owner 2026-10-17:** an interim R3 → master PR opens after the owner UAT round passes (UAT script given in chat); UAC 186 is a draft cut from our branch and waits for it |
| **Next Action** | The owner runs UAT on dev. If it passes: `/conflict-check`, a code-review pass, then the interim PR (send UAC the PR number). The next BFF deploy needs owner approval (#2052, #2025, #2035). Then task 058 via task-execute. Then 075, 076, 074; 070–072; the R3 PR (`/conflict-check` first). Uncommitted researcher agent-memory files await the owner |

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
1. 058 → 075 → 076 → 074 (074 and 059 are live and owner-gated).
2. 070/071/072; then the R3 PR to master after `/conflict-check`.

## Dependencies tracked elsewhere
- UAC task 185 = PR #1827 (head `df4ce6825`, one fix pending). Changes `ContactBindingDecision`, `ContactIdentityStore` (`BindOidAndConsumeInviteAsync`), `ContactIdentityBinder`, `CallerPrincipalResolver` (CIAM invite-code header), `CorsModule`; SPA `invite-code.ts`, `bff-client.ts`, `BffAccessState`, `WorkspaceHomePage`, `App.tsx`. Merge master into R3 when it lands, BEFORE 046/047/058 start. UAC will notify.
- Provisioning PR #1823 (`ef24853ec`) adds `sprk_decisionrecord` / `sprk_decisionrecords` / `sprk_regardingservicerequest` to `ExternalDataService.ServiceRequestRelations` (SpaarkeMaster 1.2.2.0 1:N `sprk_decisionrecord_regsvcreq`; pinned test counts 8 -> 9). Merge master into R3 after #1823 merges; re-run `ExternalDeleteServiceRequestTests` and the relationship pin test.
- ISS-001 (#1623), ISS-002 (#1633), ISS-003 (#1636), ISS-004, ISS-005 (#1673).
- Copilot agent still uses `access_as_user` on `1e40baad`.
- Operator: `mailbox-central@spaarke.com` + Exchange RBAC scoping for the stamp UAMI (H14a); BFF app user needs `prvDelete` on `sprk_servicerequest` (verify live).
