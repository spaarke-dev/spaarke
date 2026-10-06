# T251 — Exchange sidecar: identity + RBAC for Applications design

> **Status**: IMPLEMENTED 2026-10-04 (T251 complete). Spikes S0–S3 passed live (§7); S4, the Graph *effect* of a grant (30 min–2 h), is not checked by T4 and was not run. Deployed and verified in-tenant (`sidecar-live-verification-2026-10-04.json`).
> **Task**: `tasks/251-exchange-policy-sidecar-works.poml` (gap G30). Research: researcher memory
> `exo-sidecar-identity-2026-10-03.md`, `exo-rbac-for-apps-design-2026-10-04.md`.

## 1. Owner decisions (2026-10-04)

| # | Decision | Why |
|---|---|---|
| **D24** | The sidecar holds **no credential**. A new single-tenant Entra app, **`Spaarke Exchange Admin`**, carries a federated identity credential whose subject is the Worker UAMI (ADR-028 A4 default — same pattern T248 proved for SPE). The **Worker** mints the Exchange token (`https://outlook.office365.com/.default`) and passes it to the sidecar per request; the sidecar runs `Connect-ExchangeOnline -AccessToken`. No certificate, no secret, no Key Vault read in the sidecar. | `Connect-ExchangeOnline -ManagedIdentity` is not documented for App Service; a certificate adds a rotation lifecycle ADR-028 ranks lower. |
| **D25** | Exchange permission for `Spaarke Exchange Admin` = the **narrowest** that works (a custom child of *Role Management* limited to the RBAC-for-Applications cmdlets + delegating assignments for only the application roles H14a grants). Exchange Administrator only if narrowing fails — and only after returning to the owner. | Least privilege. |
| **D26** | H14a moves from **ApplicationAccessPolicy** (Microsoft: "legacy, replaced by RBAC for Applications"; ~100–300 policies per tenant) to **RBAC for Applications** in T251. | All Model 1 stamps live in Spaarke's tenant (2 policies per customer) — the cap is a real ceiling; nothing depends on H14a yet. |

## 2. What changes (the whole surface)

| Area | Today | After T251 |
|---|---|---|
| **H10** | Grants the stamp UAMI 15 Graph app roles **tenant-wide**, incl. `Mail.Read`, `Mail.ReadWrite`, `Mail.Send`, `MailboxSettings.Read`. | Grants the 11 non-mailbox roles. The 4 mailbox roles are granted by H14a **through Exchange**, scoped to the customer's group. (Entra + Exchange grants are a **union** — leaving the Entra grants in place would make the scope meaningless.) |
| **H14a** (via sidecar) | `New-ApplicationAccessPolicy -AccessRight RestrictAccess` for BFF app + UAMI. | **Stamp UAMI only** (correction found in code reading: the BFF app registration does no app-only mail — its Mail.Send is delegated — and RBAC for Applications GRANTS access, so granting it would widen its reach): `New-ServicePrincipal` (Exchange pointer to the Entra SP) → scope on the customer group (`-RecipientGroupScope` or `New-ManagementScope … MemberOfGroup`) → `New-ManagementRoleAssignment -App … -Role "Application Mail.Read"` etc. with **deterministic names** (idempotency key). Get-before-set; an existing assignment with the same name but a different role/scope, or a mailbox-role assignment for these identities outside the expected scope → **Drift** (no silent overwrite — T4 semantics kept). |
| **H13 T4** | Reads `Get-ApplicationAccessPolicy` via sidecar `GET /policies`. | Reads the identities' role assignments via a read-only sidecar route; passes when every expected (identity, role, scope) is present. Checks **configuration** — effect lags 30 min–2 h (documented). |
| **H13 T3** | Expects all 15 Graph roles on the UAMI. | Expects the 11 Entra-granted roles; the 4 mailbox roles must be **absent** in Entra (their presence would void the scope). |
| **Sidecar** | Fetches a KV certificate per call (sentinel in dev); env vars passed as literals → empty → refuses to start → Worker 503. | Needs one setting (`SIDECAR_SHARED_SECRET`, wired **by app-setting name**). **Always binds its port**; a missing setting or token makes requests fail with a named diagnostic instead of taking the Worker down. Script rewritten for RBAC for Applications; `-AccessToken` connect. |
| **Worker** | Never had `IntegrationWiring__SidecarSharedSecret*` settings → H14a would fail even with a healthy sidecar. | Gets those three + `IntegrationWiring__ExchangeAdminAppId`; mints the token via `WorkerDataverseCredentialFactory.CreateManagedIdentityFederatedCredential`. |
| **BFF** | `GraphAppRoles.All` (15) is the canonical list; nightly drift test compares it to L2's registry. | **No BFF code change planned**: the BFF needs the same 15 permissions; only the *grant mechanism* of 4 differs on stamps, which is L2's concern. L2's registry marks the 4 as Exchange-scoped. |
| **Bicep / KV / scripts** | `exchangeConnectAppId` (zeros), `Exchange-Connect-Cert` (sentinel, seeded). | Params removed; `exchangeAdminAppId` added. Seed script stops seeding `Exchange-Connect-Cert`. The existing sentinel secret is **not deleted** (KV lifecycle rule) — recorded as unused. |

## 3. Request flow (after)

```
H14a ──► Worker: token = MI-FIC(tenant, ExchangeAdminAppId) for outlook.office365.com
     ──► POST http://127.0.0.1:8091/apply-mailbox-access   (X-Sidecar-Auth + X-Exchange-Access-Token)
            sidecar: Connect-ExchangeOnline -AccessToken … ; ensure SP + group-scoped assignments ; read back
     ◄── Success | AlreadyCompliant | Drift | Failure
H13 T4 ──► POST /read-mailbox-access (read-only)
```

The stamp UAMI's Entra service-principal object id (`New-ServicePrincipal -ObjectId`) is `InterStepState.MiObjectId`.

## 4. Risks stated plainly

- **Escalation**: any identity able to create application role assignments can grant *any* app — itself included — tenant-wide mailbox access (the scope is optional). Narrowing (D25) reduces the blast radius but cannot remove it. Mitigation: only the Worker UAMI can obtain the token; audit `New-ManagementRoleAssignment` in the unified audit log.
- **Propagation**: grants take effect 30 min (idle app) – 2 h (active app). A fresh stamp's mail features work within ~2 h of H14a, not immediately.
- **Direct members only**: nested groups in the customer's scope group are not honoured.
- **Limit**: 10,000 apps per tenant use RBAC for Applications → ~5,000 Model 1 customers.

## 5. Spikes (live, owner-approved, before code is locked)

| # | Question | Pass condition | If it fails |
|---|---|---|---|
| S1 | Does app-only `-AccessToken` (MI-FIC token) run `New-ServicePrincipal` / `New-ManagementRoleAssignment` **without** the Entra Exchange Administrator role, using only the narrowed Exchange role? | The assignment is created. | Back to the owner (D25): Exchange Administrator for `Spaarke Exchange Admin`. |
| S2 | Escalation test: can the narrowed app grant itself a role it should not have? | Denied. | Record the residual risk; owner decides. |
| S3 | Does a role assignment scoped to a group restrict access as documented? | `Test-ServicePrincipalAuthorization`: in-scope mailbox `InScope=True`, out-of-scope `False`. | Investigate before coding. |
| S4 | Do **Graph change-notification subscriptions** on `/users/{id}/messages` work when the only mail grant is an Exchange RBAC assignment? (H14b depends on it; undocumented.) | `POST /subscriptions` succeeds for the in-scope mailbox and fails for an out-of-scope one. | H14b needs a different design → back to the owner. |

## 6. Separate finding (not T251 scope) — filed as G31

H10 grants every stamp UAMI `Directory.ReadWrite.All`, `User.ReadWrite.All`, `GroupMember.ReadWrite.All` and `User.Invite.All` **tenant-wide**. On Model 1 every stamp lives in **Spaarke's** tenant, so each customer's BFF identity can modify Spaarke's directory. Owner decision needed.

## 7. Spike log (2026-10-04, owner-approved)

| Time (UTC) | Step | Result |
|---|---|---|
| 13:20 | Exchange setup run 1 (operator) | Exchange SP for `Spaarke Exchange Admin` + test group created. Every role assignment refused: tenant not customised (`Enable-OrganizationCustomization` required — even for built-in roles). |
| 13:37–14:37 | `Enable-OrganizationCustomization` (owner-approved, irreversible) | `IsDehydrated` False by 14:22; custom roles allowed from 14:37. |
| 14:37 | Setup run 3 | Role **`Spaarke App RBAC Admin`** = child of Role Management trimmed to 14 cmdlets (`*-ServicePrincipal`, `*-ManagementScope`, `*-ManagementRoleAssignment`, `Get-ManagementRole`, `Test-ServicePrincipalAuthorization`); assignments `sprk-exoadmin-rbacadmin`, `-viewrecipients` (View-Only Recipients), `-deleg-{MailRead,MailReadWrite,MailSend,MailboxSettingsRead}` (DelegatingOrgWide). Assignee recorded as the Entra **SP object id**. |
| 14:39 | Container run 1 (Worker UAMI → MI-FIC → `-AccessToken`) | **S0 ✅** token `appidacr 2`, `roles Exchange.ManageAsApp`; connect with `-Organization <tenant GUID>` works — **but see 16:27: with a GUID, writes fail; use the initial domain**. **S2 ✅** self-grants of Exchange Full Access / Role Management / Mail Recipients DENIED ("must be assigned a delegating role assignment"). `New-ManagementScope` ✅. `New-ServicePrincipal` ❌ "doesn't have write permission to target DC … insufficient access rights". |
| 15:10–15:23 | Owner-approved test: Entra **Exchange Administrator** on the admin app | Worse — every write (incl. escalation tests) failed with the DC-write error. **Removed** at 15:21 (owner rule: remove if it fails). |
| 15:18 | Operator registered the test app's Exchange SP (device sign-in) | ✅ (operator writes work). |
| 15:23 | Container run 3 (narrowed role only) | S2 denials normal again; but `New-ServicePrincipal`, `New-ManagementRoleAssignment` and `New-ManagementScope` all fail with the DC-write error — the same write succeeded at 14:39. Working hypothesis: Exchange still settling after the customization switch. Automatic retry at ~16:25. |
| 16:24 | Researcher (`.claude/agent-memory/researcher/exo-apponly-dc-write-error-2026-10-04.md`) | Cause: `-Organization` must be the tenant's initial `.onmicrosoft.com` domain — the only value Microsoft documents for app-only; two Microsoft Q&A threads with accepted answers fix this exact error that way. Owner: no Microsoft support case. |
| 16:27 | Container run 4 (`-Organization spaarke.onmicrosoft.com`, narrowed role only) | **Writes work**: `New-ServicePrincipal` (target2) ✅. **S2 ✅** all four escalations still DENIED. **S3 ✅** `Test-ServicePrincipalAuthorization`: testuser1@ (in group) `InScope True`, dev@ (outside) `InScope False`. |
| 16:31 | Container run 5 | **S1 ✅** `New-ManagementRoleAssignment -App <target2> -Role 'Application Mail.Read' -RecipientGroupScope <group DN>` app-only ✅; S3 ✅ (`ScopeType Group`). Read-back shape: **no `RecipientGroupScope` property** — `RecipientWriteScope = Group`, `CustomResourceScope = <group Name>`; `Get-ManagementRoleAssignment -RoleAssignee <SP object id>` finds it. |
| 16:33 | Container run 6 (read-only) | `Get-Group` does **not** return `ExternalDirectoryObjectId`; `Get-Recipient` does, and `Get-Recipient -Filter "ExternalDirectoryObjectId -eq '<id>'"` finds the group. A management-scope assignment reads `RecipientWriteScope = CustomRecipientScope`. → Code fixed: Worker sends `organization` (Graph `/organization` initial domain); sidecar requires it and matches scope on `RecipientWriteScope` + `CustomResourceScope`. |
