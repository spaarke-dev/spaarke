# Security Review - AI Node Playbook Builder

**Date**: 2026-01-13
**Reviewer**: Claude (automated)
**Scope**: Phase 5 - Production Hardening Security Review
**Task**: 048-security-review.poml

---

## Executive Summary

The security review of the AI Node Playbook Builder identified **1 medium-severity finding** and **1 low-severity finding**. The codebase follows security best practices in most areas including endpoint authorization, input validation, and error handling. The medium-severity finding should be addressed before production deployment.

| Severity | Count | Status |
|----------|-------|--------|
| Critical | 0 | - |
| High | 0 | - |
| Medium | 1 | Fix recommended before production |
| Low | 1 | Fix recommended for defense-in-depth |
| Info | 3 | Best practices followed |

---

## Findings

### FINDING-001: Run Access Control Missing on `/runs/{runId}` Endpoints

**Severity**: MEDIUM
**Status**: Open
**Affected Files**:
- [PlaybookRunEndpoints.cs](../../../src/server/api/Sprk.Bff.Api/Api/Ai/PlaybookRunEndpoints.cs)

**Description**:
The following run endpoints do not verify that the requesting user has access to the playbook associated with the run:
- `GET /api/ai/playbooks/runs/{runId}` (GetRunStatus)
- `GET /api/ai/playbooks/runs/{runId}/stream` (StreamRunStatus)
- `GET /api/ai/playbooks/runs/{runId}/detail` (GetRunDetail)
- `POST /api/ai/playbooks/runs/{runId}/cancel` (CancelRun)

An authenticated user could potentially:
1. View execution status of any run if they know/guess the runId (GUID)
2. Cancel another user's running playbook
3. Access detailed execution metrics and node outputs

**Root Cause**:
These endpoints check if the run exists but do not validate that the requesting user owns or has access to the associated playbook.

**Current Code (GetRunStatus - line 251)**:
```csharp
private static async Task<IResult> GetRunStatus(
    Guid runId,
    IPlaybookOrchestrationService orchestrationService,
    ...)
{
    var status = await orchestrationService.GetRunStatusAsync(runId, cancellationToken);
    if (status == null)
    {
        return ProblemDetailsHelper.RunNotFound(runId, correlationId);
    }
    // No playbook ownership check!
    return Results.Ok(status);
}
```

**Recommended Fix**:
Add playbook access validation by resolving the playbook from the run context:

```csharp
// Get run status
var status = await orchestrationService.GetRunStatusAsync(runId, cancellationToken);
if (status == null)
{
    return ProblemDetailsHelper.RunNotFound(runId, correlationId);
}

// Validate user has access to the associated playbook
var playbook = await playbookService.GetPlaybookAsync(status.PlaybookId);
var userId = ExtractUserId(context.HttpContext);
if (playbook == null || (playbook.OwnerId != userId && !playbook.IsPublic && !HasSharedAccess(userId, playbook.Id)))
{
    return Results.Problem(
        statusCode: 403,
        title: "Forbidden",
        detail: "You do not have permission to access this run");
}
```

**Risk**: Unauthorized information disclosure and potential service disruption via cancellation.

**CVSS 3.1 Estimate**: 5.4 (Medium) - AV:N/AC:L/PR:L/UI:N/S:U/C:L/I:L/A:N

---

### FINDING-002: Template Engine HTML Escaping Disabled

**Severity**: LOW
**Status**: Open
**Affected Files**:
- [TemplateEngine.cs:42](../../../src/server/api/Sprk.Bff.Api/Services/Ai/TemplateEngine.cs#L42)

**Description**:
The Handlebars template engine is configured with `NoEscape = true`, which disables HTML encoding of output values.

**Current Code**:
```csharp
_handlebars = Handlebars.Create(new HandlebarsConfiguration
{
    ThrowOnUnresolvedBindingExpression = false,
    NoEscape = true  // <-- HTML encoding disabled
});
```

**Risk**:
If template output is ever rendered in an HTML context (e.g., email body with HTML content), user-controlled data from node outputs could lead to Cross-Site Scripting (XSS).

**Mitigating Factors**:
- Template engine is primarily used for plain text (email subjects, task titles)
- Node outputs are typically structured data, not user-generated HTML
- Email templates may use separate HTML sanitization

**Recommended Fix**:
1. Keep `NoEscape = true` for the default engine (for non-HTML templates)
2. Create a separate `HtmlTemplateEngine` with escaping enabled for HTML contexts
3. Or use triple-brace `{{{unsafe}}}` syntax when HTML is intentional

---

## Areas Reviewed - No Issues Found

### Authorization (Pass)

**NodeEndpoints.cs** - All endpoints properly protected:
| Endpoint | Authorization | Notes |
|----------|---------------|-------|
| GET /nodes | `AddPlaybookAccessAuthorizationFilter()` | Read access |
| POST /nodes | `AddPlaybookOwnerAuthorizationFilter()` | Owner required |
| GET /nodes/{nodeId} | `AddPlaybookAccessAuthorizationFilter()` + ownership check | Also validates node belongs to playbook |
| PUT /nodes/{nodeId} | `AddPlaybookOwnerAuthorizationFilter()` + ownership check | Also validates node belongs to playbook |
| DELETE /nodes/{nodeId} | `AddPlaybookOwnerAuthorizationFilter()` + ownership check | Also validates node belongs to playbook |

**PlaybookRunEndpoints.cs** - Playbook-scoped endpoints properly protected:
| Endpoint | Authorization | Notes |
|----------|---------------|-------|
| POST /playbooks/{id}/validate | `AddPlaybookAccessAuthorizationFilter()` | Read access |
| POST /playbooks/{id}/execute | `AddPlaybookAccessAuthorizationFilter()` + rate limiting | Read access + ai-stream rate limit |
| GET /playbooks/{id}/runs | `AddPlaybookAccessAuthorizationFilter()` | Read access |

### Input Validation (Pass)

- GUID route parameters properly typed (`{id:guid}`, `{nodeId:guid}`, `{runId:guid}`)
- CreateNodeRequest validated via `nodeService.ValidateAsync()`
- ExecutePlaybookRequest.DocumentIds validated for non-empty
- Pagination parameters clamped to valid ranges (1-100)
- No string interpolation into queries (service layer abstraction)

### Injection Vulnerabilities (Pass)

- **SQL Injection**: Not applicable - uses Dataverse OData API with typed parameters
- **Command Injection**: Not applicable - no shell execution
- **Prompt Injection**: UserContext passed as data context, not as system prompts. AI prompts constructed from action definitions, not user input.
- **Template Injection**: Handlebars is logic-less; no code execution capability

### OBO Token Handling (Pass)

- Tokens extracted from HttpContext via `TokenHelper.ExtractBearerToken()`
- Used only for authentication headers when calling downstream services
- Never logged or included in error responses
- Never stored in node outputs or run status
- HttpContext passed through execution chain; tokens not explicitly copied

### Error Handling (Pass)

- All errors use RFC 7807 ProblemDetails format
- No stack traces exposed to clients
- Correlation IDs included for debugging
- Internal error messages sanitized before returning to client

---

## Recommendations Summary

| Priority | Finding | Action |
|----------|---------|--------|
| **P1** | FINDING-001 | Add playbook access validation to all run endpoints |
| **P2** | FINDING-002 | Consider separate HTML-escaped template engine for HTML contexts |

---

## Test Cases for Verification

### FINDING-001 Test Cases

1. **Unauthorized Run Status Access**
   - User A creates and executes a playbook
   - User B attempts `GET /api/ai/playbooks/runs/{runId}` with User A's runId
   - Expected: 403 Forbidden
   - Current: 200 OK with run status (vulnerability)

2. **Unauthorized Run Cancellation**
   - User A executes a long-running playbook
   - User B attempts `POST /api/ai/playbooks/runs/{runId}/cancel`
   - Expected: 403 Forbidden
   - Current: Run cancelled (vulnerability)

3. **Run History Access Control**
   - User A creates runs for a playbook
   - User B attempts `GET /api/ai/playbooks/{playbookId}/runs`
   - Expected: 403 Forbidden (if not authorized)
   - Current: Works correctly (has `AddPlaybookAccessAuthorizationFilter`)

---

## Approval

**Review Complete**: 2026-01-13
**Recommended for Production**: Yes, after FINDING-001 is addressed
**Re-review Required**: Yes, after fixes implemented
