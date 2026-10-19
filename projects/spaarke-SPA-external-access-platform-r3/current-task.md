# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-18 22:32Z (after the owner-approved dev BFF deploy of `ad0e9e1a7`)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **058 done 2026-10-19** (SPA multitenant sign-in, profile form, link). 2 adversarial passes; fix round 1 closed the no-account `clearCache()` session wipe (real-MSAL regression test). Final: vitest 1072/1072, typecheck, lint, build, check:dist clean. Next task **075** (Teams → multitenant NAA; carry-over recorded in its POML notes) |
| **Step** | Before 075: merge `origin/master` (UAC 186 `49279def1` + 099b `d7eabba3b`), run targeted tests + ArchTests, then the deploy check round and the owner's direct approval for the dev BFF deploy (gate below) |
| **Status** | **Dev BFF = `ad0e9e1a7`** (R3 + master `1473b34c6`: ontology 102b #2088 + UAC #2094), deployed with the owner's direct approval, finished 2026-10-18 22:30:58Z. Pre-deploy live was `ebc74b331` (an ancestor). Verified: ProductVersion `1.0.0+ad0e9e1a7…`, 4 critical files SHA-256 match, `/healthz` 200, 2 CORS origins, MIs `mi-bff-api-dev` + `mi-ontology-writer-dev`. Tests at this head: targeted 1064/1064, ArchTests 1130/1130. After-notices sent to ontology (authoring routes + nightly sweep), UAC (#2094 live gate + #2083 smoke) and word-add-in. Rollback: redeploy `ebc74b331`. Dev external SPA = `ebc74b331` (run `39094144398`), not redeployed. **Owner Partner UAT passed 2026-10-18; UI changes go to a new round** |
| **Next Action** | Merge `origin/master` into the branch, keeping the owner's researcher MEMORY.md (save the patch, check it out, merge, then `apply -3`). 058's SPA is not deployed; the SPA deploy waits for 059 (multitenant client redirect URIs, plus GitHub env `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID` and `VITE_BACKENDS` with `homeTenantIds`; the workflow now fails closed without them) |

### Uncommitted files
- `.claude/agent-memory/researcher/MEMORY.md` (modified) and `.claude/agent-memory/researcher/dataverse-duplicate-detection-api-default-2026-10-15.md` (untracked) belong to the OWNER. Never commit or discard them. The 2026-10-18 master merge re-applied the owner's index line next to master's new line. Backups are in the session scratchpad (`owner-researcher-memory.patch`, `owner-researcher-MEMORY.md.bak`).

### Critical Context
- Branch `work/spaarke-SPA-external-access-platform-r3` = master `1473b34c6` + R3 state commits. Interim PR **#2086** merged 2026-10-18 03:58:39Z as `a82578cdc`. Route ledger `ExpectedEndpointFileCount` = 132.
- **Deploy rule** (memory `shared-dev-bff-deploy-coordination`): re-read the live build, run the ancestor check, get every reachable session's OK, and get fresh DIRECT owner approval. Sessions that don't receive the check (SPE-admin-r2, assistant-enhancements, project-coordination hold messages) don't block when there are no conflicts.
- **Parallel pattern:** BFF tasks run in isolated worktrees under `C:\code_files\spaarke\.claude\worktrees\agent-*`; the main session merges `--no-ff`. Agents never edit TASK-INDEX, this file, POML status or `.claude/`.
- **Review policy:** auth/security/tenant-isolation changes get two top-tier passes; other FULL tasks one. Max 4 agents.
- **Test runs:** use filtered runs and absolute csproj paths. Full-suite host tests time out under load (2–4 min each), so re-run failures in isolation. Rebuild the test project before trusting results (CS0016/MSB3030 races).

## Next actions (in order)
1. Read ontology's and UAC's post-deploy check results for `ad0e9e1a7`; if either fails, decide on rollback with the owner.
2. Partner UAT passed (2026-10-18). UI changes are a new round (not R3 scope unless the owner says otherwise).
4. 075 → 076 → 074 (074 and 059 are live and owner-gated); 070/071/072; the final R3 PR.
5. Close DEF-004 (#1829) as superseded when ebilling 051 merges (successor answers posted on the issue).

## Open owner questions
1. (none: the 102b deploy was approved and done)
2. (none: R3 registered under Epic #2099)
3. `/upload` has no in-app link (known limit, task 042 notes): add an entry point, or retire the route?
4. Owner deletes probe message `b38f0ab7-03c7-f111-a05c-0022482913fc` by hand.
5. Owner test for 067: create a service request; expect an RQST number other than RQST-0001.
6. ISS-010 #1864 (Internal Only not enforced by Dataverse security for licensed users flagged external): owner/UAC decision.

## Standing dependencies and gates
- **ebilling 015: `sprk_invoicestatus` retirement.** Grid 3ff4102c is on `statuscode` (2026-10-18 03:42Z). The external allow-list admits both columns (transitional). Ebilling's `-Delete` waits until 015 reaches master and R3 deploys a BFF containing it. **From the delete on, every dev BFF candidate must pass a CONTENT check** (not ancestry, because PR #1911 may be squash-merged): `Services/Finance/FinanceBaseValues.cs`, `FinanceRollupService.cs` and `InvoiceReviewService.cs` contain no `sprk_invoicestatus`, and FinanceBaseValues contains `InvoiceStatusCodeColumn`. `20e7b71a1` and the live `ad0e9e1a7` do NOT pass this (FinanceBaseValues 3 refs, InvoiceReviewService 1), so neither is a delete attestation. Afterwards, remove `sprk_invoicestatus` from the ReadableColumns and both test arrays. **Merge note:** keep ebilling's retirement-marker line in the ExternalAccessModule invoice block. **Rollback order:** SPA before BFF; never deploy the SPA after a failed or rolled-back BFF.
- **ebilling-r1 merge (later):** its BFF reads `sprk_policyversion.sprk_rejectionreason` (D-113) and the D-104 Pending Spend column. Their schema scripts must run on dev before deploying any BFF built from that master; ebilling notifies.
- **UAC order:** #2061 (186) → #2078 (036). 186 waits on the owner's Workforce role edit; 036 is held on an owner ADR decision. After 186: every new `/api/v1/external` route declares a ceiling requirement, handler check or exemption (`WorkforceCeilingEndpointFilter.cs`). **Tell UAC before any BFF change to the external write routes, ExternalAccessModule.cs or AccessibleRecordSetService.cs.**
- **Next dev BFF deploy must include UAC 186** (#2061, master `49279def1`, merged 2026-10-18; owner edited the dev Workforce role). Merge master only after the 058 implementer agent finishes (it edits the working tree). 099b (#2063) is also merged (master `d7eabba3b`; union-resolved with 186 in `IDataversePrivilegeChecker`/`UserPrivilegeChecker`) — take both in the same merge. After the deploy: after-notice to UAC, which then runs the record-ownership backfill on dev (-Apply, VERIFY) and the 099b + 186 live gates.
- **036 live gate:** (UAC relayed 2026-10-18 that the owner tasks R3 with this flip; still needs the owner's DIRECT confirmation here, plus UAC's merge sha.) set `ExternalAccess__ImpersonatedRootSets__Enabled=true` on spaarke-bff-dev, done by R3, ONLY after #2078 is merged and deployed to dev, and after the owner confirms DIRECTLY in this session. UAC's relay doesn't count. Show the exact `az webapp config appsettings set` command first. Rollback: delete the setting, or set it to `false`.
- **Ontology:** wants before/after restart notices; BFF before Console bundle for 102b.
- Older items (verify before relying): Copilot agent still on `access_as_user` (`1e40baad`); operator mailbox/Exchange RBAC (H14a); BFF app user `prvDelete` on `sprk_servicerequest`; ISS-001…ISS-005; DEF-001 #1736, DEF-002 #1737, DEF-003 #1828.
