# Dataverse Environments & Registration Provisioning — Design Specification

> **Status**: Draft
> **Created**: 2026-04-06
> **Author**: Ralph Schroeder / Claude Code
> **Project**: Environment Provisioning App

---

## Executive Summary

Create a reusable **Dataverse Environments** reference table (`sprk_dataverseenvironment`) that stores configuration for all Spaarke Dataverse environments. This is a core platform entity — not specific to registration — that any feature needing to target a Dataverse environment can reference (customer management, provisioning, deployment, etc.).

The self-service registration system is the first consumer: a standard lookup field on the registration request form lets the admin select the target environment before approving, replacing the current hardcoded environment picker in ribbon JS and Azure App Service config.

---

## Problem Statement

**Current state**: Provisioning environments are configured as JSON in Azure App Service app settings and duplicated as a hardcoded list in the ribbon JS webresource. There is no central registry of Dataverse environments in the platform. Adding or modifying an environment requires developer intervention.

**Desired state**: A `Dataverse Environments` table in the MDA serves as the single source of truth for all environment configuration. Admins manage environments from standard Dataverse forms. The registration request form has a simple lookup field — no custom JS dialogs, no hardcoded lists, no Azure config changes.

---

## Scope

### In Scope

- New Dataverse entity: `sprk_dataverseenvironment` (reusable platform entity)
- Form and views in the MDA for environment management
- Lookup field on `sprk_registrationrequest` → `sprk_dataverseenvironment`
- Default environment auto-populated on new registration requests
- BFF API reads environment config from Dataverse lookup at approval time
- Ribbon JS simplified — removes environment picker dialog, reads lookup from form
- Migration of existing Dev environment config into the new entity

### Out of Scope

- Automated Azure resource provisioning (Entra groups, Conditional Access, SPE containers)
- Multi-tenant support
- Environment health monitoring or connectivity testing
- Registration form changes (public website form is environment-agnostic)

---

## Data Model

### Entity: `sprk_dataverseenvironment`

**Display Name**: Dataverse Environment
**Plural**: Dataverse Environments
**Ownership**: Organization-owned
**Primary Name**: `sprk_name`

This is a **platform-level reference entity** — it will be used by registration provisioning now and by customer management, deployment tooling, and other features in the future.

#### Fields

| Schema Name | Display Name | Type | Required | Notes |
|-------------|-------------|------|----------|-------|
| `sprk_name` | Name | Text (200) | Yes | Display name: "Demo 1", "Dev", "Partner Trial" |
| `sprk_environmenttype` | Environment Type | Choice | Yes | See choice values below |
| `sprk_dataverseurl` | Dataverse URL | URL | Yes | e.g., `https://spaarke-demo.crm.dynamics.com` |
| `sprk_appid` | App ID | Text (100) | No | MDA app GUID for deep links |
| `sprk_description` | Description | Multiline (2000) | No | Admin notes about this environment |
| `sprk_isactive` | Active | Boolean | Yes | Default: Yes |
| `sprk_isdefault` | Default Environment | Boolean | Yes | Default: No. Only one should be true at a time |
| `sprk_setupstatus` | Setup Status | Choice | No | Not Started / In Progress / Ready / Issue |

#### Provisioning Configuration Fields

These fields are used by the registration provisioning system. Other consumers of this entity may not need them, but they live on the same entity to keep environment config centralized.

| Schema Name | Display Name | Type | Required | Notes |
|-------------|-------------|------|----------|-------|
| `sprk_accountdomain` | Account Domain | Text (200) | No | UPN domain for provisioned users: `demo.spaarke.com` |
| `sprk_businessunitname` | Business Unit | Text (200) | No | Target Dataverse business unit |
| `sprk_teamname` | Security Team | Text (200) | No | Team with inherited security role |
| `sprk_specontainerid` | SPE Container ID | Text (500) | No | SharePoint Embedded container for doc access |
| `sprk_securitygroupid` | Users Security Group | Text (100) | No | Entra ID security group (MFA exclusion) |
| `sprk_defaultdurationdays` | Default Duration (Days) | Integer | No | Default provisioning access duration. Default: 14 |
| `sprk_licenseconfigjson` | License Configuration | Multiline (4000) | No | JSON: `{"PowerAppsPlan2TrialSkuId":"...","FabricFreeSkuId":"...","PowerAutomateFreeSkuId":"..."}` |
| `sprk_adminemails` | Admin Notification Emails | Multiline (1000) | No | Comma-separated admin emails for notifications |

#### Environment Type Choice Values

| Value | Label | Description |
|-------|-------|-------------|
| 0 | Development | Internal development environment |
| 1 | Demo | Demo access for prospective customers |
| 2 | Sandbox | Internal sandbox for testing |
| 3 | Trial | Time-limited trial access |
| 4 | Partner | Partner/reseller access |
| 5 | Training | Training environment |
| 6 | Production | Production customer environment |

#### Setup Status Choice Values

| Value | Label | Description |
|-------|-------|-------------|
| 0 | Not Started | Environment record created, Azure resources not configured |
| 1 | In Progress | Azure resources being configured |
| 2 | Ready | Fully configured and tested, ready for use |
| 3 | Issue | Configuration problem, not available |

---

### Registration Request Changes

#### New Lookup Field

| Schema Name | Display Name | Type | Notes |
|-------------|-------------|------|-------|
| `sprk_dataverseenvironmentid` | Target Environment | Lookup → `sprk_dataverseenvironment` | Admin selects before approving |

This **replaces** the current `sprk_environment` text field.

#### Default Value Behavior

When a new registration request is created (via the BFF API submit endpoint), the lookup is auto-populated with the environment record where `sprk_isdefault = true`. The admin can change it on the form before clicking Approve.

#### Form Layout

Add the **Target Environment** lookup to the registration request form in a visible location (e.g., header or top of the General tab). The admin sees the environment name and can click into it for full details.

---

## Approval Flow (Simplified)

```
1. User submits request via website
2. BFF API creates registration request in Dataverse
   → Auto-sets Target Environment lookup to default environment
3. Admin opens registration request in MDA
   → Sees Target Environment field (can change if needed)
4. Admin clicks "Approve Demo Access" ribbon button
5. Ribbon JS reads Target Environment lookup from form
   → Passes environmentId (GUID) to BFF API
6. BFF API reads full environment config from Dataverse
   → Uses config for provisioning (domain, licenses, team, SPE, etc.)
7. User provisioned into the selected environment
```

No custom environment picker dialog. No hardcoded environment lists. Standard Dataverse form behavior.

---

## API Changes

### Modified: `POST /api/registration/requests/{id}/approve`

**Current**: Accepts `{ "environment": "Dev" }` (name string, matched against appsettings)
**New**: Accepts `{ "environmentId": "guid" }` (Dataverse environment record ID)

If `environmentId` is not provided, the API reads the `sprk_dataverseenvironmentid` lookup from the registration request record. If that's also empty, uses the environment where `sprk_isdefault = true`.

**Resolution order**:
1. `environmentId` from request body (explicit override)
2. `sprk_dataverseenvironmentid` from the registration request record (admin set on form)
3. Default environment (`sprk_isdefault = true`)

The API reads the full environment config from Dataverse at approval time (not cached), ensuring config changes are picked up immediately.

### New Service: `DataverseEnvironmentService`

Reads environment records from Dataverse via Web API:

```csharp
public sealed class DataverseEnvironmentService
{
    /// Returns environment config by Dataverse record ID.
    Task<EnvironmentConfig?> GetByIdAsync(Guid environmentId, CancellationToken ct);

    /// Returns the default environment (sprk_isdefault = true, sprk_isactive = true).
    Task<EnvironmentConfig?> GetDefaultAsync(CancellationToken ct);
}
```

### Removed: `DemoProvisioningOptions.Environments`

The `Environments` array and `DefaultEnvironment` property are removed from appsettings. All environment config moves to Dataverse records.

**What stays in appsettings**: Only tenant-level config that doesn't vary per environment:
- Entra tenant ID, client ID, client secret (auth)
- Graph API permissions (shared across environments)

---

## Ribbon JS Changes (Simplified)

The environment picker dialog (`_sprkReg_promptForEnvironment`) is **removed entirely**. The ribbon JS becomes simpler:

```javascript
// Form approve — read environment from form lookup
async function approveRequestFromForm(formContext) {
    var envLookup = formContext.getAttribute("sprk_dataverseenvironmentid");
    var environmentId = null;
    var environmentName = "(default)";

    if (envLookup && envLookup.getValue()) {
        var lookupValue = envLookup.getValue()[0];
        environmentId = lookupValue.id.replace(/[{}]/g, "");
        environmentName = lookupValue.name;
    }

    // Confirm with admin
    var confirmResult = await Xrm.Navigation.openConfirmDialog({
        title: "Approve Demo Access",
        text: "Approve demo access in " + environmentName + "?\n\nThis will provision their demo account."
    });

    if (!confirmResult.confirmed) return;

    // Call API with environmentId from form lookup
    var result = await _sprkReg_callBffApi(
        "POST",
        "/api/registration/requests/" + recordId + "/approve",
        environmentId ? { environmentId: environmentId } : {}
    );
}
```

**Grid approve** — for bulk operations from the grid view, the API falls back to each record's lookup value or the default environment. No environment picker needed.

**Removed**:
- `_sprkReg_promptForEnvironment()` function
- `SPRK_REG_CONFIG.environments` hardcoded array
- Environment picker DOM dialog

---

## MDA Configuration

### Form: Dataverse Environment — Main Form

**Header**: Name, Environment Type, Active, Default, Setup Status

**Tab 1: General**
- Section: Identity — Name, Environment Type, Description
- Section: Status — Active, Default, Setup Status

**Tab 2: Connection**
- Section: Dataverse — Dataverse URL, App ID

**Tab 3: Provisioning Configuration**
- Section: User Accounts — Account Domain, Default Duration Days, Users Security Group
- Section: Dataverse Assignment — Business Unit, Security Team
- Section: Licenses — License Configuration JSON
- Section: Documents — SPE Container ID

**Tab 4: Notifications**
- Section: Admins — Admin Notification Emails

**Tab 5: Registration Requests** (subgrid)
- Related registration requests provisioned into this environment

### Views

| View | Columns | Filter |
|------|---------|--------|
| Active Environments | Name, Type, Dataverse URL, Status, Default | Active = Yes |
| All Environments | Name, Type, Active, Status, Default | None |
| Ready for Provisioning | Name, Type, Dataverse URL, Default | Active = Yes AND Status = Ready |

### Sitemap

Add to the MDA sitemap under an "Administration" area:
- Dataverse Environments (new)
- Registration Requests (existing — move here or keep in current location)

### Registration Request Form Update

Add `Target Environment` lookup field:
- Position: header or top of General tab (highly visible)
- Filtered to: Active = Yes, Setup Status = Ready
- Default: auto-populated on create

---

## Migration Plan

### Step 1: Create Entity and Fields

Run schema creation script against dev (and later demo) Dataverse:
- Create `sprk_dataverseenvironment` entity with all fields
- Create lookup field on `sprk_registrationrequest`
- Create form, views, sitemap entry

### Step 2: Seed Data

Create initial environment records from current appsettings:

**Dev environment record**:
| Field | Value |
|-------|-------|
| Name | Dev |
| Environment Type | Development |
| Dataverse URL | `https://spaarkedev1.crm.dynamics.com` |
| App ID | `729afe6d-ca73-f011-b4cb-6045bdd8b757` |
| Account Domain | `demo.spaarke.com` |
| Business Unit | Spaarke Demo |
| Security Team | Spaarke Demo Team |
| SPE Container ID | `b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50` |
| Users Security Group | `745bfdf6-f899-4507-935d-c52de3621536` |
| Default Duration | 14 |
| License Config | `{"PowerAppsPlan2TrialSkuId":"dcb1a3ae-...","FabricFreeSkuId":"a403ebcc-...","PowerAutomateFreeSkuId":"f30db892-..."}` |
| Active | Yes |
| Default | Yes |
| Setup Status | Ready |
| Admin Emails | `ralph.schroeder@spaarke.com` |

**Demo 1 environment record** (when ready):
| Field | Value |
|-------|-------|
| Name | Demo 1 |
| Environment Type | Demo |
| Dataverse URL | `https://spaarke-demo.crm.dynamics.com` |
| App ID | `d8d06682-b152-420d-835c-2da0357cd7fe` |
| Account Domain | `demo.spaarke.com` |
| Setup Status | Not Started |

### Step 3: Refactor BFF API

1. Create `DataverseEnvironmentService` — reads environment config from Dataverse
2. Update `DemoProvisioningService` to accept environment config from Dataverse record
3. Update `POST /api/registration/requests/{id}/approve` to resolve environment from lookup/body/default
4. Auto-set default environment lookup on new registration requests
5. Remove `DemoProvisioning.Environments` from appsettings

### Step 4: Simplify Ribbon JS

1. Remove `_sprkReg_promptForEnvironment()` and environment picker dialog
2. Remove `SPRK_REG_CONFIG.environments` hardcoded array
3. Read `sprk_dataverseenvironmentid` lookup from form context
4. Pass `environmentId` to approve API

### Step 5: Remove Legacy Config

1. Remove `DemoProvisioning__Environments__*` app settings from Azure
2. Remove `DemoProvisioningOptions.Environments` and related config classes
3. Update ops guide and Azure setup guide

---

## Security Considerations

- Environment records contain sensitive data (security group IDs, SPE container IDs) — restrict entity access to admin security role only
- License SKU IDs are not secrets but should not be exposed to non-admins
- The registration request form shows only the environment name (via lookup) — not the full config
- Admin emails should be validated before saving

---

## Testing Plan

- [ ] Create environment record in Dataverse — verify form layout
- [ ] New registration request — verify default environment auto-populated
- [ ] Admin changes environment on form — verify lookup works
- [ ] Approve request — verify BFF reads config from Dataverse lookup
- [ ] Approve without environment set — verify fallback to default
- [ ] Deactivate environment — verify it no longer appears in lookup filter
- [ ] Set new default — verify new requests get new default
- [ ] Invalid environmentId in API — verify 400 error
- [ ] No active environments — verify appropriate error
- [ ] Provision into different environments — verify records link correctly
- [ ] Grid approve (bulk) — verify each record uses its own lookup value

---

## Estimated Effort

| Component | Estimate |
|-----------|----------|
| Dataverse entity + form/views/sitemap | 2-3 hours |
| BFF API refactoring (service + endpoint) | 3-4 hours |
| Ribbon JS simplification | 1 hour |
| Migration + seed data | 1-2 hours |
| Testing | 2-3 hours |
| Documentation updates | 1 hour |
| **Total** | **10-14 hours** |

---

## Dependencies

- Dataverse Web API access for reading environment config at runtime (existing S2S auth)
- Admin must manually set up Azure resources per environment before marking "Ready"

---

## Future Consumers of `sprk_dataverseenvironment`

This entity is designed to be reusable across the platform:

| Future Feature | How It Uses Environments |
|---------------|--------------------------|
| **Customer Management** | Track which environment each customer is deployed to |
| **Deployment Tooling** | Target environment for solution deployments |
| **Health Monitoring** | Monitor environment availability and performance |
| **License Management** | Track license allocation per environment |
| **Support Tickets** | Associate support requests with specific environments |

The provisioning-specific fields (account domain, security group, licenses, etc.) are optional — other consumers use only the core fields (name, URL, type, active).

---

## Future Enhancements (Not in This Project)

- **Automated Azure resource provisioning**: Script or wizard that creates Entra security group, Conditional Access policy, business unit, team, and SPE container for a new environment
- **Environment health checks**: Periodic validation that Dataverse URL, security group, SPE container are reachable
- **Capacity tracking**: Track provisioned user count per environment, enforce limits
- **Environment cloning**: Copy configuration from one environment to create another
- **Self-service environment request**: Internal teams request new environments via a form
- **Environment dashboard**: PCF or Code Page showing environment status, user counts, expiration timeline

---

*This design introduces a reusable Dataverse Environments entity that serves as the platform's central environment registry. The self-service registration system is the first consumer, using a standard lookup field to connect registration requests to target environments — eliminating custom JS dialogs, hardcoded lists, and Azure config dependencies.*
