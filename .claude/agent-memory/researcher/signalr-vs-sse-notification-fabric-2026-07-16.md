---
name: signalr-vs-sse-notification-fabric-2026-07-16
description: Azure SignalR vs SSE for cross-channel in-app notification fabric; messaging-communication-app-r1 + spaarkeai-assistant-enhancements-r1 phase 1.5; recommend defer SignalR to r2 for MDA-only r1
metadata:
  type: project
---

# Azure SignalR vs SSE as cross-channel notification fabric (2026-07-16)

**Context**: `projects/messaging-communication-app-r1` (ACS-transport messaging) proposes SignalR as a "cross-channel in-app notification fabric" — pushes timeline/badge/toast updates to all app surfaces after server persists ANY communication (chat/email/SMS). Spaarke today has NO SignalR; real-time = SSE only (ADR-033 document-stream side-channel + AI chat streaming). SignalR also flagged as `spaarkeai-assistant-enhancements-r1` "phase 1.5" push provider → shared fabric desirable.

**Why:** messaging synopsis §Real-time planes already marks SignalR "v1-optional, can ship with fan-out job rather than blocking first thread round-trip." The two-plane model (ACS WebSocket for live thread + SignalR for cross-channel fan-out) is deliberate and Microsoft-aligned (D365 Contact Center uses the same ACS-transport + Dataverse-record pattern).

**How to apply:** For a **model-driven-app-only r1**, SignalR is NOT needed. MDA can get cross-channel badge/timeline refresh from what already exists: Dataverse push (form `addOnPostSave`/subgrid refresh, or client polling of the Communication table). SignalR earns its place at **r2** when portal + Teams-tab + Spaarke-AI-code-page surfaces need simultaneous fan-out (SSE can't fan-out server→many-hosts cleanly and hits the HTTP/1.1 6-connection-per-domain cap). Recommendation: contract-first now (define notification envelope + a `INotificationPublisher` seam in the fan-out job), swap SSE→SignalR transport at r2. Build the fabric ONCE, shared across messaging + assistant-enhancements.

## Key technical facts (cited)
- **SSE**: server→client only, one long-lived HTTP response, browser HTTP/1.1 **6-connection-per-domain cap** (mitigated only over HTTP/2 multiplexing). No native server-side fan-out/backplane; each app-service instance owns its own SSE connections → no cross-instance broadcast without a backplane you build. Great for per-request token streaming (current use), weak as a multi-surface broadcast fabric.
- **Azure SignalR Service**: managed backplane + connection offload. Client connects → **redirected to the service**, so **NO sticky sessions required** (vs Redis backplane which needs them). App servers hold few connections; service scales connections independently of message volume.
- **Modes**: *Default* = you host a hub server (full-duplex, `AddAzureSignalR()`). *Serverless* = service accepts client connections only, no server connections; publish via Azure Functions bindings OR the **Management SDK** from any backend (fits "publish from a background job after Dataverse persist" without hosting a hub).
- **Limits/pricing (2025-2026)**: Free = 1 unit, 20 concurrent conns, 20K msgs/day. Standard = **$1.61/unit/day (~$49/mo)**, 1,000 conns/unit, 1M outbound msgs/day/unit included, +~$1 per additional million; up to 100 units (~100K conns). Premium P1 ~$2/unit/day; P2 up to 1,000 units (~1M conns). **Only OUTBOUND messages billed; inbound free; every 2KB = 1 message.**
- **.NET packages**: `Microsoft.Azure.SignalR` 1.33.0 (hub-server / Default mode) · `Microsoft.Azure.SignalR.Management` 1.33.0 (server-side publish to groups/users/all from a background job, no hub host). Footprint modest (~1-2 MB) — within 60 MB BFF ceiling but verify with `dotnet publish` before adopting.
- **Auth**: SignalR hub honors standard ASP.NET Core `[Authorize]` / Entra JWT; map users→connections via `Context.UserIdentifier` (from JWT `sub`/oid claim); Dataverse-derived access → SignalR **Groups** (per matter/thread) for scoped fan-out.

## Sources
- Service limits: github.com/MicrosoftDocs/azure-docs includes/signalr-service-limits.md
- Scale/sticky/backplane: learn.microsoft.com/aspnet/core/signalr/scale · /signalr/redis-backplane
- Service modes: learn.microsoft.com/azure/azure-signalr/concept-service-mode
- Pricing: azure.microsoft.com/pricing/details/signalr-service · ably.com/topic/azure-signalr-pricing
- Packages: nuget.org Microsoft.Azure.SignalR 1.33.0 + .Management 1.33.0
- Internal: projects/messaging-communication-app-r1/spaarke-messaging-solution-synopsis.md §Real-time planes; ADR-033

## Open questions
- Exact MDA cross-channel refresh mechanism in r1 (form script polling vs Dataverse real-time subgrid) — not yet spiked.
- Whether assistant-enhancements phase 1.5 push needs bidirectional (SignalR) or is pure server→client (SSE could suffice there too).
