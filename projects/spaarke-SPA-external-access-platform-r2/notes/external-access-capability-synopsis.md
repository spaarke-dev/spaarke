# External Access — Code-Based Capability Synopsis (post-R1)

> **Date**: 2026-07-21 · **Purpose**: ground R2 scope decisions in what actually exists in code.
> Source: three parallel code investigations (BFF authz/data model · external SPA + SWA wiring · new-env setup). All claims cited to `file:line` in the sub-reports; key pointers inline below.

**One-line framing**: External access is modeled as **(Contact → Project) participation grants**, brokered by the BFF, surfaced through **one** SWA-hosted SPA authenticated by **one** CIAM tenant. There is **no "application/SPA" concept** in the data model — access is per-record, not per-app.

---

## 1. How a user (Contact) is set up for external access

**Mechanism**: admin-initiated broker onboarding — resolve/create a Dataverse `contact`, create a CIAM local account, bind its `oid`.

- Endpoint: `POST /api/v1/external-access/invite` → `InviteExternalUserEndpoint.ProvisionAsync`.
- `ResolveOrCreateContactAsync` finds the Contact by `emailaddress1` (or creates it with first/last name).
- **Idempotency gate**: if `Contact.sprk_externalobjectid` is already set → skip, return `AlreadyProvisioned`.
- Else `CiamUserProvisioningService.CreateCiamUserAsync` calls Graph `POST /users` on the **cross-tenant CIAM app-only** client (`CiamGraphClientFactory`): identity `signInType=emailAddress`, `issuer=spaarkeextid.onmicrosoft.com` (**local account, not social**), temp password + `forceChangePasswordNextSignIn=true`, `passwordPolicies=DisablePasswordExpiration`.
- Returned `oid` persisted to **`Contact.sprk_externalobjectid`** (String/100). Also bound lazily at first login (`ExternalParticipationService.BindOidToContactAsync`, anti shared-email-hijack: a Contact bound to a different oid is refused).

**Net**: a "user set up for external access" = a Contact with `sprk_externalobjectid` bound to a CIAM local account. No group/role/app membership involved.

## 2. How access rights are assigned to the external user

**Mechanism**: a `sprk_externalrecordaccess` row (junction table) grants one Contact one access level on one Project.

- Access levels (`ExternalAccessLevel`, integer option-set): `ViewOnly=100000000`, `Collaborate=100000001`, `FullAccess=100000002`.
- Effective rights (`ExternalCallerContext.GetEffectiveRights`): ViewOnly→`Read`; Collaborate→`Read|Create|Write`; FullAccess→`Read|Create|Write|Delete`; none→`None`.
- Enforcement is **server-side** in the BFF (`ExternalCallerAuthorizationFilter` + per-endpoint `rights.HasFlag(...)`). Client flags are UX-only.
- The historical "three-plane" model (Power Pages table-perms + SPE container role + AI Search filter) is **collapsed to one plane** under ADR-028 A1: the BFF participation-set check. No synthetic SPE membership is written; all external SPE reads are app-only.

## 3. How a record is set up for external access

**Mechanism**: a `sprk_project` must be flagged secure and then provisioned.

- Eligibility gate: **`sprk_project.sprk_issecure = true`** (Boolean, immutable after creation). Non-secure projects can't be provisioned or granted.
- Endpoint: `POST /api/v1/external-access/provision-project` → `ProvisionProjectEndpoint`:
  1. Verify project exists and `sprk_issecure == true` (else 400).
  2. Resolve/create a child **Business Unit** (`SP-{ProjectRef}`) for isolation.
  3. Create an **SPE container** via `SpeFileStore.CreateContainerAsync` (ADR-007).
  4. Create an **External Access Account** owned by the BU.
  5. Write refs on the project: `sprk_securitybuid`, `sprk_specontainerid`, `sprk_externalaccountid`.
- Rollback deletes a newly-created BU on SPE/Account failure.

**Net**: a record is externally accessible once it's `sprk_issecure` + provisioned (BU + SPE container + External Access Account).

## 4. How an external user is assigned access to a record

**Mechanism**: the grant flow — grantee is **always the Contact (person)**, never the firm/Account.

- `POST /api/v1/external-access/grant` → `GrantExternalAccessEndpoint.CreateGrantAsync`.
- `POST /api/v1/external-access/invite-and-grant` → onboard (idempotent) + grant in one atomic action (the core-user "Invite to Secure Workspace" action).
- Payload: `sprk_contactid` (grantee) + `sprk_projectid` + `sprk_accesslevel`, `sprk_granteddate`, `sprk_grantedby` (audit — caller's oid), optional `sprk_expirydate`, optional `sprk_accountid` (firm, record-keeping only).
- **Redis cache invalidation (ADR-009)** on grant/revoke/close (`sdap:external:access:{contactId}`, 60s TTL) → grant visible immediately.
- Revoke deactivates the row (`statecode=1`) + cache-invalidates; close-project cascades over all active grants.

## 5. How an external user is assigned to a specific SPA  ⭐ (the multi-SPA question)

**Answer: they are NOT — there is no per-SPA/per-app assignment concept in the system today.**

- `sprk_externalrecordaccess` has **no app/portal/application column**. A grep for `sprk_application|sprk_portal|applicationid|portalid` finds no external-access entity or field.
- `ExternalParticipationService.GetParticipationsAsync` returns a flat `List<{ProjectId, AccessLevel}>` filtered **only** on `_sprk_contact_value + statecode=0`. **App-agnostic.**
- The BFF `/api/v1/external` surface authorizes purely on a CIAM JWT + the Contact's participations. The SPA sends **no app identifier** (only `Authorization: Bearer`). CORS is suffix-permissive (`*.azurestaticapps.net`, `*.dynamics.com`, …).
- Auth plane is pinned to **one CIAM tenant + one audience** (`api://4a4d5126…/SDAP.Access`, scheme `AuthSchemes.Ciam`).

**Consequence**: today, if a Contact has grants, they see those Projects through **any** SPA that presents a valid CIAM token — you cannot say "Contact X may use App A but not App B." To support per-SPA assignment you would have to **introduce that concept** (e.g. an application/portal entity + a Contact↔App or grant↔App scoping column, and have the SPA identify itself to the BFF). It does not exist.

## 6. UI surfaces we have / need

**Have (in the external SPA)** — `src/client/external-spa/`, BrowserRouter:
- `/` WorkspaceHomePage (aggregated projects/docs/events/tasks), `/project/:id` (Overview/Calendar/Contacts tabs), `/upload`, `/playbooks/:type/:id`, `/settings`, `*` 404.
- **One onboarding UI**: `InviteUserDialog.tsx` — email + name + access level → `POST /invite`. Gated to `FullAccess` external users, in the Contacts tab only. (Peer-invite, not admin onboarding.)

**Missing / needed** (drives R2):
- **No core-user/admin UI** for onboarding. `invite-and-grant`, `grant`, `revoke`, `provision-project`, `close-project` are **API-only** — no button on the Matter/Project form in this repo. (DI-029-01.)
- No UI to **mark a project secure / provision it** (calls `provision-project` — today wizard/API).
- No UI to **manage/list/revoke** existing grants for a project (only peer-invite exists).
- If per-SPA assignment is ever wanted (aspect 5) → no UI exists because the concept doesn't exist.

## 7. Technical components to set up external access in a NEW customer environment

Grouped; **manual-ops** items are the heavy cost (a separate CIAM tenant the workforce MI can't reach).

**(a) CIAM tenant + app-regs — mostly MANUAL portal/Graph**: dedicated CIAM external tenant; sign-in user flow (`isSignUpAllowed=false`); SSPR Email OTP (manual); **SPA public client**; **BFF API app** with `requestedAccessTokenVersion:2` + `SDAP.Access` scope; **Graph provisioner app** with `User.ReadWrite.All` (admin consent) + cert; admin consents; associate apps with user flow.

**(b) Azure infra — scriptable**: SWA resource + deploy-token GitHub secret; import provisioner **cert** into Key Vault (`ciam-graph-provisioner-cert`, by name only); grant BFF MI **Key Vault Secrets User**.

**(c) BFF config — scriptable (`az webapp config appsettings set`)**: `Ciam__Instance/TenantId/ClientId/Audience/Domain`, `Ciam__GraphProvisioner__ClientId/CertificateName`, `ExternalAccess__PortalUrl`, `Cors__AllowedOrigins`. (Note: BFF deploy pipeline does **not** run `#{...}#` token substitution — these land as App Service settings, set out-of-band.)

**(d) Dataverse schema — scriptable + solution import**: `contact.sprk_externalobjectid` (String/100); `sprk_externalrecordaccess` table + views + subgrid + Quick Create; BFF **App Service MI as a Dataverse Application User** with roles (manual).

**(e) Repo/pipeline config**: env block in `config/environments.json` + `config/spaarke-resources.yaml`; `VITE_*` build env in `deploy-external-spa.yml`; run the SWA deploy workflow (`workflow_dispatch` today).

## 8. How to add a NEW SPA that hooks into the external-access plumbing

Because the backend is **app-agnostic + identity-scoped**, a second SPA in the **same CIAM tenant** is essentially **config-only**:

1. New **SWA resource** + deploy workflow (copy `deploy-external-spa.yml`, new SWA token secret). CORS needs no change if on `*.azurestaticapps.net`.
2. New **CIAM SPA public-client app-reg** (own client id + redirect URI) requesting the **same** `SDAP.Access` scope in the **same** CIAM tenant → **no BFF change** (the `Ciam` scheme validates the same audience regardless of which client got the token).
3. Point the new SPA's `VITE_*` at the same BFF + scope.

**What is hardwired / the real gaps:**
- A **different CIAM tenant** would require BFF work (today `AuthSchemes.Ciam` is a single authority/audience — you'd add a second scheme).
- **No per-app data boundary**: the second SPA's users (Contacts with grants) would see the **same** projects through the **same** `/api/v1/external` endpoints. There is no "this data belongs to SPA X." Any real separation must be modeled in Dataverse — it does not exist today.
- The new SPA reuses the **same** `/api/v1/external` data surface (projects/documents/todos/contacts/download). If a new SPA needs a *different* data shape, that's new BFF endpoints, not just config.

---

## What this means for R2 scope (decision inputs)

- **The single biggest usability gap is #6/aspect-1: no core-user onboarding UI.** Everything server-side works and is verified; the platform simply can't be operated by the target persona without curl. (E-1 / DI-029-01.) Highest value.
- **Multi-SPA / per-SPA assignment (aspects 5 & 8) is an architecture question, not a bug.** Adding a second SPA is cheap (config); *scoping a Contact to specific SPAs* is a **new capability** that requires a new data concept. If multiple SPAs on shared external identities are on the roadmap, R2 should decide whether per-app scoping is needed — and if so, that's a design item, not a small fix.
- **New-customer-env onboarding (aspect 7) is heavy on manual CIAM ops.** If Spaarke will onboard many customer environments, an R2 "external-access provisioning runbook/automation" (script the scriptable, checklist the manual) has real leverage.
- Reliability/verification gaps (E-2 provisioner self-healing, E-3 live-E2E, E-4 SSPR) remain valid but are smaller than the UI + architecture questions above.
