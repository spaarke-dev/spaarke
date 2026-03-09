# Graph Webhook / Subscription Pattern

> **Domain**: Microsoft Graph Change Notifications / Webhooks
> **Last Validated**: 2026-03-09
> **Source ADRs**: ADR-001 (BackgroundService pattern)

---

## Canonical Implementation

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Services/Communication/GraphSubscriptionManager.cs` | Full subscription lifecycle manager |

---

## Pattern Overview

Graph subscriptions enable real-time notifications when resources change (e.g., new emails arrive).
The BFF manages subscriptions as a `BackgroundService` with a `PeriodicTimer`.

```
┌──────────────────────┐      POST /subscriptions      ┌───────────────┐
│ GraphSubscription    │ ──────────────────────────────►│ Microsoft     │
│ Manager              │                                │ Graph API     │
│ (BackgroundService)  │◄──────────────────────────────│               │
│                      │   POST /api/webhooks/mail     │               │
│ Timer: 30 min        │   (change notification)       │               │
└──────────────────────┘                                └───────────────┘
         │
         │ Creates/Renews/Deletes
         ▼
┌──────────────────────┐
│ Dataverse            │
│ sprk_communication   │
│ account records      │
│ (subscription state) │
└──────────────────────┘
```

---

## Subscription Lifecycle

### Constants

| Setting | Value | Why |
|---------|-------|-----|
| Max subscription lifetime | 3 days | Graph API limit for mail subscriptions |
| Renewal threshold | 24 hours before expiry | Ensures renewal before expiration |
| Check interval | 30 minutes | `PeriodicTimer` frequency |
| Notification URL | `{BFF_BASE_URL}/api/webhooks/mail` | Endpoint receiving change notifications |

### Lifecycle Decision Tree

On each 30-minute timer tick, for each monitored account:

```
Is subscription ID stored in Dataverse?
├── NO → CREATE new subscription
└── YES → Is expiry < 24 hours away?
    ├── NO → SKIP (subscription still valid)
    └── YES → RENEW subscription
              └── 404 error? → RECREATE (delete record + create new)
```

### Create Subscription

```csharp
var subscription = new Subscription
{
    ChangeType = "created",           // Only new messages
    NotificationUrl = notificationUrl, // BFF webhook endpoint
    Resource = $"users/{mailbox}/mailFolders/Inbox/messages",
    ExpirationDateTime = DateTimeOffset.UtcNow.AddDays(3),
    ClientState = clientStateSecret,   // Validates incoming notifications
};

var created = await graphClient.Subscriptions.PostAsync(subscription);
```

### Renew Subscription

```csharp
var update = new Subscription
{
    ExpirationDateTime = DateTimeOffset.UtcNow.AddDays(3),
};

await graphClient.Subscriptions[subscriptionId].PatchAsync(update);
```

### Handle Notification

When Graph sends a change notification to the webhook endpoint:

1. Validate `clientState` matches expected secret
2. Extract `resourceData` for the changed resource
3. Process the change (e.g., read new email, create Dataverse record)
4. Return `202 Accepted` within 3 seconds (Graph requirement)

---

## Auth Mode

Subscription management is **always app-only** (`ForApp()`):

- Subscriptions are service-level resources (not user-scoped)
- Background service has no `HttpContext` for OBO
- Requires `Mail.Read` (Application) permission

---

## Dataverse State Tracking

Subscription state is persisted in Dataverse `sprk_communicationaccount` records:

| Field | Purpose |
|-------|---------|
| `sprk_subscriptionid` | Graph subscription ID (GUID) |
| `sprk_subscriptionexpiry` | Subscription expiration (DateTimeOffset) |
| `sprk_email` | Monitored mailbox address |

This allows the subscription manager to survive BFF restarts — it reads existing subscription state from Dataverse on startup rather than recreating all subscriptions.

---

## Error Handling

| Error | Action |
|-------|--------|
| 404 on renewal | Subscription expired/deleted — recreate |
| 403 on create | Missing `Mail.Read` permission — log error, skip account |
| Network timeout | Polly retry handles via `GraphHttpMessageHandler` |
| Graph throttling (429) | Polly retry with `Retry-After` header |

---

## Extending to New Resource Types

To add Graph subscriptions for other resources (e.g., calendar events, Teams messages):

1. Create a new `BackgroundService` (follow `GraphSubscriptionManager` as template)
2. Change `Resource` to the target (e.g., `users/{id}/events`)
3. Create corresponding webhook endpoint (e.g., `/api/webhooks/calendar`)
4. Add required permissions to app registration (e.g., `Calendars.Read`)
5. Update [oauth-scopes.md](oauth-scopes.md) permission inventory

---

## Related Patterns

- [Graph SDK v5](graph-sdk-v5.md) - Client setup and ForApp() usage
- [OAuth Scopes](oauth-scopes.md) - Permission requirements
- [Graph Endpoints Catalog](graph-endpoints-catalog.md) - Existing operations

---

**Lines**: ~140
