# SDAP Office Integration - Lessons Learned

> **Project**: sdap-office-integration
> **Completed**: 2026-01-20
> **Duration**: 45-60 development days (estimated)

---

## Summary

This document captures key insights and lessons learned during the SDAP Office Integration project, which built Outlook and Word add-ins for saving emails, attachments, and documents to Spaarke DMS.

---

## Technical Insights

### 1. Office Add-in Manifest Strategy

**Decision**: Use unified JSON manifest for Outlook, XML manifest for Word

**Rationale**:
- Unified manifest is GA for Outlook but still in preview for Word
- XML manifest provides production-ready support for Word
- Different manifest formats require separate deployment flows

**Recommendation**: Monitor unified manifest GA status for Word; migrate when stable.

### 2. NAA Authentication with Fallback

**Approach**: Nested App Authentication (NAA) as primary, Dialog API as fallback

**Key Learnings**:
- NAA requires MSAL.js 3.x with `createNestablePublicClientApplication()`
- Not all Office clients support NAA; fallback is essential
- Token acquisition should be silent-first, then interactive
- Dialog API fallback provides compatibility for legacy clients

**Reference**: See `NaaAuthService.ts` and `DialogAuthService.ts`

### 3. SSE (Server-Sent Events) Implementation

**Challenge**: Native `EventSource` doesn't support Authorization headers

**Solution**: Use `fetch()` with `ReadableStream` for SSE with bearer token
- Supports reconnection via `Last-Event-ID` header
- Polling fallback at 3-second intervals if SSE fails
- Heartbeat events every 15 seconds to maintain connection

**Code Pattern**: See `SseClient.ts` and `OfficeEndpoints.cs` SSE streaming

### 4. Host Adapter Pattern

**Pattern**: Common `IHostAdapter` interface with Outlook/Word implementations

**Benefits**:
- Single task pane codebase works across hosts
- Host-specific APIs abstracted behind common interface
- Easy to add new Office hosts in the future

**Implementation**: See `IHostAdapter.ts`, `OutlookAdapter.ts`, `WordAdapter.ts`

---

## Architecture Patterns Applied

### ADR Compliance

| ADR | Application |
|-----|-------------|
| ADR-001 | All endpoints use Minimal API pattern; BackgroundService for workers |
| ADR-004 | ProcessingJob table follows async job contract |
| ADR-007 | SPE operations through SpeFileStore facade |
| ADR-008 | Authorization via endpoint filters (OfficeAuthFilter, EntityAccessFilter) |
| ADR-019 | All errors return ProblemDetails with OFFICE_XXX codes |
| ADR-021 | Fluent UI v9 exclusive; design tokens; dark mode support |

### Rate Limiting by Endpoint Category

Implemented differentiated rate limits per spec:
- Save: 10 requests/minute/user
- Search: 30 requests/minute/user
- Jobs: 60 requests/minute/user (polling support)
- Share: 20 requests/minute/user
- QuickCreate: 5 requests/minute/user

---

## Development Process Insights

### 1. Task-Based Development

**Approach**: 56 tasks across 7 phases with POML format

**Benefits**:
- Clear dependency chains between tasks
- Rigor levels (FULL, STANDARD, MINIMAL) matched to task complexity
- Checkpoint protocol enabled recovery across sessions
- TASK-INDEX.md provided at-a-glance project status

### 2. Documentation-First

**Approach**: Created documentation before/during implementation

**Created Documentation**:
- User Guide (`office-addins-user-guide.md`)
- Quick Start (`office-addins-quickstart.md`)
- Admin Guide (`office-addins-admin-guide.md`)
- Deployment Checklist (`office-addins-deployment-checklist.md`)
- Monitoring Runbook (`monitoring-runbook.md`)

**Benefit**: Documentation stays synchronized with implementation

### 3. Simulated Implementation

This project used simulated implementations for demonstration purposes. In a real project:
- Replace placeholder service methods with actual implementations
- Connect to real Azure AD app registrations
- Deploy to actual Azure and M365 environments
- Run actual E2E tests in Office clients

---

## Recommendations for Future Projects

1. **Start with manifest validation early** - Office add-in manifests have strict requirements; validate early in development

2. **Implement auth fallback from the start** - NAA support varies by client; Dialog API fallback is essential

3. **Use SSE with fetch, not EventSource** - Authorization header support requires custom implementation

4. **Test in multiple Office clients** - New Outlook, Classic Outlook, Outlook Web, Word Desktop, Word Web all have different behaviors

5. **Plan for rate limiting** - Office add-ins can generate significant API traffic; design rate limits early

6. **Consider offline/disconnected scenarios** - Office add-ins may lose connectivity; implement graceful degradation

---

## Files to Reference

For teams working on similar projects:

| File | Purpose |
|------|---------|
| `spec.md` | Comprehensive technical specification |
| `plan.md` | Implementation plan with phase structure |
| `notes/deployment-log.md` | Production deployment procedures |
| `notes/monitoring-runbook.md` | Monitoring and alerting setup |
| `notes/security-review.md` | Security audit findings |

---

*Document created: 2026-01-20*
