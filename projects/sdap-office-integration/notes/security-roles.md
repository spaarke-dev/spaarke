# Task 014: Dataverse Security Roles Configuration

> **Status**: Ready for Manual Execution
> **Type**: Dataverse Security Configuration
> **Location**: Power Apps Maker Portal → Settings → Security Roles

## Overview

Configure Dataverse security roles for the new Office integration tables (EmailArtifact, AttachmentArtifact, ProcessingJob). Users need appropriate CRUD permissions based on their existing Spaarke roles, with user-level access for personal artifacts.

## Security Model

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           SECURITY LEVELS                                   │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                     System Administrator                             │   │
│  │    Full access to ALL tables, ALL records (Organization level)       │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                     Spaarke Administrator                            │   │
│  │    Full access to Office tables (Organization level)                 │   │
│  │    Manage all EmailArtifacts, AttachmentArtifacts, ProcessingJobs    │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                     Spaarke Service Account                          │   │
│  │    Full access to Office tables (Organization level)                 │   │
│  │    Used by BFF API and background workers                            │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                     Spaarke Standard User                            │   │
│  │    User-level access to Office tables                                │   │
│  │    Can only see/edit OWN EmailArtifacts, AttachmentArtifacts, Jobs   │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Permission Matrix

### EmailArtifact (sprk_emailartifact)

| Permission | Standard User | Administrator | Service Account |
|------------|---------------|---------------|-----------------|
| **Create** | ✅ Organization | ✅ Organization | ✅ Organization |
| **Read** | ✅ User | ✅ Organization | ✅ Organization |
| **Write** | ✅ User | ✅ Organization | ✅ Organization |
| **Delete** | ✅ User | ✅ Organization | ✅ Organization |
| **Append** | ✅ User | ✅ Organization | ✅ Organization |
| **Append To** | ✅ User | ✅ Organization | ✅ Organization |
| **Assign** | ❌ None | ✅ Organization | ✅ Organization |
| **Share** | ❌ None | ✅ Organization | ✅ Organization |

**Notes**:
- Standard users create with Organization level but read/write at User level (see only their own records)
- Organization-level Create allows creating records owned by the user
- User-level Read/Write restricts access to records they own

### AttachmentArtifact (sprk_attachmentartifact)

| Permission | Standard User | Administrator | Service Account |
|------------|---------------|---------------|-----------------|
| **Create** | ✅ Organization | ✅ Organization | ✅ Organization |
| **Read** | ✅ User | ✅ Organization | ✅ Organization |
| **Write** | ✅ User | ✅ Organization | ✅ Organization |
| **Delete** | ✅ User | ✅ Organization | ✅ Organization |
| **Append** | ✅ User | ✅ Organization | ✅ Organization |
| **Append To** | ✅ User | ✅ Organization | ✅ Organization |
| **Assign** | ❌ None | ✅ Organization | ✅ Organization |
| **Share** | ❌ None | ✅ Organization | ✅ Organization |

**Notes**: Same model as EmailArtifact - user-level access for standard users.

### ProcessingJob (sprk_processingjob)

| Permission | Standard User | Administrator | Service Account |
|------------|---------------|---------------|-----------------|
| **Create** | ✅ Organization | ✅ Organization | ✅ Organization |
| **Read** | ✅ User | ✅ Organization | ✅ Organization |
| **Write** | ❌ None | ✅ Organization | ✅ Organization |
| **Delete** | ❌ None | ✅ Organization | ✅ Organization |
| **Append** | ❌ None | ✅ Organization | ✅ Organization |
| **Append To** | ❌ None | ✅ Organization | ✅ Organization |
| **Assign** | ❌ None | ✅ Organization | ✅ Organization |
| **Share** | ❌ None | ✅ Organization | ✅ Organization |

**Notes**:
- Standard users can create jobs (trigger via API) and read their own job status
- Standard users CANNOT modify or delete jobs (only service account/admin can)
- Service account updates job status during processing

## Existing Spaarke Roles

Identify these roles to update:

| Role Name | Purpose | Update Required |
|-----------|---------|-----------------|
| `Spaarke Standard User` | Regular users of the application | ✅ Add Office tables (User level) |
| `Spaarke Administrator` | Full admin access | ✅ Add Office tables (Organization level) |
| `Spaarke Service Account` | BFF API and workers | ✅ Add Office tables (Organization level) |

## Configuration Steps

### Step 1: Open Security Roles

1. Navigate to: **Power Apps > Settings (gear icon) > Advanced Settings**
2. Click **Settings > Security > Security Roles**
3. Or navigate directly: `https://{org}.crm.dynamics.com/main.aspx?settingsonly=true`

### Step 2: Update Spaarke Standard User Role

1. Click on **Spaarke Standard User** role
2. Navigate to **Custom Entities** tab
3. Find and configure these tables:

**EmailArtifact**:
| Action | Level | Click Pattern |
|--------|-------|---------------|
| Create | Organization | Click 4 times (full green) |
| Read | User | Click 1 time (single yellow) |
| Write | User | Click 1 time |
| Delete | User | Click 1 time |
| Append | User | Click 1 time |
| Append To | User | Click 1 time |

**AttachmentArtifact**: Same as EmailArtifact

**ProcessingJob**:
| Action | Level | Click Pattern |
|--------|-------|---------------|
| Create | Organization | Click 4 times |
| Read | User | Click 1 time |
| Write | None | Leave empty |
| Delete | None | Leave empty |
| Append | None | Leave empty |
| Append To | None | Leave empty |

4. Click **Save and Close**

### Step 3: Update Spaarke Administrator Role

1. Click on **Spaarke Administrator** role
2. Navigate to **Custom Entities** tab
3. For all three tables (EmailArtifact, AttachmentArtifact, ProcessingJob):

| Action | Level | Click Pattern |
|--------|-------|---------------|
| Create | Organization | Click 4 times (full green) |
| Read | Organization | Click 4 times |
| Write | Organization | Click 4 times |
| Delete | Organization | Click 4 times |
| Append | Organization | Click 4 times |
| Append To | Organization | Click 4 times |
| Assign | Organization | Click 4 times |
| Share | Organization | Click 4 times |

4. Click **Save and Close**

### Step 4: Update/Create Spaarke Service Account Role

1. If role doesn't exist, click **New** to create:
   - Role Name: `Spaarke Service Account`
   - Business Unit: Root business unit
2. Navigate to **Custom Entities** tab
3. Configure same as Administrator role (Organization level for all permissions)
4. Click **Save and Close**

### Step 5: Assign Service Account Role to Application User

1. Navigate to: **Settings > Security > Users**
2. Change view to **Application Users**
3. Find the BFF API application user (ID: `1e40baad-e065-4aea-a8d4-4b7ab273458c`)
4. Click **Manage Roles**
5. Ensure **Spaarke Service Account** role is checked
6. Click **OK**

## Verification Steps

### Test Standard User Permissions

```
1. Sign in as a standard Spaarke user
2. Create an EmailArtifact record via API or directly
   → Should succeed (Organization Create)
3. Query EmailArtifacts
   → Should only see own records (User Read)
4. Try to view another user's EmailArtifact
   → Should fail (Access Denied)
5. Create a ProcessingJob
   → Should succeed
6. Try to update the ProcessingJob status
   → Should fail (No Write permission)
```

### Test Administrator Permissions

```
1. Sign in as a Spaarke Administrator
2. Query all EmailArtifacts
   → Should see all records (Organization Read)
3. Modify any EmailArtifact
   → Should succeed (Organization Write)
4. Delete any ProcessingJob
   → Should succeed (Organization Delete)
```

### Test Service Account Permissions

```
1. Use the BFF API (authenticated as service account)
2. Create EmailArtifact on behalf of a user
   → Should succeed
3. Update ProcessingJob status
   → Should succeed
4. Query all ProcessingJobs
   → Should see all records
```

### PowerShell Verification Script

```powershell
# Connect to Dataverse
$conn = Get-CrmConnection -InteractiveMode

# Get Spaarke Standard User role privileges for EmailArtifact
$roleId = (Get-CrmRecords -conn $conn -EntityLogicalName role -FilterAttribute name -FilterOperator eq -FilterValue "Spaarke Standard User").CrmRecords[0].roleid
$privileges = Get-CrmRecordsByFetch -conn $conn -Fetch @"
<fetch>
  <entity name="roleprivileges">
    <attribute name="privilegeid" />
    <attribute name="privilegedepthmask" />
    <filter>
      <condition attribute="roleid" operator="eq" value="$roleId" />
    </filter>
    <link-entity name="privilege" from="privilegeid" to="privilegeid">
      <attribute name="name" />
      <filter>
        <condition attribute="name" operator="like" value="%sprk_emailartifact%" />
      </filter>
    </link-entity>
  </entity>
</fetch>
"@

$privileges.CrmRecords | Format-Table
```

## Depth Mask Reference

| Depth | Mask Value | Meaning | Icon |
|-------|------------|---------|------|
| None | 0 | No access | Empty |
| User | 1 | User's own records | 1 yellow circle |
| Business Unit | 2 | Same business unit | 2 yellow circles |
| Parent: Child BU | 4 | Parent and child BUs | 3 yellow circles |
| Organization | 8 | All records | 4 green circles |

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| User can see other's records | Read set to BU/Org instead of User | Set Read to User level (1 click) |
| User cannot create records | Create not set or wrong level | Set Create to Organization (4 clicks) |
| Service account access denied | Role not assigned to app user | Assign role via Manage Roles |
| ProcessingJob update fails | Wrong user context | Ensure service account token is used |

## Related Documentation

- [schema-emailartifact.md](schema-emailartifact.md) - EmailArtifact table schema
- [schema-attachmentartifact.md](schema-attachmentartifact.md) - AttachmentArtifact table schema
- [schema-processingjob.md](schema-processingjob.md) - ProcessingJob table schema
- [schema-relationships.md](schema-relationships.md) - Table relationships

## Acceptance Criteria

- [ ] Spaarke Standard User role updated with Office tables (User level access)
- [ ] Spaarke Administrator role updated with Office tables (Organization level)
- [ ] Spaarke Service Account role has full Organization access
- [ ] Application user (BFF API) has Service Account role assigned
- [ ] Standard users can only read their own EmailArtifacts
- [ ] Admins can read all EmailArtifacts
- [ ] Service account can update ProcessingJobs

---

*Execute these steps in Power Apps Maker Portal after completing Task 013 (relationships).*
