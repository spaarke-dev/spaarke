# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-16 (048 complete on the branch; dev BFF deploy approved, waiting on SPE-admin confirmation)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | ADR-028 **A9** build (`notes/a9-bff-design.md`). 044–047 done; **048 done on the branch**; next 058 |
| **Step** | 048 complete: commits `a9c5faf1d`, `6e6fa656b`, `543afaefa`, plus the code-review fix commit. Two Opus adversarial passes; code-review and adr-check done (0 ADR violations; W1/W2 recorded as path-A tensions in `spec.md` and project `CLAUDE.md` §2). Publish size +0.06 MB (37.22 MB). Details: `notes/task-048-notes.md`. Master `19d068ece` merged as `474771605` (ledger count 128 = R3 047/048 + ontology 104) |
| **Status** | **Dev BFF deploy: owner-approved 2026-10-16.** Live = R3 `a6d30891a` (06:39), an ancestor of the branch. Deploy candidate = branch head (contains master `19d068ece`: UAC #2016 `2e4ffc654`, ontology #2020/#2031 `9381e6f62`). Confirmations: ontology OK, UAC OK, ebilling OK, provisioning N/A. **Not delivered (held, then expired, different permission mode): SPE-admin-r2 (required by the deploy rule), word-add-in ×2, assistant-enhancements, project-coordination** — the owner must approve a resend in those terminals or give their status. The three retired `Documents__ShareLinks__*` app settings are absent on dev (names read only) |
| **Next Action** | 1. Get SPE-admin-r2's confirmation (via the owner), then re-read the live ProductVersion, run the ancestor check, deploy with `Deploy-BffApi.ps1` (pwsh), hash-verify, `/healthz`, and check the ontology Writer MI (`69040982-612e-469e-a85f-26d5172367c5`) is unchanged. 2. Send the ProductVersion and deploy time to ontology (it holds the 062 prompt sync and the decisionRecords layout seed until then) and the SHA to UAC (server gates for #994, #1620, #2002, share-link retirement). 3. Task 058 (SPA multitenant sign-in). Then 075, 076, 074; 070–072; the R3 PR (`/conflict-check` first). Uncommitted researcher agent-memory files await the owner |

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
1. Dev BFF deploy (see Quick Recovery): SPE-admin confirmation, then deploy and notify ontology and UAC.
2. 058 → 075 → 076 → 074 (074 and 059 are live and owner-gated).
3. 070/071/072; then the R3 PR to master after `/conflict-check`.

## Dependencies tracked elsewhere
- UAC task 185 = PR #1827 (head `df4ce6825`, one fix pending). Changes `ContactBindingDecision`, `ContactIdentityStore` (`BindOidAndConsumeInviteAsync`), `ContactIdentityBinder`, `CallerPrincipalResolver` (CIAM invite-code header), `CorsModule`; SPA `invite-code.ts`, `bff-client.ts`, `BffAccessState`, `WorkspaceHomePage`, `App.tsx`. Merge master into R3 when it lands, BEFORE 046/047/058 start. UAC will notify.
- Provisioning PR #1823 (`ef24853ec`) adds `sprk_decisionrecord` / `sprk_decisionrecords` / `sprk_regardingservicerequest` to `ExternalDataService.ServiceRequestRelations` (SpaarkeMaster 1.2.2.0 1:N `sprk_decisionrecord_regsvcreq`; pinned test counts 8 -> 9). Merge master into R3 after #1823 merges; re-run `ExternalDeleteServiceRequestTests` and the relationship pin test.
- ISS-001 (#1623), ISS-002 (#1633), ISS-003 (#1636), ISS-004, ISS-005 (#1673).
- Copilot agent still uses `access_as_user` on `1e40baad`.
- Operator: `mailbox-central@spaarke.com` + Exchange RBAC scoping for the stamp UAMI (H14a); BFF app user needs `prvDelete` on `sprk_servicerequest` (verify live).
