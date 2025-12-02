# Sprint 3: Security Hardening & Production Readiness

**Sprint Goal**: Resolve critical security vulnerabilities, eliminate placeholder code, and prepare the system for production deployment.

**Sprint Duration**: 6 weeks (can be parallelized with 2-3 developers)
**Status**: 🔴 PLANNING
**Sprint Type**: Remedial/Hardening

---

## Executive Summary

Sprint 3 addresses **critical security gaps** and **production blockers** identified in the Sprint 2 code review. The current system has:

- **Authorization completely disabled** (all users have full access)
- **OBO endpoints returning mock data** (file operations non-functional)
- **Missing configuration** (deployment will fail)
- **Architecture debt** (god classes, dual implementations, scattered logic)

This sprint transforms the codebase from **prototype to production-ready** by implementing real security, real integrations, and production-grade architecture.

---

## Critical Issues from Sprint 2 Code Review

| Issue | Impact | Task | Priority |
|-------|--------|------|----------|
| Authorization disabled (`RequireAssertion(_ => true)`) | 🔴 **SECURITY BREACH** | Task 1.1 | CRITICAL |
| Missing configuration (UAMI, secrets, etc.) | 🔴 **BLOCKS DEPLOYMENT** | Task 1.2 | CRITICAL |
| OboSpeService returns mock data | 🔴 **BLOCKS USER FEATURES** | Task 2.1 | CRITICAL |
| Dual Dataverse implementations | 🟡 Maintenance burden | Task 2.2 | HIGH |
| Background job duality | 🟡 Confusion, potential bugs | Task 3.1 | HIGH |
| SpeFileStore god class (604 lines) | 🟡 Fragile, hard to test | Task 3.2 | HIGH |
| Manual Polly retry logic | 🟢 Code duplication | Task 4.1 | MEDIUM |
| Tests assert placeholder logic | 🟢 Low confidence | Task 4.2 | MEDIUM |
| Namespace inconsistencies, 26 TODOs | 🟢 Code quality | Task 4.3 | MEDIUM |

---

## ⚠️ CRITICAL ARCHITECTURE UPDATE: Granular AccessRights System

**Date**: 2025-10-01
**Status**: 🔴 **FOUNDATIONAL CHANGE - ALL TASKS REVIEWED**

### What Changed in Task 1.1

**Before**: Simple binary access control (Grant/Deny)
**After**: **Granular permission-based system** matching Dataverse's 7 permission types

### Critical Business Rule
> **Users with Read access can preview files, but need Write access to download them.**

### Impact on Sprint 3
- **Task 1.1**: Complete rewrite (8-10 days, +2 days for UI integration)
  - New: `AccessRights` [Flags] enum → Read/Write/Delete/Create/Append/AppendTo/Share
  - New: `OperationAccessPolicy` → Maps operations to required rights
  - New: Permissions API endpoints → For UI integration
  - New: [PCF Control Specification](Task-1.1-PCF-Control-Specification.md) → Conditional buttons
- **Task 2.1**: Must use granular authorization policies (`canpreviewfiles`, `candownloadfiles`, `candeletefiles`)
- **Task 2.2**: Must preserve AccessRights mapping when cleaning up Dataverse code
- **Task 3.1**: Consider authorization context for background jobs
- **Task 3.2**: Clarify authorization happens at endpoint level (not in services)
- **Task 4.2**: Must add comprehensive AccessRights test scenarios

**See**: [Architecture Update Summary](ARCHITECTURE-UPDATE-AccessRights-Summary.md) for full cross-task impact analysis
**See**: [Tasks Update Summary](SPRINT-3-TASKS-UPDATE-SUMMARY.md) for detailed changes to each task

---

## Sprint 3 Task Breakdown

### **Phase 1: Security Foundation** (Weeks 1-2) 🔴

#### [Task 1.1: Authorization Implementation (REVISED - AccessRights)](Task-1.1-REVISED-AccessRights-Authorization.md) (8-10 days)
**CRITICAL** - Implement granular Dataverse-backed authorization with **AccessRights**
- ⚠️ **MAJOR UPDATE**: AccessLevel → AccessRights [Flags] enum (7 permission types)
- Implement granular permission system (Read/Write/Delete/Create/Append/AppendTo/Share)
- **Business Rule**: Read access = preview only; Write access = download
- Create `OperationAccessPolicy` for operation → rights mapping
- Create `OperationAccessRule` for granular permission checking
- Add Permissions API endpoints for UI integration
- **New**: [PCF Control Specification](Task-1.1-PCF-Control-Specification.md) for conditional UI buttons
- Add authorization integration tests with AccessRights scenarios

#### [Task 1.2: Configuration & Deployment Setup](Task-1.2-Configuration-And-Deployment-Setup.md) (2-3 days)
**CRITICAL** - Fix missing configuration and enable deployment
- Create configuration models with validation
- Add startup validation (fail-fast)
- Create `appsettings.Development.json`
- Document deployment process

---

### **Phase 2: Core Functionality** (Weeks 3-4) 🔴

#### [Task 2.1: OboSpeService Real Implementation](Task-2.1-OboSpeService-Real-Implementation.md) ✅ COMPLETE
**CRITICAL** - Replace all mock data with real Graph SDK calls
- ✅ Fixed namespace (`Services` → `Spe.Bff.Api.Services`)
- ✅ Removed `GenerateSampleItems`, `GenerateSampleFileContent`, and all mock generators (~150 lines)
- ✅ Implemented real file operations using Graph SDK v5 (list, download, upload, delete, update)
- ✅ Added Range request support (HTTP 206), ETag caching, chunked uploads
- ✅ **Completion**: [TASK-2.1-IMPLEMENTATION-COMPLETE.md](TASK-2.1-IMPLEMENTATION-COMPLETE.md)
- Add proper error handling and resilience

#### [Task 2.2: Dataverse Cleanup](Task-2.2-Dataverse-Cleanup.md) ✅ COMPLETE
**HIGH** - Eliminate dual Dataverse implementations
- ✅ Archived legacy `DataverseService.cs` (461 lines) to `_archive/`
- ✅ Removed 5 ServiceClient NuGet packages (WCF dependencies)
- ✅ Updated test file to use `DataverseWebApiService`
- ✅ Created comprehensive README.md documenting Web API approach
- ✅ **Completion**: [TASK-2.2-IMPLEMENTATION-COMPLETE.md](TASK-2.2-IMPLEMENTATION-COMPLETE.md)

---

### **Phase 3: Architecture Cleanup** (Week 5) 🟡

#### [Task 3.1: Background Job Consolidation](Task-3.1-Background-Job-Consolidation.md) ✅ COMPLETE
**HIGH** - Unify job processing systems
- ✅ Created `JobSubmissionService` - unified entry point for all job submissions
- ✅ Created `ServiceBusJobProcessor` - generic ADR-004 compliant processor
- ✅ Implemented feature flag `Jobs:UseServiceBus` (true=production, false=dev)
- ✅ Marked `JobProcessor` as development-only with clear warnings
- ✅ Documented `DocumentEventProcessor` coexistence (separate queue for Dataverse plugins)
- ✅ **Completion**: [TASK-3.1-IMPLEMENTATION-COMPLETE.md](TASK-3.1-IMPLEMENTATION-COMPLETE.md)

#### [Task 3.2: SpeFileStore Refactoring](Task-3.2-SpeFileStore-Refactoring.md) ✅ COMPLETE
**HIGH** - Break up 604-line god class
- ✅ Created `ContainerOperations` (180 lines) - Container CRUD operations
- ✅ Created `DriveItemOperations` (260 lines) - File listing, download, delete, metadata
- ✅ Created `UploadSessionManager` (230 lines) - Small file upload and chunked upload logic
- ✅ Refactored `SpeFileStore` to facade pattern (604 → 87 lines)
- ✅ Updated DI registration in `DocumentsModule.cs`
- ✅ **Completion**: [TASK-3.2-IMPLEMENTATION-COMPLETE.md](TASK-3.2-IMPLEMENTATION-COMPLETE.md)

---

### **Phase 4: Hardening** (Week 6) 🟢

#### [Task 4.1: Centralized Resilience](Task-4.1-Centralized-Resilience.md) ✅ COMPLETE
**MEDIUM** - Standardize resilience patterns
- ✅ Created `GraphHttpMessageHandler` with Polly (retry, circuit breaker, timeout)
- ✅ Registered via `IHttpClientFactory` with named "GraphApiClient"
- ✅ Updated `GraphClientFactory` to use IHttpClientFactory
- ✅ Removed 10 manual retry wrappers from endpoints
- ✅ Added configuration-driven policies (GraphResilience section)
- ✅ Archived unused `RetryPolicies.cs`
- ✅ **Completion**: [TASK-4.1-IMPLEMENTATION-COMPLETE.md](TASK-4.1-IMPLEMENTATION-COMPLETE.md)

#### [Task 4.2: Testing Improvements](Task-4.2-Testing-Improvements.md) ✅ COMPLETE
**MEDIUM** - Add real integration tests
- ✅ Created WireMock tests for Graph API (6 tests - all passing)
- ✅ Created WireMock tests for Dataverse Web API (4 tests - all passing)
- ✅ Tested failure scenarios (429, 403, 404)
- ✅ Added test configuration (appsettings.Test.json)
- ✅ Fixed existing test compatibility issues (AccessRights migration, SpeFileStore constructor)
- ✅ **Completion**: [TASK-4.2-IMPLEMENTATION-COMPLETE.md](TASK-4.2-IMPLEMENTATION-COMPLETE.md)

#### [Task 4.3: Code Quality & Consistency](Task-4.3-Code-Quality-And-Consistency.md) ✅ COMPLETE
**MEDIUM** - Clean up codebase
- ✅ Fixed 1 namespace inconsistency (SecurityHeadersMiddleware.cs)
- ✅ Resolved/documented all 27 TODOs (categorized, tracked, or removed)
- ✅ Created comprehensive .editorconfig (297 lines of C# formatting rules)
- ✅ Standardized on TypedResults (92 replacements across 7 files)
- ✅ Applied dotnet format to entire solution
- ✅ Main API builds with 0 warnings, 0 errors
- ✅ **Completion**: [TASK-4.3-IMPLEMENTATION-COMPLETE.md](TASK-4.3-IMPLEMENTATION-COMPLETE.md)
- ✅ **TODO Audit**: [TODO-Resolution.md](TODO-Resolution.md) - All TODOs documented

---

## Effort Summary

| Phase | Tasks | Total Effort | With 2 Devs | With 3 Devs |
|-------|-------|--------------|-------------|-------------|
| Phase 1 | 1.1, 1.2 | ~~7-11~~ **10-13** days | ~~4-6~~ **5-7** days | ~~3-4~~ **4-5** days |
| Phase 2 | 2.1, 2.2 | 9-12 days | 5-7 days | 4-5 days |
| Phase 3 | 3.1, 3.2 | 7-9 days | 4-5 days | 3-4 days |
| Phase 4 | 4.1-4.3 | 8-10 days | 5-6 days | 3-4 days |
| **Total** | **10 tasks** | ~~**31-42**~~ **34-44 days** | ~~**18-24**~~ **19-25 days** | ~~**13-17**~~ **14-18 days** |

**Note**: Phase 1 increased by 3 days due to Task 1.1 AccessRights revision (UI integration + PCF control spec)

**Recommended**: 2 senior C# developers, 6 weeks

---

## Sprint Success Criteria

### Security ✅
- Authorization enabled and enforced on all endpoints
- Real Dataverse permission queries
- Integration tests validate 401/403/200 responses
- Audit logging for all authorization decisions

### Deployment ✅
- Application starts with valid config (all environments)
- Fails fast with clear errors on invalid config
- Deployment guide validated (dev → staging → prod)
- Secrets in Key Vault (prod) and user-secrets (dev)

### Functionality ✅
- OboSpeService uses real Graph SDK (zero mock data)
- All file operations functional (list, download, upload, delete)
- Single Dataverse implementation (Web API only)

### Architecture ✅
- SpeFileStore < 150 lines (delegating to focused services)
- Unified background job processing (Service Bus)
- Centralized resilience via `DelegatingHandler`
- All ADRs followed (003, 004, 007, 010)

### Code Quality ✅
- All namespaces consistent (`Spe.Bff.Api.*`)
- Zero TODO comments
- `.editorconfig` enforced
- XML docs on public APIs
- Code coverage ≥ 70%

---

## How to Use This Sprint

### For AI-Assisted Development
1. Open task file (e.g., `Task-1.1-Authorization-Implementation.md`)
2. Read Context & Problem Statement
3. Review Implementation Steps
4. **Use AI Coding Prompts** (copy-paste to Claude/Copilot)
5. Follow Testing Strategy
6. Check Validation Checklist before marking complete

### For Senior Developers
1. Assign tasks based on dependencies and developer strengths
2. Monitor progress via validation checklists
3. Review code against standards in each task
4. Validate ADR compliance
5. Approve when Definition of Done met

### For Project Managers
1. Track progress (10 tasks total)
2. Monitor blockers (🔴 tasks are highest priority)
3. Report status using metrics dashboard
4. Escalate risks early
5. Plan Sprint 4 based on retrospective

---

## Task Files

| # | Task | Priority | Days | Status | File |
|---|------|----------|------|--------|------|
| 1.1 | Authorization Implementation (REVISED) | 🔴 CRITICAL | ~~5-8~~ **8-10** | ✅ | [Task-1.1-REVISED-AccessRights-Authorization.md](Task-1.1-REVISED-AccessRights-Authorization.md) |
| 1.2 | Configuration & Deployment | 🔴 CRITICAL | 2-3 | ✅ | [Task-1.2-Configuration-And-Deployment-Setup.md](Task-1.2-Configuration-And-Deployment-Setup.md) |
| 2.1 | OboSpeService Real Implementation | 🔴 CRITICAL | 8-10 | ✅ | [Task-2.1-OboSpeService-Real-Implementation.md](Task-2.1-OboSpeService-Real-Implementation.md) |
| 2.2 | Dataverse Cleanup | 🟡 HIGH | 1-2 | ✅ | [Task-2.2-Dataverse-Cleanup.md](Task-2.2-Dataverse-Cleanup.md) |
| 3.1 | Background Job Consolidation | 🟡 HIGH | 2-3 | ✅ | [Task-3.1-Background-Job-Consolidation.md](Task-3.1-Background-Job-Consolidation.md) |
| 3.2 | SpeFileStore Refactoring | 🟡 HIGH | 5-6 | ✅ | [Task-3.2-SpeFileStore-Refactoring.md](Task-3.2-SpeFileStore-Refactoring.md) |
| 4.1 | Centralized Resilience | 🟢 MEDIUM | 2-3 | ✅ | [Task-4.1-Centralized-Resilience.md](Task-4.1-Centralized-Resilience.md) |
| 4.2 | Testing Improvements | 🟢 MEDIUM | 4-5 | ✅ | [Task-4.2-Testing-Improvements.md](Task-4.2-Testing-Improvements.md) |
| 4.3 | Code Quality & Consistency | 🟢 MEDIUM | 2 | ✅ | [Task-4.3-Code-Quality-And-Consistency.md](Task-4.3-Code-Quality-And-Consistency.md) |

---

## Related Documentation

### Sprint 3 Core Documents
- [Architecture Update: AccessRights Summary](ARCHITECTURE-UPDATE-AccessRights-Summary.md) - **Cross-task impact analysis**
- [Sprint 3 Tasks Update Summary](SPRINT-3-TASKS-UPDATE-SUMMARY.md) - **Detailed changes per task**
- [Task 1.1 PCF Control Specification](Task-1.1-PCF-Control-Specification.md) - **UI integration spec**

### Sprint 2 & ADRs
- [Sprint 2 Code Review](../Sprint%202/sprint%202%20code%20review.docx) - Source of weaknesses
- [ADR-003: Lean Authorization](../../../docs/adr/ADR-003-lean-authorization-seams.md)
- [ADR-004: Async Job Contract](../../../docs/adr/ADR-004-async-job-contract.md)
- [ADR-007: SPE Storage Seam](../../../docs/adr/ADR-007-spe-storage-seam-minimalism.md)
- [ADR-010: DI Minimalism](../../../docs/adr/ADR-010-di-minimalism.md)

---

**Let's ship secure, production-ready code!** 🚀
