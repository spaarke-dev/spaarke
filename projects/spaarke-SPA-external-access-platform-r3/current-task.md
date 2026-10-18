# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-18 (context-handoff at 85%: waiting on owner approval for the 102b dev BFF deploy)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | No POML task is active. Next task: **058** (SPA multitenant sign-in, A9), via task-execute. 044–048 and 061–068 are done and on master |
| **Step** | Dev BFF deploy for ontology 102b is **prepared and waiting for the owner's DIRECT approval**. Ontology relayed the owner's approval; a relay is not an approval, so the owner was asked in this session |
| **Status** | Candidate **`20e7b71a1`** = R3 + master `c690b0ac2` (ontology #2088 `20fb86dc4` + #2043); src/config byte-identical to `20fb86dc4`; pushed. ArchTests + BFF build were running in background (task `blps90okx`); re-run if the result is lost. Deploy checks: **OK from ontology, UAC, ebilling, word-add-in, provisioning.** Live dev BFF = `ebc74b331` (an ancestor). Dev external SPA = `ebc74b331` (run `39094144398`); the SPA is NOT redeployed for 102b |
| **Next Action** | When the owner approves in this session: (1) read-only re-read of the live DLL ProductVersion via Kudu VFS; deploy only if it is `ebc74b331` or another ancestor of `20e7b71a1`; (2) `pwsh -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1` (delete `src/server/api/Sprk.Bff.Api/publish` first); (3) verify the live ProductVersion, MIs `mi-bff-api-dev` + `mi-ontology-writer-dev`, `/healthz` 200; (4) after-notice with ProductVersion + finish time to ontology (it checks authoring routes + nightly sweep), UAC (#2083 smoke), word-add-in; (5) record here. Rollback: redeploy `ebc74b331` |

### Uncommitted files
- `.claude/agent-memory/researcher/MEMORY.md` (modified) and `.claude/agent-memory/researcher/dataverse-duplicate-detection-api-default-2026-10-15.md` (untracked) belong to the OWNER. Never commit or discard them. The 2026-10-18 master merge re-applied the owner's index line next to master's new line. Backups are in the session scratchpad (`owner-researcher-memory.patch`, `owner-researcher-MEMORY.md.bak`).

### Critical Context
- Branch `work/spaarke-SPA-external-access-platform-r3` = master `c690b0ac2` + R3 state commits. Interim PR **#2086** merged 2026-10-18 03:58:39Z as `a82578cdc`. Route ledger `ExpectedEndpointFileCount` = 132.
- **Deploy rule** (memory `shared-dev-bff-deploy-coordination`): re-read the live build, run the ancestor check, get every reachable session's OK, and get fresh DIRECT owner approval. Sessions that don't receive the check (SPE-admin-r2, assistant-enhancements, project-coordination hold messages) don't block when there are no conflicts.
- **Parallel pattern:** BFF tasks run in isolated worktrees under `C:\code_files\spaarke\.claude\worktrees\agent-*`; the main session merges `--no-ff`. Agents never edit TASK-INDEX, this file, POML status or `.claude/`.
- **Review policy:** auth/security/tenant-isolation changes get two top-tier passes; other FULL tasks one. Max 4 agents.
- **Test runs:** use filtered runs and absolute csproj paths. Full-suite host tests time out under load (2–4 min each), so re-run failures in isolation. Rebuild the test project before trusting results (CS0016/MSB3030 races).

## Next actions (in order)
1. Ontology 102b dev BFF deploy, once the owner approves directly (above).
2. Owner continues Partner UAT (checklist given 2026-10-17). Earlier results: sign-in works; the blank invoice Status is fixed by statuscode.
3. **Register R3 on the portfolio board** before task 058: README has no `> **Portfolio**:` block, and the task-execute registration guard stops without it. Ask the owner for the Epic number.
4. Task 058 → 075 → 076 → 074 (074 and 059 are live and owner-gated); 070/071/072; the final R3 PR.
5. Close DEF-004 (#1829) as superseded when ebilling 051 merges (successor answers posted on the issue).

## Open owner questions
1. Direct approval for the 102b dev BFF deploy of `20e7b71a1`.
2. The Epic number for registering R3 on the portfolio board.
3. `/upload` has no in-app link (known limit, task 042 notes): add an entry point, or retire the route?
4. Owner deletes probe message `b38f0ab7-03c7-f111-a05c-0022482913fc` by hand.
5. Owner test for 067: create a service request; expect an RQST number other than RQST-0001.
6. ISS-010 #1864 (Internal Only not enforced by Dataverse security for licensed users flagged external): owner/UAC decision.

## Standing dependencies and gates
- **ebilling 015: `sprk_invoicestatus` retirement.** Grid 3ff4102c is on `statuscode` (2026-10-18 03:42Z). The external allow-list admits both columns (transitional). Ebilling's `-Delete` waits until 015 reaches master and R3 deploys a BFF containing it. **From the delete on, every dev BFF candidate must pass a CONTENT check** (not ancestry, because PR #1911 may be squash-merged): `Services/Finance/FinanceBaseValues.cs`, `FinanceRollupService.cs` and `InvoiceReviewService.cs` contain no `sprk_invoicestatus`, and FinanceBaseValues contains `InvoiceStatusCodeColumn`. `20e7b71a1` does NOT pass this (FinanceBaseValues 3 refs, InvoiceReviewService 1), so it is not a delete attestation. Afterwards, remove `sprk_invoicestatus` from the ReadableColumns and both test arrays. **Merge note:** keep ebilling's retirement-marker line in the ExternalAccessModule invoice block. **Rollback order:** SPA before BFF; never deploy the SPA after a failed or rolled-back BFF.
- **ebilling-r1 merge (later):** its BFF reads `sprk_policyversion.sprk_rejectionreason` (D-113) and the D-104 Pending Spend column. Their schema scripts must run on dev before deploying any BFF built from that master; ebilling notifies.
- **UAC order:** #2061 (186) → #2078 (036). 186 waits on the owner's Workforce role edit; 036 is held on an owner ADR decision. After 186: every new `/api/v1/external` route declares a ceiling requirement, handler check or exemption (`WorkforceCeilingEndpointFilter.cs`). **Tell UAC before any BFF change to the external write routes, ExternalAccessModule.cs or AccessibleRecordSetService.cs.**
- **036 live gate:** set `ExternalAccess__ImpersonatedRootSets__Enabled=true` on spaarke-bff-dev, done by R3, ONLY after #2078 is merged and deployed to dev, and after the owner confirms DIRECTLY in this session. UAC's relay doesn't count. Show the exact `az webapp config appsettings set` command first. Rollback: delete the setting, or set it to `false`.
- **Ontology:** wants before/after restart notices; BFF before Console bundle for 102b.
- Older items (verify before relying): Copilot agent still on `access_as_user` (`1e40baad`); operator mailbox/Exchange RBAC (H14a); BFF app user `prvDelete` on `sprk_servicerequest`; ISS-001…ISS-005; DEF-001 #1736, DEF-002 #1737, DEF-003 #1828.
