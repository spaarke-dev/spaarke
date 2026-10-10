# FR-12 spike — Entra B2B self-service sign-up vs a custom join flow

| | |
|---|---|
| Task | 003 (FR-12). Gates task 056 (join page) and task 060 (registration service) |
| Date | 2026-10-10 |
| Rigor | STANDARD (research spike, no production code). Model tier: opus |
| Code basis | `origin/master` @ `1b7ea7f48`. The cited files are identical in this worktree (`e6c95c007`) |
| Platform basis | Microsoft Learn, retrieved 2026-10-10 (§2). Only Learn pages are cited as platform fact |
| Live reads | None. Read-only checks that would settle open points are listed in §8; none was run |
| Status | **Recommendation for the owner.** Owner decision: **pending** (§10). It is not yet in `notes/decisions.md` |

## 0. Decision in brief

**Recommendation: build the custom join flow (Option B, as spec'd in FR-13/FR-14), with three refinements (§7).**

Entra's built-in B2B self-service sign-up flow **cannot enforce the core FR-14 rule**. That rule is "home tenant ∈ that customer's `CustomerTenantIds`, and `acct = 0`".

- For a Microsoft Entra account, the API connector receives the user's `email` and the collected attributes. It does not receive the home tenant id (P4, P6). It receives no customer context either (P4).
- The flow cannot add the new guest to `sprk-{customerId}-users` (P3).
- The API connector must authenticate with HTTP Basic or a client certificate. That is a new secret or a new certificate, which conflicts with NFR-03 (P5).

The built-in flow's one real advantage is that no Spaarke identity needs `User.Invite.All`, because Entra creates the guest itself (P1). That advantage does not outweigh the three gaps.

**No escalation.** The escalation trigger in task 003 is not hit. Option B enforces the rule with a managed identity (no secret), and it keeps `User.Invite.All` off every stamp.

**New customer prerequisite.** Option B adds one prerequisite that the built-in flow avoids. The customer's users must be able to consent to a Spaarke sign-in app in their home tenant, or the customer's admin consents once (P16; §9 Q2).

## 1. Requirements checklist (FR-13, FR-14, FR-15 + binding constraints)

Source: `spec.md` §C7 (FR-12–FR-15), NFR-02, NFR-03, NFR-07; project `CLAUDE.md` §2 "Hard limits".

| ID | Requirement |
|---|---|
| **R13.1** | One link per customer: `https://external.spaarke.com/join?customer={key}` |
| **R13.2** | The user signs in with their home company account through a registration-only client that reads only their profile |
| **R13.3** | The flow then hands off to the normal workforce sign-in (Spaarke-tenant authority) |
| **R13.4** | Acceptance: a new employee of an allow-listed company goes from link to signed-in SPA in one flow, with no admin step |
| **R14.1** | Verify that the caller's **home tenant ∈ that customer's `CustomerTenantIds`** — before anything is created |
| **R14.2** | Verify **`acct = 0`** (a member of the home tenant, not someone else's guest) |
| **R14.3** | Invite them as a B2B guest into Spaarke's tenant: no email, no licence |
| **R14.4** | Add the guest to `sprk-{customerId}-users` |
| **R14.5** | Idempotent: registering again does not create a duplicate |
| **R14.6** | `User.Invite.All` and group write are held only by a shared Spaarke-tenant service — never by a stamp identity |
| **R14.7** | Acceptance: an allow-listed member is registered automatically. A non-listed tenant, or someone else's guest, is refused with a clear message |
| **R15.1** | `WorkforceMembershipTest` gains the branch `acct = 1` ∧ `tid` = stamp tenant ∧ `idp` ∈ `CustomerTenantIds` → `Member`. Order and every existing deny code are kept |
| **R15.2** | The binder email-binds or creates exactly one contact, keyed by the guest `oid`. A guest from a non-listed home tenant is denied (`workforce_tenant_not_customer`). Spaarke staff are unaffected |
| **X1** (NFR-03) | No new secret anywhere; no `ClientSecret`. The registration service uses MI / a federated credential |
| **X2** (NFR-02) | No token goes to a host or scope taken from the link; the customer key only names the customer |
| **X3** (PRQ-C-14) | The customer's cross-tenant settings allow outbound B2B collaboration to Spaarke's tenant |

## 2. Platform facts (Microsoft Learn, retrieved 2026-10-10)

| # | Fact | Source (page "updated" date) |
|---|---|---|
| P1 | A self-service sign-up flow lets external users "sign up for specific applications themselves" instead of being invited. On completion "an account is provisioned for the user in the directory". The B2B example "creates a new guest account". "Multiple applications can use the same user flow." Flows can be attached only to apps your organization builds ("can't be used for Microsoft apps, like SharePoint or Teams") | https://learn.microsoft.com/en-us/entra/external-id/self-service-sign-up-overview (2026-06-15) |
| P2 | The feature needs the tenant toggle **Enable guest self-service sign up via user flows**, set by at least a User Administrator. "Microsoft Entra ID is the default identity provider for self-service sign-up". "You can associate one or more applications with a single user flow." Attributes are collected only at first sign-up | https://learn.microsoft.com/en-us/entra/external-id/self-service-sign-up-user-flow (2026-03-27) |
| P3 | An API connector can run at two points only: "After federating with an identity provider during sign-up" and "Before creating the user". Both run "during user **sign-up**, not sign-in". The second is "always invoked before a user account is created". **There is no step after the user is created** | https://learn.microsoft.com/en-us/entra/external-id/api-connectors-overview (2026-04-17) |
| P4 | The request body carries user attributes, custom `extension_…` attributes and `ui_locales`. The `identities` claim is annotated "Sent for Google, Facebook, and Email One Time Passcode identity providers". "Only user properties and custom attributes listed in … Custom user attributes … are available to be sent". The API can answer `Continue` (and may override attributes), `ShowBlockPage` (with `userMessage`) or `ValidationError`. Timeout: 20 s, then one retry. The documented payload has **no tenant id, no client id and no request parameter** | https://learn.microsoft.com/en-us/entra/external-id/self-service-sign-up-add-api-connector (2026-02-06) |
| P5 | "You must protect your REST API endpoint with authentication … by using either HTTP basic authentication or HTTPS client certificate authentication." "Configuration for basic or certificate is always required." The certificate is uploaded as a `.pfx` with its private key and password, and renewed by hand | https://learn.microsoft.com/en-us/entra/external-id/self-service-sign-up-secure-api-connector (2026-02-06) |
| P6 | Custom approvals: the example payload for a **Microsoft Entra** user carries only `email`, `displayName`, attributes and `ui_locales` — **no `identities`**. "For a federated Microsoft Entra user … you must use the invitation API to create the user". The sample approval app uses a **client secret** and `User.ReadWrite.All` | https://learn.microsoft.com/en-us/entra/external-id/self-service-sign-up-add-approvals (2026-02-06) |
| P7 | `POST /invitations`: the least-privileged application permission is `User.Invite.All`. It creates a user with `userType` `Guest`, who must redeem. The response carries `inviteRedeemUrl`. "Application permissions (app-only) don't work if B2B invitations are disabled on the tenant" | https://learn.microsoft.com/en-us/graph/api/invitation-post?view=graph-rest-1.0 (2025-07-30) |
| P8 | A guest can redeem through a direct, tenant-specific app link; "the link still guides them through the first-time consent experience" (the Review permissions page). Redemption finds the user's home directory first (managed Entra account). Automatic redemption removes the consent prompt only when both tenants set it. "Guests who use an alias of the email address that was invited need an email invitation" | https://learn.microsoft.com/en-us/entra/external-id/redemption-experience (2026-04-22) |
| P9 | By default B2B collaboration is enabled. Outbound settings "control whether your users can access resources in an external organization". Org-level allow or block needs no premium licence; targeting specific users, groups or apps needs P1 | https://learn.microsoft.com/en-us/entra/external-id/cross-tenant-access-overview (2026-03-27) |
| P10 | "Both allow/block list and cross-tenant access settings are checked at the time of invitation." Inbound **Allow access** lets the specified external users "be invited for B2B collaboration" | https://learn.microsoft.com/en-us/entra/external-id/cross-tenant-access-settings-b2b-collaboration (2026-04-24) |
| P11 | External ID uses MAU billing for every `UserType = Guest` user (B2B collaboration in workforce tenants), with a free tier. The tenant must be linked to an Azure subscription. No per-guest licence | https://learn.microsoft.com/en-us/entra/external-id/external-identities-pricing (2026-06-19) |
| P12 | Optional claim `acct`: "If the user is a member of the tenant, the value is `0`. If they're a guest, the value is `1`." The `email` claim "isn't guaranteed to be correct … never use it for authorization" | https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims-reference (2026-08-20) |
| P13 | `idp` "Records the identity provider that authenticated the subject … identical to … Issuer unless the user account isn't in the same tenant as the issuer, such as guests." `tid` = "the tenant that the user is signing in to" | https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference (2026-07-24) |
| P14 | An app can trust a **user-assigned** managed identity as a federated credential. The app and the MI "must belong to the same tenant". To reach another tenant, the app "must be a multitenant application and provisioned into the other tenant". "Accessing resources in another tenant is supported." Max 20 FICs per app. Microsoft.Identity.Web supports `SignedAssertionFromManagedIdentity` | https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity (2026-09-29) |
| P15 | A managed identity's service principal can be given app roles directly (`POST /servicePrincipals/{mi}/appRoleAssignments`) | https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/assign-app-role-managed-identity-azure-cli (2026-02-19) |
| P16 | "By default, all users are allowed to consent to applications for permissions that don't require administrator consent." Microsoft recommends allowing user consent only for verified publishers. The built-in `microsoft-user-default-low` policy permits consent "only for apps from verified publishers … and only for permissions that you classify as low impact". If user consent is not allowed, an admin must consent (an admin consent workflow is available) | https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/configure-user-consent (2026-08-04) |
| P17 | Dynamic groups: "You can't manually add or remove a member of a dynamic membership group." Custom extension properties can be used in rules. A P1 licence is required "for each unique user who's a member of one or more dynamic membership groups" | https://learn.microsoft.com/en-us/entra/identity/users/groups-dynamic-membership (2026-08-13) |
| P18 | External collaboration settings hold the self-service sign-up toggle, the guest invite settings (who can invite) and the domain allow/block list for invitations | https://learn.microsoft.com/en-us/entra/external-id/external-collaboration-settings-configure (2026-04-25) |
| P19 | The PAYG Power Apps per-app meter counts "unique users who actually opened the app … regardless of how many users have access to it" | https://learn.microsoft.com/en-us/power-platform/admin/pay-as-you-go-meters (2026-09-22) |

Spaarke code facts (`origin/master` @ `1b7ea7f48`):

| # | Fact | Where |
|---|---|---|
| S1 | `CustomerTenantIds` is a **per-deployment (stamp) BFF setting**, `WorkforceIdentity__CustomerTenantIds__N`. If it is empty, everyone is denied | `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/WorkforceIdentityOptions.cs:29-34` |
| S2 | The member test runs in a fixed order (kind → list → tid → acct). "Membership is never inferred from an email domain or a `#EXT#` UPN" | `WorkforceIdentityOptions.cs:194-231` (comment `:196-197`) |
| S3 | H4b writes that list onto the stamp from intake (`CustomerWorkforceTenantsRule`) | `src/server/services/Sprk.Provisioning.ControlPlane.Worker/Program.cs:226-228` |
| S4 | The existing H11 invite client looks the user up with `mail eq`. An existing Guest is reused with no second email; a Member is refused. Otherwise it calls `POST /invitations`, today with `sendInvitationMessage: true`. Permissions: `User.Read.All` + `User.Invite.All` | `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/UserProvisioning/GraphRestB2BInvitationClient.cs:8-23`, `:178` |
| S5 | The existing H11 group client calls `POST /groups/{id}/members/$ref`. A 400 "already exist" counts as already a member. Permission: `GroupMember.ReadWrite.All` | `…/UserProvisioning/GraphRestEnvironmentSecurityGroupClient.cs:7-12` |
| S6 | `sprk-{customerId}-users` is the Dataverse **environment security group**, an assigned group that H11 adds members to directly. It "keeps another customer's guests out of this environment" | `…/UserProvisioning/H11UserProvisioningHandler.cs:106-118`, `:644`; `docs/architecture/SPAARKE-ENVIRONMENT-RESOURCE-INVENTORY.md:80-81` |
| S7 | A pre-bound guest resolves through an existing `oid` binding | `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ContactIdentityBinder.cs:160-163` |
| S8 | T240c directory (designed, not built). It routes a workforce caller by the `sprk-*-users` **group claims in the token** (no Graph call). It is hosted in `rg-spaarke-shared-prod` with an MI "that can only read the registry", not on L2 | `projects/customer-provisioning-orchestration-r1/notes/t240-plan.md:43-55` |
| S9 | PRQ-C-14 is a skill-local customer attestation, `customerOutboundB2BAttested` | `.claude/skills/provision-environment/SKILL.md:1073-1075` (master) |

## 3. Option A — Entra built-in B2B self-service sign-up user flow

**How it would be wired**
1. Turn on the tenant toggle in Spaarke's tenant (P2).
2. Create a `B2X_1_…` user flow with Microsoft Entra ID as the only identity provider (P2).
3. Associate it with an app (P2).
4. Add API connectors at "after federating" and "before creating the user" (P3).
5. The join link starts that app's sign-in. An unknown user gets the sign-up path. After a `Continue` response, Entra creates the guest and returns a token to the app (P1, P6).

**What it cannot do**

| Gap | Evidence |
|---|---|
| **No home tenant id at the decision point.** For Entra accounts, `identities` is not sent; the connector sees `email` and attributes only. The only test it could run is "email domain ∈ list". That is weaker than a tenant id, Learn says not to use `email` for authorization, and Spaarke's member test forbids it | P4, P6, P12; S2 |
| **No `acct`.** No membership signal reaches the connector | P4 |
| **No customer context.** The payload carries no client id and no request parameter, so the connector cannot know which customer's link was used. The only workarounds are both poor. (a) One app registration + one user flow + one API connector (with `?customer=` baked into its URL) **per customer**. That is three new tenant objects per customer, outside provisioning today. (b) Ask the user to type a customer key | P2, P4 |
| **No group add.** There is no connector step after user creation. A dynamic group cannot replace `sprk-{customerId}-users`: that group is assigned and H11 adds to it directly, and members cannot be added by hand to a dynamic group. A dynamic group also costs a P1 per member | P3, P17; S6 |
| **A new credential.** The connector must use Basic (username + password) or a client certificate (`.pfx` + password, renewed by hand). Either is a new secret or certificate under NFR-03 | P5 |
| **Open sign-up surface.** Any Entra user can start sign-up on the associated app. If that app is the shared workforce client, every `external.spaarke.com` sign-in shows a sign-up path, and the only filter is the connector's email-domain check | P1, P2 |

**What it does well:** Entra creates the guest itself, so **no `User.Invite.All` anywhere** (P1). There is no home-tenant app consent, because B2B authenticates the user at home and issues the token from Spaarke's tenant (P8). Existing users simply sign in, since connectors run only on sign-up, which makes it idempotent (P3). Guests are billed by MAU with no licence (P11).

**Variant A2 — the flow with no connector, plus a post-sign-in registration step.** This removes the secret. After Entra creates the guest, a Spaarke service checks `idp` ∈ that customer's list from the first Spaarke-tenant token and adds the group (P13).

It reverses FR-14's order: **the guest exists before anything is checked.** Any Entra user worldwide can create a guest in Spaarke's tenant through the associated app, and a refused user leaves a guest behind. Deleting that guest needs a Graph permission stronger than `User.Invite.All` (*ASSUMPTION: `User.DeleteRestore.All` or `User.ReadWrite.All`; not verified*). The variant still needs the shared service with `GroupMember.ReadWrite.All`.

Spaarke could tighten inbound cross-tenant access to "block by default, allow each customer tenant" (P9, P10). That is a tenant-wide change affecting all of Spaarke's B2B collaboration. *ASSUMPTION: inbound cross-tenant settings also gate self-service sign-up. Learn states this only for invitations (P10).* So A2 is not simpler than B, and it is weaker.

## 4. Option B — custom join page + shared registration service (spec design, refined)

**Flow**
1. **Join page** (R3 task 056, SWA `external.spaarke.com`): `/join?customer={key}`. The key only names the customer. The registration service URL is fixed Spaarke configuration and is never read from the link (X2).
2. **Home-tenant sign-in (registration-only client).** The page redirects to the registration service's sign-in endpoint with the key. The service runs a server-side OIDC authorization-code flow (with PKCE) against `https://login.microsoftonline.com/organizations`. It uses one **multitenant** app registration in Spaarke's tenant, "Spaarke Registration", that requests only `openid profile` (R13.2). The `acct` optional claim is configured on its ID token (P12). The service authenticates to the token endpoint with an **MI-FIC client assertion**, so there is no secret (P14, X1). The ID token comes straight from Entra's token endpoint, never through the browser.
3. **Decide** (R14.1, R14.2). The service resolves the key to `{CustomerTenantIds, usersGroupId}` from the Spaarke registry. It then applies the same fail-closed order as `WorkforceMembershipTest`: user token → list not empty → `tid` ∈ list → `acct` present and `= "0"` (S2).
   - A missing or other `acct` is refused.
   - A non-listed tenant or `acct = 1` gets a clear refusal page (R14.7).
4. **Invite** (R14.3, R14.5). Reuse H11's lookup-then-invite pattern (S4) with `sendInvitationMessage: false` and `inviteRedirectUrl = https://external.spaarke.com/?customer={key}` (P7).
   - An existing Guest is reused.
   - An existing Member is refused.
   - The invite address comes **only** from the verified home ID token, never from client input (§6 F4).
5. **Group** (R14.4). Reuse H11's group client (S5), where "already a member" counts as success. Check that the target group id is the one the registry gives for that customer, and that it is named `sprk-{customerId}-users` (S6).
6. **Hand-off** (R13.3). Redirect to the SPA, which runs the normal workforce sign-in (Spaarke-tenant authority, `login_hint`). On first sign-in the guest redeems through the direct link and sees the one-time Review permissions page (P8), unless both tenants turn on automatic redemption (P9). The group was added before the first Spaarke-tenant token was issued, so the T240c directory sees the group claim on the first call (S8). *ASSUMPTION: the claim appears on the first token; verify in the task 060 end-to-end test.*
7. **Contact** (R15.1–R15.2, stamp side). The token has `tid` = Spaarke, `acct = 1` and `idp` = the home-tenant issuer (project gotcha 2026-10-08; P13). The FR-15 branch → `Member` → the binder creates or binds the contact.

**Identities and permissions (input to task 060)**

| Identity | Kind | Permissions | Notes |
|---|---|---|---|
| `uami-spaarke-registration` (name indicative) | User-assigned MI in Spaarke's tenant | Microsoft Graph application roles assigned directly to the MI (P15): `User.Invite.All`, `User.Read.All` (lookup, S4), `GroupMember.ReadWrite.All` (S5). Read access to the registry | **Separate from the T240c directory MI**, which by design only reads the registry (S8). Never on a stamp (R14.6) |
| "Spaarke Registration" app | Multitenant app registration in Spaarke's tenant (web redirect on the service host) | Delegated `openid profile` only; optional claim `acct` (ID token) | Credential = **federated identity credential → `uami-spaarke-registration`** (P14). No secret, no certificate. Needs publisher verification if customers restrict consent to verified publishers (P16) |
| Spaarke tenant settings | — | Guest invite settings must not disable invitations, or app-only invites fail (P7, P18). The self-service sign-up toggle stays **off** | Read-only checks in §8 |

**Hosting (ADR-052, decided in task 060)**
- Recommended: a minimal shared service in `rg-spaarke-shared-prod`, alongside the T240c directory. It can be the same process or a sibling, but it **has its own MI**.
- Not the stamp BFF. A stamp must never hold `User.Invite.All` (R14.6).
- Not the L2 control plane. L2 holds Owner on every customer subscription, and end-user endpoints do not belong there (the same reasoning as S8).
- If the shared host is not ready, the spec's fallback applies: FR-14 moves to R4, and C7 ships the join page plus FR-15 behind a flag (spec §Unresolved Questions).

**Registry dependency.** Today `CustomerTenantIds` exists only as stamp app settings written from intake (S1, S3). The shared service needs the same list per customer key in the Spaarke registry, next to `sprk_securitygroupid` (S8). That is a registry field (§9 Q4).

## 5. Requirement matrix

| Req | A — built-in flow + API connector | B — custom join + shared service |
|---|---|---|
| R13.1 per-customer link | **Not met** — no customer context reaches the connector. Possible only with one app + flow + connector per customer (P2, P4) | **Met** — the key names the customer; the decision uses Spaarke data (§4.1, §4.3) |
| R13.2 home account, profile only | **Met** — Entra is the default IdP (P2) | **Met** — `openid profile` only (§4.2; P14). Needs home-tenant consent (P16) |
| R13.3 hand-off to the workforce sign-in | **Met** — the flow returns a token to the app (P1) | **Met** — redirect, then normal sign-in; one consent page at redemption (P8) |
| R13.4 one flow, no admin step | **Not met** — the guest is created but not grouped, so the directory cannot route it (P3; S8) | **Met** once PRQ-C-14 and the consent prerequisite hold (P8, P16) |
| R14.1 home tenant ∈ list, checked first | **Not met** — no tenant id in the payload; email domain only (P4, P6, P12; S2) | **Met** — `tid` from the home ID token, checked before the invite (§4.3; P13) |
| R14.2 `acct = 0` | **Not met** — not in the payload (P4) | **Met** — `acct` optional claim (P12) |
| R14.3 guest, no email, no licence | **Met** — Entra creates the guest; MAU billing (P1, P11) | **Met** — `sendInvitationMessage: false`; MAU billing (P7, P11) |
| R14.4 add to `sprk-{customerId}-users` | **Not met** — no post-create step; a dynamic group cannot replace the assigned env group (P3, P17; S6) | **Met** — `members/$ref` (S5) |
| R14.5 idempotent | **Met** — connectors run only on sign-up (P3) | **Met** — lookup-then-reuse, "already member" = success (S4, S5) |
| R14.6 invite permission off stamps | **Met** — no invite at all (P1) | **Met** — the shared MI only (§4 table) |
| R14.7 clear refusal | **Partly** — `ShowBlockPage` + `userMessage` (P4), but only on an email-domain basis | **Met** — the service's own refusal page |
| R15.1–R15.2 stamp member-test branch + contact | **Met** — same FR-15 code in both. In A it becomes the *only* tenant gate (S2, S7; P13) | **Met** — same FR-15 code (S2, S7; P13) |
| X1 no new secret | **Not met** — connector Basic or certificate is always required (P5) | **Met** — MI-FIC + MI app roles (P14, P15) |
| X2 no routing from the link | **Met** (the link only starts sign-in) | **Met** (fixed service URL; §4.1) |
| X3 PRQ-C-14 | Needed (P9) | Needed (P9) |

**Score (15 rows; X3 is a prerequisite both options share):** A meets 7, partly meets 1 and fails 6. The failures include the two checks that define the feature (R14.1/R14.2) and NFR-03. B meets all 14 non-prerequisite rows. B depends on one new customer prerequisite (consent) and one registry field.

## 6. Comparison

**Moving parts**

| | A (built-in) | B (custom) |
|---|---|---|
| Tenant objects | Tenant toggle, user flow, 1–2 API connectors, an associated app — **per customer** if R13.1 is required | 1 multitenant app (+ FIC), 1 UAMI with 3 Graph roles — **shared by all customers** |
| Code | Connector endpoint (decision only) + a post-sign-in group-add service anyway | One shared service (sign-in callback, decision, invite, group add), reusing the H11 patterns (S4, S5) |
| Credentials to rotate | Connector password or certificate (P5) | None (P14, P15) |
| Customer-side prerequisites | PRQ-C-14 | PRQ-C-14 + consent to "Spaarke Registration" (user consent or a one-time admin consent; P16) |

**Failure modes**

| Case | A | B |
|---|---|---|
| F1 Already a guest in Spaarke's tenant (U2 licensed staff or re-registration) | Plain sign-in, no connector call (P3). Group not ensured | Lookup finds the Guest → reuse → ensure group → hand-off (S4, S5) |
| F2 Home tenant not listed | Can only be caught by email domain. Otherwise a guest is created and the stamp denies it later (`workforce_tenant_not_customer`), leaving a guest behind | Refused before any write (§4.3) |
| F3 Someone else's guest (`acct = 1`) | Not detectable at the connector (P4) | Refused (`acct ≠ 0`). With `/organizations` the home sign-in normally yields `acct = 0` — *ASSUMPTION; the check stays as a fail-closed guard* |
| F4 Listed user registers someone else's address | Not applicable | Prevented: the invite address comes only from the verified token. Even if it were spoofed, the stamp's `idp` check denies a foreign home tenant (R15). But group membership alone would let the directory route them, so the server-side derivation is mandatory |
| F5 Customer blocks user consent | Not affected (B2B needs no home-tenant app consent) | Users see an admin-approval prompt until the customer's admin consents (P16) → prerequisite §9 Q2 |
| F6 Customer blocks outbound B2B | Sign-up/redemption fails (P9) | Same (P9, P10) |
| F7 Invitations disabled in Spaarke's tenant | Not affected | App-only invite fails (P7) → read-only check §8 |
| F8 Email alias ≠ invited address | Not applicable | Direct-link redemption needs the invited address (P8) → invite with the address from the home token. *ASSUMPTION: `preferred_username`/UPN redeems correctly; prove it with the "Spaarke Test Partner" tenant (t240-plan.md) in task 060* |

**Who hosts what**

| Piece | A | B |
|---|---|---|
| Join page | SWA (task 056) — a link that starts app sign-in | SWA (task 056) |
| Sign-up / sign-in UI | Entra-hosted user-flow pages | Entra (home tenant), then the Entra redemption page |
| Allow-list decision | Spaarke-hosted public HTTPS connector endpoint | The shared registration service |
| Guest creation | Entra | The shared registration service (Graph invitations) |
| Group add | Still needs a Spaarke service | The shared registration service |
| Contact | Stamp BFF (FR-15) | Stamp BFF (FR-15) |

## 7. Recommendation

**Adopt Option B**, the FR-13/FR-14 design, with these refinements.

1. **Registration client = a server-side confidential web client with an MI-FIC credential**, requesting only `openid profile` + `acct`. It is not a browser-held token sent to an API. This satisfies "reads only their profile" and NFR-03 (P12, P14).
2. **A dedicated UAMI** holds `User.Invite.All`, `User.Read.All` and `GroupMember.ReadWrite.All`, assigned directly as app roles (P15). Even when co-hosted with the T240c directory, it is separate from the directory MI (S8). Never on a stamp.
3. **Reuse the H11 Graph patterns** (S4, S5) with `sendInvitationMessage: false`. Task 060 decides under root `CLAUDE.md` §11 whether to extract or copy them.
4. **Keep FR-15 as specified.** It is the stamp-side gate, and it backs up refinement 1 if a registration is ever mis-issued.
5. Keep the self-service sign-up tenant toggle **off** in Spaarke's tenant.

Rejected:
- **A**, because it fails R14.1, R14.2, R14.4, R13.1 and X1.
- **A2**, because it creates guests before checking, opens Spaarke's tenant to sign-up by any Entra user, and still needs the same shared service.

**Inputs for task 060:** the §4 identities table, the hosting paragraph and the registry dependency. **Inputs for task 056:** the join page only builds `/join?customer={key}`, sends the user to the fixed registration URL, and renders the return or refusal states.

## 8. Assumptions and proposed read-only checks (not run)

Assumptions (all marked inline):
- **A-1** Inbound cross-tenant settings gate self-service sign-up. Learn states this only for invitations (P10).
- **A-2** Deleting a refused guest needs `User.DeleteRestore.All` or `User.ReadWrite.All` (A2 only).
- **A-3** The group claim appears on the first Spaarke token after the group add.
- **A-4** A `/organizations` home sign-in yields `acct = 0`.
- **A-5** An invite by `preferred_username` redeems by direct link.
- **A-6** The documented connector payload is complete: no undocumented tenant id. Settling this needs a live test user flow, which is a **live change** and needs owner approval. Not proposed, because the documented contract is what Spaarke may depend on.

Proposed read-only checks (Spaarke tenant; `Policy.Read.All` / `Group.Read.All`; print no secrets):

```bash
# Guest-invite restrictions (allowInvitesFrom must not be "none"; P7)
az rest --method GET --url "https://graph.microsoft.com/v1.0/policies/authorizationPolicy" --query "allowInvitesFrom"
# Self-service sign-up toggle (expect false; keep it off)
az rest --method GET --url "https://graph.microsoft.com/v1.0/policies/authenticationFlowsPolicy" --query "selfServiceSignUp"
# Default cross-tenant B2B inbound (context for A-1 / F6)
az rest --method GET --url "https://graph.microsoft.com/v1.0/policies/crossTenantAccessPolicy/default" --query "b2bCollaborationInbound"
# Confirm the environment groups are assigned, not dynamic (S6, P17)
az rest --method GET --url "https://graph.microsoft.com/v1.0/groups?\$filter=startswith(displayName,'sprk-')&\$select=displayName,groupTypes,securityEnabled"
```

## 9. Open questions for the owner

- **Q1 — Approve Option B as the FR-12 decision.** If approved, record it in `notes/decisions.md` and §10 below.
- **Q2 — New customer prerequisite.** The customer's users must be able to consent to "Spaarke Registration" (`openid profile`), or the customer's admin grants tenant-wide consent once (P16). Proposal: provisioning adds it next to PRQ-C-14 (`customerOutboundB2BAttested`, S9). Spaarke completes publisher verification so that customers on the "verified publishers only" policy can consent.
- **Q3 — Should workforce contacts join `sprk-{customerId}-users`?** That is the Dataverse **environment** security group (S6), and the stamp's Dataverse is pay-as-you-go. Group membership alone is not billed: the meter counts users who *open* an app (P19). A contact who opened a model-driven app in that environment would be counted, however. The alternative is a separate `sprk-{customerId}-contacts` group that the T240c directory also resolves, which changes FR-14 step 3 and the T240c design. Recommendation: keep FR-14 as written, and make sure contacts have no app entry point beyond the SPA. Confirm.
- **Q4 — Registry field.** The registration service needs each customer's `CustomerTenantIds` in the Spaarke registry, next to `sprk_securitygroupid` (S1, S3, S8). Who adds it: provisioning (T240c) or R3 (task 060)?
- **Q5 — Host.** Co-host with the T240c directory (separate MI; recommended) or use a dedicated function app. Decided in task 060 under ADR-052.

## 10. Owner decision

- **Decision:** _pending_ (to be recorded by the main session with the date and the owner's words).
- **Mirror in:** `notes/decisions.md`.

## 11. Findings in existing docs (for the main session)

- **Doc drift — contact creation.**
  - `notes/r3-auth-path.md:60` (D1) and `:74` (§5 step 4) say the registration service "creates their contact" and that "the member-test change becomes optional".
  - `design.md:259-261` says the same ("the member-test change in #1563 becomes optional").
  - Both contradict `spec.md` FR-15 (contact created on first sign-in through the member-test branch; owner 2026-10-09) and the project `CLAUDE.md` §3. The spec governs. Those two docs should be updated to match.
- **ADR tension for task 001.** ADR-028 A1 MUST (`.claude/adr/ADR-028-spaarke-auth-architecture.md:52`) says that when external self-registration is enabled, it MUST use "Entra External ID self-service sign-up user flows". That rule was written for CIAM partners. C7 self-registers *workforce contacts* as B2B guests with a custom flow. Task 001's A1 clarification should state that the rule covers the CIAM plane only, and cite this spike as the reason the B2B flow is not used for workforce contacts.

## 12. Sources

Most authoritative: the Learn pages P3–P6 (API connector contract and authentication) and P14 (MI-FIC). Together they decide the comparison.

- P1 https://learn.microsoft.com/en-us/entra/external-id/self-service-sign-up-overview — retrieved 2026-10-10
- P2 https://learn.microsoft.com/en-us/entra/external-id/self-service-sign-up-user-flow — retrieved 2026-10-10
- P3 https://learn.microsoft.com/en-us/entra/external-id/api-connectors-overview — retrieved 2026-10-10
- P4 https://learn.microsoft.com/en-us/entra/external-id/self-service-sign-up-add-api-connector — retrieved 2026-10-10
- P5 https://learn.microsoft.com/en-us/entra/external-id/self-service-sign-up-secure-api-connector — retrieved 2026-10-10
- P6 https://learn.microsoft.com/en-us/entra/external-id/self-service-sign-up-add-approvals — retrieved 2026-10-10
- P7 https://learn.microsoft.com/en-us/graph/api/invitation-post?view=graph-rest-1.0 — retrieved 2026-10-10
- P8 https://learn.microsoft.com/en-us/entra/external-id/redemption-experience — retrieved 2026-10-10
- P9 https://learn.microsoft.com/en-us/entra/external-id/cross-tenant-access-overview — retrieved 2026-10-10
- P10 https://learn.microsoft.com/en-us/entra/external-id/cross-tenant-access-settings-b2b-collaboration — retrieved 2026-10-10
- P11 https://learn.microsoft.com/en-us/entra/external-id/external-identities-pricing — retrieved 2026-10-10
- P12 https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims-reference — retrieved 2026-10-10
- P13 https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference — retrieved 2026-10-10
- P14 https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity — retrieved 2026-10-10
- P15 https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/assign-app-role-managed-identity-azure-cli — retrieved 2026-10-10
- P16 https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/configure-user-consent — retrieved 2026-10-10
- P17 https://learn.microsoft.com/en-us/entra/identity/users/groups-dynamic-membership — retrieved 2026-10-10
- P18 https://learn.microsoft.com/en-us/entra/external-id/external-collaboration-settings-configure — retrieved 2026-10-10
- P19 https://learn.microsoft.com/en-us/power-platform/admin/pay-as-you-go-meters — retrieved 2026-10-10
- Repo: `spec.md` §C7, `design.md` §C7 and §4.6 item 7, `notes/r3-auth-path.md`, `notes/coordination/2026-10-10-from-provisioning-t240d-s1-partial.md` (S1 PASS: keyless app-only Graph with MI-FIC), `.claude/adr/ADR-028-spaarke-auth-architecture.md` (A1, A4, A6), code files S1–S9. `knowledge/` holds no material on B2B self-service sign-up; the prior researcher notes cover CIAM only.
