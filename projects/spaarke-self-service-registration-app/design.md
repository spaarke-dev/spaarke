# Spaarke Self-Service Registration App — Design Document

> **Author**: Ralph Schroeder
> **Date**: 2026-03-10 (updated 2026-04-03)
> **Status**: Draft v3
> **Scope**: Phase 1 — Demo Access via Internal Accounts

---

## 1. Executive Summary

Build a streamlined demo access process: prospective users submit a "Request Early Access" form on the Spaarke website, an admin reviews and approves in the Spaarke MDA via a ribbon button, and the system auto-provisions an internal demo account (`user@demo.spaarke.com`) with all required licenses and access. No separate registration form — approval triggers full provisioning and a welcome email with credentials.

The architecture is designed to extend to production user provisioning in future phases.

---

## 2. Problem Statement

Today, onboarding a new Spaarke demo user is entirely manual:
1. An admin creates a user account in Entra ID
2. An admin assigns licenses (Power Apps, Fabric, Power Automate)
3. An admin assigns Dataverse security roles and business unit
4. An admin grants SPE container access
5. The user is manually notified with credentials

This doesn't scale for demo/trial access or future multi-customer onboarding. We need a one-click approval process that automates all provisioning steps.

---

## 3. Objectives

| # | Objective | Phase |
|---|-----------|-------|
| 1 | User can submit a demo access request through a public web form on spaarke.com | Phase 1 |
| 2 | Admin reviews and approves requests in Spaarke MDA (ribbon button) | Phase 1 |
| 3 | Approved users are auto-provisioned: internal Entra ID account, licenses, Dataverse role, SPE access | Phase 1 |
| 4 | User receives welcome email with credentials, access URL, and quick start guide | Phase 1 |
| 5 | Demo access is time-limited (default 14 days, adjustable) with automated expiration | Phase 1 |
| 6 | No MFA/authenticator required for demo accounts | Phase 1 |
| 7 | Production user provisioning (customer-scoped, dedicated environments) | Phase 2 |

---

## 4. Infrastructure

### Demo Environment(s)

The demo environment is **pre-provisioned and fully configured** — Spaarke solution deployed, business unit created, security roles defined, sample data loaded, SPE containers ready. This project does NOT set up the environment itself.

What this project automates is **per-user provisioning** into the existing environment: create the Entra account, assign them to the correct BU/team/role, grant SPE access, assign licenses, and send credentials.

**Phase 1**: Single environment. **Future**: Support multiple environments by adding config entries.

| Setting | Phase 1 Value | Description |
|---------|---------------|-------------|
| Environment URL | `https://spaarke-demo.crm.dynamics.com/` | Pre-existing Dataverse environment |
| Account Domain | `demo.spaarke.com` | UPN domain for demo accounts |
| Account Format | `firstname.lastname@demo.spaarke.com` | Auto-generated from request |
| Business Unit | "Spaarke Demo" (pre-existing) | User is assigned to this BU |
| Team | "Spaarke Demo Team" (pre-existing) | User is added to this team → inherits `Spaarke Demo User` security role |
| SPE Container | (pre-existing) | User is granted access to this container |
| Demo Duration (default) | 14 days | Default; admin can manually adjust expiration date on any record |

#### Environment Configuration

Environment references are stored in **BFF API configuration** (appsettings / Key Vault):

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

**Adding a second demo environment** = add another entry to the config array. The provisioning pipeline reads the target environment config and provisions the user into it. No code changes required.

### Existing Infrastructure (Reused)

| Resource | Details | Used For |
|----------|---------|----------|
| Entra ID Tenant | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | Identity provider; demo.spaarke.com domain |
| BFF API | `spe-api-dev-67e2xz` (.NET 8 Minimal API) | Provisioning endpoints |
| BFF API App Registration | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | Token validation, app-only Graph calls |
| Dataverse App Registration | `170c98e1-d486-4355-bcbe-170454e0207c` | S2S Dataverse access |
| Spaarke Website | `spaarke-website` repo (Next.js 16 + React 19) | Request Early Access form |
| Spaarke Communication Service | BFF API `POST /api/communications/send` | Email notifications |
| Key Vault | `spaarke-spekvcert` | Secrets management |

### What We Need to Create

| Resource | Purpose |
|----------|---------|
| "Request Early Access" page on spaarke.com | Public form for demo requests |
| BFF API registration endpoints | Submission + approval/provisioning APIs |
| `sprk_registrationrequest` Dataverse table | Track requests (deployed to demo environment via solution) |
| Approval fields on `sprk_registrationrequest` | Status, provisioned username, expiration |
| MDA view + ribbon button | "Approve Demo Access" in Spaarke Demo MDA |
| Ribbon button JS webresource | Calls BFF API approve endpoint |
| "Spaarke Demo Users" Entra security group | For Conditional Access MFA exclusion (setup script) |
| Conditional Access policy | Exclude demo user group from MFA (setup script) |
| Graph API permissions on BFF app reg | `User.ReadWrite.All`, `GroupMember.ReadWrite.All`, `Directory.ReadWrite.All` (setup script) |
| Email templates | Admin notification, welcome email, expiration warning |

**Prerequisites (already in place, NOT part of this project)**:
- Demo Dataverse environment (`spaarke-demo.crm.dynamics.com`) with Spaarke solution deployed
- "Spaarke Demo" Business Unit, Team, and `Spaarke Demo User` security role
- Demo SPE container with sample documents
- Sample data (matters, projects, playbooks)

---

## 5. Architecture — Phase 1

### 5.1 End-to-End Flow

```
1. User visits spaarke.com → clicks "Request Early Access"
2. Fills out public form (name, email, org, use case, consent)
3. Website submits to BFF API → creates sprk_registrationrequest in Demo Dataverse
4. Admin receives notification email (FYI — no action links)
5. Admin opens Spaarke Demo MDA → "Pending Demo Requests" view
6. Admin selects request → clicks "Approve Demo Access" ribbon button
7. Ribbon button calls BFF API POST /api/registration/requests/{id}/approve
8. BFF API auto-provisions (synchronous, ~10-30 seconds):
   a. Generate username (firstname.lastname@demo.spaarke.com)
   b. Create Entra ID user account with temp password
   c. Assign licenses: Power Apps Plan 2 Trial, Fabric (Free), Power Automate (Free)
   d. Add user to "Spaarke Demo" BU team in Dataverse
   e. Grant SPE demo container access
   f. Send welcome email (username, temp password, access URL)
   g. Update request status to "Provisioned", set expiration date
9. User receives welcome email:
   - Username: jane.smith@demo.spaarke.com
   - Temporary password (must change on first login)
   - Access URL: https://spaarke-demo.crm.dynamics.com/
   - Quick start guide link
   - "Use InPrivate/Incognito if signed into other Microsoft accounts"
   - Demo expires on [date]
10. User logs in → explores Spaarke with sample data
11. Day 27: "Your demo is expiring" email
12. Day 30: Access disabled, expiration email sent
```

### 5.2 Request Form (Spaarke Website)

**Location**: New page on `spaarke-website` repo — `spaarke.com/demo` or `spaarke.com/get-started`

**No authentication required** — this is a public marketing form. The website already has reCAPTCHA integrated for bot protection.

#### Form Fields

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| First Name | text | Yes | |
| Last Name | text | Yes | |
| Work Email | email | Yes | Used for communications and username derivation |
| Organization | text | Yes | |
| Job Title | text | No | Helps qualify/prioritize |
| Phone | tel | No | |
| Use Case | dropdown | Yes | "Document Management", "AI Analysis", "Financial Intelligence", "General Evaluation" |
| How Did You Hear About Us | dropdown | No | Marketing tracking |
| Notes | textarea | No | Additional context |
| Consent | checkbox | Yes | Terms of use, data processing |

**Anti-Spam/Bot Protection**:
- reCAPTCHA (already integrated in website)
- Rate limiting on BFF API endpoint
- Email domain validation (block disposable email providers)

**Submission**:
1. Client-side validation
2. Next.js API route `POST /api/registration/demo-request` proxies to BFF API
3. BFF API validates, creates `sprk_registrationrequest` in Demo Dataverse
4. BFF API sends admin notification email via Spaarke Communication Service
5. User sees confirmation: "Thanks! We'll review your request within 1 business day."

### 5.3 API Endpoints

**New BFF API endpoints** in `RegistrationEndpoints.cs`:

#### Submit Demo Request (called by website)

```
POST /api/registration/demo-request
Content-Type: application/json

{
  "firstName": "Jane",
  "lastName": "Smith",
  "email": "jane.smith@contoso.com",
  "organization": "Contoso Ltd",
  "jobTitle": "Legal Operations Manager",
  "phone": "+1-555-0123",
  "useCase": "DocumentManagement",
  "referralSource": "Conference",
  "notes": "Interested in SPE integration",
  "consentAccepted": true
}
```

**Response**: `202 Accepted`
```json
{
  "trackingId": "REG-20260403-A1B2",
  "message": "Your request has been submitted for review."
}
```

**Validation**:
- Duplicate check: reject if email already has an active/pending request
- Email domain validation: block disposable providers
- Input sanitization and length limits
- reCAPTCHA token validation

**Note**: This endpoint is **unauthenticated** (public form submission). Security relies on reCAPTCHA, rate limiting, and email domain validation.

#### Approve Demo Request (called by MDA ribbon button)

```
POST /api/registration/requests/{id}/approve
Authorization: Bearer {admin-token}
```

This is the **single trigger** for the entire provisioning pipeline. The endpoint:
1. Validates admin has `Spaarke Registration Admin` role
2. Runs provisioning synchronously (Section 5.5)
3. Returns provisioning result

**Response**: `200 OK`
```json
{
  "status": "Provisioned",
  "username": "jane.smith@demo.spaarke.com",
  "expirationDate": "2026-05-03T00:00:00Z"
}
```

#### Reject Demo Request (called by MDA ribbon button)

```
POST /api/registration/requests/{id}/reject
Authorization: Bearer {admin-token}
Content-Type: application/json

{
  "reason": "Not a qualified prospect"
}
```

Updates status to "Rejected". Optionally sends rejection notification email.

### 5.4 Dataverse Data Model

#### `sprk_registrationrequest` (in Demo Dataverse environment)

| Column | Type | Notes |
|--------|------|-------|
| `sprk_registrationrequestid` | PK (GUID) | |
| `sprk_name` | Text (200) | Primary name: "{FirstName} {LastName} - {Organization}" |
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
| `sprk_trackingid` | Text (50) | Public reference (REG-20260403-A1B2) |
| `sprk_requestdate` | DateTime | Auto-set on create |
| `sprk_reviewedby` | Lookup (SystemUser) | Who approved/rejected |
| `sprk_reviewdate` | DateTime | When approved/rejected |
| `sprk_rejectionreason` | Text (500) | If rejected |
| `sprk_demousername` | Text (200) | Provisioned: `jane.smith@demo.spaarke.com` |
| `sprk_demouserobjectid` | Text (50) | Entra ID object ID of provisioned user |
| `sprk_provisioneddate` | DateTime | When account was created |
| `sprk_expirationdate` | DateTime | Demo access expiry (default: now + 14 days; admin can adjust on the record) |
| `sprk_consentaccepted` | Boolean | |
| `sprk_consentdate` | DateTime | |

**Design decision**: Single table (`sprk_registrationrequest`) instead of separate request + access tables. The request record tracks the full lifecycle from submission through provisioning to expiration. Simpler for Phase 1; can split later if needed for production user management.

### 5.5 Provisioning Pipeline (Synchronous)

Triggered by `POST /api/registration/requests/{id}/approve`. All steps run synchronously — no Service Bus for Phase 1. The entire pipeline takes ~10-30 seconds.

**Idempotency**: Each step checks current state before acting. If the endpoint is called twice (e.g., network retry), it won't create duplicate users or send duplicate emails.

#### Step 1: Generate Username

- Pattern: `firstname.lastname@demo.spaarke.com`
- Handle collisions: append number if taken (`jane.smith2@demo.spaarke.com`)
- Check Entra ID for existing UPN: `GET /users?$filter=userPrincipalName eq '{upn}'`

#### Step 2: Create Entra ID User

```
POST https://graph.microsoft.com/v1.0/users
{
  "accountEnabled": true,
  "displayName": "Jane Smith",
  "givenName": "Jane",
  "surname": "Smith",
  "userPrincipalName": "jane.smith@demo.spaarke.com",
  "mailNickname": "jane.smith",
  "passwordProfile": {
    "password": "{generated-temp-password}",
    "forceChangePasswordNextSignIn": true
  },
  "usageLocation": "US",
  "department": "Demo",
  "companyName": "Contoso Ltd",
  "jobTitle": "Legal Operations Manager"
}
```

- Temp password: auto-generated (16+ chars, mixed case, numbers, symbols)
- `forceChangePasswordNextSignIn: true` — user sets their own password on first login
- Add to "Spaarke Demo Users" security group (for Conditional Access MFA exclusion)

**Graph API Permissions** (application-level, on BFF API app registration):
- `User.ReadWrite.All` — create users, check existing, update properties
- `GroupMember.ReadWrite.All` — add user to demo security group
- `Directory.ReadWrite.All` — license assignment

#### Step 3: Assign Licenses

```
POST https://graph.microsoft.com/v1.0/users/{userId}/assignLicense
{
  "addLicenses": [
    { "skuId": "{PowerApps-Plan2-Trial-SKU}" },
    { "skuId": "{Fabric-Free-SKU}" },
    { "skuId": "{PowerAutomate-Free-SKU}" }
  ],
  "removeLicenses": []
}
```

Licenses assigned:
- **Microsoft Power Apps Plan 2 Trial** — required for Dataverse MDA access
- **Microsoft Fabric (Free)** — required for data platform features
- **Microsoft Power Automate (Free)** — included for completeness

**Note**: SKU IDs must be looked up from the tenant's `subscribedSkus` endpoint. These are trial/free plans so no cost impact.

#### Step 4: Dataverse User Sync + Team Assignment

After the Entra ID user is created:

1. **Create systemuser in Dataverse** — Using S2S (existing app registration):
   ```
   POST https://spaarke-demo.crm.dynamics.com/api/data/v9.2/systemusers
   {
     "azureactivedirectoryobjectid": "{entra-object-id}",
     "firstname": "Jane",
     "lastname": "Smith",
     "internalemailaddress": "jane.smith@demo.spaarke.com",
     "businessunitid@odata.bind": "/businessunits({demo-bu-id})"
   }
   ```

2. **Add to Demo Team** — The "Spaarke Demo" team inherits the `Spaarke Demo User` security role
   ```
   POST https://spaarke-demo.crm.dynamics.com/api/data/v9.2/teams({team-id})/teammembership_association/$ref
   {
     "@odata.id": "systemusers({user-id})"
   }
   ```

#### Step 5: SPE Container Access

- Add demo user to shared demo SPE container with `Writer` permissions
- Graph API: `POST /storage/fileStorage/containers/{containerId}/permissions`
- Demo container pre-loaded with sample documents

#### Step 6: Send Welcome Email

Via Spaarke Communication Service (`POST /api/communications/send`, app-only mode):

```json
{
  "to": [{ "email": "jane.smith@contoso.com", "name": "Jane Smith" }],
  "subject": "Your Spaarke Demo Access is Ready!",
  "body": "<html>...branded welcome email...</html>",
  "bodyFormat": "HTML",
  "sendMode": "SharedMailbox",
  "fromMailbox": "noreply@spaarke.com",
  "associations": [{ "entityType": "sprk_registrationrequest", "id": "{requestId}" }]
}
```

Welcome email includes:
- **Username**: `jane.smith@demo.spaarke.com`
- **Temporary password**: `{generated-password}` (must change on first login)
- **Access URL**: `https://spaarke-demo.crm.dynamics.com/`
- **Quick start guide** link
- **Browser tip**: "Use InPrivate/Incognito if you're signed into other Microsoft accounts"
- **Demo expiration date**
- **Support contact**

#### Step 7: Update Request Record

- Set `sprk_status` to "Provisioned"
- Set `sprk_demousername` to generated UPN
- Set `sprk_demouserobjectid` to Entra ID object ID
- Set `sprk_provisioneddate` to now
- Set `sprk_expirationdate` to now + default duration (14 days; admin can manually adjust afterward)

### 5.6 Admin Approval UI (Spaarke Demo MDA)

#### MDA View: "Pending Demo Requests"

- Entity: `sprk_registrationrequest`
- Filter: `sprk_status = Submitted`
- Columns: Name, Email, Organization, Use Case, Request Date
- Sort: Request Date ascending (oldest first)

#### Additional Views

- "All Demo Requests" — no filter, all statuses
- "Active Demo Users" — `sprk_status = Provisioned`, shows username + expiration
- "Expired Demo Users" — `sprk_status = Expired`

#### Ribbon Button: "Approve Demo Access"

- **Location**: `sprk_registrationrequest` entity — both list view (HomePageGrid) and form command bar
- **Visibility**: Only when `sprk_status = Submitted`
- **Action**: JavaScript webresource calls BFF API `POST /api/registration/requests/{id}/approve`
- **UX**: Confirmation dialog → progress indicator → success/error notification
- **Bulk approve**: Support multi-select in list view (process sequentially)

#### Ribbon Button: "Reject Request"

- Same location as approve button
- Prompts for rejection reason (text input dialog)
- Calls BFF API `POST /api/registration/requests/{id}/reject`

### 5.7 Demo Expiration

**DemoAccessExpirationService** — BFF API BackgroundService, runs daily at midnight UTC (same pattern as existing `DailySendCountResetService`).

**Daily check**:
1. Query Dataverse: `sprk_registrationrequest` where `sprk_status = Provisioned` and `sprk_expirationdate <= today`
2. For each expired request:
   a. Disable Entra ID account: `PATCH /users/{id}` → `{ "accountEnabled": false }`
   b. Remove from Demo Team in Dataverse (revokes security role)
   c. Revoke SPE container permissions
   d. Send expiration email via Communication Service
   e. Update `sprk_status` to "Expired"

**Pre-expiration warning**: 3 days before expiry, send "Your demo is expiring in 3 days" email with:
- Option to contact us for extension
- Offer to discuss production access

**Note**: We disable the Entra account rather than delete it — allows easy reactivation if the user requests an extension. Deletion can happen as a separate cleanup process for accounts expired > 90 days.

### 5.8 No-MFA Configuration for Demo Accounts

**Approach**: Conditional Access policy exclusion.

1. Create Entra ID security group: **"Spaarke Demo Users"**
2. All provisioned demo accounts are added to this group (Step 2 of provisioning)
3. Create or modify Conditional Access policy:
   - **Exclude**: "Spaarke Demo Users" group from MFA requirements
   - Scope: applies only to demo.spaarke.com accounts

**Important**: This is a one-time Azure Portal configuration, not automated by the provisioning pipeline. The pipeline just adds users to the group.

### 5.9 Browser Session Conflict Mitigation

Since demo accounts are internal (`demo.spaarke.com`) rather than B2B guest accounts, there's no cross-tenant session conflict. However, users with active Microsoft sessions in the same browser may experience credential caching issues.

**Mitigation**: Welcome email includes clear guidance:
- "Open the access URL in an InPrivate (Edge) or Incognito (Chrome) window"
- "This avoids conflicts with your existing Microsoft account sessions"

This is a known friction point with B2B guest access that internal accounts largely avoid — but the browser tip handles the remaining edge case.

---

## 6. Security Considerations

| Concern | Mitigation |
|---------|------------|
| Form spam/abuse | reCAPTCHA (website); rate limiting on BFF API; email domain validation |
| Unauthorized approval | Admin endpoint requires `Spaarke Registration Admin` role; ribbon button only visible to admins |
| Demo user over-privilege | Dedicated Demo BU with scoped security role; no cross-BU access; no delete permissions |
| Demo data leakage | Demo BU contains only synthetic/sample data |
| Stale demo accounts | Automated expiration via DemoAccessExpirationService |
| Password in email | Temp password with forced change on first login; welcome email sent to verified work email |
| Credential stuffing on demo accounts | No MFA, but accounts are time-limited and low-value (sample data only) |
| Provisioning service permissions | App-only Graph permissions: `User.ReadWrite.All`, `GroupMember.ReadWrite.All`, `Directory.ReadWrite.All` |
| PII in registration data | Dataverse handles at-rest encryption; access restricted to admin role |

---

## 7. Technology Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| User identity | Internal Entra ID account (`demo.spaarke.com`) | Avoids B2B guest cross-tenant conflicts; cleaner auth experience; full control over account lifecycle |
| Demo environment | Separate Dataverse environment (`spaarke-demo.crm.dynamics.com`) | Complete isolation from dev/prod |
| Request form | Public form on Spaarke website (no auth required) | Lower friction; reCAPTCHA for bot protection |
| Registration form | **Eliminated** — not needed | Admin approval triggers auto-provisioning directly; username/password auto-generated |
| Admin approval UI | MDA view + ribbon button | Centralized, auditable, filterable, extensible to production user mgmt; no separate admin UI to build |
| Provisioning | Synchronous (BFF API endpoint) | Simpler than Service Bus for Phase 1; pipeline takes ~10-30s; can add async later if volume demands |
| Licensing | Auto-assign 3 licenses via Graph API | Power Apps Plan 2 Trial + Fabric Free + Power Automate Free; no manual admin steps |
| MFA for demo users | Disabled via Conditional Access exclusion | Reduces demo friction; accounts are time-limited with sample data only |
| Expiration handling | BFF API BackgroundService (daily) | Same pattern as DailySendCountResetService; disable account rather than delete |
| Email notifications | Spaarke Communication Service | Existing infrastructure; branded templates; Dataverse audit trail |
| Password handling | Auto-generated temp password, forced change on first login | No password selection form needed; secure enough for demo |

---

## 8. Extensibility for Production

| Phase 1 (Demo) | Future (Production) |
|-----------------|---------------------|
| Internal demo accounts (`demo.spaarke.com`) | Customer-scoped accounts or B2B guest |
| Single pre-existing demo environment | Per-customer dedicated environments |
| Default 14-day access (adjustable) | Subscription-based access |
| Manual admin approval | Rules-based auto-approve for known domains |
| Single security role | Customer-specific role templates |
| Shared demo data | Customer-isolated data |
| Config-driven environment reference | Config-driven customer environment reference |

**Key extensibility points**:
- Provisioning pipeline already reads environment config — just add new entries to appsettings array
- `sprk_registrationrequest` table can add fields for customer tier, environment preference
- Provisioning steps are modular — can swap "create internal user" for "invite B2B guest" per request type

---

## 9. Open Questions (Resolved)

| # | Question | Resolution |
|---|----------|------------|
| 1 | Demo duration | Default 14 days; admin can adjust expiration date per record |
| 2 | Admin review UI | MDA view + ribbon button (not website admin panel) |
| 3 | User identity model | Internal accounts (`demo.spaarke.com`), not B2B guest |
| 4 | Registration form needed? | No — approval triggers auto-provisioning directly |
| 5 | MFA for demo users | Disabled via Conditional Access exclusion |
| 6 | Service Bus for provisioning? | No — synchronous for Phase 1 (simpler) |

### Remaining Open Questions

1. **Demo data**: Shared sample data across all demo users (simpler) vs. per-user data sets?
2. **Password policy**: Allow simple passwords for demo accounts, or enforce standard complexity?
3. **Account extension**: Can demo users request more time? If so, process and max duration?
4. **Concurrent user limit**: Any cap on active demo users?
5. **Demo data reset**: Automated cleanup of user-created data in demo environment?

---

## 10. Success Criteria

| Metric | Target |
|--------|--------|
| Request to provisioned | < 4 hours (with human approval) |
| Provisioning (after approval click) | < 60 seconds (fully automated) |
| Zero manual provisioning steps | After "Approve" click, everything is automated |
| Demo expiration enforcement | 100% — no stale accounts |
| Form completion rate | > 70% of visitors who start the form |

---

## 11. Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Graph API permission escalation** (`User.ReadWrite.All`, `Directory.ReadWrite.All`) | Medium | Scope to demo tenant only; document justification; review with security |
| **Power Apps Plan 2 Trial license availability** | Low | Trial is free and auto-assigned; falls within tenant license pool |
| **Demo data quality degrades** from user activity | Low | Weekly or on-demand data reset via scheduled BackgroundService |
| **Username collisions** (`firstname.lastname`) | Low | Auto-append number; check Entra ID before creation |
| **Browser session conflicts** despite internal accounts | Low | Welcome email guidance: use InPrivate/Incognito |
| **BFF API needs connectivity to Demo Dataverse** (different from dev) | Medium | Add Demo Dataverse connection string to BFF API config; may need separate S2S app registration |
| **Conditional Access misconfiguration** | Medium | Test thoroughly; demo group exclusion is narrow and well-scoped |
| **Temp password in email** | Low | Forced change on first login; email sent to verified work address; demo data only |

---

## 12. Implementation Scope — Complete Deliverables

**Project completion means everything is in place end-to-end.** When the last task is done, an admin can click "Approve" on a real request and the user receives a working demo account. No manual setup steps remain.

### A. BFF API — Registration & Provisioning (spaarke repo)

| Component | Location | Description |
|-----------|----------|-------------|
| Registration endpoints | `Sprk.Bff.Api/Endpoints/RegistrationEndpoints.cs` | Submit, approve, reject APIs |
| Provisioning service | `Sprk.Bff.Api/Services/Registration/DemoProvisioningService.cs` | Orchestrates all provisioning steps |
| Graph user service | `Sprk.Bff.Api/Services/Registration/GraphUserService.cs` | Create Entra user, assign licenses, manage group |
| Dataverse registration service | `Sprk.Bff.Api/Services/Registration/RegistrationDataverseService.cs` | CRUD for `sprk_registrationrequest`, systemuser sync, team assignment |
| Expiration BackgroundService | `Sprk.Bff.Api/Services/Registration/DemoExpirationService.cs` | Daily: disable expired accounts, send warnings |
| Email templates | `Sprk.Bff.Api/Services/Registration/EmailTemplates/` | HTML templates: admin notification, welcome, expiration warning, expired |
| Configuration | `appsettings.json` / Key Vault | Demo Dataverse URL, Graph permissions, SKU IDs, demo BU/team IDs |
| Request/response models | `Sprk.Bff.Api/Models/Registration/` | DTOs for endpoints |

### B. Website — Request Early Access Form (spaarke-website repo)

| Component | Location | Description |
|-----------|----------|-------------|
| Demo request page | `app/demo/page.tsx` (or `app/get-started/page.tsx`) | Public form with reCAPTCHA |
| Form component | `components/DemoRequestForm.tsx` | React 19 form with client-side validation |
| API route | `app/api/registration/demo-request/route.ts` | Next.js API route → proxies to BFF API |
| Confirmation page/state | Same page or `app/demo/thank-you/page.tsx` | "Thanks! We'll review within 1 business day." |
| Navigation link | Site header/hero | "Request Early Access" / "Try Spaarke" CTA button |

### C. Dataverse — Schema & MDA Configuration (Demo environment)

| Component | Description |
|-----------|-------------|
| `sprk_registrationrequest` table | Full schema per Section 5.4 — created via solution import or setup script |
| MDA views | "Pending Demo Requests", "All Demo Requests", "Active Demo Users", "Expired Demo Users" |
| MDA form | Registration request form with all fields, status timeline |
| Ribbon button: "Approve Demo Access" | List + form command bar; calls BFF API approve endpoint |
| Ribbon button: "Reject Request" | List + form command bar; prompts for reason; calls BFF API reject endpoint |
| Ribbon JS webresource | `sprk_registrationribbon.js` — button click handlers |
| Site map entry | Registration Requests added to MDA navigation |

**Note**: The `sprk_registrationrequest` table/views/ribbon are deployed to the demo environment via Dataverse solution import. The BU, team, security role, and sample data are **prerequisites** (already set up in the demo environment).

### D. Entra ID / Azure Setup Scripts (one-time)

| Script | Purpose |
|--------|---------|
| `Setup-EntraInfrastructure.ps1` | Create "Spaarke Demo Users" security group, Conditional Access MFA exclusion, Graph API permissions + admin consent |
| `Get-LicenseSkuIds.ps1` | Discover and output tenant SKU IDs for the 3 required licenses (for BFF config) |

These are **one-time setup scripts** for Entra ID infrastructure only. The demo Dataverse environment (solution, BU, team, role, SPE, sample data) is a **prerequisite** and not part of this project.

### E. Email Templates

| Template | Trigger | Content |
|----------|---------|---------|
| Admin notification | New request submitted | Request details, link to MDA record |
| Welcome email | Account provisioned | Username, temp password, access URL, browser tip, expiry date, quick start guide |
| Expiration warning | 3 days before expiry | "Demo expiring soon", contact us for extension, production access offer |
| Expired notification | Day of expiry | Account disabled, thank you, production access CTA |

### F. Testing & Verification

| Test | Description |
|------|-------------|
| End-to-end smoke test | Submit form → approve in MDA → verify account created → verify login works |
| Provisioning idempotency | Call approve twice → no duplicate users or emails |
| Expiration service | Create expired record → run service → verify account disabled |
| Username collision | Submit two "Jane Smith" requests → verify `jane.smith2@demo.spaarke.com` |
| Form validation | Required fields, email format, reCAPTCHA, duplicate email rejection |
| Ribbon button visibility | Button hidden when status ≠ Submitted |

### Delivery Checklist

When the project is complete, all of the following must be true:

- [ ] Website form at `spaarke.com/demo` accepts and submits requests
- [ ] BFF API creates `sprk_registrationrequest` in Demo Dataverse
- [ ] Admin notification email sent on new request
- [ ] MDA view shows pending requests with Approve/Reject ribbon buttons
- [ ] "Approve" button provisions full account in < 60 seconds
- [ ] User receives welcome email with working credentials
- [ ] User can log in to `https://spaarke-demo.crm.dynamics.com/` with demo account
- [ ] No MFA prompt for demo accounts
- [ ] Expired accounts are automatically disabled based on expiration date (default 14 days, adjustable)
- [ ] Pre-expiration warning email sent 3 days before expiry
- [ ] Entra ID setup scripted (security group, Conditional Access, Graph permissions)
- [ ] BFF config has demo environment settings (URL, BU name, team name, SPE container, SKU IDs)
