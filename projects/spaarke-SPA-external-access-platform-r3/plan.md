# Project Plan: Spaarke External Access Platform R3

> **Last Updated**: 2026-10-10
> **Status**: Ready for execution
> **Source**: `spec.md` (FR-01–FR-26, NFR-01–07)

## 1. Executive Summary

**Purpose.** Make the external SPA and the Teams tab a working destination for workforce users (staff, licensed Model 1 guests, licence-free customer staff) and CIAM partners. Align sign-in with the decided auth path.

**Scope.** C1–C9 per `spec.md`. Out of scope: Ask Legal, e-signature, core-record editing, new identity planes, CIAM in Teams, Model 2, email on message send, message attachments, T240c/T240d (provisioning), A5 (UAC-r2).

**Estimated effort.** 30–40 working days across 33 tasks. About a third of that is security-sensitive BFF work on the shared external-access surface.

## 2. Architecture Context

### Design Constraints
- **External plane.** Every new route sits in `/api/v1/external` (`ExternalAccessEndpoints.cs:60-63`: `ExternalCollaboration` policy + `CallerPrincipalAuthorizationFilter`). Each route checks the accessible set, fail-closed, and reads and writes app-only with no OBO (NFR-01, ADR-008, ADR-028 A3).
- **Stamping.** The plane comes only from validated `iss`/`tid` (`CallerPrincipalResolver.DeterminePlane`, `:386`). Sender, creator and requester come from the resolved principal (FR-11, FR-25).
- **Scope and backend.** `user_impersonation` on both planes. The backend URL and scope come from Spaarke-owned data, never from a link (FR-16, FR-17, NFR-02).
- **Secrets.** None new, and no `ClientSecret` (NFR-03). A stamp identity never gets `User.Invite.All` (FR-14).
- **BFF hygiene (§10).** Placement justification, publish-size delta, ≤ 60 MB, no new HIGH CVE, tests (NFR-04). Build and deploy only from a net10 tree (design §4.5).
- **UI.** Fluent v9 with dark mode (ADR-021), the canonical modal shell (ADR-050), shared components (ADR-012).

### Key Technical Decisions
- **One single-tenant Spaarke workforce client** for browser and Teams:
  - authority = Spaarke tenant;
  - Teams signs in with NAA, then an MSAL popup;
  - no `webApplicationInfo`, no SSO fallback.
  - Mirrors the Office add-in (`notes/r3-auth-path.md` §2).
- **Run-time backend selection seam** on both planes:
  - dev keeps one configured backend until T240c ships;
  - known customer keys are kept in `localStorage`, with a picker when there are several.
- **Feed, no new table.** The in-portal feed is derived from `sprk_externalrecordaccess.sprk_granteddate` and C6 messages; the "last seen" marker is kept in Redis (ADR-009; `notes/decisions.md`).
- **Creator stamps.**
  - Systemuser callers → `sprk_createdbyperson` (exists on document, to-do, event and communication).
  - Contact callers → new `sprk_createdbycontact`.
  - Service requests → `sprk_requestedby`.
- **Service-request documents** link through the existing `sprk_document.sprk_relatedservicerequest` lookup (live describe, dev).
- **Intake schema.** `sprk_servicerequest` has no request-type or intake-answers column; task 020 adds `sprk_requesttype` and `sprk_intakedata`.
- **Wizards** use `Wizard/WizardShell` + `InAppWizardHost` (`WizardModal`/`WizardRegistry` were deleted 2026-10-03).
- **Partner service-request switch** `ExternalAccess:PartnerServiceRequestsEnabled`, default off. It changes the Service Requests descriptor predicate (`ExternalAccessModule.cs:444-455`), the intake endpoint, and the CIAM default modules.

### Discovered Resources

**ADRs**
| File | Topic |
|---|---|
| `.claude/adr/ADR-028-spaarke-auth-architecture.md` | Auth, A1–A6; amended by task 001 |
| `ADR-001` | Minimal API |
| `ADR-008` | Endpoint filters |
| `ADR-010` | DI minimalism |
| `ADR-024` | Polymorphic regarding |
| `ADR-034` | Membership |
| `ADR-007` | SPE facade |
| `ADR-009` | Redis |
| `ADR-012` | Shared components |
| `ADR-021` | Fluent v9 |
| `ADR-050` | Modal shell |
| `ADR-052` | Workload placement |
| `ADR-038` | Testing |

**Constraints:** `.claude/constraints/auth.md`, `api.md`, `bff-extensions.md`, `testing.md`, `data.md`, `pcf.md` (frontend rules).

**Patterns:** `.claude/patterns/auth/spaarke-sso-binding.md`, `oauth-scopes.md`; `.claude/patterns/api/endpoint-definition.md`, `endpoint-filters.md`; `.claude/patterns/caching/distributed-cache.md`; `.claude/patterns/ui/record-modal-selection.md`.

**Skills:** `task-execute`, `adr-check`, `code-review`, `conflict-check`, `dataverse-deploy`, `bff-deploy`, `ui-test`, `researcher` (agent).

**Canonical code**

BFF:
| Use | File |
|---|---|
| Endpoint shape and rights checks | `Api/ExternalAccess/ExternalProjectDataEndpoints.cs` (routes 90–249; Read `:417`, Create `:451/:655/:900`, Write `:1019`) |
| Scoped reads | `ExternalModuleDataEndpoints.cs` |
| Principal resolution | `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs`, `WorkforcePrincipalResolver.cs:176`, `WorkforceIdentityOptions.cs:173-241`, `ContactIdentityBinder.cs:163` |
| Module entitlement | `ModuleEntitlementResolver.cs:44` |
| Module descriptor | `Infrastructure/DI/ExternalAccessModule.cs:444` |
| Accessible set | `AccessibleRecordSetService.cs:49` |
| Auth schemes | `Infrastructure/DI/AuthorizationModule.cs` (default scheme ~48, Ciam ~60–72, `CopilotAudience` merge ~100–139) |
| Message read and send | `Services/Communication/CommunicationThreadReadService.cs` (`ListThreadsAsync` `:342`, `sprk_isinternalonly` `:75`); `CommunicationService.SendAsync` `:1107` |
| SPE ownership | `Infrastructure/Graph/SpeContainerOwnershipGuard.cs:71` |

SPA: `src/client/external-spa/src/auth/*` (`msal-config.ts`, `msal-auth.ts`, `standalone-plane.ts`, `bff-client.ts`), `src/config.ts`, `host/TeamsHostAdapter.ts`, `components/shell/QuickStartPane.tsx`, `registry/widgetRegistry.ts`, `widgets/GridWidgetBody.tsx`; `src/client/shared/Spaarke.Auth/src/strategies/OfficeNaaStrategy.ts` (the sign-in to copy); `@spaarke/ui-components` `SprkModal`, `RecordNavigationModalShell`, `Wizard/WizardShell` + `InAppWizardHost`, `CreateRecordWizard`.

**Scripts and CI:** `.github/workflows/deploy-external-spa.yml`, `deploy-teams-app.yml`; `/bff-deploy`; `/dataverse-deploy`.

**Schema check (dev, read-only, 2026-10-10)**
| Table | Finding |
|---|---|
| `sprk_document`, `sprk_communication`, `sprk_event`, `sprk_todo` | Have `sprk_createdbyperson`; none has `sprk_createdbycontact` → task 020 |
| `sprk_document` | Has `sprk_regardingrecordid` and `sprk_relatedservicerequest` (→ service request) |
| `sprk_servicerequest` | Has `sprk_requestedby` (→ contact), the `sprk_regarding*` family, `sprk_name` (required), `sprk_direction` (required), `sprk_recordsummary`; no `sprk_createdbyperson`, no request-type or intake column; 0 rows on dev |

## 3. Implementation Approach

### Phase Structure

| Phase | Tasks | Goal |
|---|---|---|
| 0 Governance and inputs | 001–003 | ADR-028 amendment, grid columns, self-registration spike |
| 1 BFF auth and entitlement | 010–012 | FR-22/23, FR-19 + FR-06 module half, FR-15 |
| 2 Schema | 020 | `sprk_createdbycontact`; intake columns on `sprk_servicerequest` |
| 3 BFF features | 030–038 | FR-25, FR-24, FR-26, FR-04, FR-11, FR-05/06 intake, FR-07, FR-08, dev deploy |
| 4 SPA auth and platform | 040–043 | FR-16, FR-17, FR-18, FR-20 |
| 5 SPA features | 050–056 | FR-01–03, messages UI, wizards, documents + delete UI, feed + deep link, join page |
| 6 Registration service | 060 | FR-14, or an R4 hand-off |
| 7 Deploy and verify | 070–073 | Dev SPA deploy, FR-21 Teams live check, both-plane E2E, production deploy (gated) |
| Wrap-up | 090 | Gates, test-diet, close-out |

### Critical Path

```
001 → 040 → 041 → 042 → 070 → 071 → 072 → 073
020 → 030 → 031 → 032 → 054 ─┘
001 → 011 → 033 → 034 → 036 (after UAC-r2 PR #1583) → 038 → 070
```

## 4. Phase Breakdown

### Phase 0 — Governance and inputs
- **001 ADR-028 amendment** (main session, `.claude/adr/` + `docs/adr/`, owner approval):
  - A2: single-tenant client, NAA + popup, no SSO, `user_impersonation`;
  - A3 `:124`: modules by user type;
  - A1 `:55`: workforce-contact guest clarification;
  - the self-registration flow.
  - Coordinate with `spaarke-auth-system-of-record-r1` (§12b).
- **002 Grid columns (FR-09).** Data-only `sprk_gridconfiguration` updates from the owner's per-tab list. Gated on that list.
- **003 Spike (FR-12).** Compare Entra's built-in B2B self-service sign-up user flow with a custom join flow. Output: a written decision with evidence.

### Phase 1 — BFF auth and entitlement
- **010 (FR-22, FR-23).** The Ciam scheme accepts `{appId}` and `api://{appId}`. The default scheme rejects `iss ∋ ciamlogin.com` or `tid == Ciam:TenantId`.
- **011 (FR-19 + FR-06 module half).** Default modules by user type; the partner switch option and the descriptor predicate.
- **012 (FR-15, #1563).** Member-test guest branch: home tenant ∈ `CustomerTenantIds` → Member; the binder creates or binds the contact.

### Phase 2 — Schema
- **020 (FR-25, FR-05 schema).** `sprk_createdbycontact` lookup (→ contact) on document, to-do, event and communication, with FLS mirroring `sprk_createdbyperson`; `sprk_requesttype` and `sprk_intakedata` on `sprk_servicerequest`. Solution export and dev import, owner-gated.

### Phase 3 — BFF features
- **030 (FR-25).** Creator stamping on every external create.
- **031 (FR-24).** Document routes for Matter, Work Assignment, Invoice and Service Request.
- **032 (FR-26).** DELETE own documents (row + SPE file), to-dos, events and service requests.
- **033 (FR-04).** External message read endpoint (accessible records + own service requests; internal-only hidden).
- **034 (FR-11).** External message send endpoint (thread post, stamped sender, no email).
- **035 (FR-05/FR-06 server).** Intake submit endpoint for `sprk_servicerequest`.
- **036 (FR-07 + C6 member notify).** Subsequent-grant email and the message-notify hook, extending UAC-r2's `GrantAccessNotifier` (PR #1583).
- **037 (FR-08).** Notification feed endpoint plus the last-seen marker.
- **038.** Deploy the BFF to dev and verify live (owner-gated).

### Phase 4 — SPA auth and platform
- **040 (FR-16).** Workforce client switch: config, NAA + popup, `auth-callback.html`, manifest without `webApplicationInfo`. The owner creates the app registration.
- **041 (FR-17).** Run-time backend selection seam: directory client stub, dev fallback, CIAM invite key, picker.
- **042 (FR-18, #1566).** Out-of-plane pages go through `/api/v1/external/**`; remove the production mock identity.
- **043 (FR-20).** Production build, production Teams package and CI.

### Phase 5 — SPA features
- **050 (FR-01–03).** Record detail view: shell, read-only fields, docs/invoices, events/tasks, denied state.
- **051 (FR-04/FR-11 client).** Messages section: thread read plus compose/send.
- **052 (FR-05).** Intake framework wiring plus the NDA Assessment and Policy Question wizards.
- **053 (FR-05).** Invention Submission and Trademark Search wizards.
- **054 (FR-24/FR-26 client).** Documents on the detail view (list, download, upload) and delete-own actions.
- **055 (FR-08/FR-07 client).** Notification feed UI with unread count; deep link `?customer=&record=` opens the detail view.
- **056 (FR-13).** Join page and hand-off to workforce sign-in, behind a flag until 060 lands.

### Phase 6 — Registration service
- **060 (FR-14).** Host decision under ADR-052, then either the build (idempotent invite + group add, MI/FIC, no secret) or a written R4 path. Blocked on the owner question of who builds the T240c directory and host.

### Phase 7 — Deploy and verify
- **070.** Deploy the SPA and dev Teams package to dev (owner-gated).
- **071 (FR-21).** Teams live check with the owner's test guest on desktop and web.
- **072.** Both-plane E2E on dev against success criteria 1–12 and FR-10.
- **073.** Production deploy (A4). Gated on T240c, T240d, DNS and owner go.

## 5. Dependencies

### External Dependencies
- **T240c directory (provisioning).** Production routing (FR-17); dev runs without it.
- **T240d (provisioning).** Partner features on stamps; the production CIAM SPA client. Spike S1 (i)–(ii) passed; (iv) pending gives FR-22's token shapes.
- **H3 pre-authorization** of the new workforce client on every customer BFF app.
- **UAC-r2 PR #1583** (`GrantAccessNotifier`). Must merge before task 036.
- **Owner inputs:**
  - the per-tab column list (002);
  - the new client app registration (040);
  - the registration-service host decision (060);
  - the test tenant for C7 E2E;
  - DNS for `external.spaarke.com`.

### Internal Dependencies
- 001 before 011, 012 and 040 (the amendment merges before or with the first C8 code).
- 020 before 030; 033 before 034, 035 and 037.
- Shared file chains are sequential:
  - `ExternalProjectDataEndpoints.cs`: 030 → 031 → 032;
  - endpoint registration in `ExternalAccessEndpoints.cs`: 033 → 035 → 037;
  - the detail view: 050 → 051 → 054 → 055.

## 6. Testing Strategy
- **Unit** (`tests/unit/Sprk.Bff.Api.Tests/`): each FR acceptance case, including the negatives — 401/403, outside-set, ViewOnly upload, delete by a non-creator, partner switch on and off, guest from a non-listed tenant.
- **Seeding proofs** only on the security guards: FR-23 guard, FR-15 branch, FR-26 creator check, FR-11 Tier-2 send check, FR-06 descriptor.
- **Contract / integration:** extend `tests/integration/contract/Api/ExternalAccess/` and `tests/integration/auth/UnifiedAccessControl/` where the existing fixtures cover the route.
- **SPA:** component tests in the external-spa test setup; `<ui-tests>` per frontend task, including dark mode (ADR-021).
- **E2E (task 072):** both planes on dev; Teams desktop and web for workforce.

## 7. Acceptance Criteria

### Technical Acceptance
- Spec success criteria 1–12 (README §Graduation Criteria).
- All BFF and SPA tests pass; publish ≤ 60 MB; no new HIGH CVE.
- No new route outside the external group; no OBO on the external plane (adr-check).

### Business Acceptance
- Owner sign-off on grid columns, notification copy, and the E2E walkthrough (072).

## 8. Risk Register

| Risk | Impact | Mitigation |
|---|---|---|
| Shared external-access BFF files change under UAC-r2 PRs (#1583, #1586) | Merge conflicts; regressions | `/conflict-check` before every BFF PR; 036 waits for #1583; rebase before each wave |
| FR-23 guard rejects legitimate workforce or Copilot tokens | Outage on internal routes | Explicit tests for workforce, Copilot and CIAM tokens; seeding proof; staged dev deploy (038) |
| FR-15 branch admits the wrong guests | Tenant isolation breach | Exact conjunction (acct = 1 ∧ tid = stamp ∧ idp ∈ list); negative tests per deny code; two adversarial passes |
| Teams NAA against a stamp app needs broker pre-authorization | Teams sign-in fails | FR-21 live check (071); H3 adds the broker if needed |
| Registration service host undecided | C7 incomplete | 060 escalates; FR-14 moves to R4 with a written path; the join page ships behind a flag |
| T240c/T240d not ready | No production go-live | 073 gated; dev completes all criteria |
| Publish size | §10 breach | Measure on every BFF task |

## 9. Next Steps
1. Task 001 (main session; needs owner approval to edit `.claude/adr/`).
2. In parallel: 003 spike, 010, 020 (schema; owner-gated import), 050 (SPA detail view), and 002 once the owner supplies the column list.
3. See `tasks/TASK-INDEX.md` for the waves.
