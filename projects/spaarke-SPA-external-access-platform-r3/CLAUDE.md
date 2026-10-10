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
- **C7** workforce-contact self-registration;
- **C8** auth alignment (single-tenant workforce client, run-time backend selection, out-of-plane page fixes, modules by user type, Ciam audience forms, default-scheme CIAM guard, production build);
- **C9** documents on every core record, creator stamps, delete-own.

**Out of scope:** Ask Legal; e-signature; editing core records; new identity planes; CIAM in Teams; Model 2; email on message send; message attachments; the T240c directory and T240d provisioning (provisioning); ADR-028 A5 (UAC-r2).

Status: see `tasks/TASK-INDEX.md` and `current-task.md`. Spec: `spec.md` (source of truth for scope). Design: `design.md`. Plan: `plan.md`.

## 2. Binding rules for this project

**Authorization and data access**
- Every new external-plane route sits in the `/api/v1/external` group (`ExternalCollaboration` policy + `CallerPrincipalAuthorizationFilter`, `ExternalAccessEndpoints.cs:60-63`). It checks the record against the accessible set, fail-closed (NFR-01).
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
- The workforce client:
  - is one single-tenant Spaarke client;
  - uses authority `https://login.microsoftonline.com/{Spaarke tenant}`;
  - in Teams, signs in with NAA then an MSAL popup;
  - has no `webApplicationInfo` and no Teams-SSO fallback.
- The backend URL and scope come from Spaarke-owned data (the directory, or dev config until T240c ships), never from a link. A crafted link cannot redirect a token (FR-17, NFR-02).

**Hard limits**
- No new secret and no `ClientSecret` credential. A stamp identity never gets `User.Invite.All`; invites happen only in the shared registration service (NFR-03, FR-14).
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
- **ADR-052** — path C. The registration service host is chosen under ADR-052 §6 (task 060).

## 3. Owner directives and standing decisions

Full table: `spec.md` §Owner Clarifications. These still constrain work:

**Scope decisions**
- 2026-10-08 — C6 = thread post + notify members; no outbound email; no attachments.
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
- 2026-10-09 — licence-free customer staff self-register through `/join?customer={key}`:
  - approval is automatic (home tenant ∈ `CustomerTenantIds` and `acct = 0`);
  - a shared Spaarke service invites them;
  - the contact is created on first sign-in through the member-test branch (#1563).
- 2026-10-10 — the add-in is for Dataverse-licensed users only. Do not add add-in work here.
- 2026-10-10 — R3 owns FR-22 (Ciam audience forms) and FR-23 (default-scheme CIAM guard). Provisioning owns `CiamGraphClientFactory` → MI-FIC, H3c, H4b `Ciam:*` and the H13 probes.

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

- **2026-10-10 — `sprk_servicerequest` schema** (live describe, dev): it has `sprk_requestedby` (→ contact), the `sprk_regarding*` family, `sprk_name` (required), `sprk_direction` (required: Inbound/Outbound) and `sprk_recordsummary`. It has **no** `sprk_createdbyperson`, **no** request-type column and **no** intake-answers column (task 020 adds the last two). Documents link to a service request through the existing `sprk_document.sprk_relatedservicerequest` lookup.
- **2026-10-10 — `WizardModal` and `WizardRegistry` no longer exist** (deleted 2026-10-03, reuse audit C-20; `Spaarke.UI.Components/src/components/index.ts:197`). The spec's "WizardModal" means the current framework: `Wizard/WizardShell.tsx` + `InAppWizardHost.tsx`, with `CreateRecordWizard` as the structural pattern.
- **2026-10-10 — the Service Requests module descriptor is fail-closed for CIAM** (`ExternalAccessModule.cs:444-455`, predicate `Plane == Workforce && ContactId != Guid.Empty`). FR-06's switch changes that predicate, so test both settings.
- **2026-10-10 — `WorkforceMembershipTest.Evaluate`** (`WorkforceIdentityOptions.cs:199`) checks kind → list → tid → acct, and a guest is denied. FR-15 adds one branch: `acct = 1` AND `tid` = the stamp tenant AND home tenant (`idp`) ∈ `CustomerTenantIds` → Member. Keep the order and every existing deny code.
- **2026-10-10 — there is no `MapDelete` anywhere under `Api/ExternalAccess`.** FR-26 adds the first ones.
- **2026-10-08 — the guest token shape is proven live:** `tid` = Spaarke, `acct = 1`, `idp` = home tenant. Don't re-investigate it.
- **2026-10-08 — `/organizations` signs a guest into their home tenant**, with a different `oid` and no systemuser match. The workforce authority must be the Spaarke tenant.

## 7. Key documents

- `spec.md` · `design.md` · `plan.md` · `notes/r3-auth-path.md` · `notes/decisions.md` · `notes/defer-issues.md` · `notes/coordination/`
- Auth basis (branch `work/spaarke-auth-system-of-record-r1`): `docs/architecture/SPAARKE-AUTH-ARCHITECTURE.md`, `projects/spaarke-auth-system-of-record-r1/auth-system-of-record.md`
- Applicable ADRs: ADR-028, ADR-001, ADR-008, ADR-010, ADR-024, ADR-034, ADR-007, ADR-009, ADR-012, ADR-021, ADR-050, ADR-052, ADR-038
- Related projects: `spaarke-SPA-external-access-platform-r2`, `unified-access-control-r2`, `customer-provisioning-orchestration-r1`, `spaarke-auth-system-of-record-r1`
