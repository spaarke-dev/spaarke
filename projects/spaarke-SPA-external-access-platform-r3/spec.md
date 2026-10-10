# Spaarke External Access Platform R3 — AI Implementation Specification

> **Status**: Ready for owner review (then `/project-pipeline`)
> **Created**: 2026-10-09
> **Source**: `design.md` (owner-confirmed through 2026-10-09) + `notes/r3-auth-path.md`
> **Auth basis**: `spaarke-auth-system-of-record-r1` — `auth-system-of-record.md`, `docs/architecture/SPAARKE-AUTH-ARCHITECTURE.md`, research `working/x09a`–`x09d` (branch `work/spaarke-auth-system-of-record-r1`)

## Executive Summary

R3 turns the external SPA from a read-only portal into a working destination for three audiences:

- **workforce users** — Spaarke staff and Model 1 customer staff, licensed or licence-free — in the browser and the Teams tab;
- **external partners** (CIAM contacts) in the browser.

**What R3 adds:**

- a record detail surface, with messages and events;
- the four Legal Front Door intake wizards;
- notifications on new grants, by email and in the portal;
- refined grid columns;
- sending messages on accessible records;
- self-registration for licence-free customer staff.

**Auth.** R3 also aligns the SPA and Teams sign-in with the decided auth path:

- one single-tenant Spaarke client;
- Spaarke-tenant authority;
- `user_impersonation`;
- run-time selection of each customer's backend.

## Scope

### In Scope

- **C1 — Record detail surface.** Opening a row in any SPA/Teams grid shows read-only fields, related documents/invoices, messages, and events/tasks. Every section is Tier-2 authorized.
- **C2 — Legal Front Door intake wizards.** Four wizards, workforce only: NDA Assessment, Invention Submission, Policy Question, Trademark Search. Each creates `sprk_servicerequest`.
- **C3 — Subsequent-grant notification.**
  - An email with a portal deep link when an already-onboarded contact is granted another record.
  - An in-portal notification feed derived from existing data (no new table).
- **C4 — Grid column refinement.** Data-only `sprk_gridconfiguration` changes; the input is the owner's per-tab column list.
- **C5 — Teams parity, workforce plane.** C1–C4 in the Teams tab.
- **C6 — Message send.** A thread post plus a notification to the record's members. No outbound email from the sender. Tier-2 is enforced on send.
- **C7 — Workforce-contact self-registration (join link).**
  - The join page.
  - A shared Spaarke registration service that invites the B2B guest and adds them to the customer group.
  - A stamp-side member-test change that creates and binds the contact on first sign-in.
- **C9 — Documents, creator attribution, delete-own.** Documents on every core record that allows them, including service requests; server-stamped creator on every SPA create; users delete only what they created.
- **C8 — Auth alignment.**
  - The workforce client switch.
  - Run-time backend selection on both planes.
  - A fix for the SPA pages that bypass the plane seam (#1566).
  - Default modules by user type (#1568).
  - The production build and production Teams package.
  - The Teams live check.

### Out of Scope

- Ask Legal assistant (FR-26 preview).
- E-signature.
- Editing core records (Matter, Project, Work Assignment and similar) in the portal.
- New identity planes.
- CIAM partners in Teams (not possible).
- Model 2 (customer-tenant) auth.
- Outbound email on message send.
- Attachments on message send (deferred).
- **Built by other projects**, which R3 consumes as dependencies:
  - the T240c directory service (provisioning);
  - the T240d CIAM-on-stamps provisioning work — `Ciam:*` settings via H4b, H3c service-principal provisioning, the keyless `CiamGraphClientFactory` (MI-FIC, `User.Create`), H13 probes (provisioning). *(The two BFF auth changes moved to R3 — FR-22/FR-23.)*
  - ADR-028 A5 / the impersonated record set (UAC-r2 task 036, #1567).

### Affected Areas

- `src/client/external-spa/src/` — host, auth (`auth/msal-config.ts`, `msal-auth.ts`, `standalone-plane.ts`, `host/TeamsHostAdapter.ts`), API clients (`api/`), pages, widget/quick-start registries, the new join page, the detail view, and the notification surface.
- `src/client/external-spa/appPackage/` — Teams manifest (dev and prod).
- `src/client/shared/Spaarke.UI.Components/` — reused modal, wizard and grid components; extended only where the external host needs it.
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/` and `Infrastructure/ExternalAccess/`:
  - new external-plane endpoints (message read, message send, notification feed);
  - the `ModuleEntitlementResolver` and `WorkforceIdentityOptions` / `ContactIdentityBinder` changes.
- `src/server/api/Sprk.Bff.Api/Services/Communication/` — reuse the send pipeline behind the new endpoint.
- The grant notification path (`invite-and-grant` / grant endpoint) — extend for subsequent grants.
- The shared registration service host (placement per ADR-052; see Unresolved Questions).
- `sprk_gridconfiguration` records (C4); `sprk_servicerequest` schema (C2).
- `.github/workflows/deploy-external-spa.yml`, `deploy-teams-app.yml` — production build and package.

## Requirements

### Functional Requirements

#### C1 — Record detail

- **FR-01: open a row into a read-only detail view.** Grid row-open in the Xrm-free host opens a read-only detail view in `SprkModal` + `RecordNavigationModalShell` ("1 of N" browse).
  - Applies to Matter, Project, Work Assignment, Document, Invoice and Service Request rows.
  - Acceptance:
    - clicking a row opens the view with read-only fields;
    - next/previous moves through the grid's rows;
    - a record outside the caller's accessible set returns 403 and shows a denied state.
- **FR-02: documents and invoices.** The detail view shows related documents/invoices from the existing R2 read path (task 028); no new server read. Acceptance: the lists match the tab rollup for the same record.
- **FR-03: events and tasks.** The detail view shows events/tasks through the existing external-plane `EventsCalendar` / `SmartTodo` reads. Acceptance: shown for an accessible record; empty state otherwise.
- **FR-04: messages (new endpoint).** A new external-plane endpoint returns the record's communication thread.
  - The record must be in the caller's accessible set (`IAccessibleRecordSetService`), fail-closed.
  - Internal-only messages are hidden from callers flagged external.
  - Acceptance:
    - an accessible record returns its thread;
    - an inaccessible record returns 403;
    - a CIAM caller never sees internal-only messages.
  - Also covers the caller's **own service requests** (scoped by `sprk_requestedby`, not the root set), so a submitter can read the questions asked on their request (owner 2026-10-10: "answer questions").

#### C2 — Legal Front Door

- **FR-05: four intake wizards.** One `WizardModal` intake wizard per request type (NDA Assessment, Invention Submission, Policy Question, Trademark Search) writes a `sprk_servicerequest`.
  - Acceptance:
    - each wizard opens from its quick-start card and from "More Services";
    - submitting creates a row that appears in the Service Requests tab.
- **FR-06: workforce by default; partners behind a switch (owner 2026-10-10).** Intake is workforce-only by default.
  - A per-customer setting (`ExternalAccess:PartnerServiceRequestsEnabled`, default **off**) lets CIAM partners submit and read their own service requests. When it is on:
    - the Service Requests module's accessible set includes the CIAM contact's own requests;
    - the intake endpoint accepts the CIAM plane;
    - partners' default modules add `legal-front-door`.
  - Owned like workforce submissions; no extra partner-only work.
  - Acceptance:
    - switch off → a CIAM caller cannot launch or submit (403 server-side) and sees 0 rows;
    - switch on → a CIAM caller submits, sees only their own requests, and never sees anyone else's.

#### C3 — Notifications

- **FR-07: email on a subsequent grant.** A `/grant` to an already-onboarded contact sends a "you've been given access to {record}" email.
  - Deep link: `https://external.spaarke.com/?customer={key}&record={type}:{id}`. It never carries a BFF URL or scope (T240d).
  - Acceptance: a grant to an onboarded contact sends exactly one email; the link opens the record detail after sign-in.
- **FR-08: in-portal notification feed.**
  - A new external-plane endpoint derives the feed from existing data, with no new table:
    - recent grants (`sprk_externalrecordaccess.sprk_granteddate`);
    - new C6 messages on accessible records.
  - A per-user "last seen" marker drives unread state.
  - The SPA shows the feed with an unread count.
  - Acceptance:
    - new grants and messages appear;
    - marking seen clears the unread count;
    - records outside the set never appear.

#### C4 — Grid columns

- **FR-09: owner-approved columns.** The `sprk_gridconfiguration` column sets match the owner's per-tab list (Projects, Matters, Work Assignments, Documents, Invoices, Service Requests). Acceptance: each tab shows exactly the approved columns.

#### C5 — Teams parity

- **FR-10: C1–C4 in Teams.** C1–C4 work in the Teams tab for workforce users. Acceptance: same behaviour as the browser for a licensed user and for a workforce contact.

#### C6 — Message send

- **FR-11: send endpoint.** A new external-plane send endpoint reuses `CommunicationService` (extend, don't fork).
  - It creates a `sprk_communication` on the record's thread, with the regarding record per ADR-024.
  - Recipients are the record's members, told through the C3 path.
  - The record must be in the caller's accessible set (fail-closed).
  - The sender is stamped from the resolved principal, never from client input.
  - No outbound email (no OBO `/me/sendMail` — CIAM contacts and Model 1 guests have no mailbox it can use; #1464).
  - Acceptance:
    - a send on an accessible record appears in that record's thread;
    - a send on a record outside the set returns 403 (negative test);
    - the sender equals the caller.
  - Also allowed on the caller's **own service requests** (answering a question asked on the request).

#### C7 — Workforce-contact self-registration

- **FR-12: spike, first.** Compare Entra's built-in B2B self-service sign-up user flow with a custom join flow. Adopt the built-in flow if it covers FR-13–FR-15 more simply. Acceptance: a written decision with evidence.
- **FR-13: join page.**
  - Route: `https://external.spaarke.com/join?customer={key}`.
  - The user signs in with their home company account through a registration-only client that reads only their profile.
  - The page then hands off to the normal workforce sign-in (Spaarke-tenant authority).
  - Acceptance: a new employee of an allow-listed company goes from link to signed-in SPA in one flow, with no admin step.
- **FR-14: shared registration service.**
  1. Verify the caller's home tenant is on that customer's `CustomerTenantIds` and `acct = 0`.
  2. Invite them as a B2B guest into Spaarke's tenant (Graph invitations, no email, no licence).
  3. Add the guest to `sprk-{customerId}-users`.

  The service is idempotent: a re-registration does not duplicate. It holds `User.Invite.All` + group write in Spaarke's tenant — never a stamp identity.

  Acceptance:
  - an allow-listed member is registered automatically;
  - a non-listed tenant, or someone else's guest (`acct = 1`), is refused with a clear message.
- **FR-15: contact created on first sign-in (stamp side, #1563).**
  - `WorkforceMembershipTest` gains a branch: `acct = 1` AND `tid` = the stamp tenant AND home tenant (`idp`) ∈ `CustomerTenantIds` → `Member`.
  - The existing binder then email-binds or creates the contact keyed by the guest `oid`.
  - Every other guest stays denied.
  - Acceptance:
    - a registered guest's first SPA/Teams sign-in creates or binds exactly one contact and grants workforce-contact access;
    - a guest from a non-listed home tenant is denied (`workforce_tenant_not_customer`);
    - Spaarke staff are unaffected.

#### C9 — Documents, creator attribution and delete-own (owner 2026-10-10)

- **FR-24: documents on every core record that allows them.**
  - Covers the record types `sprk_document` links to: Project (exists today), Matter, Work Assignment, Invoice, and **Service Request** (workforce submitters; partners when the FR-06 switch is on).
  - Users can list, view/download and upload documents in the SPA.
  - Every route checks Tier-2 on the parent: Read to list/download, Create to upload. A service request is checked through the submitter scope.
  - SPE access is app-only through `SpeContainerOwnershipGuard`; no OBO.
  - Service requests have no document lookup today. Link them through the ADR-024 regarding model (`sprk_regardingrecordid`), or add a `sprk_servicerequest` lookup if that model needs one (§11 below).
  - Acceptance:
    - for each type, upload then download round-trips;
    - a ViewOnly caller cannot upload (403);
    - a record outside the set returns 403.
- **FR-25: creator attribution on every SPA create.**
  - Every create through `/api/v1/external/**` stamps who created it:
    - a systemuser principal → `sprk_createdbyperson` (existing; FLS-secured lookup to systemuser);
    - a contact principal (workforce contact or partner) → a **new contact-creator lookup**.
  - Covers documents, to-dos, events, communications and service requests (service requests already carry `sprk_requestedby`).
  - Server-side only, never from client input.
  - This closes auth record L-5 (no creator attribution): today SPA creates are app-only, so Dataverse `createdby` is always the BFF application user.
  - Acceptance: each create records the calling principal; a client-supplied creator is ignored.
- **FR-26: delete your own records.**
  - New DELETE routes for documents (Dataverse row + SPE file, app-only through the guard), to-dos, events and service requests.
  - A delete is allowed only when the FR-25 stamp equals the caller **and** the caller still holds Read on the parent.
  - Nobody can delete another person's record, whatever their level.
  - Records created before FR-25 have no stamp, so they are not deletable from the SPA.
  - Acceptance:
    - the creator deletes their own record;
    - a different user, even with FullAccess, gets 403;
    - a document delete removes the SPE file.
- **"Answer questions" (owner 2026-10-10).** Covered by:
  - **forms** — C2 wizards;
  - **replies on a record or own service request** — C6/FR-04/FR-11;
  - **completing a to-do** — the existing `PATCH /api/v1/external/todos/{id}`, Write-checked.

  Richer tasks and questionnaires are future scope.

#### C8 — Auth alignment

- **FR-16: workforce client (browser and Teams).**
  - A dedicated single-tenant Spaarke client in Spaarke's tenant.
  - Authority `https://login.microsoftonline.com/{Spaarke tenant}`.
  - Scope `api://{customer BFF app}/user_impersonation`.
  - Teams: NAA first, then MSAL popup fallback; no `webApplicationInfo`; no Teams-SSO fallback.
  - `access_as_user` is retired.
  - Acceptance: a member and a guest sign in on browser and Teams (desktop and web), and the token shows `tid` = Spaarke, `aud` = the target BFF app, `scp` = `user_impersonation`.
- **FR-17: run-time backend selection on both planes.**
  - Workforce: from the T240c directory.
  - CIAM: from the invite key through the directory's CIAM lookup.
  - No baked BFF URL or scope. Dev keeps one configured backend until 240c ships.
  - Known customer keys are kept, and a picker appears when there are several.
  - Acceptance: the same build reaches two different backends by selection; a crafted link cannot redirect a token.
- **FR-18: fix out-of-plane pages (#1566).**
  - `DocumentUploadPage` (OBO route), `PlaybookLibraryPage` (unmapped route) and `SemanticSearch` (default-scheme route) go through `/api/v1/external/**` with the active plane's token acquirer.
  - The production mock identity on 401/403 is removed.
  - Acceptance: the pages work for both planes; a 401/403 shows a sign-in or denied state, never a mock user.
- **FR-19: default modules by user type (#1568).** `ModuleEntitlementResolver` returns, by principal type:
  - workforce (systemuser or workforce contact) → `legal-front-door` + `policy-library`;
  - CIAM → `assigned-work`, plus `legal-front-door` when the FR-06 partner switch is on.

  `sprk_approlemodulemap` stays only for optional extras. Acceptance: a workforce user sees the Legal Front Door and the Policy Library; a partner sees Assigned Work.
- **FR-20: production build and package (design actions A1, A2, A4).**
  - A production SPA build for `external.spaarke.com`.
  - A production Teams app package with its own manifest.
  - Redirects `https://external.spaarke.com` and `brk-multihub://external.spaarke.com`.
  - Acceptance: the artefacts build in CI. Go-live is gated (Dependencies).
- **FR-22: `Ciam` scheme audiences (owner decision 2026-10-10).** The `Ciam` JwtBearer scheme accepts the per-customer audience in both forms, `{bffAppId}` (bare GUID, v2 tokens) and `api://{bffAppId}`. Acceptance:
  - a CIAM token for this stamp's app passes in either form;
  - a token for another customer's app returns 401 (T240d S1 (iv) shape).
- **FR-23: default-scheme guard against CIAM tokens (owner decision 2026-10-10; auth review R1).** The default (workforce) scheme rejects any token whose `iss` contains `ciamlogin.com` or whose `tid` equals `Ciam:TenantId`. This mirrors `CallerPrincipalResolver.DeterminePlane` and does not rely on library issuer defaults. Acceptance: a CIAM token on a workforce-only route returns 401 (the H13 hard-gate probe); workforce and Copilot tokens are unaffected.
- **FR-21: Teams live check.** Use the owner's test guest, on Teams desktop and web, to confirm FR-16 against a stamp app, including whether Microsoft's broker must be pre-authorized. Acceptance: a written result; the H3 pre-authorized list is updated through provisioning if needed.

### Non-Functional Requirements

- **NFR-01 — Tier-2 on every new endpoint.** Every new external-plane endpoint authorizes through `CallerPrincipalAuthorizationFilter` + the accessible set, fail-closed. No handler branches on plane. There is no OBO on the external plane.
- **NFR-02 — tenant isolation.** No token is sent to a host or scope taken from a URL. Customer routing comes only from Spaarke-owned data.
- **NFR-03 — no secrets.** No new secret anywhere. The registration service uses a managed identity / federated credential (ADR-028 A4/A6).
- **NFR-04 — BFF hygiene (§10).** Placement justification; publish-size delta per BFF task; no new HIGH CVE; tests in `tests/unit/Sprk.Bff.Api.Tests/`; net10 build (design §4.5).
- **NFR-05 — UI standards.** Fluent v9 (ADR-021), dark mode, the canonical modal shell (ADR-050), and shared components (ADR-012).
- **NFR-06 — testing.** Tests follow ADR-038. Each FR acceptance case, including the negative cases, has a test.
- **NFR-07 — dev configuration.** Dev differs from stamps (it lists Spaarke's own tenant in `CustomerTenantIds`). C7 and FR-15 are tested against a correct customer-tenant configuration, never dev's.

## Technical Constraints

### Applicable ADRs

- **ADR-028** — auth architecture (A1 CIAM plane, A2 Teams, A3 dual plane, A4 credentials, A6 keyless). **Amendment required** (see ADR Tensions).
- **ADR-001** — Minimal API.
- **ADR-008** — authorization by endpoint filters.
- **ADR-010** — DI minimalism.
- **ADR-024** — polymorphic regarding (service requests, communications).
- **ADR-034** — membership (current basis of the system-user record set).
- **ADR-007** — SPE file store facade for document access.
- **ADR-009** — Redis caching (feed / last-seen).
- **ADR-012** — shared components.
- **ADR-021** — Fluent v9.
- **ADR-050** — canonical modal shell.
- **ADR-052** — workload placement (registration service host).
- **ADR-038** — testing strategy.

### MUST Rules

- ✅ MUST authorize every external-plane route with the `ExternalCollaboration` policy + `CallerPrincipalAuthorizationFilter`, and check the record against the accessible set (fail-closed).
- ✅ MUST pick the plane only from validated `iss`/`tid` (existing `DeterminePlane`).
- ✅ MUST use `user_impersonation` as the only requested BFF scope on both planes.
- ✅ MUST resolve the backend URL and scope from Spaarke-owned data (directory), never from a link.
- ✅ MUST keep external-plane Dataverse/SPE access app-only, scoped to the composed set.
- ❌ MUST NOT exchange the caller token (OBO) on the external plane.
- ❌ MUST NOT add a new identity plane, a new secret, or a `ClientSecret` credential.
- ❌ MUST NOT send mail "as the user" (OBO) from R3 surfaces.
- ❌ MUST NOT give a stamp identity `User.Invite.All`; invites happen only in the shared registration service.

### Existing Patterns

- External-plane endpoint + filter: `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalModuleDataEndpoints.cs`.
- Principal resolution and member test: `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs`, `WorkforcePrincipalResolver.cs`, `WorkforceIdentityOptions.cs`, `ContactIdentityBinder.cs`.
- Module entitlement: `Infrastructure/ExternalAccess/ModuleEntitlementResolver.cs`.
- Send pipeline: `Services/Communication/CommunicationService.cs` (shared-mailbox/app-only branch) and `CommunicationRecordAuthorizationFilter.cs`.
- SPA run-time auth seams: `src/client/external-spa/src/auth/` (`setActiveBffTokenAcquirer`, `setActiveLoginScope`, `workforceAuthorityConfig({authority})`).
- Office add-in sign-in to copy: `src/client/shared/Spaarke.Auth/src/strategies/OfficeNaaStrategy.ts`; `src/client/office-addins/shared/services/AuthService.ts`.
- Wizards: `WizardModal` / `WizardRegistry` / `CreateRecordWizard` in `@spaarke/ui-components`.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>                 <!-- C1 message read, C3 notify + feed, C6 send, C7 member-test branch, C8 module defaults -->
  <spaarkeai>N</spaarkeai>
  <ci-workflows>Y</ci-workflows> <!-- FR-20 production SPA build + Teams package -->
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**BFF placement.** All BFF additions are external-plane endpoints, or modifications inside `Infrastructure/ExternalAccess`, per `.claude/constraints/bff-extensions.md`. The per-task publish-size checks and the ≤ 60 MB ceiling apply. No background work is added to the BFF.

**Registration service.** It is **not** in the BFF: stamps cannot hold `User.Invite.All`. Its host is chosen under ADR-052 (see Unresolved Questions).

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost of doing nothing |
|---|---|---|---|
| External message read endpoint (FR-04) | `CommunicationsWorkspaceWidget` / thread reads are internal-plane, impersonated as a systemuser | No: the external plane needs app-only reads under a Tier-2 check, and contacts have no systemuser | The detail view cannot show messages (G1) |
| External message send endpoint (FR-11) | `CommunicationService` send pipeline | Yes: extend the pipeline; only a new external-plane entry point is added | A granted user cannot send a message (G6) |
| Notification feed endpoint (FR-08) | `appnotification` (systemusers only); grant rows | Derive from existing grant + message rows; no new entity | Users have no in-portal notice (G3) |
| Subsequent-grant email (FR-07) | `invite-and-grant` email | Yes: extend onto the `/grant` path | An onboarded contact is never told about a new record (UAT 6A) |
| Member-test branch (FR-15) | `WorkforceMembershipTest.Evaluate` | Yes: modify (one branch) | Licence-free customer staff cannot sign in on any Model 1 stamp |
| Default modules by type (FR-19) | `ModuleEntitlementResolver` | Yes: modify | Workforce users see no Front Door / Policy Library modules |
| Join page (FR-13) | none in `external-spa` | No existing page | Licence-free staff have no way to get access |
| Shared registration service (FR-14) | None in the repo. H11 (L2) invites guests at provisioning time, but only for a known list, and owner direction says provisioning does not own new components | Possibly co-hosted with the T240c directory; to be decided | Self-registration (owner requirement) is impossible without a Spaarke-tenant invite identity |
| Contact-creator lookup column (FR-25) on document, to-do, event, communication | `sprk_createdbyperson` exists but targets systemuser only (FLS-secured) | No: a systemuser lookup cannot hold a contact. A polymorphic retype of `sprk_createdbyperson` would break existing readers | Contacts can never delete their own mistaken records; SPA creates carry no author (L-5) |
| Document routes for Matter / WA / Invoice / Service Request (FR-24) | Project document routes in `ExternalProjectDataEndpoints.cs:193` | Yes: generalize the project routes over record type | Users cannot attach documents to most records (owner requirement) |
| DELETE routes (FR-26) | none on the external surface | New. Gated on the creator stamp + parent Read | Users cannot remove records they created in error |
| Partner service-request switch (FR-06) | Service Requests module descriptor (`ExternalAccessModule.cs:444`) | Yes: a configuration branch in the existing descriptor | (Optional) partners cannot submit requests where a customer wants it |
| Workforce client app registration (FR-16) | The dev BFF app doubles as the client today | No: the client must be decoupled from per-customer backends | Teams/SPA cannot sign users into per-customer backends |

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-028 A2 (:70) | "MUST authenticate Teams users via Teams SSO / NAA against a **multitenant** app" | R3 uses a single-tenant Spaarke client, Spaarke-tenant authority, NAA + popup, no SSO fallback | **B — amend** | Owner decision 2026-10-08/09. Matches the add-in pattern proven live for guests. A multitenant client is needed only for Model 2 |
| ADR-028 A3 (:124) | "MUST NOT infer Tier-1 entitlement from the plane" | FR-19 gives default modules by user type | **B — amend** | Owner decision 2026-10-09 (option b): scales with no per-user setup; the CIAM blanket was already the owner's choice |
| ADR-028 A1 (:55) | "MUST NOT provision a B2B guest per external user" | C7 creates one B2B guest per workforce contact | **B — clarify** | The rule targets CIAM partners (external users). Workforce contacts are customer staff, the same population as H11 guests. The amendment states the scope explicitly |
| ADR-052 | Workload placement for a new host | The registration service needs a shared Spaarke-tenant host | **C — comply** | The host is chosen under ADR-052 §6 (stamp UAMI pattern not applicable; shared-platform identity) |

Amendment source: auth record §12b + `notes/r3-auth-path.md`. The amendment is merged before or with the first C8 code task.

## Success Criteria

1. [ ] A partner (browser) and a workforce user (browser and Teams) open a granted record into the detail view with fields, docs/invoices, messages, and events/tasks; an ungranted record is denied. Verify: E2E on dev, plus a negative test.
2. [ ] Each quick-start card opens its wizard; submitting creates a visible `sprk_servicerequest`; a CIAM caller is refused. Verify: E2E + a server test.
3. [ ] A grant to an onboarded contact sends one email whose link opens the record, and appears in the in-portal feed. Verify: E2E + an email capture.
4. [ ] Grid columns match the owner's list. Verify: owner review.
5. [ ] A message send on an accessible record appears in the thread; a send outside the set returns 403. Verify: a server negative test + E2E.
6. [ ] A new employee of an allow-listed company self-registers from the join link and lands signed in; a non-listed tenant is refused. Verify: E2E with a test tenant.
7. [ ] Browser and Teams sign-in use the single-tenant client, Spaarke-tenant authority and `user_impersonation`, for both a member and a guest. Verify: token diagnostics + the FR-21 live check.
8. [ ] One SPA build reaches two different backends by run-time selection. Verify: a dev test with two configured entries.
9. [ ] No SPA page leaves the external plane; no mock identity in production. Verify: code test + E2E.
10. [ ] Documents upload/download on Project, Matter, Work Assignment, Invoice and Service Request; ViewOnly cannot upload. Verify: server tests + E2E.
11. [ ] A user deletes a record they created; another user (even FullAccess) gets 403. Verify: server negative tests.
12. [ ] With the partner switch off, partners cannot submit service requests; with it on, they see only their own. Verify: server tests both ways.

## Dependencies

### Prerequisites

- net10 BFF build environment (design §4.5).
- ADR-028 amendment (A2, A3, A1 clarification) merged before or with the C8 code.
- The owner's per-tab column list (C4 task input).
- A test guest account (`ralph@deweycheatham…`, exists) for FR-21 and the guest cases of FR-16.

### External

- **T240c directory service** (workforce lookup + CIAM lookup) — required for production routing (FR-17). Dev runs without it.
- **T240d CIAM on stamps** (provisioning; spike S1 first) — required for partner features on provisioned customers. Includes the production CIAM SPA client (redirect `https://external.spaarke.com`), keyless contact-account creation and the H13 probes. The BFF audience forms and the guard are R3 (FR-22/FR-23).
- **H3 pre-authorization** of the new workforce client on every customer BFF app (provisioning platform list).
- **PRQ-C-14** — customer tenant allows outbound B2B collaboration. **Added** by provisioning (PR #1589): a customer attestation `customerOutboundB2BAttested`, required for every B2BGuest (Model 1) run.
- **Production SWA** `swa-spaarke-external-spa-prod` / `external.spaarke.com` (exists; DNS by the owner).
- **Per-customer shared mailbox** (owner-approved provisioning step, #1562). It is the sender for C3's notification emails on stamps. C6 sends no email.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Front Door types | Which request types? | All four (2026-08-12) | FR-05 |
| Detail depth | What does detail include? | Fields, docs/invoices, messages, events/tasks; read-only (2026-08-12) | FR-01–04 |
| Notification channel | How? | Email (deep link) + in-portal (2026-08-12) | FR-07–08 |
| In-portal storage | New table? | No — a derived feed (2026-10-08) | FR-08 |
| Grid columns | Spec now? | Parameterized; an early task with owner input (2026-10-08) | FR-09 |
| Messages | View or send? | Send — thread post + notify members, no email (2026-10-08) | FR-11 |
| C2 submitters | Who? | Workforce by default; partners behind an on/off switch if cheap (2026-10-10, refines 2026-10-08) | FR-06 |
| Delete | Who may delete? | Only the creator, for records created in error; never someone else's (2026-10-10) | FR-25, FR-26 |
| Documents | Where? | Every core record that allows documents, including service requests (2026-10-10) | FR-24 |
| "Answer questions" | Meaning? | Answering questions, submitting forms; completing tasks later (2026-10-10) | C2, C6, to-do update |
| SPA access levels | CRUD? | All users can have CRUD; some may be read-only — auth must not prevent CRUD (2026-10-10) | Grant levels (ViewOnly / Collaborate / FullAccess) + FR-26 |
| Add-in | Who? | Dataverse-licensed users only (2026-10-10) | Server already requires a systemuser on `/api/office/*` |
| Teams for partners | In scope? | No — browser only (2026-10-07) | FR-10 |
| Scope name | `access_as_user` or `user_impersonation`? | `user_impersonation` everywhere (2026-10-09) | FR-16 |
| Workforce-contact access | Pre-created or self-service? | Self-registration via a customer-shared link; automatic approval (2026-10-09) | C7 |
| Contact creation | When? | During self-registration, on the first sign-in that ends the join flow, via the member-test branch (2026-10-09) | FR-15 |
| Registration service owner | Provisioning? | No — R3 builds it, or R4 with a written path (2026-10-09) | FR-14 |
| Built-in Entra sign-up | Use it? | Yes, if available and simpler (2026-10-09) | FR-12 |
| Customer IT setting | Handling? | A required customer approval (PRQ-C-14) (2026-10-09) | Dependencies |
| Modules | App roles or by type? | By user type, option (b) (2026-10-09) | FR-19 |
| Partner-token BFF changes | R3 or T240d? | **R3** — owner decision 2026-10-10, relayed by provisioning; supersedes the 2026-10-09 assumption | FR-22, FR-23 |
| Default module sets | Staff the same as workforce contacts? | Yes (2026-10-09) | FR-19 |
| Deep-link target | Where? | Record detail via `?customer=&record=` (2026-10-09) | FR-07 |
| Production | R3 delivers? | Yes — build + package; go-live gated (2026-10-09) | FR-20 |

## Assumptions

- **Notification copy**: drafted in the C3 task; the owner approves the wording.
- **Feed retention**: the feed shows the last 30 days of grants/messages. *Affects FR-08 query cost; confirm in the task.*
- **Messages are not deletable**: sent messages notify the record's members, so FR-26 excludes communications. Owner to confirm.
- **Delete is a hard delete** of the user's own row (and SPE file for documents), not a deactivate. Owner to confirm.
- **Picker storage**: known customer keys are kept in `localStorage`; a new device re-uses the invite or join link (T240d §4).

## Unresolved Questions

- [ ] **Registration service host (FR-14).** Recommendation: co-host with the T240c directory service (both are shared Spaarke-tenant services with no customer data). However, T240c is planned as a provisioning task, and the owner has said provisioning does not build new components.
  - Decide who builds the directory and where the shared host lives (ADR-052).
  - If the shared host is not ready when R3 reaches FR-14, FR-14 moves to **R4** with this spec section as its starting point, and C7 ships the join page + FR-15 behind a feature flag.
  - Blocks: FR-14 build order only.
- [ ] **Teams broker pre-authorization (FR-21).** Whether stamp apps must pre-authorize Microsoft's broker for NAA. Blocks: nothing until the FR-21 live check; the outcome may add one H3 platform-list entry.

---

*AI-optimized specification. Original design: `design.md`. Auth path: `notes/r3-auth-path.md`.*
