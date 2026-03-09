# R1 Assessment: Phase C — Inbound Shared Mailbox Monitoring

> **Task**: ECS-020 | **Date**: 2026-03-09 | **Assessor**: Claude Code (Opus 4.6)
> **Branch**: `work/email-communication-solution-r2`

---

## 1. Component Inventory

| Component | File | Lines | R1 Origin |
|-----------|------|-------|-----------|
| GraphSubscriptionManager | `src/server/api/Sprk.Bff.Api/Services/Communication/GraphSubscriptionManager.cs` | 352 | Task 071 |
| IncomingCommunicationProcessor | `src/server/api/Sprk.Bff.Api/Services/Communication/IncomingCommunicationProcessor.cs` | 511 | Task 072 |
| InboundPollingBackupService | `src/server/api/Sprk.Bff.Api/Services/Communication/InboundPollingBackupService.cs` | 221 | Task 072 |
| CommunicationEndpoints (webhook) | `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs` | Lines 312-538 | Task 072 |
| IncomingCommunicationJobHandler | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/IncomingCommunicationJobHandler.cs` | 222 | Task 072 |
| GraphChangeNotification models | `src/server/api/Sprk.Bff.Api/Services/Communication/Models/GraphNotification.cs` | 80 | Task 072 |
| IncomingCommunicationJob model | `src/server/api/Sprk.Bff.Api/Services/Communication/Models/IncomingCommunicationJob.cs` | 7 | Task 072 |
| CommunicationModule (DI) | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CommunicationModule.cs` | 44 | Task 070+ |

---

## 2. Per-Component Detailed Analysis

### 2.1 GraphSubscriptionManager — 92% R2-Ready

**File**: `GraphSubscriptionManager.cs` (352 lines)

**What works (R2-aligned):**
- ADR-001 BackgroundService + PeriodicTimer pattern (line 58)
- 30-minute tick interval, 24-hour renewal threshold, 3-day subscription lifetime (lines 21-23)
- Full lifecycle: Create / Renew / Recreate / Skip (enum lines 345-351)
- Per-account subscription management iterating `QueryReceiveEnabledAccountsAsync` (line 102)
- Subscription creation targets `users/{email}/mailFolders/{monitorFolder}/messages` (line 248)
- `clientState` validated via configuration `Communication:WebhookClientState` (line 46)
- Graceful renewal failure handling: 404 -> recreate, other error -> delete + recreate (lines 208-231)
- `TryDeleteSubscriptionAsync` swallows 404 safely (lines 298-323)
- Updates Dataverse `sprk_subscriptionid` + `sprk_subscriptionexpiry` on account (lines 329-343)
- Error isolation per account — one failure doesn't stop the loop (lines 149-155)
- Correlation ID tracing per cycle (line 93)
- Metrics counters: created/renewed/recreated/skipped/failed (lines 122-161)

**Gaps vs R2 spec:**

| # | Gap | Severity | Spec Reference | Line(s) |
|---|-----|----------|----------------|---------|
| G1 | Does NOT update `sprk_subscriptionstatus` field on account record | Medium | Spec "sprk_subscriptionstatus" field in entity schema; unresolved question about Active=100000 vs 100000000 | Line 332-337 (UpdateAccountSubscriptionAsync only sets subscriptionid + expiry) |
| G2 | No cleanup when account is disabled (no subscription deletion on `sprk_receiveenabled` toggle off) | Low | Implicit: if account removed from `QueryReceiveEnabledAccountsAsync`, orphaned Graph subscriptions persist until expiry | Service only acts on receive-enabled accounts |
| G3 | No feature flag / kill switch for receive functionality | Low | ADR-018 Feature Flags mentioned in spec | Not present |

**Changes needed for R2:**
1. Add `sprk_subscriptionstatus` update in `UpdateAccountSubscriptionAsync` (line 332) — set to Active on create/renew, verify the correct OptionSetValue (100000 vs 100000000, unresolved in spec)
2. Consider adding a cleanup pass for disabled accounts (accounts with `subscriptionId` but `receiveEnabled=false`) — nice to have
3. Feature flag integration if ADR-018 is implemented in R2 scope

---

### 2.2 IncomingCommunicationProcessor — 75% R2-Ready

**File**: `IncomingCommunicationProcessor.cs` (511 lines)

**What works (R2-aligned):**
- 7-step processing pipeline clearly documented (Steps 1-7, lines 70-198)
- Step 2: Account lookup via `GetReceiveAccountAsync` (line 83)
- Step 3: Graph message fetch with `$expand=attachments` in single call (lines 97-112) — matches spec
- Step 4: `CreateCommunicationRecordAsync` sets all required fields (lines 257-319):
  - `sprk_communicationtype` = Email (100000000) — FIXED (was typo `sprk_communiationtype`)
  - `statuscode` = Delivered (659490003)
  - `sprk_direction` = Incoming (100000000)
  - `sprk_graphmessageid` set for dedup
  - `sprk_from`, `sprk_to`, `sprk_cc` all mapped
  - `sprk_hasattachments` + `sprk_attachmentcount`
  - Explicit comment: does NOT set regarding fields (correct per spec — association is separate)
- Step 5: Attachment processing with filtering via `EmailAttachmentProcessor.ShouldFilterAttachment` (line 366)
  - Uploads to SPE, creates `sprk_communicationattachment` records
  - Non-fatal error handling (line 154-162)
- Step 6: EML archival via `EmlGenerationService` + SPE upload (lines 422-490)
  - Creates `sprk_document` record with correct type/source
  - Non-fatal error handling (line 172-178)
- Step 7: Mark message as read in Graph (lines 496-507) — prevents backup polling re-pickup
- Error isolation: Steps 5, 6, 7 are all non-fatal with try/catch (processor continues)
- Uses `GraphClientFactory.ForApp()` per ADR-007

**Gaps vs R2 spec:**

| # | Gap | Severity | Spec Reference | Line(s) |
|---|-----|----------|----------------|---------|
| G4 | **Deduplication is STUBBED** — `ExistsByGraphMessageIdAsync` always returns `false` | **HIGH** | FR-14: "uses Redis idempotency key + sprk_graphmessageid uniqueness"; spec mandates `Communication:{graphMessageId}:Process` idempotency key | Lines 209-228 |
| G5 | No Redis processing lock (5-min TTL) around job execution | Medium | Spec NFR-04: "processing locks 5 minutes" | Not present |
| G6 | **Association resolution is NOT implemented** — spec says simple binary matching (thread, sender, subject, mailbox context) | **HIGH** | FR-08: "auto-linked to entities via priority cascade"; spec says "simple binary matching" not AI | Line 19-20 explicitly defers this |
| G7 | No `sprk_receivedat` field set on communication record | Low | Spec entity schema shows `sprk_receivedat` field; code sets `sprk_sentat` (line 294) but not `sprk_receivedat` | Line 276-299 |
| G8 | `sprk_ArchiveIncomingOptIn` not checked before archival | Medium | FR-12: "toggling to No skips archival for that direction" | Lines 165-178 (archives unconditionally) |
| G9 | No AI analysis enqueue after attachment processing | Low | FR-10: "AI analysis enqueued" after document archival | Steps 5-6 don't enqueue analysis |
| G10 | IdempotencyKey format mismatch: code uses `IncomingComm:{subscriptionId}:{messageId}` (endpoint, line 467) vs spec `Communication:{graphMessageId}:Process` | Medium | Spec Service Bus section | CommunicationEndpoints.cs line 467 |

**Changes needed for R2:**
1. **CRITICAL**: Implement `ExistsByGraphMessageIdAsync` — add Dataverse query for `sprk_communication` where `sprk_graphmessageid` matches (line 209-228)
2. **CRITICAL**: Implement association resolution service with priority cascade: In-Reply-To thread, sender contact/account match, subject line pattern, mailbox context (FR-08)
3. Add Redis processing lock in `ProcessAsync` before executing steps (5-min TTL)
4. Set `sprk_receivedat` field on communication record (line 294 area)
5. Check `account.ArchiveIncomingOptIn` before EML archival (line 165)
6. Align idempotency key format to spec: `Communication:{graphMessageId}:Process`
7. (Later/Phase E) Enqueue AI analysis after document creation

---

### 2.3 InboundPollingBackupService — 55% R2-Ready

**File**: `InboundPollingBackupService.cs` (221 lines)

**What works (R2-aligned):**
- ADR-001 BackgroundService + PeriodicTimer pattern (line 60)
- 5-minute polling interval confirmed (line 22) — matches NFR-03
- Per-account polling iterating `QueryReceiveEnabledAccountsAsync` (line 103)
- In-memory `ConcurrentDictionary<Guid, DateTimeOffset>` for last-poll tracking (line 34)
- 15-minute initial lookback window on restart (line 40)
- Graph query with `receivedDateTime ge {filterDateTime}` filter (line 177)
- Selects only needed fields: id, receivedDateTime, subject, from, isRead (line 179)
- Top 50 limit per poll (line 178)
- Error isolation per account (lines 133-139)
- Correlation ID tracing (line 94)
- Updates poll timestamp after each poll (lines 192, 217)

**Gaps vs R2 spec:**

| # | Gap | Severity | Spec Reference | Line(s) |
|---|-----|----------|----------------|---------|
| G11 | **Does NOT enqueue jobs** — only logs found messages | **CRITICAL** | FR-07: "catches missed messages within 5 minutes"; must enqueue `ProcessIncomingCommunication` jobs | Lines 201-214 (just logs) |
| G12 | Does not filter by `isRead == false` — will find already-processed messages | Medium | Processor marks as read (Step 7); polling should only pick unread | Line 177 (filter is only receivedDateTime) |
| G13 | No injection of `JobSubmissionService` to enqueue jobs | High | Required to submit jobs | Constructor (lines 42-50) |
| G14 | `IncomingCommunicationJob` model exists but is unused by this service | Low | Model at `Models/IncomingCommunicationJob.cs` | Not referenced |
| G15 | Does not respect `account.MonitorFolder` in filter (uses it in URL but not for folder-specific queries) | Low | Works correctly — uses mailFolders/{folder} in Graph URL path | Line 173 (actually OK) |

**Changes needed for R2:**
1. **CRITICAL**: Inject `JobSubmissionService` and enqueue `IncomingCommunication` jobs for each unread message found
2. Add `isRead eq false` to Graph query filter (line 177) to skip already-processed messages
3. Build job payload matching the format expected by `IncomingCommunicationJobHandler` (subscriptionId can be null for polling-triggered jobs, TriggerSource = "BackupPolling")
4. Consider setting `TriggerSource` in job payload to distinguish polling-triggered from webhook-triggered

---

### 2.4 CommunicationEndpoints (Webhook) — 90% R2-Ready

**File**: `CommunicationEndpoints.cs` (lines 312-538)

**What works (R2-aligned):**
- `POST /api/communications/incoming-webhook` registered as `AllowAnonymous` (line 83) — correct per ADR-008 note
- Step 1: Graph subscription validation — echoes `validationToken` as text/plain 200 (lines 339-348)
- Step 2-3: Body parsing + JSON deserialization into `GraphChangeNotificationCollection` (lines 351-388)
- Step 4: `clientState` validation per notification (lines 391-419) — rejects entire batch on mismatch
- Step 5: In-memory deduplication with `ConcurrentDictionary` + 10-minute window (lines 35-40, 422-436)
- Step 6: Extracts mailbox + messageId from resource path (lines 438-449)
- Step 7: Enqueues `IncomingCommunication` job via `JobSubmissionService` (lines 452-478)
  - IdempotencyKey: `IncomingComm:{subscriptionId}:{messageId}` (line 467)
  - MaxAttempts: 3 (line 469)
  - Payload includes SubscriptionId, Resource, MessageId, ChangeType, TenantId, TriggerSource
- Step 8: Returns 202 Accepted with summary (lines 481-494) — fast response for NFR-01
- Dedup cache pruning on each batch (line 427)
- Full error handling with ProblemDetails (lines 497-505)
- `ExtractLastSegment` helper for resource path parsing (lines 512-521)

**Gaps vs R2 spec:**

| # | Gap | Severity | Spec Reference | Line(s) |
|---|-----|----------|----------------|---------|
| G16 | IdempotencyKey format `IncomingComm:{subscriptionId}:{messageId}` differs from spec `Communication:{graphMessageId}:Process` | Medium | Spec Service Bus section | Line 467 |
| G17 | Does not validate subscription ID against known accounts (could accept notifications for unknown subscriptions) | Low | Implicit security hardening | Line 403 (validates clientState but not subscriptionId) |
| G18 | `_recentNotifications` is static in-memory only — not shared across instances in scale-out | Low | NFR-04 mentions Redis idempotency; webhook dedup should use Redis for multi-instance | Lines 35-36 |
| G19 | No HMAC or signature validation beyond clientState | Low | Spec says clientState is sufficient | OK per spec |

**Changes needed for R2:**
1. Align idempotency key format to `Communication:{messageId}:Process` (line 467)
2. (Optional) Add subscription ID validation against Dataverse accounts for additional security
3. (Scale-out) Consider Redis-backed dedup cache for multi-instance deployments

---

### 2.5 IncomingCommunicationJobHandler — 95% R2-Ready

**File**: `IncomingCommunicationJobHandler.cs` (222 lines)

**What works (R2-aligned):**
- Implements `IJobHandler` interface with `JobType = "IncomingCommunication"` (lines 27, 35)
- Parses payload (lines 158-175) and extracts mailbox from resource path (lines 139-156)
- Delegates to `IncomingCommunicationProcessor.ProcessAsync` (line 102)
- Retryable exception detection: HttpRequestException, TaskCanceledException, TimeoutException, ODataError 429/503/504 (lines 180-195)
- Proper `JobOutcome` returns: Success, Failure (retryable), Poisoned (terminal) (lines 110, 123, 128)
- Stopwatch timing for telemetry (line 49)
- Null/missing payload guards with Poisoned outcomes (lines 61-93)

**Gaps vs R2 spec:**

| # | Gap | Severity | Spec Reference | Line(s) |
|---|-----|----------|----------------|---------|
| G20 | Job type name `IncomingCommunication` vs spec `ProcessIncomingCommunication` | Low | Spec says "ProcessIncomingCommunication"; code uses shorter name | Line 35 |

**Changes needed for R2:**
1. Minor: consider renaming job type to `ProcessIncomingCommunication` for spec alignment (line 35), or document the intentional deviation
2. No functional changes required

---

### 2.6 GraphNotification Models — 100% R2-Ready

**File**: `GraphNotification.cs` (80 lines)

All models are complete and correctly structured:
- `GraphChangeNotificationCollection` with `value` array (lines 9-13)
- `GraphChangeNotification` with all required fields: subscriptionId, clientState, changeType, resource, tenantId, resourceData (lines 20-42)
- `GraphResourceData` with OData properties + id (lines 48-61)
- `IncomingWebhookResponse` for 202 Accepted response (lines 66-79)
- All use `System.Text.Json` attributes (correct for Minimal API)

No changes needed.

---

### 2.7 DI Registration (CommunicationModule) — 100% R2-Ready

**File**: `CommunicationModule.cs` (44 lines)

All inbound components are registered:
- `IncomingCommunicationProcessor` as Singleton (line 26)
- `IncomingCommunicationJobHandler` as Scoped `IJobHandler` (line 34)
- `GraphSubscriptionManager` as HostedService (line 37)
- `InboundPollingBackupService` as HostedService (line 40)

No changes needed for existing registrations. New services (AssociationResolver, etc.) will need registration.

---

## 3. Gap Summary

### Critical Gaps (Must Fix for R2)

| ID | Component | Gap | Impact |
|----|-----------|-----|--------|
| **G4** | IncomingCommunicationProcessor | Dedup `ExistsByGraphMessageIdAsync` always returns `false` | Duplicate `sprk_communication` records on webhook retry + polling overlap |
| **G6** | IncomingCommunicationProcessor | Association resolution not implemented | Incoming emails have no matter/contact/org linkage (FR-08) |
| **G11** | InboundPollingBackupService | Only logs found messages, does not enqueue jobs | Backup polling is non-functional — missed webhooks are lost |

### Medium Gaps (Should Fix for R2)

| ID | Component | Gap | Impact |
|----|-----------|-----|--------|
| **G1** | GraphSubscriptionManager | `sprk_subscriptionstatus` not updated | Admin UI shows stale subscription status |
| **G5** | IncomingCommunicationProcessor | No Redis processing lock (5-min TTL) | Concurrent processing of same message possible |
| **G8** | IncomingCommunicationProcessor | `sprk_ArchiveIncomingOptIn` not checked | Cannot opt out of inbound EML archival |
| **G10/G16** | Endpoints + Processor | IdempotencyKey format mismatch with spec | Inconsistency; may affect cross-system dedup |
| **G12** | InboundPollingBackupService | No `isRead == false` filter | Polls already-processed messages, wastes Graph API calls |
| **G13** | InboundPollingBackupService | No `JobSubmissionService` injection | Prerequisite for G11 fix |

### Low Gaps (Nice to Have)

| ID | Component | Gap |
|----|-----------|-----|
| G2 | GraphSubscriptionManager | No cleanup of subscriptions for disabled accounts |
| G3 | GraphSubscriptionManager | No feature flag / kill switch |
| G7 | IncomingCommunicationProcessor | `sprk_receivedat` not set on record |
| G9 | IncomingCommunicationProcessor | No AI analysis enqueue (Phase E scope) |
| G14 | InboundPollingBackupService | `IncomingCommunicationJob` model unused |
| G17 | Webhook endpoint | No subscription ID validation against known accounts |
| G18 | Webhook endpoint | In-memory dedup not shared across instances |
| G20 | JobHandler | Job type name `IncomingCommunication` vs spec `ProcessIncomingCommunication` |

---

## 4. R2 Readiness Per Component

| Component | R2 Readiness | Critical Gaps | Effort to Complete |
|-----------|-------------|---------------|-------------------|
| GraphSubscriptionManager | **92%** | 0 | ~1 hour (add subscription status update) |
| IncomingCommunicationProcessor | **75%** | 2 (G4, G6) | ~6-8 hours (dedup query + association resolver) |
| InboundPollingBackupService | **55%** | 1 (G11) | ~2-3 hours (inject job service, enqueue, filter) |
| Webhook Endpoint | **90%** | 0 | ~1 hour (idempotency key alignment) |
| JobHandler | **95%** | 0 | ~15 min (optional rename) |
| Notification Models | **100%** | 0 | None |
| DI Registration | **100%** | 0 | None (new services need separate registration) |
| **Overall Phase C** | **~78%** | **3** | **~10-12 hours** |

---

## 5. Recommended Implementation Order for Tasks 021-026

Based on dependency analysis and criticality:

### Priority 1: Foundation Fixes (unblock everything)

**Task 021 — Implement Dataverse-Level Dedup Query (G4)**
- Add `QueryCommunicationByGraphMessageIdAsync` to `IDataverseService` or a dedicated repo
- Implement real `ExistsByGraphMessageIdAsync` in `IncomingCommunicationProcessor` (replace stub at line 209-228)
- Add Redis processing lock wrapper (G5)
- Align idempotency key format to `Communication:{graphMessageId}:Process` (G10/G16)
- **Why first**: Every other component depends on dedup working. Without this, testing any inbound flow produces duplicates.

**Task 022 — Wire InboundPollingBackupService to Enqueue Jobs (G11, G12, G13)**
- Inject `JobSubmissionService` into constructor
- Replace log-only loop (lines 201-214) with job submission matching webhook payload format
- Add `isRead eq false` to Graph filter (line 177)
- Set `TriggerSource = "BackupPolling"` in job payload
- **Why second**: Completes the backup path. Independent of association resolution.

### Priority 2: Core Feature

**Task 023 — Implement Association Resolution Service (G6)**
- Create `IncomingAssociationResolver` service with priority cascade:
  1. In-Reply-To thread matching (check `sprk_graphmessageid` chain)
  2. Sender email -> `sprk_organization` or `sprk_person` contact match
  3. Subject line pattern match (e.g., matter reference patterns)
  4. Mailbox context (account default associations)
- Wire into `IncomingCommunicationProcessor.ProcessAsync` after Step 4 (create record) — update record with resolved associations
- Register in `CommunicationModule`
- **Why third**: Biggest feature gap but cleanly additive. Processor already creates the record without associations; resolver updates it.

### Priority 3: Polish & Admin

**Task 024 — GraphSubscriptionManager Enhancements (G1, G2)**
- Update `sprk_subscriptionstatus` in `UpdateAccountSubscriptionAsync` (resolve 100000 vs 100000000 value)
- Add optional cleanup pass for disabled accounts with stale subscriptions
- Verify subscription status OptionSetValue in Dataverse before deploying

**Task 025 — Archival Opt-In/Opt-Out + Field Fixes (G7, G8)**
- Check `account.ArchiveIncomingOptIn` before EML archival in processor
- Add `sprk_receivedat` field to communication record creation
- Ensure `CommunicationAccount` model exposes `ArchiveIncomingOptIn` property

**Task 026 — Integration Testing & End-to-End Validation**
- Test webhook -> job -> processor -> Dataverse record flow
- Test backup polling -> job -> processor flow
- Test dedup: same message via webhook + polling = 1 record
- Test association resolution for each cascade level
- Test subscription lifecycle: create, renew, recreate, disable
- Verify NFR-01 (webhook < 3s), NFR-02 (end-to-end < 30s), NFR-03 (polling <= 5 min)

---

## 6. Key Decisions Needed

| Decision | Options | Recommendation |
|----------|---------|----------------|
| `sprk_subscriptionstatus` Active value | 100000 or 100000000 | Check Dataverse before Task 024 |
| Dedup query approach | IDataverseService extension vs. dedicated CommunicationRepository | IDataverseService extension (minimal DI, ADR-010) |
| Association resolver scope | All 4 cascade levels vs. sender-only for R2 | All 4 levels (spec says "simple binary matching" for each) |
| Job type name | Keep `IncomingCommunication` or rename to `ProcessIncomingCommunication` | Keep as-is, document deviation (rename is breaking change) |
| Polling `isRead` filter | Add to Graph filter or check in code | Graph filter (reduces API payload) |

---

## 7. Files Modified by R2 Phase C Work

| File | Changes |
|------|---------|
| `Services/Communication/IncomingCommunicationProcessor.cs` | Implement dedup, add association call, add archival opt-in check, set receivedat |
| `Services/Communication/InboundPollingBackupService.cs` | Inject JobSubmissionService, enqueue jobs, add isRead filter |
| `Services/Communication/GraphSubscriptionManager.cs` | Add subscription status update |
| `Api/CommunicationEndpoints.cs` | Align idempotency key format |
| `Services/Communication/IncomingAssociationResolver.cs` | **NEW** — association resolution service |
| `Infrastructure/DI/CommunicationModule.cs` | Register IncomingAssociationResolver |
| `Services/Communication/Models/CommunicationAccount.cs` | Ensure ArchiveIncomingOptIn property exists |
