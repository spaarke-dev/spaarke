# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2 "Binding rules", §3 "Owner directives", §6 "Gotchas". Decisions → `notes/model1-dedicated-remediation-plan.md` D-table (D30/D31 = 2026-10-09). Session narrative → commit messages.

> **Last Updated**: 2026-10-09 SESSION 46. Owner answered the 11 decisions (D31). #1502 merged; SpaarkeMaster 1.2.1.0 published; T252, T257, T259, ISS-015 built + merged on the branch (pushed `531119b3d`+). Next: the single control-plane deploy (after its two prerequisites) and the owner's open answers.

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Control-plane redeploy (dev) carrying run guard + ReservedTenants + T252 + ISS-015 + T259 + T257; then T186. |
| **Status** | waiting on the owner for 5 answers (below). No agents or background jobs running. |
| **Next Action** | (1) Open a PR work-branch → master (all lanes merged; verified build 0/0, ControlPlane 2636/2637, ArchTests 889) and merge when EVERY check is green. (2) With owner OK per step: create the "Spaarke Copilot Agent" client app (deployment guide §7.12 step 1); run `scripts/Extend-DataverseEnvironmentSchema-v3.3.ps1` on the admin env (adds `sprk_bffappid`, `sprk_copilotauthconfigid` — MUST precede the deploy, else H13's registry PATCH fails); set `copilotAgentClientAppId` in the dev bicepparam. (3) `scripts/provisioning/Deploy-ControlPlane.ps1` (dev) from master; post-deploy checks from T252 report: Worker KV refs = only `ExchangeSidecar__SharedSecret`; 4 `*__Credentials__*` settings; `/healthz` 200 both apps; `sprk-controlplane-dev-kv` same 7 secrets, unchanged timestamps; `Seed-PlatformKeyVault.ps1 -DryRun` creates none; `CustomerRunGuard__Enabled=True`; prove a 2nd concurrent run for one customer is refused (ISS-008). |
| **Branch** | `work/customer-provisioning-orchestration-r1`, pushed. All lane branches merged (worktree-agent-afb0e56…, -a48e19db…, -ae886473…, -a179c693…). |

## 🔔 Waiting on the owner

1. **ISS-014 / #1527** — OK to add a read-only BFF endpoint `POST /api/platform/secure-record-isolation-census` behind the existing `KeylessProofAuthorizationFilter` (no new role), so H13 requires `isolated`? (new BFF route + auth → needs explicit OK). Until then T186 runs the census by hand after H11.
2. **T240d** — does the "yes" override the standing "never touch `spaarkeextid`" rule for spike S1 + §7 one-time steps (SP provisioning, `User.Create` grant, test local account, CIAM app registrations)? Nothing written there until confirmed. T240d step 2 (code) is gated on S1.
3. **G36** — create a "Spaarke Customers" management group + common policy (tenant-level, owner action), then a small task (constants + PRQ check).
4. **G31** — task to inventory the Graph calls a customer BFF makes and cut H10's tenant-wide directory WRITE grants to that set.
5. **T242c demo** — recommendation: rebuild demo as a provisioned stamp (D27) after T186 rather than hand-refresh (hand-refresh needs a new Content Safety resource; demo stays Stopped). Exact hand-refresh commands are in the POML note's source report if the owner prefers. Open: fate of the unmanaged `spaarke-demo` Dataverse env.
- **T240b** — owner at the keyboard: Business Standard licence for ralph@deweycheatham; word-add-in-r1's diagnostics build installed in Dewey Cheatham (Integrated apps); then Outlook/Word desktop+web sign-in, Diagnostics shows `tid`=Spaarke, `acct`=1. Unblocks T240c.

## ✅ Done this session (SESSION 46)

- **#1502** merged (`b35c0a9c2`). **SpaarkeMaster 1.2.1.0**: Assemble (+contact key) → Export → PR #1518 merged → publish dry run + publish=true (run 37969187349); latest.json = 1.2.1.0. **T255 ✅.** Removed component: WebResource `sprk_corporateworkspace` (retired).
- **ISS-008**: dev bicepparam `customerRunGuardEnabled = true` (UAMI `sprk-controlplane-dev-uami` 965a4a01 already an app user on spaarkedev1 with role "Spaarke Provisioning Registry"). Takes effect at the deploy.
- **T252** (code): control plane secret-free by default (`requireSecretFreeIdentity=true`, dev explicit); Seed script stops seeding the two sentinels. Deploy changes dev's Worker chain from the (broken, sentinel) ClientSecret to `[ManagedIdentityFederated]`.
- **ISS-015 / #1524** (found by T252): H3 now keeps a 2nd FIC `spaarke-l2-worker` (subject = L2 Worker UAMI principalId) on each customer BFF app so H6/H7/H7b can sign in. Rollout: for a stamp whose H3 already ran, resume H3 before H6.
- **T257** (code): Copilot agent template + render script + publish workflow; `copilotAgentClientAppId` Bicep param → H3 pre-authorization; registry columns `sprk_bffappid` (H13 promotes) + `sprk_copilotauthconfigid`; skill 6f gate; scope corrected to `user_impersonation`. Filed ISS-017 / #1523 (unused AgentToken options; BFF owner).
- **T259** (ISS-010, D30): H10 customer BU (intake `displayName`, now REQUIRED at POST /api/runs) + app users in it; H11 moves guests in before roles; H7 links the customer BU to the container; H7b refuses Secure-Record users / app users outside. #1486 closed; census split to ISS-014.
- **T242c** step 1 (read-only) recorded in its POML.

## 📋 Remaining tasks

- **Owner-gated:** 240b, 240c, 240d, 242c, ISS-014 build. **T257 live steps** (client app, schema columns, bicepparam, publish template, per-customer auth config, Dewey live test).
- **T186** (first live E2E, mock customer homed in Dewey Cheatham `bc3aa7f4-3ca3-47e6-84e7-fea35f5c245b`; container type = dev `fb3817a8-…` "Spaarke Model 1", already in constants). Needs: the deploy above; census by hand after H11 (until ISS-014); intake `displayName`.
- **Post-186:** 203d. **Wrap-up:** 090.

## 🐞 Open defects

#1527 ISS-014 census unreachable from H13 · #1484 ISS-008 (closes after the deploy proof) · #1485 ISS-009 E11 bind-by-email (UAC-r2) · #1487 ISS-011 · #1499 ISS-012 · #1500 ISS-013 · #1523 ISS-017 · #1504 office tests near cap (word-add-in-r1) · #1432 ISS-006 · #1376 ISS-003 · #1377 ISS-004.

## Live state

- Store `sprkcpartifactsdev/provisioning-artifacts`: SpaarkeMaster **1.2.1.0** latest (`latest.previous` = 1.2.0.0).
- Dev control plane (`rg-spaarke-platform-dev`): still the old deploy — no T252/T255/T257/T259/ISS-015 code, guard off, no ReservedTenants settings.
- Demo BFF `spaarke-bff-demo`: Stopped, .NET 8 (T242c).
- Dev BFF `spaarke-bff-dev`: hand-set settings as before; deploy only from master.
