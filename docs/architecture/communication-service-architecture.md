# Communication Service Architecture

> **Last Updated**: April 5, 2026
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current
> **Purpose**: Architecture documentation for the Communication Service module — outbound/inbound email via Microsoft Graph, Dataverse-managed mailbox accounts, webhook/polling hybrid for inbound, SPE archival, deduplication, mailbox verification, and AI playbook integration.

---

## Overview

The Communication Service provides a unified email communication layer built on Microsoft Graph. It supports outbound email (shared mailbox via app-only and individual user via OBO), inbound email processing via a webhook/polling hybrid with multi-layer deduplication, `.eml` archival to SharePoint Embedded, and integration with the AI playbook system. All mailbox configuration is managed through Dataverse `sprk_communicationaccount` records rather than static configuration files.

The critical design principle is **Graph send is the critical path** — if the Graph `sendMail` call fails, the entire operation fails. All tracking (Dataverse record creation, SPE archival, attachment records, AI analysis) is best-effort and wrapped in try/catch.

---

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| CommunicationService | `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs` | Core send pipeline: validates, resolves sender, builds Graph message, sends, best-effort tracking |
| ApprovedSenderValidator | `src/server/api/Sprk.Bff.Api/Services/Communication/ApprovedSenderValidator.cs` | Two-tier sender validation (config-based + Dataverse accounts) |
| CommunicationAccountService | `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationAccountService.cs` | Manages `sprk_communicationaccount` records with Redis caching (5-min TTL) |
| IncomingCommunicationProcessor | `src/server/api/Sprk.Bff.Api/Services/Communication/IncomingCommunicationProcessor.cs` | Processes inbound emails: dedup, fetch from Graph, create record, resolve associations, process attachments, archive .eml |
| IncomingAssociationResolver | `src/server/api/Sprk.Bff.Api/Services/Communication/IncomingAssociationResolver.cs` | 3-level priority cascade: thread → sender → subject pattern matching |
| GraphSubscriptionManager | `src/server/api/Sprk.Bff.Api/Services/Communication/GraphSubscriptionManager.cs` | BackgroundService (30-min cycle): creates, renews, recreates Graph webhook subscriptions; orphan cleanup |
| InboundPollingBackupService | `src/server/api/Sprk.Bff.Api/Services/Communication/InboundPollingBackupService.cs` | BackgroundService (5-min cycle): polls Graph for unread messages missed by webhooks |
| DailySendCountResetService | `src/server/api/Sprk.Bff.Api/Services/Communication/DailySendCountResetService.cs` | BackgroundService (midnight UTC): resets `sprk_sendstoday` to 0 for all accounts |
| MailboxVerificationService | `src/server/api/Sprk.Bff.Api/Services/Communication/MailboxVerificationService.cs` | Tests send/read capabilities, persists results, auto-creates Graph subscription on successful read verification |
| EmlGenerationService | `src/server/api/Sprk.Bff.Api/Services/Communication/EmlGenerationService.cs` | Generates RFC 2822 `.eml` files for archival |
| GraphAttachmentAdapter | `src/server/api/Sprk.Bff.Api/Services/Communication/GraphAttachmentAdapter.cs` | Adapts Graph attachment objects for processing |
| GraphMessageToEmlConverter | `src/server/api/Sprk.Bff.Api/Services/Communication/GraphMessageToEmlConverter.cs` | Converts Graph Message to .eml format |
| CommunicationJobProcessor | `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationJobProcessor.cs` | Job handler for async communication processing |

---

## Data Flow

### Outbound Send Pipeline

`CommunicationService.SendAsync()` supports two modes based on `SendMode`:

1. **SharedMailbox Mode** (App-Only Auth): Validates request → resolves approved sender via `ApprovedSenderValidator` → checks daily send limit (`SendsToday >= DailySendLimit` returns HTTP 429 `DAILY_SEND_LIMIT_REACHED`) → builds Graph message → sends via `graphClient.Users[sender].SendMail.PostAsync()` (**critical path**) → best-effort tracking (Dataverse `sprk_communication` record, SPE `.eml` archival, attachment records).

2. **User Mode** (OBO Auth): Same pipeline but sender derived from JWT claims; sends via `graphClient.Me.SendMail.PostAsync()`.

**Daily send limit**: Enforced synchronously before Graph call. `DailySendCountResetService` resets counts at midnight UTC.

### Inbound Pipeline

1. Graph change notifications arrive at the anonymous `/incoming-webhook` endpoint
2. Validated via `clientState` header
3. Deduplicated in-memory (10-min window, keyed by **message ID** — not subscription ID, to catch duplicates from multiple subscriptions)
4. Enqueued as `IncomingCommunication` job to Service Bus with `IdempotencyKey = Communication:{messageId}:Process`

`IncomingCommunicationProcessor` then processes the job:

5. Deduplicates against `sprk_graphmessageid` in Dataverse
6. Resolves mailbox account: direct email match → stored `subscriptionId` → single-account fallback for GUIDs
7. Fetches full message from Graph via app-only auth
8. Creates `sprk_communication` record; prefers `uniqueBody` over full body to strip reply chains
9. Resolves associations via 3-level cascade (thread → sender → subject pattern matching)
10. Processes attachments and archives `.eml` to SPE (both best-effort)
11. Marks message as read in Graph (best-effort)

---

## Mailbox Verification

`MailboxVerificationService.VerifyAsync()` tests communication account capabilities:

1. Retrieves `sprk_communicationaccount` record from Dataverse
2. Sets `sprk_verificationstatus` to `Pending`
3. Tests **send capability** (if `sprk_sendenabled`): sends test email to account's own address via `graphClient.Users[email].SendMail.PostAsync()`
4. Tests **read capability** (if `sprk_receiveenabled`): queries `graphClient.Users[email].Messages.GetAsync($top=1)`. Success means the mailbox is accessible even if zero messages are returned.
5. Determines overall status: all tested capabilities must pass for `Verified`; any failure results in `Failed`
6. Persists results to Dataverse (`sprk_verificationstatus`, `sprk_lastverified`, `sprk_verificationmessage`)
7. **Auto-creates Graph subscription**: If read verification passed, creates a Graph webhook subscription immediately so inbound email processing starts without waiting for the next `GraphSubscriptionManager` cycle

---

## Multi-Layer Deduplication

Inbound emails are deduplicated at four levels to prevent duplicate processing:

| Level | Mechanism | Key | TTL/Scope |
|-------|-----------|-----|-----------|
| 1 | In-memory webhook cache | `messageId` (not subscriptionId) | 10-min window |
| 2 | Service Bus `MessageId` | `IdempotencyKey` = SHA-256 hashed for keys > 128 chars | Service Bus dedup window |
| 3 | Dataverse `sprk_graphmessageid` query | Graph message ID | Permanent (record lifetime) |
| 4 | Dataverse duplicate detection rule | Configured in solution | Permanent |

---

## Sender Resolution

`ApprovedSenderValidator` uses a two-tier model:

- **Tier 1**: Config-based `ApprovedSenders` list (synchronous, no Dataverse call)
- **Tier 2**: Dataverse `sprk_communicationaccount` records via Redis-cached `CommunicationAccountService` (5-min TTL). Dataverse accounts overlay config senders on email match.

Resolution priority for `fromMailbox`:
- `null` → default sender (`IsDefault=true`, else `DefaultMailbox` config match, else first sender)
- Explicit email → must match approved list (case-insensitive) or returns `INVALID_SENDER`

Shared mailboxes are not matter-specific — no automatic default-matter assignment. Unassociated inbound emails surface for manual review via `Pending Review` status.

---

## Inbound Association Resolution

`IncomingAssociationResolver` uses a 3-level priority cascade. First match wins:

1. **Thread**: Fetches `In-Reply-To` header from Graph; searches `sprk_internetmessageid` then `sprk_graphmessageid`; copies regarding fields from parent record.
2. **Sender**: Queries `contact` by sender email; queries `account` by sender email domain (skips common providers like gmail.com, outlook.com).
3. **Subject pattern**: Applies regex patterns (`MAT-(\d+)`, `Matter\s*#(\d+)`, etc.) against subject line to find `sprk_matter` by reference number.

---

## Graph Subscription Management

`GraphSubscriptionManager` (BackgroundService, 30-min cycle, 10s startup delay) manages the subscription lifecycle per receive-enabled account:

| Condition | Action |
|-----------|--------|
| No SubscriptionId on account | CREATE new subscription |
| Expiry < 24h from now | RENEW (PATCH expiration, extend by 3 days) |
| Renewal fails (404/error) | DELETE old + CREATE new |
| Otherwise | SKIP (healthy) |

**Orphan cleanup**: Each cycle lists all Graph subscriptions and deletes untracked orphans whose `NotificationUrl` matches the configured webhook URL. This prevents duplicate webhook notifications from accumulated subscriptions across deployments — this was the root cause of duplicate email processing discovered in March 2026.

**Subscription resource**: `users/{email}/mailFolders/{monitorFolder}/messages` with `changeType=created`. Graph maximum subscription lifetime: 3 days.

**Startup resilience**: Initial cycle is wrapped in try/catch. An unhandled exception from `ExecuteAsync` triggers .NET 8's default `StopHost` behavior; wrapping ensures retry on the next timer tick.

---

## Background Services

Three `BackgroundService` implementations following ADR-001:

| Service | Interval | Startup Delay | Purpose |
|---------|----------|---------------|---------|
| GraphSubscriptionManager | 30 min | 10s | Subscription lifecycle: create, renew, recreate, orphan cleanup |
| InboundPollingBackupService | 5 min | 15s | Queries unread messages in Graph to catch webhooks missed during deployments (max 50 per poll) |
| DailySendCountResetService | Midnight UTC | — | Resets `sprk_sendstoday` to 0 for all accounts |

---

## Authentication

| Component | Auth Type | Why |
|-----------|-----------|-----|
| Outbound SharedMailbox | App-only (`GraphClientFactory.ForApp()`) | No per-user context needed for shared mailbox |
| Outbound User mode | OBO (`GraphClientFactory.ForUserAsync()`) | User sends as themselves; identity preserved |
| Inbound webhook receiver | Anonymous + `clientState` validation | Graph sends no user context |
| Inbound processor | App-only | No user context in job handlers |
| Mailbox verification | App-only | Tests mailbox capabilities without user context |

**Exchange Application Access Policy**: Security group on `sprk_securitygroupid` restricts which mailboxes the app registration can access.

---

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | Microsoft Graph | `IGraphClientFactory` → `GraphServiceClient` | sendMail, subscription management, message fetch |
| Depends on | Dataverse | `ICommunicationDataverseService`, `IGenericEntityService` | Record CRUD, account management |
| Depends on | SPE/Documents | `SpeFileStore` | .eml archival and attachment storage |
| Depends on | Redis | `IDistributedCache` | Account caching (5-min TTL), webhook dedup |
| Depends on | Service Bus | `JobSubmissionService` | Inbound email job enqueue |
| Consumed by | AI Playbooks | `SendCommunicationToolHandler` (IAiToolHandler) | AI-sent emails use same pipeline |
| Consumed by | Finance Intelligence | Email attachments trigger classification pipeline | Feature-flagged |

---

## AI Playbook Integration

`SendCommunicationToolHandler` implements `IAiToolHandler` (ADR-013). AI-sent emails go through the same validation, sender resolution, archival, and tracking pipeline as UI-initiated sends. This ensures consistency regardless of trigger source.

---

## Error Codes

| Code | HTTP | Scenario |
|------|------|----------|
| `INVALID_SENDER` | 400 | Sender not in approved list |
| `DAILY_SEND_LIMIT_REACHED` | 429 | `SendsToday >= DailySendLimit` |
| `ATTACHMENT_LIMIT_EXCEEDED` | 400 | >150 attachments or >35MB total |
| `GRAPH_SEND_FAILED` | 502/500 | Graph sendMail API error |

---

## Known Pitfalls

1. **Duplicate webhook/poll processing**: Before orphan cleanup was added to `GraphSubscriptionManager`, accumulated subscriptions across deployments caused duplicate webhook notifications. The orphan cleanup (matching `NotificationUrl`) was the fix. Always ensure subscriptions are cleaned up on deployment.

2. **Subscription expiry (3-day Graph maximum)**: Graph mail subscriptions have a hard 3-day lifetime. If the `GraphSubscriptionManager` is down for >3 days, all subscriptions expire silently. The `InboundPollingBackupService` catches these gaps, but there is a 5-minute window where new emails may be delayed.

3. **In-memory dedup keyed by messageId, not subscriptionId**: The webhook dedup cache uses `messageId` as key because the same message can arrive via multiple subscriptions (if orphan subscriptions exist). Using `subscriptionId` would miss these duplicates.

4. **Service Bus IdempotencyKey length**: SHA-256 hashing is applied when `IdempotencyKey` exceeds 128 characters. The `Communication:{messageId}:Process` format can exceed this limit with long Graph message IDs.

5. **`uniqueBody` preferred over `body`**: When creating `sprk_communication` records, the processor uses `uniqueBody` (if available) to strip reply chains. This prevents storing the entire email thread in each record.

6. **Mailbox verification auto-creates subscription**: On successful read verification, `MailboxVerificationService` immediately creates a Graph subscription. If this fails, it is best-effort — the `GraphSubscriptionManager` will create it on its next 30-minute cycle.

7. **Sender validation is synchronous**: The approved sender list is validated before any Graph call. A misconfigured sender list blocks all sends immediately with HTTP 400.

---

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Graph API over Dataverse email activities | Microsoft Graph `sendMail` | Avoids complex activity party resolution, faster send, no plugin overhead | — |
| Graph subscriptions + polling backup | Webhook primary, 5-min poll backup | Real-time (<60s) with fault tolerance; no Exchange Server-Side Sync dependency | — |
| `sprk_communicationaccount` entity | Dataverse-managed config | Replaces `appsettings.json`-only config; travels with solution imports | — |
| Best-effort tracking | Graph send is critical path | Tracking failures are logged but don't fail the send operation | — |
| Dual send modes | SharedMailbox (app-only) + User (OBO) | Different auth contexts for different use cases | ADR-008 |
| Feature module DI | `CommunicationModule` registers all services | ADR-010 compliant; single registration point | ADR-010 |

---

## Constraints

- **MUST**: Treat Graph `sendMail` as the sole critical path; all tracking is best-effort
- **MUST**: Validate approved sender list synchronously before any Graph call
- **MUST**: Use endpoint filters for authorization, not global middleware (ADR-008)
- **MUST**: Register all services via `CommunicationModule` (ADR-010)
- **MUST NOT**: Make retry decisions in the service; callers handle retries
- **MUST NOT**: Use `SpeFileStore` with leaked Graph SDK types (ADR-007)

---

## Related

- [sdap-auth-patterns.md](sdap-auth-patterns.md) — Auth patterns (app-only for background, OBO for user-mode)
- [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) — BFF patterns including job handler pattern
- [email-processing-architecture.md](email-processing-architecture.md) — Merged email-to-document pipeline (R1 + R2)
- [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) — AI tool handler pattern for SendCommunicationToolHandler

---

*See also: [Admin Guide](../guides/COMMUNICATION-ADMIN-GUIDE.md) | [Deployment Guide](../guides/COMMUNICATION-DEPLOYMENT-GUIDE.md)*

*Last Updated: April 5, 2026*
