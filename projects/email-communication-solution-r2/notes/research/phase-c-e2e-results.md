# Test Plan: Inbound Shared Mailbox Monitoring End-to-End

**Phase**: Phase C - Inbound Shared Mailbox Monitoring
**Task**: ECS-027
**Status**: Pre-Deployment (Code Changes Committed)
**Date**: 2026-03-09

---

## Overview

This document defines the test plan and pre-deployment checklist for Task 027 (End-to-End Inbound Monitoring Test). The tests verify that the complete inbound email monitoring pipeline functions correctly, including:

- **Graph Subscription Lifecycle**: Create, renew, and manage subscriptions for receive-enabled accounts
- **Webhook Notification Processing**: Receive Graph notifications and enqueue jobs to Service Bus
- **Communication Record Creation**: Auto-create `sprk_communication` records with proper metadata
- **Association Resolution**: Link incoming emails to known contacts, matters, or threads
- **Backup Polling**: Catch messages missed by webhook failures within 5-minute cycle
- **Deduplication**: Prevent double records when same message arrives via webhook and polling

This is a **pre-deployment checklist**, not actual test execution. Execution and full results will be documented when deployment occurs.

---

## Prerequisites

Before testing, the following must be in place:

### Infrastructure & Deployment

- [ ] **BFF API deployed** with latest inbound monitoring code:
  - `GraphSubscriptionManager.cs` BackgroundService running
  - `CommunicationWebhookEndpoint.cs` (`POST /api/communications/incoming-webhook`)
  - `InboundPollingBackupService.cs` BackgroundService running
  - `IncomingCommunicationProcessor.cs` job handler in Service Bus
  - `AssociationResolver.cs` context linking logic
  - `GraphClientFactory.ForApp()` configured for app-only token
  - `RedisConnectionMultiplexer` configured for idempotency tracking
  - `ILogger<T>` configured for correlation tracking

- [ ] **Dataverse CommunicationAccount records** configured:
  - At least one account with `sprk_receiveenabled` = True
  - Account email address matches shared mailbox (e.g., `team-mailbox@contoso.com`)
  - Account `sprk_monitorfolder` = "Inbox" or equivalent
  - Account `sprk_processingrules` configured (or default rules applied)

- [ ] **Service Bus** configured:
  - Queue or topic for `IncomingCommunication` job type
  - Dead-letter queue available for failed messages
  - Session ID support enabled (if using)

- [ ] **Graph Webhook** infrastructure:
  - Webhook URL publicly accessible from Microsoft Graph
  - SSL/TLS certificate valid and trusted
  - Network firewall allows inbound Graph notifications (port 443)
  - ngrok or Azure App Gateway configured (if needed for dev/test)

- [ ] **Azure Key Vault** secrets:
  - `GraphSubscriptionClientState` secret configured (webhook validation)
  - Graph API credentials for app-only auth configured

- [ ] **Redis Cache** (if caching enabled):
  - Connection string configured
  - Accessible from BFF API
  - TTL policy for idempotency keys (e.g., 24 hours)

### User Permissions & Configuration

- [ ] **Azure AD Application (BFF) configured**:
  - Graph API permissions:
    - `Mail.Read` (application - for inbound shared mailbox monitoring)
    - `Subscription.Read` (application - to query subscription status)
  - Client credentials configured for app-only auth
  - Webhook callback authentication configured

- [ ] **Dataverse Security**:
  - System user (BFF service account) has read/write on `sprk_communication`
  - System user can create and update `sprk_communication` records

- [ ] **Email test accounts**:
  - Monitored shared mailbox accessible from test environment
  - External test email account to send messages (e.g., `external-test@gmail.com`)
  - Known contact in Dataverse with matching email (for association testing)
  - Dataverse matter/case record with ID for reference testing

### Testing Environment

- [ ] **BFF API running** and accepting requests:
  - Health check: `GET /healthz` returns 200
  - Logs accessible and configured for debug level
  - Correlation IDs enabled for tracing

- [ ] **Dataverse accessible**:
  - Communication form visible
  - Ability to query and create `sprk_communication` records
  - Access to CommunicationAccount configuration

- [ ] **Network connectivity**:
  - Test environment can reach Microsoft Graph API (api.microsoft.com)
  - Graph can reach webhook endpoint (reverse connectivity)
  - Service Bus accessible from BFF
  - Redis accessible from BFF

---

## Test Case 1: Graph Subscription Creation

**Purpose**: Verify that `GraphSubscriptionManager` correctly creates Graph subscriptions for each receive-enabled account on BFF startup.

**Prerequisites**:
- BFF API not yet started (or subscriptions deleted from previous run)
- One or more receive-enabled `CommunicationAccount` records in Dataverse
- Graph API credentials configured

**Steps**:

1. **Start BFF API**
   ```bash
   dotnet run --project src/server/api/Sprk.Bff.Api/
   ```
   - Observe logs for `GraphSubscriptionManager` initialization
   - Expected log message: `"Creating subscription for account: team-mailbox@contoso.com"`

2. **Verify subscription created in Graph**
   ```bash
   # Using Microsoft Graph CLI or SDK
   az graph query --query "subscriptions" \
     --filter "notificationUrl eq 'https://your-webhook-url/api/communications/incoming-webhook'"
   ```
   - Verify subscription exists with status `Active`
   - Record subscription ID for later validation

3. **Verify subscription ID stored in Dataverse**
   - Open CommunicationAccount record in Dataverse
   - Check fields:
     - **sprk_subscriptionid**: Should match Graph subscription ID from step 2
     - **sprk_subscriptionstatus**: Should be "Active"
     - **sprk_subscriptionexpiry**: Should show expiry date (3 days from creation)

4. **Verify webhook configuration**
   - Subscription notification URL should be: `https://your-api/api/communications/incoming-webhook`
   - Subscription resource should be: `/me/mailFolders('Inbox')/messages` (or custom folder)
   - Subscription change type should include: `created`, `updated`, `deleted`

5. **Verify clientState is set**
   - clientState secret stored and validated on webhook (not visible in Graph API, stored in Key Vault)
   - BFF configuration includes secret for validation

**Expected Result**: ✅ PASS
- Subscription successfully created in Graph
- Subscription ID and status persisted in Dataverse
- Webhook URL publicly accessible and ready to receive notifications
- No errors in BFF logs

**Error Handling**:
- If creation fails with 401: Check Graph API credentials and app-only permissions
- If creation fails with 403: Verify application has `Mail.Read` permission
- If creation fails with 400: Check webhook URL format and accessibility
- If subscription not stored in Dataverse: Verify system user has write permission on CommunicationAccount

---

## Test Case 2: Webhook Notification Processing

**Purpose**: Verify that Graph webhook notifications are received, validated, and processed correctly.

**Prerequisites**:
- Graph subscription created (Test Case 1 passed)
- Webhook URL accessible and returning 200/202
- BFF API running with `IncomingCommunicationProcessor` ready

**Steps**:

1. **Send test email to monitored mailbox**
   - From: External test email (e.g., `external-test@gmail.com`)
   - To: Monitored shared mailbox (e.g., `team-mailbox@contoso.com`)
   - Subject: `"TEST: Webhook Notification [timestamp]"`
   - Body: `"This is a test email to verify webhook processing."`
   - Record the email timestamp and subject for tracking

2. **Monitor BFF API logs** (within 10 seconds)
   - Expected log: `"Received webhook notification for account: team-mailbox@contoso.com"`
   - Expected log: `"Webhook validation passed. ClientState verified."`
   - Expected log: `"Enqueueing IncomingCommunication job for message ID: [Graph Message ID]"`
   - Correlation ID should be logged for tracking

3. **Verify webhook response** (developer tools / API logs)
   - BFF should return `202 Accepted` within 3 seconds (Graph timeout requirement)
   - Response body: `{ "success": true }` or similar confirmation

4. **Monitor Service Bus** (within 5 seconds)
   - Check Service Bus queue for `IncomingCommunication` job
   - Job message should contain:
     - `messageId`: Graph message ID
     - `fromEmailAddress`: Sender email (`external-test@gmail.com`)
     - `toEmailAddress`: Monitored mailbox
     - `subject`: Email subject
     - `trigger_source`: "Webhook"
     - `account_id`: CommunicationAccount record ID
     - `client_state_validation`: `true`

5. **Monitor job processing** (within 30 seconds)
   - Expected log: `"Processing IncomingCommunication job: [message ID]"`
   - Expected log: `"Creating communication record..."`
   - Expected log: `"Communication record created: [communication record ID]"`

6. **Verify sprk_communication record created in Dataverse** (within 60 seconds)
   - Query: `sprk_graphmessageid = "[Graph Message ID]"`
   - Verify record contains:
     - **sprk_from**: `external-test@gmail.com`
     - **sprk_to**: `team-mailbox@contoso.com`
     - **sprk_subject**: `"TEST: Webhook Notification [timestamp]"`
     - **sprk_direction**: `Incoming` (100000001)
     - **sprk_receivedat**: Recent timestamp (email send time)
     - **sprk_graphmessageid**: Matches Graph message ID
     - **statuscode**: `Delivered` (659490003)
     - **sprk_communicationtrigger**: `Webhook`
     - **createdby**: System user (BFF service account)
     - **createdon**: Recent timestamp (should be close to webhook receipt time)

7. **Check association fields** (will be handled by Test Case 4)
   - **sprk_regardingperson**: May be empty (unless sender is known contact)
   - **sprk_regardingmatter**: May be empty (unless subject contains matter reference)
   - **sprk_associationstatus**: May be "Pending Review" (default)

8. **Verify email body archival** (if enabled)
   - Email content should be stored in SharePoint Embedded via `sprk_storagekey`
   - Or logged in EML format to audit trail

**Expected Result**: ✅ PASS
- Webhook receives notification within 10 seconds of email send
- BFF returns 202 response within 3 seconds
- Job enqueued to Service Bus within 5 seconds
- sprk_communication record created within 60 seconds
- All metadata correctly populated
- No errors or dead-letter messages in Service Bus

**Error Handling**:
- If webhook not received: Check Graph notification settings and BFF logs
- If 202 not returned: Check BFF endpoint is handling request correctly
- If job not enqueued: Check Service Bus connection and credentials
- If record not created: Check Dataverse system user permissions
- If metadata incomplete: Check job payload includes all required fields

---

## Test Case 3: Association Resolution

**Purpose**: Verify that incoming emails are automatically linked to known contacts, matters, or reply threads.

**Prerequisites**:
- Test Case 2 passed (webhook processing works)
- Known contact in Dataverse with matching email
- Known matter/case record with unique ID
- AssociationResolver service running

**Test 3.1: Link to Known Contact**

1. **Create or identify test contact** in Dataverse:
   - Email: `known-contact@contoso.com`
   - First Name: "Test"
   - Last Name: "Contact"
   - Add to Dataverse `contact` table

2. **Send email from known contact**:
   - From: `known-contact@contoso.com`
   - To: `team-mailbox@contoso.com`
   - Subject: `"TEST: Association to Contact [timestamp]"`
   - Body: `"Testing association to contact."`

3. **Wait for processing** (within 60 seconds)
   - Monitor logs for: `"Resolving associations for message ID: [id]"`
   - Expected log: `"Found matching contact: [contact ID]"`

4. **Verify association in Dataverse**:
   - Open `sprk_communication` record created from email
   - Check **sprk_regardingperson**: Should contain contact record link
   - Check **sprk_regardingperson_name**: Should show "Test Contact"
   - Check **sprk_associationstatus**: Should be "Auto-Linked"

**Test 3.2: Link to Matter via Subject Reference**

1. **Create or identify test matter** in Dataverse:
   - Matter ID: "MAT-12345" (or similar)
   - Matter Title: "Test Legal Matter"
   - Add to Dataverse `matter` table (if custom)

2. **Send email with matter reference in subject**:
   - From: `external-test@gmail.com`
   - To: `team-mailbox@contoso.com`
   - Subject: `"RE: MAT-12345 Update - Test Email [timestamp]"`
   - Body: `"Follow-up on matter MAT-12345."`

3. **Wait for processing** (within 60 seconds)
   - Monitor logs for: `"Resolving associations for message ID: [id]"`
   - Expected log: `"Found matching matter via subject: MAT-12345 → [matter ID]"`

4. **Verify association in Dataverse**:
   - Open `sprk_communication` record
   - Check **sprk_regardingmatter**: Should contain matter record link
   - Check **sprk_regardingmatter_name**: Should show "Test Legal Matter"
   - Check **sprk_associationstatus**: Should be "Auto-Linked"

**Test 3.3: Link to Reply Thread**

1. **Create initial communication record** (simulating previous outbound email):
   - Create `sprk_communication` with:
     - `sprk_from`: `team-mailbox@contoso.com`
     - `sprk_to`: `known-contact@contoso.com`
     - `sprk_subject`: `"Original Message Subject"`
     - `sprk_direction`: `Outgoing`
     - `sprk_threadid`: Set to a unique value (e.g., Graph conversation ID)

2. **Send reply email**:
   - From: `known-contact@contoso.com`
   - To: `team-mailbox@contoso.com`
   - Subject: `"RE: Original Message Subject"`
   - Thread ID (Graph): Use same conversation ID as initial message
   - Body: `"This is a reply."`

3. **Wait for processing** (within 60 seconds)
   - Monitor logs for: `"Resolving associations for message ID: [id]"`
   - Expected log: `"Found parent message in thread: [parent communication ID]"`

4. **Verify association in Dataverse**:
   - Open new `sprk_communication` record (reply)
   - Check **sprk_parentcommunication**: Should link to initial message
   - Check **sprk_threadid**: Should match parent thread ID
   - Check **sprk_regardingperson** and **sprk_regardingmatter**: Should inherit from parent if set
   - Check **sprk_associationstatus**: Should be "Auto-Linked (Thread)"

**Test 3.4: Unknown Sender (No Association)**

1. **Send email from unknown sender**:
   - From: `unknown-external-user@random-domain.com`
   - To: `team-mailbox@contoso.com`
   - Subject: `"TEST: Unknown Sender [timestamp]"`
   - Body: `"This sender is not in the system."`

2. **Wait for processing** (within 60 seconds)
   - Monitor logs for: `"No matching contact found. Setting status to Pending Review."`

3. **Verify record state in Dataverse**:
   - Open `sprk_communication` record
   - Check **sprk_regardingperson**: Empty (no match)
   - Check **sprk_regardingmatter**: Empty (no reference in subject)
   - Check **sprk_associationstatus**: `"Pending Review"`
   - Record should be visible in admin queue for manual linking

**Expected Result**: ✅ PASS (all 4 sub-tests)
- Emails from known contacts auto-linked to contact record
- Emails with matter references in subject auto-linked to matter
- Reply emails linked to original message via thread
- Unknown senders marked as "Pending Review" for manual processing
- No errors in association resolution logs

**Error Handling**:
- If contact not linked: Check contact email exactly matches sender email
- If matter not linked: Check matter reference pattern matches (e.g., "MAT-12345")
- If thread not linked: Check conversation ID / thread ID is correct
- If association status wrong: Verify AssociationResolver service is running

---

## Test Case 4: Backup Polling

**Purpose**: Verify that `InboundPollingBackupService` catches messages missed by webhook failures within the 5-minute polling cycle.

**Prerequisites**:
- Test Case 2 passed (webhook processing baseline)
- InboundPollingBackupService running in BFF
- Redis cache configured (if using for polling state)

**Steps**:

1. **Simulate webhook failure**:
   - Option A: Temporarily disable webhook in Graph (via management API)
   - Option B: Deploy code change to skip webhook processing (mock failure)
   - Option C: Manually delete subscription to prevent notifications
   - Verify logs show webhook notifications NOT being received

2. **Send test email to monitored mailbox**:
   - From: `external-test@gmail.com`
   - To: `team-mailbox@contoso.com`
   - Subject: `"TEST: Backup Polling [timestamp]"`
   - Body: `"Testing backup polling."`
   - Record email timestamp

3. **Wait for polling cycle** (within 5 minutes):
   - Monitor BFF logs for: `"InboundPollingBackupService: Starting polling cycle for account: team-mailbox@contoso.com"`
   - Expected log: `"Querying mailbox for messages received in last 5 minutes..."`
   - Expected log: `"Found unprocessed message: [Graph Message ID]. Enqueueing for processing."`

4. **Monitor Service Bus** (within 5 minutes):
   - Check Service Bus queue for new `IncomingCommunication` job
   - Job message should contain:
     - `messageId`: Graph message ID
     - `trigger_source`: "BackupPolling" (not "Webhook")
     - All other fields same as webhook-based job

5. **Verify communication record created** (within 10 minutes):
   - Query Dataverse: `sprk_graphmessageid = "[Graph Message ID]"`
   - Verify record contains correct data (same as Test Case 2)
   - Check **sprk_communicationtrigger**: Should be `"BackupPolling"` (not "Webhook")
   - Verify single record exists (not duplicate from webhook that may have also processed)

6. **Re-enable webhook**:
   - Restore subscription or re-enable webhook processing
   - Send confirmation email to verify webhook works again
   - Expected log: `"Webhook re-enabled. Processing resumed."`

**Expected Result**: ✅ PASS
- Backup polling cycle runs automatically every 5 minutes
- Polling finds emails missed by webhook failure
- Job enqueued with `trigger_source: "BackupPolling"`
- Communication record created with correct metadata
- No errors in polling service logs

**Error Handling**:
- If polling doesn't run: Check InboundPollingBackupService is started
- If email not found: Check polling query includes correct time window (last 5 minutes)
- If double processing: See Test Case 5 (Deduplication)
- If record not created: Check Service Bus and processor logs

---

## Test Case 5: Deduplication

**Purpose**: Verify that the same message processed via both webhook AND polling creates only ONE sprk_communication record.

**Prerequisites**:
- Test Case 2 and Test Case 4 passed
- Redis idempotency tracking configured
- Deduplication logic implemented in IncomingCommunicationProcessor

**Steps**:

1. **Set up timing scenario**:
   - Configure webhook to receive notification (will be processed immediately)
   - Simultaneously, ensure polling cycle will also pick up same message
   - Use small time windows to increase chance of overlap

2. **Send test email to monitored mailbox**:
   - From: `external-test@gmail.com`
   - To: `team-mailbox@contoso.com`
   - Subject: `"TEST: Deduplication [timestamp]"`
   - Body: `"Testing deduplication logic."`
   - Record Graph Message ID (check inbox for it)

3. **Monitor webhook processing** (within 10 seconds):
   - Expected log: `"Received webhook notification..."`
   - Expected log: `"Enqueueing IncomingCommunication job from webhook..."`
   - Job enqueued to Service Bus

4. **Monitor job processing** (within 30 seconds):
   - Expected log: `"Processing IncomingCommunication job [message ID]"`
   - Expected log: `"Checking Redis idempotency key: [message ID]"`
   - Expected log: `"No prior processing found. Creating communication record."`
   - Expected log: `"Redis idempotency key set: [message ID]" (TTL: 24 hours)`

5. **Wait for polling cycle** (within 5 minutes):
   - Polling discovers same message
   - Expected log: `"Found unprocessed message: [Graph Message ID]. Enqueueing for processing."`
   - Job enqueued to Service Bus again

6. **Monitor second job processing** (within 10 minutes):
   - Expected log: `"Processing IncomingCommunication job [message ID]"`
   - Expected log: `"Checking Redis idempotency key: [message ID]"`
   - **Key Log**: `"Message already processed. Skipping duplicate."`
   - Job should complete without creating new record

7. **Verify single record in Dataverse**:
   - Query: `sprk_graphmessageid = "[Graph Message ID]"`
   - **Result**: Exactly ONE record found (not two)
   - Record metadata is correct
   - No duplicate entries in communication list view

8. **Fallback dedup verification** (if Redis not available):
   - Also verify Dataverse-level dedup: sprk_graphmessageid uniqueness constraint
   - If webhook and polling create jobs simultaneously (before Redis checked):
     - First job creates record
     - Second job queries Dataverse: `sprk_graphmessageid exists → skip creation`
     - Result: Still single record

**Expected Result**: ✅ PASS
- Webhook and polling may both process same message (OK)
- Deduplication layer prevents duplicate records
- Redis idempotency tracking (primary) + Dataverse uniqueness (fallback) both verified
- Single communication record persists
- Logs show "already processed" message for duplicate job

**Error Handling**:
- If duplicate records created: Check Redis is configured and accessible
- If Redis unavailable: Verify Dataverse uniqueness constraint on sprk_graphmessageid
- If both records created: Check deduplication logic in IncomingCommunicationProcessor
- If record data inconsistent: Verify both jobs have same payload

---

## Test Case 6: Subscription Renewal

**Purpose**: Verify that `GraphSubscriptionManager` renews subscriptions before expiry to maintain continuous monitoring.

**Prerequisites**:
- Test Case 1 passed (subscription created)
- GraphSubscriptionManager running in BFF
- Time advancement capability (or manual test timing)

**Steps**:

1. **Verify initial subscription expiry**:
   - Query Dataverse CommunicationAccount: `sprk_subscriptionexpiry`
   - Record should show expiry date (3 days from creation in Test Case 1)
   - Example: Created on 2026-03-09, expiry should be ~2026-03-12

2. **Wait for renewal trigger** (or simulate):
   - Renewal should occur when < 24 hours remaining until expiry
   - Option A: Wait 2 days 1 hour for natural renewal
   - Option B: Manually invoke renewal logic via test endpoint
   - Option C: Advance system clock (test environment only)

3. **Monitor BFF logs** for renewal:
   - Expected log: `"Checking subscription renewal for account: team-mailbox@contoso.com"`
   - Expected log: `"Subscription expiring in [X hours]. Renewing..."`
   - Expected log: `"Subscription renewed successfully. New expiry: [new date]"`

4. **Verify renewal in Graph API**:
   - Query Graph subscriptions for account
   - Subscription should still exist with new expiry date
   - Subscription ID may change (depending on Graph behavior)

5. **Verify renewal recorded in Dataverse**:
   - Open CommunicationAccount record
   - Check **sprk_subscriptionexpiry**: Should show new (future) date
   - Check **sprk_subscriptionstatus**: Should remain "Active"
   - Check **sprk_subscriptionid**: May be new ID or same (verify it matches Graph)

6. **Continue monitoring for uninterrupted service**:
   - Send test email (similar to Test Case 2)
   - Verify webhook continues to work after renewal
   - Expected log: `"Webhook notification received after renewal. Service uninterrupted."`

**Expected Result**: ✅ PASS
- Subscription renewed before expiry (within 24 hours of expiration)
- New expiry date extends subscription lifetime
- Webhook continues to receive notifications after renewal
- No gap in monitoring due to expiry
- No errors in renewal process

**Error Handling**:
- If renewal doesn't trigger: Check renewal window (< 24 hours)
- If renewal fails with 401: Check Graph credentials are still valid
- If webhook stops after renewal: Check new subscription ID is correct
- If expiry date not updated: Verify renewal logic persists new date to Dataverse

---

## Test Case 7: Error Recovery

**Purpose**: Verify that the system recovers gracefully from transient failures (BFF restart, malformed webhooks, invalid clientState).

**Prerequisites**:
- Test Cases 1-6 passed
- Ability to restart BFF API
- Ability to send malformed webhook requests

**Test 7.1: BFF Restart with Active Subscriptions**

1. **Verify subscriptions exist** (from Test Case 1):
   - Dataverse CommunicationAccount records have:
     - `sprk_subscriptionid`: Non-empty
     - `sprk_subscriptionstatus`: "Active"

2. **Stop BFF API**:
   - Gracefully shut down: `Ctrl+C` or stop Docker container
   - Verify stopped: `GET /healthz` returns error

3. **Restart BFF API**:
   - Start again: `dotnet run --project src/server/api/Sprk.Bff.Api/`
   - Monitor startup logs

4. **Verify GraphSubscriptionManager recovery** (on startup):
   - Expected log: `"GraphSubscriptionManager starting. Loading existing subscriptions..."`
   - Expected log: `"Found [N] existing subscriptions in Dataverse. Validating with Graph..."`
   - For each subscription:
     - Expected log: `"Subscription status: Active. Continuing."`
     - OR log: `"Subscription expired. Recreating..."`
     - OR log: `"Subscription not found in Graph. Recreating..."`

5. **Verify subscriptions restored** (within 30 seconds):
   - All active subscriptions should still exist in Graph
   - If subscription was lost, new one created with new ID
   - Dataverse CommunicationAccount updated with current subscription ID

6. **Verify webhook processing resumes**:
   - Send test email to monitored mailbox
   - Webhook should fire normally (within 10 seconds)
   - Communication record created

**Test 7.2: Malformed Webhook Notification**

1. **Send malformed webhook request** to `POST /api/communications/incoming-webhook`:
   - Missing required fields (e.g., no `value` array)
   - Invalid JSON
   - Invalid authorization header

2. **Verify error handling**:
   - Expected response: `400 Bad Request` or `401 Unauthorized` (depending on error)
   - Response time: < 3 seconds (Graph timeout requirement)
   - BFF should NOT crash
   - Expected log: `"Invalid webhook request: [error details]"`

3. **Verify webhook endpoint is still operational**:
   - Send valid webhook request
   - Expected response: `202 Accepted`
   - Processing continues normally

**Test 7.3: Invalid clientState**

1. **Send webhook with incorrect clientState**:
   - Valid Graph notification structure
   - clientState value: Wrong secret (e.g., "invalid-secret-123")

2. **Verify rejection**:
   - Expected response: `401 Unauthorized`
   - Expected log: `"Webhook validation failed. Invalid clientState."`
   - Request should NOT be processed

3. **Verify logging for security audit**:
   - Log should include:
     - Timestamp
     - Source IP
     - Request details
     - Failure reason

**Test 7.4: Service Bus Unavailable**

1. **Stop Service Bus** (or simulate connection failure):
   - Temporarily disable Service Bus connection in config
   - Or stop Service Bus instance

2. **Send email to monitored mailbox**:
   - Webhook receives notification
   - Expected log: `"Failed to enqueue job. Service Bus unavailable."`
   - Expected log: `"Retrying with exponential backoff..."`

3. **Monitor retry behavior**:
   - Webhook should return 202 to Graph immediately (don't block)
   - Job enqueue should retry (background thread)
   - Logs show retry attempts

4. **Restore Service Bus**:
   - Re-enable Service Bus connection
   - Expected log: `"Service Bus connection restored. Processing pending jobs."`

5. **Verify recovery**:
   - Pending job should be processed
   - Communication record eventually created

**Expected Result**: ✅ PASS (all 4 sub-tests)
- BFF restart doesn't lose subscriptions
- Subscriptions validated and restored on startup
- Malformed webhooks don't crash service
- Invalid clientState rejected securely
- Transient failures recovered with retry logic
- No data loss during outages

**Error Handling**:
- If subscriptions lost after restart: Check persistence logic in startup
- If webhook processing doesn't resume: Check GraphSubscriptionManager logs
- If service crashes on bad input: Add null checks and validation
- If clientState validation bypassed: Verify validation logic executes

---

## Expected Infrastructure Flow

### Happy Path: Email → Webhook → Communication Record

```
External Email (Gmail, Outlook, etc.)
        ↓
Microsoft Graph
        ↓
Webhook Notification (Graph → BFF)
        ↓
POST /api/communications/incoming-webhook
        ↓
Validate clientState (Key Vault secret)
        ↓
Enqueue IncomingCommunication Job (Service Bus)
        ↓
Service Bus Topic/Queue
        ↓
IncomingCommunicationProcessor (Job Handler)
        ↓
Create sprk_communication Record
        ↓
AssociationResolver (Link to Contact/Matter/Thread)
        ↓
Email Body Archival (SPE via Graph attachments)
        ↓
✅ Communication ready in Dataverse
```

**Timeline**: Email → Graph (instant) → Webhook (< 10 sec) → Job (< 5 sec) → Record (< 60 sec total)

### Fallback Path: Webhook Miss → Polling → Communication Record

```
External Email
        ↓
Microsoft Graph
        ↓
Webhook Notification (Graph loses connection or BFF timeout)
        ↓
❌ Webhook not delivered
        ↓
InboundPollingBackupService (5-minute cycle)
        ↓
Query mailbox for messages in last 5 minutes
        ↓
Graph API: GET /me/mailFolders('Inbox')/messages?$filter=receivedDateTime >= [5 min ago]
        ↓
Enqueue IncomingCommunication Job (trigger_source: "BackupPolling")
        ↓
Deduplication Check (Redis + Dataverse)
        ↓
Create sprk_communication Record (or skip if duplicate)
        ↓
AssociationResolver → Email Archival
        ↓
✅ Communication ready (within 10 minutes)
```

**Timeline**: Email → Graph (instant) → Wait for polling (up to 5 min) → Dedup/Record (< 10 min total)

### Deduplication Flow (Webhook + Polling Race)

```
Webhook receives notification (T=10s)
        ↓
Job #1 enqueued (T=15s)
        ↓
Polling cycle starts (T=300s = 5 min)
        ↓
Polling also finds message (T=305s)
        ↓
Job #2 enqueued (T=310s)
        ↓
Job #1 processing starts (T=330s)
        ↓
Check Redis idempotency: "message_12345" → NOT FOUND
        ↓
Create sprk_communication record
        ↓
Set Redis key: "message_12345" (TTL: 24h)
        ↓
Job #1 complete (T=360s)
        ↓
Job #2 processing starts (T=380s)
        ↓
Check Redis idempotency: "message_12345" → FOUND
        ↓
Skip creation. Job complete.
        ↓
✅ Single communication record persists
```

---

## Quality Checklist

Before marking this test complete, verify:

- [ ] **Test Case 1 Completed**: Graph subscription created and stored in Dataverse
- [ ] **Test Case 2 Completed**: Webhook fires, job enqueued, communication record created
- [ ] **Test Case 3 Completed**: Associations resolved (contact, matter, thread, pending review)
- [ ] **Test Case 4 Completed**: Backup polling catches missed messages within 5 minutes
- [ ] **Test Case 5 Completed**: Webhook + polling race condition handled; single record created
- [ ] **Test Case 6 Completed**: Subscriptions renewed before expiry; service uninterrupted
- [ ] **Test Case 7 Completed**: BFF restart, malformed webhooks, invalid clientState, service outages recovered
- [ ] **Email Delivery**: All test emails delivered and processed (check sent items folder)
- [ ] **Communication Records**: All records contain correct metadata (from, to, subject, direction, etc.)
- [ ] **No Orphaned Subscriptions**: After complete test, all subscriptions either active or properly removed
- [ ] **No Double Records**: Same message never created two sprk_communication records
- [ ] **Association Accuracy**: Auto-linking works for contacts, matters, threads; unknown senders marked for review
- [ ] **Logs Reviewed**: All error conditions documented; no unexpected exceptions
- [ ] **Performance Verified**: Email → Record within 60 seconds (webhook path), within 10 minutes (polling path)
- [ ] **Security Validated**: clientState validation works; invalid webhooks rejected

---

## Deployment Readiness Assessment

### Go/No-Go Criteria

| Criterion | Status | Notes |
|-----------|--------|-------|
| Code changes committed | ✅ Complete | GraphSubscriptionManager, Webhook, Polling, Processor, AssociationResolver all implemented |
| Unit tests passing | ✅ Complete | Mocks for Graph API, Service Bus, Dataverse created |
| Subscription lifecycle test plan ready | ✅ Complete | Test Cases 1, 6 defined |
| Webhook processing test plan ready | ✅ Complete | Test Case 2 defined |
| Association resolution test plan ready | ✅ Complete | Test Case 3 defined (4 sub-tests) |
| Backup polling test plan ready | ✅ Complete | Test Case 4 defined |
| Deduplication test plan ready | ✅ Complete | Test Case 5 defined |
| Error recovery test plan ready | ✅ Complete | Test Case 7 defined (4 sub-tests) |
| Infrastructure configured | ✅ Pending | Graph webhooks, Service Bus, Redis configured (deployment phase) |
| Azure AD permissions configured | ✅ Pending | Mail.Read, Subscription.Read permissions (deployment phase) |
| Dataverse schema complete | ✅ Complete | Communication account, communication, subscription fields ready |
| BFF API deployment process documented | ✅ Complete | See CLAUDE.md for deployment procedure |
| API endpoint tested | ❌ Pending | Requires deployment environment |
| Graph subscription validated | ❌ Pending | Requires deployment environment |

### Blockers

None identified. Code is ready for deployment. Testing will proceed upon deployment to staging/production environment.

---

## Test Execution Schedule (Placeholder)

This test will be executed during **Phase C Deployment**. Actual results will be documented here.

| Test Case | Scheduled Date | Tester | Result | Notes |
|-----------|----------------|--------|--------|-------|
| Case 1: Subscription Creation | TBD | QA | Pending | Initial subscription setup |
| Case 2: Webhook Processing | TBD | QA | Pending | Email → record creation |
| Case 3.1: Contact Association | TBD | QA | Pending | Known sender linking |
| Case 3.2: Matter Association | TBD | QA | Pending | Subject reference linking |
| Case 3.3: Thread Association | TBD | QA | Pending | Reply threading |
| Case 3.4: Pending Review | TBD | QA | Pending | Unknown sender handling |
| Case 4: Backup Polling | TBD | QA | Pending | Webhook miss fallback |
| Case 5: Deduplication | TBD | QA | Pending | Race condition handling |
| Case 6: Subscription Renewal | TBD | QA | Pending | 3-day lifecycle management |
| Case 7.1: BFF Restart | TBD | QA | Pending | Recovery after restart |
| Case 7.2: Malformed Webhook | TBD | QA | Pending | Input validation |
| Case 7.3: Invalid clientState | TBD | QA | Pending | Security validation |
| Case 7.4: Service Bus Outage | TBD | QA | Pending | Resilience & recovery |

---

## Sign-Off

**Test Plan Author**: AI Assistant
**Date Created**: 2026-03-09
**Status**: Ready for Deployment & Testing

**Approver**: [To be assigned during deployment phase]
**Approval Date**: [Pending]

**Test Execution Lead**: [To be assigned during deployment phase]
**Execution Start Date**: [Pending deployment]

---

## Appendix: Troubleshooting Guide

### Issue: Webhook Notification Not Received

**Symptom**: Email sent to monitored mailbox, but no webhook log entry appears.

**Probable Causes**:
- Webhook URL not publicly accessible (Graph can't reach it)
- Graph subscription not created or expired
- Network firewall blocking inbound Graph notifications
- Webhook endpoint returning non-202 status

**Resolution**:
1. Verify subscription exists: Check Dataverse CommunicationAccount `sprk_subscriptionid`
2. Test webhook URL from public IP:
   ```bash
   curl -X POST https://your-webhook-url/api/communications/incoming-webhook \
     -H "Content-Type: application/json" \
     -d '{"test": true}'
   ```
3. Check BFF logs for webhook endpoint requests
4. Verify network ACL/firewall allows HTTPS inbound from Microsoft Graph IPs
5. If using ngrok, verify ngrok tunnel is active and URL is correct
6. Verify subscription's `notificationUrl` matches actual webhook URL

### Issue: Job Not Enqueued to Service Bus

**Symptom**: Webhook received, BFF logs show "Enqueueing job", but Service Bus queue is empty.

**Probable Causes**:
- Service Bus connection string incorrect
- Service Bus queue/topic doesn't exist
- Service Bus credentials missing or invalid
- Job serialization error

**Resolution**:
1. Verify Service Bus connection string in configuration: `ServiceBusConnectionString`
2. Verify queue exists and is accessible: `az servicebus queue show --namespace-name [ns] --name [queue]`
3. Check BFF logs for Service Bus connection errors
4. Verify system user has "Send" permission on Service Bus queue
5. Test Service Bus connectivity separately: Use Azure SDK sample code

### Issue: Communication Record Not Created After Job Processing

**Symptom**: Job processed (logs show "Processing IncomingCommunication"), but no record in Dataverse.

**Probable Causes**:
- Dataverse connection failed mid-processing
- System user doesn't have write permission on `sprk_communication`
- Record creation threw exception (check logs)
- Job payload missing required fields

**Resolution**:
1. Check job handler logs: Look for "Creating communication record" and any exceptions
2. Verify system user permissions in Dataverse: Security role must include Create on Communication
3. Check job payload in Service Bus: Ensure it has all required fields (from, to, subject, etc.)
4. Test Dataverse connectivity separately: Use Power Platform CLI or SDK

### Issue: Association Not Applied (Contact/Matter/Thread)

**Symptom**: Communication record created, but `sprk_regardingperson` / `sprk_regardingmatter` is empty.

**Probable Causes**:
- AssociationResolver service not running or crashed
- Contact email doesn't exactly match sender email
- Matter reference pattern not recognized in subject
- Thread ID not matching parent message

**Resolution**:
1. Verify AssociationResolver service is running: Check logs for "Resolving associations"
2. Check contact email matches exactly: Case-sensitive, no leading/trailing spaces
3. Verify matter reference pattern in subject (e.g., "MAT-12345") matches resolver logic
4. Check thread ID in Graph notification matches conversation ID
5. Review logs for "No matching contact found" or similar messages

### Issue: Duplicate Communication Records

**Symptom**: Same email creates two `sprk_communication` records (webhook + polling both processed).

**Probable Causes**:
- Redis idempotency check failed or bypassed
- Dataverse `sprk_graphmessageid` uniqueness constraint not enforced
- Deduplication logic not running

**Resolution**:
1. Verify Redis is configured and accessible: Check BFF logs for Redis connection
2. Verify `sprk_graphmessageid` field has uniqueness constraint in Dataverse
3. Check job handler logs: Look for "Checking Redis idempotency key"
4. If Redis unavailable, verify Dataverse-level dedup:
   ```query
   SELECT * FROM sprk_communication WHERE sprk_graphmessageid = "[id]"
   ```
   Should return exactly one record.
5. If multiple records exist, manually delete duplicates and verify dedup logic

### Issue: Backup Polling Never Runs

**Symptom**: BFF logs don't show polling cycle (every 5 minutes expected).

**Probable Causes**:
- InboundPollingBackupService not started
- Polling disabled in configuration
- No receive-enabled accounts configured

**Resolution**:
1. Check BFF startup logs: Look for "InboundPollingBackupService starting"
2. Verify configuration enables polling: `EnableInboundPolling: true`
3. Verify at least one CommunicationAccount has `sprk_receiveenabled = true`
4. Check logs for "Starting polling cycle" every 5 minutes
5. If not found, enable debug logging and check for exceptions

### Issue: Subscription Not Renewed Before Expiry

**Symptom**: Webhook stops firing after 3 days (subscription expired).

**Probable Causes**:
- GraphSubscriptionManager renewal logic not running
- Renewal attempt failed (Graph error)
- New subscription ID not stored in Dataverse

**Resolution**:
1. Monitor logs starting 24 hours before subscription expiry
2. Look for: "Checking subscription renewal" and "Subscription expiring in [X hours]"
3. If not found, verify GraphSubscriptionManager is running
4. If renewal attempted, check for errors: 401 (credentials), 403 (permissions), 400 (invalid request)
5. Manually renew if needed: Delete old subscription, trigger subscription creation

### Issue: BFF Doesn't Start After Restart

**Symptom**: BFF crashes on startup when trying to load subscriptions.

**Probable Causes**:
- Startup logic tries to renew expired subscriptions and fails
- Graph API credentials invalid
- Dataverse connection fails

**Resolution**:
1. Check startup logs: Look for exception before BFF listens
2. Verify Graph API credentials are valid
3. Verify Dataverse connection string is correct
4. Temporarily comment out subscription renewal in startup to get BFF running
5. Fix underlying issue (credentials, connectivity), then restart

---

## Related Documents

- **Specification**: `projects/email-communication-solution-r2/spec.md` - Complete requirements for Phase C
- **Implementation Plan**: `projects/email-communication-solution-r2/plan.md` - Timeline and deliverables
- **Task Definition**: `projects/email-communication-solution-r2/tasks/027-inbound-e2e-test.poml` - Task specification
- **Phase A Results**: `projects/email-communication-solution-r2/notes/research/r1-assessment-phase-a.md` - Phase A foundation
- **Phase B Results**: `projects/email-communication-solution-r2/notes/research/phase-b-e2e-results.md` - Outbound send testing
- **Architecture**: `projects/email-communication-solution-r2/design-communication-accounts.md` - Data model and architecture

---

**Document Version**: 1.0
**Last Updated**: 2026-03-09
