# Email-to-Document Automation - Lessons Learned

> **Project**: Email-to-Document Automation
> **Completed**: 2026-01-09
> **Duration**: 10 weeks (5 phases)

---

## What Went Well

### 1. Hybrid Trigger Architecture
The webhook + polling backup model proved robust. The webhook provides near real-time processing while the polling backup catches any emails missed due to webhook failures or network issues.

### 2. ADR Compliance
Following established ADRs (especially ADR-001, ADR-004, ADR-008) accelerated development:
- ADR-001: BackgroundService pattern worked seamlessly
- ADR-004: JobContract schema simplified job processing
- ADR-008: Endpoint filters provided clean authorization model

### 3. Reuse of Existing Components
Leveraging existing services significantly reduced development time:
- `SpeFileStore` for SharePoint Embedded operations
- `ServiceBusJobProcessor` pattern for job handling
- `TextExtractorService` (extended for .eml support)
- `IDataverseService` for all Dataverse operations

### 4. MimeKit for RFC 5322 Compliance
MimeKit library proved excellent for .eml file generation:
- Full RFC 5322 compliance out of the box
- Robust attachment handling
- Good text extraction capabilities

### 5. Test Coverage
The unit test suite (1132+ tests) caught issues early and provided confidence for refactoring.

---

## What Could Be Improved

### 1. Task 033 Architecture Change
Task 033 (AI processing enqueue) was cancelled because the architecture changed during Phase 4:
- Original plan: Background job triggers AI processing automatically
- Actual: PCF triggers AI processing (requires OBO auth token)

**Lesson**: For features requiring OAuth flows, verify the auth flow early in design.

### 2. PCF Bundle Size
EmailProcessingMonitor PCF initially had a 9.9 MiB bundle:
- Root cause: Importing icons as separate modules
- Fix: Inline SVG icons reduced to 2.06 MiB

**Lesson**: For PCF controls, inline small assets rather than importing from libraries.

### 3. React 16 Compatibility
PCF controls must use React 16 APIs (not React 18):
- `useId` hook not available
- `createRoot` not available

**Lesson**: Always check ADR-022 (PCF Platform Libraries) before using React features.

### 4. .NET Framework Reference Assemblies
Building PCF controls that reference .NET Standard libraries required:
```xml
<PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3">
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

**Lesson**: Document PCF build dependencies in module CLAUDE.md.

---

## Technical Decisions That Worked

### 1. Confidence Scoring for Associations
The 0.0-1.0 confidence score for email-to-entity associations:
- Provides transparency to users
- Enables manual override for low-confidence matches
- Tracking token matching (0.95) vs domain matching (0.4) clearly differentiated

### 2. DLQ Re-drive Tooling
Building DLQ management endpoints (list, get, re-drive) from the start:
- Saved significant debugging time
- Enabled self-service ops during testing
- Production-ready admin tooling

### 3. Idempotency via Alternate Keys
Using `(sprk_emailactivityid, sprk_isemailarchive)` alternate key:
- Guaranteed no duplicates at storage level
- Simplified idempotency checks
- No reliance on Redis/cache for deduplication

### 4. Redis Caching for Filter Rules
5-minute TTL cache for email filter rules:
- Reduced Dataverse queries significantly
- Rules changes propagate within acceptable window
- Admin can force cache refresh via API

---

## Technical Debt Introduced

### 1. Webhook Secret Storage
Currently webhook secret is stored in app settings. Should migrate to Key Vault:
```json
"Email:WebhookSecret": "..."  // TODO: Move to Key Vault
```

### 2. Hard-coded Configuration Values
Some configuration values are hard-coded and should be made configurable:
- Batch processing max concurrency (5)
- Polling interval (5 minutes)
- DLQ re-drive batch size (100)

### 3. Integration Test Coverage
Integration tests require live Azure services (Service Bus, Redis). Consider:
- Adding Testcontainers for local integration testing
- Mocking Azure services for CI/CD pipeline

---

## Key Metrics

| Metric | Target | Achieved |
|--------|--------|----------|
| Unit test count | 1000+ | 1132 |
| Build time | < 2 min | ~90 sec |
| API response time (P95) | < 2s | < 1s |
| Email processing time (P95) | < 2 min | < 60s |
| Batch throughput | 100/min | 100+/min |

---

## Recommendations for Similar Projects

1. **Design auth flows first** - OBO vs app-only impacts architecture significantly
2. **Build admin tooling early** - DLQ management, stats endpoints save debugging time
3. **Check PCF constraints** - React version, bundle size limits, unmanaged solutions
4. **Leverage existing patterns** - ADRs and existing services accelerate development
5. **Test with diverse data** - Emails vary wildly; test with real-world samples

---

## Files of Note

| File | Purpose | Notes |
|------|---------|-------|
| [RUNBOOK.md](docs/RUNBOOK.md) | Production operations | Complete troubleshooting guide |
| [ADMIN-GUIDE.md](docs/ADMIN-GUIDE.md) | Admin training | Manual operations reference |
| [DEPLOYMENT-CHECKLIST.md](docs/DEPLOYMENT-CHECKLIST.md) | Deploy verification | Pre/post checks |
| [tests/load/](tests/load/) | Load testing | k6 scripts for performance |

---

*Created: 2026-01-09*
