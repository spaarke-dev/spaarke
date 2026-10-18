# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-18 (interim PR #2086 merged as a82578cdc; dev BFF + SPA = ebc74b331)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | ADR-028 **A9** build (`notes/a9-bff-design.md`). 044–048 done; next **058** (SPA multitenant sign-in) |
| **Step** | Interim R3 → master PR **#2086 merged 2026-10-18 03:58:39Z as `a82578cdc`** (merge commit; CI 44 pass / 3 skipped; 0 behind, clean merge-tree). Branch = master at `a82578cdc`. The route ledger count is 132 |
| **Status** | **Dev BFF live = `ebc74b331`** (owner-approved 2026-10-18; 03:32:53–03:34:49Z). Contents: R3 + master `5f1593aca` (UAC #2083) + the invoice `statuscode` allow-list (`a6513cd56`). Checks: SHA-256 verified, `/healthz` 200, CORS present, MIs `mi-bff-api-dev` + `mi-ontology-writer-dev` assigned, no app settings changed; the live build before was `9721819ed`, an ancestor. **Dev external SPA = `ebc74b331`** (run `39094144398`, success 03:36:44Z, deployed after the BFF). Confirmations: UAC, ontology, word-add-in, provisioning, ebilling. Ebilling switched grid 3ff4102c to `statuscode` (03:42:42Z). Owner Partner UAT (CIAM Test User) signs in and works; CORS checks clean (owner-approved reads 14:40Z 2026-10-17: no platform CORS, origin allowed, preflight 204) |
| **Next Action** | Tell UAC (#2061 / 186, then 036 rebase onto master), ebilling and ontology that #2086 merged. Owner continues Partner UAT (checklist given 2026-10-17). Then task 058 via task-execute; then 075, 076, 074; 070–072. Pending deploys: ontology 102b (BFF before its Console bundle; owner approval). Close DEF-004 (#1829) as superseded when ebilling 051 merges. Uncommitted researcher agent-memory files await the owner |

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
- **ebilling 015 retires `sprk_invoice.sprk_invoicestatus` on dev (deploy gate).** R3 `a6513cd56` admits `statuscode` beside it (transitional). Ebilling runs `Remove-EbillingInvoiceStatus.ps1 -GridConfig` only after R3 confirms a BFF admitting statuscode is live. It runs `-Delete` only after a dev BFF containing its Finance readers (`e0df77339`) is live; the script requires R3's deploy attestation. **From the delete on, every dev BFF deploy first checks by CONTENT (not ancestry: PR #1911 may be squash-merged): in the candidate, `Services/Finance/FinanceBaseValues.cs`, `FinanceRollupService.cs` and `InvoiceReviewService.cs` contain no `sprk_invoicestatus` (`git show <sha>:<path> | grep -c sprk_invoicestatus` = 0), and FinanceBaseValues contains `InvoiceStatusCodeColumn`.** Without that commit, the BFF reads and writes the deleted column, which breaks invoice create, finance rollup, recent invoices, the finance AI tool and the external invoices widget. After the delete, remove `sprk_invoicestatus` from the external ReadableColumns and its test arrays. **Deploy order:** the R3 SPA from `a6513cd56` on reads only `statuscode`, and BFF `9721819ed` admits only `sprk_invoicestatus`. So deploy the BFF before the SPA, never deploy the SPA if the BFF deploy failed or was rolled back, and roll back the SPA (to `88697cfc1`) BEFORE the BFF. **Merge note (UAC):** when ebilling 015 merges into R3, the ExternalAccessModule invoice block conflicts. Keep ebilling's retirement-marker line; its ArchTest exempts only that line.
- **036 live gate: app setting `ExternalAccess__ImpersonatedRootSets__Enabled=true` on spaarke-bff-dev, done by R3.** UAC relayed the owner's approval 2026-10-18. A relay is not an approval, so confirm directly with the owner before the change. Preconditions: #2078 (036) merged to master, and a dev BFF build containing it deployed. UAC names the commit to check. Before changing it, show the owner the exact `az webapp config appsettings set` command and the rollback (delete the setting, or set it to `false`; the app restarts).
- **UAC 036 (branch feat/uac-036-fr20-flag-gated-swap, cut from R3 `5b2375f6f`): flag-gated impersonated root sets + CallerRecordAccessProbe on every external write route.** Merge order agreed 2026-10-17: R3 interim PR → #2061 (186) → 036. **Before writing any BFF change to the external write routes (to-do create, upload, event create; ExternalProjectDataEndpoints.cs) or ExternalAccessModule.cs / AccessibleRecordSetService.cs, tell UAC first.** UAC resolves the ExternalProjectDataEndpoints.cs conflict on its branch.
- **UAC 186 (draft PR #2061): workforce ceiling.** R3 reviewed the diff 2026-10-17: two `.ExemptFromWorkforceCeiling` lines on the 047/048 routes, OK, with a wording fix asked for on registration. Once 186 is on master, every new R3 `/api/v1/external` route declares exactly one of a requirement, a handler check or an exemption (`Api/ExternalAccess/WorkforceCeilingEndpointFilter.cs`). Otherwise the coverage ArchTest fails, and the route is denied to workforce contacts at runtime. For UAT afterwards, the owner adds the missing Workforce-role privileges on dev (all four on sprk_servicerequest).

- UAC task 185 = PR #1827 (head `df4ce6825`, one fix pending). Changes `ContactBindingDecision`, `ContactIdentityStore` (`BindOidAndConsumeInviteAsync`), `ContactIdentityBinder`, `CallerPrincipalResolver` (CIAM invite-code header), `CorsModule`; SPA `invite-code.ts`, `bff-client.ts`, `BffAccessState`, `WorkspaceHomePage`, `App.tsx`. Merge master into R3 when it lands, BEFORE 046/047/058 start. UAC will notify.
- Provisioning PR #1823 (`ef24853ec`) adds `sprk_decisionrecord` / `sprk_decisionrecords` / `sprk_regardingservicerequest` to `ExternalDataService.ServiceRequestRelations` (SpaarkeMaster 1.2.2.0 1:N `sprk_decisionrecord_regsvcreq`; pinned test counts 8 -> 9). Merge master into R3 after #1823 merges; re-run `ExternalDeleteServiceRequestTests` and the relationship pin test.
- ISS-001 (#1623), ISS-002 (#1633), ISS-003 (#1636), ISS-004, ISS-005 (#1673).
- Copilot agent still uses `access_as_user` on `1e40baad`.
- Operator: `mailbox-central@spaarke.com` + Exchange RBAC scoping for the stamp UAMI (H14a); BFF app user needs `prvDelete` on `sprk_servicerequest` (verify live).
