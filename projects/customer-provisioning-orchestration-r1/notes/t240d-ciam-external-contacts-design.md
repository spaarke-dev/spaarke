# T240d step 1: design note on how stamp BFFs serve CIAM external contacts

> **Date**: 2026-10-08 · **Task**: `tasks/240d-stamp-bff-ciam-external-contacts.poml`, step 1 · **Status**: draft for the owner,
> external-access-r3 and unified-access-control-r2 (UAC-r2). Nothing live was touched.
> **Inputs**: `coordination/2026-10-07-from-external-access-r3.md` (Q3), `…-to-external-access-r3-2.md`; R3 `design.md` §4.6
> ("240d co-design"); UAC-r2 `notes/D-13-per-customer-bff-app-registration.md` and `notes/141-link-contract.md`; ADR-028 A1, A3, A4
> and A6; BFF and L2 code as cited below.

## 0. Escalation trigger: not hit

No design puts external contacts on a shared backend. R3 `design.md` §4.6 item 3 says "External contacts are served by the
customer's OWN backend (not a shared one), forced by D-13". Our 2026-10-07 reply and the owner's Q3 answer say the same.
UAC-r2 D-13 §2 gives the reason: the contact's records live in the customer's Dataverse, and only that customer's app
registration has an application user there. **Step 1 can go ahead.**

## 1. The code today (ground truth)

| What | Where | Status for a stamp |
|---|---|---|
| "Ciam" JwtBearer scheme: authority `{Ciam:Instance}/{Ciam:TenantId}/v2.0`; one `Audience = Ciam:Audience`; it validates issuer, audience, lifetime and signing key | `Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs:59-71` | Provisioning never sets `Ciam:*`, so every CIAM token fails |
| Policies: `CiamExternal` is Ciam-only; `ExternalCollaboration` accepts Ciam OR workforce | `AuthorizationModule.cs:343-361` | Unchanged |
| Plane choice: CIAM iff `iss` contains `ciamlogin.com` or `tid == Ciam:TenantId` | `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs:353, 386-405` | Unchanged |
| CIAM caller resolves to a contact by `oid`, with an email-bind "repair" path; grants come from `sprk_externalrecordaccess` in this stamp's Dataverse | `CallerPrincipalResolver.cs:452-527`; `ContactIdentityBinder.cs:195-201` | Unchanged; see the E11 gap in §4 |
| E11: an unbound, active contact with no systemuser link is bound to **any** CIAM oid whose token email matches it. There is no check that this stamp invited that person | `ContactBindingDecision.cs:565-660` (E11 at :659) | Isolation gap (§4). The 141 contract (`141-link-contract.md:94`) says the path is "repair only" |
| CIAM Graph provisioner: MSAL confidential client `.WithCertificate(...)` from a Key Vault PFX; authority `{Instance}/{TenantId}`; client `Ciam:GraphProvisioner:ClientId` (an app registered **in** the CIAM tenant, `User.ReadWrite.All`) | `Infrastructure/Graph/CiamGraphClientFactory.cs:63-79, 112-145`; registered `Infrastructure/DI/ExternalAccessModule.cs:510-514`; template `appsettings.template.json:52-64` | Cannot run on a keyless stamp |
| Invite: `POST /users` creates a CIAM local account, writes the oid onto the contact, and emails `ExternalAccess:PortalUrl` | `Api/ExternalAccess/InviteExternalUserEndpoint.cs:115, 228-248`; `Services/Registration/CiamUserProvisioningService.cs:57-69` | Nothing handles "this person already has a CIAM account" (for example, invited earlier by another customer). The second customer's invite fails |
| `Ciam:ClientId` appears only in the template; no code reads it | `appsettings.template.json:57` | Do not set it on stamps |
| A keyless MI-FIC assertion and an ordered credential selector already exist | `Infrastructure/Auth/ManagedIdentityAssertionProvider.cs:60-90`, registered `AuthorizationModule.cs:207`; `OrderedCredentialClientProvider.cs:211, 366, 425-442` (`GetClientAsync(tenantId, clientId)`) | Reuse these. Do not build a new credential path |
| **L2 H3** creates one BFF app per customer in Spaarke's tenant: **`signInAudience = AzureADMultipleOrgs`**, `identifierUris = api://{appId}`, a FIC trusting the stamp UAMI (`spaarke-uami-trust`), and `preAuthorizedApplications` (T240a) | `Handlers/EntraAppReg/EntraAppRegOptions.cs:57, 121`; `GraphAppRegistrationProvisioner.cs:70-85, 446-503, 964-1050` | Nothing for CIAM |
| **L2 H4b** settings: `AzureAd__ClientId` comes from `from-h3-output:bff_app_client_id`; there is no `Ciam__*` and no `ExternalAccess__PortalUrl` | `scripts/canonical-secret-catalog/manifest.yaml:553+, 618-623`; `BulkAppSettings/PerEnvSourceCatalog.cs:58, 73` | Gap (F13) |
| **L2 H13**: keyless proof and workforce-audience checks; no CIAM probe | `Handlers/E2EAcceptance/*` (`E2EValidationRunner.cs:275` is the workforce audience) | Gap |
| Dev CIAM tenant `spaarkeextid` `7052feba-…`; dev SPA client `bd57e54e-…`; dev CIAM API app `4a4d5126-…`; provisioner `e63e6eb1-…`; sign-up is disabled (`isSignUpAllowed=false`) | `config/environments.json:59-67`, `config/spaarke-resources.yaml:205-213` | Accounts exist only by invitation |

## 2. Microsoft rules that shape the design (Learn)

1. **MI as a federated credential.** The app registration "must belong to the same tenant as the managed identity". To
   reach another tenant, "your app registration must be a multitenant application and provisioned into the other tenant".
   The issuer is `https://login.microsoftonline.com/{tenant}/v2.0` of the shared tenant. The subject is the MI principal
   id. The audience is `api://AzureADTokenExchange`. Only user-assigned identities can be used. An app holds **at most 20
   FICs**.
   <https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity>
   → **Correction to R3 §4.6:** "stamp MI → CIAM app in `spaarkeextid`" is not a supported shape. A stamp MI lives in
   Spaarke's tenant, so it can only federate into an app registered in Spaarke's tenant. That app must be multitenant and
   have a service principal in `spaarkeextid`.
2. **External tenants.** App registrations made in an external tenant are always single-tenant. Admin consent for
   enterprise apps is "same as workforce". Client credentials are supported for "v2.0 applications". Expose-an-API and
   app roles are "same as workforce". The authority is `<tenant>.ciamlogin.com`.
   <https://learn.microsoft.com/en-us/entra/external-id/customers/concept-supported-features-customers>
   Learn neither confirms nor denies that a multitenant app from a workforce tenant can be provisioned into an external
   tenant. **Spike S1 settles this (§8).**
3. **Creating users.** The least-privileged application permission for `POST /users` is **`User.Create`**;
   `User.ReadWrite.All` is "higher privileged". Example 3 shows how to create a customer account in an external tenant.
   <https://learn.microsoft.com/en-us/graph/api/user-post-users?view=graph-rest-1.0>
   Changing `identities` needs `User.ManageIdentities.All`. App-only `passwordProfile` updates need
   `User-PasswordProfile.ReadWrite.All` **and** the User Administrator role.
   <https://learn.microsoft.com/en-us/graph/api/user-update?view=graph-rest-1.0>
4. **Limits.** A tenant holds 50,000 directory objects, or 300,000 with a verified domain; users and service principals
   both count. An object has at most 700 app roles plus scopes. A new tenant is limited to 600 objects for its first two
   days. <https://learn.microsoft.com/en-us/entra/identity/users/directory-service-limits-restrictions>

## 3. (a) CIAM audience: per customer, using the customer's own BFF app

**Option 1: one shared CIAM API app (today's dev shape, `4a4d5126`).** Every stamp would accept the same `aud` and `iss`,
so a token issued for customer X also passes authentication at customer Y. Isolation would then depend only on Y's
Dataverse binding, including the E11 gap. This is the "one credential, every customer" pattern D-13 §2 rejects. It also
cannot pass the POML step-3 probe ("refuses one for another stamp's"). **Rejected.**

**Option 2: one CIAM API app per customer, registered in `spaarkeextid`.** This shape is documented, and audience
isolation holds. But each customer gets a second app in a second tenant. L2 must hold app-write rights in the CIAM
tenant, and the keyless provisioner in (c) still needs a Spaarke-tenant multitenant app with a service principal in
`spaarkeextid`. That is the same cross-tenant mechanism as option 3, with more objects and no gain in isolation.
**Fallback only**, if S1 shows external tenants will not issue user tokens for a foreign resource app.

**Option 3 (recommended): the customer's own BFF app (H3, D-13) is also its CIAM audience.** It is already multitenant
(`EntraAppRegOptions.cs:57`) and already trusts the stamp UAMI (FIC). Provision its service principal into `spaarkeextid`.
The shared CIAM SPA client then requests `api://{bffAppId}/user_impersonation`, and `spaarkeextid` issues a token with
`aud = {bffAppId}` and `iss = https://{tid}.ciamlogin.com/{tid}/v2.0`.
- **Validation.** The stamp sets `Ciam:Audience = {bffAppId}`, which makes the audience unique per customer. `Ciam:TenantId`
  and `Ciam:Instance` are the shared CIAM tenant's values, so the issuer is checked as today. Small code change: accept
  both `{appId}` and `api://{appId}` (`ValidAudiences`), as the workforce scheme does.
- **Isolation.** A token for X fails audience validation at Y and returns 401 before any handler runs. The workforce
  scheme sees a CIAM issuer and rejects it, so a CIAM token for X reaches only X's `Ciam` and `ExternalCollaboration`
  endpoints. That negative case goes in the S1 test and in the H13 probe.
- **Limits.** One service principal per customer in `spaarkeextid`; no extra app objects. No FIC-count pressure: one FIC
  per app, and the app is per customer. The SPA client needs no per-customer consent, because H3 adds it to each BFF
  app's `preAuthorizedApplications`, the same list T240a already sets.
- **To verify in S1.** The BFF app may need `api.requestedAccessTokenVersion = 2` for CIAM to issue v2 tokens. That also
  changes the format of workforce tokens: Identity.Web accepts both versions, but the change is tested.

## 4. (b) Contact-to-customer routing, and how a BFF refuses another customer's contact

**Directory or invitation.** The T240c directory resolves workforce users through `sprk-{customerId}-users` group
membership. CIAM contacts are in no such group (R3 §4.6 agrees). A contact's link to a customer is created when that
customer invites them, so the **invitation link** carries the customer:
- H4b sets `ExternalAccess__PortalUrl = https://external.spaarke.com/?customer={customerId}`. The exact query form is
  R3's choice.
- The SPA signs in to the shared CIAM authority, which is fixed at build time. It then calls the **T240c directory's CIAM
  lookup** with the key and gets `{customerId, displayName, apiBaseUrl, ciamScope}` for that one key. It then silently
  acquires a token for `ciamScope` and calls `apiBaseUrl`.
- The SPA keeps the keys it has seen in `localStorage`, giving the user a customer picker. On a new device the contact
  uses the invite link again; the email can be re-sent.
- **The link never carries `apiBaseUrl` or the scope.** R3's draft encodes `{customerId, apiBaseUrl}`. A crafted link
  could then make the SPA mint customer X's token and send it to an attacker's host. The host and scope must come from
  Spaarke-owned data.
- The directory's CIAM lookup needs a CIAM sign-in, because every API endpoint requires auth. That means one shared
  directory audience in `spaarkeextid`, an operator one-time step. It returns routing data only, so it does not conflict
  with D-13.

**How customer Y's BFF refuses a contact of customer X**:
1. A token for X's audience fails audience validation at Y: 401 (§3).
2. Any CIAM user can still get a token for Y's audience, because the SPA client is pre-authorized for every customer. Y
   then resolves the `oid` against **Y's** Dataverse. With no binding, Y returns 403 `contact_not_found` and no records.
3. **Gap, owned by UAC-r2.** E11 email-binds an unbound Y contact with a matching email even when Y never invited that
   person (`ContactBindingDecision.cs:659`). This breaks the POML constraint "grants nothing at Y unless Y bound that
   contact itself". **Fix:** the invite writes a pending-invite marker (plane External, no oid) *before* it creates the
   CIAM account. E11 then binds only a contact that carries that marker. The same marker covers a person who already
   has an account (see (c)).

## 5. (c) Keyless CIAM provisioner (no Key Vault certificate)

- **Credential.** The stamp UAMI presents its token (MI-FIC) to the customer's BFF app (Spaarke tenant, multitenant, FIC
  already set by H3) against the authority of `Ciam:TenantId`. This is client credentials for Graph `/.default` in
  `spaarkeextid`. In code, `CiamGraphClientFactory` gets its client from
  `IConfidentialClientProvider.GetClientAsync(Ciam:TenantId, bffAppId)` (`OrderedCredentialClientProvider.cs:211`).
  Stamps set the credential order to `ManagedIdentityFederated` only, as the Graph client already does
  (`manifest.yaml:732`). The certificate branch survives only where the order lists it (dev, until it migrates). There
  is no new credential code.
- **Permission in `spaarkeextid`.** The BFF app's service principal gets the Graph application role **`User.Create`**,
  not `User.ReadWrite.All`. With `User.Create` a compromised stamp can create accounts, but it cannot read, disable,
  delete or re-key other customers' contacts. With `User.ReadWrite.All` it could read every customer's outside-counsel
  list and disable or delete their accounts.
- **Someone who already has an account** (invited earlier by another customer). `POST /users` conflicts on the email
  identity. The invite then keeps the contact pending (the §4 marker) and sends a "sign in with your existing Spaarke
  account" email. The person's first sign-in to this customer binds the oid through the marker-gated E11. No
  `User.Read.All` is needed, so no stamp can list the directory. S1 confirms the conflict error code. One person has one
  CIAM account, and each customer's Dataverse holds its own binding.
- **Idempotent.** A re-invite of a bound contact is skipped (today's `AlreadyProvisioned`). A re-invite of a pending one
  re-sends the email. Re-running provisioning finds the existing SP, role assignment and settings, and writes nothing.

## 6. What provisioning sets per stamp

**H4b `per_env_settings`** (all plain app settings; nothing in Key Vault):

| Key | Source |
|---|---|
| `Ciam__Instance` | literal `https://spaarkeextid.ciamlogin.com/` (platform value, one for all stamps) |
| `Ciam__TenantId` | literal `7052feba-bfc4-43e0-b09e-65014b429131` (platform) |
| `Ciam__Domain` | literal `spaarkeextid.onmicrosoft.com` (the issuer for local-account identities) |
| `Ciam__Audience` | `from-h3-output:bff_app_client_id` (the customer's own BFF app) |
| `Ciam__GraphProvisioner__ClientId` | `from-h3-output:bff_app_client_id` |
| `ExternalAccess__PortalUrl` | `https://external.spaarke.com/?customer=` + `from-intake-parameter:customer_id` (needs a composed source, or the value is built in H4b) |

There is no `Ciam__GraphProvisioner__CertificateName` and no `Ciam__ClientId`. CORS for `external.spaarke.com` is T240a's.
If the platform values move later, they belong in L2 options, not intake.

**H3**: add the shared CIAM SPA client id to `PreAuthorizedClientAppIds`, which is a platform-wide list. Set
`requestedAccessTokenVersion = 2` if S1 requires it.

**New H-step, H3c "CIAM service principal"** (after H3, before H4b). L2 acts in `spaarkeextid` through its own
cross-tenant app (see §7):
- create or get the BFF app's service principal there;
- create or get its Graph `User.Create` app-role assignment.

Re-runs are idempotent: match by `appId`, and by principal plus role.

**H13**: a CIAM audience probe.
- L2's CIAM app takes an app-only CIAM token for this stamp's audience. The stamp must return anything but 401; 403 is
  expected, because the token has no contact.
- L2 takes a token for a sentinel *other* audience. The stamp must return 401.
- A CIAM token on a workforce-only route must return 401.

**Tests**:
- unit: the settings catalog, the H3c planners, the CIAM provisioner's credential selection;
- BFF: the audience negative case, and marker-gated E11 (UAC-r2's);
- an acceptance-criterion test that no stamp vault holds a CIAM certificate or secret.

## 7. Operator one-time steps in `spaarkeextid` (each a live action the owner approves)

1. Register **"Spaarke Provisioning (CIAM)"**, an L2-owned app, in Spaarke's tenant. It is multitenant and has a FIC from
   the L2 UAMI. Admin-consent it in `spaarkeextid` with Graph `Application.ReadWrite.All` and
   `AppRoleAssignment.ReadWrite.All`, which H3c needs. Its blast radius is the CIAM tenant, the same posture as L2 in
   Spaarke's tenant (H10).
2. Create the **production CIAM SPA client** in `spaarkeextid` (single-tenant). Its redirect URI is
   `https://external.spaarke.com/...`. R3 owns its config; provisioning needs only its client id.
3. Register the **directory's CIAM API app** in `spaarkeextid` and pre-authorize the SPA client on it (T240c).
4. Tenant settings stay as they are: sign-up off, SSPR email OTP on.

## 8. Spike S1: the gate before step 2 (owner-approved live actions, in dev)

Use the dev BFF app (multitenant, FIC'd), or a throwaway multitenant app plus a UAMI.
- (i) Provision its service principal into `spaarkeextid`, grant `User.Create`, take an MI-FIC client-credentials token
  for `spaarkeextid`, and create a test local account. Check which authority works: `ciamlogin.com` or
  `login.microsoftonline.com`.
- (ii) `POST` the same email again and record the conflict error.
- (iii) The SPA client gets a user token for `api://{thatApp}/user_impersonation`. Record `aud`, `iss` and `ver`.
- (iv) A token for app A is rejected by a BFF configured for app B, and by the workforce scheme.

**Outcomes:**
- (i) fails: no keyless path exists within Microsoft's rules. Stop and take it to the owner, because the alternatives are a
  stamp certificate (against D13 and A6) or a Spaarke-side broker.
- Only (iii) fails: use option 2 for the audience; (c) is unchanged.

## 9. Open decisions

| # | Decision | Owner |
|---|---|---|
| 1 | Accept option 3 (the BFF app doubles as the CIAM audience, subject to S1), with option 2 as the fallback | owner |
| 2 | Approve S1 and the §7 one-time live actions in `spaarkeextid` | owner |
| 3 | Pending-invite marker, marker-gated E11, and the invite's "already has an account" branch (a contract and schema change to the 141 binding model) | unified-access-control-r2, then the owner |
| 4 | Invitation-link format, the SPA's customer picker and cold-landing UX, the production SPA client, and dropping `apiBaseUrl` from R3's deep-link draft | spaarke-SPA-external-access-platform-r3. R3 says its auth items are on hold for `spaarke-auth-system-of-record-r1` (R3 `design.md` §4.6) |
| 5 | The T240c directory gains a CIAM-authenticated lookup by customer key | this project (T240c); the owner approved the service |
| 6 | Production uses `spaarkeextid`, today recorded under `dev` in `config/environments.json`. When does the dev BFF's CIAM certificate and shared audience `4a4d5126` retire? | owner |
| 7 | Least privilege: `User.Create` instead of today's `User.ReadWrite.All` (the dev provisioner keeps its grant until migrated) | owner (security sign-off, `tenant-isolation` tag) |

## 10. Recommendation

**Each customer's own BFF app registration (D-13) also serves as that customer's CIAM audience and its CIAM Graph
provisioner.**
- **Audience.** Its service principal is provisioned into `spaarkeextid`, and the stamp sets
  `Ciam:Audience = Ciam:GraphProvisioner:ClientId = {bffAppId}`.
- **Credential.** The stamp signs in to the CIAM tenant with the MI-FIC it already has, as Graph `User.Create`. It holds
  no certificate or secret.
- **Routing.** Contacts reach the right stamp through an invitation link that carries only the customer key. The SPA
  resolves the key through the T240c directory's CIAM lookup.
- **Refusal.** A stamp refuses another customer's contacts by audience (401) and by oid binding in its own Dataverse
  (403). E11 is narrowed to contacts this stamp invited.
- **Gate.** Spike S1 must pass before step 2.

Alternatives rejected:
- **One shared CIAM API audience**: a token for X authenticates at Y, the "one credential, every customer" pattern D-13
  rejects.
- **A per-customer API app in `spaarkeextid`**: doubles the objects across two tenants and adds no isolation. Kept only
  as the fallback if S1 (iii) fails.
- **Stamp MI federating straight into a CIAM-tenant app**: Microsoft requires the MI and the app to be in the same tenant.
- **Keep the Key Vault certificate**: violates D13 / ADR-028 A6.
- **One shared provisioner app trusting every stamp MI**: capped at 20 FICs per app, and one credential for every customer.
- **A central oid→customers index for routing**: puts a cross-customer record of who represents whom in shared storage,
  and adds a write path from every stamp.
- **apiBaseUrl/scope in the link, or derived by naming convention**: crafted links, or squatted `*.azurewebsites.net`
  names, can leak a customer's token.
- **`appRoleAssignmentRequired` with per-contact assignment**: stamps would need `AppRoleAssignment.ReadWrite.All` in the
  CIAM tenant, which is enough to take the tenant over.
- **`User.ReadWrite.All` or `User.Read.All` on stamps**: any one stamp could read, disable or delete every customer's
  contacts.
