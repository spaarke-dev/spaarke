---
name: assistant-push-channel-2026-07-15
description: Server->client push options for Assistant proactive suggestions (SignalR Service vs Web PubSub vs persistent SSE vs polling); recommendation = Azure SignalR Service + durable outbox
metadata:
  type: project
---

## 2026-07-15: Server-initiated push channel for Assistant proactive suggestions

**Question**: Long-term server->client push architecture for `spaarkeai-assistant-enhancements-r1` proactive/unsolicited-surface mechanism (code page SPA + .NET 8 BFF on App Service; today only request-scoped SSE).

**Findings**:
- **Recommendation: Azure SignalR Service (Default mode) + hub in BFF + durable pending-suggestion outbox.** MS Learn explicitly recommends Azure SignalR Service for SignalR apps on App Service; it removes the ARR-affinity/WebSocket-enable/Redis-backplane burden entirely (app server holds only one service connection; clients connect to the service).
- **Durable outbox is required regardless of channel.** Azure SignalR does NOT persist messages (at-most-once; no offline replay — reliability doc is explicit). Web PubSub same (reliable subprotocol only covers in-flight recovery). Pattern: outbox is truth, push is a hint; client fetches pending on connect, push triggers refetch.
- **Web PubSub**: MS FAQ says it targets non-SignalR/polyglot scenarios; for .NET + SignalR-shaped apps use Azure SignalR. Event-handler upstream webhooks add surface. Not a fit.
- **Self-hosted persistent SSE**: viable mid-term (native EventSource + Last-Event-ID pairs beautifully with outbox; zero new Azure resource; reuses fetch-parser) BUT at scale-out needs Redis pub/sub routing + keep-alive comments to defeat App Service's non-configurable 230s idle timeout + per-instance connection ownership — you end up rebuilding SignalR. EventSource can't set Authorization headers; must use fetch-based parser or query token.
- **Dataverse-native**: in-app notifications (`appnotification` + `SendAppNotification`) are durable and per-user but delivery is CLIENT POLLING by the MDA shell, and they render in the MDA notification center, not inside the code-page Assistant pane. Complementary, not a substitute.
- App Service WebSocket limits: Basic 350/instance; Standard+ effectively unlimited (Windows); Linux ~50k/instance. Azure SignalR Free = 20 connections (dev only); Standard S1 ~= $49/mo per unit (1K concurrent conns, 1M msgs/day).
- CSP: if Power Platform env CSP enabled, `wss://*.service.signalr.net` must be in connect-src; `@microsoft/signalr` ~40KB gzip; stateful reconnect (.NET 8 `AllowStatefulReconnects`) covers blips, not offline.

**Sources**:
- https://learn.microsoft.com/aspnet/core/signalr/publish-to-azure-web-app (recommends Azure SignalR on App Service)
- https://learn.microsoft.com/aspnet/core/signalr/scale (sticky sessions / backplane)
- https://learn.microsoft.com/azure/reliability/reliability-signalr (no persistence, at-most-once)
- https://learn.microsoft.com/azure/azure-web-pubsub/resource-faq (SignalR vs Web PubSub choice)
- https://learn.microsoft.com/answers/questions/825154/ (App Service 230s timeout non-configurable)
- https://learn.microsoft.com/power-apps/developer/model-driven-apps/clientapi/send-in-app-notifications (polling-based)
- https://azure.microsoft.com/pricing/details/signalr-service/

**Open questions**: exact outbox home (Dataverse `sprk_` table vs existing store); whether Assistant pane should also mirror suggestions into `appnotification` for MDA-shell visibility; CSP state of Spaarke's target environments.
