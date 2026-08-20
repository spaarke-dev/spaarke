# Issue Assessment — Azure SignalR real-time notification delivery fails with 401 (UAT env)

> ## ✅ RESOLVED 2026-08-19 (spaarke-notification-spine-r1)
> **Confirmed root cause (via read-only Azure diagnosis): rotated key.** The BFF app setting
> `Notifications__SignalR__ConnectionString` on `spaarke-bff-dev` (rg-spaarke-dev) held an `AccessKey=` that
> matched **neither** the current primary nor secondary key of `spaarke-signalr-dev` — the assessment's #1 prediction.
> The other two suspects were **ruled out**: the connection-string **Endpoint** already pointed at the correct
> resource, and the resource **is** in **Serverless** mode (`features[].ServiceMode = Serverless`). `SignalR:Enabled`
> defaults true.
> **Note on config shape:** in dev/UAT this is a **plaintext App Setting**, NOT a Key Vault reference (the ADR-028
> KV-reference is a production requirement — see follow-up below).
> **Fix applied:** re-copied `spaarke-signalr-dev`'s **current primary connection string** into the app setting via
> `az webapp config appsettings set` (which restarted the app). Verified: configured key now == current primary;
> `/healthz` 200; negotiate returns 401 (auth up), **not** 503 (so SignalR is configured, not null-object). **No code
> deploy needed** — the token-mint code was always correct.
> **Remaining verification (UAT owner):** re-test in a browser — the `@microsoft/signalr` negotiate should now return
> **200** and the client-console poll-fallback warning should disappear (real-time push connects).
> **Follow-ups (not blocking):** (1) the compose-r7 endpoint-host diagnostic log + this project's idempotency fix are
> on master but not yet deployed to `spaarke-bff-dev` — deploying makes future drift diagnosable server-side; (2) both
> keys being stale indicates a rotation happened without updating the app setting — move dev to a **Key Vault
> reference** (ADR-028) so a rotated key can be re-synced in one place, preventing recurrence.

> **Created**: 2026-08-19
> **Origin**: `spaarkeai-compose-r7` UAT (item UAT-10). Surfaced during Compose UAT but **not a Compose issue**.
> **Severity**: Medium — real-time push is degraded; **poll-fallback keeps notifications functional** (delayed, not lost).
> **Disposition**: **ENV / OPS fix on the UAT Azure SignalR resource** — confirmed **not a code defect**.
> **Suggested owner**: UAT-environment / notification-infrastructure owner (**`spaarke-notification-spine-r1`** owns the code path if any change is ever needed). Handed off from compose-r7 for action.

---

## Executive summary

In the UAT environment, the Assistant/notifications client cannot open a **real-time SignalR** connection — the
`@microsoft/signalr` SDK's negotiate against the **Azure SignalR *service*** returns **HTTP 401**. The client
silently degrades to the **REST poll-fallback**, so notifications still arrive (just not instantly).

The BFF side is **correct and healthy**: its own REST negotiate endpoint returns **200**, and the poll-fallback
(same auth) works. The 401 comes from the **Azure SignalR service rejecting the client access token** the BFF
mints for it — a **connection-string / resource / service-mode mismatch on the UAT SignalR resource**, not a bug.

**One most-likely cause**: the SignalR resource's access key was **rotated** and the new key was **not re-copied**
into the BFF's `Notifications__SignalR__ConnectionString` (Key Vault). That produces exactly this signature:
BFF negotiate 200, SignalR service 401.

---

## Symptom

Client console:
```
[SpaarkeAi] Notifications client failed to connect (poll-fallback is active):
Failed to complete negotiation with the server: Status code '401'
```
Real-time push does not connect; the app runs on the REST poll-fallback.

## Root cause (confirmed via full trace, 2026-08-18)

The notification delivery uses **Azure SignalR in Serverless mode** with the **Management SDK, transient
transport** (`Microsoft.Azure.SignalR.Management`, `ServiceTransportType.Transient`). There is **no hosted hub**
in the BFF. Flow:

1. Client calls the BFF REST negotiate (`POST /api/notifications/negotiate`, behind `RequireAuthorization()`) → **200**; returns `{ url, accessToken }` where `url` is the **Azure SignalR service** endpoint and `accessToken` is a client token the BFF **mints locally** from `Notifications:SignalR:ConnectionString`.
2. The `@microsoft/signalr` SDK then negotiates **directly against that Azure SignalR service `url`** using the minted token via `accessTokenFactory`.
3. **The Azure SignalR service rejects that token → 401.** The token's **signature** (derived from the connection-string `AccessKey`) and/or **audience** (derived from the `Endpoint` host) don't match what the target resource expects.

So the 401 is at **step 3 (client ↔ Azure SignalR service)**, never at step 1 (client ↔ BFF).

## What is NOT the problem (ruled out)

- ❌ **BFF REST auth** — negotiate returns 200; the poll-fallback (same JWT auth) works. JWT validation + OBO are fine.
- ❌ **The classic "hosted-hub query-string JWT" gotcha** (`MapHub` + `JwtBearerEvents.OnMessageReceived` reading `access_token` from the query string) — that applies to a **self-hosted** hub. This topology is **serverless with a transient-transport client-negotiate**, so there is correctly **no `MapHub`** and no query-string handling. Do **not** add one.
- ❌ **Compose / SpaarkeAi code** — unrelated; this is shared notification infrastructure.
- ❌ **A code defect anywhere** — the token-mint + negotiate code is correct; it just signs with whatever key/endpoint the UAT config provides.

## Evidence

| Location | Shows |
|---|---|
| `src/client/shared/.../notificationsBootstrap.ts:62-100` | Bootstrap + the logged 401 warning; poll-fallback activation |
| `Spaarke.Notifications/src/negotiate.ts:60-143` | BFF negotiate returns 200 → SDK connects to `info.url` via `accessTokenFactory` |
| `src/server/api/Sprk.Bff.Api/Services/Notifications/SignalRDeliveryService.cs:44,207-247` | Serverless, **no hosted hub**, transient transport, local token mint |
| `src/server/api/Sprk.Bff.Api/Api/Notifications/NotificationsEndpoints.cs:37-105` | REST negotiate returns `{url, token}` under `RequireAuthorization()` (returns 200) |
| `pollFallback.ts` | Working REST fallback — isolates the fault to the **hub-token** path only |
| `.../Services/Notifications/SignalRDeliveryOptions.cs` (`SectionName = "Notifications:SignalR"`) | Config binding; connection string is a Key Vault reference (ADR-027 per-customer SignalR) |

## Recommended remediation (OPS — UAT Azure SignalR resource, in priority order)

1. **Verify the access key matches.** Confirm `Notifications__SignalR__ConnectionString`'s `AccessKey=` equals the
   resource's **current** primary or secondary key. **A rotated-but-not-recopied key is the single most likely
   cause and produces exactly this "BFF 200 + service 401".** Re-copy the current key into the Key Vault secret
   the BFF reads.
2. **Confirm Serverless service mode.** The transient-transport **client-negotiate** requires the Azure SignalR
   resource to be in **Serverless** mode. If it's in Default/Classic mode, the client negotiate token is rejected.
3. **Confirm the endpoint targets the correct UAT resource.** `Endpoint=` in the connection string must point at
   the intended UAT SignalR resource — the minted token's **audience embeds the endpoint host**, so a
   wrong/renamed endpoint yields a 401.

After any change: restart the BFF App Service (to re-read the Key Vault secret) and re-test — the SDK negotiate
should return 200 and real-time push should connect (the poll-fallback warning disappears).

## Diagnostic already in place (from compose-r7, hardening only)

`SignalRDeliveryService.NegotiateAsync` logs, at **Information** level, the **resolved endpoint host** (never the
key or token). Check the BFF App Service logs to confirm which SignalR endpoint the BFF is actually targeting vs.
the intended UAT resource — this makes the drift diagnosable server-side instead of only as a client 401.

## Config keys (for the environment owner)

- `Notifications__SignalR__ConnectionString` — Azure SignalR **Serverless** connection string
  (`Endpoint=…;AccessKey=…;Version=1.0;`), stored as a **Key Vault** reference (ADR-027: SignalR is provisioned
  per-customer). Bound from section `Notifications:SignalR` (`SignalRDeliveryOptions.SectionName`).
- The ADR-032 kill-switch: when SignalR is not configured/enabled, delivery falls back cleanly (no crash) — which
  is the current degraded-but-functional state via REST polling.

## Ownership / handoff

- **This is not compose-r7 or compose-r8 work** (r8 = Compose render-on-save fidelity + durable files; unrelated).
- **Immediate action = ops**: remediate the UAT Azure SignalR resource per the steps above.
- **Code owner if a code change is ever required**: `spaarke-notification-spine-r1` (owns
  `Services/Notifications/SignalRDeliveryService.cs` + `SignalRDeliveryOptions.cs`). No code change is expected —
  the fix is environment configuration.
