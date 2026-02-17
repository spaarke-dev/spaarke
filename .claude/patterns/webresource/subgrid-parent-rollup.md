# Subgrid Parent Rollup Pattern

> **When to use**: When child records in a subgrid should trigger recalculation of parent record fields (e.g., KPI rollups, totals, status aggregation).

## Architecture

```
Parent Form (Matter, Project, etc.)
  ├── OnLoad: Register subgrid listener
  │
  ├── Subgrid (child records)
  │     └── addOnLoad fires when row count changes
  │
  └── On subgrid change:
        1. Call BFF API to recalculate parent fields
        2. Wait for Dataverse to commit
        3. formContext.data.refresh(false) to reload field values
```

**Why this pattern?**
- Quick Create flyouts in UCI cannot refresh the parent form (same window context)
- `window.parent.Xrm.Page` does not reference the parent form in UCI flyouts
- The ONLY reliable way to refresh parent form fields is from the parent form's own context
- Subgrid `addOnLoad` fires automatically after Quick Create save

## Generic Web Resource

**File**: `src/solutions/webresources/sprk_subgrid_parent_rollup.js`

```javascript
/* eslint-disable no-undef */
"use strict";

var Spaarke = Spaarke || {};
Spaarke.SubgridRollup = Spaarke.SubgridRollup || {};

/**
 * Generic subgrid parent rollup handler.
 *
 * Register on ANY parent form OnLoad. Pass a JSON config string as
 * the event handler's optional parameter to configure behavior.
 *
 * Config parameter format (JSON string):
 * {
 *   "subgridName": "subgrid_kpiassessments",
 *   "apiPathTemplate": "/api/{entityPath}/{entityId}/recalculate-grades",
 *   "entityPathMap": {
 *     "sprk_matter": "matters",
 *     "sprk_project": "projects"
 *   },
 *   "refreshDelayMs": 1500
 * }
 *
 * Registration:
 *   Event: OnLoad
 *   Function: Spaarke.SubgridRollup.onLoad
 *   Pass execution context: Yes
 *   Parameters: <JSON config string above>
 */

Spaarke.SubgridRollup._instances = {};

Spaarke.SubgridRollup._getApiBaseUrl = function () {
    try {
        var clientUrl = Xrm.Utility.getGlobalContext().getClientUrl();
        if (clientUrl.indexOf("spaarkedev1.crm.dynamics.com") !== -1)
            return "https://spe-api-dev-67e2xz.azurewebsites.net";
        if (clientUrl.indexOf("spaarkeuat.crm.dynamics.com") !== -1)
            return "https://spaarke-bff-uat.azurewebsites.net";
        if (clientUrl.indexOf("spaarkeprod.crm.dynamics.com") !== -1)
            return "https://spaarke-bff-prod.azurewebsites.net";
        return "https://localhost:5001";
    } catch (e) {
        return "https://spe-api-dev-67e2xz.azurewebsites.net";
    }
};

Spaarke.SubgridRollup.onLoad = function (executionContext, configJson) {
    try {
        var formContext = executionContext.getFormContext();
        var config = JSON.parse(configJson || "{}");

        // Defaults
        config.refreshDelayMs = config.refreshDelayMs || 1500;
        config.entityPathMap = config.entityPathMap || {};

        // Resolve entity path for API URL
        var entityName = formContext.data.entity.getEntityName();
        var entityPath = config.entityPathMap[entityName] || entityName;

        // Instance state (supports multiple subgrids on one form)
        var instanceKey = config.subgridName;
        Spaarke.SubgridRollup._instances[instanceKey] = {
            lastRowCount: -1,
            refreshTimer: null,
            config: config,
            entityPath: entityPath,
            apiBaseUrl: Spaarke.SubgridRollup._getApiBaseUrl()
        };

        // Wait for subgrid, then attach listener
        Spaarke.SubgridRollup._waitForSubgrid(formContext, instanceKey, 0);

        console.log("[SubgridRollup:" + instanceKey + "] Initialized for " + entityName);
    } catch (error) {
        console.error("[SubgridRollup] Error in onLoad:", error);
    }
};

Spaarke.SubgridRollup._waitForSubgrid = function (formContext, key, attempt) {
    var inst = Spaarke.SubgridRollup._instances[key];
    var subgrid = formContext.getControl(inst.config.subgridName);

    if (subgrid) {
        try {
            var grid = subgrid.getGrid();
            if (grid) inst.lastRowCount = grid.getTotalRecordCount();
        } catch (e) { /* grid not loaded yet */ }

        subgrid.addOnLoad(function () {
            Spaarke.SubgridRollup._onSubgridChange(formContext, subgrid, key);
        });
        console.log("[SubgridRollup:" + key + "] Listener attached. Rows: " + inst.lastRowCount);
        return;
    }

    if (attempt < 10) {
        setTimeout(function () {
            Spaarke.SubgridRollup._waitForSubgrid(formContext, key, attempt + 1);
        }, 500);
    }
};

Spaarke.SubgridRollup._onSubgridChange = function (formContext, subgrid, key) {
    var inst = Spaarke.SubgridRollup._instances[key];
    try {
        var count = subgrid.getGrid().getTotalRecordCount();
        if (count !== inst.lastRowCount && count >= 0) {
            console.log("[SubgridRollup:" + key + "] Rows: " +
                inst.lastRowCount + " → " + count);
            inst.lastRowCount = count;

            var entityId = formContext.data.entity.getId().replace(/[{}]/g, "");
            Spaarke.SubgridRollup._callApiAndRefresh(formContext, key, entityId);
        }
    } catch (e) { /* ignore */ }
};

Spaarke.SubgridRollup._callApiAndRefresh = function (formContext, key, entityId) {
    var inst = Spaarke.SubgridRollup._instances[key];

    if (inst.refreshTimer) {
        clearTimeout(inst.refreshTimer);
        inst.refreshTimer = null;
    }

    var apiUrl = inst.apiBaseUrl +
        inst.config.apiPathTemplate
            .replace("{entityPath}", inst.entityPath)
            .replace("{entityId}", entityId);

    console.log("[SubgridRollup:" + key + "] POST " + apiUrl);

    fetch(apiUrl, {
        method: "POST",
        headers: { "Content-Type": "application/json", "Accept": "application/json" }
    }).then(function (response) {
        if (response.ok) {
            inst.refreshTimer = setTimeout(function () {
                inst.refreshTimer = null;
                formContext.data.refresh(false).then(
                    function () { console.log("[SubgridRollup:" + key + "] Refreshed."); },
                    function (err) { console.warn("[SubgridRollup:" + key + "] Refresh failed:", err); }
                );
            }, inst.config.refreshDelayMs);
        } else {
            console.warn("[SubgridRollup:" + key + "] API returned " + response.status);
        }
    }).catch(function (err) {
        console.warn("[SubgridRollup:" + key + "] API call failed:", err);
    });
};
```

## Registration Examples

### KPI Assessments → Matter/Project Grade Rollup

**Form**: Matter main form (and Project main form)
**Event**: OnLoad
**Function**: `Spaarke.SubgridRollup.onLoad`
**Pass execution context**: Yes
**Parameters**:
```json
{"subgridName":"subgrid_kpiassessments","apiPathTemplate":"/api/{entityPath}/{entityId}/recalculate-grades","entityPathMap":{"sprk_matter":"matters","sprk_project":"projects"},"refreshDelayMs":1500}
```

### Billing Entries → Matter Total Rollup (Hypothetical)

**Form**: Matter main form
**Event**: OnLoad
**Function**: `Spaarke.SubgridRollup.onLoad`
**Parameters**:
```json
{"subgridName":"subgrid_billingentries","apiPathTemplate":"/api/{entityPath}/{entityId}/recalculate-totals","entityPathMap":{"sprk_matter":"matters"},"refreshDelayMs":2000}
```

### Multiple Subgrids on One Form

Register the OnLoad handler **twice** with different config parameters. The instance
key (subgridName) keeps them isolated:

```
Handler 1: Spaarke.SubgridRollup.onLoad → KPI config JSON
Handler 2: Spaarke.SubgridRollup.onLoad → Billing config JSON
```

## Anti-Patterns

### DON'T: Try to refresh parent form from Quick Create

```javascript
// ❌ WRONG: Quick Create runs in same UCI window - parent.Xrm.Page
// refers to the Quick Create form, not the parent entity form
window.parent.Xrm.Page.data.refresh(false);
```

### DON'T: Fire-and-forget API without waiting for Dataverse commit

```javascript
// ❌ WRONG: Refresh fires immediately but Dataverse hasn't committed yet
fetch(apiUrl, { method: "POST" });
formContext.data.refresh(false); // reads stale values
```

### DON'T: Skip row count check (causes infinite refresh loops)

```javascript
// ❌ WRONG: Subgrid OnLoad fires on ANY refresh, including our own
// formContext.data.refresh(). Without the row count guard, this loops.
subgrid.addOnLoad(function () {
    fetch(apiUrl).then(function () {
        formContext.data.refresh(false); // triggers subgrid OnLoad again!
    });
});
```

## BFF API Endpoint Pattern

The API endpoint should:
1. Query child records from Dataverse
2. Calculate aggregated values
3. Update parent record fields in Dataverse
4. Return the calculated values in the response

```csharp
// Endpoint registration (ADR-001: Minimal API)
matterGroup.MapPost("/{matterId:guid}/recalculate-grades", RecalculateAsync)
    .AllowAnonymous()  // Web resources can't acquire Azure AD tokens
    .RequireRateLimiting("dataverse-query");  // Abuse protection

// Service method
public async Task<Response> RecalculateAsync(Guid parentId, CancellationToken ct)
{
    // 1. Query child records per dimension (parallel for performance)
    var task1 = _dataverse.QueryChildRecordsAsync(parentId, Dimension.A, ct);
    var task2 = _dataverse.QueryChildRecordsAsync(parentId, Dimension.B, ct);
    await Task.WhenAll(task1, task2);

    // 2. Calculate aggregates
    var currentA = GetLatest(task1.Result);
    var averageA = GetAverage(task1.Result);

    // 3. Persist to parent record
    await _dataverse.UpdateRecordFieldsAsync(entityName, parentId, fields, ct);

    // 4. Return response
    return new Response { CurrentA = currentA, AverageA = averageA };
}
```

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Main form listener (not Quick Create) | UCI Quick Create cannot refresh parent form |
| Row count guard | Prevents infinite refresh loops |
| Debounced API calls | Handles rapid subgrid events |
| Configurable via JSON parameter | Single web resource for all rollup scenarios |
| `.AllowAnonymous()` on API | Web resources can't acquire Azure AD tokens |
| `RequireRateLimiting` on API | Compensates for anonymous access |
| 1.5s delay before refresh | Gives Dataverse time to commit updated values |
| Instance-keyed state | Supports multiple subgrids on one form |

## Future: Background Processing (No JS Required)

For non-Power App clients, use a **Dataverse Webhook → BFF API** approach:

1. Register a Service Endpoint in Dataverse pointing to BFF API
2. Register a Webhook Step on child entity Create/Update/Delete
3. BFF API receives the webhook payload, extracts parent ID, recalculates
4. Works for any client: API, Power Automate, imports, custom apps

This eliminates the need for any client-side JavaScript.
