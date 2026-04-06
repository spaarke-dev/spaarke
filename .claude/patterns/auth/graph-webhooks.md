# Graph Webhook / Subscription Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Implementing or modifying Graph change notification subscriptions (e.g., email inbox monitoring).

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Services/Communication/GraphSubscriptionManager.cs` — Full subscription lifecycle (create, renew, delete)

## Constraints
- **ADR-001**: Use BackgroundService for subscription renewal — not Azure Functions
- Webhook endpoint must respond to validation handshake within 10 seconds

## Key Rules
- Subscriptions expire (max 4230 min for mail) — BackgroundService renews before expiry
- Subscription resource: `/users/{id}/mailFolders('Inbox')/messages` for email monitoring
- Notification URL must be publicly accessible HTTPS endpoint
- Change notifications contain `resourceData` with entity ID — fetch full data separately
- Handle duplicate notifications idempotently (same change can arrive multiple times)
