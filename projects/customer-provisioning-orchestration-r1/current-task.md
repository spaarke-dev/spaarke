# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2/§3/§6. Decisions → `notes/model1-dedicated-remediation-plan.md` D-table (D30/D31 = 2026-10-09). Session narrative → commit messages.

> **Last Updated**: 2026-10-10 SESSION 46. PRs #1502, #1518, #1535, #1589 merged. SpaarkeMaster 1.2.1.0 published. Dev control plane deployed from master `327368057` (secret-free Worker, run guard ON, ReservedTenants, Copilot client pre-auth). G36 management groups applied. Waiting on owner answers; then T186.

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Owner answers below → T240d spike S1 → T186 (first live E2E, mock customer homed in Dewey Cheatham, stamp in the DEMO subscription). |
| **Status** | waiting on the owner. No agents running. |
| **Next Action** | (1) On "ready": fresh device-code sign-in to `spaarkeextid` with private `AZURE_CONFIG_DIR` (scratchpad `azcfg-ciam`; `az login --tenant 7052feba-bfc4-43e0-b09e-65014b429131 --allow-no-subscriptions --use-device-code` in background, read the code from the output file), then spike S1 (design §8 + R3 asks R1/R2/R5). (2) On OK: revoke Graph `Mail.Read` (app role `810c84a8-4a9e-49e6-bf7d-12d183f40d01`) from `sprk-controlplane-dev-uami` (principal 38f7693f-…). (3) Then T186 via `/provision-environment`. |
| **Branch** | `work/customer-provisioning-orchestration-r1` = master + this checkpoint. |

## 🔔 Waiting on the owner

1. **Revoke `Mail.Read`** on the L2 UAMI — tenant-wide mailbox read; only H14b used it (removed). Recommend yes.
2. **"ready"** → spike S1 in `spaarkeextid` (approved 2026-10-09: additive writes only; never delete/change existing config; sign-up stays off).
3. **T240d split (R4)**: R3 builds Ciam valid audiences + the R1 default-scheme guard; we build CiamGraphClientFactory → MI-FIC. Recommend yes.
4. **Mail model (#1562)**: one Spaarke-tenant shared mailbox per customer, created + verified by a provisioning step after H14a. Recommend yes.
- Owner already decided via R3 (2026-10-09): `user_impersonation` everywhere; join-link self-registration, auto-approve, invite permission in one shared service (T240c directory); modules by user type; PRQ-C-14 required.

## Spike S1 objects already created (throwaway — delete when S1 ends)

- Spaarke tenant app "Spaarke CIAM Spike S1 - Dev" appId `40ec7b2b-d13c-4165-87c3-f0fc807e8840` (obj `78a9cb64-…`), multitenant, `api://{appId}`, scope `user_impersonation`, `requestedAccessTokenVersion=2`, FIC `spike-s1-uami` → UAMI.
- UAMI `id-spaarke-ciam-spike-s1-dev` (rg-spaarke-dev, sub 484bc857; clientId `a3a09844-…`, principal `51d8ba07-…`).
- Still to do: SP for the spike app in `spaarkeextid` + Graph `User.Create` there; a short-lived ACI in rg-spaarke-dev running as the UAMI for (i)/(ii); (iii) user token incl. R5 silent second resource; (iv) cross-audience + workforce-route refusal (R1 hard gate).

## ✅ Done (SESSION 46)

- Merged: #1502; #1518 SpaarkeMaster 1.2.1.0 (published, latest.json); #1535 (T252, T257, T259, T260/ISS-014, ISS-015, T261/G31, T262/G36, run guard); #1589 (ISS-019 H14b/H14c removed, ISS-020 H7b S19–S21, ISS-021 T7 fail-closed, #1564 guide, PRQ-C-14).
- Live (owner OK): Copilot client "Spaarke Copilot Agent - Dev" `3a36eac4-10c5-4b90-9a83-913138a466af` (no secret, admin consent); registry columns `sprk_bffappid`, `sprk_copilotauthconfigid` on spaarkedev1; G36 MGs `spaarke-environments` → `spaarke-customers` + 9 Audit policies; demo → customers, dev + shared prod → environments; temp root Contributor removed; L2 UAMI granted Graph `Application.ReadWrite.OwnedBy` + `AppRoleAssignment.ReadWrite.All` (old broad roles stay until OwnedBy ownership is proven at T186 H3); dev control plane Bicep + code + swap (all checks green; KV untouched).
- Coordination: notes to/from external-access-r3 (`notes/coordination/2026-10-09-*`); R3 review accepted T240d option 3 subject to S1 + R1.

## 📋 Remaining

- **T186** (owner gate cleared except the answers above): mock customer homed in Dewey Cheatham `bc3aa7f4-3ca3-47e6-84e7-fea35f5c245b`, stamp in the demo subscription `2ff9ee48-…` (now in `spaarke-customers`), container type dev `fb3817a8-…`. Live proofs owed there: ISS-008 (second POST for the same customer refused), T261 OwnedBy ownership at H3, ISS-015 Worker FIC at H6, T260 census at H13, H7b S19–S21, T259 BU placement, PRQ-C-14 attestation.
- After S1: T240d build (+R3 asks), production CIAM SPA client (redirect exactly `https://external.spaarke.com`).
- Open owner-gated: 240b (owner at keyboard), 240c, 242c (demo rebuild via T186), mail-model step (#1562), ISS-022 SignalR keyless setting.
- Post-186: 203d; L2 old broad Graph roles removal; wrap-up 090.

## 🐞 Open defects

#1543 ISS-018 (F3 live proof, F6 H3 delegated ids, F2 OBO search) · #1484 ISS-008 (proof at T186) · #1485 ISS-009 · #1487 ISS-011 · #1499 ISS-012 · #1500 ISS-013 · #1523 ISS-017 · #1561/#1562/#1563/#1564/#1570 (auth-system-of-record-r1 set) · #1590 ISS-023 · #1504 · #1432 · #1376 · #1377.

## Live state

- Dev control plane `rg-spaarke-platform-dev`: master `327368057`, Api swapped to production, Worker deployed; `CustomerRunGuard__Enabled=True`.
- Store: SpaarkeMaster 1.2.1.0 latest.
- Demo BFF `spaarke-bff-demo` Stopped (.NET 8) — retire after T186.
