# Implementation Plan — Spaarke Self-Service Registration App

> **Created**: 2026-04-03
> **Phases**: 5 (structured for maximum parallel execution)
> **Estimated Tasks**: ~25

## Execution Strategy

This plan is structured for **maximum parallel execution** using concurrent Claude Code agents. Independent work streams are grouped so multiple agents can run simultaneously with no approval gates.

```
Phase 0 ─── Foundation (serial, 1 agent)
   │         Models, config, DI skeleton
   │
Phase 1 ─── Independent Streams (parallel, 5 agents)
   │         ├─ A1: Graph user service
   │         ├─ A2: Dataverse registration service
   │         ├─ C:  Dataverse schema + views
   │         ├─ E:  Entra setup scripts
   │         └─ F:  Email HTML templates
   │
Phase 2 ─── Integration Streams (parallel, 3 agents)
   │         ├─ A3: Provisioning orchestrator
   │         ├─ A4: Registration endpoints
   │         └─ B:  Website form + API route
   │
Phase 3 ─── Dependent Streams (parallel, 2 agents)
   │         ├─ A5: Expiration BackgroundService
   │         └─ D:  Ribbon buttons + JS webresource
   │
Phase 4 ─── Integration & Verification (serial, 1 agent)
   │         DI wiring, deployment, E2E testing
   │
Phase 5 ─── Wrap-up (serial, 1 agent)
```

## Phase Breakdown

### Phase 0: Foundation (Serial — 1 agent)

**Purpose**: Create shared models, configuration classes, and DI module skeleton that all parallel streams depend on.

**Deliverables**:
1. **Request/response DTOs** — `Models/Registration/` — `DemoRequestDto`, `ApproveResponseDto`, `RejectRequestDto`
2. **Configuration model** — `DemoProvisioningOptions` with environment array, account domain, default duration
3. **DI module skeleton** — `RegistrationModule.cs` with `AddRegistrationModule()` extension method
4. **Status enum** — Registration status values (Submitted, Approved, Rejected, Provisioned, Expired, Revoked)

**Files created**:
- `src/server/api/Sprk.Bff.Api/Models/Registration/DemoRequestDto.cs`
- `src/server/api/Sprk.Bff.Api/Models/Registration/ApproveResponseDto.cs`
- `src/server/api/Sprk.Bff.Api/Models/Registration/RejectRequestDto.cs`
- `src/server/api/Sprk.Bff.Api/Models/Registration/RegistrationStatus.cs`
- `src/server/api/Sprk.Bff.Api/Configuration/DemoProvisioningOptions.cs`
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/RegistrationModule.cs`

**Dependencies**: None
**Blocks**: All Phase 1 and Phase 2 tasks

---

### Phase 1: Independent Streams (Parallel — 5 agents)

All 5 streams can execute simultaneously. No dependencies between them. Each depends only on Phase 0 (foundation models).

#### Stream A1: Graph User Service

**Purpose**: Entra ID user management — create users, assign licenses, manage security group membership.

**Deliverables**:
1. **GraphUserService** — Create `demo.spaarke.com` users, check UPN availability, handle collisions
2. **License assignment** — Assign 3 licenses via Graph API (`assignLicense`)
3. **Security group management** — Add/remove users from "Spaarke Demo Users" group
4. **Password generation** — Secure temp password generator (16+ chars)
5. **Unit tests** for Graph user service

**Files created**:
- `src/server/api/Sprk.Bff.Api/Services/Registration/GraphUserService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Registration/PasswordGenerator.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Registration/GraphUserServiceTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Registration/PasswordGeneratorTests.cs`

**Canonical references**:
- `Infrastructure/Graph/GraphClientFactory.cs` → `ForApp()` for app-only Graph
- `.claude/patterns/auth/service-principal.md`

#### Stream A2: Dataverse Registration Service

**Purpose**: CRUD operations for `sprk_registrationrequest` records + systemuser sync + team assignment in Demo Dataverse.

**Deliverables**:
1. **RegistrationDataverseService** — Create/update/query `sprk_registrationrequest` records
2. **Systemuser sync** — Create systemuser in Demo Dataverse from Entra object ID
3. **Team assignment** — Add user to Demo Team (inherits security role)
4. **Duplicate detection** — Check for existing requests by email
5. **Tracking ID generation** — `REG-{YYYYMMDD}-{4char}` format
6. **Unit tests**

**Files created**:
- `src/server/api/Sprk.Bff.Api/Services/Registration/RegistrationDataverseService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Registration/TrackingIdGenerator.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Registration/RegistrationDataverseServiceTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Registration/TrackingIdGeneratorTests.cs`

**Canonical references**:
- `Spaarke.Dataverse/DataverseServiceClientImpl.cs` for S2S access
- `.claude/patterns/dataverse/web-api-client.md`
- `.claude/patterns/dataverse/entity-operations.md`

#### Stream C: Dataverse Schema & MDA Configuration

**Purpose**: Define `sprk_registrationrequest` table, views, form, and sitemap for Demo Dataverse environment.

**Deliverables**:
1. **Table definition** — All columns per spec Section 5.4 (schema XML or PAC CLI)
2. **MDA Views** — Pending Demo Requests, All Demo Requests, Active Demo Users, Expired Demo Users
3. **MDA Form** — Registration request form with all fields, status-based sections
4. **Sitemap entry** — Registration Requests in MDA navigation
5. **Solution packaging** — Unmanaged solution for demo environment

**Files created**:
- `src/solutions/DemoRegistration/` — Dataverse solution project
- Schema definition files (entity XML, view XML, form XML, sitemap XML)

**Canonical references**:
- `docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md`

#### Stream E: Entra ID Setup Scripts

**Purpose**: One-time PowerShell scripts for Entra ID infrastructure.

**Deliverables**:
1. **Setup-EntraInfrastructure.ps1** — Create "Spaarke Demo Users" security group, Conditional Access MFA exclusion policy, add Graph API permissions to BFF app registration, grant admin consent
2. **Get-LicenseSkuIds.ps1** — Query tenant `subscribedSkus`, output SKU IDs for Power Apps Plan 2 Trial, Fabric Free, Power Automate Free

**Files created**:
- `scripts/Setup-EntraInfrastructure.ps1`
- `scripts/Get-LicenseSkuIds.ps1`

**Canonical references**:
- `scripts/Register-EntraAppRegistrations.ps1` — reference for Graph permission patterns

#### Stream F: Email HTML Templates

**Purpose**: Branded HTML email templates for all registration lifecycle events.

**Deliverables**:
1. **Admin notification template** — New request details, link to MDA record
2. **Welcome email template** — Username, temp password, access URL, browser tip, expiry date, quick start guide
3. **Expiration warning template** — "Demo expiring in 3 days", extension contact, production access CTA
4. **Expired notification template** — Account disabled, thank you, production access CTA
5. **Template rendering service** — Simple string interpolation for template variables

**Files created**:
- `src/server/api/Sprk.Bff.Api/Services/Registration/EmailTemplates/AdminNotificationTemplate.html`
- `src/server/api/Sprk.Bff.Api/Services/Registration/EmailTemplates/WelcomeTemplate.html`
- `src/server/api/Sprk.Bff.Api/Services/Registration/EmailTemplates/ExpirationWarningTemplate.html`
- `src/server/api/Sprk.Bff.Api/Services/Registration/EmailTemplates/ExpiredTemplate.html`
- `src/server/api/Sprk.Bff.Api/Services/Registration/RegistrationEmailService.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Registration/RegistrationEmailServiceTests.cs`

**Canonical references**:
- `Services/Communication/CommunicationService.cs` — email send patterns
- `.claude/patterns/api/send-email-integration.md`

---

### Phase 2: Integration Streams (Parallel — 3 agents)

Depends on Phase 0 + Phase 1 (Stream A1 + A2 + F). Three agents run simultaneously.

#### Stream A3: Provisioning Orchestrator

**Purpose**: Orchestrate all provisioning steps into a single idempotent pipeline.

**Deliverables**:
1. **DemoProvisioningService** — Orchestrates: generate username → create Entra user → assign licenses → add to group → sync Dataverse user → add to team → grant SPE → send email → update record
2. **Idempotency logic** — Each step checks state; safe to retry
3. **SPE container access** — Grant Writer permissions via Graph API
4. **Error handling** — Partial provisioning rollback/reporting
5. **Unit tests**

**Files created**:
- `src/server/api/Sprk.Bff.Api/Services/Registration/DemoProvisioningService.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Registration/DemoProvisioningServiceTests.cs`

**Dependencies**: A1 (GraphUserService), A2 (RegistrationDataverseService), F (RegistrationEmailService)

#### Stream A4: Registration Endpoints

**Purpose**: BFF API endpoints for form submission, admin approval, and rejection.

**Deliverables**:
1. **RegistrationEndpoints.cs** — `MapGroup("/api/registration")` with 3 endpoints
2. **Submit endpoint** — `POST /demo-request` (unauthenticated, reCAPTCHA validated, rate-limited)
3. **Approve endpoint** — `POST /requests/{id}/approve` (admin auth via endpoint filter)
4. **Reject endpoint** — `POST /requests/{id}/reject` (admin auth via endpoint filter)
5. **RegistrationAuthorizationFilter** — Check `Spaarke Registration Admin` role
6. **Input validation** — Email domain blocking, field length limits
7. **Unit tests**

**Files created**:
- `src/server/api/Sprk.Bff.Api/Endpoints/RegistrationEndpoints.cs`
- `src/server/api/Sprk.Bff.Api/Endpoints/Filters/RegistrationAuthorizationFilter.cs`
- `src/server/api/Sprk.Bff.Api/Services/Registration/EmailDomainValidator.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Endpoints/RegistrationEndpointsTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Registration/EmailDomainValidatorTests.cs`

**Canonical references**:
- `Ai/AnalysisEndpoints.cs` — endpoint structure exemplar
- `.claude/patterns/api/endpoint-definition.md`
- `.claude/patterns/api/endpoint-filters.md`

#### Stream B: Website Form (spaarke-website repo)

**Purpose**: Public "Request Early Access" page on `spaarke.com/demo`.

**Deliverables**:
1. **Demo page** — `app/demo/page.tsx` — public page with form
2. **DemoRequestForm component** — React 19 form with client-side validation, reCAPTCHA
3. **API route** — `app/api/registration/demo-request/route.ts` — proxies to BFF API
4. **Confirmation state** — Success message with tracking ID after submission
5. **Navigation CTA** — "Request Early Access" button in site header/hero

**Files created** (in `spaarke-website` repo):
- `app/demo/page.tsx`
- `components/DemoRequestForm.tsx`
- `app/api/registration/demo-request/route.ts`

**Note**: This stream works in the `spaarke-website` repo, not the main spaarke repo. Can run fully in parallel with API work.

---

### Phase 3: Dependent Streams (Parallel — 2 agents)

Depends on Phase 2 (endpoints exist, provisioning service exists). Two agents run simultaneously.

#### Stream A5: Expiration BackgroundService

**Purpose**: Daily scheduled service to disable expired accounts and send warning emails.

**Deliverables**:
1. **DemoExpirationService** — `BackgroundService` running daily at midnight UTC
2. **Expiration logic** — Query expired records, disable Entra account, remove from team, revoke SPE, send email, update status
3. **Pre-expiration warning** — 3 days before expiry, send warning email
4. **Unit tests**

**Files created**:
- `src/server/api/Sprk.Bff.Api/Services/Registration/DemoExpirationService.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Registration/DemoExpirationServiceTests.cs`

**Canonical references**:
- `Services/Communication/DailySendCountResetService.cs` — exact pattern to follow
- `.claude/patterns/api/background-workers.md`

#### Stream D: Ribbon Buttons + JS Webresource

**Purpose**: Approve/Reject ribbon buttons on `sprk_registrationrequest` entity in Demo MDA.

**Deliverables**:
1. **Ribbon XML** — Approve and Reject buttons on HomePageGrid + form command bar
2. **Enable rules** — Show buttons only when `sprk_status = Submitted`
3. **JS webresource** — `sprk_registrationribbon.js` — button click handlers calling BFF API
4. **Confirmation dialogs** — Approve confirmation, Reject reason prompt
5. **Bulk approve support** — Multi-select in list view
6. **Solution packaging** — Include in DemoRegistration solution

**Files created**:
- `src/solutions/DemoRegistration/RibbonDiffXml/` — Ribbon customization XML
- `src/client/webresources/sprk_registrationribbon.js`

**Canonical references**:
- `docs/guides/RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md`
- `.claude/patterns/webresource/custom-dialogs-in-dataverse.md`

---

### Phase 4: Integration & Verification (Serial — 1 agent)

**Purpose**: Wire everything together, deploy, and verify end-to-end.

**Deliverables**:
1. **Complete DI wiring** — Register all services in `RegistrationModule.cs`, add to `Program.cs`
2. **Configuration** — Add `DemoProvisioning` section to `appsettings.json` with placeholder values
3. **Deploy BFF API** — Push updated API with registration endpoints
4. **Deploy Dataverse solution** — Import `DemoRegistration` solution to demo environment
5. **Run Entra setup scripts** — Execute `Setup-EntraInfrastructure.ps1` and `Get-LicenseSkuIds.ps1`
6. **End-to-end smoke test** — Submit form → approve → verify account → verify login
7. **Idempotency test** — Call approve twice, verify no duplicates
8. **Expiration test** — Set expiration to today, run service, verify disable

---

### Phase 5: Wrap-up (Serial — 1 agent)

1. Update README.md status to Complete
2. Update TASK-INDEX.md — all tasks ✅
3. Create lessons-learned.md
4. Final commit

---

## Parallel Execution Summary

| Phase | Agents | Tasks | Prerequisite |
|-------|--------|-------|--------------|
| 0 | 1 (serial) | 001 | None |
| 1 | 5 (parallel) | 010-014 | Phase 0 complete |
| 2 | 3 (parallel) | 020-022 | Phase 1 complete |
| 3 | 2 (parallel) | 030-031 | Phase 2 complete |
| 4 | 1 (serial) | 040-042 | Phase 3 complete |
| 5 | 1 (serial) | 050 | Phase 4 complete |

**Total parallel agent slots**: Up to 5 concurrent agents (Phase 1)
**Total tasks**: ~16 (optimized for parallel execution — larger tasks per agent)

## Architecture Context

### Discovered Resources

**ADRs** (7):
- ADR-001: Minimal API + BackgroundService
- ADR-004: Idempotent job handling
- ADR-006: No legacy JS webresources (ribbon JS is an exception — it's a command, not UI)
- ADR-008: Endpoint filters for authorization
- ADR-010: DI minimalism
- ADR-019: ProblemDetails for errors
- ADR-027: Solution deployment (unmanaged for demo)

**Patterns** (8):
- `api/endpoint-definition.md` — Endpoint structure
- `api/background-workers.md` — BackgroundService pattern
- `api/send-email-integration.md` — Email sending
- `api/service-registration.md` — DI module pattern
- `api/error-handling.md` — ProblemDetails
- `auth/service-principal.md` — App-only Graph
- `dataverse/web-api-client.md` — S2S Dataverse access
- `dataverse/entity-operations.md` — CRUD operations

**Canonical Implementations** (6):
- `AnalysisEndpoints.cs` — Endpoint exemplar
- `DailySendCountResetService.cs` — Scheduled BackgroundService
- `CommunicationService.cs` — Email sending
- `GraphClientFactory.cs` — Graph API factory
- `DataverseServiceClientImpl.cs` — Dataverse S2S
- `CommunicationModule.cs` — DI module

**Scripts** (4 reference):
- `Register-EntraAppRegistrations.ps1`
- `Provision-Customer.ps1`
- `Deploy-DataverseSolutions.ps1`
- `Deploy-BffApi.ps1`

**Guides** (4):
- `COMMUNICATION-DEPLOYMENT-GUIDE.md`
- `DATAVERSE-AUTHENTICATION-GUIDE.md`
- `DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md`
- `RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md`
