# Spaarke External Access Platform R3 — project operating manual

> Read with `current-task.md` (current state). Repo-wide rules are in root `CLAUDE.md`; this file holds only what is specific to this project. Guidance: `.claude/skills/project-setup/references/claudemd-template.md`.

## 1. Scope and status

R3 makes the external SPA (`external.spaarke.com` and the Teams tab) a working destination. It covers:
- **C1** record detail;
- **C2** four Legal Front Door intake wizards;
- **C3** grant notifications (email + derived in-portal feed);
- **C4** grid columns;
- **C5** Teams parity for workforce users;
- **C6** message send;
- **C7** workforce-contact sign-in and registration, end to end, under ADR-028 A9 (owner 2026-10-13): the multitenant SPA client, BFF token acceptance, the first-sign-in profile form, and the stored link for licensed users;
- **C8** auth alignment (single-tenant workforce client, run-time backend selection, out-of-plane page fixes, modules by user type, Ciam audience forms, default-scheme CIAM guard, production build);
- **C9** documents on every core record, creator stamps, delete-own.

**Out of scope:** Ask Legal; e-signature; editing core records; new identity planes; CIAM in Teams; Model 2; email on message send; message attachments; the T240c directory and T240d provisioning (provisioning); ADR-028 A5 (UAC-r2).

Status: see `tasks/TASK-INDEX.md` and `current-task.md`. Spec: `spec.md` (source of truth for scope). Design: `design.md`. Plan: `plan.md`.

## 2. Binding rules for this project

**Authorization and data access**
- Every new external-plane route sits in the `/api/v1/external` group (`ExternalCollaboration` policy + `CallerPrincipalAuthorizationFilter`, `ExternalAccessEndpoints.cs:60-63`). It checks the record against the accessible set, fail-closed (NFR-01).
  - **Exceptions (A9 identity routes):** `POST /api/v1/external/registration` (047) and `POST /api/v1/external/workforce/link` (048) sit in their own `/api/v1/external` group, with `ExternalCollaboration` but no `CallerPrincipalAuthorizationFilter`. That filter would refuse a not-yet-registered or not-yet-linked caller before the handler runs. Both take no record id and act only on the caller's own identities, so the handler checks WorkforceHome first. They are ledgered `Permanent CallerScopedOnly` (`r3-047`, `r3-048`).
- No handler branches on plane. The plane comes only from validated `iss`/`tid` (`CallerPrincipalResolver.DeterminePlane`, `:386`).
- No OBO on the external plane. Dataverse and SPE access there is app-only, scoped to the composed set; SPE goes through `SpeContainerOwnershipGuard` (NFR-01, FR-24).
- Rights per record: a list or download needs Read, an upload or create needs Create, an update needs Write.
- A delete needs the caller's creator stamp **and** Read on the parent. No level grants deleting someone else's record (FR-26).

**Identity and stamping**
- The sender, creator and requester come from the resolved principal, never from client input (FR-11, FR-25):
  - a systemuser → `sprk_createdbyperson`;
  - a contact → `sprk_createdbycontact`;
  - service requests → `sprk_requestedby`.

**Client auth and routing**
- `api://{customerBffAppId}/user_impersonation` is the only BFF scope on both planes; `access_as_user` is retired (FR-16).
- **Licensed core users** (A8): one single-tenant Spaarke client; authority `https://login.microsoftonline.com/{Spaarke tenant}`; in Teams, NAA then an MSAL popup; no `webApplicationInfo` and no Teams-SSO fallback.
- **Workforce contacts** (A9, browser SPA only): one multitenant Spaarke SPA client, with authority = the customer's home directory from Spaarke-owned data (`organizations` only for discovery). The BFF accepts only `iss` exact + `tid` ∈ the stamp's `CustomerTenantIds` + `aud` = the stamp BFF + a user token + `acct` = 0, keyed on (`tid`, `oid`). Never authorize on email/UPN. Licensed users reach the SPA via the stored link (`tid`, `oid`) → `systemuserid`; there is no "core user sign-in" button.
- Terms: "Spaarke tenant" = Spaarke's Entra directory; "customer's home directory" = the customer's own. Never a bare "tenant" for the second.
- The backend URL and scope come from Spaarke-owned data (the directory, or dev config until T240c ships), never from a link. A crafted link cannot redirect a token (FR-17, NFR-02).

**Hard limits**
- No new secret and no `ClientSecret` credential. A stamp identity never gets `User.Invite.All` (NFR-03). Workforce contacts are not B2B guests, so R3 issues no invites (A9 withdrew A8's guest self-registration).
- No mail is sent "as the user" from R3 surfaces. C6 posts to the thread only (FR-11).

**BFF hygiene and tests**
- Every BFF task follows `.claude/constraints/bff-extensions.md`:
  - a Placement Justification in the PR;
  - publish-size delta measured against a fresh master build;
  - ≤ 60 MB total;
  - no new HIGH CVE;
  - tests in `tests/unit/Sprk.Bff.Api.Tests/` (NFR-04).
- Every FR acceptance case, including the negative ones, has a test (NFR-06, ADR-038).

**Process**
- Deferred work and newly found issues: `/project-defer-issue-tracking` writes `notes/defer-issues.md` AND a GitHub issue — never one without the other.

**ADR tensions approved for this project** (root CLAUDE.md §6.5; `spec.md` §ADR Tensions):
- **ADR-028 A2 (:70)** — path B. Single-tenant Spaarke client, NAA + popup, no SSO fallback, `user_impersonation`. Amendment task 001; it merges before or with the first C8 code task.
- **ADR-028 A3 (:124)** — path B. Default Tier-1 modules by user type (FR-19). Task 001.
- **ADR-028 A1 (:55)** — path B (clarify). The "no B2B guest per external user" rule targets CIAM partners. Workforce contacts are customer staff and become guests (C7). Task 001.
- **ADR-052** — path C. The registration service host is chosen under ADR-052 §6 (task 060). *Lapsed: task 060 is withdrawn under A9.*
- **ADR-028 A9, "authorization never relies on email/UPN"** (task 048) — path A, owner-approved 2026-10-13 (`notes/a9-bff-design.md` §11 item 3: "The UPN consistency check stays").
  - The stored-link route compares the guest token's home UPN with the home bearer's `preferred_username`, as a refuse-only check (`SpaarkeGuestTokenValidator.cs`).
  - It can never grant or choose the link target: the systemuser is found by the proven guest `oid`.
  - It can refuse a genuine user (`upn_mismatch`, known limit K1 in `notes/task-048-notes.md`). Task 074 verifies the claim shapes live.
- **ADR-028 A9** (2026-10-13) — path B, owner-approved. Workforce contacts sign in with their customer's home directory, and the A1 guest clarification and A8 guest self-registration are superseded for them. Applied in PR #1810. Cross-project note: `notes/coordination/2026-10-13-model1-workforce-signin-cross-project.md`.

## 3. Owner directives and standing decisions

Full table: `spec.md` §Owner Clarifications. These still constrain work:

**Scope decisions**
- 2026-10-08 — C6 = thread post + notify members; no outbound email; no attachments. Reaffirmed 2026-10-11 (option A): no email on message post; members are told through the in-portal feed (task 037). Task 036 is the grant email only.
- 2026-10-08 — the in-portal feed is derived from existing grant and message rows; no new table.
- 2026-10-10 — Front Door submitters:
  - workforce by default;
  - CIAM partners behind `ExternalAccess:PartnerServiceRequestsEnabled` (default **off**);
  - no extra partner-only work.
- 2026-10-10 — documents on every core record that allows them, including service requests.

**Delete and creator stamps (2026-10-10)**
- Delete is creator-only in the SPA, for records created in error.
- General delete stays a Dataverse role (`Spaarke Core User` Deep delete), used in the model-driven app.
- Messages are not deletable.
- Delete is a hard delete.
- `sprk_createdbycontact` is the new contact-creator lookup. The delete check reads both stamps.

**Auth and identity**
- 2026-10-09 — `user_impersonation` everywhere.
- 2026-10-09 — modules by user type:
  - workforce (staff and workforce contacts) → `legal-front-door` + `policy-library`;
  - CIAM → `assigned-work`.
- *Superseded 2026-10-13 by A9 (below)* — 2026-10-09: licence-free customer staff self-register through `/join?customer={key}`:
  - approval is automatic (home tenant ∈ `CustomerTenantIds` and `acct = 0`);
  - a shared Spaarke service invites them;
  - the contact is created on first sign-in through the member-test branch (#1563).
- 2026-10-10 — the add-in is for Dataverse-licensed users only. Do not add add-in work here.
- **2026-10-13 — A9 (owner-approved):**
  - Workforce contacts use their company account (home directory). Licensed core users stay B2B guests (A8). Partners stay on CIAM.
  - Licensed users are linked by a stored link (option 1). There is no core-user button.
  - Workforce contacts get no Office add-ins. Teams for workforce contacts is deferred (soft requirement).
  - R3 owns sign-in and registration end to end, including the BFF token acceptance, registration and stored link. UAC owns only the baseline role access.
  - Re-plan: 040 → multitenant browser client, Teams deferred via DEF; 041 → the directory gains the home directory id(s); 056 → profile form; 012 and 060 withdrawn.
  - Licensing approach: **not decided**. Publisher verification is needed before the first production customer (provisioning).
- 2026-10-10 — R3 owns FR-22 (Ciam audience forms) and FR-23 (default-scheme CIAM guard). Provisioning owns `CiamGraphClientFactory` → MI-FIC, H3c, H4b `Ciam:*` and the H13 probes.

**Owner answers 2026-10-12**
- Multi-root visibility: option B. A row linked to several core roots needs Read on every linked **secure** root (`sprk_issecure`); an unreadable non-secure root does not hide it. Unknown secure state counts as secure. Delete paths stay every-parent (FR-26). Invoice threads stay any-parent.
- A closed service request cannot be deleted.
- Invoice document upload needs only the invoice's rights (the union of its parents); no every-parent requirement.
- Bell items: staff with access through a team get grant items; a renewed grant produces an item.
- A workforce contact created at sign-in belongs to the customer's primary business unit (owned by its default team, I-6).
- Intake: department from the contact record; Invention for workforce by default, partners behind a flag (default off); incidents kept simple (alerting deferred); fixed option lists in R3; NDA covers review and issuing our standard NDA. Details: `notes/intake-form-design.md` §6.1.
- Grid columns: the owner wants grids driven by the entity's Dataverse views (savedqueries) with the shared view selector, as in the workspace grids, rather than hard-coded column sets. Approach under discussion (task 002).
- Registration service (060): a shared service is acceptable if it does not conflict with the Model 1 dedicated and segregated customer directive.

**Owner answers 2026-10-13 ("follow your recommendations so we are not blocked")**
- Grid views (task 002 → "use Dataverse views"):
  - "My" views use the assignment/membership resolver;
  - each entity has External Active, My and Inactive views;
  - the internal `ownerid eq-userid` "My" views are logged as an issue (`/project-defer-issue-tracking`) at the start of the grid task.
- Bell items:
  - team access: option A, no bell item; people still get message items on records reached through a team;
  - renewals: option A, accept `modifiedon` noise; no `sprk_renewedon` column.
- ISS-005: option A. Provisioning H4b writes `ExternalAccess:CustomerBusinessUnitId` from H10's BU id. While it is missing, no contact is created (fail safe). Dev sets it by hand.
- 052:
  - Service Requests stays a partner default tab, but only while the partner switch is on (as built);
  - no intake idempotency key is a known limit (Finish is locked while a submit is in flight).
- 053: no Trademark "classes" field (design §4: Legal assigns the classes).
- Licensing: a commercial item, handled outside this project. It does not block R3.
- Mechanical guard against `organizations`/`common`: **not added**.
  - The security control is the BFF's A9 token acceptance; a client-side authority grants nothing.
  - The A9 BFF task carries must-reject tests: an unlisted `tid`, `acct` = 1, an app token, a non-exact `iss` and a wrong `aud`.

**Delivery**
- 2026-10-09 — R3 delivers the production build and package; go-live is gated on T240c/T240d and DNS.
- 2026-10-09 — provisioning builds no new components. The registration service is R3's, or R4's with a written path (task 060).

## 4. Coordination

**Shared external-access BFF surface** (`Api/ExternalAccess/**`, `Infrastructure/ExternalAccess/**`, `Infrastructure/DI/ExternalAccessModule.cs`, `AuthorizationModule.cs`)
- Shared with `unified-access-control-r2`, which is active. Its open PRs touch the same files:
  - #1583, task 181 — `GrantAccessNotifier.cs`, `GrantExternalAccessEndpoint.cs`, `ExternalAccessModule.cs`;
  - #1586, task 179 — `DelegationRuleFilter.cs`, `ExternalAccessEndpoints.cs`.
- Run `/conflict-check` before every BFF PR.
- Task 036 (subsequent-grant email) builds on #1583's `GrantAccessNotifier`, so wait for #1583 to merge.

**`Services/Communication/**`**
- Shared with the messaging, email and notification projects. Open PR #1494 (notification options) touches `Services/Communication`.
- Extend `CommunicationService` / `CommunicationThreadReadService`; never fork them.

**Provisioning (`customer-provisioning-orchestration-r1`)**
- R3 consumes:
  - the T240c directory (workforce + CIAM lookup);
  - T240d (per-customer CIAM audience, keyless provisioner, production CIAM SPA client after spike S1);
  - H3 pre-authorization of the new workforce client;
  - PRQ-C-14 (landed, PR #1589);
  - the per-customer shared mailbox (#1562).
- Coordination notes: `notes/coordination/`.

**A9 responsibilities (2026-10-13)** — `notes/coordination/2026-10-13-model1-workforce-signin-cross-project.md`; messages sent to UAC (-1d), word-add-in (-46) and provisioning (-b2), awaiting confirmations.
- UAC: merges master, corrects round 97, re-scopes 186 to baseline access. It allows R3 to change `WorkforceIdentityOptions.cs`, `WorkforcePrincipalResolver.cs` and `ContactIdentityBinder.cs`, plus a new registration endpoint, with UAC reviewing the PR.
- word-add-in: no change (licensed users on A8); live-tests licensed-guest sign-in from their home Office/Teams.
- provisioning: multitenant BFF apps (`requestedAccessTokenVersion` = 2), the admin-consent link via `knownClientApplications`, the `CustomerTenantIds` intake, publisher verification, and the home directory id in the T240c entry.

**Other projects**
- **`spaarke-auth-system-of-record-r1`:** the auth record and `docs/architecture/SPAARKE-AUTH-ARCHITECTURE.md` are the auth basis. That project also plans an ADR-028 amendment (§12b). Task 001 coordinates so there is one amendment, or two that do not conflict.
- **UAC-r2:** owns A5 (#1567).
- **Issues R3 carries:** #1563 (FR-15), #1566 (FR-18), #1568 (FR-19).
- **CI:** `.github/workflows/deploy-external-spa.yml` and `deploy-teams-app.yml`. Dependabot PR #909 bumps `setup-node` in both; check it before editing them (task 043).

## 5. Environment and live actions

- **Dev:**
  - BFF `spaarke-bff-dev`;
  - SWA `swa-spaarke-external-spa-dev` (`green-dune-0c4f1221e.7.azurestaticapps.net`);
  - Dataverse SPAARKE DEV 1.
- Dev serves both planes from one BFF; every acceptance criterion is testable on dev.
- **Production:** SWA `swa-spaarke-external-spa-prod` / `external.spaarke.com` (exists; DNS by the owner). Production deploy (task 073) is owner-gated and depends on T240c/T240d.
- **Owner approval and a stated rollback before every live change:**
  - app registrations (the new workforce client; moving dev pre-authorizations to `user_impersonation`);
  - Dataverse schema imports;
  - BFF, SWA and Teams package deploys;
  - Graph invitations.
- Live reads are read-only, and the commands are shown first.
- **BFF builds and deploys** run only from a net10 tree (SDK 10.0.101; design §4.5). A net8 deploy to the net10 runtime returns 503. Verify `/healthz` after deploy.
- **NFR-07:** dev lists Spaarke's own tenant in `CustomerTenantIds`; stamps never do. Test C7/FR-15 with a correct customer-tenant configuration, not dev's.
- Test guest for FR-16/FR-21: the owner's existing guest account (`ralph@deweycheatham…`).

## 6. Gotchas — do not re-learn

- **2026-10-11 — do not run the full BFF unit suite while other agents build or test on this machine.** Under that load, host-based contract tests time out (`TaskCanceledException`, about 100 s each): 7 false failures after the 032 merge, and the run took 47 min instead of 26. Run it when the machine is quiet, or re-run the failures in isolation before treating them as real.
- **2026-10-11 — a POML can contradict the spec.** The 036 POML asked for an email on message post, which the 2026-10-08 decision forbids. Check each POML against §1 "Out of scope" and §3 before you implement it, and take any conflict to the owner first.
- **2026-10-10 — `sprk_servicerequest` schema** (live describe, dev): it has `sprk_requestedby` (→ contact), the `sprk_regarding*` family, `sprk_name` (required), `sprk_direction` (required: Inbound/Outbound) and `sprk_recordsummary`. It has **no** `sprk_createdbyperson`, **no** request-type column and **no** intake-answers column (task 020 adds the last two). Documents link to a service request through the existing `sprk_document.sprk_relatedservicerequest` lookup.
- **2026-10-10 — `WizardModal` and `WizardRegistry` no longer exist** (deleted 2026-10-03, reuse audit C-20; `Spaarke.UI.Components/src/components/index.ts:197`). The spec's "WizardModal" means the current framework: `Wizard/WizardShell.tsx` + `InAppWizardHost.tsx`, with `CreateRecordWizard` as the structural pattern.
- **2026-10-10 — the Service Requests module descriptor is fail-closed for CIAM** (`ExternalAccessModule.cs:453-462, descriptor "service-requests"`, predicate `Plane == Workforce && ContactId != Guid.Empty`). FR-06's switch changes that predicate, so test both settings.
- **2026-10-10 — `WorkforceMembershipTest.Evaluate`** (`WorkforceIdentityOptions.cs:199`) checks kind → list → tid → acct, and a guest is denied. FR-15 adds one branch: `acct = 1` AND `tid` = the stamp tenant AND home tenant (`idp`) ∈ `CustomerTenantIds` → Member. Keep the order and every existing deny code.
- **2026-10-11 — the external plane's only DELETE routes are four** (`/documents|/todos|/events/{id}` from task 032, plus `/servicerequests/{id}` from the 2026-10-11 owner decision, `ExternalProjectDataEndpoints.cs`), pinned by `TheExternalPlane_MapsExactlyFourDeleteRoutes`. Adding another means updating that test and the route ledger. Their problem bodies use `reasonCode`; the service-request delete also answers 409 `service_request_closed` (a closed request) and `has_related_records`.
- **2026-10-08 — the guest token shape is proven live:** `tid` = Spaarke, `acct = 1`, `idp` = home tenant. Don't re-investigate it.
- **2026-10-08 — `/organizations` signs a guest into their home tenant**, with a different `oid` and no systemuser match. The workforce authority must be the Spaarke tenant.

## 7. Key documents

- `spec.md` · `design.md` · `plan.md` · `notes/r3-auth-path.md` · `notes/decisions.md` · `notes/defer-issues.md` · `notes/coordination/`
- Auth basis (branch `work/spaarke-auth-system-of-record-r1`): `docs/architecture/SPAARKE-AUTH-ARCHITECTURE.md`, `projects/spaarke-auth-system-of-record-r1/auth-system-of-record.md`
- Applicable ADRs: ADR-028, ADR-001, ADR-008, ADR-010, ADR-024, ADR-034, ADR-007, ADR-009, ADR-012, ADR-021, ADR-050, ADR-052, ADR-038
- Related projects: `spaarke-SPA-external-access-platform-r2`, `unified-access-control-r2`, `customer-provisioning-orchestration-r1`, `spaarke-auth-system-of-record-r1`
