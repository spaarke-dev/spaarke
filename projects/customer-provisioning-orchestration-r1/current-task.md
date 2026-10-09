# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2 "Binding rules", §3 "Owner directives", §6 "Gotchas". Decisions → `notes/decisions.md`. Session narrative → commit messages. History: git + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-10-09 SESSION 45 end (context-handoff before /compact). Two parallel waves done and on the branch; **PR #1502 open, every check green (44 pass / 3 skip, CLEAN) — waiting for the owner's OK to merge.** Board: 214 of 228 tasks ✅.

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **Merge PR #1502** (owner OK needed) → owner-blocked items → **T186** (first live E2E on a NEW environment). |
| **Status** | waiting on the owner. No agents or background jobs running. |
| **Next Action** | (1) On owner OK: `gh pr checks 1502` — every check terminal + green (CI head `0e14cc113`); master was 1 commit ahead, trial merge clean → `gh pr merge 1502 --merge`; verify master: grep BFF `.ForApp(` = 31, `dotnet test tests/Spaarke.ArchTests` (build it — ArchTests is not in Spaarke.sln). (2) Push the local checkpoint commit(s) (see Branch). (3) Work the owner decisions below as they arrive. |
| **Branch** | `work/customer-provisioning-orchestration-r1`. Remote head `0e14cc113` (= PR #1502 head). **This checkpoint is committed LOCALLY only — not pushed, so PR CI is not restarted. Push it after #1502 merges** (or with the next change). |
| **Main repo** | `C:\code_files\spaarke` master not fast-forwarded: another session's uncommitted researcher-memory edits block it. Leave them; the owner decides. |

## ✅ Completed this session (all on the branch, verified)

- **T253** the L2 Worker runs no shell tool: H4b via ARM SDK (both slots, merge, parity test); H6 org settings via Dataverse Web API, no application installer (PRQ-C-07 retired); H12a/H12b embedded manifests; ArchTest bans `Process.Start` in L2.
- **T256** H7b Secure Record setup (unit, owner team, Secure Record Owner role in the unit; `sprk_noaccessentry` gate before H9; dry run `secureRecordSetupDryRun`) **+ identity-link profile memberships** (Readers ← default teams; Writers ← H10's two BFF app users).
- **T258** stamp BFF startup settings (PublicConfig:*, Graph:Scopes, ServiceBus:QueueName via H4b; Onboarding consent callback gated off → 404).
- **T255 (code)** customer workforce tenant list for every model + H3 `acct` claim; new required L2 setting `ReservedTenants__*` / Bicep `ciamTenantIds`; package rule now includes alternate keys on standard tables.
- **T206/T207** every prereq recipe exits 1 on failure; skill Step 0.5b `Invoke-PrereqPass`; **new Step 1e-ter** runs the 18 `once_per_customer` checks; PRQ-T-05/T-06 retired.
- **T208** prereqs validator gates merges via router job `prereqs` (standalone workflow deleted). **T209** done (ruleset 21824191). **T250** superseded (master `bb8ba7251`). **204e** ADR-032 + IOptions-drift ArchTests. Punch list 203b/204a/204f/204g closed.
- Fixes: H9 dead Deploy-Release scan removed; CA2024; bicep dry-run uses the dev .bicepparam; tier2 Markdown timeout 2→5 min.
- Branch verification: build 0 errors / 0 warnings; ControlPlane 2532/2533; ArchTests 889; LoadTests 5/5; recipe Pester 39/39; catalog `-Verify` OK; validate.ps1 OK (41 prereqs); BFF publish 38,110,809 B (+218 B vs master).
- Design notes written for owner review: `notes/t257-copilot-agent-design.md`, `notes/t240d-ciam-external-contacts-design.md`.

## 🔔 Owner decisions / live actions waiting (each needs an explicit OK)

1. **Merge PR #1502.**
2. **SpaarkeMaster re-export** with the contact alternate key `sprk_ExternalObjectIdUniqueKey` (Assemble → Export from spaarkedev1, then CI publish `publish-dataverse-solutions-manifest.yml` publish=true). Unblocks T255's last item; then remove the `KeyAwaitingExport` pin in `ContactIdentityBindingSchemaPackagedTests`. Without it every external-contact create fails on a new stamp.
3. **ISS-010 / #1486** (T186 blocker): guests in the root BU read every secure record. Decide the customer business unit placement (recommend H10 creates the unit + app users in it; H11 creates guests in it; H13 runs the isolation census) + one intake value (customer display name). Then build it.
4. **ISS-008 / #1484**: run guard off — make the L2 UAMI an Application User on the admin environment (`Grant-ControlPlaneIdentity.ps1`), set `customerRunGuardEnabled=true` in the dev bicepparam, redeploy.
5. **Control-plane redeploy** with `ReservedTenants__*` (`ciamTenantIds` in the bicepparam) — `Deploy-ControlPlane.ps1` refuses the new code without it.
6. **T240d** design note review (+ spike S1 approval, CIAM-tenant one-time actions, `User.Create` sign-off).
7. **T257** design note review (7 questions, recommended answers in §4).
8. **T240b** owner re-test (add-in 1.1.2, Dewey Cheatham guest; Diagnostics shows `acct` 1) → unblocks **T240c**.
9. **T242c** demo BFF refresh and **T252** control-plane vault sentinels — live, OK per action.
10. Dewey Cheatham's tenant id (T186 intake `customerWorkforceTenantIds`); the SPE container type id for the T186 stamp.
11. Still open from before: G36 (management group), G31 (H10 tenant-wide roles) — do NOT act; board Status/Reason mismatch on #438.

## 📋 Remaining tasks (TASK-INDEX)

- **Open, owner-gated:** 240b, 240c, 240d, 257, 242c, 252, 255 (export only).
- **Blocked:** 186 (first live E2E — needs items 2–5 + 10 above and ISS-010 built), 162, 089, 212, 213 (historical gates folded into 186).
- **Post-186:** 203d. **Wrap-up:** 090 (`/test-diet`, lessons learned, archive).
- **To build after decisions:** ISS-010 customer BU (new task), T240c directory endpoint, T240d per the accepted design, T257 per the accepted design.

## 🐞 Filed defects (open)

#1484 ISS-008 run guard · #1485 ISS-009 E11 bind-by-email (UAC-r2; before CIAM on stamps) · #1486 ISS-010 guests/secure records (before T186) · #1487 ISS-011 BFF gate asymmetries (latent) · #1499 ISS-012 Graph:Scopes unread (owner: drop validator?) · #1500 ISS-013 consent callback (Model 2) · #1504 office server tests near 20-min cap (word-add-in-r1) · #1432 ISS-006 RAG Dedicated · #1376 ISS-003 · #1377 ISS-004.

## Cross-project

- **UAC-r2**: #1364 confirmation (H7b implements the check); E11 fix (#1485); open question — Model 1 sign-in authority: if clients sign customer staff in through Spaarke's tenant, `tid` = Spaarke and the T255 member test denies them (coordinate with external-access-r3). `Set-ContactIdentityBindingSchema.ps1` still defaults to `SpaarkeCore` (theirs).
- **word-add-in-r1**: first prod deploy waits for T240c; #1504 is theirs.
- **external-access-r3**: T240d design (link carries only the customer key; no BFF URL in links); Teams client app id.

## T186 (first live E2E) — plan

- Mock customer homed in Dewey Cheatham (paid M365 Business); stamp in Spaarke's tenant (own subscription, Dataverse env `spaarke-{customerId}`, BFF app reg, `sprk-{customerId}-users`). Run 2 (later) adds a second customer tenant for isolation (ISS-003/004).
- Live checks to watch: H4b ARM write both slots + `/healthz`; H6 org PATCH (`maxuploadfilesize` 25600000) + import with no MissingDependency; H7b dry run then apply; H12a/H12b embedded manifests; stamp BFF starts with no hand-set settings, `GET /api/config` correct, consent callback 404; jobs on `sdap-jobs`; keyless proofs `proved`; H3 `acct` on registration + token; H4b workforce list + stale removal, T7 passes; earlier lists (T228 H5 WhoAmI, T218b async import, T227 PATCHes, T251 group name, T240a client access + CORS).
- H12a seeds 4 of 12 artifacts (playbooks + consumers pending — task 150).

## Live state

- Store `sprkcpartifactsdev/provisioning-artifacts`: SpaarkeMaster **1.2.0.0** published (managed + unmanaged, SHA-256); `latest.previous` = hand-made 2026-08-21 manifest. Demo holds 1.0.0.0 unmanaged.
- Dev control plane (`rg-spaarke-platform-dev`): deployed from `35c20287c` — does NOT yet carry this branch's L2 code (needs item 5).
- Dev BFF `spaarke-bff-dev` (rg-spaarke-dev): hand-set `Graph__Scopes__0`, `ServiceBus__QueueName`, `PublicConfig__*`, `Onboarding__EnableDevBypass`; deploy only from master.
