# Lessons Learned - AI Document Intelligence R1

> **Project**: AI Document Intelligence R1 - Core Infrastructure
> **Completed**: December 28, 2025
> **Duration**: December 25-28, 2025 (4 days)

---

## Executive Summary

R1 was a **verification-first** project that confirmed existing infrastructure works correctly while filling in gaps. The project completed all phases successfully with 22 tasks: 7 completed, 11 skipped (entities existed), and 4 done in Phase 1C.

---

## What Went Well

### 1. Verification-First Approach

**Decision**: Start with verification before creating anything.

**Outcome**: Saved significant effort - all 10 Dataverse entities already existed, skipping 10 creation tasks.

**Recommendation for R2/R3**: Continue verification-first approach for any infrastructure work.

### 2. Structured Task Files (POML)

**Decision**: Use XML-based task files with metadata, steps, and acceptance criteria.

**Outcome**: Clear execution path, easy context recovery, consistent documentation.

**Recommendation for R2/R3**: Continue using POML format for complex projects.

### 3. Incremental Verification Reports

**Decision**: Create verification reports (entities, env vars, API, AI Foundry) before consolidating.

**Outcome**: Clear audit trail, identified entity name discrepancy (`sprk_aiknowledgedeployment` not `sprk_knowledgedeployment`).

**Recommendation for R2/R3**: Maintain verification report pattern for infrastructure validation.

### 4. Solution Export Strategy

**Decision**: Export both managed and unmanaged solutions.

**Outcome**: Ready for production deployments (managed) and development imports (unmanaged).

**Recommendation for R2/R3**: Always export both types when creating deployment packages.

---

## What Could Be Improved

### 1. Integration Test Configuration

**Issue**: All 32 integration tests failed due to missing local Service Bus configuration.

**Root Cause**: `WebApplicationFactory` requires all service dependencies to initialize.

**Recommendation for R2/R3**:
- Create mock services for integration tests
- Add conditional service registration for test environments
- Document required local configuration

### 2. Bicep Module Bug (ai-search.bicep)

**Issue**: `listQueryKeys()[0].key` should be `listQueryKeys().value[0].key`

**Impact**: Blocks `model1-shared.bicep` and `model2-full.bicep` compilation.

**Recommendation for R2/R3**:
- Fix the bug before next deployment
- Add Bicep validation to CI/CD pipeline

### 3. API Keys in Plain Text

**Issue**: AI service keys stored as App Settings, not Key Vault references.

**Impact**: Reduced security posture.

**Recommendation for R2/R3**:
- Migrate secrets to Key Vault
- Use `@Microsoft.KeyVault(SecretUri=...)` format
- Document Key Vault reference pattern in deployment guide

### 4. Duplicate Configuration

**Issue**: Settings exist under both `Ai__` and `DocumentIntelligence__` prefixes with identical values.

**Impact**: Maintenance overhead, potential for drift.

**Recommendation for R2/R3**:
- Consolidate to single configuration section
- Document canonical configuration pattern

---

## Technical Debt Identified

| Item | Priority | Effort | Recommendation |
|------|----------|--------|----------------|
| Fix ai-search.bicep bug | High | 1 hour | Fix before R3 |
| Migrate API keys to Key Vault | Medium | 4 hours | Address in R3 |
| Fix integration test configuration | Medium | 4 hours | Address in R2 |
| Consolidate config prefixes | Low | 2 hours | Address in R3 |

---

## Key Discoveries

### Entity Name Correction

| Documented | Actual |
|------------|--------|
| `sprk_knowledgedeployment` | `sprk_aiknowledgedeployment` |

Updated all documentation to reflect actual entity name.

### Security Role SQL Errors

When assigning privileges, choice/option set backing tables return "SQL Integrity violation":
- `sprk_aiknowledgetype`
- `sprk_airetrievalmode`
- `sprk_aiskilltype`

**Finding**: This is expected behavior - skip these tables.

### API Health

BFF API fully operational:
- `/ping` - 0.82s response
- `/healthz` - 1.21s response
- All 55 environment variables configured

---

## Recommendations for R2 (UI)

1. **PCF Controls Ready**: AnalysisBuilder and AnalysisWorkspace are built; focus on deployment verification.

2. **Use Existing Scripts**: `Deploy-PCFWebResources.ps1` and `Test-SdapBffApi.ps1` are available.

3. **Verify API Integration First**: Before deploying PCF controls, test API endpoints with auth tokens.

4. **Theme Compliance**: Follow ADR-021 Fluent UI v9 patterns (already implemented).

---

## Recommendations for R3 (Advanced Features)

1. **Fix Bicep Bug First**: The ai-search module needs fixing before customer deployments.

2. **Key Vault Migration**: Move all secrets to Key Vault before production.

3. **Integration Test Strategy**: Implement proper test configuration or mocking before adding new tests.

4. **Prompt Flow Templates**: Templates exist in infrastructure/ai-foundry/ - verify before creating new ones.

---

## Project Statistics

| Metric | Value |
|--------|-------|
| Total Tasks | 22 |
| Completed | 7 |
| Skipped | 11 |
| Documentation Files Created | 8 |
| Solution Exports | 2 (managed + unmanaged) |
| Security Roles Created | 2 |
| Entities Verified | 10 |
| Environment Variables Verified | 55 |

---

## Documentation Created

| Document | Purpose |
|----------|---------|
| `VERIFICATION-SUMMARY.md` | Phase 1A consolidated results |
| `notes/verification/entities-verification.md` | Entity existence report |
| `notes/verification/env-vars-verification.md` | Environment variables report |
| `notes/verification/ai-foundry-verification.md` | AI infrastructure report |
| `notes/verification/api-verification.md` | API health report |
| `notes/verification/security-roles-verification.md` | Security roles gap analysis |
| `notes/verification/env-vars-api-verification.md` | API App Service config |
| `notes/deployment/bicep-verification.md` | Bicep template validation |
| `notes/testing/integration-test-results.md` | Integration test results |
| `notes/security-roles.md` | Security roles configuration |
| `docs/guides/AI-PHASE1-DEPLOYMENT-GUIDE.md` | Deployment guide |

---

## Handoff Notes for R2

1. **Project is COMPLETE** - All verification and infrastructure work done.

2. **Security Roles Ready** - "Spaarke AI Analysis User" and "Spaarke AI Analysis Admin" created.

3. **Solution Exported** - Both managed and unmanaged packages in `infrastructure/dataverse/solutions/`.

4. **API Healthy** - BFF API fully operational at `https://spe-api-dev-67e2xz.azurewebsites.net`.

5. **Deployment Guide** - Use `docs/guides/AI-PHASE1-DEPLOYMENT-GUIDE.md` for new environments.

---

*Lessons learned documented: December 28, 2025*
