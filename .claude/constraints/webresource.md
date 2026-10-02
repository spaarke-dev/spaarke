# Web Resource Constraints

> **Domain**: Dataverse JavaScript Web Resources
> **Source ADRs**: ADR-006 (PCF preferred), ADR-008 (endpoint auth)
> **Last Updated**: 2026-10-01 (auth rules rewritten: `Spaarke.BffAuth` delegated tokens; `.AllowAnonymous()` retired)
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

---

## When to Load This File

Load when:
- Creating or modifying JavaScript web resources for Dataverse forms
- Building API endpoints that will be called from web resources
- Implementing parent-child rollup patterns (subgrid → parent form refresh)
- Registering form event handlers with parameters
- Reviewing web resource code

---

## MUST Rules

### General (ADR-006)

- ✅ **MUST** prefer PCF controls over web resources for UI components
- ✅ **MUST** only use web resources for form event handlers and ribbon commands (PCF cannot register these)
- ✅ **MUST** use `"use strict"` and `Spaarke.*` namespace for all web resources
- ✅ **MUST** wrap all event handlers in try/catch (never throw from form events)

### Authentication — Web Resource → API Calls (rewritten 2026-10-01, `unified-access-control-r2` session 27)

- ✅ **MUST** call the BFF through the shared helper **`Spaarke.BffAuth`** (`src/solutions/webresources/sprk_bff_auth.js`, task 030 / FR-17): `Spaarke.BffAuth.authenticatedFetch(url, options, apiBaseUrl)` or `Spaarke.BffAuth.getToken(apiBaseUrl)`. It acquires a **delegated** BFF token by MSAL SSO for the signed-in user. Load `sprk_bff_auth.js` before the calling library on the form or ribbon command.
- ✅ **MUST** keep every BFF endpoint authenticated (root CLAUDE.md §9: only `/healthz` and `/ping` are anonymous) **and** authorized per record where the endpoint touches a record (ADR-003 / ADR-008 endpoint filters).
- ✅ **MUST** treat a null token or a failed token acquisition as a failure (show a notice; never fall back to an anonymous call).

**Why this changed**: the previous rule said web resources *cannot* acquire Azure AD tokens and prescribed `.AllowAnonymous()` plus rate limiting. That premise is false since `Spaarke.BffAuth` exists, and the rule produced real holes: the scorecard recalculate endpoints were anonymous / auth-only IDORs (session-26 defect **C8**, task 130). Live users of the helper: `sprk_kpiassessment_quickcreate.js`, `sprk_kpi_subgrid_refresh.js`, `sprk_matter_kpi_refresh.js`.

```javascript
// ✅ CORRECT: delegated token via the shared helper; the endpoint stays authenticated + per-record authorized
var res = await Spaarke.BffAuth.authenticatedFetch(apiBaseUrl + "/api/matters/" + id + "/recalculate-grades",
    { method: "POST" }, apiBaseUrl);
```

```csharp
// ❌ WRONG (retired): anonymous endpoint for web-resource callers
group.MapPost("/{id:guid}/recalculate", Handler).AllowAnonymous();
```

### UCI Quick Create Forms

- ✅ **MUST** handle parent form refresh from the **parent form** (not the Quick Create form)
- ✅ **MUST** use subgrid `addOnLoad` listener on the parent main form to detect child record changes
- ✅ **MUST** include a row count guard to prevent infinite refresh loops

**Why**: In UCI (Unified Client Interface), Quick Create forms open as flyout panels in the same window context. `window.parent.Xrm.Page` refers to the Quick Create form itself, not the parent entity form. All parent refresh strategies fail from Quick Create context.

### Subgrid Listener Pattern

- ✅ **MUST** compare row count before/after to detect actual data changes
- ✅ **MUST** debounce API calls (cancel pending timers before scheduling new ones)
- ✅ **MUST** wait 1-2 seconds after API success before `formContext.data.refresh(false)` (Dataverse commit delay)
- ✅ **MUST** use `formContext.data.refresh(false)` (soft refresh), never `true` (save + refresh)

---

## MUST NOT Rules

### Authentication

- ❌ **MUST NOT** use `.AllowAnonymous()` on any endpoint called from a web resource (retired 2026-10-01 — see Authentication above)
- ❌ **MUST NOT** hand-roll MSAL or token caching in an individual web resource — use `Spaarke.BffAuth`
- ❌ **MUST NOT** use `credentials: "include"` expecting it to provide Azure AD tokens (cookies are not Azure AD tokens)

### UCI Quick Create

- ❌ **MUST NOT** call `window.parent.Xrm.Page.data.refresh()` from Quick Create forms (references Quick Create, not parent)
- ❌ **MUST NOT** call `window.top.Xrm.Page.data.refresh()` from Quick Create forms (same issue)
- ❌ **MUST NOT** use `Xrm.Navigation.openForm()` as a refresh strategy (heavy, poor UX)

### Subgrid Listeners

- ❌ **MUST NOT** skip the row count guard (causes infinite refresh loops)
- ❌ **MUST NOT** call `formContext.data.refresh()` without debouncing (rapid subgrid events)
- ❌ **MUST NOT** refresh immediately after API call (Dataverse needs ~1.5s to commit)

### Event Handler Parameters

- ❌ **MUST NOT** pass JSON strings as Dataverse event handler parameters
- ❌ **MUST NOT** rely on "Comma separated list of parameters" for structured data

**Why**: The Dataverse "Comma separated list of parameters" field splits values on ALL commas, including those inside JSON strings. A config like `{"a":"b","c":"d"}` becomes three separate parameters: `{"a":"b"`, `"c":"d"}`. Use entity-specific web resources with hardcoded configuration instead.

---

## Quick Reference Patterns

### Environment Detection (BFF API URL)

```javascript
Spaarke.MyModule._getApiBaseUrl = function () {
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
```

### Subgrid Listener (Row Count Guard)

```javascript
subgrid.addOnLoad(function () {
    var count = subgrid.getGrid().getTotalRecordCount();
    if (count !== lastRowCount && count >= 0) {
        lastRowCount = count;
        callApiAndRefresh(formContext, entityId);
    }
    // Skip if count unchanged — prevents infinite loop from formContext.data.refresh()
});
```

---

## Pattern Files (Complete Examples)

- [Subgrid Parent Rollup](../patterns/webresource/subgrid-parent-rollup.md) — Full parent-child rollup pattern with API call + form refresh
- [Custom Dialogs in Dataverse](../patterns/webresource/custom-dialogs-in-dataverse.md)

---

## Source ADRs (Full Context)

| ADR | Focus | When to Load |
|-----|-------|--------------|
| [ADR-006](../adr/ADR-006-pcf-over-webresources.md) | PCF preferred over web resources | Deciding between PCF and web resource |
| [ADR-008](../adr/ADR-008-endpoint-filters.md) | Endpoint authorization | Auth decisions for web resource-called APIs |

---

**Lines**: ~140
**Purpose**: Platform constraints for Dataverse web resources and their API integrations
