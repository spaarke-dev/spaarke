# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-19 (task 076 done)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **076 done 2026-10-19** (Teams package for A9: the deploy-teams-app.yml preflight fails closed before the build (home client required, a GUID, ≠ A8; prod TEAMS_APP_ID); `match()` is now a whole-string `[[ =~ ]]` in BOTH SPA workflows (grep matched line by line); CI checks the SPA host is in validDomains and the tabs are https; env files and comments say A9; manifest unchanged (no client in a manifest without webApplicationInfo). Stale 075 text fixed (config.ts error message, docs, design §8.2/§12). 059 POML gained `/auth-callback.html` and step 7b `VITE_BACKENDS` with `homeTenantIds` + `ciamBffAppId`. 2 adversarial passes + fix round 1. Provisioning sent the items 5/7 confirmation (+ `/auth-callback.html` addition)). 075 done earlier the same day |
| **Step** | Next: task 059 (live, owner-gated, prescriptive): write the plan with masked reads, commands and rollbacks into `notes/task-059-dev-setup-record.md` and show it to the owner BEFORE any live call. Watch for UAC #2102/#2104 merges (next dev BFF deploy) |
| **Status** | **Dev BFF = `44f802a1d`** (= candidate `f111de2b7` + a state-file commit: live `ad0e9e1a7` + master `d7eabba3b` UAC 186 #2061 + 099b #2063). Deployed with the owner's direct approval 2026-10-19 11:35:09–11:37:07Z. Verified: ProductVersion `1.0.0+44f802a1d…`; 4 critical files SHA-256 match; `/healthz` 200; 2 CORS origins; MIs `mi-bff-api-dev` + `mi-ontology-writer-dev`. Session OKs: UAC, ontology, ebilling (no `sprk_invoicestatus` delete yet), word-add-in, provisioning; field-mapping unreachable. After-notices sent. **Rollback: redeploy `ad0e9e1a7`.** Dev external SPA = `ebc74b331`, not redeployed (058's SPA waits for 059) |
| **Next Action** | Start task 059 via task-execute (step 1 = the plan for the owner; nothing live without approval). Its steps 7/7b set external-spa-dev `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID` and `VITE_BACKENDS` (`homeTenantIds`, `ciamBffAppId`); then the SPA deploy (058 + 075 SPA code; dev SPA is still `ebc74b331`), then 074 |

### Uncommitted files
- `.claude/agent-memory/researcher/MEMORY.md` (modified) and `.claude/agent-memory/researcher/dataverse-duplicate-detection-api-default-2026-10-15.md` (untracked) belong to the OWNER. Never commit or discard them. The 2026-10-18 master merge re-applied the owner's index line next to master's new line. Backups are in the session scratchpad (`owner-researcher-memory.patch`, `owner-researcher-MEMORY.md.bak`).

### Critical Context
- Branch `work/spaarke-SPA-external-access-platform-r3` = master `1473b34c6` + R3 state commits. Interim PR **#2086** merged 2026-10-18 03:58:39Z as `a82578cdc`. Route ledger `ExpectedEndpointFileCount` = 132.
- **Deploy rule** (memory `shared-dev-bff-deploy-coordination`): re-read the live build, run the ancestor check, get every reachable session's OK, and get fresh DIRECT owner approval. Sessions that don't receive the check (SPE-admin-r2, assistant-enhancements, project-coordination hold messages) don't block when there are no conflicts.
- **Parallel pattern:** BFF tasks run in isolated worktrees under `C:\code_files\spaarke\.claude\worktrees\agent-*`; the main session merges `--no-ff`. Agents never edit TASK-INDEX, this file, POML status or `.claude/`.
- **Review policy:** auth/security/tenant-isolation changes get two top-tier passes; other FULL tasks one. Max 4 agents.
- **Test runs:** use filtered runs and absolute csproj paths. Full-suite host tests time out under load (2–4 min each), so re-run failures in isolation. Rebuild the test project before trusting results (CS0016/MSB3030 races).

## Next actions (in order)
1. Read UAC's backfill + 186/099b live-gate results for `44f802a1d`; on a failure, decide on rollback (`ad0e9e1a7`) with the owner.
2. Partner UAT passed (2026-10-18). UI changes are a new round (not R3 scope unless the owner says otherwise).
4. 059 (live, owner-gated) → SPA deploy → 074 (live probes) → 070/071/072 (Teams package, live check, catalog); the final R3 PR. Teams package publishing waits for the SPA deploy (task-076 notes K2).
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
- **Next-after-`f111de2b7` dev BFF deploy:** include UAC #2102 (#2084, master `947d98e31`, matter-scoped rules in the secure family).
  - Order: BFF before or with the Console. The RuleWizard maps `ontology.admin.rule_held_secure` and `ontology.admin.owner_unavailable`; an older Console shows the detail text, so there is no regression either way.
  - Dev prerequisite: SRO Read on `sprk_policy`/`sprk_policyversion`, pending the owner's approval in UAC.
- **UAC #2103** (revoke on a Secure root for a contact with no email always returns 500 `container_not_cleared`; SPE membership is keyed by email):
  - UAC added the SPA's `POST /api/v1/external/contact-grants/revoke` (`IssuedGrantsList` → `revokeContactGrant`) to the scope.
  - Any reason-code or status change on either route comes to R3, with its consumers, before merge. Review it against `IssuedGrantsList.tsx` failure handling.
- **036 live gate:** (UAC relayed 2026-10-18 that the owner tasks R3 with this flip; still needs the owner's DIRECT confirmation here, plus UAC's merge sha.) set `ExternalAccess__ImpersonatedRootSets__Enabled=true` on spaarke-bff-dev, done by R3, ONLY after #2078 is merged and deployed to dev, and after the owner confirms DIRECTLY in this session. UAC's relay doesn't count. Show the exact `az webapp config appsettings set` command first. Rollback: delete the setting, or set it to `false`.
- **Ontology:** wants before/after restart notices; BFF before Console bundle for 102b.
- Older items (verify before relying): Copilot agent still on `access_as_user` (`1e40baad`); operator mailbox/Exchange RBAC (H14a); BFF app user `prvDelete` on `sprk_servicerequest`; ISS-001…ISS-005; DEF-001 #1736, DEF-002 #1737, DEF-003 #1828.
