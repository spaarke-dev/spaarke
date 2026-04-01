# sprk_ReportingAccess Security Role

> **Purpose**: Control access to the Reporting module and the `sprk_report` entity catalog.
> **Project**: spaarke-powerbi-embedded-r1
> **Created**: 2026-03-31

## Overview

The `sprk_ReportingAccess` security role gates all Reporting module operations. End users do NOT
need a Power BI license — their access is controlled entirely through this Dataverse role.

This role provides three privilege tiers that map to common organizational access patterns:

| Tier | Intended Audience | What They Can Do |
|------|-------------------|-----------------|
| Viewer | All staff who consume reports | View embedded reports |
| Author | Power users, report admins | View and manage report catalog entries |
| Admin | IT / Platform admins | Full CRUD on the report catalog |

## Privilege Tiers

### Tier 1 — Viewer

Minimum privilege to access the Reporting module and view embedded reports.

| Entity | Privilege | Scope | Notes |
|--------|-----------|-------|-------|
| sprk_report | Read | Organization | Can read all active report catalog entries |

All other privileges on `sprk_report`: None.

> Viewers can see the report list and load embed tokens via the BFF API.
> They cannot create, modify, or delete catalog entries.

---

### Tier 2 — Author

For users who maintain the report catalog (add new reports, update metadata).

| Entity | Privilege | Scope | Notes |
|--------|-----------|-------|-------|
| sprk_report | Read | Organization | Same as Viewer |
| sprk_report | Write | Organization | Update existing catalog entries |
| sprk_report | Create | Organization | Add new reports to the catalog |

Delete privilege on `sprk_report`: None.

> Authors can update report names, descriptions, category assignments, and embed URLs.
> They cannot permanently remove catalog entries.

---

### Tier 3 — Admin

Full control over the report catalog. Assign only to platform administrators.

| Entity | Privilege | Scope | Notes |
|--------|-----------|-------|-------|
| sprk_report | Read | Organization | Full read access |
| sprk_report | Write | Organization | Full update access |
| sprk_report | Create | Organization | Full create access |
| sprk_report | Delete | Organization | Permanent removal of catalog entries |

> Admins have complete CRUD on `sprk_report`. Use with care — deleted catalog entries
> cannot be recovered without a backup.

## Privilege Matrix Summary

| Privilege | Viewer | Author | Admin |
|-----------|--------|--------|-------|
| Read (Org) | Yes | Yes | Yes |
| Write (Org) | No | Yes | Yes |
| Create (Org) | No | Yes | Yes |
| Delete (Org) | No | No | Yes |
| Append | No | No | No |
| Append To | No | No | No |
| Share | No | No | No |
| Assign | No | No | No |

> Append, Append To, Share, and Assign are not required for R1. These can be added later
> if cross-entity relationships are introduced.

## Implementation Notes

### Single Role, Three Tiers

Dataverse security roles are not hierarchical by default. To implement tiers:

**Option A — Three separate roles** (recommended for clear separation):
- `sprk_ReportingAccess_Viewer`
- `sprk_ReportingAccess_Author`
- `sprk_ReportingAccess_Admin`

**Option B — Role inheritance via base + additive roles**:
- Base: `sprk_ReportingAccess_Viewer` (Read only)
- Assign `sprk_ReportingAccess_Author` alongside Viewer role to grant Write + Create
- Assign `sprk_ReportingAccess_Admin` alongside Viewer role to grant full CRUD

For R1, **Option A** is preferred — it reduces assignment errors and makes the privilege
grant self-evident from the role name.

### No Power BI License Required

Access to the Reporting module is gated by this Dataverse role. The Power BI embed token is
generated server-side using the service principal (App Owns Data pattern). End users only
need a valid Dataverse session and this security role — no per-user PBI license is needed.

## Role Assignment — Administrator Guide

### Prerequisites

- System Administrator or System Customizer role in the target Dataverse environment
- The SpaarkeCore solution (containing this role definition) must be imported

### Assigning to Individual Users

1. Open the [Power Platform Admin Center](https://admin.powerplatform.microsoft.com)
2. Navigate to **Environments** → select your environment → **Settings**
3. Under **Users + permissions**, select **Users**
4. Find the user, open their record, click **Manage roles**
5. Select the appropriate tier role:
   - `sprk_ReportingAccess_Viewer` for read-only report consumers
   - `sprk_ReportingAccess_Author` for report catalog maintainers
   - `sprk_ReportingAccess_Admin` for platform administrators
6. Click **OK** to save

### Assigning to Teams (Recommended for Scale)

1. Open Power Platform Admin Center → **Environments** → **Settings** → **Users + permissions** → **Teams**
2. Create or select a team (e.g., "Reporting Viewers", "Reporting Admins")
3. Add users to the team
4. Assign the appropriate `sprk_ReportingAccess_*` role to the team
5. All team members inherit the role automatically

### Per-Environment Assignment

Each Dataverse environment (Dev, UAT, Prod) maintains its own role assignments.
The role definition ships in the SpaarkeCore solution; assignments must be made manually
or via a post-import configuration script.

```powershell
# Example: Assign Viewer role to a user via PAC CLI
pac admin assign-user `
  --environment https://spaarkedev1.crm.dynamics.com `
  --user user@contoso.com `
  --role "sprk_ReportingAccess_Viewer"
```

## Solution Packaging

This role is included in the **SpaarkeCore** Dataverse solution. When the solution is exported
and imported into a new environment, the role definition is carried with it. User/team assignments
are environment-specific and must be applied separately after import.

```bash
# Export solution containing the role
pac solution export --name SpaarkeCore --path ./exports --managed false

# Import into target environment
pac solution import --path ./exports/SpaarkeCore.zip \
  --environment https://spaarkedev1.crm.dynamics.com
```

## Related

- Entity schema: `src/solutions/SpaarkeCore/entities/sprk_report/entity-schema.md`
- Module gate: `src/solutions/SpaarkeCore/environment-variables/sprk_ReportingModuleEnabled.md`
- BFF authorization filter: `src/server/api/Sprk.Bff.Api/Api/Reporting/ReportingAuthorizationFilter.cs`

---

*Version: 1.0 | Project: spaarke-powerbi-embedded-r1 | Created: 2026-03-31*
