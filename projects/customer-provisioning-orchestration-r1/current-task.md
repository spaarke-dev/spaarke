# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2/§3/§6. Decisions → `notes/model1-dedicated-remediation-plan.md` D-table. Session narrative → commit messages.

> **Last Updated**: 2026-10-13 SESSION 47. PRs #1809 and #1822 merged (master `67c80623f`). Dev control plane deployed from `67c80623f` (H14m live; all post-deploy checks green). T240d spike S1 done → option 2. D32 Exchange roles applied, mailbox checks M1–M5 and M7 PASS, ISS-024 closed.

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | T186 (first live E2E via `/provision-environment`, mock customer homed in Dewey Cheatham, stamp in the DEMO subscription) and the T240d build (option 2), in parallel with reviews of UAC task 185 |
| **Status** | No agents running. The dev control plane is current with master. |
| **Next Action** | (1) Owner: revoke Graph `Mail.Read` from `sprk-controlplane-dev-uami`? (2) T186 via `/provision-environment` (needs a SpaarkeMaster export carrying `sprk_externalinvitedon` first; see Remaining). (3) T240d build. |
| **Branch** | `work/customer-provisioning-orchestration-r1` = master + this checkpoint |

## 🔔 Waiting on the owner

1. **Revoke `Mail.Read`** (app role `810c84a8-4a9e-49e6-bf7d-12d183f40d01`) on the L2 UAMI (principal 38f7693f-…): it is a tenant-wide mailbox read, and nothing uses it since H14b was removed. Recommend yes.
2. **ISS-026** publisher verification (ADR-028 A9): an owner/business action before the first production customer.
3. **UAC task 185** (invite code) is with the owner. Our review (2026-10-13): R1 secret-bearing send option, as a T240d production prerequisite; R2 `spaarkeextid` stays email + password. See t240d design §7 step 4 and §10.

## Live state

- Dev control plane `rg-spaarke-platform-dev`: master `67c80623f` (2026-10-13). The Api is swapped to production and the Worker deployed. The sidecar image was built from `d39c066c3` (H14m endpoints). `CustomerRunGuard__Enabled=True`.
- Spaarke Exchange Admin (app 46670ee2, Exchange SP b5d396bb) holds `Spaarke App RBAC Admin`, `View-Only Recipients`, and the D32 roles `Spaarke Shared Mailbox Create` and `Spaarke Scope Group Membership` (scope `Spaarke Customer Scope Groups`). There is a leftover delegating `sprk-exoadmin-deleg-MailboxSettingsRead` (harmless; remove at the next maintenance).
- Store: SpaarkeMaster 1.2.1.0 latest. It does NOT yet carry `contact.sprk_externalinvitedon` (UAC #1801, merged).
- Demo BFF `spaarke-bff-demo` is stopped (.NET 8). Retire it after T186.
- T240d spike S1 objects: all deleted (the owner deleted the test user and the spike enterprise app).

## 📋 Remaining

- **T186 prerequisites:** a new SpaarkeMaster export and version (carrying `sprk_externalinvitedon` plus the `sprk_createdbyperson` locks). Live proofs owed at T186:
  - ISS-008 (a second POST for the same customer is refused);
  - T261 OwnedBy ownership at H3;
  - ISS-015 Worker FIC at H6;
  - T260 census at H13;
  - H7b S19–S21;
  - T259 BU placement;
  - PRQ-C-14 attestation;
  - H14m route-level M4 (`Success` → `AlreadyCompliant`, `Test-ServicePrincipalAuthorization` for the 3 roles).
- **T240d build (option 2):**
  - MI-FIC provisioner;
  - H3c (CIAM API app `sprk-{customerId}-ciam-api`, with read-back and retry);
  - H4b `Ciam:*` (Audience = the CIAM API app id; `Ciam:Domain`);
  - H13 probes;
  - CiamUserProvisioningService (authority login.microsoftonline.com, `User.Create`);
  - the production CIAM SPA client (redirect `https://external.spaarke.com`).

  Gates are in design §10: BFF at or after 185, SpaarkeMaster carrying both contact columns, the secret-bearing send. The live acceptance checks are the cross-customer ExistingAccount check (UAC §11b) and the SSPR lock-out.
- **A9 (workforce sign-in at the home directory):**
  - H3: `requestedAccessTokenVersion=2`, `knownClientApplications`, and the workforce SPA client in PreAuthorizedClientAppIds;
  - intake `customerWorkforceTenantIds`;
  - H4b `WorkforceIdentity__CustomerTenantIds__N`;
  - the admin-consent URL in the hand-off;
  - accept SPA-r3 task 041's directory field.

  Mapping: `notes/coordination/2026-10-13-from-spa-r3-adr028-a9-workforce-signin.md`.
- **ISS-025 (#1803):** H7b verifies the locks on `sprk_externalinvitedon` and `sprk_externalinvitecodehash`.
- **Other:**
  - T240b, T240c, ISS-022;
  - remove the old L2 broad Graph roles after the T186 H3 proof;
  - 203d; wrap-up 090.

## 🐞 Open defects

#1543 ISS-018 · #1484 ISS-008 · #1485 ISS-009 · #1487 ISS-011 · #1499 ISS-012 · #1500 ISS-013 · #1523 ISS-017 · #1561/#1562/#1563/#1564/#1570 · #1590 ISS-023 · #1803 ISS-025 · #1811 ISS-026 · #1504 · #1432 · #1376 · #1377.
