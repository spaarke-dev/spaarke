> **Status, 2026-10-13: a draft, superseded in part by ADR-028 A9** (owner-confirmed; PR #1810). Under A9, workforce contacts sign in at their customer's home directory, through a multi-tenant Spaarke SPA client. **SPA-r3 owns** token acceptance, registration and the stored link to licensed users. Those sections here (and the option A appendix) are **inputs for SPA-r3, not UAC scope**. **UAC task 186 owns only baseline access** (section 5: the role as a ceiling). Where this note recommends "stay with A8 (path C)", A9 supersedes that.

# Task 186: Model 1 workforce sign-in, registration and baseline access (design note)

> **Status:** design for owner review. No code, config, Entra, Dataverse or tenant setting was changed.
> **Code basis:** `origin/master` @ `cd07fd7c3` (this worktree's branch is 932 commits behind master; every `file:line` below is master). Task 184 (#1801) is not on master yet.
> **Dev facts read (read-only, `az` login, spaarkedev1):** the Workforce role, its holders and privileges; `sprk_approlemodulemap`; guest systemusers. No Key Vault was read.
> **Frame (main session, 2026-10-13):** the owner's round-97 "option A" conflicts with ADR-028 **A8** (owner-approved 2026-10-10, already built by R3 in #1795). Part 1 is the A8-compliant design (§6.5 path C). The appendix gives option A (path B, amend A8) and what it costs.

---

## Part 1: the A8-compliant design (path C)

### 1. Today's token path for each client, and why Dewey Cheatham gets 401 IDX40001

| Client | Deployed on dev today | On master (not deployed) | Token `tid` | BFF result |
|---|---|---|---|---|
| External SPA, workforce realm | Build `885d0c5` (last `deploy-external-spa` run, 2026-10-08). Authority `https://login.microsoftonline.com/organizations` (`msal-config.ts:157` at that commit). Client `1e40baad`, scope `access_as_user` (`deploy-external-spa.yml:51-52` at that commit) | R3 task 040 (#1795): single-tenant "Spaarke Workspace" client `9f1da340`, tenant-pinned authority, `user_impersonation` (`.env.development:31-35`; `msal-config.ts:80` refuses anything else; `deploy-external-spa.yml:70-72,90-93` reads repo `vars`) | Deployed: the **home** tid (`bc3aa7f4…`), `acct = 0`. Master: Spaarke (`a221a95e…`), `acct = 1`, `idp` = home | Deployed: **401 IDX40001**. Master: accepted by the default scheme |
| External SPA in Teams | Pre-040 bundle: Teams SSO `getAuthToken` with `webApplicationInfo` = `1e40baad` (task 040 POML background) | NAA, then the MSAL popup, against Spaarke's tenant; no `webApplicationInfo` | Deployed: home. Master: Spaarke | As above |
| Word and Outlook add-ins | NAA with the Spaarke-tenant authority (`Spaarke.Auth/src/strategies/OfficeNaaStrategy.ts:416,447`; Spaarke tenant forced since #1453) | Same | Spaarke | Accepted. Calls `/api/office/*`, which uses OBO (`Api/Office/CommunicationsEndpoints.cs:44`) |

**The BFF side.** The default scheme is `AddMicrosoftIdentityWebApi(AzureAd)` (`AuthorizationModule.cs:55`), with `AzureAd:TenantId` set to Spaarke (`appsettings.template.json:38-43`). Microsoft.Identity.Web builds its AAD issuer validator from that tenant-specific authority, so the only issuer it accepts is `https://login.microsoftonline.com/a221a95e…/v2.0` (and its v1 `sts.windows.net` form). A home-tenant token has issuer `…/bc3aa7f4…/v2.0`. Its signature and audience pass; its issuer does not, so validation fails with **IDX40001** ("Issuer … does not match any of the valid issuers").
- The Ciam scheme (`:65-81`) rejects it as well.
- `ExternalCollaboration` = Ciam + default (`:445`), so `/api/v1/external/*` returns 401.

**Why T126 saw the home tid.** The T126 table (`projects/spaarkeai-word-add-in-r1/notes/126-message-to-uac-r2.md`) describes the deployed SPA: `/organizations` and Teams SSO. Both are exactly what A8 forbids (ADR-028 `:545`; `auth.md:66`). Master already removed them.

**The fix A8 implies is a deployment, not BFF code:**
1. Set the repo `vars` `VITE_TEAMS_MSAL_CLIENT_ID` / `_TENANT_ID` / `_BFF_SCOPE` and deploy master's SPA and Teams package (R3 task 040 step 4 and task 070).
2. Pre-authorize `9f1da340` on the dev BFF app's `user_impersonation` (task 040 notes §5 step 2).
3. Run the live check (task 071).

Word-add-in-r1 then needs **no client change**, and its local `wip/t126-workforce-customer` branch is retired.

### 2. What R3 already built for Model 1, and what is left for UAC 186

| R3 item | State | What it does |
|---|---|---|
| A8 (task 001) | ✅ on master | One single-tenant Spaarke workforce client. Auto-approve only when home tid ∈ `CustomerTenantIds` and `acct = 0` at home (`ADR-028:540`). One shared registration path (`:539`). No invite permission on a stamp (`:547`) |
| 040 client switch | 🔄 code merged, **not deployed** | §1 |
| 012 member-test guest branch (FR-15) | ✅ on master | `acct = 1` ∧ `tid` = stamp ∧ home tenant from `idp` ∈ list → Member (`WorkforceIdentityOptions.cs:282-340`). The disabled-systemuser deny (`WorkforcePrincipalResolver.cs:234-242`) |
| 011 default modules | ✅ on master | Workforce → `legal-front-door` + `policy-library`; the map adds extras (`ModuleEntitlementResolver.cs:61,172-205`) |
| 003 spike → Option B | ✅ owner 2026-10-11 | Custom join page plus a shared registration service. Home sign-in through a registration-only **`/organizations`** client, ID token only |
| 056 join page | ✅ behind `VITE_JOIN_ENABLED` | `/join?customer={key}` |
| 060 registration service | 🔲 **blocked on the owner** | Verifies home tid ∈ list and `acct = 0`, invites the guest without email, adds it to `sprk-{customerId}-users`. Its open owner items include **the A8 carve-out for the `/organizations` registration client** (`task-060-model1-shared-service-analysis.md` §5–6) |

Under A8, "no approval" is A8's auto-approve rule, applied at 060. The stamp never sees `acct = 0`, because the guest token carries `acct = 1`. It trusts that 060 is the only path that creates guests (060 constraint "review 012 K2").

**Left for UAC 186:**
- (i) Replace today's silent contact **creation** at first sign-in with `registration_required` plus a form.
- (ii) The registration endpoint.
- (iii) Contact ownership.
- (iv) Visibility on the contact table.
- (v) Baseline access from the role.
- (vi) Tests and the dev live gate.

**No issuer or validator change, and no route reclassification.** Every Model 1 workforce token is a Spaarke-tenant token.

### 3. The member test in this flow, and every deny code

The sign-in path, in order (`WorkforcePrincipalResolver.cs:126-289`):
1. No `oid` → **401** `sdap.access.deny.missing_identity_claims`.
2. Not a user token → `workforce_app_only_token`.
3. The `oid` names an enabled systemuser → systemuser principal. Licensed guests (U2) stop here, for example Ralph Dewey, a licensed guest on dev.
4. The `oid` names only a disabled systemuser → `systemuser_disabled`.
5. Otherwise the binder (`ContactIdentityBinder.cs:170-195`) runs `WorkforceMembershipTest.Evaluate`.

| Token | Member-test result | Code (`sdap.access.deny.*`) |
|---|---|---|
| Guest: tid = stamp, `acct = 1`, `idp` home ∈ list | **Member** (FR-15), then refused by the binder since #2128 (2026-10-20): nothing created, bound or resolved | `workforce_guest_use_home_signin` |
| Guest whose home is not listed, or whose `idp` is missing or unparsable | ForeignTenant | `workforce_tenant_not_customer` (on dev, where the list holds Spaarke's own tenant: `workforce_guest`) |
| Spaarke staff, `acct = 0`, no systemuser (prod stamp) | ForeignTenant | `workforce_tenant_not_customer` |
| List empty | TenantListEmpty | `workforce_tenant_list_empty` |
| No `acct` / other value | | `workforce_acct_claim_missing` / `workforce_acct_unrecognized` |
| Home-tenant token (any tenant) | never reaches the test | **401** IDX40001 at the scheme |

After Member, the binder's own codes apply unchanged (`ContactBindingDecision.cs:399-466`):
- `contact_inactive` (D3);
- `contact_oid_ambiguous`;
- `contact_email_ambiguous` (E3);
- `contact_bound_to_different_oid` (E7);
- `contact_binding_unreadable`;
- `contact_linked_to_other_user` (E10);
- `contact_lookup_failed`;
- `binding_column_missing` / `_masked`;
- `contact_key_conflict`;
- `contact_bind_failed`;
- `contact_create_failed` / `_unavailable`.

186 adds these codes:
- `sdap.access.registration_required` (403);
- `sdap.access.deny.registration_email_missing`;
- `sdap.access.deny.workforce_baseline_unavailable` (§6);
- `sdap.access.deny.workforce_baseline_privilege_missing` (§6).

### 4. Registration

**Signal.** Today a Member with no oid binding and no email match is **created silently** on the sign-in request:
- E1 (no email) and E4 (no match) return `CreateByOid` (`ContactBindingDecision.cs:573-577, 601-605`);
- the binder writes it (`ContactIdentityBinder.cs:480-525`).

186 changes only the sign-in call on the `WorkforceToken` plane: those two outcomes become **403** ProblemDetails with `reasonCode = sdap.access.registration_required`. The response carries prefill extensions (`givenName`, `familyName`, `email`, all from the token).

Unchanged:
- every other outcome, including the E11 email bind of an existing unbound contact, which still happens at sign-in without a form;
- task 141's rules;
- the CIAM and systemuser planes.

**How each client surfaces it:**
- **SPA, browser and Teams** (one bundle): the CallerPrincipal filter returns the 403 on `/me`. `AuthGuard` routes the code `registration_required` to a Fluent v9 registration page, and after a 201 it reloads `/me`.
- **Add-ins:** not applicable. A licence-free contact cannot use OBO routes, so the add-in's existing no-access path stands.

**Form (minimal):**
- first name (required, prefilled from `given_name`);
- last name (required, prefilled from `family_name` / `name`);
- department (optional; written to the standard `department` column, which the Legal Front Door intake already reads, `ExternalIntakeEndpoints.cs:25,264`).

The email is shown read-only from the token and is **never** accepted from the body. Server-side validation: trim, Dataverse max lengths, and no control characters.

**Endpoint: `POST /api/v1/external/registration`.**
- It is mapped on its own small group with `RequireAuthorization(AuthPolicies.ExternalCollaboration)` but **without** `AddCallerPrincipalAuthorizationFilter`, because that filter denies the very state this route serves (`ExternalAccessEndpoints.cs:60-63`).
- The handler requires the Workforce plane (`CallerPrincipalResolver.DeterminePlane`, `:394-413`), runs the same resolver steps 1–4, and calls a new binder method, `RegisterWorkforceCallerAsync(user, oid, stampTenantId, details)`. That method is the **only** caller allowed to create on the workforce plane.
- Responses: 201 `{contactId}` when created; 200 when the oid is already bound; otherwise the deny codes above.
- **Placement** (`.claude/constraints/bff-extensions.md`): in the BFF.
  - It must validate the stamp's user token and write the stamp's Dataverse app-only, next to the existing create path.
  - The shared registration service (060) holds no customer data by design.
  - It is not AI and not background work.
  - BFF obligations: Placement Justification and a `<hot-path-declaration>` (`Infrastructure/ExternalAccess/**` is shared with R3, so run `/conflict-check`); publish-size delta against a fresh master build; no new HIGH CVE; tests.
- **§11:**
  - (1) *Existing:* the binder's first-sign-in create (`ContactIdentityBinder.cs:480`) and `/me`.
  - (2) *Extension:* yes. The route is a thin caller of the existing decision and of `CreateContactForOidAsync`; only the eligibility source (the form) is new.
  - (3) *Cost of doing nothing:* the contact is created silently with token-derived fields, against the owner's "first sign-in shows a form", or nobody registers at all.

**Creation.**
- App-only, through the existing payload: oid in `sprk_externalobjectid`, plus the key mirror `sprk_externalobjectidkey`, plus `sprk_identityplane = Workforce (100000001)`, written in one POST (`ContactIdentityStore.cs:318-330,621-633`).
- New: `department`, and `ownerid`.
- No approval step.

**Owner and organization.**
- Today the payload sets no owner, so the contact is owned by the BFF application user's business unit (root).
- 186 sets `ownerid` to the **customer BU's default team**, from `ExternalAccess:CustomerBusinessUnitId`. That is R3 ISS-005 (#1673) option A, written by provisioning H4b from H10's BU (`H10DataverseAppUserGraphParityHandler.cs:664`). Absent → the root BU (dev), logged.
- **No organization link** (`parentcustomerid` / `sprk_contactorganization`). The organization-expansion term in `AccessibleRecordSetService` would hand a self-registered person every organization-level grant.

**Collisions and idempotency** (E10 and task 141 unchanged):
- **Double submit:** the oid lookup runs first (D1–D4), so a second submit resolves by oid and returns 200 with the same contact.
- **Concurrent submit:** the alternate key on the mirror refuses the loser with 412 → re-read → it resolves to the one contact (`ContactIdentityBinder.cs:500-505`).
- **Email already has a contact:**
  - one unbound, unlinked contact → E11 binds it at sign-in, so registration is never offered;
  - bound elsewhere → E7 `contact_bound_to_different_oid`;
  - linked to a systemuser → E10 `contact_linked_to_other_user`;
  - several → E3 `contact_email_ambiguous`.
  - In none of these cases does registration create anything.
- **No token email:** E1 would create a contact with no email, which escapes the "one contact per email" check. Registration therefore requires a token email: `registration_email_missing`. Under A8 the guest's `email` is the invite address, which 060 takes from the verified home ID token.
- **Known limit (K):** two *different* oids with the same email registering in the same instant can both pass the E4 read. The key is on the oid, not the email. Mitigation: re-read the email after the create. If more than one active row has it, write the existing `EmailAmbiguous` collision flag on both, so an operator sees it.

**Optional hardening (not blocking):** request the `groups` claim on the stamp BFF registration and require `sprk-{customerId}-users`. The stamp would then check positively that the guest came through 060, instead of trusting it.

### 4b. Visibility on the contact table

**Existing columns suffice for "who has signed in":**
- `sprk_identityplane = Workforce`;
- `sprk_externalobjectidkey` (the unsecured oid mirror; the binding column itself is field-secured);
- `createdon` (registration = first successful sign-in);
- `createdby` (the BFF application user).

A self-registered contact is told apart from a licensed user's linked contact because no systemuser's `sprk_primarycontact` points at it. 186 ships a **system view**, "Workforce contacts (self-registered)": plane = Workforce, outer-joined to systemuser on `sprk_primarycontact`, with no match. It adds no column.

**Last sign-in, only if the owner asks for "last" rather than "first":** `contact.sprk_lastsignin` (date only).
- **§11:**
  - (1) `createdon` covers the first sign-in only; the notification `lastSeenAt` is a cache marker, not on the table.
  - (2) No existing column means "last active".
  - (3) Without it the customer cannot tell dormant registrants from active ones.
- **Writes:** on a contact-branch resolve, at most one per contact per UTC day, guarded by an `ITenantCache` marker `(contactId, yyyyMMdd)` with a 24 h TTL. App-only PATCH. A failed write never denies.
- **Field security:** none (not a trust signal); read-only on forms.
- **Schema:** needs owner approval.

### 5. Baseline access: "Spaarke Internal Contact User (Workforce)"

**Dev facts:**
- Role `9deaac9c-02c7-f111-a05c-0022482913fc`, root BU `06fbf21c`, unmanaged.
- **0** user holders, **0** team holders.
- 655 privileges (`RetrieveRolePrivilegesRole`): Basic 309, Global 232, Deep 97, Local 17.
- Examples:
  - Deep Read on `sprk_matter`, `sprk_project`, `sprk_document`, `sprk_invoice`, `sprk_analysis`;
  - Deep CRUD on `sprk_agreement` and `sprk_analysis`;
  - Basic CRUD on `contact`, `account` and the communication tables.
- **No privilege at all on `sprk_servicerequest`**, the Legal Front Door table the description names.
- `sprk_approlemodulemap` has two active rows: `FrontDoorUser` → `legal-front-door` / `policy-library`.

**Can the role be assigned directly under A8?**
- A licence-free guest gets no systemuser row through background sync. On dev the only enabled guest systemusers are licensed; an unlicensed one is disabled.
- But the stamp Dataverse is pay-as-you-go (spike `fr-12…md` Q3), and 060 adds the guest to `sprk-{customerId}-users`, the **environment security group**. Microsoft documents a just-in-time add, `POST systemusers(azureactivedirectoryobjectid=…)/systemuserroles_association/$ref`, as an app-callable path (researcher note `payg-b2b-guest-dataverse-user-provisioning-2026-10-07.md`; whether PAYG covers an unlicensed guest is "a live T186 check").
- So **(d)**, direct assignment to a JIT systemuser, is possible and is analysed below.

| | (a) BFF reads the role as a ceiling | (b) team holds the role | (c) role → modules | (d) direct role on a JIT systemuser |
|---|---|---|---|---|
| Mechanism | `RetrieveRolePrivilegesRole` → table × operation map. Record scope stays UAC (grants ∪ standing ∪ org expansion, after vetoes) | `RetrievePrincipalAccess(team, record)` per record | Privileges projected onto Tier-1 module codes | Dataverse evaluates natively; the A5 impersonated root set (`ImpersonatedRootSetSource.cs`) |
| Depth for a contact caller | **Collapses:** None = refused; any depth (Basic…Global) = allowed **within the UAC set**, exactly as a Dataverse share works at Basic. Basic also covers rows anchored to the caller (`sprk_requestedby`, `sprk_createdbycontact`). Local/Deep/Global never widen | Relative to the team: Basic = team-owned rows only (none of the granted ones); Local+ = the whole team BU, which must then be intersected with UAC anyway | No depth | Native: Deep Read = **every** matter, project, document and invoice in the BU, for anyone who self-registers with no approval |
| Owner's edits take effect | ≤ **5 min** (cached as DATA in `ITenantCache`; "minutes, not hours", task 130) | Immediately (uncached) | ≤ 60 s (map cache, `ModuleEntitlementResolver.cs:63`) | Immediately; root-set cache 2 min (`ImpersonatedRootSetSource.cs:91`) |
| Fail closed | Role missing, ambiguous or unreadable → `workforce_baseline_unavailable`; no fault cached (`auth.md` C12) | Per-call fault → deny | Unentitled → module hidden or 404 | JIT refusal or disabled row → `systemuser_disabled` |
| Cost | One reader (extend `Services/Dataverse/Privileges/UserPrivilegeChecker.cs`, today systemuser-keyed, Read-only, 24 h cache), one check at the module-data seam and on each `/api/v1/external` write route, tests | N Dataverse calls per page; a privileged team principal that leaks if anyone is added to it | Small, but Tier-1 is UX, not a boundary (NFR-06), and it cannot express Create vs Read | A PAYG meter per person (uncertain), the BFF needs role-assign rights, U3 becomes U2, BU-wide reads |

**Recommendation: (a).**
- It keeps the owner's role as the single, owner-editable statement of *what* a workforce contact may do, table by table and operation by operation. Record scope stays UAC's, the same as for every other contact.
- It is the only option where a self-registered, unapproved person cannot read beyond their grants.
- It needs no new Dataverse principal, licence or team.

Details:
- **Applies only to** `Plane = Workforce` with `SystemUserId = null`. Licensed users keep their own roles; CIAM partners are unaffected.
- **Role lookup:** by name in the root BU; exactly one match, else fail closed. The role must ship in the SpaarkeCore solution to reach other stamps (an owner or provisioning action).
- **Entitlements** are intersected with the ceiling, so `/me/entitlements` never shows a tab the role forbids (NFR-06).
- **The owner must add `sprk_servicerequest` Create and Read**, or every Legal Front Door submission is refused.

### 6. Tests and the dev live gate

**Unit tests (BFF):**
- Task 141 and R3 012 tests unchanged and green.
- Sign-in no longer creates: E1 and E4 → `registration_required`; E11 still binds.
- Registration:
  - creates exactly one contact keyed by the oid, with the customer-BU owner;
  - a double submit → 200, same contact;
  - a forced 412 → one contact;
  - an email held by an existing contact → no create (E3, E7, E10);
  - no email → `registration_email_missing`;
  - a CIAM token, a systemuser and a non-member → refused.
- Ceiling:
  - None → deny; any depth → allow inside the set; Deep never admits a record outside the set;
  - missing, duplicate or faulting role → deny, with nothing cached;
  - TTL respected (fake cache);
  - seeding proof: drop the check and confirm the "outside the set" test fails.

**ArchTests:**
- the full suite, including `RouteAuthorizationGuard`;
- **only** the registration route reaches `RegisterWorkforceCallerAsync`;
- a scheme census (default + Ciam + RagApiKey only), so a foreign-issuer scheme cannot arrive unnoticed.

**SPA:** `npm run build:prod` green; `registration_required` → form → POST → reload, in browser and Teams hosts. BFF build: 0 errors.

**Live gate on dev (owner-gated):**

Preconditions:
- master SPA and Teams package deployed (§1);
- dev `CustomerTenantIds` includes `bc3aa7f4`;
- the role has `sprk_servicerequest` privileges;
- a **new, unlicensed** Dewey Cheatham account exists as a guest. It comes through 060, or, while 060 is unbuilt, through an owner manual invite. That is out of policy (060 K2) and dev only.

Steps:
1. Sign-in gives `tid` = Spaarke, `acct = 1`, `idp` = `bc3aa7f4`, and `/me` → 403 `registration_required`.
2. Submit → 201. Check the contact's columns, its owner and the view.
3. `/me` → 200; entitlements = `legal-front-door` + `policy-library`; an intake is accepted. Remove the Create privilege → refused within 5 min.
4. Resubmit → nothing new.
5. Ralph Dewey (licensed) still resolves as a systemuser.
6. A guest from an unlisted home tenant → 403 `workforce_tenant_not_customer`. Any home-tenant (`/organizations`) token → **401**. This is how the POML's "unlisted tenant → 401" reads under A8.
7. Confirm no systemuser row (enabled or disabled) appeared for the new guest.

### 7. Open owner questions (blocking only)

1. **Path C (A8) or path B (amend A8)?** This is the ADR conflict the main session raises.
2. **Baseline (5):** approve (a), including "depth only distinguishes none from some; record scope is UAC's". Add `sprk_servicerequest` privileges to the role.
3. **Contact owner = the customer BU** through `ExternalAccess:CustomerBusinessUnitId`? This is shared with R3 ISS-005 and blocks the create.
4. **"Last sign-in" column, or `createdon` plus the view?** A schema approval is needed only for the column.
5. **The path-C dependency:** R3 060's host decision and the A8 carve-out for the registration-only `/organizations` client (R3 open question 4). For the dev gate until then, is an owner manual guest invite acceptable?

---

## Appendix: option A, if the owner amends A8 (path B)

**Issuer rule.** A named JwtBearer scheme `WorkforceCustomer`, default for nothing:
- signing keys from the multi-tenant metadata;
- `ValidIssuers` = exactly `https://login.microsoftonline.com/{tid}/v2.0` for `tid` ∈ `WorkforceIdentity:CustomerTenantIds` (read live), with the token `tid` equal to the issuer tenant;
- never Spaarke's tid or the CIAM tid;
- an empty list rejects everything;
- `EnableAadSigningKeyIssuerValidation()`;
- audience = the BFF's own (`AzureAd:ClientId` / `api://…`);
- a delegated `scp` is required, so app-only tokens are rejected.

The member test is task 141's `acct = 0` home branch; FR-15 goes unused.

**Routes:**
- **Accept:** the 54 `/api/v1/external/*` routes (`/me`, `/me/entitlements`, and 39 project-data, 5 module-data, 2 message, 1 intake, 2 notification and 3 contact-grant routes), plus the new registration route. All are app-only, with no OBO (ADR-028 `:559`).
- **Own tenant only:** every other route, notably the OBO routes (`/api/office/*`, the Communications OBO reads, and the `/api/v1/external-access/*` admin routes using `CallerRecordAccessProbe` OBO).
- **Opt-in:** only by adding the scheme to the `ExternalCollaboration` policy, with an ArchTest census that it is named only there and carried only by `/api/v1/external/*`.

**Owner and provisioning actions:**
- each per-customer BFF registration becomes `AzureADMultipleOrgs`;
- customer admin consent in every customer tenant (otherwise AADSTS700016);
- the `acct` optional claim;
- the `CustomerTenantIds` intake (`INCOMING-141`);
- the SPA and Teams clients revert from R3 040's tenant-pinned client to a multitenant one.

**Model change.** The B2B guest model for licence-free staff is abandoned: 056, 060 and FR-15 become dead code. But **licensed customer staff (U2) are still guests whose systemuser is keyed by the guest oid**. The SPA cannot know a user's type before sign-in, so a licensed user who signs in at home presents a different oid. They get no systemuser match and become a second, contact principal, which splits their access. That is A8's own reason (`ADR-028:502-505`).

**Cost already found:** the word-add-in branch `wip/t126-workforce-customer` (`e8e7f249f`, not pushed). Its review found fix-now issues:
- (a) no `scp` check, so an app-only token from a listed tenant was accepted;
- (b) no `EnableAadSigningKeyIssuerValidation`;
- (c) email-bind trust: a customer-tenant admin can set any user's `mail`, so binding would need `upn`, a verified domain or `xms_edov`;
- (d) the oid → systemuser lookup has no tid guard (`MembershipEndpoints.cs` ~`:436`; `WorkforcePrincipalResolver.cs:336-345` on master queries by oid only);
- (e) customer-tenant `roles` reach `/me/entitlements` (`ModuleEntitlementResolver.cs:185`).

**Recommendation: path C.**
- A8 is approved and built, and the 401 is a stale deployment.
- Path C keeps one identity (the guest oid) per person across the MDA, the Console, the add-ins, the SPA and Teams.
- Path C widens no issuer trust and needs only a low-impact `openid profile` consent in customer tenants, instead of admin consent for the BFF API.
- Path C's real costs: it depends on 060 (owner host decision), it needs a narrow A8 carve-out for the registration client, and the stamp trusts 060 as the only way guests are created.
