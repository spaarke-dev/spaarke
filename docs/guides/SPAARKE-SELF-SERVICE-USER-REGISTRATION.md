# Spaarke Self-Service User Registration — Operations Guide

> **Last Updated**: 2026-04-06
> **Project Branch**: `work/spaarke-self-service-registration-app`
> **Status**: Phase 1 — Demo Access via Internal Accounts

---

## Overview

The Spaarke Self-Service Registration system automates demo user provisioning. Prospective users submit a public "Request Early Access" form on `spaarke.com/demo`, an admin reviews and one-click approves via an MDA ribbon button, and the BFF API synchronously provisions an internal Entra ID account (`user@demo.spaarke.com`) with licenses, Dataverse role, SPE container access, and a branded welcome email with credentials. A daily BackgroundService automatically disables expired accounts and sends pre-expiration warnings.

### Architecture

```
                                    ┌─────────────────────────────────────┐
                                    │           BFF API (.NET 8)          │
                                    │  spe-api-dev-67e2xz.azurewebsites  │
┌──────────────┐   POST             │                                     │
│  Website     │   /demo-request    │  RegistrationEndpoints              │
│  spaarke.com ├───(public)────────►│       │                             │
│  /demo       │                    │       ▼                             │
└──────────────┘                    │  DemoProvisioningService            │
                                    │       │                             │
┌──────────────┐   POST             │       ├──► Entra ID (Graph API)    │
│  MDA Ribbon  │   /requests/{id}   │       │    - Create user           │
│  Approve     ├───/approve────────►│       │    - Add to security group │
│  Button      │   (admin)          │       │    - Assign 3 licenses     │
└──────────────┘                    │       │                             │
                                    │       ├──► Dataverse (S2S)         │
                                    │       │    - Create systemuser     │
                                    │       │    - Add to Demo Team      │
                                    │       │                             │
                                    │       ├──► SPE (Graph API)         │
                                    │       │    - Grant Writer access    │
                                    │       │                             │
                                    │       └──► Email Service           │
                                    │            - Welcome email         │
                                    │            - Admin notification    │
                                    │                                     │
                                    │  DemoExpirationService (daily)      │
                                    │       - Disable expired accounts   │
                                    │       - Send warning/expired email │
                                    └─────────────────────────────────────┘
```

---

## How It Works — End-to-End Flow

1. **User visits** `spaarke.com/demo` and fills out the public "Request Early Access" form (name, email, organization, use case, consent).

2. **Website submits** to BFF API `POST /api/registration/demo-request` (unauthenticated). The API validates input, checks for duplicate emails, blocks disposable email domains, and creates a `sprk_registrationrequest` record in the Demo Dataverse environment with status `Submitted`.

3. **Tracking ID** is generated in format `REG-{YYYYMMDD}-{4char}` (e.g., `REG-20260403-XMG9`) and returned to the user as confirmation.

4. **Admin notification email** is sent (fire-and-forget) to configured admin addresses with request details and a link to the MDA record.

5. **Admin opens** the Spaarke Demo MDA, navigates to "Pending Demo Requests" view, selects a request.

5b. **Admin selects Target Environment** via the `sprk_dataverseenvironmentid` lookup on the registration request form. This lookup points to a `sprk_dataverseenvironment` record containing all environment-specific config (Dataverse URL, business unit, team, SPE container, license SKUs, admin emails). The lookup must be set before approval — there is no default.

6. **Admin clicks "Approve Demo Access"** ribbon button. The JS webresource validates the Target Environment lookup is set (alerts if empty), then calls `POST /api/registration/requests/{id}/approve` with an admin bearer token. No environment name is sent in the request body — the API reads the environment from the Dataverse lookup.

7. **BFF API runs the provisioning pipeline** synchronously (~10-30 seconds):
   - Step 1: Generate unique username (`firstname.lastname@demo.spaarke.com`, collision handling appends number)
   - Step 2: Generate temporary password (16+ chars, mixed case, numbers, symbols)
   - Step 3: Create Entra ID user with `forceChangePasswordNextSignIn: true`
   - Step 4: Add user to "Spaarke Demo Users" security group (MFA exclusion)
   - Step 5: Assign 3 licenses (Power Apps Plan 2 Trial, Fabric Free, Power Automate Free)
   - Step 6: Create systemuser in Demo Dataverse Business Unit
   - Step 7: Add systemuser to Demo Team (inherits `Spaarke Demo User` security role)
   - Step 8: Grant SPE container Writer access (non-fatal if fails)
   - Step 9: Send welcome email to applicant's work email

8. **Record is updated** to status `Provisioned` with demo username, Entra object ID, provisioned date, and expiration date (default: 14 days).

9. **User receives welcome email** with: username, temporary password, access URL, "use InPrivate/Incognito" tip, quick start guide link, expiration date, support contact.

10. **User logs in** to `https://spaarke-demo.crm.dynamics.com/`, changes password on first login, explores Spaarke with sample data.

11. **Day 11** (3 days before expiry): Pre-expiration warning email sent with extension contact info and production access CTA.

12. **Day 14** (expiration day): DemoExpirationService disables the Entra account, removes from Demo Team, revokes SPE container access, sends expired notification email, updates status to `Expired`.

### What the Admin Sees in MDA

- **Pending Demo Requests** view: Submitted requests sorted oldest-first. Columns: Name, Email, Organization, Use Case, Request Date.
- **All Demo Requests** view: All statuses, full audit trail.
- **Active Demo Users** view: Status = Provisioned, shows demo username + expiration date.
- **Expired Demo Users** view: Status = Expired.

---

## Prerequisites

Before the registration system can operate, the following must be in place:

| Prerequisite | Description |
|-------------|-------------|
| **Demo Dataverse environment** | `spaarke-demo.crm.dynamics.com` provisioned with Spaarke solution deployed |
| **Business Unit + Owner Team** | "Spaarke Demo" BU and "Spaarke Demo Team" with `Spaarke Demo User` security role |
| **SPE container** | Pre-existing container with sample documents loaded |
| **Entra ID security group** | "Spaarke Demo Users" group for Conditional Access MFA exclusion (created by `Setup-EntraInfrastructure.ps1`) |
| **Conditional Access policy** | Excludes "Spaarke Demo Users" group from MFA requirements |
| **Graph API Application permissions** | `User.ReadWrite.All`, `GroupMember.ReadWrite.All`, `Directory.ReadWrite.All` on BFF app registration, with admin consent granted |
| **Exchange Application Access Policy** | Grants the BFF app registration permission to send email from `demo@demo.spaarke.com` shared mailbox |
| **BFF API deployed** | With `DemoProvisioning` configuration section populated |
| **`demo.spaarke.com` domain** | Configured in Entra ID as a verified domain |

---

## Configuration Reference

### Environment Configuration — Dataverse Entity

**As of v2.0**, per-environment config is stored in `sprk_dataverseenvironment` records in Dataverse, not in appsettings. Admins manage environments through the MDA form (Settings → Dataverse Environments).

#### `sprk_dataverseenvironment` Entity

| Column | Type | Required | Description |
|--------|------|----------|-------------|
| `sprk_name` | Text (200) | Yes | Display name (e.g., "Dev", "Demo 1") |
| `sprk_environmenttype` | Choice | Yes | Development, Demo, Sandbox, Trial, Partner, Training, Production |
| `sprk_dataverseurl` | URL | Yes | e.g., `https://spaarke-demo.crm.dynamics.com` |
| `sprk_appid` | Text (100) | No | MDA app GUID for deep links |
| `sprk_description` | Multiline (2000) | No | Admin notes |
| `sprk_isactive` | Boolean | Yes | Default: Yes |
| `sprk_isdefault` | Boolean | Yes | Default: No (informational, not enforced) |
| `sprk_setupstatus` | Choice | No | Not Started, In Progress, Ready, Issue |
| `sprk_accountdomain` | Text (200) | No | UPN domain (e.g., `demo.spaarke.com`) |
| `sprk_businessunitname` | Text (200) | No | Target Dataverse business unit |
| `sprk_teamname` | Text (200) | No | Team with inherited security role |
| `sprk_specontainerid` | Text (500) | No | SharePoint Embedded container ID |
| `sprk_securitygroupid` | Text (100) | No | Entra ID security group for demo users |
| `sprk_defaultdurationdays` | Integer | No | Default demo duration (days) |
| `sprk_licenseconfigjson` | Multiline (4000) | No | JSON: `{"PowerAppsPlan2TrialSkuId":"...","FabricFreeSkuId":"...","PowerAutomateFreeSkuId":"..."}` |
| `sprk_adminemails` | Multiline (1000) | No | Comma-separated admin email addresses |

#### Registration Request Lookup

The `sprk_registrationrequest` entity has a lookup field `sprk_dataverseenvironmentid` → `sprk_dataverseenvironment`. Admin must select the target environment before approving. The field defaults to blank.

### appsettings.json — DemoProvisioning Section (Tenant-Level)

Per-environment config has moved to Dataverse. The `DemoProvisioning` section now contains only **tenant-level** settings:

```json
{
  "DemoProvisioning": {
    "AccountDomain": "demo.spaarke.com",
    "DemoUsersGroupId": "745bfdf6-f899-4507-935d-c52de3621536",
    "Licenses": {
      "PowerAppsPlan2TrialSkuId": "dcb1a3ae-b33f-4487-846a-a640262fadf4",
      "FabricFreeSkuId": "a403ebcc-fae0-4ca2-8c8c-7a907fd6c235",
      "PowerAutomateFreeSkuId": "f30db892-07e9-47e9-837c-80727f46fd3d"
    },
    "AdminNotificationEmails": [
      "admin@spaarke.com"
    ]
  }
}
```

> **Note**: `Environments` array and `DefaultEnvironment` are deprecated. They are retained temporarily for backward compatibility with `DemoExpirationService` but will be removed in a future update.

### Configuration Field Reference

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `AccountDomain` | String | Yes | UPN domain for demo accounts (e.g., `demo.spaarke.com`) |
| `DemoUsersGroupId` | String | Yes | Entra ID security group GUID for MFA exclusion |
| `Licenses.PowerAppsPlan2TrialSkuId` | String | Yes | SKU ID for Power Apps Plan 2 Trial license |
| `Licenses.FabricFreeSkuId` | String | Yes | SKU ID for Microsoft Fabric (Free) license |
| `Licenses.PowerAutomateFreeSkuId` | String | Yes | SKU ID for Power Automate (Free) license |
| `AdminNotificationEmails` | String[] | Yes (min 1) | Admin email addresses for new request notifications |
| `DATAVERSE_URL` | String | Yes | Admin Dataverse URL (e.g., `https://spaarkedev1.crm.dynamics.com`) |

### App Service Configuration

Set via Azure CLI or portal:

```bash
az webapp config appsettings set \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --settings \
    DATAVERSE_URL="https://spaarkedev1.crm.dynamics.com" \
    DemoProvisioning__AccountDomain="demo.spaarke.com" \
    DemoProvisioning__DemoUsersGroupId="745bfdf6-f899-4507-935d-c52de3621536" \
    DemoProvisioning__Licenses__PowerAppsPlan2TrialSkuId="dcb1a3ae-b33f-4487-846a-a640262fadf4" \
    DemoProvisioning__Licenses__FabricFreeSkuId="a403ebcc-fae0-4ca2-8c8c-7a907fd6c235" \
    DemoProvisioning__Licenses__PowerAutomateFreeSkuId="f30db892-07e9-47e9-837c-80727f46fd3d" \
    DemoProvisioning__AdminNotificationEmails__0="admin@spaarke.com"
```

---

## API Endpoints

### POST /api/registration/demo-request (Public — Unauthenticated)

Submit a new demo access request. No authentication required. Protected by reCAPTCHA, rate limiting (`anonymous` policy), and disposable email domain blocking.

**Request:**

```json
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

**Response (202 Accepted):**

```json
{
  "trackingId": "REG-20260403-A1B2",
  "message": "Your demo request has been submitted successfully. You will receive an email when your access is ready."
}
```

**Error Responses:**

| Status | Condition |
|--------|-----------|
| 400 | Missing required fields (firstName, email, consent), disposable email domain |
| 409 | Duplicate — active/pending request already exists for this email |
| 429 | Rate limit exceeded |
| 500 | Dataverse create failure |

All errors return RFC 7807 `ProblemDetails` with `correlationId` extension (ADR-019).

---

### POST /api/registration/requests/{id}/approve (Admin — Authenticated)

Approve a pending registration request and trigger the full provisioning pipeline. Requires admin bearer token validated by `RegistrationAuthorizationFilter`.

**Request:** No body required. `{id}` is the GUID of the `sprk_registrationrequest` record.

**Response (200 OK):**

```json
{
  "status": "Provisioned",
  "username": "jane.smith@demo.spaarke.com",
  "expirationDate": "2026-04-17T00:00:00+00:00"
}
```

**Error Responses:**

| Status | Condition |
|--------|-----------|
| 400 | Request status is not `Submitted` (already approved, rejected, etc.) |
| 401 | Missing or invalid bearer token |
| 403 | Caller does not have required admin role |
| 404 | Request ID not found |
| 500 | Provisioning pipeline failure (includes `completedSteps`, `failedAfterStep`, `entraUserId`, `upn` in ProblemDetails extensions) |

---

### POST /api/registration/requests/{id}/reject (Admin — Authenticated)

Reject a pending registration request. Requires admin bearer token.

**Request:**

```json
{
  "reason": "Not a qualified prospect"
}
```

**Response (200 OK):**

```json
{
  "status": "Rejected",
  "requestId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**Error Responses:**

| Status | Condition |
|--------|-----------|
| 400 | Missing rejection reason, or request status is not `Submitted` |
| 401 | Missing or invalid bearer token |
| 403 | Caller does not have required admin role |
| 404 | Request ID not found |
| 500 | Dataverse update failure |

---

## Dataverse Schema

### sprk_registrationrequest Table

**Entity**: `sprk_registrationrequest` | **Ownership**: Organization Owned | **Publisher Prefix**: `sprk_`

#### Applicant Information Columns

| Column | Type | Max Length | Required | Description |
|--------|------|-----------|----------|-------------|
| `sprk_name` | String | 200 | Yes | Primary name: "{FirstName} {LastName} - {Organization}" |
| `sprk_firstname` | String | 100 | Yes | Applicant's first name |
| `sprk_lastname` | String | 100 | Yes | Applicant's last name |
| `sprk_email` | String | 200 | Yes | Applicant's work email |
| `sprk_organization` | String | 200 | Yes | Applicant's organization |
| `sprk_jobtitle` | String | 200 | No | Applicant's job title |
| `sprk_phone` | String | 50 | No | Applicant's phone number |

#### Request Details Columns

| Column | Type | Values/Length | Required | Description |
|--------|------|-------------|----------|-------------|
| `sprk_usecase` | Choice | 0=Document Management, 1=AI Analysis, 2=Financial Intelligence, 3=General | Yes | Primary interest area |
| `sprk_referralsource` | Choice | 0=Conference, 1=Website, 2=Referral, 3=Search, 4=Other | No | How applicant heard about Spaarke |
| `sprk_notes` | Memo | 10000 | No | Additional notes from applicant |
| `sprk_consentaccepted` | Boolean | Yes/No | No | Whether consent checkbox was accepted |
| `sprk_consentdate` | DateTime | — | No | Timestamp of consent acceptance |

#### Lifecycle / Status Columns

| Column | Type | Values/Length | Required | Description |
|--------|------|-------------|----------|-------------|
| `sprk_status` | Choice | 0=Submitted, 1=Approved, 2=Rejected, 3=Provisioned, 4=Expired, 5=Revoked | Yes | Current lifecycle status |
| `sprk_trackingid` | String | 50 | No | Public reference: REG-{YYYYMMDD}-{4char} |
| `sprk_requestdate` | DateTime | — | No | When request was submitted |

#### Review Columns

| Column | Type | Values/Length | Required | Description |
|--------|------|-------------|----------|-------------|
| `sprk_reviewedby` | Lookup (SystemUser) | — | No | Admin who approved/rejected |
| `sprk_reviewdate` | DateTime | — | No | When approved/rejected |
| `sprk_rejectionreason` | String | 500 | No | Reason if rejected |

#### Provisioning Columns

| Column | Type | Values/Length | Required | Description |
|--------|------|-------------|----------|-------------|
| `sprk_demousername` | String | 200 | No | Provisioned UPN (e.g., `jane.smith@demo.spaarke.com`) |
| `sprk_demouserobjectid` | String | 50 | No | Entra ID object ID of provisioned user |
| `sprk_provisioneddate` | DateTime | — | No | When account was provisioned |
| `sprk_expirationdate` | DateTime | — | No | When demo access expires (default: 14 days; admin can adjust) |

### Status Lifecycle

```
Submitted (0) ─── Approve ──► Approved (1) ──► Provisioned (3) ──► Expired (4)
      │                                              │
      └──── Reject ───► Rejected (2)                 └──► Revoked (5)
```

- **Approved** is a transient status set at the start of provisioning. On success it immediately becomes **Provisioned**. If provisioning fails partway, it stays at Approved to indicate retry is needed.
- **Expired** is set by the DemoExpirationService when `sprk_expirationdate <= today`.
- **Revoked** is set manually by an admin who wants to end access before the expiration date.

### Views

| View | Filter | Default | Sort |
|------|--------|---------|------|
| Pending Demo Requests | `sprk_status = 0` (Submitted) | Yes | Request Date ASC |
| All Demo Requests | (none) | No | Request Date DESC |
| Active Demo Users | `sprk_status = 3` (Provisioned) | No | Expiration Date ASC |
| Expired Demo Users | `sprk_status = 4` (Expired) | No | Expiration Date DESC |

---

## Admin Operations

### How to Approve a Request

1. Open the **Spaarke Demo MDA** (`https://spaarke-demo.crm.dynamics.com/`)
2. Navigate to **Registration Requests** in the sitemap
3. Open the **"Pending Demo Requests"** view
4. Select a request and click the **"Approve Demo Access"** ribbon button (or open the record and use the form command bar button)
5. Confirm the approval in the dialog
6. Wait ~10-30 seconds for the provisioning pipeline to complete
7. The record status updates to **Provisioned** with the demo username and expiration date

**Bulk approve**: Select multiple requests in the list view and click Approve. Requests are processed sequentially.

### How to Reject a Request

1. Select a request in the "Pending Demo Requests" view
2. Click the **"Reject Request"** ribbon button
3. Enter a rejection reason in the prompt dialog
4. The record status updates to **Rejected** with the reason stored

### How to Extend a Demo

1. Open the provisioned `sprk_registrationrequest` record in MDA
2. Edit the **Expiration Date** (`sprk_expirationdate`) field to the desired new date
3. Save the record
4. The DemoExpirationService respects the updated date — no further action needed

### How to Revoke Access Manually

To end access before the expiration date:

1. **Disable the Entra account**: In Azure Portal or via Graph API: `PATCH /users/{objectId}` with `{"accountEnabled": false}`
2. **Remove from Demo Team**: In MDA, remove the systemuser from the "Spaarke Demo Team"
3. **Update the record**: Set `sprk_status` to `Revoked` (value 5) on the `sprk_registrationrequest` record

### How to Reset a Demo User Password

1. Open Azure Portal > Entra ID > Users
2. Find the demo user (e.g., `jane.smith@demo.spaarke.com`)
3. Click **Reset password** and set `forceChangePasswordNextSignIn: true`
4. Communicate the new temporary password to the user

### How to Clean Up Expired Users

Expired accounts are disabled (not deleted) to allow reactivation. For permanent cleanup of accounts expired more than 90 days:

1. Query Dataverse for records where `sprk_status = 4` (Expired) and `sprk_expirationdate < today - 90 days`
2. For each record: delete the Entra ID user via Graph API `DELETE /users/{objectId}`
3. Optionally: remove the `sprk_registrationrequest` record or mark it for archival

---

## Adding a New Demo Environment

To add a second (or subsequent) demo environment, no code changes are required:

1. **Add an entry** to the `DemoProvisioning.Environments` array in appsettings (or App Service settings):

```json
{
  "Name": "Demo 2",
  "DataverseUrl": "https://spaarke-demo2.crm.dynamics.com",
  "BusinessUnitName": "Spaarke Demo",
  "TeamName": "Spaarke Demo Team",
  "SpeContainerId": "{new-container-guid}",
  "DefaultDemoDurationDays": 14
}
```

2. **Run the Dataverse schema script** against the new environment:

```powershell
.\scripts\Create-RegistrationRequestSchema.ps1 -EnvironmentDomain "spaarke-demo2.crm.dynamics.com"
```

3. **Deploy the ribbon solution** (DemoRegistration) to the new environment via solution import.

4. **Upload the JS webresource** (`sprk_registrationribbon.js`) to the new environment.

5. **Ensure prerequisites** exist in the new environment: Business Unit, Team, security role, SPE container, sample data.

6. **Update `DefaultEnvironment`** if the new environment should be the default provisioning target.

---

## Email Templates

The system sends 4 email types via the existing Spaarke Communication Service (`POST /api/communications/send`, SharedMailbox mode from `demo@demo.spaarke.com`).

| Template | File | Trigger | Subject |
|----------|------|---------|---------|
| Admin Notification | `AdminNotificationTemplate.html` | New request submitted | "New Demo Request: {name} ({org})" |
| Welcome | `WelcomeTemplate.html` | Account provisioned | "Your Spaarke Demo Access is Ready!" |
| Expiration Warning | `ExpirationWarningTemplate.html` | 3 days before expiry | "Your Spaarke Demo Expires in 3 Days" |
| Expired | `ExpiredTemplate.html` | Day of expiry | "Your Spaarke Demo Access Has Ended" |

### How to Modify Templates

Templates are **embedded resources** in the BFF API assembly:

- **Location**: `src/server/api/Sprk.Bff.Api/Services/Registration/EmailTemplates/`
- **Format**: HTML with `{{placeholder}}` tokens
- **Build action**: EmbeddedResource (set in .csproj)

To modify a template:
1. Edit the HTML file in `Services/Registration/EmailTemplates/`
2. Use `{{PlaceholderName}}` for dynamic values
3. Rebuild and redeploy the BFF API
4. Available placeholders are defined in `RegistrationEmailService.cs` for each method

### Email Sender Configuration

- **From address**: `demo@demo.spaarke.com` (hardcoded in `RegistrationEmailService`)
- **Send mode**: `SharedMailbox` via CommunicationService
- **Requirement**: The BFF API app registration must have an Exchange Application Access Policy granting send-as permission for the shared mailbox

---

## Expiration Service

### DemoExpirationService

- **Type**: .NET 8 `BackgroundService` (ADR-001 pattern, same structure as `DailySendCountResetService`)
- **Schedule**: Runs daily at midnight UTC
- **Location**: `src/server/api/Sprk.Bff.Api/Services/Registration/DemoExpirationService.cs`

### Daily Processing Logic

1. Query Dataverse for all records where `sprk_status = Provisioned`
2. For each record:
   - If `sprk_expirationdate <= now`: **Expire** the record:
     - Disable Entra ID account (`PATCH /users/{id}` with `accountEnabled: false`)
     - Remove from Demo Team in Dataverse
     - Remove from "Spaarke Demo Users" Entra security group
     - Revoke SPE container permissions
     - Send expired notification email
     - Update `sprk_status` to `Expired`
   - If `sprk_expirationdate <= now + 3 days` (and not yet expired): **Warn**:
     - Send expiration warning email with extension contact and production access CTA
3. Each record is processed independently — one failure does not block others (each sub-operation has its own try/catch)

### Error Handling

- If a daily run fails entirely, the service retries after 1 minute
- If an individual record fails, it logs the error and continues to the next record
- Cancellation (app shutdown) is handled gracefully via `stoppingToken`

### Known Issue

The DemoExpirationService is currently **commented out in `RegistrationModule.cs`** due to a DI resolution issue. The `GraphUserService` singleton fails to resolve when the hosted service starts. This is likely a constructor dependency chain timing issue (`GraphClientFactory` -> `GraphTokenCache`). The service code is complete and tested — it just needs the DI chain debugged before enabling.

---

## Troubleshooting

### "Insufficient privileges" from Graph API

**Cause**: The BFF API app registration is missing required Application permissions or admin consent has not been granted.

**Fix**: Verify the following Application permissions exist with admin consent on the BFF app registration:
- `User.ReadWrite.All`
- `GroupMember.ReadWrite.All`
- `Directory.ReadWrite.All`

Run `Setup-EntraInfrastructure.ps1` to add permissions and grant consent, or check in Azure Portal > App registrations > API permissions.

### "INVALID_SENDER" or email send failures

**Cause**: The Exchange Application Access Policy does not grant the BFF app registration send-as permission for the `demo@demo.spaarke.com` shared mailbox.

**Fix**: Create or update the Exchange Application Access Policy:
```powershell
New-ApplicationAccessPolicy -AppId "{bff-app-id}" -PolicyScopeGroupId "{mail-enabled-security-group}" -AccessRight RestrictAccess
```

### "userPrincipalName is required" on SPE permission grant

**Cause**: The SPE container permission API requires `userPrincipalName` set via `AdditionalData` on the `SharePointIdentity` object, not via a direct property.

**Fix**: Verify that `GrantSpeContainerAccessAsync` uses the `AdditionalData` pattern:
```csharp
User = new SharePointIdentity
{
    AdditionalData = new Dictionary<string, object>
    {
        ["userPrincipalName"] = upn
    }
}
```

### App crashes on startup

**Cause**: DI registration chain issue — typically a missing or misconfigured dependency in `RegistrationModule.cs`.

**Fix**:
1. Check that all required services are registered: `GraphUserService`, `RegistrationDataverseService`, `RegistrationEmailService`, `DemoProvisioningService`, `PasswordGenerator`, `EmailDomainValidator`, `TrackingIdGenerator`
2. Verify `DemoProvisioning` configuration section is present and valid in appsettings or App Service config
3. If `DemoExpirationService` is causing the crash, it can be temporarily disabled in `RegistrationModule.cs`

### Duplicate request error (409 Conflict)

**Cause**: A previous request with the same email address already exists with status `Submitted`, `Approved`, or `Provisioned`.

**Fix**: Check the existing request in MDA. Options:
- If the previous request was abandoned: Reject it, then the user can resubmit
- If the user needs a new account: Expire or revoke the previous request first

### User can't see documents after provisioning

**Cause**: SPE container permission was not granted (Step 8 is non-fatal and may have been skipped).

**Fix**:
1. Check provisioning logs for "SPE container access grant failed (non-fatal)"
2. Manually grant Writer access via Graph API:
```
POST /storage/fileStorage/containers/{containerId}/permissions
{
  "roles": ["writer"],
  "grantedToV2": {
    "user": {
      "userPrincipalName": "jane.smith@demo.spaarke.com"
    }
  }
}
```

### Username collision

**Cause**: Another user with the same first and last name was previously provisioned.

**How it's handled**: The system automatically checks Entra ID for existing UPNs and appends a number: `jane.smith@demo.spaarke.com` -> `jane.smith2@demo.spaarke.com`. No manual intervention needed.

---

## Key IDs and Resources

### Dev Environment

| Resource | Value |
|----------|-------|
| Dataverse URL | `https://spaarkedev1.crm.dynamics.com` |
| BFF API URL | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| BFF App Registration ID | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| Dataverse App Registration ID | `170c98e1-d486-4355-bcbe-170454e0207c` |
| Entra Tenant ID | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| Demo BU ID (in dev) | `9271b764-952f-f111-88b5-7c1e520aa4df` |
| Demo Team ID (in dev) | `9471b764-952f-f111-88b5-7c1e520aa4df` |

### Demo Environment

| Resource | Value |
|----------|-------|
| Dataverse URL | `https://spaarke-demo.crm.dynamics.com` |
| Demo BFF App Registration ID | `da03fe1a-4b1d-4297-a4ce-4b83cae498a9` |

### Shared Resources

| Resource | Value |
|----------|-------|
| Demo Users Security Group ID | `745bfdf6-f899-4507-935d-c52de3621536` |
| Power Apps Plan 2 Trial SKU ID | `dcb1a3ae-b33f-4487-846a-a640262fadf4` |
| Fabric Free SKU ID | `a403ebcc-fae0-4ca2-8c8c-7a907fd6c235` |
| Power Automate Free SKU ID | `f30db892-07e9-47e9-837c-80727f46fd3d` |

---

## Scripts Reference

All scripts are in the `scripts/` directory at the repository root.

### Create-RegistrationRequestSchema.ps1

Creates the `sprk_registrationrequest` entity and all 22 columns in a Dataverse environment via Web API. Idempotent — skips if entity already exists.

```powershell
.\scripts\Create-RegistrationRequestSchema.ps1 -EnvironmentDomain "spaarkedev1.crm.dynamics.com"
```

**Prerequisites**: Azure CLI authenticated with access to the target Dataverse environment.

**Steps performed**: Create entity, 10 string columns, 1 memo column, 3 choice columns (use case, referral source, status), 5 datetime columns, 1 boolean column, 1 lookup relationship (reviewedby -> systemuser), publish entity.

### Setup-EntraInfrastructure.ps1

One-time Entra ID infrastructure setup. All operations are idempotent.

```powershell
# Preview (dry run)
.\scripts\Setup-EntraInfrastructure.ps1 -DryRun

# Execute
.\scripts\Setup-EntraInfrastructure.ps1
```

**Steps performed**:
1. Create "Spaarke Demo Users" security group
2. Create Conditional Access policy (report-only mode) excluding the demo group from MFA
3. Add Graph API Application permissions (`User.ReadWrite.All`, `GroupMember.ReadWrite.All`, `Directory.ReadWrite.All`) to BFF app registration
4. Grant admin consent for the added permissions

**Prerequisites**: Microsoft.Graph PowerShell SDK, Entra ID Global Administrator or Privileged Role Administrator.

**Important**: The Conditional Access policy is created in **report-only mode**. Review in Azure Portal and change to **enabled** when ready.

### Get-LicenseSkuIds.ps1

Discovers and outputs tenant SKU IDs for the 3 required demo licenses.

```powershell
.\scripts\Get-LicenseSkuIds.ps1
```

**Output**: Lists Power Apps Plan 2 Trial, Fabric Free, and Power Automate Free SKU IDs with available unit counts. Copy these IDs into the `DemoProvisioning.Licenses` configuration.

**Prerequisites**: Microsoft.Graph PowerShell SDK, `Organization.Read.All` scope.

---

## Security Considerations

| Area | Detail |
|------|--------|
| **Submit endpoint** | Unauthenticated (public form). Protected by: reCAPTCHA validation, rate limiting (`anonymous` policy), disposable email domain blocking, duplicate email check, input validation and sanitization. |
| **Approve/Reject endpoints** | Require authenticated admin bearer token. `RegistrationAuthorizationFilter` validates admin role before processing. |
| **Demo accounts** | No MFA required — Conditional Access policy excludes the "Spaarke Demo Users" security group. This is acceptable because demo accounts are time-limited (14 days default) and access only non-confidential sample data. |
| **Temporary passwords** | Auto-generated (16+ chars, mixed case, numbers, symbols). `forceChangePasswordNextSignIn: true` ensures the user sets their own password on first login. The temp password is sent to the applicant's verified work email, not the demo account. |
| **Demo data** | Shared across all demo users. Contains only synthetic/sample data. Demo BU has scoped security role with no cross-BU access and no delete permissions. |
| **Graph API permissions** | `User.ReadWrite.All`, `GroupMember.ReadWrite.All`, `Directory.ReadWrite.All` are powerful permissions. Scoped to the BFF app registration with explicit admin consent. Justified by the need to create/disable users, manage group membership, and assign licenses programmatically. |
| **Account lifecycle** | Expired accounts are disabled (not deleted) to allow reactivation. Manual cleanup process recommended for accounts expired > 90 days. |
| **PII protection** | Registration data stored in Dataverse with at-rest encryption. Access restricted to admin role via MDA security. |

---

*Source code: `src/server/api/Sprk.Bff.Api/Services/Registration/` and `src/server/api/Sprk.Bff.Api/Endpoints/RegistrationEndpoints.cs`*
*Configuration model: `src/server/api/Sprk.Bff.Api/Configuration/DemoProvisioningOptions.cs`*
*Dataverse schema: `src/solutions/DemoRegistration/schema-definition.md`*
