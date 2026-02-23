# Lessons Learned — Email Communication Solution R1 (Phases 6-9)

**Date**: 2026-02-22
**Scope**: Extension phases (Communication Account Management, Individual Send, Inbound Monitoring, Verification)

---

## What Went Well

### 1. Parallel Task Execution
- Phases 7, 8, and 9 all depended only on Phase 6 completion (task 055), enabling true cross-phase parallelism
- Tasks 060, 070, 080 launched simultaneously — cutting elapsed time significantly
- Within each phase, independent tasks (e.g., web resource + form UX, backup polling + inbound fields) ran in parallel
- Dependency graph in TASK-INDEX.md proved invaluable for identifying parallel-safe work

### 2. Existing Patterns Scaled Cleanly
- CommunicationAccountService followed the exact same query + Redis cache pattern as the original approved sender flow
- BackgroundService pattern (ADR-001) worked cleanly for GraphSubscriptionManager and InboundPollingBackupService
- Concrete DI registration (ADR-010) via `AddCommunicationModule()` kept registration manageable even with 6+ new services
- The `IJobHandler` pattern from FinanceModule was reused directly for IncomingCommunicationJobHandler

### 3. Two-Tier Config Model
- Keeping appsettings.json as fallback (config base + Dataverse overlay) proved valuable during development
- When Dataverse queries fail, the system degrades gracefully to config-only senders
- Redis 5-minute cache TTL provides good balance between freshness and performance

### 4. Explicit Scope Boundaries
- "Association resolution is a separate AI project" — this clear constraint prevented scope creep in task 072
- Incoming emails create records with empty regarding fields, documented with explicit tests
- Every test file includes a "DoesNotSetRegardingFields" test as a guard

## What Could Be Improved

### 1. Dataverse Field Name Discovery
- `sprk_sendenableds` (trailing 's') and `sprk_communiationtype` (intentional typo) caused initial confusion
- **Recommendation**: Always verify exact field names against actual Dataverse schema before coding
- The `design-communication-accounts.md` field mapping table was essential for this

### 2. Test Project Pre-existing Errors
- The test project has 72+ compilation errors in unrelated files (DataverseUpdateToolHandlerTests, InvoiceExtractionToolHandlerTests, FinancialCalculationToolHandlerTests)
- These leaked from other branches' test files into this worktree
- Made it impossible to run `dotnet test` for full validation — only API build (`dotnet build src/server/api/`) could be verified
- **Recommendation**: Clean up cross-branch test file contamination before starting new projects

### 3. Graph SDK Kiota Migration
- Graph SDK v5+ uses Kiota-generated types with different mock patterns
- `RequestConfiguration<T>` replaced the old builder-specific configuration types
- Test mocking required careful attention to the new async patterns
- **Recommendation**: Document Kiota mock patterns in `.claude/patterns/testing/`

## Patterns Discovered

### 1. Multi-Layer Deduplication
Incoming email processing uses three deduplication layers:
1. **Webhook level**: ConcurrentDictionary for in-flight notification dedup
2. **Job level**: ServiceBus idempotency key prevents duplicate processing
3. **Dataverse level**: Check `sprk_graphmessageid` before creating records

### 2. MSAL in Dataverse Web Resources
Individual send mode required MSAL authentication in the JavaScript web resource:
- `PublicClientApplication` with `acquireTokenSilent` → `ssoSilent` → `acquireTokenPopup` strategy
- Token used for OBO flow via BFF (passed as bearer token in Authorization header)
- BFF extracts user identity from token claims for `ForUserAsync()` call

### 3. Background Service Resilience
Both GraphSubscriptionManager and InboundPollingBackupService follow the same resilience pattern:
- `PeriodicTimer` for regular intervals (30min / 5min)
- Try/catch around entire cycle — never let one failure kill the service
- Individual account errors don't block processing of other accounts
- Logged as warnings, not errors, for routine transient failures

## Recommendations for Future Work

1. **Association Resolution AI Project**: The infrastructure is ready — incoming records exist with empty regarding fields. The AI project should query "Unassociated Incoming" view and match based on sender email + subject patterns.

2. **Dataverse-Level Dedup Query**: Add `ExistsByGraphMessageIdAsync` to `IDataverseService` for true Dataverse-level duplicate detection on incoming messages.

3. **Exchange Application Access Policy Automation**: Currently documented as manual PowerShell steps. Could be automated with a BFF admin endpoint.

4. **Monitoring Dashboard**: Add Application Insights custom metrics for subscription health, processing latency, and error rates.

---

*Generated during project wrap-up on 2026-02-22*
