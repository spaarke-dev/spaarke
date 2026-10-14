# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2/§3/§6. Decisions → `notes/model1-dedicated-remediation-plan.md` D-table. Session narrative → commit messages.

> **Last Updated**: 2026-10-14 SESSION 47. PR #1823 merged (master `8cc1ef392`): SpaarkeMaster 1.2.2.0 source, decision-record delete relation, Crypto Pkcs/Xml 10.0.13 CVE pins. SpaarkeMaster 1.2.2.0 PUBLISHED (run 38493202673). Earlier: PRs #1809 and #1822 merged. Dev control plane deployed from `67c80623f` (H14m live; all post-deploy checks green). T240d spike S1 done → option 2. D32 Exchange roles applied, mailbox checks M1–M5 and M7 PASS, ISS-024 closed.

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | T186 (first live E2E via `/provision-environment`, mock customer homed in Dewey Cheatham, stamp in the DEMO subscription) and the T240d build (option 2), in parallel with reviews of UAC task 185 |
| **Status** | No agents running. The #1827 gate review was sent to UAC on 2026-10-14. UAC is fixing F1 (owner 98c: the inviter always gets `Sent`), F3 (503) and K1 (`#invite=`) in scope. When UAC sends the fix-round head SHA, re-check the fix diff only. Results are in t240d design §10. The dev control plane is at `67c80623f`; master since then adds only the CVE pins, the decision-record relation and SpaarkeMaster source, so no redeploy is needed for T186. |
| **Next Action** | (1) Owner OK to start T186. (2) T186 via `/provision-environment`, intake `solutionPackageType = managed` (D34); H6 imports 1.2.2.0. (3) T240d build. |
| **Branch** | `work/customer-provisioning-orchestration-r1` = master + this checkpoint |

## 🔔 Waiting on the owner

1. ~~L2 UAMI mail grants~~: decided 2026-10-13 (D33): keep for now.
2. **ISS-026** publisher verification (ADR-028 A9): an owner/business action before the first production customer.
3. **UAC task 185** (invite code) is with the owner. Our review (2026-10-13): R1 secret-bearing send option, as a T240d production prerequisite; R2 `spaarkeextid` stays email + password. See t240d design §7 step 4 and §10.

## Live state

- Dev control plane `rg-spaarke-platform-dev`: master `67c80623f` (2026-10-13). The Api is swapped to production and the Worker deployed. The sidecar image was built from `d39c066c3` (H14m endpoints). `CustomerRunGuard__Enabled=True`.
- Spaarke Exchange Admin (app 46670ee2, Exchange SP b5d396bb) holds `Spaarke App RBAC Admin`, `View-Only Recipients`, and the D32 roles `Spaarke Shared Mailbox Create` and `Spaarke Scope Group Membership` (scope `Spaarke Customer Scope Groups`). There is a leftover delegating `sprk-exoadmin-deleg-MailboxSettingsRead` (harmless; remove at the next maintenance).
- Store: SpaarkeMaster **1.2.2.0** latest (2026-10-14, managed sha256 `691d1dd1…f630f2fb`). It carries `contact.sprk_externalinvitedon` and its field-security profiles. It does NOT carry `sprk_externalinvitecodehash` (UAC #1827, not merged), which needs the next release before T240d production. The workforce role `Spaarke Internal Contact User (Workforce)` is excluded until UAC 186 closes.
- Demo BFF `spaarke-bff-demo` is stopped (.NET 8). D27 (owner 2026-10-05): demo is rebuilt by T186. Delete it after T186, with the owner's OK.
- PR #1838 (pre-T186: ISS-018 F6 H3 delegated ids + ISS-025 H7b S22) MERGED 2026-10-14 (master `630373cbb`). Dev control plane REDEPLOYED 2026-10-14 from `e18cb33a0` (= master `630373cbb` + notes; owner OK): the swap, Worker health, queue properties and config keys are all green. T186 next. Its customer prerequisites are not yet met (findings 2026-10-14): no L2 Owner on the DEMO sub; existing `spaarke-demo` Dataverse holds SpaarkeMaster 1.0.0.0 UNMANAGED (H6 would refuse managed), so a NEW Dataverse env is needed; no registry placeholder, no `Spaarke-AppAccess-demo` group, Microsoft.SignalRService not registered. Owner approved the T240c directory service in `rg-spaarke-shared-prod` 2026-10-14.
- T240d spike S1 objects: all deleted (the owner deleted the test user and the spike enterprise app).

## 📋 Remaining

- **T186 prerequisites:** met (SpaarkeMaster 1.2.2.0 published). Live proofs owed at T186:
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
  - `CiamGraphClientFactory`: make `CertificateName` optional under MI-FIC (after #1827 it is required);
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
