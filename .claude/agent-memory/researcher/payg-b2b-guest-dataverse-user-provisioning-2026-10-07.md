---
name: payg-b2b-guest-dataverse-user-provisioning-2026-10-07
description: T232 — PAYG covers B2B guests (FAQ); pac licensing billing-policy cmds + PP API 2024-10-01 recipe; app-only guest→systemuser via documented JIT alternate-key role association; BAP addUser needs tenant admin/mgmt app
metadata:
  type: reference
---

# PAYG + B2B guests + app-only Dataverse user provisioning (2026-10-07, T232)

**Question**: Can an app-only Dataverse application user (UAMI, System Admin) make B2B guests Dataverse users + assign roles in a PAYG env with an env security group; how does an operator prove env X is linked to a billing policy on subscription S?

**Findings**
- PAYG FAQ: "Can guest use apps in a pay-as-you-go environment without licenses? Yes." Model-driven: yes. Meter = Power Apps per-app, $10/active user/**per app**/month. Prod + Sandbox only; Managed Env NOT a documented prereq. Tenant pooled capacity doesn't apply (1 GB DB + 1 GB file free; DB $48/GB). SP-owned premium flows are charged per run.
- create-users "Requirements": licence exceptions list does NOT name PAYG (only app-pass, marketing, free M365 Dataverse plan — all "on demand only"); note "Guest users should also have a license from the environment's tenant". Inference: PAYG behaves like app-pass → background sync won't add; on-demand (JIT / API / PPAC) will. Not explicitly documented.
- **pac HAS billing-policy commands under `pac licensing` (Preview)** — NOT `pac admin`: `list-billing-policies [--columns --top]`, `list-billing-policy-environments --billing-policy-id`, `get-billing-policy-environment --billing-policy-id [--environment]`, `get-environment-billing-policy [--environment]`, `get-billing-policy --billing-policy-id`. REST GA api-version **2024-10-01**: `GET api.powerplatform.com/licensing/billingPolicies` (value[].billingInstrument.subscriptionId/resourceGroup, status), `.../billingPolicies/{id}/environments[/{envId}]` (200/404). Delegated scope `Licensing.BillingPolicies.Read`. Token: `Get-AzAccessToken -ResourceUrl https://api.powerplatform.com/` (documented).
- **Documented app-callable Dataverse path** (aad-group-team doc): `POST systemusers(azureactivedirectoryobjectid=<oid>)/systemuserroles_association/$ref` → "If the user doesn't exist in Dataverse, the system automatically adds the user"; `GET systemusers(azureactivedirectoryobjectid=<oid>)` also JIT-adds. Group team: `POST teams {azureactivedirectoryobjectid, membershiptype}` (0 Members+guests, 1 Members, 2 Owners, 3 Guests). JIT users land in root BU.
- Plain `POST systemusers` with azureactivedirectoryobjectid: Create message exists, column writable, but `domainname` is SystemRequired; online-create semantics not documented → prefer alternate-key path.
- Non-SG-member error (community, not Learn): "The user … cannot be created in Microsoft Dataverse because the account is not a member of group …" (UserNotMemberOfCdsSecurityGroup). Nested groups not honored. App users bypass SG.
- BAP force-sync = `Add-AdminPowerAppsSyncUser -EnvironmentName -PrincipalObjectId` (REST not in Learn REST ref). Callers: tenant PP/D365/Global admin or registered management app (treated as PP Admin, unscopable; admin user must register it; SP can't self-register). Dataverse app-user SysAdmin role does NOT grant BAP access. MI-as-management-app undocumented.
- No Dataverse `organization` column for PAYG/billing (checked table ref). `organization.restrictGuestUserAccess` IS readable (logical name camel-case) — default restricted for new envs (guest-access doc 2026-03-09).

**Sources**: learn.microsoft.com/power-platform/admin/{pay-as-you-go-meters, pay-as-you-go-issues-faq, pay-as-you-go-set-up, create-users, control-user-access, add-users-to-environment, manage-group-teams, security/guest-access, powerplatform-api-create-service-principal, programmability-authentication-v2, programmability-permission-reference}; /power-platform/developer/cli/reference/{licensing, admin, user-management}; /rest/api/power-platform/licensing/billing-policy*/…; /power-apps/developer/data-platform/aad-group-team; systemuser/organization/team table refs (GitHub raw powerapps-docs).

**Open questions**: whether on-demand JIT honors PAYG as entitlement for an unlicensed guest (strongly implied, live T186 check); exact HTTP status/code of SG-refusal; whether restrictGuestUserAccess=true blocks admin-side user creation for guests; response schema of `get-environment-billing-policy`.

Related: [[dataverse-env-provisioning-e2e-2026-08-22]] (restrictGuestUserAccess flip; PAYG api-version there was the old 2022-03-01-preview — superseded by 2024-10-01), [[ciam-user-provisioning-graph-2026-07-19]].
