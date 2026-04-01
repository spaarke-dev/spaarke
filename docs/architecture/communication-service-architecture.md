# Communication Service Architecture

> **Last Updated**: March 12, 2026
> **Purpose**: Architecture documentation for the Communication Service module — outbound/inbound email via Microsoft Graph, Dataverse-managed mailbox accounts, SPE archival, and AI playbook integration.
> **Status**: Implemented (R2 Complete)

---

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Graph API over Dataverse email activities | Avoids complex activity party resolution, faster send, no plugin overhead |
| Graph subscriptions over Server-Side Sync | Real-time webhooks (<60s), backup polling (<5 min), no Exchange dependency |
| `sprk_communicationaccount` entity | Centralized mailbox management in Dataverse; replaces `appsettings.json`-only config |
| Best-effort tracking | Email send is the critical path; Dataverse record, SPE archival, and attachment records are non-fatal |
| Dual send modes (SharedMailbox + User) | Shared mailbox via app-only auth; individual user via OBO |
| Per-endpoint authorization filter | Follows ADR-008; avoids global middleware |
| Feature module DI pattern | `CommunicationModule` registers all services; ADR-010 compliant |

---

## Architecture Principles

1. **Graph Send is Critical Path**: If Graph `sendMail` fails, the entire operation fails. No partial success.
2. **Best-Effort Tracking**: Dataverse record creation, SPE archival, attachment records, and AI analysis are wrapped in try/catch. Failures are logged as warnings.
3. **No Retry Logic**: Failures are immediate. Callers handle retry decisions.
4. **Multi-Layer Deduplication**: Inbound emails are deduplicated at four levels: in-memory webhook cache (keyed by message ID) → Service Bus `MessageId` set to `IdempotencyKey` (SHA-256 hashed for keys >128 chars) → Dataverse `sprk_graphmessageid` query → Dataverse duplicate detection rule.
5. **Sender Validation Before Send**: The approved sender list is validated synchronously before any Graph call.
6. **Correlation ID Tracing**: Every operation is tagged with a `correlationId` for end-to-end tracing.

---

## Outbound Send Pipeline

`CommunicationService.SendAsync()` supports two modes based on `SendMode`:

- **SharedMailbox Mode** (App-Only Auth): Validates request → resolves approved sender → checks daily send limit → builds Graph message → sends via `graphClient.Users[sender].SendMail.PostAsync()` (CRITICAL PATH) → best-effort tracking (Dataverse record, SPE archival, attachment records).
- **User Mode** (OBO Auth): Same pipeline but sender is derived from JWT claims; sends via `graphClient.Me.SendMail.PostAsync()`.

**Daily send limit**: Enforced before Graph call. Returns HTTP 429 `DAILY_SEND_LIMIT_REACHED` when `SendsToday >= DailySendLimit`. The `DailySendCountResetService` resets counts at midnight UTC.

---

## Inbound Pipeline

Graph change notifications arrive at the anonymous `/incoming-webhook` endpoint. Each notification is:
1. Validated via `clientState`
2. Deduplicated in-memory (10-min window, keyed by message ID — not subscription ID, to catch duplicates from multiple subscriptions)
3. Enqueued as `IncomingCommunication` job to Service Bus with `IdempotencyKey = Communication:{messageId}:Process`

`IncomingCommunicationProcessor` then processes the job:
- Deduplicates against `sprk_graphmessageid` in Dataverse
- Resolves mailbox account (direct email match → stored subscriptionId → single-account fallback for GUIDs)
- Fetches full message from Graph
- Creates `sprk_communication` record, prefers `uniqueBody` over full body to strip reply chains
- Resolves associations via 3-level cascade (thread → sender → subject pattern matching)
- Processes attachments and archives `.eml` to SPE (both best-effort)
- Marks message as read in Graph (best-effort)

---

## Sender Resolution

`ApprovedSenderValidator` uses a two-tier model:

- **Tier 1**: Config-based `ApprovedSenders` list (synchronous).
- **Tier 2**: Dataverse `sprk_communicationaccount` records queried via Redis-cached `CommunicationAccountService` (5-min TTL). Dataverse accounts overlay config senders on email match.

Resolution priority for `fromMailbox`:
- `null` → default sender (`IsDefault=true`, else `DefaultMailbox` match, else first sender)
- explicit email → must match approved list (case-insensitive) or returns `INVALID_SENDER`

Shared mailboxes are not matter-specific — no automatic default-matter assignment. Unassociated inbound emails surface for manual review via `Pending Review` status.

---

## Inbound Association Resolution

`IncomingAssociationResolver` uses a 3-level priority cascade. First match wins:

1. **Thread**: Fetches `In-Reply-To` header from Graph; searches `sprk_internetmessageid` then `sprk_graphmessageid`; copies regarding fields from parent record.
2. **Sender**: Queries `contact` by sender email; queries `account` by sender email domain (skips common providers).
3. **Subject pattern**: Applies regex patterns (`MAT-(\d+)`, `Matter\s*#(\d+)`, etc.) against subject line to find `sprk_matter` by reference number.

---

## Graph Subscription Management

`GraphSubscriptionManager` (30-min cycle) manages the subscription lifecycle per receive-enabled account:

| Condition | Action |
|-----------|--------|
| No SubscriptionId | CREATE new subscription |
| Expiry < 24h from now | RENEW (PATCH expiration) |
| Renewal fails (404/error) | DELETE old + CREATE new |
| Otherwise | SKIP (healthy) |

**Orphan cleanup** is performed each cycle — lists all Graph subscriptions, deletes untracked orphans whose `NotificationUrl` matches the configured webhook URL. This prevents duplicate webhook notifications from accumulated subscriptions across deployments. This was the root cause of duplicate email processing discovered in March 2026.

Subscription resource: `users/{email}/mailFolders/{monitorFolder}/messages` with `changeType=created`. Graph maximum subscription lifetime: 3 days.

---

## Background Services

Three `BackgroundService` implementations following ADR-001:

- **GraphSubscriptionManager** (30-min cycle, 10s startup delay): Subscription lifecycle.
- **InboundPollingBackupService** (5-min cycle, 15s startup delay): Queries unread messages in Graph to catch webhooks missed during deployments. Max 50 messages per poll.
- **DailySendCountResetService** (midnight UTC): Resets `sprk_sendstoday` to 0 for all accounts.

**Startup resilience**: All services wrap the initial cycle in try/catch. An unhandled exception from `ExecuteAsync` triggers .NET 8's default `StopHost` behavior; wrapping ensures retry on the next timer tick.

---

## Authentication

| Component | Auth Type | Why |
|-----------|-----------|-----|
| Outbound SharedMailbox | App-only (`GraphClientFactory.ForApp()`) | No per-user context needed for shared mailbox |
| Outbound User mode | OBO (`GraphClientFactory.ForUserAsync()`) | User sends as themselves; identity preserved |
| Inbound webhook receiver | Anonymous + clientState validation | Graph sends no user context |
| Inbound processor | App-only | No user context in job handlers |

Exchange Application Access Policy (security group on `sprk_securitygroupid`) restricts which mailboxes the app registration can access.

---

## AI Playbook Integration

`SendCommunicationToolHandler` implements `IAiToolHandler` (ADR-013). AI-sent emails go through the same validation, sender resolution, archival, and tracking as UI-initiated sends. This ensures consistency regardless of trigger source.

---

## Error Codes

| Code | HTTP | Scenario |
|------|------|----------|
| `INVALID_SENDER` | 400 | Sender not in approved list |
| `DAILY_SEND_LIMIT_REACHED` | 429 | `SendsToday >= DailySendLimit` |
| `ATTACHMENT_LIMIT_EXCEEDED` | 400 | >150 attachments or >35MB total |
| `GRAPH_SEND_FAILED` | 502/500 | Graph sendMail API error |

---

## ADR Compliance

| ADR | Compliance |
|-----|-----------|
| ADR-001 | Minimal API endpoints + 3 BackgroundServices |
| ADR-007 | `SpeFileStore` facade for all SPE operations |
| ADR-008 | `CommunicationAuthorizationFilter` as endpoint filter, not global middleware |
| ADR-010 | All services registered via `CommunicationModule`; concrete types, singletons |
| ADR-013 | `SendCommunicationToolHandler` extends BFF via `IAiToolHandler` |

---

*See also: [Admin Guide](../guides/COMMUNICATION-ADMIN-GUIDE.md) | [Deployment Guide](../guides/COMMUNICATION-DEPLOYMENT-GUIDE.md)*
