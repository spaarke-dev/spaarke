# EXTERNAL ACCESS — ADMIN & OPERATIONS SETUP GUIDE

> **Audience**: Power Platform admins and DevOps engineers configuring the external access environment
> **Last Updated**: 2026-03-19
> **Applies To**: Power Pages portal, Dataverse, Entra ID, Azure App Service
> **Architecture Reference**: [`docs/architecture/external-access-spa-architecture.md`](../architecture/external-access-spa-architecture.md)

---

## Overview

This guide covers the one-time and recurring configuration required to run the External Access SPA in Power Pages. It includes:

- **Power Pages setup**: table permissions, web roles, site settings
- **Entra B2B configuration**: app registrations, CORS, CSP
- **BFF API settings**: JWT validation, CORS, connection strings
- **Invitation flow**: how external users are invited and onboarded
- **Monitoring and troubleshooting**

For SPA development, see [EXTERNAL-ACCESS-SPA-GUIDE.md](EXTERNAL-ACCESS-SPA-GUIDE.md).

---

## Environment Reference

| Item | Value |
|------|-------|
| Power Pages site URL | `https://sprk-external-workspace.powerappsportals.com` |
| Dataverse org | `https://spaarkedev1.crm.dynamics.com` |
| BFF API (dev) | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| Entra tenant | `a221a95e-6abc-4434-aecc-e48338a1b2f2` (main workforce tenant) |
| SPA app registration | `spaarke-external-access-SPA` — `f306885a-8251-492c-8d3e-34d7b476ffd0` |
| BFF app registration | `SDAP-BFF-SPE-API` — `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| Web resource name | `sprk_externalworkspace` |

---

## Section 1: Entra ID App Registrations

### 1.1 SPA App Registration (`spaarke-external-access-SPA`)

**Platform configuration** (must be SPA platform, not Web):

1. Azure Portal → Entra ID → App registrations → `spaarke-external-access-SPA`
2. Authentication → Add a platform → Single-page application
3. Redirect URIs:
   - `https://sprk-external-workspace.powerappsportals.com`
   - `http://localhost:3000` (local dev)
4. Implicit grant settings: **uncheck both** (ID tokens and access tokens) — code flow only

**API permissions** (delegated):
- `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/SDAP.Access` — grant admin consent

### 1.2 BFF API App Registration (`SDAP-BFF-SPE-API`)

1. Azure Portal → Entra ID → App registrations → `SDAP-BFF-SPE-API`
2. Expose an API:
   - Application ID URI: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c`
   - Scope name: `SDAP.Access`
   - Authorized client applications: `f306885a-8251-492c-8d3e-34d7b476ffd0` (the SPA)
3. Token configuration → Add optional claims → Access: `preferred_username` — **required** for BFF Contact lookup

### 1.3 B2B Guest Invite Settings

External users are Azure AD B2B guests. Ensure the tenant allows B2B invitation:

1. Azure Portal → Entra ID → External Identities → External collaboration settings
2. Guest invite settings: **Admins and users in the guest inviter role can invite** (minimum)
3. Collaboration restrictions: Allow invitations to any domain (or allowlist specific external domains)

When the BFF calls `/api/v1/external-access/invite`, it creates an Azure AD B2B invitation. The guest user receives an email with a redemption link. On first sign-in they accept the B2B invitation and appear as a guest in the Spaarke tenant.

---

## Section 2: BFF API Configuration

### 2.1 Azure App Service Configuration

BFF API: `spe-api-dev-67e2xz` in resource group `spe-infrastructure-westus2`

**Required app settings** (Azure App Service → Configuration → Application settings):

| Setting | Value |
|---------|-------|
| `AzureAd__TenantId` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `AzureAd__ClientId` | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `Cors__AllowedOrigins__0` | `https://sprk-external-workspace.powerappsportals.com` |
| `Cors__AllowedOrigins__1` | `http://localhost:3000` |

### 2.2 CORS Configuration

The BFF must respond to pre-flight OPTIONS requests from the Power Pages portal domain. Verify in `Sprk.Bff.Api` Program.cs that `AllowedOrigins` includes the portal URL.

Testing CORS:
```bash
curl -I -X OPTIONS \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/external/me \
  -H "Origin: https://sprk-external-workspace.powerappsportals.com" \
  -H "Access-Control-Request-Method: GET"
# Expected: HTTP 204 with Access-Control-Allow-Origin header
```

---

## Section 3: Power Pages Site Setup (One-Time)

### 3.1 Create or Activate the Site

1. Go to [make.powerpages.microsoft.com](https://make.powerpages.microsoft.com)
2. Select **SPAARKE DEV 1** environment
3. If no site exists: create a new blank site
4. Note the **website record ID** (GUID) — needed for PAC CLI and support

### 3.2 Surface the SPA Web Resource

The SPA is deployed as a Dataverse web resource (`sprk_externalworkspace`). To serve it on the portal:

1. In the Power Pages site, create a page at URL `/workspace`
2. Set the page type to **Custom** or **Blank** layout
3. Add a **Liquid** template reference:
   ```liquid
   {{ webresource('sprk_externalworkspace') }}
   ```
4. Alternatively, set the root page to redirect to the web resource URL directly

This only needs to be done once. Subsequent SPA deployments update the web resource content without any Power Pages portal changes.

---

## Section 4: Power Pages Table Permissions

Table permissions control which Dataverse records an authenticated portal contact can read or write via the Power Pages Web API (`/_api/`). Even though the current SPA routes data through the BFF instead of `/_api/`, table permissions are still required for:
- The parent-chain access model used by the BFF's participation lookup
- Future use of the `/_api/` path for lightweight reads

Configure all permissions in the **Portal Management** app (not Power Pages Design Studio — parent scope is only available in Portal Management).

### 4.1 Web Roles

Create three web roles (Portal Management → Web Roles):

| Role Name | Key | Anonymous Users | Authenticated Users |
|-----------|-----|----------------|---------------------|
| Secure Project Viewer | `secure-project-viewer` | No | No |
| Secure Project Collaborator | `secure-project-collaborator` | No | No |
| Secure Project Full Access | `secure-project-full` | No | No |

Set Website to the active Power Pages site. Do NOT mark as Authenticated Users Role — roles are assigned explicitly per contact when access is granted.

### 4.2 Table Permission Chain

The parent-chain provides cascading access: Contact → Participation record → Project → Documents/Events.

#### Level 0 — Participation Records (all roles)

| Field | Value |
|-------|-------|
| Entity Name | `External Record Access - Contact` |
| Entity Logical Name | `sprk_externalrecordaccess` |
| Scope | **Contact** |
| Contact Relationship | `sprk_contactid` |
| Read | ✓ | Write | ✗ | Create | ✗ | Delete | ✗ |
| Associated Web Roles | Viewer + Collaborator + Full Access |

#### Level 1 — Projects (child of Level 0, all roles)

| Field | Value |
|-------|-------|
| Entity Name | `Secure Projects` |
| Entity Logical Name | `sprk_project` |
| Scope | **Parent** |
| Parent Relationship | `sprk_projectid` |
| Parent Entity Permission | `External Record Access - Contact` |
| Read | ✓ | Write | ✗ | Create | ✗ | Delete | ✗ |
| Associated Web Roles | Viewer + Collaborator + Full Access |

#### Level 2 — Documents

| Role | Read | Write | Create | Delete | Scope |
|------|------|-------|--------|--------|-------|
| Viewer | ✓ | ✗ | ✗ | ✗ | Parent (sprk_projectid → Secure Projects) |
| Collaborator | ✓ | ✗ | ✓ | ✗ | Parent |
| Full Access | ✓ | ✓ | ✓ | ✗ | Parent |

Entity Logical Name: `sprk_document`, Parent Relationship: `sprk_projectid`

#### Level 2 — Events

| Role | Read | Write | Create | Delete | Scope |
|------|------|-------|--------|--------|-------|
| Viewer | ✓ | ✗ | ✗ | ✗ | Parent |
| Collaborator | ✓ | ✓ | ✓ | ✗ | Parent |
| Full Access | ✓ | ✓ | ✓ | ✗ | Parent |

Entity Logical Name: `sprk_event`, Parent Relationship: `sprk_regardingproject` (use actual relationship name)

#### Global Read — Reference Tables (Authenticated Users role)

For lookup/reference tables shared across all projects:

| Table | Entity Logical Name | Scope | CRUD |
|-------|---------------------|-------|------|
| Organizations/Accounts | `account` | Global | Read only |
| Document Types | `sprk_documenttype` | Global | Read only |

---

## Section 5: Power Pages Site Settings

Configure via Portal Management app → Site Settings (or Power Pages Studio → Settings).

### 5.1 Entra B2B Identity Provider

1. Power Pages Studio → Security → Identity providers
2. Add → Microsoft Entra ID
3. Configure:
   - Tenant type: **Workforce** (main tenant, not B2C)
   - Client ID: `f306885a-8251-492c-8d3e-34d7b476ffd0`
   - Authority: `https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2`
4. Claim mappings (map Entra claims to Contact fields):
   - `preferred_username` → `emailaddress1`
   - `given_name` → `firstname`
   - `family_name` → `lastname`

### 5.2 Content Security Policy

Add to site settings (`HTTP/Content-Security-Policy`):

```
default-src 'self';
script-src 'self' 'unsafe-inline';
style-src 'self' 'unsafe-inline';
connect-src 'self'
  https://spe-api-dev-67e2xz.azurewebsites.net
  https://login.microsoftonline.com
  https://graph.microsoft.com;
frame-ancestors 'self';
img-src 'self' data: https:;
```

### 5.3 Web API Site Settings (if using `/_api/`)

Required only if `/_api/` is used for direct Dataverse reads. Currently the SPA routes all data through the BFF, but these settings may be needed for future development:

```
Webapi/sprk_project/enabled = true
Webapi/sprk_project/fields = sprk_projectid,sprk_projectname,sprk_projectnumber,sprk_projectdescription,sprk_issecure,statecode,createdon,modifiedon

Webapi/sprk_document/enabled = true
Webapi/sprk_document/fields = sprk_documentid,sprk_documentname,sprk_documenttype,sprk_filesummary,_sprk_project_value,createdon

Webapi/sprk_event/enabled = true
Webapi/sprk_event/fields = sprk_eventid,sprk_eventname,sprk_duedate,sprk_eventstatus,sprk_todoflag,_sprk_regardingproject_value,createdon

Webapi/sprk_externalrecordaccess/enabled = true
Webapi/sprk_externalrecordaccess/fields = sprk_name,_sprk_contact_value,_sprk_project_value,sprk_accesslevel,statecode
```

**Note:** Use the actual Dataverse logical field names, not the friendly display names. See the [Architecture Reference](../architecture/external-access-spa-architecture.md) for the correct field name mapping.

---

## Section 6: Invitation Flow

When a Core User invites an external contact to a Secure Project, the BFF handles the full flow.

### 6.1 What POST `/api/v1/external-access/invite` Does

1. Looks up or creates a Contact record by email in Dataverse
2. Creates an `sprk_externalrecordaccess` record (the participation)
3. Calls the Microsoft Graph B2B invitation API to send an invite email
4. Returns `{ contactId, inviteRedeemUrl, status }` to the caller

### 6.2 What the External User Experiences

1. Receives an invitation email (from Microsoft/Entra) with a "Get Started" link
2. Clicks the link → Entra B2B redemption flow (accepts permissions, creates guest account)
3. After redemption, navigates to the portal URL
4. MSAL triggers login → SSO with their Microsoft 365 account (often zero-click)
5. SPA loads → `GET /api/v1/external/me` returns their project access
6. Workspace displays with their assigned project(s)

### 6.3 Re-Inviting or Resending

If a user reports not receiving the invitation:
1. Check the B2B invitation status in Entra → External Identities → All users
2. If status is `PendingAcceptance`, use the invitation link from the original invite response
3. The BFF's invite endpoint is idempotent — calling it again for the same email will resolve the existing contact and create a new invitation

### 6.4 Revoking Access

POST `/api/v1/external-access/revoke`:
1. Sets the `sprk_externalrecordaccess` record to inactive (`statecode = 1`)
2. Removes the contact from the SPE container (if `containerId` provided)
3. Invalidates Redis participation cache for the contact (60s TTL forces immediate eviction)

The user's guest account in Entra is **not** deleted — only participation is revoked. The user will see an empty workspace on next login (no projects listed).

---

## Section 7: SPE Container Membership

For projects with SharePoint Embedded file storage, the BFF manages container membership via Microsoft Graph.

### 7.1 Granting Container Access

When access is granted at Collaborate or Full Access level, the BFF calls:
```
POST /storage/fileStorage/containers/{containerId}/permissions
{
    "roles": ["writer"],
    "grantedToV2": { "user": { "userPrincipalName": "alice@contoso.com" } }
}
```

View Only level: `"roles": ["reader"]`

### 7.2 External Sharing Prerequisite

External user sharing must be enabled on the SPE container application:
```powershell
Set-SPOApplication -OverrideTenantSharingCapability $true -OwningApplicationId "{speAppId}"
```

This is a one-time configuration per SPE application registration.

---

## Section 8: Monitoring and Troubleshooting

### 8.1 Common Issues

| Symptom | Likely Cause | Resolution |
|---------|-------------|------------|
| SPA loads but shows login loop | MSAL redirect URI mismatch | Check app registration has exact portal URL as SPA redirect URI |
| 401 on all BFF calls | JWT audience mismatch | Verify `MSAL_BFF_SCOPE` matches `api://1e40baad-...` in app settings |
| 403 `contact_not_found` | No Dataverse Contact with matching email | Ensure Contact record exists with `emailaddress1` = user's UPN |
| Empty project list (`/me` returns empty projects) | Access records inactive or missing | Check `sprk_externalrecordaccess` for `statecode = 0` records for the contact |
| `AttributePermissionIsMissing` | Column not in Web API site setting | Add field to `Webapi/{table}/fields` site setting |
| User sees data they shouldn't | Table permission scope too broad | Check scope — must be Contact or Parent, not Global |
| Invitation email not received | B2B invitation in pending state | Check Entra External Identities for invitation status |
| CORS error in browser console | BFF CORS not configured for portal domain | Add portal URL to BFF `Cors__AllowedOrigins` app setting |
| SPA blank after deploy | Old cached version | Hard refresh (Ctrl+F5) or clear browser cache |
| SPE files not accessible | Container membership not provisioned | Re-call `/grant` or check `SpeContainerMembershipService` logs |

### 8.2 Checking BFF Logs

```bash
# Stream live logs from App Service
az webapp log tail -g spe-infrastructure-westus2 -n spe-api-dev-67e2xz

# Filter external access logs
az webapp log tail -g spe-infrastructure-westus2 -n spe-api-dev-67e2xz | grep "\[EXT"
```

Key log prefixes:
- `[EXT-AUTH]` — ExternalCallerAuthorizationFilter (contact resolution, participation loading)
- `[EXT-ME]` — GetExternalUserContext handler
- `[EXT-DATA]` — ExternalDataService (Dataverse queries)
- `[EXT-GRANT]` — GrantExternalAccessEndpoint
- `[EXT-INVITE]` — InviteExternalUserEndpoint

### 8.3 Checking Participation Records

```bash
# Using az CLI with Dataverse
TOKEN=$(az account get-access-token \
  --resource https://spaarkedev1.crm.dynamics.com \
  --query accessToken -o tsv)

# Get all active access records for a contact
curl -s -H "Authorization: Bearer $TOKEN" \
  "https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_externalrecordaccesses?\$filter=_sprk_contact_value eq {contactId} and statecode eq 0&\$select=_sprk_contact_value,_sprk_project_value,sprk_accesslevel,statecode"
```

### 8.4 Audit Trail

| What | Where | Maintained By |
|------|-------|---------------|
| Who granted access + when | `sprk_externalrecordaccess.sprk_grantedby` + `sprk_granteddate` | BFF API on grant |
| Who approved file access | `sprk_externalrecordaccess.sprk_approvedby` + `sprk_approveddate` | BFF API on file approval |
| B2B invitation sent/redeemed | Entra External Identities → All users | Entra (automatic) |
| BFF authorization decisions | App Service logs `[EXT-AUTH]` | BFF (INFO/WARN) |
| SPE file access | SPE audit logs via Graph API | SharePoint Embedded |

---

## Section 9: Access Level → Role Mapping

When granting access, the BFF assigns the appropriate web role to the contact:

| `sprk_accesslevel` value | Access Level | Web Role Assigned |
|--------------------------|-------------|-------------------|
| `100000000` | ViewOnly | `secure-project-viewer` |
| `100000001` | Collaborate | `secure-project-collaborator` |
| `100000002` | FullAccess | `secure-project-full` |

**Capability summary:**

| Capability | ViewOnly | Collaborate | FullAccess |
|------------|----------|-------------|------------|
| View project and documents | Yes | Yes | Yes |
| Upload documents | No | Yes | Yes |
| Download files (SPE) | Yes (Reader) | Yes (Writer) | Yes (Writer) |
| Create events/tasks | No | Yes | Yes |
| Update events/tasks | No | Yes | Yes |
| Run AI summaries | No | Yes | Yes |
| Invite other participants | No | No | Yes |

---

## Section 10: Power Pages Built-In Table Reference

Power Pages provides these tables out of the box. Spaarke uses them directly — no custom equivalents needed.

### `mspp_webrole` — Web Role

Groups table permissions and is assigned to Contacts to gate record access.

| Column | Type | Purpose |
|--------|------|---------|
| `mspp_webroleid` | PK | Primary key |
| `mspp_name` | String(100) | Role name (e.g., "Secure Project Participant") |
| `mspp_key` | String(100) | Non-localized key for code/workflow lookups |
| `mspp_description` | Memo | Description |
| `mspp_anonymoususersrole` | Boolean | Applies to unauthenticated visitors |
| `mspp_authenticatedusersrole` | Boolean | Applies to all signed-in users automatically |
| `mspp_websiteid` | Lookup → `mspp_website` | Which Power Pages site owns this role |

Contact assignment: N:N relationship `powerpagecomponent_mspp_webrole_contact`.

Default roles auto-created per site: **Anonymous Users** (unauthenticated) and **Authenticated Users** (all signed-in contacts).

---

### `mspp_entitypermission` — Table Permission

Defines CRUD access rules for a Dataverse table, scoped by access type.

| Column | Type | Purpose |
|--------|------|---------|
| `mspp_entitypermissionid` | PK | Primary key |
| `mspp_entityname` | String(400) | Display name of the rule |
| `mspp_entitylogicalname` | String(250) | Dataverse table this applies to |
| `mspp_scope` | Choice | Access type scope (see below) |
| `mspp_read` / `mspp_write` / `mspp_create` / `mspp_delete` | Boolean | CRUD permissions |
| `mspp_append` / `mspp_appendto` | Boolean | Append permissions |
| `mspp_contactrelationship` | String | Relationship name for Contact scope |
| `mspp_parentrelationship` | String | Relationship name for Parent scope |
| `mspp_parententitypermission` | Lookup → self | Parent permission (for hierarchy chains) |
| `mspp_websiteid` | Lookup → `mspp_website` | Site that owns this permission |

**Scope values:**

| Value | Label | Meaning |
|-------|-------|---------|
| `756150000` | Global | All records of this table |
| `756150001` | Contact | Records related to the signed-in Contact |
| `756150002` | Account | Records related to the Contact's parent Account |
| `756150003` | Parent | Records related to a parent record the Contact can access |
| `756150004` | Self | Only the Contact's own record |

N:N to Web Roles via `mspp_entitypermission_webrole`.

---

### `adx_invitation` — Invitation

Built-in invitation system for onboarding external users.

| Column | Type | Purpose |
|--------|------|---------|
| `adx_invitationid` | PK | Primary key |
| `adx_invitationcode` | String(200) | Single-use redemption code |
| `adx_type` | Choice | Single (`756150000`) or Group (`756150001`) |
| `adx_invitecontact` | Lookup → Contact | Invited contact |
| `adx_invitercontact` | Lookup → Contact | Who sent the invitation |
| `adx_redeemedcontact` | Lookup → Contact | Contact who redeemed |
| `adx_assigntoaccount` | Lookup → Account | Assign redeemed contact to this Account |
| `adx_expirydate` | DateTime | Expiration date |
| `adx_maximumredemptions` | Integer | Max redemptions (1 for single-use) |
| `adx_redemptions` | Integer | Current redemption count |
| `statuscode` | Status | New → Sent → Redeemed → Inactive |

N:N to `mspp_webrole` — web roles are **automatically assigned on redemption**.

---

### `adx_externalidentity` — External Identity

Maps a Contact to their federated login identity. Auto-created by Power Pages on first login; can be pre-created to enable immediate access after Entra registration.

| Column | Type | Purpose |
|--------|------|---------|
| `adx_externalidentityid` | PK | Primary key |
| `adx_username` | String(100) | Username from identity provider |
| `adx_identityprovidername` | String(400) | Provider name (e.g., `"AzureAD"`) |
| `adx_contactid` | Lookup → Contact | Mapped Contact |

Pre-create via BFF on invite to allow login without separate portal registration flow:

```http
POST /api/data/v9.2/adx_externalidentities
{
    "adx_username": "jane.smith@partner.com",
    "adx_identityprovidername": "https://login.microsoftonline.com/{tenantId}/v2.0",
    "adx_contactid@odata.bind": "/contacts({contactId})"
}
```

---

### `adx_inviteredemption` — Invite Redemption

Activity record tracking each redemption event. Auto-created by the platform — no manual management needed.

---

## Related Resources

- **Architecture Reference**: [external-access-spa-architecture.md](../architecture/external-access-spa-architecture.md)
- **Developer Guide**: [EXTERNAL-ACCESS-SPA-GUIDE.md](EXTERNAL-ACCESS-SPA-GUIDE.md)
- **UAC Architecture**: [uac-access-control.md](../architecture/uac-access-control.md)
- **Power Pages table permissions (MS Learn)**: https://learn.microsoft.com/en-us/power-pages/security/table-permissions
- **Create web roles (MS Learn)**: https://learn.microsoft.com/en-us/power-pages/security/create-web-roles
- **B2B invitation API (MS Learn)**: https://learn.microsoft.com/en-us/graph/api/invitation-post
- **Invitation table reference (MS Learn)**: https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/entities/adx_invitation
- **Power Pages security overview**: https://learn.microsoft.com/en-us/power-pages/security/power-pages-security
