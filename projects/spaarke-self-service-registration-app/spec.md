# Spaarke Self-Service Registration App - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-04-03
> **Source**: design.md (Draft v3)

## Executive Summary

Build a self-service demo access system: prospective users submit a public "Request Early Access" form on `spaarke.com`, an admin reviews and one-click approves via MDA ribbon button, and the system auto-provisions an internal Entra ID account (`user@demo.spaarke.com`) with licenses, Dataverse role, and SPE access. Welcome email with credentials is sent automatically. Expired accounts are disabled daily by a BackgroundService.

Internal accounts (not B2B guest) avoid cross-tenant session conflicts. The architecture supports multiple demo environments via config and extends to production user provisioning in future phases.

## Scope

### In Scope

- **Website form**: Public "Request Early Access" page on `spaarke.com/demo` (Next.js 16 + React 19, `spaarke-website` repo)
- **BFF API endpoints**: Submit demo request (unauthenticated), approve request, reject request (authenticated admin)
- **Provisioning pipeline**: Synchronous — create Entra ID user, assign 3 licenses, sync Dataverse systemuser, add to Demo BU team, grant SPE access, send welcome email
- **Dataverse schema**: `sprk_registrationrequest` table with full lifecycle tracking (Submitted → Provisioned → Expired)
- **MDA configuration**: Views (Pending, All, Active, Expired), form, ribbon buttons (Approve/Reject), sitemap entry
- **Ribbon JS webresource**: Button click handlers calling BFF API approve/reject endpoints
- **Expiration BackgroundService**: Daily check — disable expired accounts, revoke access, send notification emails
- **Pre-expiration warning**: Email 3 days before expiry
- **Entra ID setup scripts**: One-time PowerShell scripts for security group, Conditional Access MFA exclusion, Graph API permissions
- **Email templates**: Admin notification, welcome, expiration warning, expired (4 templates)
- **Multi-environment config**: BFF appsettings supports multiple demo environments; no code change to add a second environment
- **Idempotent provisioning**: All steps check current state; safe to retry

### Out of Scope

- Demo Dataverse environment provisioning (environment is a prerequisite — already exists with solution, BU, team, role, sample data, SPE)
- Production user provisioning (Phase 2)
- Automated approval rules (Phase 2)
- Customer admin self-service portal (Phase 2)
- Usage analytics / lead qualification (Phase 2)
- Website authentication (form is public; no sign-in required)
- Service Bus / async provisioning (synchronous is sufficient for Phase 1 volume)

### Affected Areas

- `src/server/api/Sprk.Bff.Api/` — New registration endpoints, provisioning services, expiration BackgroundService
- `spaarke-website` repo — New demo request page + API route (separate repository)
- Demo Dataverse environment — New `sprk_registrationrequest` table, views, form, ribbon buttons (deployed via solution import)
- Entra ID tenant — New security group, Conditional Access policy, Graph API permissions (one-time scripts)

## Requirements

### Functional Requirements

1. **FR-01**: Public form on `spaarke.com/demo` collects: first name, last name, work email, organization, job title (optional), phone (optional), use case (required dropdown), referral source (optional dropdown), notes (optional), consent (required checkbox) — Acceptance: Form submits successfully, user sees confirmation message with tracking ID
2. **FR-02**: Form submission creates `sprk_registrationrequest` record in Demo Dataverse with status "Submitted" and auto-generated tracking ID (format: `REG-{YYYYMMDD}-{4char}`) — Acceptance: Record appears in MDA "Pending Demo Requests" view
3. **FR-03**: Admin notification email sent via Spaarke Communication Service when new request is submitted — Acceptance: Designated admin(s) receive email with request details and link to MDA record
4. **FR-04**: Duplicate request detection — reject if email already has an active/pending request — Acceptance: Second submission with same email returns error, no duplicate record created
5. **FR-05**: Disposable email domain blocking — reject submissions from known disposable email providers — Acceptance: Submission with mailinator/guerrillamail/etc. email is rejected
6. **FR-06**: MDA "Pending Demo Requests" view shows submitted requests filtered by `status = Submitted`, sorted oldest first, with columns: Name, Email, Organization, Use Case, Request Date — Acceptance: View renders correctly with filter
7. **FR-07**: "Approve Demo Access" ribbon button visible on `sprk_registrationrequest` list and form when `status = Submitted`; hidden for other statuses — Acceptance: Button visibility toggles correctly based on status
8. **FR-08**: Clicking "Approve" triggers synchronous provisioning pipeline that: generates username (`firstname.lastname@demo.spaarke.com` with collision handling), creates Entra ID user, adds to "Spaarke Demo Users" security group, assigns 3 licenses (Power Apps Plan 2 Trial, Fabric Free, Power Automate Free), creates Dataverse systemuser in Demo BU, adds to Demo Team, grants SPE container Writer access, sends welcome email, updates record to "Provisioned" with username + expiration date — Acceptance: All steps complete in < 60 seconds; user can log in to demo environment
9. **FR-09**: "Reject Request" ribbon button prompts for rejection reason, updates status to "Rejected", optionally sends rejection email — Acceptance: Status updates, reason stored
10. **FR-10**: Bulk approve in list view — multi-select requests, click Approve, process sequentially — Acceptance: All selected requests provisioned
11. **FR-11**: Welcome email sent to applicant's work email containing: username, temporary password, access URL (`https://spaarke-demo.crm.dynamics.com/`), browser tip (InPrivate/Incognito), quick start guide link, expiration date, support contact — Acceptance: Email received with all elements; credentials work
12. **FR-12**: Demo expiration — daily BackgroundService disables Entra account, removes from Demo Team, revokes SPE access, sends expiration email, updates status to "Expired" for all records where `expirationdate <= today` and `status = Provisioned` — Acceptance: Expired accounts cannot log in; email received
13. **FR-13**: Pre-expiration warning email sent 3 days before expiry — Acceptance: Email received with extension contact info and production access CTA
14. **FR-14**: Expiration date defaults to 14 days from provisioning but admin can manually edit `sprk_expirationdate` on any record at any time — Acceptance: Modified expiration date is respected by expiration service
15. **FR-15**: Provisioning is idempotent — calling approve endpoint twice for the same request does not create duplicate users, send duplicate emails, or error — Acceptance: Second call returns existing provisioning result

### Non-Functional Requirements

- **NFR-01**: Provisioning completes in < 60 seconds after approve click
- **NFR-02**: Form submission endpoint rate-limited (prevent abuse)
- **NFR-03**: All API errors return RFC 7807 ProblemDetails with correlation ID (ADR-019)
- **NFR-04**: Graph API calls use app-only tokens (application permissions, not delegated)
- **NFR-05**: Temp passwords auto-generated with 16+ chars, mixed case, numbers, symbols
- **NFR-06**: No MFA required for demo accounts (Conditional Access exclusion)
- **NFR-07**: reCAPTCHA validation on form submission endpoint

## Technical Constraints

### Applicable ADRs

- **ADR-001**: MUST use Minimal API for endpoints; MUST use BackgroundService for expiration job; MUST NOT use Azure Functions or Power Automate
- **ADR-004**: MUST implement idempotent provisioning; each step checks current state before acting
- **ADR-006**: Ribbon JS webresource is permitted (ribbon command, not a standalone UI); MUST NOT create legacy JS webresources for UI
- **ADR-008**: MUST use endpoint filters for admin authorization on approve/reject endpoints
- **ADR-010**: MUST register provisioning services as concretes; keep DI registrations minimal
- **ADR-019**: MUST return ProblemDetails for all API errors; MUST include correlation IDs
- **ADR-027**: Solution deployment — unmanaged solution for demo environment (not managed; demo is not a customer/production environment)

### MUST Rules

- ✅ MUST use internal Entra ID accounts (`demo.spaarke.com`), NOT B2B guest invitations
- ✅ MUST auto-generate username from `firstname.lastname` with collision handling
- ✅ MUST assign all 3 licenses programmatically via Graph API
- ✅ MUST add demo users to "Spaarke Demo Users" Entra security group (MFA exclusion)
- ✅ MUST force password change on first login (`forceChangePasswordNextSignIn: true`)
- ✅ MUST disable (not delete) expired accounts — allows reactivation
- ✅ MUST support multiple demo environments via BFF appsettings config array
- ✅ MUST send welcome email to applicant's work email (not the demo.spaarke.com address)
- ❌ MUST NOT require authentication on the request form submission endpoint (public form)
- ❌ MUST NOT use Service Bus for provisioning in Phase 1 (synchronous is sufficient)
- ❌ MUST NOT hardcode environment-specific values (BU ID, team name, container ID, SKU IDs) — read from config

### Existing Patterns to Follow

- Expiration BackgroundService: follow `DailySendCountResetService` pattern
- Email sending: use existing `POST /api/communications/send` endpoint (app-only mode, SharedMailbox)
- Endpoint authorization: use endpoint filter pattern per ADR-008
- Dataverse S2S access: use existing app registration pattern (`170c98e1...`)
- Error responses: ProblemDetails per ADR-019

### Graph API Permissions Required (BFF API App Registration)

- `User.ReadWrite.All` — create/disable users, check existing UPNs
- `GroupMember.ReadWrite.All` — add users to demo security group
- `Directory.ReadWrite.All` — assign licenses

## Data Model

### `sprk_registrationrequest` Table (Demo Dataverse)

| Column | Type | Notes |
|--------|------|-------|
| `sprk_registrationrequestid` | PK (GUID) | |
| `sprk_name` | Text (200) | Primary: "{FirstName} {LastName} - {Organization}" |
| `sprk_firstname` | Text (100) | |
| `sprk_lastname` | Text (100) | |
| `sprk_email` | Text (200) | Applicant's work email |
| `sprk_organization` | Text (200) | |
| `sprk_jobtitle` | Text (200) | |
| `sprk_phone` | Text (50) | |
| `sprk_usecase` | Choice | Document Management, AI Analysis, Financial Intelligence, General |
| `sprk_referralsource` | Choice | Conference, Website, Referral, Search, Other |
| `sprk_notes` | Multiline Text | |
| `sprk_status` | Choice | Submitted (default), Approved, Rejected, Provisioned, Expired, Revoked |
| `sprk_trackingid` | Text (50) | Public reference: REG-{YYYYMMDD}-{4char} |
| `sprk_requestdate` | DateTime | Auto-set on create |
| `sprk_reviewedby` | Lookup (SystemUser) | Who approved/rejected |
| `sprk_reviewdate` | DateTime | When approved/rejected |
| `sprk_rejectionreason` | Text (500) | If rejected |
| `sprk_demousername` | Text (200) | Provisioned UPN: `jane.smith@demo.spaarke.com` |
| `sprk_demouserobjectid` | Text (50) | Entra ID object ID |
| `sprk_provisioneddate` | DateTime | When account was created |
| `sprk_expirationdate` | DateTime | Default: now + 14 days; admin can adjust |
| `sprk_consentaccepted` | Boolean | |
| `sprk_consentdate` | DateTime | |

## API Endpoints

### Submit Demo Request (unauthenticated)

```
POST /api/registration/demo-request
→ 202 Accepted { trackingId, message }
```
Validation: duplicate email check, disposable domain block, reCAPTCHA, rate limit, input sanitization

### Approve Demo Request (admin, authenticated)

```
POST /api/registration/requests/{id}/approve
→ 200 OK { status, username, expirationDate }
```
Authorization: endpoint filter checks `Spaarke Registration Admin` role. Triggers full provisioning pipeline synchronously.

### Reject Demo Request (admin, authenticated)

```
POST /api/registration/requests/{id}/reject
Body: { reason }
→ 200 OK
```

## Environment Configuration

```json
{
  "DemoProvisioning": {
    "Environments": [
      {
        "Name": "Demo 1",
        "DataverseUrl": "https://spaarke-demo.crm.dynamics.com",
        "BusinessUnitName": "Spaarke Demo",
        "TeamName": "Spaarke Demo Team",
        "SpeContainerId": "{container-id}",
        "DefaultDemoDurationDays": 14
      }
    ],
    "DefaultEnvironment": "Demo 1",
    "AccountDomain": "demo.spaarke.com"
  }
}
```

Adding a second demo environment = add entry to array. No code changes.

## Success Criteria

1. [ ] Website form at `spaarke.com/demo` accepts and submits requests — Verify: submit test request, see confirmation
2. [ ] BFF API creates `sprk_registrationrequest` in Demo Dataverse — Verify: check MDA after form submit
3. [ ] Admin notification email sent on new request — Verify: check admin inbox
4. [ ] MDA view shows pending requests with Approve/Reject ribbon buttons — Verify: open MDA, see view and buttons
5. [ ] "Approve" provisions full account in < 60 seconds — Verify: time the approval, check Entra/Dataverse/SPE
6. [ ] User receives welcome email with working credentials — Verify: check applicant inbox, try login
7. [ ] User can log in to `https://spaarke-demo.crm.dynamics.com/` — Verify: InPrivate browser, use demo credentials
8. [ ] No MFA prompt for demo accounts — Verify: login flow has no MFA step
9. [ ] Expired accounts automatically disabled based on expiration date — Verify: set expiration to today, run service, try login
10. [ ] Pre-expiration warning email sent 3 days before expiry — Verify: set expiration to 3 days out, run service
11. [ ] Expiration date adjustable by admin — Verify: edit date in MDA, confirm expiration service respects it
12. [ ] Entra ID setup scripted — Verify: run scripts, confirm group/policy/permissions created
13. [ ] BFF config has demo environment settings — Verify: check appsettings, confirm all values present

## Dependencies

### Prerequisites (NOT Part of This Project)

- Demo Dataverse environment (`spaarke-demo.crm.dynamics.com`) provisioned with Spaarke solution deployed
- "Spaarke Demo" Business Unit, Team, and `Spaarke Demo User` security role configured
- Demo SPE container with sample documents
- Sample data loaded (matters, projects, playbooks)
- `demo.spaarke.com` domain configured in Entra ID (already done)

### External Dependencies

- Microsoft Graph API — user creation, license assignment, group management
- Entra ID admin consent — for new Graph API permissions on BFF app registration
- Spaarke Communication Service — existing email infrastructure
- reCAPTCHA — already integrated in website

## Owner Clarifications

*Captured during design discussion (2026-04-03):*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| User identity model | B2B guest vs. internal account? | Internal accounts (`demo.spaarke.com`) — avoids cross-tenant session conflicts | Architecture: create Entra user, not guest invitation |
| Registration form | Separate registration form after approval? | Eliminated — approval triggers auto-provisioning directly | Simpler flow: no second form, no security codes |
| Admin approval UI | Email links vs. MDA ribbon button? | MDA view + ribbon button — centralized, auditable, extensible | Build: ribbon customization + JS webresource calling BFF API |
| Provisioning model | Async (Service Bus) vs. synchronous? | Synchronous for Phase 1 — simpler, ~10-30s is acceptable | No Service Bus consumer needed; can add later if volume demands |
| MFA | Required for demo users? | No — Conditional Access exclusion for demo security group | One-time Entra setup; provisioning adds users to group |
| Demo duration | Fixed or adjustable? | Default 14 days; admin can manually edit expiration date per record | `sprk_expirationdate` editable on MDA form |
| Environment setup | Bootstrap scripts in scope? | No — demo environment is a prerequisite (already set up) | Project handles per-user provisioning into existing environment |
| Multi-environment | Single environment or configurable? | Config-driven — add environments via appsettings array | BFF reads environment config; no code changes to add environments |
| Browser conflicts | How to handle cached Microsoft sessions? | Welcome email includes "use InPrivate/Incognito" guidance | Internal accounts largely avoid the issue; browser tip covers edge case |

## Assumptions

*Proceeding with these assumptions (owner confirmed or did not specify otherwise):*

- **Demo data**: Shared across all demo users (simplest approach) — all users see same sample matters/documents
- **Password policy**: Standard Entra ID complexity requirements apply; auto-generated temp password meets them
- **Account extension**: Admin manually edits expiration date — no formal extension request workflow in Phase 1
- **Concurrent user limit**: No hard cap — license availability is the natural limit
- **Demo data reset**: Not automated in this project — manual/separate process if needed
- **Website page location**: `spaarke.com/demo` (could be `/get-started` — finalize during implementation)
- **reCAPTCHA version**: Use whatever version is already integrated in the website

## Unresolved Questions

*None blocking — all architectural decisions resolved during design discussion.*

---

*AI-optimized specification. Original design: design.md (Draft v3)*
