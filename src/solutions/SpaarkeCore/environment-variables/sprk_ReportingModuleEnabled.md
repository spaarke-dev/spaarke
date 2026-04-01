# sprk_ReportingModuleEnabled Environment Variable

> **Purpose**: Feature gate that controls visibility and access to the Reporting module.
> **Project**: spaarke-powerbi-embedded-r1
> **Created**: 2026-03-31

## Definition

| Property | Value |
|----------|-------|
| **Schema Name** | sprk_ReportingModuleEnabled |
| **Display Name** | Reporting Module Enabled |
| **Type** | Boolean (Yes/No) |
| **Default Value** | No (false) |
| **Description** | Controls visibility and access to the Reporting module. When disabled, all /api/reporting/* BFF endpoints return 403 and the Reporting navigation item is hidden. |
| **Solution** | SpaarkeCore |

## Purpose and Behavior

This environment variable acts as a top-level feature gate for the entire Reporting module.
It allows the module to be deployed as part of the SpaarkeCore solution without being immediately
visible to users in environments where Power BI Embedded has not yet been configured.

| Value | Effect |
|-------|--------|
| **No (false)** — default | Module is disabled. BFF returns `403 Forbidden` for all `/api/reporting/*` endpoints. Navigation item is hidden in the Reporting Code Page. |
| **Yes (true)** | Module is enabled. BFF serves embed tokens; navigation item is visible to users with `sprk_ReportingAccess` role. |

> Setting this to `Yes` does NOT bypass the `sprk_ReportingAccess` security role check.
> Both gates must pass for a user to access reports: the env var must be enabled AND the
> user must have the appropriate role tier.

## How the BFF API Reads This Variable

The `ReportingAuthorizationFilter` reads this environment variable on each request to
`/api/reporting/*` endpoints using the Dataverse SDK:

```csharp
// Pseudocode — see ReportingAuthorizationFilter.cs for full implementation
var envVar = await _dataverseService.GetEnvironmentVariableAsync(
    "sprk_ReportingModuleEnabled",
    cancellationToken);

if (envVar?.Value is not "true" and not "yes" and not "1")
{
    context.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
    context.Result = new ForbidResult();
    return;
}
```

The variable is cached in Redis (ADR-009) with a short TTL (e.g., 5 minutes) to avoid
hitting Dataverse on every API request while still reflecting changes quickly.

## Per-Environment Toggle

Each Dataverse environment has its own value for this variable. The SpaarkeCore solution
ships the variable definition with the default value of `No`. Administrators must explicitly
set it to `Yes` in each environment where the module should be active.

### Toggle via Power Apps Maker Portal

1. Open [make.powerapps.com](https://make.powerapps.com) and select the target environment
2. Navigate to **Solutions** → open **SpaarkeCore** (or the managed solution)
3. Find **Environment Variables** in the solution objects list
4. Locate **Reporting Module Enabled** (`sprk_ReportingModuleEnabled`)
5. Click the variable to open it
6. Under **Current Value**, add or update the value to **Yes**
7. Save

### Toggle via Power Platform Admin Center

1. Open [admin.powerplatform.microsoft.com](https://admin.powerplatform.microsoft.com)
2. Navigate to **Environments** → select the target environment
3. Under **Resources**, select **Environment variables**
4. Search for `sprk_ReportingModuleEnabled`
5. Set the current value override to **Yes** or **No**

### Toggle via CLI (Power Platform CLI)

```powershell
# Enable the Reporting module in a specific environment
pac env update-variable `
  --name sprk_ReportingModuleEnabled `
  --value "true" `
  --environment https://spaarkedev1.crm.dynamics.com
```

### Toggle via Dataverse Web API

```http
PATCH https://spaarkedev1.crm.dynamics.com/api/data/v9.2/environmentvariablevalues(<value-id>)
Content-Type: application/json
Authorization: Bearer <token>

{
  "value": "true"
}
```

## Deployment Notes

- The variable definition (schema, display name, type, default) is included in the SpaarkeCore solution export
- The **default value** (`No`) ships with the solution — the module is off unless an administrator explicitly enables it
- **Current value overrides** are environment-specific and are NOT exported with the solution; they must be set manually in each environment
- For Dev/UAT environments, set to `Yes` after solution import to enable testing
- For Prod, leave as `No` until Power BI capacity and workspace configuration is complete

## Related

- Security role: `src/solutions/SpaarkeCore/security-roles/sprk_ReportingAccess.md`
- BFF filter: `src/server/api/Sprk.Bff.Api/Api/Reporting/ReportingAuthorizationFilter.cs`
- Spec constraint: "MUST gate module via `sprk_ReportingModuleEnabled` environment variable"

---

*Version: 1.0 | Project: spaarke-powerbi-embedded-r1 | Created: 2026-03-31*
