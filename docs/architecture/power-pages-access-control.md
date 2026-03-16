# Power Pages Access Control & UAC Configuration Guide

> **Domain**: External User Access Control, Power Pages Security
> **Status**: Design / Configuration Guide
> **Last Updated**: 2026-03-16
> **Applies To**: Secure Project Module, future external access features

---

## Overview

This guide describes how to configure Power Pages security to implement Spaarke's Unified Access Control (UAC) model for external users. It covers the built-in Power Pages tables, how to configure table permissions with parent-chain cascading, web role setup, and the orchestration between Power Pages (Plane 1) and the BFF API (Planes 2 & 3).

For the overall UAC architecture, see [UAC Access Control Architecture](uac-access-control.md).
For SPA development standards, see [Power Pages SPA Technical Guide](power-pages-spa-guide.md).

---

## Built-In Power Pages Tables (Do Not Replicate)

Power Pages provides these tables out of the box. Spaarke leverages them directly — no custom equivalents needed.

### Web Role (`mspp_webrole`)

Defines roles that group table permissions and are assigned to Contacts.

| Column | Type | Purpose |
|--------|------|---------|
| `mspp_webroleid` | PK | Primary key |
| `mspp_name` | String(100) | Role name (e.g., "Secure Project Participant") |
| `mspp_key` | String(100) | Non-localized key for code/workflow lookups |
| `mspp_description` | Memo | Description |
| `mspp_anonymoususersrole` | Boolean | Applies to unauthenticated visitors |
| `mspp_authenticatedusersrole` | Boolean | Applies to all signed-in users automatically |
| `mspp_websiteid` | Lookup → `mspp_website` | Which Power Pages site owns this role |

**Contact assignment**: N:N relationship `powerpagecomponent_mspp_webrole_contact`.

**Default roles** (auto-created per site):
- **Anonymous Users** — applied to all unauthenticated visitors
- **Authenticated Users** — applied to all signed-in contacts automatically

### Table Permission (`mspp_entitypermission`)

Defines CRUD access rules for a Dataverse table, scoped by access type.

| Column | Type | Purpose |
|--------|------|---------|
| `mspp_entitypermissionid` | PK | Primary key |
| `mspp_entityname` | String(400) | Display name of the rule |
| `mspp_entitylogicalname` | String(250) | Dataverse table this applies to |
| `mspp_scope` | Choice | Access type scope (see below) |
| `mspp_read` | Boolean | Allow Read |
| `mspp_write` | Boolean | Allow Write/Update |
| `mspp_create` | Boolean | Allow Create |
| `mspp_delete` | Boolean | Allow Delete |
| `mspp_append` | Boolean | Allow Append |
| `mspp_appendto` | Boolean | Allow Append To |
| `mspp_contactrelationship` | String | Relationship name for Contact scope |
| `mspp_accountrelationship` | String | Relationship name for Account scope |
| `mspp_parentrelationship` | String | Relationship name for Parent scope |
| `mspp_parententitypermission` | Lookup → self | Parent permission (for hierarchy) |
| `mspp_websiteid` | Lookup → `mspp_website` | Site that owns this permission |

**Scope values:**

| Value | Label | Meaning |
|-------|-------|---------|
| 756150000 | **Global** | All records of this table |
| 756150001 | **Contact** | Records related to the signed-in Contact |
| 756150002 | **Account** | Records related to the Contact's parent Account |
| 756150003 | **Parent** | Records related to a parent record the Contact can access |
| 756150004 | **Self** | Only the Contact's own record |

**N:N to Web Roles**: `mspp_entitypermission_webrole`.

### Invitation (`adx_invitation`)

Built-in invitation system for onboarding external users.

| Column | Type | Purpose |
|--------|------|---------|
| `adx_invitationid` | PK | Primary key |
| `adx_invitationcode` | String(200) | Single-use redemption code |
| `adx_type` | Choice | Single (756150000) or Group (756150001) |
| `adx_invitecontact` | Lookup → Contact | Invited contact |
| `adx_invitercontact` | Lookup → Contact | Who sent the invitation |
| `adx_redeemedcontact` | Lookup → Contact | Contact who redeemed |
| `adx_assigntoaccount` | Lookup → Account | Assign redeemed contact to this Account |
| `adx_expirydate` | DateTime | Expiration date |
| `adx_maximumredemptions` | Integer | Max redemptions (1 for single-use) |
| `adx_redemptions` | Integer | Current redemption count |
| `statuscode` | Status | New → Sent → Redeemed → Inactive |

**Key feature**: N:N to `mspp_webrole` — web roles are **automatically assigned on redemption**.

### External Identity (`adx_externalidentity`)

Maps a Contact to their federated login identity.

| Column | Type | Purpose |
|--------|------|---------|
| `adx_externalidentityid` | PK | Primary key |
| `adx_username` | String(100) | Username from identity provider |
| `adx_identityprovidername` | String(400) | Provider name (e.g., "AzureAD") |
| `adx_contactid` | Lookup → Contact | Mapped Contact |

**Auto-managed**: Power Pages creates/updates this on login. Can be pre-created to allow login without separate registration.

### Invite Redemption (`adx_inviteredemption`)

Activity record tracking each invitation redemption event. Auto-created by the platform.

---

## Custom Table: sprk_externalrecordaccess

The one custom table needed — a participation/membership junction linking Contacts to records with access level and audit trail.

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_externalrecordaccessid` | PK | Primary key |
| `sprk_name` | String | Auto-generated display name |
| `sprk_contactid` | Lookup → Contact | External contact granted access |
| `sprk_projectid` | Lookup → sprk_project | Project (nullable — future: matter, etc.) |
| `sprk_matterid` | Lookup → sprk_matter | Future: matter-level access |
| `sprk_accesslevel` | Choice | View Only / Collaborate / Full Access |
| `sprk_grantedby` | Lookup → SystemUser | Core User who granted access |
| `sprk_granteddate` | DateTime | When access was granted |
| `sprk_approvedby` | Lookup → SystemUser | Core User who approved doc/file access |
| `sprk_approveddate` | DateTime | When doc/file access was approved |
| `sprk_expirydate` | DateTime | Optional expiration |
| `sprk_accountid` | Lookup → Account | External Access Account |
| `statecode` | State | Active / Inactive |

This table is the **anchor** for the Power Pages table permission parent chain (Level 0).

---

## Table Permission Configuration

### Parent-Chain Architecture

The parent-chain model provides automatic cascading access. When a Contact has access to a participation record, they automatically get access to the parent project and all its child records — no per-record permission grants needed.

```
Level 0: sprk_externalrecordaccess
         Scope: Contact
         Relationship: sprk_contactid
         CRUD: Read
         Web Role: "Secure Project Participant"

  └── Level 1: sprk_project
               Scope: Parent
               Relationship: sprk_projectid (on participation record)
               CRUD: Read

        ├── Level 2: sprk_document
        │            Scope: Parent
        │            Relationship: sprk_projectid (on document)
        │            CRUD: Read (View Only) or Read+Create (Collaborate)
        │
        ├── Level 2: sprk_event
        │            Scope: Parent
        │            Relationship: sprk_projectid (on event)
        │            CRUD: Read+Create+Write (all access levels)
        │
        ├── Level 2: sprk_todo (if separate from events)
        │            Scope: Parent
        │            Relationship: sprk_projectid
        │            CRUD: Read+Create+Write
        │
        └── Level 2: sprk_externalrecordaccess (other participants)
                     Scope: Parent
                     Relationship: sprk_projectid
                     CRUD: Read (see who else has access)
```

### Reference/Lookup Tables

Tables that are shared across all projects (organizations, jurisdictions, document types) use **Global Read-Only** scope on the "Authenticated Users" web role:

| Table | Scope | CRUD | Web Role |
|-------|-------|------|----------|
| `sprk_organization` | Global | Read | Authenticated Users |
| `sprk_documenttype` | Global | Read | Authenticated Users |
| `sprk_jurisdiction` | Global | Read | Authenticated Users |
| `sprk_priority` | Global | Read | Authenticated Users |

### Configuration Steps

**Step 1: Create web role**

In Portal Management app → Web Roles:
- Name: `Secure Project Participant`
- Key: `secure-project-participant`
- Website: (select your Power Pages site)
- Anonymous Users Role: No
- Authenticated Users Role: No (assigned explicitly per contact)

**Step 2: Create table permissions**

In Portal Management app → Table Permissions:

1. **Level 0 — Participation records**
   - Entity Name: "External Record Access - Contact"
   - Entity Logical Name: `sprk_externalrecordaccess`
   - Scope: **Contact**
   - Contact Relationship: `sprk_contactid`
   - Read: Yes | Write: No | Create: No | Delete: No
   - Associated Web Roles: `Secure Project Participant`

2. **Level 1 — Projects (child of Level 0)**
   - Entity Name: "Secure Projects"
   - Entity Logical Name: `sprk_project`
   - Scope: **Parent**
   - Parent Relationship: `sprk_projectid`
   - Parent Entity Permission: "External Record Access - Contact"
   - Read: Yes | Write: No | Create: No | Delete: No
   - Associated Web Roles: `Secure Project Participant`

3. **Level 2 — Documents (child of Level 1)**
   - Entity Name: "Project Documents"
   - Entity Logical Name: `sprk_document`
   - Scope: **Parent**
   - Parent Relationship: `sprk_projectid`
   - Parent Entity Permission: "Secure Projects"
   - Read: Yes | Write: No | Create: Yes | Delete: No
   - Associated Web Roles: `Secure Project Participant`

4. **Level 2 — Events (child of Level 1)**
   - Entity Name: "Project Events"
   - Entity Logical Name: `sprk_event`
   - Scope: **Parent**
   - Parent Relationship: `sprk_projectid`
   - Parent Entity Permission: "Secure Projects"
   - Read: Yes | Write: Yes | Create: Yes | Delete: No
   - Associated Web Roles: `Secure Project Participant`

5. **Reference tables — Global Read**
   - One permission per reference table
   - Scope: **Global**
   - Read: Yes | all others: No
   - Associated Web Roles: `Authenticated Users`

**Step 3: Enable Web API for tables**

In Portal Management app → Site Settings:

```
Webapi/sprk_externalrecordaccess/enabled = true
Webapi/sprk_externalrecordaccess/fields = sprk_name,sprk_contactid,sprk_projectid,sprk_accesslevel,statecode

Webapi/sprk_project/enabled = true
Webapi/sprk_project/fields = sprk_name,sprk_description,sprk_referencenumber,sprk_issecure,...

Webapi/sprk_document/enabled = true
Webapi/sprk_document/fields = sprk_name,sprk_documenttype,sprk_summary,sprk_filesize,...

Webapi/sprk_event/enabled = true
Webapi/sprk_event/fields = sprk_name,sprk_duedate,sprk_status,sprk_assignedto,...
```

---

## Invitation Flow Configuration

### Creating Invitations

The BFF API creates `adx_invitation` records when a Core User invites an external contact:

```
POST /api/data/v9.2/adx_invitations

{
    "adx_name": "Invitation to Acme Litigation",
    "adx_type": 756150000,                          // Single
    "adx_invitecontact@odata.bind": "/contacts({contactId})",
    "adx_invitercontact@odata.bind": "/contacts({coreUserContactId})",
    "adx_assigntoaccount@odata.bind": "/accounts({externalAccessAccountId})",
    "adx_expirydate": "2026-04-16",
    "adx_maximumredemptions": 1
}
```

**Then associate web roles** (N:N):
```
POST /api/data/v9.2/adx_invitations({invitationId})/adx_invitation_mspp_webrole_powerpagecomponent/$ref

{
    "@odata.id": "/mspp_webroles({secureProjectParticipantRoleId})"
}
```

**On redemption** (automatic):
1. Contact clicks invitation link
2. Completes Entra External ID signup/verification
3. `adx_invitation.statuscode` → Redeemed
4. `adx_inviteredemption` activity record created
5. Web role auto-assigned to Contact
6. Contact assigned to Account (if `adx_assigntoaccount` set)
7. Contact now has access via table permission chain

### Pre-Creating External Identity

For a smoother onboarding, pre-create the `adx_externalidentity` record so the Contact can log in immediately after Entra registration:

```
POST /api/data/v9.2/adx_externalidentities

{
    "adx_username": "jane.smith@kirkland.com",
    "adx_identityprovidername": "https://login.microsoftonline.com/{tenantId}/v2.0",
    "adx_contactid@odata.bind": "/contacts({contactId})"
}
```

---

## Three-Plane Orchestration

### What Power Pages Handles (Plane 1 — Automatic)

Once table permissions are configured and the Contact has the web role:
- Contact can read their participation records (Level 0)
- Contact can read accessible projects (Level 1, parent chain)
- Contact can read/create documents, events, tasks (Level 2, parent chain)
- Contact can read reference/lookup data (Global scope)

**No BFF action needed** — the parent chain handles Dataverse record access automatically.

### What BFF Must Handle (Planes 2 & 3)

| Plane | Trigger | BFF Action |
|-------|---------|------------|
| **Plane 2: SPE Files** | Participation record created | Add Contact's Entra External ID to SPE container via Graph API (`POST /storage/fileStorage/containers/{containerId}/permissions`) |
| **Plane 2: SPE Files** | Participation record deactivated | Remove Contact from SPE container |
| **Plane 3: AI Search** | Contact queries AI features | BFF reads active participation records → constructs `search.in` filter with accessible project IDs |

### SPE Container Membership Management

```
// Grant: Add contact to container as Reader or Writer
POST /storage/fileStorage/containers/{containerId}/permissions

{
    "roles": ["reader"],  // or ["writer"] for Collaborate access
    "grantedToV2": {
        "user": {
            "userPrincipalName": "jane.smith@kirkland.com"
        }
    }
}
```

```
// Revoke: Remove contact from container
DELETE /storage/fileStorage/containers/{containerId}/permissions/{permissionId}
```

**External user sharing must be enabled:**
```powershell
Set-SPOApplication -OverrideTenantSharingCapability $true -OwningApplicationId "{appId}"
```

### AI Search Filter Construction

```csharp
// BFF constructs filter at query time
var participations = await _dataverse.GetActiveParticipationsAsync(contactId);
var projectIds = participations.Select(p => p.ProjectId.ToString()).ToList();

var searchOptions = new SearchOptions
{
    Filter = $"project_ids/any(p:search.in(p, '{string.Join(",", projectIds)}'))"
};
```

---

## Access Level Matrix

| Capability | View Only | Collaborate | Full Access |
|------------|-----------|-------------|-------------|
| **View project metadata** | Yes | Yes | Yes |
| **View documents** | Yes | Yes | Yes |
| **Upload documents** | No | Yes | Yes |
| **Download files (SPE)** | Yes (Reader) | Yes (Writer) | Yes (Writer) |
| **Create events/tasks** | No | Yes | Yes |
| **Update events/tasks** | No | Yes | Yes |
| **Run AI summaries** | Yes | Yes | Yes |
| **AI semantic search** | Yes | Yes | Yes |
| **View other participants** | Yes | Yes | Yes |

### Mapping Access Levels to Permissions

| Access Level | Table Permissions | SPE Container Role | AI Search |
|-------------|-------------------|-------------------|-----------|
| View Only | Read on all tables | Reader | Filter includes project |
| Collaborate | Read + Create on documents; Read + Create + Write on events/tasks | Writer | Filter includes project |
| Full Access | Read + Create + Write on all tables | Writer | Filter includes project |

To implement per-access-level table permissions, create **separate web roles** per access level, each with different CRUD settings:
- `secure-project-viewer` — Read only
- `secure-project-collaborator` — Read + Create (docs) + Read/Create/Write (events)
- `secure-project-full` — Read + Create + Write (all)

---

## Constraints and Limitations

| Constraint | Detail | Mitigation |
|-----------|--------|------------|
| **Max parent-chain depth** | ~4-5 levels practical limit | Spaarke needs 2-3 levels — well within limit |
| **No polymorphic lookups** | Not supported in parent-child chains | Spaarke uses field resolver pattern (no polymorphic fields) |
| **Parent scope config location** | Only in Portal Management app (not Design Studio) | Document configuration steps clearly |
| **Deep chains impact performance** | Each level = additional Dataverse query | Keep to 2-3 levels |
| **No per-record Power Pages permissions** | Table permissions are schema-level, not record-level | Contact scope + parent chain provides record-level effect |
| **Web role assignment has no expiry** | N:N relationship has no date fields | Use `sprk_externalrecordaccess.sprk_expirydate` + scheduled cleanup |
| **POA table for Contacts** | `principalobjectaccess` works for systemuser, not Power Pages contacts | Power Pages uses its own table permission model — no POA needed |
| **Teams don't apply to Contacts** | Dataverse teams contain systemuser records only | Use Account scope or Contact scope — not teams |

---

## Monitoring and Troubleshooting

### Common Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| External user sees no data | Missing web role assignment | Check Contact → Web Role N:N association |
| External user sees all records | Table permission scope = Global | Change to Contact or Parent scope |
| External user can see project but not documents | Missing Level 2 parent-chain permission | Add document table permission as child of project permission |
| Invitation not working | Expired or already redeemed | Check `adx_invitation.statuscode` and `adx_expirydate` |
| User can log in but sees nothing | Web role not assigned on invitation redemption | Check `adx_invitation` → Web Role N:N association |
| "AttributePermissionIsMissing" error | Column not in Web API site settings | Add column to `Webapi/{table}/fields` |
| User can see docs but not download files | Missing SPE container membership | BFF must add Contact to container via Graph API |
| AI search returns no results | Missing project in search filter | BFF must include project IDs from active participation records |

### Audit Trail

| What | Where | Maintained By |
|------|-------|---------------|
| Who granted external access | `sprk_externalrecordaccess.sprk_grantedby` + `sprk_granteddate` | BFF API |
| Who approved file access | `sprk_externalrecordaccess.sprk_approvedby` + `sprk_approveddate` | BFF API |
| Invitation sent/redeemed | `adx_invitation.statuscode` + `adx_inviteredemption` | Power Pages (automatic) |
| SPE file access | SPE audit logs via Graph API | SharePoint Embedded |
| BFF API authorization decisions | `AuthorizationService` audit logs | BFF API (INFO/WARN) |

---

## Future Considerations

### Power Pages Web Role + Dataverse Security Role Merge (2025 Wave 2 Preview)

Microsoft is unifying Power Pages web roles with Dataverse security roles. When GA:
- Each web role will have a corresponding Dataverse security role
- External contacts could eventually use the same BU-scoped role model as internal users
- Could simplify the three-plane orchestration (Dataverse security would handle more automatically)

**Current status**: Preview. Do not depend on this for production. Design the UAC model to work with current Power Pages table permissions.

### Azure AI Search Native Entra ACL (2025 Preview)

Document-level access control using Entra tokens at query time:
- Documents indexed with ACL entries (Entra user/group OIDs)
- Search automatically trims results based on caller's Entra token
- Could eliminate manual `search.in` filter construction

**Current status**: Preview (REST API 2025-05-01-preview). Requires caller to have valid Entra token. For external users via non-Entra identity, manual filters remain necessary.

---

## Related Resources

- [UAC Access Control Architecture](uac-access-control.md) — Three-plane authorization model
- [Power Pages SPA Technical Guide](power-pages-spa-guide.md) — SPA development standards
- [Secure Project Module Design](../../projects/sdap-secure-project-module/design.md) — Product design document
- [Set table permissions (MS Learn)](https://learn.microsoft.com/en-us/power-pages/security/table-permissions)
- [Create web roles (MS Learn)](https://learn.microsoft.com/en-us/power-pages/security/create-web-roles)
- [Power Pages security overview (MS Learn)](https://learn.microsoft.com/en-us/power-pages/security/power-pages-security)
- [Invitation table reference (MS Learn)](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/entities/adx_invitation)
- [OAuth 2.0 implicit grant flow (MS Learn)](https://learn.microsoft.com/en-us/power-pages/security/oauth-implicit-grant-flow)
