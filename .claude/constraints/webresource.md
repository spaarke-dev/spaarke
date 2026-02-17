# Web Resource Constraints

> **Domain**: Dataverse JavaScript Web Resources
> **Source ADRs**: ADR-006 (PCF preferred), ADR-008 (endpoint auth)
> **Last Updated**: 2026-02-16

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

### Authentication — Web Resource → API Calls

- ✅ **MUST** use `.AllowAnonymous()` on API endpoints called from web resources
- ✅ **MUST** add `RequireRateLimiting()` as compensating control for anonymous endpoints
- ✅ **MUST** document anonymous access with TODO for production hardening (API key or service-to-service auth)

**Why**: Dataverse web resources run in the browser context but cannot acquire Azure AD tokens for external APIs. There is no mechanism to obtain a Bearer token from JavaScript running on a Dataverse form. `fetch()` calls to protected endpoints will always return 401.

```csharp
// ✅ CORRECT: AllowAnonymous + rate limiting for web resource callers
var group = app.MapGroup("/api/matters")
    .WithTags("Scorecard")
    .RequireRateLimiting("dataverse-query");

group.MapPost("/{id:guid}/recalculate", Handler)
    .AllowAnonymous();  // Web resources cannot acquire tokens
```

```csharp
// ❌ WRONG: RequireAuthorization blocks all web resource calls
var group = app.MapGroup("/api/matters")
    .RequireAuthorization();  // Returns 401 from web resource fetch()
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

- ❌ **MUST NOT** use `RequireAuthorization()` on endpoints called from web resources
- ❌ **MUST NOT** assume web resources can pass Bearer tokens in `fetch()` headers
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
