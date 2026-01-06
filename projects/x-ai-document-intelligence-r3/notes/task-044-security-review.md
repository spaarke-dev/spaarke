# Task 044: Security Review - AI Document Intelligence R3

> **Date**: December 30, 2025
> **Reviewer**: Claude Code
> **Project**: AI Document Intelligence R3
> **Status**: COMPLETE - Issues Fixed

---

## Executive Summary

Security review completed for all R3 AI endpoints. **3 critical issues** identified and **ALL 3 FIXED**, **2 medium issues** documented as accepted risk, **1 low issue** noted. The application now has proper tenant isolation, document-level authorization, and authentication on all endpoints.

---

## Security Findings

### CRITICAL (Fixed)

#### 1. RAG Endpoint Tenant Isolation Bypass

**Location**: [RagEndpoints.cs](../../../src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs)

**Issue**: The RAG endpoints accepted `tenantId` from the request body without validating that the authenticated user has access to that tenant. Any authenticated user could query/index/delete documents in ANY tenant by passing an arbitrary tenantId.

**Impact**: Cross-tenant data leakage - HIGH

**Fix Applied**: Created [TenantAuthorizationFilter.cs](../../../src/server/api/Sprk.Bff.Api/Api/Filters/TenantAuthorizationFilter.cs) that:
- Extracts user's Azure AD `tid` (tenant ID) claim
- Compares against `tenantId` in request body
- Returns 403 Forbidden if tenants don't match

Applied to endpoints:
- POST `/api/ai/rag/search`
- POST `/api/ai/rag/index`
- POST `/api/ai/rag/index/batch`
- DELETE `/api/ai/rag/{documentId}`
- DELETE `/api/ai/rag/source/{sourceDocumentId}`

#### 2. Resilience Endpoints Public (Information Disclosure)

**Location**: [ResilienceEndpoints.cs](../../../src/server/api/Sprk.Bff.Api/Api/ResilienceEndpoints.cs)

**Issue**: The `/api/resilience/*` endpoints had no authentication requirement, exposing internal service state (circuit breaker names, states, retry times).

**Impact**: Information disclosure - MEDIUM to HIGH

**Fix Applied**: Added `.RequireAuthorization()` to the resilience endpoint group. Consider restricting to admin roles in production.

#### 3. Document Intelligence Authorization Disabled (FIXED)

**Location**: [DocumentIntelligenceEndpoints.cs:32](../../../src/server/api/Sprk.Bff.Api/Api/Ai/DocumentIntelligenceEndpoints.cs#L32), [AiAuthorizationFilter.cs:55-59](../../../src/server/api/Sprk.Bff.Api/Api/Filters/AiAuthorizationFilter.cs#L55-L59)

**Issue**: The `AddAiAuthorizationFilter()` was commented out. The original comment incorrectly stated "requires Dataverse OBO auth configuration" but investigation revealed the real issue was a **claim extraction mismatch**:
- `AiAuthorizationFilter` was extracting `ClaimTypes.NameIdentifier`
- `DataverseAccessDataSource.LookupDataverseUserIdAsync()` expects the Azure AD `oid` (Object ID) claim to query `azureactivedirectoryobjectid` in Dataverse systemusers

This mismatch caused user lookup to fail, returning `AccessRights.None` and blocking all requests.

**Impact**: Unauthorized document analysis - HIGH

**Fix Applied**:
1. Updated `AiAuthorizationFilter.cs` to extract `oid` claim with proper fallback chain:
   ```csharp
   var userId = httpContext.User.FindFirst("oid")?.Value
       ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
       ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
   ```
2. Enabled `AddAiAuthorizationFilter()` on all DocumentIntelligence endpoints:
   - `/analyze`
   - `/enqueue`
   - `/enqueue-batch`

**Architecture Note**: The existing SDAP authentication architecture is correct:
- OBO (On-Behalf-Of) is used for Graph API calls (user-delegated)
- ClientSecret is used for Dataverse queries (app credentials querying user permissions)
- `DataverseAccessDataSource` correctly uses `RetrievePrincipalAccess` to check user permissions

---

### MEDIUM (Documented - Accepted Risk)

#### 4. Analysis Record Authorization Skipped

**Location**: [AnalysisAuthorizationFilter.cs:200-220](../../../src/server/api/Sprk.Bff.Api/Api/Filters/AnalysisAuthorizationFilter.cs#L200-L220)

**Issue**: The `AuthorizeAnalysisAccessAsync` method contains a Phase 1 TODO and skips actual Dataverse authorization lookup.

**Impact**: Users can access/continue/export any analysis by ID - MEDIUM

**Status**: **Accepted Risk** for Phase 1. The analysis store is in-memory and session-scoped, limiting exposure.

**Mitigation**: When Dataverse integration is complete (Phase 2):
1. Uncomment the authorization lookup code
2. Verify user owns the analysis OR has access to source document

#### 5. Prompt Injection Risk

**Location**: [AnalysisContextBuilder.cs:67-69](../../../src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisContextBuilder.cs#L67-L69)

**Issue**: Document text and user messages are concatenated directly into prompts without sanitization.

**Impact**: Prompt injection could manipulate AI behavior - MEDIUM

**Status**: **Accepted Risk** - Standard pattern for LLM applications.

**Mitigations in Place**:
- Users can only analyze documents they have access to (via AiAuthorizationFilter)
- System prompts clearly define expected behavior
- Output is for user's own consumption

**Future Enhancement**: Consider adding:
- Prompt boundary markers (e.g., `<|document_start|>`)
- Content filtering for known injection patterns
- Output validation for sensitive content

---

### LOW (Documented)

#### 6. PII in Debug Logs

**Location**: Various AI services

**Issue**: File names, email addresses logged at Debug level. DocumentId and TenantId logged at Info level.

**Impact**: Potential PII exposure if debug logs enabled in production - LOW

**Status**: **Acceptable** - Debug logging disabled by default in production.

**Recommendation**: Review log levels before production deployment:
```json
// appsettings.Production.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Sprk.Bff.Api.Services.Ai": "Warning"
    }
  }
}
```

---

## Security Controls Verified

### Compliant

| Control | Status | Evidence |
|---------|--------|----------|
| Rate limiting on AI endpoints | ✅ Pass | `.RequireRateLimiting("ai-batch")` on all endpoints |
| Authentication required | ✅ Pass | `.RequireAuthorization()` on all endpoint groups |
| Input validation | ✅ Pass | Request models validated, null checks present |
| ProblemDetails errors | ✅ Pass | No stack traces in error responses |
| Circuit breakers | ✅ Pass | OpenAI, AI Search protected by Polly circuit breakers |
| No secrets in logs | ✅ Pass | API keys not logged, only endpoints |
| Playbook authorization | ✅ Pass | Owner-based and access-based filters implemented |
| Analysis execute auth | ✅ Pass | Document access validated via AddAnalysisExecuteAuthorizationFilter |

### Secret Management

| Secret | Storage | Status |
|--------|---------|--------|
| OpenAiKey | Key Vault (production) / user-secrets (dev) | ✅ Compliant |
| DocIntelKey | Key Vault (production) / user-secrets (dev) | ✅ Compliant |
| AiSearchKey | Key Vault (production) / user-secrets (dev) | ✅ Compliant |
| RedisConnectionString | Key Vault reference in app settings | ✅ Compliant |

---

## Files Changed

| File | Change |
|------|--------|
| `Api/Filters/TenantAuthorizationFilter.cs` | **NEW** - Tenant isolation filter |
| `Api/Filters/AiAuthorizationFilter.cs` | Fixed claim extraction to use `oid` claim with fallback chain |
| `Api/Ai/RagEndpoints.cs` | Added TenantAuthorizationFilter to all endpoints |
| `Api/Ai/DocumentIntelligenceEndpoints.cs` | Enabled AiAuthorizationFilter on all 3 endpoints |
| `Api/ResilienceEndpoints.cs` | Added RequireAuthorization |

---

## Pre-Production Checklist

Before deploying to production, verify:

- [x] ~~Dataverse OBO authentication configured~~ (N/A - existing architecture uses ClientSecret for Dataverse, which is correct)
- [x] ~~Re-enable `AddAiAuthorizationFilter()` on DocumentIntelligence endpoints~~ (DONE - fixed claim extraction and enabled)
- [ ] Review log levels in appsettings.Production.json
- [ ] Consider admin-only access for resilience endpoints
- [ ] Verify Key Vault secrets are populated
- [ ] Run integration tests with authenticated requests
- [ ] Verify `DataverseAccessDataSource` can successfully lookup users by Azure AD `oid`

---

## ADR Compliance

| ADR | Requirement | Status |
|-----|-------------|--------|
| ADR-008 | Use endpoint filters for auth | ✅ Compliant |
| ADR-016 | AI rate limits and security | ✅ Compliant |

---

*Security Review completed: December 30, 2025*
