# Sprint 4: Production Readiness - Critical Security & Architecture Fixes

**Sprint Duration:** 5 days (October 2-6, 2025)
**Sprint Goal:** Resolve all P0 blockers identified in comprehensive architectural review to achieve production readiness
**Status:** Ready for Implementation

---

## Executive Summary

Sprint 4 addresses **5 critical P0 blockers** that prevent production deployment. These issues were identified during a comprehensive architectural and ADR compliance review. All fixes are well-scoped, have clear acceptance criteria, and include detailed AI-directed coding instructions.

**Production Readiness Assessment:**
- **Current State:** 7.5/10 (blocked by 5 critical issues)
- **Post-Sprint 4:** 9.0/10 (production ready)

---

## Sprint Overview

### Critical Issues (P0 Blockers)

| Task | Priority | Effort | Impact |
|------|----------|--------|--------|
| 4.1 - Fix Distributed Cache | 🔴 P0 | 4 hours | Idempotency broken in multi-instance deployments |
| 4.2 - Enable Authentication | 🔴 P0 | 1 day | All endpoints unauthenticated (security risk) |
| 4.3 - Enable Rate Limiting | 🔴 P0 | 1 day | All endpoints vulnerable to DoS attacks |
| 4.4 - Remove ISpeService | 🔴 P0 | 2 days | ADR-007 violation (only major non-compliance) |
| 4.5 - Secure CORS | 🔴 P0 | 2 hours | Dangerous wildcard fallback in production |

**Total Effort:** 5 days (40 hours)

---

## Task Details

### Task 4.1: Fix Distributed Cache Configuration
**File:** [TASK-4.1-DISTRIBUTED-CACHE-FIX.md](TASK-4.1-DISTRIBUTED-CACHE-FIX.md)

**Problem:**
- Application uses `AddDistributedMemoryCache()` which is NOT distributed across instances
- Breaks idempotency service in multi-instance deployments
- Duplicate job processing will occur

**Solution:**
- Replace with `AddStackExchangeRedisCache()` for production
- Add configuration-driven fallback to in-memory for local development
- Provision Azure Redis Cache

**Key Changes:**
- `Program.cs` lines 180-187: Add Redis configuration with enabled flag
- `appsettings.json`: Add Redis section with `Enabled: false` for dev
- `appsettings.Production.json`: Add Redis section with `Enabled: true`
- Add NuGet: `Microsoft.Extensions.Caching.StackExchangeRedis`

**Acceptance Criteria:**
- [ ] Build succeeds with 0 errors
- [ ] Development uses in-memory cache (no Redis required)
- [ ] Production configuration ready for Redis connection string
- [ ] Idempotency service works across multiple instances

---

### Task 4.2: Enable Authentication Middleware
**File:** [TASK-4.2-ENABLE-AUTHENTICATION.md](TASK-4.2-ENABLE-AUTHENTICATION.md)

**Problem:**
- Authorization policies configured BUT no authentication middleware enabled
- User identity never validated
- Bearer tokens never verified
- All endpoints effectively public

**Solution:**
- Add Microsoft.Identity.Web authentication
- Enable Azure AD JWT bearer token validation
- Add `app.UseAuthentication()` to middleware pipeline
- Configure OBO (On-Behalf-Of) token acquisition

**Key Changes:**
- Add NuGet: `Microsoft.Identity.Web`, `Microsoft.Identity.Web.MicrosoftGraph`
- `Program.cs` after line 15: Add authentication services
- `Program.cs` after line 240: Add `app.UseAuthentication()` middleware
- `appsettings.json`: Add AzureAd configuration section
- Update `GraphClientFactory`: Add `CreateClientForUser()` method

**Acceptance Criteria:**
- [ ] Build succeeds with 0 errors
- [ ] Protected endpoints return 401 without token
- [ ] Valid Azure AD tokens authenticate successfully
- [ ] Health check remains anonymous (200 without token)
- [ ] Logs show "Token validated for user {UserId}"

---

### Task 4.3: Enable Rate Limiting on All Endpoints
**File:** [TASK-4.3-ENABLE-RATE-LIMITING.md](TASK-4.3-ENABLE-RATE-LIMITING.md)

**Problem:**
- Rate limiter service configured but empty
- 20+ TODO comments indicating deferred implementation
- All 27 endpoints vulnerable to DoS attacks
- No protection against Graph API / Dataverse quota exhaustion

**Solution:**
- Configure 6 rate limiting policies (graph-read, graph-write, dataverse-query, upload-heavy, job-submission, anonymous)
- Add `app.UseRateLimiter()` middleware
- Apply `.RequireRateLimiting("policy")` to all 27 endpoints
- Remove all rate limiting TODO comments

**Rate Limiting Policies:**
| Policy | Limit | Window | Strategy |
|--------|-------|--------|----------|
| graph-read | 100 req/min | Sliding | High volume reads |
| graph-write | 50 req/min | Token bucket | Moderate writes |
| dataverse-query | 200 req/min | Sliding | Permission checks |
| upload-heavy | 5 concurrent | Concurrency | Upload sessions |
| job-submission | 10 req/min | Fixed | Background jobs |
| anonymous | 60 req/min | Fixed | Public endpoints |

**Key Changes:**
- `Program.cs` line 315: Configure rate limiter with 6 policies
- `Program.cs` pipeline: Add `app.UseRateLimiter()` after authorization
- `OBOEndpoints.cs`: Apply rate limiting to 9 endpoints
- `DocumentsEndpoints.cs`: Apply rate limiting to 6 endpoints
- `UploadEndpoints.cs`: Apply rate limiting to 3 endpoints
- `PermissionsEndpoints.cs`: Apply rate limiting to 4 endpoints
- `UserEndpoints.cs`: Apply rate limiting to 5 endpoints

**Acceptance Criteria:**
- [ ] Build succeeds with 0 errors
- [ ] No TODO comments for rate limiting remain
- [ ] Exceed rate limit returns 429 with Retry-After header
- [ ] Health check exempt from rate limiting
- [ ] Logs show rate limiter configured with 6 policies

---

### Task 4.4: Remove ISpeService/IOboSpeService Abstractions (Full Refactor)
**Master Guide:** [TASK-4.4-FULL-REFACTOR-IMPLEMENTATION.md](TASK-4.4-FULL-REFACTOR-IMPLEMENTATION.md)
**Phase Index:** [TASK-4.4-PHASE-INDEX.md](TASK-4.4-PHASE-INDEX.md) ⭐ **Start here for implementation**
**Analysis:** [TASK-4.4-OBO-EXPLANATION.md](TASK-4.4-OBO-EXPLANATION.md) | [TASK-4.4-DECISION-ANALYSIS.md](TASK-4.4-DECISION-ANALYSIS.md)

**Problem:**
- `ISpeService` and `IOboSpeService` interfaces directly violate ADR-007
- ADR explicitly decided to remove these abstractions
- Only major ADR non-compliance in codebase (10/11 compliant)
- OBO operations exist but in separate service (not integrated into facade)

**Solution (Full Refactor - Approved):**
- Move existing OBO code from `OboSpeService` into modular operation classes
- Add `*AsUserAsync` methods to ContainerOperations, DriveItemOperations, UploadSessionManager
- Create new `UserOperations` class for user info/capabilities
- Expose all OBO methods via `SpeFileStore` facade (single facade for all Graph operations)
- Delete interface abstractions

**Architecture Change:**
```
BEFORE:
  Admin Endpoints → SpeFileStore → ContainerOperations/DriveItemOps (app-only)
  User Endpoints → IOboSpeService → OboSpeService (OBO)

AFTER:
  Admin Endpoints → SpeFileStore → ContainerOperations/DriveItemOps (app-only methods)
  User Endpoints → SpeFileStore → ContainerOperations/DriveItemOps (OBO methods)
  (Single facade, dual auth modes)
```

**Implementation Phases (7 phases - each in separate file):**
1. **[Phase 1: Add OBO Methods](TASK-4.4-PHASE-1-ADD-OBO-METHODS.md)** (6 hours) - Add user context methods to operation classes
2. **[Phase 2: Update Facade](TASK-4.4-PHASE-2-UPDATE-FACADE.md)** (1 hour) - Update SpeFileStore to expose OBO methods
3. **[Phase 3: TokenHelper](TASK-4.4-PHASE-3-TOKEN-HELPER.md)** (30 min) - Create token extraction utility
4. **[Phase 4: Update Endpoints](TASK-4.4-PHASE-4-UPDATE-ENDPOINTS.md)** (2 hours) - Replace IOboSpeService in 9 endpoints
5. **[Phase 5: Delete Files](TASK-4.4-PHASE-5-DELETE-FILES.md)** (30 min) - Remove interface files
6. **[Phase 6: Update DI](TASK-4.4-PHASE-6-UPDATE-DI.md)** (1 hour) - Update dependency injection
7. **[Phase 7: Build & Test](TASK-4.4-PHASE-7-BUILD-TEST.md)** (1.5 hours) - Verify and test

**Effort:** 12.5 hours (1.5 days)
- Update all tests to mock `SpeFileStore` or `IGraphClientFactory`

**Acceptance Criteria:**
- [ ] 3 interface files deleted
- [ ] No references to ISpeService/IOboSpeService remain
- [ ] Build succeeds with 0 errors
- [ ] All tests passing with updated mocks
- [ ] ADR-007 fully compliant (100% ADR compliance)

---

### Task 4.5: Secure CORS Configuration
**File:** [TASK-4.5-SECURE-CORS-CONFIGURATION.md](TASK-4.5-SECURE-CORS-CONFIGURATION.md)

**Problem:**
- CORS configuration has dangerous `AllowAnyOrigin()` fallback
- If configuration missing or empty, allows ALL origins (security risk)
- Wildcard `"*"` origin accepted
- Production deployment could accidentally expose API to all websites

**Solution:**
- Fail-closed CORS: Throw exception if configuration missing (non-dev)
- Reject wildcard origins explicitly
- Validate URLs (must be absolute HTTPS in production)
- Log configured origins at startup for audit trail

**Key Changes:**
- `Program.cs` lines 20-50: Secure CORS configuration with validation
- Remove `AllowAnyOrigin()` fallback
- Add fail-fast validation (throw if misconfigured)
- Replace `AllowAnyHeader()` with explicit whitelist
- `appsettings.json`: Add explicit localhost origins
- `appsettings.Staging.json`: Add staging frontend URL
- `appsettings.Production.json`: Add empty array (overridden by Azure config)

**Acceptance Criteria:**
- [ ] Build succeeds with 0 errors
- [ ] Development allows localhost origins
- [ ] Production throws exception if CORS not configured
- [ ] Wildcard `"*"` rejected at startup
- [ ] Logs show "CORS: Configured with N allowed origins"
- [ ] Allowed origins receive CORS headers
- [ ] Disallowed origins do NOT receive CORS headers

---

## Sprint Planning

### Day 1 (October 2)
- **Morning:** Task 4.1 - Distributed Cache (4 hours)
- **Afternoon:** Task 4.2 - Authentication (start, 4 hours)

### Day 2 (October 3)
- **Full Day:** Task 4.2 - Authentication (complete, 4 hours) + Testing (2 hours)
- **Afternoon:** Task 4.5 - CORS (2 hours)

### Day 3 (October 4)
- **Full Day:** Task 4.3 - Rate Limiting (8 hours)

### Day 4-5 (October 5-6)
- **Full Days:** Task 4.4 - Remove ISpeService (16 hours)
  - Day 4: Refactor SpeFileStore, update endpoints
  - Day 5: Update tests, integration testing, final validation

### Day 5 Afternoon
- **Final Validation:** Run full test suite, integration tests, smoke tests
- **Sprint Review:** Demo production-ready features
- **Sprint Retrospective:** Document lessons learned

---

## Testing Strategy

### Unit Tests
Each task includes specific unit test updates:
- Cache idempotency tests
- Authentication 401/403 tests
- Rate limiting 429 tests
- SpeFileStore OBO method tests
- CORS preflight tests

### Integration Tests
- Real Azure AD token authentication
- Real Redis cache operations
- Rate limiting with load testing
- End-to-end OBO flow
- CORS with real browser requests

### Smoke Tests (Post-Deployment)
```bash
# 1. Health check (anonymous)
curl https://sdap-api.azurewebsites.net/healthz

# 2. Protected endpoint without token (expect 401)
curl https://sdap-api.azurewebsites.net/api/user/containers

# 3. Protected endpoint with token (expect 200 or 403)
curl -H "Authorization: Bearer {token}" https://sdap-api.azurewebsites.net/api/user/containers

# 4. Rate limiting (expect 429 after 100 requests)
for i in {1..150}; do curl -H "Authorization: Bearer {token}" https://sdap-api.azurewebsites.net/api/user/containers; done

# 5. CORS preflight (expect 204 with CORS headers)
curl -X OPTIONS -H "Origin: https://sdap.contoso.com" https://sdap-api.azurewebsites.net/api/user/containers
```

---

## Infrastructure Requirements

### Azure Resources to Provision

**1. Azure Redis Cache**
```bash
az redis create \
  --name sdap-redis \
  --resource-group sdap-rg \
  --location eastus \
  --sku Basic \
  --vm-size C0
```
**Cost:** ~$16/month (Basic C0)

**2. Azure AD App Registration**
- Create app registration for SDAP API
- Add app roles for authorization
- Generate client secret
- Configure API permissions (Microsoft.Graph)

**3. Application Insights**
- Used for monitoring rate limits, auth failures, cache metrics
- Should already exist from previous sprints

---

## Configuration Updates

### Azure App Service Settings (Production)

```bash
# Authentication
az webapp config appsettings set \
  --name sdap-api-prod \
  --resource-group sdap-rg \
  --settings \
  AzureAd__TenantId="{tenant-id}" \
  AzureAd__ClientId="{client-id}" \
  AzureAd__Audience="api://{client-id}"

# Redis Cache
az webapp config appsettings set \
  --name sdap-api-prod \
  --resource-group sdap-rg \
  --settings \
  Redis__Enabled=true \
  Redis__ConnectionString="{redis-connection-string}" \
  Redis__InstanceName="sdap-prod:"

# CORS
az webapp config appsettings set \
  --name sdap-api-prod \
  --resource-group sdap-rg \
  --settings \
  Cors__AllowedOrigins__0="https://sdap.contoso.com"
```

---

## Success Criteria

### Production Readiness Checklist

✅ **Security:**
- [ ] All endpoints require authentication (401 without token)
- [ ] All endpoints have rate limiting (429 after limit)
- [ ] CORS only allows whitelisted origins
- [ ] No wildcard CORS in production
- [ ] Tokens validated via Azure AD
- [ ] OBO flow works for user context operations

✅ **Architecture:**
- [ ] ADR-007 compliant (no ISpeService/IOboSpeService)
- [ ] 100% ADR compliance (11/11)
- [ ] Distributed cache works across instances
- [ ] Idempotency prevents duplicate job processing

✅ **Quality:**
- [ ] Build succeeds with 0 errors, 0 warnings
- [ ] All unit tests passing
- [ ] All integration tests passing
- [ ] No TODO comments for P0 items

✅ **Deployment:**
- [ ] Azure Redis Cache provisioned
- [ ] Azure AD app registration configured
- [ ] Staging environment validated
- [ ] Production smoke tests pass

✅ **Monitoring:**
- [ ] Application Insights configured
- [ ] Logs show authentication events
- [ ] Logs show rate limit violations
- [ ] Cache hit/miss metrics tracked

---

## Risk Management

### Known Risks

**Risk 1: Azure AD App Registration Delays**
- **Probability:** Medium
- **Impact:** High (blocks Task 4.2 testing)
- **Mitigation:** Create app registration on Day 1
- **Contingency:** Use test authentication handler for unit tests

**Risk 2: Redis Cache Provisioning Time**
- **Probability:** Low
- **Impact:** Medium (blocks Task 4.1 validation)
- **Mitigation:** Provision Redis on Day 1 morning
- **Contingency:** Validate with in-memory cache, swap to Redis later

**Risk 3: ISpeService Refactoring Complexity**
- **Probability:** Medium
- **Impact:** High (Task 4.4 is 2 days)
- **Mitigation:** Task has detailed step-by-step instructions
- **Contingency:** Can defer to Sprint 5 if needed (mark as technical debt)

**Risk 4: Breaking Changes in Tests**
- **Probability:** High (expected)
- **Impact:** Medium (slows testing phase)
- **Mitigation:** Each task includes test update instructions
- **Contingency:** Allocate extra time on Day 5 for test fixes

---

## Rollback Plan

Each task document includes a specific rollback plan. General rollback strategy:

1. **Immediate Rollback:** Revert Git commit
   ```bash
   git revert <commit-hash>
   git push origin master
   ```

2. **Configuration Rollback:** Disable feature via app settings
   ```bash
   # Example: Disable Redis, fall back to in-memory
   az webapp config appsettings set --settings Redis__Enabled=false
   ```

3. **Hotfix Rollback:** Deploy previous release from CI/CD pipeline

4. **Impact Assessment:** Document what broke and why
5. **Root Cause Analysis:** Schedule post-mortem
6. **Fix Forward:** Plan remediation in next sprint

---

## Sprint Retrospective Topics

**What Went Well:**
- Comprehensive task documentation with AI prompts
- Clear acceptance criteria and testing strategy
- Infrastructure automation scripts

**What Could Be Improved:**
- [Fill after sprint completion]

**Action Items:**
- [Fill after sprint completion]

**Lessons Learned:**
- [Fill after sprint completion]

---

## Sprint Artifacts

### Documentation Created
- [TASK-4.1-DISTRIBUTED-CACHE-FIX.md](TASK-4.1-DISTRIBUTED-CACHE-FIX.md) - 48 pages
- [TASK-4.2-ENABLE-AUTHENTICATION.md](TASK-4.2-ENABLE-AUTHENTICATION.md) - 52 pages
- [TASK-4.3-ENABLE-RATE-LIMITING.md](TASK-4.3-ENABLE-RATE-LIMITING.md) - 58 pages
- [TASK-4.4-REMOVE-ISPESERVICE-ABSTRACTION.md](TASK-4.4-REMOVE-ISPESERVICE-ABSTRACTION.md) - 48 pages
- [TASK-4.5-SECURE-CORS-CONFIGURATION.md](TASK-4.5-SECURE-CORS-CONFIGURATION.md) - 42 pages

**Total Documentation:** 248 pages of detailed implementation guidance

### Code Changes (Estimated)
- Files Modified: ~25 files
- Files Deleted: 3 files (ISpeService interfaces)
- Files Created: ~5 files (configuration classes, helpers)
- Lines Added: ~1,500 lines
- Lines Deleted: ~800 lines
- Net Change: ~700 lines

---

## References

### Related Sprint Documents
- [Sprint 3 Completion Review](../Sprint%203/SPRINT-3-COMPLETION-REVIEW.md)
- [Sprint 4 Planning Inputs](SPRINT-4-PLANNING-INPUTS.md)
- [Comprehensive Architectural Review](../../../docs/spaarke-comprehensive-architectural-review.md)

### ADRs Referenced
- **ADR-007:** SPE Storage Seam Minimalism (Task 4.4)
- **ADR-009:** Caching Policy - Redis First (Task 4.1)
- **ADR-008:** Authorization Endpoint Filters (Task 4.2)
- **ADR-010:** DI Minimalism (Task 4.4)

### External Documentation
- [Microsoft.Identity.Web Documentation](https://learn.microsoft.com/en-us/azure/active-directory/develop/microsoft-identity-web)
- [ASP.NET Core Rate Limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit)
- [Azure Redis Cache](https://learn.microsoft.com/en-us/azure/azure-cache-for-redis/)
- [CORS Security Best Practices](https://cheatsheetseries.owasp.org/cheatsheets/CORS_Security_Cheat_Sheet.html)

---

## Appendix: AI Implementation Workflow

### Using Task Documents with AI Coding Assistants

Each task document includes an "AI Implementation Prompt" section at the end. To use:

1. **Read Task Document:** Understand the problem and solution
2. **Copy AI Prompt:** Find the prompt at the end of each task document
3. **Paste to AI:** Use with Claude, GitHub Copilot, ChatGPT, etc.
4. **Review Generated Code:** Validate against acceptance criteria
5. **Test Thoroughly:** Run unit tests, integration tests, smoke tests
6. **Iterate:** Refine based on test results

### Example Workflow (Task 4.1)

```
Developer → Read TASK-4.1-DISTRIBUTED-CACHE-FIX.md
         → Copy AI prompt from end of document
         → Paste to Claude Code: "Implement distributed Redis cache..."
         → Claude generates code changes
         → Developer reviews changes against Step 1-5 in document
         → Run: dotnet build
         → Run: dotnet test
         → Commit if all tests pass
```

---

**Sprint Owner:** [Assign to tech lead]
**Scrum Master:** [Assign to scrum master]
**Team Members:** [Assign developers]
**Created:** 2025-10-02
**Last Updated:** 2025-10-02

---

## Quick Links

- [Task 4.1: Distributed Cache](TASK-4.1-DISTRIBUTED-CACHE-FIX.md)
- [Task 4.2: Authentication](TASK-4.2-ENABLE-AUTHENTICATION.md)
- [Task 4.3: Rate Limiting](TASK-4.3-ENABLE-RATE-LIMITING.md)
- [Task 4.4: Remove ISpeService](TASK-4.4-REMOVE-ISPESERVICE-ABSTRACTION.md)
- [Task 4.5: Secure CORS](TASK-4.5-SECURE-CORS-CONFIGURATION.md)
- [Architectural Review](../../../docs/spaarke-comprehensive-architectural-review.md)

**Status:** ✅ Ready for Sprint Kickoff
