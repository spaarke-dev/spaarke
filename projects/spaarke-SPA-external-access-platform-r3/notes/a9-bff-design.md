# A9 design: workforce contacts sign in with their customer's home directory (BFF + SPA)

> Status: **DRAFT for owner sign-off** (2026-10-13). Auth code: owner signs off before build; two adversarial passes per BFF task (project CLAUDE.md review policy).
> Authority: ADR-028 **A9** (master, PR #1810). Code basis: branch `work/spaarke-SPA-external-access-platform-r3` @ `4e9113759` (master incl. UAC #1801).
> Inputs: UAC `projects/unified-access-control-r2/notes/186-model1-workforce-design.md` (routes, registration, option A appendix: word-add-in review findings (a)–(e)); code map 2026-10-13; researcher 2026-10-13 (Microsoft Learn, cited in §9).
> Terms: **Spaarke tenant** = Spaarke's Entra directory. **Customer's home directory** = the customer's own. A **home token** is a token issued by a customer's home directory to the multitenant SPA client for the stamp BFF.

## 1. What changes, in one paragraph

Today the BFF accepts only Spaarke-tenant workforce tokens. The default scheme is `AddMicrosoftIdentityWebApi(AzureAd)` with a single `TenantId` (`AuthorizationModule.cs:53-55`), so a home token fails with IDX40001. Contact identity is keyed on `oid` alone, with no tenant column (`ContactBindingDecision.cs:1227-1248`), and the first-sign-in create sets no owner (`ContactIdentityStore.cs:465-478`).

A9 adds the following:
- a **second, narrow authentication scheme** for home tokens, accepted only on the app-only `/api/v1/external/*` routes;
- **home-directory principal resolution**: stored link → systemuser, else contact by (`tid`, `oid`), else `registration_required`;
- a **registration endpoint** behind a profile form;
- a **link endpoint** that proves a licensed user's two identities in one call.

The SPA's browser workforce sign-in moves to one multitenant client. Teams is deferred.

## 2. Token acceptance (BFF)

**Scheme `WorkforceHome`** (new, `AuthorizationModule.cs`), a JwtBearer scheme on the stamp's own BFF app id:
- **Signing keys:** from the multitenant metadata (`https://login.microsoftonline.com/organizations/v2.0`), with `EnableAadSigningKeyIssuerValidation()` set explicitly. This fixes word-add-in review finding (b).
- **Issuer:** `AadIssuerValidator` (the issuer must equal `https://login.microsoftonline.com/{tid}/v2.0` for the token's own `tid`). Then, in `OnTokenValidated`, the A9 allowlist:
  - `tid` ∈ `WorkforceIdentity:CustomerTenantIds` (read through `IOptionsMonitor`, so a config change applies without restart); an empty list rejects everything;
  - `tid` ≠ `AzureAd:TenantId` (the Spaarke tenant; its tokens stay on the default scheme) and ≠ `Ciam:TenantId`;
  - `iss` equals `https://login.microsoftonline.com/{tid}/v2.0` **exactly** (A9; v1 `sts.windows.net` issuers are refused);
  - audience = the stamp BFF app (bare id or `api://{id}`), never the Copilot audience;
  - a **user** token: `scp` contains `user_impersonation`, and `idtyp` is not `app` (finding (a));
  - `acct` = `0`. A missing value or `1` is refused. The `acct` optional claim is already added by provisioning (`GraphAppRegistrationProvisioner.cs:750`).
  - Any failure is `context.Fail` → 401.
- **Where it is accepted:** the scheme is added to the `ExternalCollaboration` policy only (`/api/v1/external/*`, all app-only, no OBO). No other policy names it.
- **ArchTest scheme census:** the registered schemes are exactly default + Ciam + WorkforceHome + the existing API-key schemes. `WorkforceHome` appears only in `ExternalCollaboration`, and only `/api/v1/external/*` routes carry that policy. This is the mechanical guard on the security control. The owner declined a client-side `organizations` guard; this server-side census is different.
- **Plane:** a principal authenticated by `WorkforceHome` is the **Workforce** plane. Handlers stay plane-agnostic, and default modules are as A8: `legal-front-door` + `policy-library`. The resolver chooses the home strategy by the **authenticating scheme**, not by parsing `iss`.

Must-reject tests (fail before, pass after):
- an unlisted `tid`;
- the Spaarke `tid` on this scheme;
- the CIAM `tid`;
- a v1 issuer;
- an `iss`/`tid` mismatch;
- `acct` = 1 or missing;
- an app-only token;
- a wrong `aud`;
- the Copilot audience;
- an expired token;
- a token signed by a key whose issuer differs.

## 3. Principal resolution for home tokens

`WorkforcePrincipalResolver` gains a home branch. The existing Spaarke-tenant branch is unchanged. In order:
1. **No `oid`/`tid`** → 401 `missing_identity_claims`.
2. **Stored link** (§5): (`tid`, `oid`) → `systemuserid`. If enabled → systemuser principal. If disabled → `systemuser_disabled`.
3. **Contact binding** by plane **WorkforceHome** + `tid` + `oid` (§4) → contact principal.
4. **Otherwise → 403 `sdap.access.registration_required`**, with prefill extensions `givenName`, `familyName` and `email`, all from the token. Nothing is created at sign-in; this replaces A8's silent create for this plane.

**Guards (finding (d)):**
- The `oid` → systemuser lookups never run for a home token: `WorkforcePrincipalResolver.ResolveSystemUserAsync` (`:336-345`) and `MembershipEndpoints.ResolveSystemUserIdAsync` (`:436-453`).
- A home `oid` is never compared with `systemuser.azureactivedirectoryobjectid`.
- The `/api/v1/membership` route and the workspace services stay on the default scheme, so a home token cannot reach them.

**Finding (e):** customer-directory `roles` claims are ignored for home tokens in `ModuleEntitlementResolver` (`:185`). Entitlements come from the principal kind and the server-side map only.

## 4. Contact identity for home tokens (schema)

Task 141's invariants stay: E10, oid-first, and "never clear or re-point a link". The CIAM and Spaarke-tenant planes do not change.

New Dataverse schema (**owner approval needed**, in the solution):
- `sprk_identityplane` gains the option **WorkforceHome = 100000002**. A9 contacts are told apart from A8 guest contacts.
- `contact.sprk_externaltenantid`: text(36), field-secured like the binding column, written once with the binding.

A9 home contacts are looked up by plane = WorkforceHome **and** `sprk_externaltenantid` = `tid` **and** `sprk_externalobjectid` = `oid`. The key mirror `sprk_externalobjectidkey` stays the `oid`. Its alternate key keeps one contact per `oid`, which is stricter than (`tid`, `oid`) and safe because Entra oids are GUIDs.

**Registering to an existing contact (owner, 2026-10-13: no Legal step; this is common; the form confirms the email):**
- The profile form shows the sign-in email read-only. When an existing contact matches it, the form says so ("We found your existing record") and the user **confirms** that the email is theirs. Registration then **binds that contact** instead of creating one: no duplicate, and its grants carry over.
- **Why the email can be trusted here:**
  - Only **domain-verified** emails reach the BFF: `removeUnverifiedEmailClaim` = true on the multitenant apps (§9) removes an `email` whose domain the user's directory has not verified.
  - Only listed customer directories are accepted (§2).
  - So a token can only claim an address in **that customer's own verified domains**: one of its own employees, whose account the customer's admin already controls.
  - Never authorizing on email (Microsoft) still holds in the sense that matters: the email selects which existing record to bind **once**, at registration, behind those two gates. Afterwards identity is (`tid`, `oid`).
  - A token without `email` never binds by email. `preferred_username` is not used for binding.
- **Bindable:** exactly one active contact with that `emailaddress1`, not bound to any identity, not linked to a systemuser (E10), and not a CIAM partner (plane External).
- **Not bindable:**
  - several matches, or a match that is bound, linked or a partner → **`registration_contact_conflict`**; the contact is flagged `EmailAmbiguous` for an operator, nothing is created or bound;
  - no match → create (§6).
- **Live check (task 074):** the token carries `email` for a Dewey Cheatham member, and does not for an address in an unverified domain.
- **Dataverse duplicate detection:** Web API creates do not run duplicate-detection rules unless the request opts in (`MSCRM.SuppressDuplicateDetection: false`), so it never blocks a registration. Binding an existing contact is what keeps duplicates from being created in the first place. Task 047 checks dev's active contact duplicate rules and records them.

## 5. Stored link for licensed users

**Store:** two new columns on **systemuser**: `sprk_hometenantid` and `sprk_homeobjectid`. They are field-secured to the BFF writers. Dataverse refuses an alternate key on a field-secured column, so the uniqueness key sits on two unsecured mirrors, `sprk_hometenantidkey` and `sprk_homeobjectidkey`. This is the contact pattern (`sprk_externalobjectidkey`, owner round 4, B2), approved by the owner on 2026-10-14 (task 044, option A). The BFF writes all four columns in one update. It **looks a link up by the secured pair only**, so editing a mirror can block a link but can never redirect a sign-in. The mirrors exist only to make a duplicate pair fail with 412. This is "its own field", per UAC's condition: not `sprk_externalobjectid`, not the plane columns, not contact.
- §11:
  1. *Existing:* `TryLinkSystemUserAsync` links systemuser → contact, not home id → systemuser.
  2. *Extension:* it extends the existing systemuser table rather than adding a table.
  3. *Cost of doing nothing:* a licensed user signing in with their company account gets `registration_required` and becomes a second, contact principal with split access. That is A8's own objection.

**Proof (no `home_oid` claim exists; Microsoft says oids differ across tenants by design, §9).** The link is created by `POST /api/v1/external/workforce/link` only when **one request** proves both identities:
- The **bearer** is the home token (validated by `WorkforceHome`, §2).
- The **body** carries the same person's **Spaarke-tenant guest access token** for the same BFF app. The BFF validates it with the default scheme's `TokenValidationParameters`:
  - signature, `aud`, lifetime;
  - `tid` = the Spaarke tenant;
  - `acct` = 1;
  - `idp` home tenant = the home token's `tid` (`TryParseHomeTenant`);
  - **not expired**. Both tokens must be within their own Entra lifetime (60–90 minutes). That is the longest window possible, since access tokens cannot be older. The SPA gets both within seconds, so the user waits for nothing. (Owner asked for 24 hours; an access token cannot last that long, so its own lifetime is used, with no shorter extra limit.)
  - its `oid` → an **enabled** systemuser.
- **Defense in depth:** the guest token's home UPN (from the `#EXT#` UPN or `email`) must equal the home token's `preferred_username`, case-insensitively. This is a necessary extra condition that can only refuse; it never grants on its own.
- **Outcome:** the link is written once. An existing different link is a collision; the BFF denies it and never re-points it. Audit logs record the systemuser id and the hashed home ids.
- **Residual risk (K):** someone holding a stolen, unexpired guest token *and* a home token from the same customer directory with the same UPN. In practice that is the same person.

**How the SPA gets the guest token without a "core user" button** (owner: none):
1. After a home sign-in, `/me` returns 403 `registration_required`.
2. The SPA tries `ssoSilent` with the A8 single-tenant client (`9f1da340`), authority = the Spaarke tenant, and `loginHint` = the home account.
3. A licensed guest succeeds silently through their home session, and the SPA posts the link. `/me` then resolves to the systemuser.
4. A non-guest fails silently → the profile form.

Fallback: if the registration POST sees that the token email matches an **enabled systemuser's** email, it refuses with `licensed_user_link_required`. That is a refusal only, never a grant. The SPA then runs the same acquisition **interactively** (popup), so a licensed user whose silent attempt failed is never registered as a contact.

## 6. Registration endpoint

`POST /api/v1/external/registration`, following UAC's route design:
- **Policy and plane:** its own small group with the `ExternalCollaboration` policy and **without** `CallerPrincipalAuthorizationFilter`. Home tokens only (the `WorkforceHome` scheme).
- **Body:**
  - `firstName`, required, ≤ 50;
  - `lastName`, required, ≤ 50;
  - `department`, optional, ≤ 100;
  - each value is trimmed and must contain no control characters.

  Email comes from the token only; a body email never binds (closed DTO). A token with no `email` and no `preferred_username` → `registration_email_missing`.
- **Order:**
  1. resolution steps 1–3 (§3), so an already-linked or already-bound caller gets 200 and nothing is created;
  2. the email checks: §5 `licensed_user_link_required` first, then the §4 bind of an existing contact (only when the body carries `confirmExistingRecord: true`) or `registration_contact_conflict`;
  3. otherwise the create.
- **Create:** app-only, in one POST:
  - contact with `firstname`, `lastname`, `department`, `emailaddress1` (from the token);
  - `sprk_externalobjectid`, `sprk_externalobjectidkey`, `sprk_externaltenantid`, plane WorkforceHome;
  - **`ownerid`** = the default team of `ExternalAccess:CustomerBusinessUnitId` (ISS-005 option A). If the setting is absent → **500 `registration_owner_unconfigured`, nothing created** (owner, 2026-10-13: fail safe);
  - no organization link (UAC: org-level grants must not flow to self-registrants);
  - active immediately, no approval (owner).
- **Idempotency:**
  - the `oid` lookup runs first;
  - a concurrent loser gets 412 from the alternate key, re-reads and returns 200 with the same contact;
  - a resubmit creates nothing.
- **Responses:** 201 `{contactId}` / 200 `{contactId}` / 403 with a reason code / 409 `existing_record_found` when one bindable contact matches and the body lacks `confirmExistingRecord: true` (added at task creation 2026-10-13 so the form can ask the user to confirm, §4).
- **Only caller of the create:** an ArchTest pins that only this route reaches the home-plane create.
- **Placement (`bff-extensions.md`):** in the BFF. It must validate the stamp's user token and write the stamp's Dataverse app-only next to the existing create path. It is not AI and not background work. A Placement Justification, publish-size delta, CVE check and tests ship with it.
- **§11:**
  1. *Existing:* the binder's create (`ContactIdentityBinder.cs:509-550`).
  2. *Extension:* the route is a thin caller of the existing decision and store, plus the home plane.
  3. *Cost of doing nothing:* A9 requires a form and no silent create; without the route nobody can register.

## 7. SPA

- **One multitenant SPA client** ("Spaarke Workforce SPA", new registration in the Spaarke tenant).
  - The browser "My organization" sign-in uses it with authority `organizations`, for discovery only: the SPA does not know the customer before sign-in.
  - After sign-in, the token's `tid` → the directory entry → backend URL and BFF app id. The directory is the T240c contract; until it ships, `VITE_BACKENDS`. Entries gain `homeTenantIds`, agreed with provisioning (task 041 follow-up).
  - The BFF token is then requested with authority `https://login.microsoftonline.com/{tid}` and `api://{bffAppId}/user_impersonation`.
  - An unknown `tid` → a clear "your organization is not set up for Spaarke" page; no token is sent anywhere.
- **`AuthGuard`:**
  - `registration_required` → the silent link attempt (§5) → otherwise the **profile form**: first name, last name and department prefilled; email read-only; Fluent v9. This replaces the 056 join page and its `VITE_REGISTRATION_SERVICE_URL` hand-off.
  - `licensed_user_link_required` → the interactive link.
  - an existing-record match → the form says "We found your existing record" and asks the user to confirm the email, then binds it;
  - `registration_contact_conflict` → a message that an administrator must resolve the record.
- **Landing page wording:** the link reads "Spaarke employee? Sign in with your work account" (`RealmChooser.tsx:76`). Under A9 it becomes "Work for {customer}? Sign in with your company account", and "Spaarke employee" goes.
- **Teams (owner, 2026-10-13: included; A9 amended):** the Teams strategy uses the multitenant client through NAA (`createNestablePublicClientApplication`), with authority = the Teams context `tid`, then the popup. Everyone in Teams, licensed users included, gets a home token, and licensed users resolve through the stored link. The silent link inside Teams uses NAA against the Spaarke tenant, falling back to the popup. The Office add-ins stay on A8.
- **`assertTenantPinnedWorkforceAuthority`** (`msal-config.ts:80-100`): kept for the A8 client. A new assert allows `organizations` and `login.microsoftonline.com/{GUID}` **only** for the multitenant client id.

## 8. Dev setup (live, owner-gated, each with rollback)

Order:
1. **Schema (§4, §5)** on dev: the solution import or the column scripts. Rollback: remove the columns and the option value (unused until code ships).
2. **New multitenant SPA client** in the Spaarke tenant:
   - SPA redirects `https://green-dune-0c4f1221e.7.azurestaticapps.net/`, its `/auth-callback.html` (the silent retry and the Teams popup) and `brk-multihub://green-dune-0c4f1221e.7.azurestaticapps.net` (Teams NAA), and the localhost equivalents (corrected by task 076);
   - `requestedAccessTokenVersion` = 2 on the dev BFF app `1e40baad`;
   - `signInAudience` `AzureADMultipleOrgs` on `1e40baad`;
   - `knownClientApplications` = the new client.

   Rollback: revert `signInAudience` and delete the client. Note: `1e40baad` is shared by dev surfaces; a multitenant audience changes nothing for Spaarke-tenant tokens.
3. **Admin consent** in the Dewey Cheatham home directory `bc3aa7f4`, done by its admin. Rollback: delete the enterprise app there.
4. **Dev BFF settings:**
   - `WorkforceIdentity:CustomerTenantIds` includes `bc3aa7f4` (read first);
   - `ExternalAccess:CustomerBusinessUnitId` = BU1 `cb15f587-baa0-f111-aaac-000d3a99d1d7`.

   Rollback: previous values.
5. **BFF deploy** from master (`/bff-deploy`), **SPA deploy**, then the live probes:
   - member registration;
   - resubmit;
   - licensed-user silent link (Ralph Dewey);
   - unlisted directory → 401;
   - `acct` = 1 home guest → 401;
   - intake as a new contact.

## 9. Microsoft facts relied on (researcher 2026-10-13; V = in current docs)

- **No `home_oid` claim exists.** `oid`/`sub` change across tenants by design; do not correlate with `idp` (id-token-claims-reference). V.
- **`acct`** is a v1/v2 optional claim: 0 member, 1 guest, relative to the **issuing** tenant (optional-claims-reference). V.
- **`xms_edov`** covers MSA, Google and OTP emails, not Entra work-account mail; never authorize on `email`/`upn` (optional-claims-reference, claims-validation). V.
- **Multitenant v2 issuer** = `https://login.microsoftonline.com/{tid}/v2.0`. Validate the signing-key issuer; key data on `tid` + `oid`; the allowlist is an app-level check after the issuer chain (access-tokens). V.
- **Microsoft.Identity.Web** handles issuer validation for `organizations`. Its `MicrosoftIdentityJwtBearerOptionsPostConfigurator` calls `EnableAadSigningKeyIssuerValidation()` (master source, line 86). §2 still asserts it in a test, and does not replace `IssuerValidator` or set `ValidIssuers`: that interaction is untested, so the allowlist runs in `OnTokenValidated`.
- **`authenticationBehaviors.removeUnverifiedEmailClaim`** removes unverified-domain emails from tokens. Set it to `true` on the multitenant SPA client and the BFF apps (provisioning item; dev in task 059), so the `email` used for prefill and refusals is domain-verified where Entra can tell.
- **Microsoft's default** is to treat a guest as a new user and not to correlate identities across tenants (id-token-claims-reference). The §5 stored link departs from that **deliberately**: A9 requires it, owner-approved 2026-10-13. It is proven per person and gated by the stamp's tenant allowlist.

## 10. Tasks (after sign-off)

| New | Title | Depends | Rigor / model |
|---|---|---|---|
| 044 | A9 schema: contact `sprk_externaltenantid` + plane option WorkforceHome; systemuser `sprk_hometenantid`/`sprk_homeobjectid` + key (solution + dev, owner-gated) | sign-off | STANDARD / sonnet |
| 045 | BFF `WorkforceHome` scheme, `ExternalCollaboration` only, scheme census ArchTest, must-reject tests | sign-off | FULL / opus, 2 adversarial passes |
| 046 | BFF home principal resolution: link → systemuser, contact by (`tid`,`oid`), `registration_required`, finding (d)/(e) guards | 044, 045 | FULL / opus, 2 passes |
| 047 | BFF registration endpoint (owner team, marker bind, refusals, idempotency) | 046 | FULL / opus, 2 passes |
| 048 | BFF link endpoint (dual-token proof) | 046 | FULL / opus, 2 passes |
| 058 | SPA multitenant sign-in, `tid` → backend, silent/interactive link, profile form (replaces 056's join page) | 047, 048 | FULL / sonnet |
| 059 | Dev Entra + settings setup (§8 steps 2–4), live, owner-gated | 044 | STANDARD / sonnet |
| 074 | A9 live probes on dev | 058, 059 | STANDARD / sonnet |
| 075 | SPA Teams strategy → multitenant NAA (Teams context `tid`), registration and link in Teams | 058 | FULL / sonnet |
| 076 | Teams package and registration: `brk-multihub://` redirect on the multitenant client, manifest client and domain (dev in 059; 043 pipeline) | 075 | STANDARD / sonnet |

Other task changes:
- **Withdrawn:** 060 (registration service). **Superseded:** 012's guest branch for workforce contacts (the code stays for licensed guests; the FR-15 path becomes unused for contacts) and 056's registration-service hand-off.
- **040:** the A8 client stays for Teams and the link acquisition.
- **Teams:** included (075, 076). Task 071, the Teams live check, covers desktop, web and **mobile** for a workforce contact, a licensed user and a member of an unlisted directory.

## 11. Owner decisions (2026-10-13)

1. **Existing contact with the same email:** no Legal step. The form shows the email; the user confirms it; the existing contact is bound (no duplicate). This is safe because only domain-verified emails from listed customer directories reach the BFF (§4).
2. **Schema (§4, §5):** approved: one option value, three columns, one alternate key. Amended 2026-10-14 (owner, option A): plus two unsecured key mirrors on systemuser that carry the key (§5).
3. **Link proof (§5):** "both sign-ins" are the licensed user's company account and their Spaarke guest account. Freshness = each token's own lifetime (the longest possible; 24 hours is not available for access tokens). The UPN consistency check stays.
4. **Dev setup (§8):** approved step by step in task 059, with commands and rollback shown first.

## 12. Teams for workforce contacts: what adding it would take (owner asked, 2026-10-13)

- **Backend: nothing.** A Teams sign-in under A9 yields the same home token (customer `tid`, `acct` = 0), so §2–§6 apply unchanged.
- **SPA (one task):**
  - The Teams strategy uses the multitenant client through NAA (`createNestablePublicClientApplication`), with authority = the Teams context's `tid`, which Teams provides, so there is no discovery step.
  - Registration and linking are as in the browser. The silent link inside Teams uses NAA against the Spaarke tenant, which is undocumented; fall back to the popup.
- **App registration:** add the `brk-multihub://{SPA origin}` redirect to the multitenant client (one line; dev in task 059, provisioning for production).
- **Teams package:** `deploy-teams-app.yml` / task 043 already builds it. As built (task 076): the manifest names **no** client (a manifest names one only through `webApplicationInfo`, which A8/A9 forbid); the multitenant client id is in the SPA bundle (`VITE_WORKFORCE_HOME_MSAL_CLIENT_ID`), and the SPA domain is in `validDomains`, checked by CI. Each customer's Teams admin approves the app once. That approval is needed for any Teams use anyway.
- **Live test:** task 071 (desktop, web, mobile). Microsoft documents multitenant NAA as generally available in Teams. **Teams mobile specifics are unverified**, so that is the main risk.
- **One decision it forces:**
  - A9 currently keeps **licensed users** in Teams on A8: NAA with the Spaarke-tenant authority, a cross-tenant guest token that is **undocumented**.
  - Two Teams paths cannot coexist, because the app does not know the user type before sign-in.
  - Adding Teams for workforce contacts therefore means moving **licensed users in Teams** to the multitenant client plus the stored link as well. That needs a one-line A9 change ("Teams uses the multitenant client for both populations"), and it is the **documented** path.
  - The Office add-ins are unaffected.
- **Size:** about two tasks (SPA Teams strategy, manifest and registration) plus the existing live check. No backend work. A small widening, not a large one, provided the Teams mobile check passes.
