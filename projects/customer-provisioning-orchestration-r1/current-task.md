# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2/§3/§6. Decisions → `notes/model1-dedicated-remediation-plan.md` D-table. Session narrative → commit messages.

> **Last Updated**: 2026-10-14 SESSION 48. PR #1860 merged (master `bd52b6290`). SpaarkeMaster **1.2.3.0 PUBLISHED** (CI run 38603706393): it carries `contact.sprk_externalinvitecodehash`, which satisfies the T240d release gate; UAC has been told. The old demo stack has been retired (owner D35). T186 = the ACME Corporation demo stamp; prerequisites are in progress.

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | T186: first live E2E via `/provision-environment acme` ("ACME Corporation", DEMO subscription `2ff9ee48-…`, managed SpaarkeMaster 1.2.3.0). Run log: `notes/t186-acme-run-log.md`, kept up to date AS THINGS HAPPEN (owner 2026-10-14: the process must be fully documented and repeatable; each lesson goes into the operator docs before T186 closes). |
| **Status** | Waiting on the owner's prerequisites (below). My prerequisites done: PRQ-S-04 (L2 Worker UAMI Owner on the DEMO sub) and PRQ-C-10 (group `sprk-acme-users` `7de5a154-2604-438b-b1cd-462406a869a9`). No agents running. |
| **Next Action** | When the owner says the environment `spaarke-acme` exists: (1) add the L2 Worker as a System Administrator application user (PRQ-C-09); (2) set `restrictguestuseraccess=false` (PRQ-C-12); (3) create the registry placeholder on spaarkedev1 (PRQ-E-13, skill Step 1); (4) create two ACME test users; (5) run `/provision-environment acme`. Each live action needs the owner's OK. |
| **Branch** | `work/customer-provisioning-orchestration-r1` = master + this checkpoint |

## 🔔 Waiting on the owner

1. **ACME prerequisites:**
   - the ACME Workforce tenant (PRQ-C-13; plus the PRQ-C-14 outbound B2B attestation);
   - the Dataverse environment `spaarke-acme` (US, Production, security group `sprk-acme-users`);
   - the PAYG billing policy on the DEMO sub (PRQ-C-11);
   - the Exchange group `Spaarke-AppAccess-acme` (PRQ-C-08).
2. **Demo accounts the owner hasn't classified:** `jake.schroeder`, `rachel.schroeder`, `sarah.johnson`, `jane.smith`, `alex.demo` @demo.spaarke.com. Keep or delete?
3. **Dev BFF secrets:** an agent printed the email-processing webhook secret, the signing key and the SignalR key into its local transcript. Rotating them is the owner's call.
4. **ISS-026** publisher verification: Partner ID 7011236. The owner asked why it is needed; the answer was "consent-screen trust, not functionally required". The operator steps wait for Partner Center facts.

## Live state

- Dev control plane `rg-spaarke-platform-dev`: redeployed 2026-10-14 from `e18cb33a0` (= master `630373cbb`); all checks green; `CustomerRunGuard__Enabled=True`.
- Store: SpaarkeMaster **1.2.3.0** latest (2026-10-14, managed sha256 `e84cd5d9…3f662e`). 1.2.3.0 adds `sprk_externalinvitecodehash`, secured: Writers 4/4/4, Readers no row. It also adds other projects' dev work: ServiceRequest autonumber `RQST-{SEQNUM:4}`, the CommunicationConnections PCF, and 3 web resources. Nothing removed. The workforce role `Spaarke Internal Contact User (Workforce)` is still excluded until UAC 186 closes.
- **Old demo (D35), retired 2026-10-14:**
  - Deleted: the Dataverse env `spaarke-demo` (deleted by the owner), `spaarke-bff-demo` and its plan, SB, OpenAI, docintel, Cosmos, Redis, App Insights, `spaarke-demo-logs`, `mi-bff-api-demo`, the apps "Spaarke BFF API - Demo" and "Spaarke UI - Demo", and 10 test accounts.
  - KEPT: the website functions with `WestUS2Plan`, `sprkdemosa`, `sprk-demo-kv` and the Smart Detection action group; Syntex `1f3cfdfb` (owner OK); the group "Spaarke Demo Users" `745bfdf6` (used by dev BFF demo approvals); and the accounts jessica, lori, chelsea, `demo@` and `uac.child.user`.
- The demo-request flow is UNCHANGED and works: website → `spaarke-bff-dev` → `sprk_registrationrequest` (spaarkedev1) → "Approve Demo Access" → registry row "Dev". Retargeting it to the ACME stamp needs a code change (it ignores the row's `sprk_securitygroupid`) plus a cross-stamp identity decision. Bring the owner ONE recommendation after T186 (run log L3).

## 📋 Remaining

- **T186 live proofs:**
  - ISS-008: a second POST is refused;
  - T261: OwnedBy at H3;
  - ISS-015: Worker FIC at H6;
  - T260: census at H13;
  - H7b S19–S22;
  - T259: BU placement;
  - PRQ-C-14;
  - H14m route-level M4;
  - H3: 6-entry delegated catalog with the admin-consent gate (`POST /api/runs/{id}/gates/admin-consent/advance`).
- **Repo cleanup of old-demo references** (after #1860, now unblocked): `config/environments.json:77-115`, `config/spaarke-resources.yaml:601-630`, `spaarke-constants.yaml:79`, `scripts/Reconcile-DemoEnvironment.ps1`, `redis-demo.bicepparam`, `DocumentOperations.js:164` (demo-URL runtime branch), docs.
- **Builds:**
  - D36: H4b keyless SignalR string when `signalrEnabled`, plus a manifest note on `Rag__ApiKey`;
  - D37: `appRoleAssignmentRequired` on stamp BFF SPs, after A9;
  - T240c directory service (after T186; D38);
  - T240d (option 2; the keyless `CiamGraphClientFactory`, with `CertificateName` optional under MI-FIC);
  - A9 H3 changes (wait on SPA-r3 task 059);
  - 203d; wrap-up 090.
- Remove the old L2 broad Graph roles after the T186 H3 proof.

## 🐞 Open defects

#1543 ISS-018 · #1484 ISS-008 · #1485 ISS-009 · #1487 ISS-011 · #1499 ISS-012 · #1500 ISS-013 · #1523 ISS-017 · #1561/#1562/#1563/#1564/#1570 · #1590 ISS-023 · #1803 ISS-025 · #1811 ISS-026 · #1504 · #1432 · #1376 · #1377.
