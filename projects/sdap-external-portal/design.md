# design.md — SDAP External Portal (Outside Counsel Collaboration)

Date: 2026-01-19
Audience: Claude Code (primary), Spaarke Engineering/Product (secondary)
Scope: Power Pages external collaboration portal for outside counsel access

## Project Context

This is **Project 3 of 3** in the Spaarke Office + Teams integration initiative:
1. SDAP-office-integration — Outlook + Word add-ins with shared platform
2. SDAP-teams-app — Teams integration
3. **SDAP-external-portal** (this project) — Power Pages external collaboration

### Development Independence

This project can be developed **in parallel** with SDAP-office-integration:
- Defines its own Dataverse entities (ExternalUser, Invitation, AccessGrant)
- Provides APIs that Office/Teams projects consume for "Grant access" features
- Power Pages development is largely independent of Office add-in work

---

## 1. Objective

Enable external collaboration (outside counsel, external partners) without granting full Power Apps/model-driven app access:
- **Dedicated external portal**: Constrained "Outside Counsel Portal" experience
- **Invitation-based access**: Internal users grant access to specific Matters/Projects/Documents
- **Scope-limited entitlements**: External users see only what they're explicitly granted
- **Secure authentication**: Microsoft Entra External ID for external identity management

### Core Requirement

Corporate counsel wants to share documents with external parties (outside counsel, co-counsel, clients) without:
- Granting full Dataverse/Power Apps licenses
- Exposing internal model-driven app interfaces
- Creating security risks from overly broad access

---

## 2. Design Principles and Non-Negotiables

### 2.1 Least-Privilege Access

- External users access ONLY explicitly granted resources
- No browsing beyond granted scope
- Time-limited access with expiry options
- Revocable at any time by internal users

### 2.2 Authorization is Server-Side

- All access checks enforced by Spaarke API/UAC
- Power Pages displays only what AccessGrant allows
- No client-side trust; server is source of truth

### 2.3 Auditable Collaboration

- Log all external access: who, what, when, from where
- Track invitation lifecycle: sent, viewed, redeemed, expired, revoked
- Document open/download events for compliance

### 2.4 Self-Service Where Appropriate

- External users can complete onboarding independently
- Password reset, profile management without internal IT involvement
- Access requests flow to workspace owners, not IT admins

---

## 3. Technology Choice: Power Pages

### 3.1 Rationale

**Primary recommendation: Power Pages** with Microsoft Entra External ID

| Factor | Power Pages Advantage |
|--------|----------------------|
| Licensing | External user licensing designed for portal access |
| Authentication | Native Entra External ID integration |
| Dataverse access | Direct table access with web roles |
| Customization | Liquid templates, JavaScript, custom web APIs |
| Production readiness | GA, enterprise-supported |

### 3.2 Alternative Considered

Power Apps "code apps" are documented as **preview** (as of Jan 2026):
- Require enabling environment options
- Licensing clarity for large external populations unclear
- Not recommended as default until GA

**Decision**: Use Power Pages for V1. Document roadmap to evaluate code apps when GA.

---

## 4. Entitlement Model

### 4.1 Core Entities

| Entity | Purpose |
|--------|---------|
| **ExternalUser** | Identity mapping for authenticated external users |
| **Invitation** | Pending onboarding record with scope and expiry |
| **AccessGrant** | Effective entitlements linking user to resources |
| **ShareLink** | Optional: document-level share with unique URL token |

### 4.2 Entity Relationships

```
ExternalUser (1) ←→ (N) AccessGrant
AccessGrant (N) ←→ (1) Matter | Project | Document
Invitation (1) → (1) ExternalUser (after redemption)
Invitation (N) ←→ (1) Matter | Project | Document (scope)
```

### 4.3 AccessGrant Properties

| Property | Description |
|----------|-------------|
| ExternalUserId | Link to external user |
| ResourceType | Matter, Project, or Document |
| ResourceId | Specific resource ID |
| Role | ViewOnly, Contribute, Download |
| GrantedBy | Internal user who created grant |
| GrantedDate | When access was granted |
| ExpiryDate | Optional expiration |
| Status | Active, Expired, Revoked |

### 4.4 Invitation Properties

| Property | Description |
|----------|-------------|
| RecipientEmail | Email address of invitee |
| Scope | Matter/Project/Document IDs being shared |
| Role | Access level being granted |
| InvitedBy | Internal user sending invitation |
| Token | Unique redemption token |
| Status | Pending, Redeemed, Expired, Revoked |
| ExpiryDate | Invitation validity period |
| Message | Optional personalized message |

---

## 5. Invitation and Onboarding Flow

### 5.1 "Grant Access" from Office/Teams (Internal User)

1. Internal user composing email/message selects "Grant access"
2. System identifies recipient email addresses
3. For each external recipient:
    - Create Invitation record with scope and role
    - Generate unique redemption token/URL
4. Email includes:
    - Document link(s) that resolve through Spaarke access check
    - "Register / Sign in to Spaarke" button with redemption URL
    - Access instructions and expiry information

### 5.2 Invitation Redemption (External User)

1. External user clicks redemption link in email
2. Directed to External Portal sign-in/registration
3. If new user:
    - Complete Entra External ID registration
    - Create ExternalUser record in Dataverse
4. If existing user:
    - Sign in with existing credentials
5. System validates invitation token:
    - Check not expired or revoked
    - Create AccessGrant records for invitation scope
    - Mark Invitation as Redeemed
6. Redirect to portal showing newly accessible content

### 5.3 Returning User Experience

1. External user signs in to portal
2. Portal shows all active AccessGrants:
    - Matters they can access
    - Projects they can access
    - Individual documents shared with them
3. User navigates and works within granted scope

---

## 6. Portal UX Design

### 6.1 Portal Pages

| Page | Purpose |
|------|---------|
| **Home** | Dashboard showing accessible Matters, Projects, Recent Documents |
| **Workspaces** | List of Matters/Projects user can access |
| **Workspace Detail** | Documents within a specific Matter/Project |
| **Document Viewer** | View/download document with access logging |
| **Profile** | User profile, notification preferences |
| **Invitation Landing** | Redemption flow entry point |

### 6.2 Home Dashboard

- **My Workspaces**: Cards for each accessible Matter/Project
- **Recent Documents**: Recently viewed/downloaded documents
- **Pending Invitations**: Any unredeemed invitations for this email
- **Quick Actions**: Search, View all documents

### 6.3 Document Viewer

- Inline preview where supported (PDF, images, Office docs via preview service)
- Download button (if role permits)
- Metadata display: document name, type, associated workspace, last modified
- Access log notation (document opened by external user)

### 6.4 Access Denied / No Content

When user has no active grants:
- Clear message explaining they need an invitation
- "Request access" option (sends request to workspace owners)
- Contact information for internal sponsor

---

## 7. Security and Authorization

### 7.1 Authentication

- Microsoft Entra External ID
- Support for social identity providers if configured (Google, Microsoft personal, etc.)
- MFA enforcement based on tenant policy
- Session timeout and re-authentication requirements

### 7.2 Authorization Enforcement

| Layer | Enforcement |
|-------|-------------|
| Power Pages Web Roles | Coarse access to portal areas |
| Table Permissions | Row-level access based on AccessGrant |
| API Endpoints | Validate AccessGrant on every request |
| Document Access | SpeFileStore checks AccessGrant before serving |

### 7.3 Row-Level Security

Power Pages table permissions configured to:
- ExternalUser can only see their own record
- AccessGrant: user sees only grants where ExternalUserId = current user
- Document/Matter/Project: filtered by existence of active AccessGrant

### 7.4 Auditability

Log all external user actions:
- Sign-in events (success and failure)
- Document views and downloads
- Navigation patterns
- Failed access attempts (for security monitoring)

---

## 8. Backend APIs

### 8.1 External Access APIs

| Endpoint | Purpose | Consumer |
|----------|---------|----------|
| POST /external/invitations | Create invitation(s) for recipients | Office/Teams add-ins |
| GET /external/invitations/{token} | Validate invitation token | Portal redemption |
| POST /external/invitations/{token}/redeem | Complete redemption, create grants | Portal redemption |
| POST /external/invitations/{id}/revoke | Revoke pending invitation | Internal admin |

### 8.2 External User APIs (Portal)

| Endpoint | Purpose |
|----------|---------|
| GET /external/my/profile | Get current external user profile |
| PUT /external/my/profile | Update profile |
| GET /external/my/workspaces | List accessible Matters/Projects |
| GET /external/my/documents | List accessible documents |
| GET /external/workspaces/{id}/documents | Documents in a workspace |
| GET /external/documents/{id} | Document metadata |
| GET /external/documents/{id}/content | Download/stream document (with access log) |
| POST /external/access-requests | Request access to a resource |

### 8.3 Internal Management APIs

| Endpoint | Purpose |
|----------|---------|
| GET /admin/external-users | List external users |
| GET /admin/external-users/{id}/grants | List user's access grants |
| DELETE /admin/access-grants/{id} | Revoke access grant |
| GET /admin/workspaces/{id}/external-access | List external access to a workspace |

---

## 9. Dataverse Schema

### 9.1 ExternalUser Table

| Column | Type | Description |
|--------|------|-------------|
| sprk_externaluserid | GUID | Primary key |
| sprk_email | String | Email address (unique) |
| sprk_displayname | String | Display name |
| sprk_entraobjectid | String | Entra External ID object ID |
| sprk_status | OptionSet | Active, Suspended, Deactivated |
| sprk_firstsignin | DateTime | First portal sign-in |
| sprk_lastsignin | DateTime | Most recent sign-in |
| sprk_createdbycontact | Lookup(Contact) | Internal sponsor who first invited |

### 9.2 Invitation Table

| Column | Type | Description |
|--------|------|-------------|
| sprk_invitationid | GUID | Primary key |
| sprk_recipientemail | String | Invitee email |
| sprk_token | String | Unique redemption token |
| sprk_status | OptionSet | Pending, Redeemed, Expired, Revoked |
| sprk_role | OptionSet | ViewOnly, Contribute, Download |
| sprk_scopetype | OptionSet | Matter, Project, Document |
| sprk_scopeid | String | Resource ID(s) - JSON array for multiple |
| sprk_invitedby | Lookup(SystemUser) | Internal user who sent |
| sprk_sentdate | DateTime | When invitation sent |
| sprk_expirydate | DateTime | Invitation expiry |
| sprk_redeemeddate | DateTime | When redeemed |
| sprk_externaluser | Lookup(ExternalUser) | Link after redemption |
| sprk_message | Memo | Optional personal message |

### 9.3 AccessGrant Table

| Column | Type | Description |
|--------|------|-------------|
| sprk_accessgrantid | GUID | Primary key |
| sprk_externaluser | Lookup(ExternalUser) | Grantee |
| sprk_resourcetype | OptionSet | Matter, Project, Document |
| sprk_resourceid | String | Resource ID |
| sprk_role | OptionSet | ViewOnly, Contribute, Download |
| sprk_grantedby | Lookup(SystemUser) | Internal user who granted |
| sprk_granteddate | DateTime | When granted |
| sprk_expirydate | DateTime | Optional expiry |
| sprk_status | OptionSet | Active, Expired, Revoked |
| sprk_invitation | Lookup(Invitation) | Source invitation |

### 9.4 ExternalAccessLog Table

| Column | Type | Description |
|--------|------|-------------|
| sprk_accesslogid | GUID | Primary key |
| sprk_externaluser | Lookup(ExternalUser) | Who |
| sprk_action | OptionSet | View, Download, Search, SignIn, FailedAccess |
| sprk_resourcetype | OptionSet | Document, Matter, Project, Portal |
| sprk_resourceid | String | What resource |
| sprk_timestamp | DateTime | When |
| sprk_ipaddress | String | From where |
| sprk_useragent | String | Client info |
| sprk_details | Memo | Additional context (JSON) |

---

## 10. Power Pages Configuration

### 10.1 Site Structure

| Web Page | Template | Purpose |
|----------|----------|---------|
| Home | home.html | Dashboard |
| Workspaces | workspaces.html | List workspaces |
| Workspace Detail | workspace-detail.html | Documents in workspace |
| Document | document.html | Document viewer |
| Profile | profile.html | User profile |
| Redeem | redeem.html | Invitation redemption |
| Access Denied | access-denied.html | No access message |

### 10.2 Web Roles

| Role | Description |
|------|-------------|
| External User | Base role for all authenticated external users |
| (Future) Premium External | Enhanced features for premium collaboration |

### 10.3 Table Permissions

| Table | Permission | Scope |
|-------|------------|-------|
| ExternalUser | Read | Where entraobjectid = current user |
| AccessGrant | Read | Where externaluser = current user AND status = Active |
| Invitation | Read | Where recipientemail = current user email |
| Document | Read | Via AccessGrant parental relationship |
| Matter | Read | Via AccessGrant parental relationship |
| Project | Read | Via AccessGrant parental relationship |

---

## 11. Non-Functional Requirements

### 11.1 Performance

- Portal pages load within 3 seconds
- Document preview generation within 5 seconds
- Search results within 2 seconds

### 11.2 Scalability

- Support 10,000+ external users
- Support 100+ concurrent portal sessions
- Efficient table permission evaluation

### 11.3 Security

- All traffic over HTTPS
- No sensitive data in URLs
- Session tokens rotated appropriately
- Failed login lockout policies

### 11.4 Compliance

- GDPR considerations for external user data
- Data retention policies for access logs
- Right to deletion support for external users

### 11.5 Observability

- Portal analytics (page views, user sessions)
- API metrics (latency, error rates)
- Security event monitoring (failed logins, access denials)

---

## 12. Required Deliverables

Claude Code must produce for this project:

1. **Detailed Portal UX Spec**
    - Page-by-page design with layouts
    - User flows for invitation redemption
    - Access denied and error handling
    - Responsive design considerations

2. **Dataverse Schema Spec**
    - Complete table definitions with all columns
    - Relationships and indexes
    - Security role configuration
    - Table permissions for Power Pages

3. **API Contract Spec**
    - All endpoint definitions with DTOs
    - Authentication requirements
    - Error responses
    - Rate limiting considerations

4. **Power Pages Configuration Spec**
    - Site settings
    - Web roles and permissions
    - Liquid template requirements
    - Custom JavaScript needs

5. **Identity Configuration Spec**
    - Entra External ID setup
    - Identity provider configuration
    - Claims mapping
    - Session policies

6. **Test Plan**
    - Invitation flows (happy path and edge cases)
    - Access enforcement scenarios
    - Multi-tenant considerations
    - Performance testing approach

---

## 13. Reference Pointers

Power Pages:
- https://learn.microsoft.com/en-us/power-pages/
- https://learn.microsoft.com/en-us/power-pages/security/table-permissions

Entra External ID:
- https://learn.microsoft.com/en-us/entra/external-id/
- https://learn.microsoft.com/en-us/power-pages/security/authentication/azure-ad-b2c-provider

---

## 14. Integration Points

### Provides To Other Projects

| Artifact | Consumer | Usage |
|----------|----------|-------|
| POST /external/invitations API | Office add-ins, Teams | Create invitations from "Grant access" |
| ExternalUser entity | Office add-ins | Lookup existing external users |
| AccessGrant entity | All projects | Check external access |
| Invitation entity | Email templates | Include redemption links |

### Depends On

| Artifact | Provider | Usage |
|----------|----------|-------|
| Document entity | SDAP-office-integration | Core document model |
| Matter/Project entities | SDAP-office-integration | Workspace context |
| SpeFileStore | SDAP-office-integration | Document content retrieval |
| UAC module | SDAP-office-integration | Authorization checks |

---

**EOF**
