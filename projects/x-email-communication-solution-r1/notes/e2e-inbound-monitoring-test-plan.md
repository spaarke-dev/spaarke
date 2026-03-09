# E2E Inbound Monitoring Test Plan

**Phase**: 8 (Final Gate Task)
**Task**: 077 - End-to-end inbound monitoring testing
**Date**: 2026-02-22
**Status**: Complete

---

## Overview

This document describes the end-to-end integration tests for the inbound email monitoring pipeline, covering the full lifecycle from webhook notification receipt through to sprk_communication record creation in Dataverse.

## Test Architecture

All tests are located in:
- `tests/unit/Sprk.Bff.Api.Tests/Integration/CommunicationIntegrationTests.cs`
- Region: `Phase 8 -- Inbound Monitoring E2E Tests`

Tests use **xUnit + Moq + FluentAssertions** consistent with existing Phase 6 and Phase 7 test regions.

Infrastructure dependencies (Graph SDK, Dataverse, SPE, Redis) are mocked. Real service instances (IncomingCommunicationProcessor, GraphSubscriptionManager, InboundPollingBackupService, CommunicationAccountService) are used.

---

## Test Scenarios

### 1. Webhook Flow: Notification to Record Creation

**Test**: `InboundPipeline_WebhookNotification_CreatesIncomingRecord`

Simulates the complete inbound pipeline:

```
Graph Change Notification
  --> Webhook endpoint validates clientState
    --> Enqueue to ServiceBus (mocked - test calls ProcessAsync directly)
      --> IncomingCommunicationProcessor.ProcessAsync
        --> Dedup check (multi-layer)
        --> Fetch full message from Graph
        --> Create sprk_communication record in Dataverse
        --> Process attachments (if enabled)
        --> Archive .eml (if configured)
        --> Mark as read in Graph
```

**Verifies**:
- sprk_communication record is created with all required fields
- Direction, CommunicationType, StatusCode are set to correct enum values
- From, To, Subject, Body, GraphMessageId, SentAt all populated correctly

### 2. Regarding Fields Verification (Must Be Empty)

**Test**: `InboundPipeline_IncomingRecord_HasNoRegardingFields`

**CRITICAL**: Verifies that incoming communication records do NOT have any association/regarding fields set:

| Field | Expected Value | Reason |
|-------|---------------|--------|
| `sprk_regardingmatter` | **absent** | Association resolution is a separate AI project |
| `sprk_regardingorganization` | **absent** | Association resolution is a separate AI project |
| `sprk_regardingperson` | **absent** | Association resolution is a separate AI project |
| `sprk_associationcount` | **absent** | No associations exist on incoming records |
| `sprk_regardingrecordname` | **absent** | No regarding record set |
| `sprk_regardingrecordid` | **absent** | No regarding record set |

### 3. Deduplication: Duplicate Webhook Handling

**Test**: `InboundPipeline_DuplicateWebhook_DoesNotCreateDuplicate`

Documents the multi-layer deduplication strategy:
- **Layer 1**: In-memory ConcurrentDictionary in webhook endpoint (same-process dedup)
- **Layer 2**: ServiceBus IdempotencyKey (cross-process dedup)
- **Layer 3**: Dataverse duplicate detection rule on sprk_graphmessageid

The test verifies that if upstream dedup layers fail, the processor's behavior is documented -- it defers to Dataverse duplicate detection as the final safety net.

### 4. Subscription Auto-Creation

**Test**: `InboundPipeline_SubscriptionAutoCreated_ForReceiveEnabledAccount`

Verifies GraphSubscriptionManager lifecycle:
1. Queries receive-enabled accounts from Dataverse
2. Detects account with no subscription (sprk_subscriptionid is null)
3. Creates Graph subscription via `POST /subscriptions`
4. Updates Dataverse account with sprk_subscriptionid and sprk_subscriptionexpiry

### 5. Backup Polling: Catching Missed Messages

**Test**: `InboundPipeline_BackupPolling_CatchesMissedMessages`

Verifies InboundPollingBackupService:
1. Queries receive-enabled accounts
2. For each account, queries Graph for messages received since last poll
3. Uses 15-minute initial lookback window on first run
4. Detects unprocessed messages that webhook may have missed
5. Logs found messages (processing via IncomingCommunicationJob in production)

### 6. Subscription Renewal: Expiry Extension

**Test**: `InboundPipeline_SubscriptionRenewal_ExtendsExpiry`

Verifies GraphSubscriptionManager renewal logic:
1. Account has subscription expiring in < 24 hours (below renewal threshold)
2. Manager detects renewal needed
3. Patches Graph subscription with new 3-day expiry
4. Updates sprk_subscriptionexpiry in Dataverse
5. Extended expiry is at least 2 days in the future

---

## Field Verification Table: Incoming Communication Record

| Dataverse Field | Type | Expected Value | Notes |
|----------------|------|---------------|-------|
| `sprk_name` | string | `"Email: {subject}"` | Truncated to 200 chars |
| `sprk_communiationtype` | OptionSetValue | `100000000` (Email) | Intentional typo in field name |
| `statuscode` | OptionSetValue | `659490003` (Delivered) | Not Send (659490002) -- incoming emails are Delivered |
| `statecode` | OptionSetValue | `0` (Active) | Standard active state |
| `sprk_direction` | OptionSetValue | `100000000` (Incoming) | Distinguishes from Outgoing (100000001) |
| `sprk_bodyformat` | OptionSetValue | `100000001` (HTML) or `100000000` (PlainText) | Based on Graph message body content type |
| `sprk_from` | string | External sender email | e.g., "external.sender@partner.com" |
| `sprk_to` | string | Receiving mailbox or original To list | Semicolon-separated if multiple recipients |
| `sprk_cc` | string | CC recipients (if any) | Semicolon-separated |
| `sprk_subject` | string | Email subject | "(No Subject)" if null |
| `sprk_body` | string | Email body content | Prefers uniqueBody over full body |
| `sprk_graphmessageid` | string | Graph message ID | Used for deduplication |
| `sprk_sentat` | DateTime | Graph receivedDateTime (UTC) | Falls back to DateTime.UtcNow |
| `sprk_hasattachments` | bool | true/false | Set when message has attachments |
| `sprk_attachmentcount` | int | Count of file attachments | Only file attachments, not inline |
| `sprk_regardingmatter` | EntityReference | **NOT SET** | Deferred to AI project |
| `sprk_regardingorganization` | EntityReference | **NOT SET** | Deferred to AI project |
| `sprk_regardingperson` | EntityReference | **NOT SET** | Deferred to AI project |

---

## Subscription Lifecycle

```
[Account Created with ReceiveEnabled=true]
         |
         v
  GraphSubscriptionManager (30-min PeriodicTimer)
         |
         +-- No SubscriptionId? --> CREATE subscription
         |                           |
         |                           +--> Graph POST /subscriptions
         |                           +--> Store sprk_subscriptionid
         |                           +--> Store sprk_subscriptionexpiry
         |
         +-- Expiry < 24h? --> RENEW subscription
         |                       |
         |                       +--> Graph PATCH /subscriptions/{id}
         |                       +--> Update sprk_subscriptionexpiry
         |
         +-- Renewal fails? --> RECREATE
         |                       |
         |                       +--> Delete old subscription (best-effort)
         |                       +--> Create new subscription
         |
         +-- Healthy? --> SKIP (no action needed)
```

## Backup Polling

```
InboundPollingBackupService (5-min PeriodicTimer)
         |
         v
  Query receive-enabled accounts
         |
         v
  For each account:
    - Get last poll time (or 15-min lookback on first run)
    - Query Graph: messages received since last poll
    - Log found messages for processing
    - Update last poll time
```

---

## Known Limitations

1. **Association resolution deferred**: sprk_regardingmatter, sprk_regardingorganization, sprk_regardingperson are intentionally not set on incoming records. This is a separate AI project that will use NLP/entity extraction to automatically associate incoming emails with matters, organizations, and contacts.

2. **In-processor dedup is best-effort**: The ExistsByGraphMessageIdAsync method in IncomingCommunicationProcessor currently returns `false` (relying on upstream dedup layers). A future enhancement could add a Dataverse query for sprk_graphmessageid as an additional dedup layer.

3. **Backup polling logs only**: InboundPollingBackupService currently logs found messages. Actual re-processing of missed messages via IncomingCommunicationJob will consume them in production through the ServiceBus queue.

4. **Graph SDK mock limitations**: Tests use MockHttpMessageHandler to simulate Graph responses. The Graph SDK's internal deserialization may behave slightly differently from production when processing complex message structures.

---

## Execution

```bash
# Run Phase 8 E2E tests only (by class name)
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~CommunicationIntegrationTests"

# Run all integration tests
dotnet test tests/unit/Sprk.Bff.Api.Tests/
```
