# SDAP External Portal - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-01-19
> **Source**: design.md
> **Project**: 3 of 3 in Office + Teams Integration Initiative
> **Parallel Development**: Can develop alongside SDAP-office-integration

---

## Executive Summary

Build a Power Pages-based external collaboration portal enabling outside counsel and external partners to access shared Matters, Projects, and Documents without requiring full Dataverse/Power Apps licenses. The portal uses invitation-based access with Microsoft Entra External ID for authentication, providing a constrained, auditable experience for external users. This project defines the entitlement model (ExternalUser, Invitation, AccessGrant) consumed by Office and Teams integrations.

---

## Scope

### In Scope

- **Entra External ID Configuration**
  - Create new External ID tenant
  - Email/password authentication only (no social providers V1)
  - MFA enforcement per tenant policy
  - Session management

- **Power Pages Portal**
  - Home dashboard with accessible workspaces
  - Workspace list and detail pages
  - Document viewer with preview and download
  - User profile management
  - Invitation redemption flow
  - Access denied / request access pages

- **Entitlement Model (Dataverse)**
  - ExternalUser table
  - Invitation table
  - AccessGrant table
  - ExternalAccessLog table

- **Backend APIs**
  - Invitation management (`/external/invitations/*`)
  - Portal user APIs (`/external/my/*`)
  - Document access APIs (`/external/documents/*`)
  - Admin management APIs (`/admin/external-*`)

- **External User Capabilities**
  - View accessible Matters/Projects/Documents
  - Download documents (if role permits)
  - Upload documents to workspaces (Contribute role)
  - Request access to resources

- **Audit Logging**
  - All external user actions logged
  - 1-year retention

### Out of Scope

- **Social identity providers** - Email/password only for V1
- **Power Apps code apps** - Evaluate when GA
- **Mobile-optimized portal** - Responsive but not native app
- **Real-time collaboration** - View/download/upload only, no co-authoring
- **External user management UI** - Internal users manage via model-driven app

### Affected Areas

| Area | Path | Changes |
|------|------|---------|
| BFF API | `src/server/api/Sprk.Bff.Api/` | New `/external/*` and `/admin/*` endpoints |
| Power Pages | `src/solutions/portal/` | New Power Pages site |
| Dataverse | `src/solutions/` | ExternalUser, Invitation, AccessGrant, ExternalAccessLog tables |
| Entra | Azure Portal | New External ID tenant configuration |

---

## Requirements

### Functional Requirements

| ID | Requirement | Acceptance Criteria |
|----|-------------|---------------------|
| **FR-01** | Create invitation | Internal user can create invitation for email with scope and role |
| **FR-02** | Invitation email | System sends email with redemption link and expiry info |
| **FR-03** | Invitation expiry | Invitations expire after 48 hours by default |
| **FR-04** | Invitation redemption | External user can redeem invitation and gain access |
| **FR-05** | New user registration | First-time external users complete Entra registration |
| **FR-06** | Returning user login | Existing external users sign in with credentials |
| **FR-07** | Portal home | Authenticated user sees dashboard with accessible content |
| **FR-08** | Workspace browsing | User can browse Matters/Projects they have access to |
| **FR-09** | Document list | User can see documents within accessible workspaces |
| **FR-10** | Document preview | User can preview PDF, images, Office docs inline |
| **FR-11** | Document download | User with Download role can download documents |
| **FR-12** | Document upload | User with Contribute role can upload to workspace |
| **FR-13** | Access request | User can request access to denied resources |
| **FR-14** | Profile management | User can update display name and preferences |
| **FR-15** | Revoke invitation | Internal user can revoke pending invitations |
| **FR-16** | Revoke access | Internal user can revoke active access grants |
| **FR-17** | Audit logging | All external actions logged with user, action, resource, timestamp |

### Non-Functional Requirements

| ID | Requirement | Target |
|----|-------------|--------|
| **NFR-01** | Page load time | Portal pages load within 3 seconds |
| **NFR-02** | Preview generation | Document preview within 5 seconds |
| **NFR-03** | Search response | Results within 2 seconds |
| **NFR-04** | Scalability | Support 10,000+ external users |
| **NFR-05** | Concurrency | Support 100+ concurrent sessions |
| **NFR-06** | Security | HTTPS only, no sensitive data in URLs |
| **NFR-07** | Session timeout | 8 hours, re-auth required after |
| **NFR-08** | Failed login lockout | 5 failed attempts = 15 minute lockout |
| **NFR-09** | Audit retention | 1 year log retention |
| **NFR-10** | GDPR compliance | Support right to deletion for external users |

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|-----|-----------|
| **ADR-001** | Minimal API for `/external/*` endpoints |
| **ADR-007** | SpeFileStore for document content retrieval |
| **ADR-008** | Endpoint filters for authorization |
| **ADR-019** | ProblemDetails for errors |

### MUST Rules

- ✅ MUST use Power Pages for external portal
- ✅ MUST use Entra External ID for authentication
- ✅ MUST create new External ID tenant (not existing B2C)
- ✅ MUST use email/password authentication only (no social V1)
- ✅ MUST enforce MFA per tenant policy
- ✅ MUST validate AccessGrant on every document access
- ✅ MUST log all external user actions to ExternalAccessLog
- ✅ MUST use row-level security via table permissions
- ✅ MUST expire invitations after 48 hours by default
- ✅ MUST retain access logs for 1 year

### MUST NOT Rules

- ❌ MUST NOT expose document content to unauthorized users
- ❌ MUST NOT cache sensitive data client-side
- ❌ MUST NOT include sensitive IDs in URLs
- ❌ MUST NOT allow browsing beyond granted scope
- ❌ MUST NOT use social identity providers in V1

### Technology Stack

| Technology | Purpose |
|------------|---------|
| Power Pages | Portal framework |
| Entra External ID | Authentication |
| Liquid templates | Page rendering |
| JavaScript | Client-side interactions |
| Web API | Custom endpoints via BFF |

---

## Architecture Overview

### Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    External User Browser                        │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼ HTTPS
┌─────────────────────────────────────────────────────────────────┐
│                      Power Pages Portal                         │
├─────────────────────────────────────────────────────────────────┤
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌───────────┐ │
│  │    Home     │ │ Workspaces  │ │  Documents  │ │  Profile  │ │
│  │ (Liquid)    │ │  (Liquid)   │ │  (Liquid)   │ │ (Liquid)  │ │
│  └─────────────┘ └─────────────┘ └─────────────┘ └───────────┘ │
│                                                                 │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │              Table Permissions (Row-Level)              │   │
│  │  ExternalUser → AccessGrant → Document/Matter/Project   │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
          │                                    │
          │ Dataverse                          │ Web API
          ▼                                    ▼
┌─────────────────────┐           ┌─────────────────────────────┐
│      Dataverse      │           │      Spaarke BFF API        │
├─────────────────────┤           ├─────────────────────────────┤
│ • ExternalUser      │           │  /external/invitations/*    │
│ • Invitation        │           │  /external/my/*             │
│ • AccessGrant       │           │  /external/documents/*      │
│ • ExternalAccessLog │           │  /admin/external-*          │
│ • Document          │           └──────────────┬──────────────┘
│ • Matter/Project    │                          │
└─────────────────────┘                          ▼
                                  ┌─────────────────────────────┐
                                  │       SpeFileStore          │
                                  │   (Document Content)        │
                                  └─────────────────────────────┘
```

### Invitation Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                    INVITATION CREATION                          │
└─────────────────────────────────────────────────────────────────┘

Internal User                    System                    External User
     │                             │                             │
     │  "Grant access" in          │                             │
     │   Outlook/Teams             │                             │
     │ ─────────────────────────►  │                             │
     │                             │                             │
     │                    Create Invitation                      │
     │                    (48hr expiry)                          │
     │                             │                             │
     │                    Generate token                         │
     │                             │                             │
     │                    Send email with                        │
     │                    redemption link ──────────────────────►│
     │                             │                             │
     │                             │         Click link          │
     │                             │◄────────────────────────────│
     │                             │                             │
     │                    Validate token                         │
     │                    (not expired/revoked)                  │
     │                             │                             │
     │                             │      New user?              │
     │                             │      ┌─────┐                │
     │                             │      │ Yes │───► Entra      │
     │                             │      └─────┘    Registration│
     │                             │      ┌─────┐                │
     │                             │      │ No  │───► Sign In    │
     │                             │      └─────┘                │
     │                             │                             │
     │                    Create AccessGrant(s)                  │
     │                    Mark Invitation Redeemed               │
     │                             │                             │
     │                             │      Redirect to            │
     │                             │      Portal Home ──────────►│
     │                             │                             │
```

### Access Control Model

```
┌─────────────────────────────────────────────────────────────────┐
│                     ACCESS CONTROL LAYERS                       │
└─────────────────────────────────────────────────────────────────┘

Layer 1: Authentication (Entra External ID)
├── User must be authenticated
├── Session must be valid (< 8 hours)
└── MFA completed if required

Layer 2: Web Role (Power Pages)
├── "External User" role assigned
└── Grants access to portal pages

Layer 3: Table Permissions (Power Pages)
├── ExternalUser: Read own record only
├── AccessGrant: Read where externaluser = me AND status = Active
├── Document: Read via AccessGrant parental
├── Matter/Project: Read via AccessGrant parental
└── ExternalAccessLog: Create (for audit)

Layer 4: API Authorization (BFF)
├── Validate AccessGrant exists for resource
├── Check role permits operation (View/Download/Upload)
└── Log action to ExternalAccessLog
```

### Roles and Permissions

| Role | View | Download | Upload |
|------|------|----------|--------|
| ViewOnly | ✅ | ❌ | ❌ |
| Download | ✅ | ✅ | ❌ |
| Contribute | ✅ | ✅ | ✅ |

---

## API Contracts

### POST /external/invitations

Create invitation(s) for external recipients.

**Request:**
```json
{
  "recipients": [
    {
      "email": "counsel@lawfirm.com",
      "role": "Download"
    }
  ],
  "scope": {
    "type": "Matter",
    "ids": ["guid1"]
  },
  "message": "Please review the attached documents for the Smith case.",
  "expiryHours": 48
}
```

**Response (201 Created):**
```json
{
  "invitations": [
    {
      "id": "guid",
      "recipientEmail": "counsel@lawfirm.com",
      "token": "abc123...",
      "redemptionUrl": "https://portal.spaarke.com/redeem?token=abc123",
      "expiresAt": "2026-01-21T10:00:00Z",
      "status": "Pending"
    }
  ]
}
```

### GET /external/invitations/{token}

Validate invitation token (before redemption).

**Response (200 OK):**
```json
{
  "valid": true,
  "recipientEmail": "counsel@lawfirm.com",
  "scope": {
    "type": "Matter",
    "names": ["Smith vs Jones"]
  },
  "role": "Download",
  "expiresAt": "2026-01-21T10:00:00Z",
  "invitedBy": "John Internal"
}
```

**Response (400 Bad Request - Invalid/Expired):**
```json
{
  "valid": false,
  "reason": "Expired",
  "message": "This invitation has expired. Please request a new invitation."
}
```

### POST /external/invitations/{token}/redeem

Complete invitation redemption.

**Request:**
```json
{
  "externalUserId": "guid (from Entra auth)"
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "grantsCreated": 1,
  "redirectUrl": "/workspaces/guid"
}
```

### GET /external/my/workspaces

List accessible workspaces for current user.

**Response:**
```json
{
  "workspaces": [
    {
      "id": "guid",
      "type": "Matter",
      "name": "Smith vs Jones",
      "role": "Download",
      "documentCount": 15,
      "grantedAt": "2026-01-19T10:00:00Z"
    }
  ]
}
```

### GET /external/my/documents

List all accessible documents for current user.

**Response:**
```json
{
  "documents": [
    {
      "id": "guid",
      "name": "Contract.docx",
      "type": "Word",
      "size": 125000,
      "modifiedAt": "2026-01-18T14:30:00Z",
      "workspace": { "id": "guid", "name": "Smith vs Jones", "type": "Matter" },
      "canDownload": true,
      "canUpload": false
    }
  ]
}
```

### GET /external/documents/{id}

Get document metadata.

**Response:**
```json
{
  "id": "guid",
  "name": "Contract.docx",
  "type": "Word",
  "size": 125000,
  "modifiedAt": "2026-01-18T14:30:00Z",
  "modifiedBy": "John Internal",
  "workspace": { "id": "guid", "name": "Smith vs Jones" },
  "previewUrl": "/external/documents/guid/preview",
  "canDownload": true,
  "canUpload": false
}
```

### GET /external/documents/{id}/content

Download document content.

**Response:** Binary file stream with appropriate Content-Type and Content-Disposition headers.

**Side Effect:** Creates ExternalAccessLog entry with action = "Download"

### GET /external/documents/{id}/preview

Get preview content for inline display.

**Response:**
- PDF: Binary stream
- Images: Binary stream
- Office docs: Redirect to Office Online viewer URL

**Side Effect:** Creates ExternalAccessLog entry with action = "View"

### POST /external/workspaces/{id}/documents

Upload document to workspace (Contribute role required).

**Request:** Multipart form data with file

**Response (201 Created):**
```json
{
  "documentId": "guid",
  "name": "NewDocument.pdf",
  "uploadedAt": "2026-01-19T11:00:00Z"
}
```

### POST /external/access-requests

Request access to a resource.

**Request:**
```json
{
  "resourceType": "Matter",
  "resourceId": "guid",
  "reason": "Need to review for upcoming case review"
}
```

**Response (201 Created):**
```json
{
  "requestId": "guid",
  "status": "Pending",
  "message": "Your request has been submitted to the workspace owner"
}
```

### Admin APIs

| Endpoint | Purpose |
|----------|---------|
| GET /admin/external-users | List all external users (paginated) |
| GET /admin/external-users/{id} | Get external user details |
| GET /admin/external-users/{id}/grants | List user's access grants |
| DELETE /admin/external-users/{id} | Deactivate external user (GDPR) |
| GET /admin/invitations | List invitations (filter by status) |
| POST /admin/invitations/{id}/revoke | Revoke pending invitation |
| DELETE /admin/access-grants/{id} | Revoke access grant |
| GET /admin/workspaces/{id}/external-access | List external access to workspace |
| GET /admin/audit-log | Query external access logs |

---

## Dataverse Schema

### ExternalUser Table

| Column | Type | Description |
|--------|------|-------------|
| sprk_externaluserid | GUID | Primary key |
| sprk_email | String(100) | Email address (unique, indexed) |
| sprk_displayname | String(200) | Display name |
| sprk_entraobjectid | String(50) | Entra External ID object ID (unique) |
| sprk_status | OptionSet | Active=1, Suspended=2, Deactivated=3 |
| sprk_firstsignin | DateTime | First portal sign-in |
| sprk_lastsignin | DateTime | Most recent sign-in |
| sprk_createdbycontact | Lookup(SystemUser) | Internal sponsor who first invited |
| sprk_organization | String(200) | External organization name |

### Invitation Table

| Column | Type | Description |
|--------|------|-------------|
| sprk_invitationid | GUID | Primary key |
| sprk_name | String(100) | Auto-generated: "Invite - {email} - {date}" |
| sprk_recipientemail | String(100) | Invitee email (indexed) |
| sprk_token | String(100) | Unique redemption token (unique, indexed) |
| sprk_status | OptionSet | Pending=1, Redeemed=2, Expired=3, Revoked=4 |
| sprk_role | OptionSet | ViewOnly=1, Download=2, Contribute=3 |
| sprk_scopetype | OptionSet | Matter=1, Project=2, Document=3 |
| sprk_scopeids | Memo | JSON array of resource IDs |
| sprk_invitedby | Lookup(SystemUser) | Internal user who sent |
| sprk_sentdate | DateTime | When invitation sent |
| sprk_expirydate | DateTime | Invitation expiry (default: +48 hours) |
| sprk_redeemeddate | DateTime | When redeemed (null if pending) |
| sprk_externaluser | Lookup(ExternalUser) | Link after redemption |
| sprk_message | Memo | Optional personal message |

### AccessGrant Table

| Column | Type | Description |
|--------|------|-------------|
| sprk_accessgrantid | GUID | Primary key |
| sprk_name | String(100) | Auto-generated: "{user} - {resource}" |
| sprk_externaluser | Lookup(ExternalUser) | Grantee (indexed) |
| sprk_resourcetype | OptionSet | Matter=1, Project=2, Document=3 |
| sprk_resourceid | String(50) | Resource ID (indexed) |
| sprk_role | OptionSet | ViewOnly=1, Download=2, Contribute=3 |
| sprk_grantedby | Lookup(SystemUser) | Internal user who granted |
| sprk_granteddate | DateTime | When granted |
| sprk_expirydate | DateTime | Optional expiry (null = permanent) |
| sprk_status | OptionSet | Active=1, Expired=2, Revoked=3 |
| sprk_invitation | Lookup(Invitation) | Source invitation |
| sprk_revokedby | Lookup(SystemUser) | Who revoked (if revoked) |
| sprk_revokeddate | DateTime | When revoked |

### ExternalAccessLog Table

| Column | Type | Description |
|--------|------|-------------|
| sprk_accesslogid | GUID | Primary key |
| sprk_name | String(100) | Auto-generated |
| sprk_externaluser | Lookup(ExternalUser) | Who (indexed) |
| sprk_action | OptionSet | SignIn=1, SignOut=2, View=3, Download=4, Upload=5, Search=6, FailedAccess=7 |
| sprk_resourcetype | OptionSet | Document=1, Matter=2, Project=3, Portal=4 |
| sprk_resourceid | String(50) | What resource (indexed) |
| sprk_timestamp | DateTime | When (indexed, for retention) |
| sprk_ipaddress | String(50) | Client IP |
| sprk_useragent | String(500) | Browser/client info |
| sprk_details | Memo | Additional context (JSON) |
| sprk_correlationid | String(50) | For tracing |

**Retention:** Bulk delete job to remove records older than 1 year based on sprk_timestamp.

---

## Power Pages Configuration

### Site Settings

| Setting | Value |
|---------|-------|
| Site Name | Spaarke External Portal |
| Authentication | Entra External ID |
| Default Page | /home |
| Theme | Spaarke brand theme |
| Session Timeout | 8 hours |

### Web Roles

| Role | Description | Assigned To |
|------|-------------|-------------|
| External User | Base role for authenticated external users | All authenticated |
| Anonymous | Public pages only (sign-in, redeem landing) | Unauthenticated |

### Table Permissions

| Table | Role | Permission | Scope |
|-------|------|------------|-------|
| ExternalUser | External User | Read | Where entraobjectid = current user |
| AccessGrant | External User | Read | Where externaluser = current user AND status = Active |
| Invitation | External User | Read | Where recipientemail = current user email |
| Document | External User | Read | Parental via AccessGrant |
| Matter | External User | Read | Parental via AccessGrant |
| Project | External User | Read | Parental via AccessGrant |
| ExternalAccessLog | External User | Create | Global (for audit logging) |

### Page Templates

| Page | Route | Template | Purpose |
|------|-------|----------|---------|
| Home | /home | home.liquid | Dashboard |
| Workspaces | /workspaces | workspaces.liquid | Workspace list |
| Workspace Detail | /workspaces/{id} | workspace-detail.liquid | Documents in workspace |
| Document | /documents/{id} | document.liquid | Document viewer |
| Profile | /profile | profile.liquid | User settings |
| Redeem | /redeem | redeem.liquid | Invitation redemption |
| Access Denied | /access-denied | access-denied.liquid | No access message |
| Sign In | /signin | signin.liquid | Entra redirect |

---

## Entra External ID Configuration

### Tenant Setup

| Item | Configuration |
|------|---------------|
| Tenant Type | External ID (not B2C) |
| User Flows | Sign-up/Sign-in, Password Reset |
| Authentication | Email + Password only |
| MFA | Enabled per policy |
| Branding | Spaarke logo and colors |

### Claims Configuration

| Claim | Source | Mapping |
|-------|--------|---------|
| oid | Entra | ExternalUser.sprk_entraobjectid |
| email | Entra | ExternalUser.sprk_email |
| name | Entra | ExternalUser.sprk_displayname |

### App Registration

| Setting | Value |
|---------|-------|
| Redirect URIs | https://portal.spaarke.com/signin-oidc |
| ID Token | Enabled |
| Scopes | openid, profile, email |

---

## Success Criteria

| # | Criterion | Verification Method |
|---|-----------|---------------------|
| 1 | Entra External ID tenant created and configured | Azure portal verification |
| 2 | Internal user can create invitation from Outlook | E2E test with email delivery |
| 3 | Invitation email received with redemption link | Email verification |
| 4 | New external user can register and redeem | E2E test with new email |
| 5 | Returning user can sign in | E2E test with existing account |
| 6 | Portal shows only accessible workspaces | Test with different grants |
| 7 | Document preview works (PDF, Office, images) | Manual test each type |
| 8 | Download works for Download role | Test download functionality |
| 9 | Upload works for Contribute role | Test upload functionality |
| 10 | Upload denied for Download role | Negative test |
| 11 | Access log captures all actions | Query log after operations |
| 12 | Invitation expires after 48 hours | Time-based test |
| 13 | Revoked grant denies access immediately | Revoke and verify denial |
| 14 | GDPR deletion removes user data | Delete user and verify |

---

## Dependencies

### Prerequisites

| Dependency | Status | Notes |
|------------|--------|-------|
| Document entity | From SDAP-office-integration | Core document model |
| Matter/Project entities | From SDAP-office-integration | Workspace context |
| SpeFileStore | From SDAP-office-integration | Document content |
| UAC module | From SDAP-office-integration | Authorization patterns |

### External Dependencies

| Dependency | Purpose |
|------------|---------|
| Entra External ID | Authentication (new tenant) |
| Power Pages license | Portal hosting |
| Office Online Server / Graph | Document preview |
| SendGrid / Azure Email | Invitation emails |

### Provides to Other Projects

| Artifact | Consumer |
|----------|----------|
| POST /external/invitations API | Office add-ins, Teams app |
| ExternalUser entity | All projects |
| AccessGrant entity | All projects |
| Invitation entity | Email templates |

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Entra External ID | Existing or new? | Create new tenant | Additional setup required |
| Social providers | In scope? | Email/password only V1 | Simpler auth config |
| Contribute role | What can they do? | View + Download + Upload | Full upload capability |
| Invitation expiry | Default duration? | 48 hours | Short-lived invitations |
| Access grant expiry | Default? | No expiry (permanent) | Until explicitly revoked |
| Log retention | How long? | 1 year | Bulk delete job needed |
| Document preview | Which service? | Office Online/Graph for Office, native for PDF/images | Standard approach |

---

## Assumptions

| Topic | Assumption | Affects |
|-------|------------|---------|
| Email service | Azure Communication Services or SendGrid available | Invitation emails |
| Power Pages license | Organization has Power Pages capacity | Portal hosting |
| Office Online access | Graph API access for doc preview | Preview functionality |
| External user volume | <10,000 users V1 | Scalability approach |

---

## Test Plan Overview

### Test Categories

1. **Unit Tests**: API endpoints, token validation
2. **Integration Tests**: Entra auth flow, Dataverse operations
3. **E2E Tests**: Full invitation → redemption → access flow
4. **Security Tests**: Access denial, permission boundaries
5. **Compliance Tests**: GDPR deletion, log retention
6. **Performance Tests**: Concurrent sessions, page load times

### Key Test Scenarios

| Scenario | Expected Result |
|----------|-----------------|
| Valid invitation redemption | Access granted, redirected to portal |
| Expired invitation redemption | Error: invitation expired |
| Revoked invitation redemption | Error: invitation revoked |
| Access document without grant | 403 Forbidden |
| Download with ViewOnly role | 403 Forbidden |
| Upload with Contribute role | 201 Created |
| Upload with Download role | 403 Forbidden |
| Sign in after grant revoked | Access denied to revoked resources |

---

*AI-optimized specification. Original design: design.md*
