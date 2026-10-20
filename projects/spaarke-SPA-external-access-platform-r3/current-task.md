# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-19 21:20Z (059 stage B done: 1e40baad on v2 tokens; per-surface browser check and stage C approval next, owner-gated)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **077 done 2026-10-19**: the default scheme also accepts aud = the bare BFF app id (v2 tokens; separate PostConfigure, GUID ClientId only) and refuses ID tokens for the app (aud = bare id without azpacr/appidacr; falls back to principal claims). 2 adversarial passes (F4 ID token, F3 link harness fixed) + scoped re-check (K hardening). Commits f68d0f312, 77d74f305, 283466d40. Notes `notes/task-077-notes.md`. 076/075 done earlier the same day |
| **Step** | **059 in progress**: R done; owner S-1/S-2 decided; 077 deployed (`283466d40`). **Stage A DONE** ~21:00Z: "Spaarke Workforce SPA" `5859e812-e5e7-452c-be21-1e5aa7b4d478` (obj `e97e41bf…`, SP `afe6ad3c…`), 5 SPA redirects incl. brk-multihub (Graph accepted it), 1 scope (1e40baad user_impersonation), 0 secrets/certs/FICs, removeUnverifiedEmailClaim true. Rollback: `az ad app delete --id 5859e812…`. **Stage B DONE 21:13:14Z** (owner "059 stage B is approved"): 1e40baad `requestedAccessTokenVersion` 2, known += 5859e812, accessToken claims += given_name/family_name, removeUnverifiedEmailClaim true; read-after matches. Session smoke: v2 token (bare aud, v2 iss, azpacr, email present) → `/api/me` 200; cached v1 → 200; no `sdap.auth.deny.*` to 21:15:44Z. Before-snapshot scratchpad `t059/before-stageB.json`. Record §11 |
| **Status** | **Dev BFF = `00aba6d99`** (= `283466d40` [077 + UAC #2102 + ontology 042b] + master `d301c0675` UAC #2104). Deployed with the owner's direct approval 21:09:27–21:11:34Z; verified (ProductVersion, hashes, healthz, `/api/me` 200, anon 401). **Rollback: `Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261019T210920Z` (= 283466d40). NEVER roll back below 077 while 1e40baad is on v2, or within 90 min of reverting it (059 §5/§10).** Dev external SPA = `ebc74b331` |
| **Next Action** | 1) Send stage B after-notices (UAC, ontology, word-add-in, ebilling, provisioning). 2) Owner: fresh-session browser sign-ins (Console, a PCF, Office add-in, Teams/A8 SPA, Copilot if used); then re-run the App Insights deny/401 query from 21:13:14Z. 3) Ask the owner to approve stage C (admin consent URL for Dewey Cheatham `bc3aa7f4`, client 5859e812), then D, E, F |

### Uncommitted files
- `.claude/agent-memory/researcher/MEMORY.md` (modified) and `.claude/agent-memory/researcher/dataverse-duplicate-detection-api-default-2026-10-15.md` (untracked) belong to the OWNER. Never commit or discard them. The 2026-10-18 master merge re-applied the owner's index line next to master's new line. Backups are in the session scratchpad (`owner-researcher-memory.patch`, `owner-researcher-MEMORY.md.bak`).

### Critical Context
- Branch `work/spaarke-SPA-external-access-platform-r3` = master `1473b34c6` + R3 state commits. Interim PR **#2086** merged 2026-10-18 03:58:39Z as `a82578cdc`. Route ledger `ExpectedEndpointFileCount` = 132.
- **Deploy rule** (memory `shared-dev-bff-deploy-coordination`): re-read the live build, run the ancestor check, get every reachable session's OK, and get fresh DIRECT owner approval. Sessions that don't receive the check (SPE-admin-r2, assistant-enhancements, project-coordination hold messages) don't block when there are no conflicts.
- **Parallel pattern:** BFF tasks run in isolated worktrees under `C:\code_files\spaarke\.claude\worktrees\agent-*`; the main session merges `--no-ff`. Agents never edit TASK-INDEX, this file, POML status or `.claude/`.
- **Review policy:** auth/security/tenant-isolation changes get two top-tier passes; other FULL tasks one. Max 4 agents.
- **Test runs:** use filtered runs and absolute csproj paths. Full-suite host tests time out under load (2–4 min each), so re-run failures in isolation. Rebuild the test project before trusting results (CS0016/MSB3030 races).

## Next actions (in order)
1. Watch for 401s/`sdap.auth.deny.id_token_on_workforce_scheme` reports from sessions after the 283466d40 deploy; ontology smokes 042b, UAC runs the 186 gate.
2. Partner UAT passed (2026-10-18). UI changes are a new round (not R3 scope unless the owner says otherwise).
4. 059 (live, owner-gated) → SPA deploy → 074 (live probes) → 070/071/072 (Teams package, live check, catalog); the final R3 PR. Teams package publishing waits for the SPA deploy (task-076 notes K2).
5. Close DEF-004 (#1829) as superseded when ebilling 051 merges (successor answers posted on the issue).

## Open owner questions
0. Observation for the owner: `spaarke-bff-api-prod` has tenant-wide consent (`SDAP.Access`) to the DEV BFF app 1e40baad: intended? (059 R3)
1. (none: the 102b deploy was approved and done)
2. (none: R3 registered under Epic #2099)
3. `/upload` has no in-app link (known limit, task 042 notes): add an entry point, or retire the route? (explained to the owner 2026-10-19; awaiting choice)
4. Probe message `b38f0ab7-03c7-f111-a05c-0022482913fc` (sprk_communication, body "[R3 probe 038] safe to delete"): owner said "yes can delete" 2026-10-19. The session runs the read-then-delete only on the owner's "go" to the exact commands it showed.
5. 067: owner reports 2026-10-19 that the number is created automatically (closed).
6. ISS-010 #1864 (Internal Only not enforced by Dataverse security for licensed users flagged external): explained to the owner 2026-10-19; owner/UAC decision.
7. 059 stage C approved 2026-10-19: owner opens the consent URL (record §11), then reports the read-after. Browser sign-in checks pending.

## Standing dependencies and gates
- **ebilling 015: `sprk_invoicestatus` retirement.** Grid 3ff4102c is on `statuscode` (2026-10-18 03:42Z). The external allow-list admits both columns (transitional). Ebilling's `-Delete` waits until 015 reaches master and R3 deploys a BFF containing it. **From the delete on, every dev BFF candidate must pass a CONTENT check** (not ancestry, because PR #1911 may be squash-merged): `Services/Finance/FinanceBaseValues.cs`, `FinanceRollupService.cs` and `InvoiceReviewService.cs` contain no `sprk_invoicestatus`, and FinanceBaseValues contains `InvoiceStatusCodeColumn`. `20e7b71a1` and the live `ad0e9e1a7` do NOT pass this (FinanceBaseValues 3 refs, InvoiceReviewService 1), so neither is a delete attestation. Afterwards, remove `sprk_invoicestatus` from the ReadableColumns and both test arrays. **Merge note:** keep ebilling's retirement-marker line in the ExternalAccessModule invoice block. **Rollback order:** SPA before BFF; never deploy the SPA after a failed or rolled-back BFF.
- **ebilling-r1 merge (later):** its BFF reads `sprk_policyversion.sprk_rejectionreason` (D-113) and the D-104 Pending Spend column. Their schema scripts must run on dev before deploying any BFF built from that master; ebilling notifies.
- **UAC order:** #2061 (186) → #2078 (036). 186 waits on the owner's Workforce role edit; 036 is held on an owner ADR decision. After 186: every new `/api/v1/external` route declares a ceiling requirement, handler check or exemption (`WorkforceCeilingEndpointFilter.cs`). **Tell UAC before any BFF change to the external write routes, ExternalAccessModule.cs or AccessibleRecordSetService.cs.**
- **Next dev BFF deploy (needs fresh owner approval):** UAC #2110 (thread re-file; merged as `2b07480ab`); ontology 042c (#2113) after its sync. (#2104 is already live in `00aba6d99`.) **Every candidate must contain 077 (`283466d40`): 1e40baad is on v2 tokens since 21:13:14Z.**
- **UAC #2103** (revoke on a Secure root for a contact with no email always returns 500 `container_not_cleared`; SPE membership is keyed by email):
  - UAC added the SPA's `POST /api/v1/external/contact-grants/revoke` (`IssuedGrantsList` → `revokeContactGrant`) to the scope.
  - Any reason-code or status change on either route comes to R3, with its consumers, before merge. Review it against `IssuedGrantsList.tsx` failure handling.
- **036 live gate:** (UAC relayed 2026-10-18 that the owner tasks R3 with this flip; still needs the owner's DIRECT confirmation here, plus UAC's merge sha.) set `ExternalAccess__ImpersonatedRootSets__Enabled=true` on spaarke-bff-dev, done by R3, ONLY after #2078 is merged and deployed to dev, and after the owner confirms DIRECTLY in this session. UAC's relay doesn't count. Show the exact `az webapp config appsettings set` command first. Rollback: delete the setting, or set it to `false`.
- **Ontology:** wants before/after restart notices; BFF before Console bundle for 102b.
- Older items (verify before relying): Copilot agent still on `access_as_user` (`1e40baad`); operator mailbox/Exchange RBAC (H14a); BFF app user `prvDelete` on `sprk_servicerequest`; ISS-001…ISS-005; DEF-001 #1736, DEF-002 #1737, DEF-003 #1828.
