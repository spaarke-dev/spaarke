# Spaarke Self-Service Registration App — Design Document

> **Author**: Ralph Schroeder
> **Date**: 2026-03-10 (updated 2026-03-11)
> **Status**: Draft v2
> **Scope**: Phase 1 — Demo Access in Shared Environment

---

## 1. Executive Summary

Build a self-service registration process that allows prospective users to request access to Spaarke, go through an approval workflow, and receive automated provisioning of all required access (Dataverse model-driven app, SPE containers, BFF API authorization). The initial phase focuses on **demo access** — a time-limited trial in a shared demo environment with sample data.

Future phases extend to production access (shared business unit or dedicated environment) with customer-scoped registration and more robust identity governance.

---

## 2. Problem Statement

Today, onboarding a new Spaarke user is entirely manual:
1. An admin creates/confirms the user in Entra ID
2. An admin assigns Dataverse security roles via Power Platform Admin Center
3. An admin grants SPE container access
4. The user is manually notified

This doesn't scale for demo/trial access, multi-customer onboarding, or self-service scenarios. We need a streamlined, partially automated process that maintains security review while eliminating repetitive manual steps.

---

## 3. Objectives

| # | Objective | Phase |
|---|-----------|-------|
| 1 | User can submit a demo access request through a secure web form | Phase 1 |
| 2 | Request is reviewed/approved (human approval initially, automated later) | Phase 1 |
| 3 | Approved users are automatically provisioned in Dataverse + SPE | Phase 1 |
| 4 | User receives confirmation email with access instructions | Phase 1 |
| 5 | Demo access is time-limited (e.g., 14-30 days) with expiration handling | Phase 1 |
| 6 | Production access with customer BU/environment scoping | Phase 2 |
| 7 | Customer admin self-manages their user roster | Phase 2 |

---

## 4. Assumptions — Existing Infrastructure

### What We Already Have

| Resource | Details | Used For |
|----------|---------|----------|
| Entra ID Tenant | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | Identity provider for all Spaarke users |
| BFF API | `spe-api-dev-67e2xz` (.NET 8 Minimal API) | Middle-tier; validates tokens, OBO for Graph/Dataverse |
| BFF API App Registration | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | Token validation, OBO grants |
| Dataverse Dev Environment | `https://spaarkedev1.crm.dynamics.com` | Model-driven app, security roles, data |
| Dataverse App Registration | `170c98e1-d486-4355-bcbe-170454e0207c` | S2S Dataverse access (ClientSecret auth) |
| SPE Container Type | `8a6ce34c-6055-4681-8f87-2f4f9f921c06` | Document storage containers |
| Service Bus | `spaarke-servicebus-dev` | Job queue (has unused `customer-onboarding` queue) |
| Key Vault | `spaarke-spekvcert` | Secrets management |
| Azure OpenAI | `spaarke-openai-dev` | AI features (available in demo) |

### What We Need to Create

| Resource | Purpose | Phase |
|----------|---------|-------|
| Registration page on Spaarke website | Integrated registration form at `/demo` or `/get-started` (Next.js 16 + React 19) | Phase 1 |
| Website authentication | Entra ID sign-in on website (NextAuth / MSAL) for verified identity | Phase 1 |
| BFF API registration endpoints | Submission, approval, provisioning, expiration APIs | Phase 1 |
| BFF API provisioning BackgroundService | Service Bus consumer for automated user provisioning | Phase 1 |
| Dataverse tables | `sprk_registrationrequest`, `sprk_demoaccess` | Phase 1 |
| Email templates (Spaarke Communication Service) | Welcome, confirmation, expiration warning, conversion offer | Phase 1 |
| Demo Dataverse BU | Dedicated business unit for demo users in shared environment | Phase 1 |
| Demo SPE Container | Shared container with sample documents | Phase 1 |
| Admin review UI | Registration request management view in Spaarke MDA or website admin section | Phase 1 |

---

## 5. Architecture — Phase 1 (Demo Access)

### 5.1 High-Level Flow

```
┌─────────────────────┐     ┌──────────────────────┐     ┌──────────────────────┐
│  Spaarke Website     │     │  Approval             │     │  Provisioning        │
│  (Next.js 16)        │     │  (BFF API + Admin UI)  │     │  Pipeline            │
│                      │     │                        │     │  (BackgroundService)  │
│  1. User signs in    │     │  Admin reviews in      │     │                      │
│     (Entra ID SSO)   │────▶│  Spaarke MDA or        │────▶│  Service Bus queue    │
│  2. Fills demo       │     │  website admin panel   │     │  consumer executes:   │
│     request form     │     │                        │     │                      │
└─────────────────────┘     └──────────────────────┘     └──────────────────────┘
        │                        │                         │
        ▼                        ▼                         ▼
   Authenticated user      Admin approves/rejects     Automated steps:
   submits request         via BFF API endpoint       1. Send Entra guest invitation
   (identity verified)     Notification via Spaarke   2. Assign Dataverse security role
                           Communication Service      3. Add to Demo BU team
                                                      4. Grant SPE container access
                                                      5. Send welcome email (Comm Svc)
                                                      6. Set expiration timer
```

### 5.2 Registration — Integrated into Spaarke Website

**Technology: New page on existing Spaarke website (Next.js 16 + React 19 + Tailwind CSS)**

The registration experience is built directly into the Spaarke marketing website (`spaarke-website` repo), not as a separate app. This provides a seamless brand experience and reuses existing infrastructure (Azure Static Web App, reCAPTCHA, Application Insights).

**Why React 19**: The Spaarke website already runs React 19.2.3 and Next.js 16. Fluent UI v9 is compatible with React 19 (confirmed in our code-pages: AnalysisWorkspace, SprkChatPane both use `@fluentui/react-components ^9.54.0` with React 19). There is no stability reason to use React 18 — React 19 has been stable since December 2024 and is the standard for all new Spaarke web work.

#### Authentication-First Registration Flow

Users sign in to the Spaarke website before accessing the registration form. This provides:
- **Verified identity** — we know who is requesting access (Entra ID SSO)
- **Pre-populated fields** — name, email, organization pulled from token claims
- **Reduced spam** — authenticated users only, no bot protection needed on form
- **Audit trail** — every request tied to an Entra ID identity

**Website Auth Implementation**:
- Add NextAuth.js (or MSAL.js) with Entra ID provider to the website
- Authority: `https://login.microsoftonline.com/common/v2.0` (multi-tenant, any org user can sign in)
- The website app registration can be a new lightweight one (no API permissions needed beyond `User.Read`)
- Sign-in only required for `/demo` and `/account` routes (marketing pages remain public)

**Registration Form Fields**:

| Field | Type | Required | Source | Notes |
|-------|------|----------|--------|-------|
| First Name | text | Yes | Auto from token | Pre-populated, editable |
| Last Name | text | Yes | Auto from token | Pre-populated, editable |
| Email | email | Yes | Auto from token | Read-only (verified identity) |
| Organization | text | Yes | Auto from token | Pre-populated from `tid` claim org name |
| Job Title | text | No | Manual | Helps prioritize/qualify |
| Phone | tel | No | Manual | |
| Use Case | dropdown | Yes | Manual | "Document Management", "AI Analysis", "Financial Intelligence", "General Evaluation" |
| How Did You Hear About Us | dropdown | No | Manual | Marketing tracking |
| Notes | textarea | No | Manual | Additional context |
| Consent | checkbox | Yes | Manual | Terms of use, data processing |

**Anti-Spam/Bot Protection**:
- Authentication gate is the primary protection (user must sign in with a valid Entra ID org account)
- Rate limiting on submission endpoint (5 per user per day)
- Email domain validation (block consumer domains if desired)
- reCAPTCHA available as fallback (already integrated in website)

**Submission Flow**:
1. User visits `spaarke.com/demo` → prompted to sign in (Entra ID SSO)
2. After sign-in, registration form renders with pre-populated fields
3. User completes form → client-side validation
4. Next.js API route `POST /api/registration/demo-request` proxies to BFF API
5. BFF API validates, creates `sprk_registrationrequest` in Dataverse
6. BFF API publishes message to Service Bus `customer-onboarding` queue
7. BFF API sends notification to admin(s) via Spaarke Communication Service
8. User sees confirmation: "Thanks! We'll review your request within 1 business day."
9. User can check status at `spaarke.com/demo/status` (authenticated)

### 5.3 Registration API Endpoints

**New BFF API endpoints** — authenticated (bearer token from website sign-in):

#### Submit Demo Request

```
POST /api/registration/demo-request
Authorization: Bearer {user-token-from-website}
Content-Type: application/json

{
  "firstName": "Jane",
  "lastName": "Smith",
  "email": "jane.smith@contoso.com",
  "organization": "Contoso Ltd",
  "jobTitle": "Legal Operations Manager",
  "phone": "+1-555-0123",
  "useCase": "Document Management",
  "referralSource": "Conference",
  "notes": "Interested in SPE integration",
  "consentAccepted": true,
  "entraObjectId": "abc123-...",
  "tenantId": "def456-..."
}
```

**Response**: `202 Accepted` with tracking ID and status URL

```json
{
  "trackingId": "REG-20260311-A1B2",
  "statusUrl": "/api/registration/status/REG-20260311-A1B2",
  "message": "Your request has been submitted for review."
}
```

#### Check Request Status

```
GET /api/registration/status/{trackingId}
Authorization: Bearer {user-token}
```

Returns current status (Submitted, Under Review, Approved, Provisioning, Ready, Rejected).

#### Admin: Review Requests

```
GET /api/registration/requests?status=submitted
POST /api/registration/requests/{id}/approve
POST /api/registration/requests/{id}/reject
Authorization: Bearer {admin-token}
```

Admin endpoints secured with a new `Spaarke Registration Admin` security role check.

**Security**:
- All endpoints authenticated (no public/anonymous endpoints)
- Rate limiting: 5 submissions per user per day
- Input sanitization and length limits
- Correlation IDs for tracing (ADR-019)

### 5.4 Dataverse Data Model

#### `sprk_registrationrequest` (Registration Request)

| Column | Type | Notes |
|--------|------|-------|
| `sprk_registrationrequestid` | PK (GUID) | |
| `sprk_firstname` | Text (100) | |
| `sprk_lastname` | Text (100) | |
| `sprk_email` | Text (200) | Unique key |
| `sprk_organization` | Text (200) | |
| `sprk_jobtitle` | Text (200) | |
| `sprk_phone` | Text (50) | |
| `sprk_usecase` | Choice | Document Management, AI Analysis, Financial Intelligence, General |
| `sprk_referralsource` | Choice | Conference, Website, Referral, Search, Other |
| `sprk_notes` | Multiline Text | |
| `sprk_status` | Choice | Submitted (default), Under Review, Approved, Rejected, Provisioned, Expired |
| `sprk_requestdate` | DateTime | Auto-set on create |
| `sprk_reviewedby` | Lookup (SystemUser) | Who approved/rejected |
| `sprk_reviewdate` | DateTime | When approved/rejected |
| `sprk_rejectionreason` | Text (500) | If rejected |
| `sprk_provisioneddate` | DateTime | When access was granted |
| `sprk_expirationdate` | DateTime | Demo access expiry |
| `sprk_trackingid` | Text (50) | Public-facing reference (e.g., REG-20260310-A1B2) |
| `sprk_consentaccepted` | Boolean | |
| `sprk_consentdate` | DateTime | |

#### `sprk_demoaccess` (Demo Access Record — tracks active demo sessions)

| Column | Type | Notes |
|--------|------|-------|
| `sprk_demoaccessid` | PK (GUID) | |
| `sprk_registrationrequest` | Lookup | Link to original request |
| `sprk_userid` | Lookup (SystemUser) | Dataverse user record |
| `sprk_entraidobjectid` | Text (50) | Entra ID user object ID |
| `sprk_email` | Text (200) | |
| `sprk_status` | Choice | Active, Expired, Revoked, Converted (to production) |
| `sprk_activateddate` | DateTime | |
| `sprk_expirationdate` | DateTime | |
| `sprk_lastlogindate` | DateTime | Updated by BFF API on auth |
| `sprk_businessunit` | Lookup (BusinessUnit) | Demo BU |
| `sprk_containerid` | Text (200) | SPE container ID for demo |
| `sprk_securityrole` | Text (200) | Assigned role name |

### 5.5 Approval Workflow (BFF API BackgroundService)

**No Power Automate** — the entire workflow runs in the BFF API, consistent with ADR-001 (no Azure Functions, no Power Automate in the product).

**Trigger**: Service Bus message on `customer-onboarding` queue (published when request is created)

**BackgroundService Steps**:

1. **Duplicate Check** — Query Dataverse for existing requests by email
   - If duplicate with status "Provisioned" or "Active": Auto-reject with message "Already has access"
   - If duplicate with status "Submitted": Auto-reject as duplicate

2. **Validation** — Check email domain
   - Block disposable email domains (mailinator, guerrillamail, etc.)
   - Flag free consumer email providers (gmail, outlook) for manual review
   - Known partner/customer domains: auto-flag as priority

3. **Notify Admin** — Send email via Spaarke Communication Service (`POST /api/communications/send`)
   - HTML email to designated reviewer(s) with request details
   - Include deep link to admin review page (website or MDA)
   - Request appears in Spaarke MDA as a `sprk_registrationrequest` record

4. **Admin Reviews** — via:
   - **Option A**: Spaarke MDA view — admin opens the request record, clicks Approve/Reject (triggers BFF API endpoint)
   - **Option B**: Website admin panel at `spaarke.com/admin/registrations` (authenticated, admin role required)
   - **Option C**: Email reply link (deep link to approve/reject endpoint with one-time token)

5. **On Approval** (admin calls `POST /api/registration/requests/{id}/approve`):
   - Update request status to "Approved"
   - Publish provisioning message to Service Bus `customer-onboarding` queue with action "provision"
   - BackgroundService picks up and executes provisioning pipeline (Section 5.6)

6. **On Rejection** (admin calls `POST /api/registration/requests/{id}/reject`):
   - Update request status to "Rejected" with reason
   - Send rejection email to applicant via Spaarke Communication Service

7. **Escalation** — A separate scheduled BackgroundService (daily) checks for requests older than 3 business days in "Submitted" status and sends reminder emails to reviewers

### 5.6 Provisioning Pipeline

After approval, the provisioning BackgroundService processes the Service Bus message and executes all steps automatically.

**Orchestrator: BFF API BackgroundService** (consistent with ADR-001, reuses existing Service Bus `customer-onboarding` queue)

#### Step 1: Entra ID Guest Invitation

Since the user already signed in to the website (they have a valid Entra ID in their own tenant), we know their identity. To give them access to **our** Dataverse environment, they need to exist as a **guest user** in our Entra ID tenant.

**Process**:
- Check if guest already exists: `GET /users?$filter=mail eq '{email}'` (using app-only Graph token)
- If guest exists: skip invitation, proceed to Step 2
- If guest doesn't exist: Send guest invitation via Microsoft Graph:
  ```
  POST /invitations
  {
    "invitedUserEmailAddress": "jane.smith@contoso.com",
    "inviteRedirectUrl": "https://spaarkedev1.crm.dynamics.com",
    "sendInvitationMessage": false,
    "invitedUserDisplayName": "Jane Smith",
    "invitedUserType": "Guest"
  }
  ```
  - `sendInvitationMessage: false` — we send our own branded email via Spaarke Communication Service
  - The invitation creates a guest user object in our tenant
  - User redeems by signing in with their own org credentials (federated SSO)

**Why Guest Users**: Guest users in our Entra ID tenant is the standard Microsoft pattern for giving external users access to Dataverse. The user authenticates with their own organization's credentials — no separate password needed. Their access to our tenant is scoped by the security roles we assign.

**Graph API Permissions needed** (application-level, added to BFF API app registration):
- `User.Invite.All` (send guest invitations)
- `User.ReadWrite.All` (check existing users, update properties)
- `Directory.Read.All` (resolve tenant/org info)

#### Step 2: Dataverse User + Security Role Assignment

Using Dataverse S2S (existing `170c98e1...` app registration):

1. **Sync user to Dataverse** — Users sync automatically when they first access the environment, OR can be triggered via `systemuser` create with `azureactivedirectoryobjectid`
2. **Add user to Demo Business Unit team** — Assign to a "Demo Users" team that inherits appropriate security roles
3. **Assign security role** — `Spaarke Demo User` role (new, limited variant of full user role)
   - Read access to demo data only (BU-scoped)
   - No delete permissions
   - No admin features
   - AI features enabled (read-only playbooks)

#### Step 3: SPE Container Access

- Add the demo user to the **shared demo SPE container** with `Reader` or `Writer` permissions
- Use Graph API: `POST /storage/fileStorage/containers/{containerId}/permissions`
- Demo container pre-loaded with sample documents

#### Step 4: Send Welcome Email (Spaarke Communication Service)

Via existing `POST /api/communications/send` endpoint (app-only mode via shared mailbox):

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
  - Access URL (Dataverse MDA): `https://spaarkedev1.crm.dynamics.com`
  - **Power Apps license activation link** (see Section 5.8)
  - Quick start guide link
  - Demo expiration date
  - Support contact
  - Sample scenarios to try

#### Step 5: Create Demo Access Record

- Create `sprk_demoaccess` record linked to the registration request
- Set expiration date (request date + 14 or 30 days)

#### Step 6: Schedule Expiration

- BFF API **DemoAccessExpirationService** (BackgroundService, runs daily at midnight UTC — same pattern as `DailySendCountResetService`)
- Queries Dataverse for `sprk_demoaccess` records where `sprk_expirationdate <= today` and `sprk_status = Active`
- On expiration:
  1. Remove user from Demo BU team (revokes Dataverse security role)
  2. Revoke SPE container permissions via Graph API
  3. Send expiration notice email via Spaarke Communication Service
  4. Update `sprk_demoaccess.sprk_status` to "Expired"
  5. Update `sprk_registrationrequest.sprk_status` to "Expired"
- **Pre-expiration warning**: 3 days before expiry, sends "Your demo is expiring" email with option to request extension or convert to production

### 5.8 Power Apps Licensing for Demo Users

**This is the most significant operational consideration.** Guest users accessing a Dataverse model-driven app require a Power Apps license.

#### Licensing Options

| Option | Duration | Cost | Provisioning | Best For |
|--------|----------|------|--------------|----------|
| **Power Apps per-app plan** | Monthly | ~$5/user/app/month | Admin assigns in M365 Admin Center | Controlled demos, predictable cost |
| **Power Apps Trial** | 30 days | Free | User self-activates via link | Zero-cost demos, self-service |
| **Power Apps Developer Plan** | Indefinite | Free | User signs up | Dev/test only (not shared envs) |
| **Included with M365 E3/E5** | Ongoing | N/A | Already licensed | Users whose org has E3/E5 |

#### Recommended Approach for Demo Access

**Hybrid: Trial-first with per-app fallback**

1. **In the welcome email**, include a Power Apps trial activation link:
   - `https://aka.ms/tryppsa` — 30-day self-service trial
   - User clicks, activates trial, immediately has access
   - Aligns with demo period (30-day trial = 30-day demo)

2. **If trial isn't available** (user already used their one-time trial):
   - Admin assigns a **Power Apps per-app plan** ($5/month) from the M365 Admin Center
   - This is a manual step flagged to admin during provisioning
   - Can be automated later via Microsoft Graph licensing APIs

3. **Users with existing M365 E3/E5 licenses** already have Power Apps access — no additional license needed. The provisioning pipeline should check for this.

#### License Check in Provisioning Pipeline

After guest invitation (Step 1), before completing provisioning:
- Query Microsoft Graph: `GET /users/{userId}/licenseDetails`
- Check for Power Apps-relevant SKU IDs (E3, E5, Power Apps per-user, Power Apps per-app)
- If licensed: proceed normally
- If unlicensed: include trial activation link in welcome email + flag for admin review

#### Important Constraints

- Power Apps trial is **one-time per user** — cannot be reactivated
- Guest users from tenants with E3/E5 may already have Power Apps access via their home tenant, but this does NOT grant access to apps in our tenant — they need a license assigned in our tenant or a trial
- Per-app plans are assigned per environment — the demo environment needs its own allocation
- **Budget consideration**: At $5/user/month, 50 concurrent demo users = $250/month

### 5.7 Demo Environment Setup (One-Time)

**Business Unit**: Create "Spaarke Demo" business unit in Dataverse
- Isolates demo users from internal data
- BU-scoped security roles limit data visibility

**Security Role**: `Spaarke Demo User`
- Clone of `Spaarke AI Analysis User` with restrictions:
  - Organization-scope Read on reference data (playbooks, skills)
  - BU-scope Read/Write on documents, matters
  - No Delete on anything
  - No customization privileges

**Demo Data**: Pre-populated in Demo BU
- Sample matters/projects
- Sample documents (in demo SPE container)
- Sample AI playbooks (read-only)

**Demo SPE Container**: Shared container with sample documents
- Pre-loaded with ~20 sample legal documents
- Read-write for demo users (they can upload their own test docs)
- Reset weekly or on-demand (scheduled cleanup)

---

## 6. Security Considerations

| Concern | Mitigation |
|---------|------------|
| Registration abuse | Authentication-first (Entra ID sign-in required); rate limiting per user |
| Guest user over-privilege | Dedicated Demo BU with scoped security role; no cross-BU access |
| Demo data leakage | Demo BU contains only synthetic/sample data |
| Stale demo accounts | Automated expiration via DemoAccessExpirationService (daily BackgroundService) |
| PII in registration data | Dataverse handles at-rest encryption; access restricted to Registration Admin role |
| Credential management | No passwords stored; Entra ID federated SSO via guest invitation |
| Provisioning service permissions | App-only Graph permissions scoped to `User.Invite.All` + `User.ReadWrite.All` |
| Cross-tenant trust | Guest invitation uses standard Entra ID B2B — user's home tenant controls their credentials |

---

## 7. User Experience Flow

```
1. User visits spaarke.com → clicks "Try Spaarke" / "Request Demo"
2. Redirected to spaarke.com/demo → prompted to sign in (Entra ID SSO)
3. User signs in with their work account (any organization)
4. Registration form renders with pre-populated name/email/org
5. User completes remaining fields → submits
6. Confirmation page: "Thanks! Tracking ID: REG-20260311-A1B2"
7. User can check status anytime at spaarke.com/demo/status
8. (Behind scenes: Dataverse record created, admin notified via email)
9. Admin reviews request → Approves via Spaarke MDA or admin panel
10. (Behind scenes: provisioning pipeline runs — guest invite, roles, SPE)
11. User receives branded welcome email:
    - "Your Spaarke Demo Access is Ready!"
    - Power Apps license activation link (if needed)
    - Dataverse MDA access URL
    - Quick start guide
    - Demo expires on [date]
12. User activates Power Apps trial (if not already licensed)
13. User clicks MDA link → SSO via their Entra ID → lands in Spaarke
14. User explores Spaarke features with sample data for 14-30 days
15. Day 27: "Your demo is expiring in 3 days" email
16. Day 30: Access revoked, "Convert to production?" email sent
```

---

## 8. Technology Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Registration UI | Integrated into Spaarke website (Next.js 16) | Seamless brand experience, reuse existing infrastructure |
| React version | React 19 | Website already on 19.2.3; Fluent UI v9 compatible; stable since Dec 2024 |
| Website authentication | NextAuth.js with Entra ID provider (multi-tenant) | Verified identity before registration; pre-populated fields |
| Submission endpoint | BFF API (authenticated) | Reuse existing API; identity verified via token |
| Approval workflow | BFF API BackgroundService + Service Bus | No Power Automate — consistent with ADR-001 and product architecture |
| Admin notification | Spaarke Communication Service | Existing email infrastructure; branded templates; audit trail in Dataverse |
| Provisioning orchestration | BFF API BackgroundService + Service Bus `customer-onboarding` queue | ADR-001 compliant; reuses existing queue |
| User identity | Entra ID Guest Invitation (B2B) | Standard Microsoft pattern; user keeps their own credentials |
| Email notifications | Spaarke Communication Service (`POST /api/communications/send`) | Existing service; HTML templates; tracking records; approved sender management |
| Demo data isolation | Dataverse Business Unit | Native multi-tenancy mechanism |
| Demo expiration | BFF API BackgroundService (daily scheduled) | Same pattern as DailySendCountResetService |
| Power Apps licensing | Trial-first with per-app fallback | Zero-cost for most demos; $5/user/month for non-trial users |

---

## 9. Phase 2 Considerations (Future — Not in Scope)

For context on where this design extends:

- **Production customer onboarding**: provision dedicated BU or environment, assign customer admin role
- **Customer admin portal**: customer admins manage their own user roster
- **Automated approval**: rules-based auto-approve for known domains/organizations
- **Usage analytics**: track demo engagement to qualify leads
- **Conversion flow**: seamless upgrade from demo to production access
- **Multi-environment**: separate registration for dev/staging/production

---

## 10. Open Questions

1. **Demo duration**: 30 days recommended (aligns with Power Apps trial period). Configurable via Dataverse environment variable?
2. **Concurrent demo users**: Any limit on total active demo users? At $5/month per-app fallback, what's the budget ceiling?
3. **Demo data reset**: Should each demo user get a fresh data set (isolated), or share the same sample data (simpler)?
4. **Approval SLA**: Is 1 business day review acceptable, or should some requests auto-approve (e.g., known partner domains)?
5. **Website auth scope**: Should sign-in be required only for `/demo` routes, or should we add auth to other website sections (e.g., blog, support portal)?
6. **Admin review UI**: Spaarke MDA view (simpler, Dataverse-native) vs. website admin panel (richer, but more to build)?
7. **Extension requests**: Can demo users request an extension? If so, how many days and how many times?

---

## 11. Success Criteria

| Metric | Target |
|--------|--------|
| Registration to provisioned | < 4 hours (with human approval) |
| Provisioning (after approval) | < 5 minutes (fully automated) |
| Zero manual provisioning steps | After approval click, everything is automated |
| Demo expiration enforcement | 100% — no stale demo accounts |
| Registration form completion rate | > 70% of visitors who start the form |

---

## 12. Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Power Apps licensing cost** at scale | High | Trial-first approach; per-app plan at $5/user/month for fallback; budget cap on concurrent demos |
| **Guest invitation blocked** by user's org tenant | Medium | Document prerequisite (org must allow external collaboration); provide troubleshooting guide; detect during provisioning and notify admin |
| **Entra ID B2B guest limits** | Low | Default limit is 5 guests per inviter per day; use app-only permissions (no limit) |
| **Demo data quality degrades** over time | Low | Weekly reset of demo BU sample data via scheduled BackgroundService |
| **Website auth adds friction** to registration | Medium | Sign-in is one click for users with Entra ID; alternative: allow unauthenticated registration with email verification as fallback |
| **BFF API scope expansion** | Low | New endpoints are well-isolated in `RegistrationEndpoints.cs` + `RegistrationModule.cs`; follows existing endpoint pattern |
| **Power Apps trial already used** by returning users | Medium | Detect via Graph licensing API; flag for admin to assign per-app plan |
