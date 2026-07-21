# Task 020 — SignalR Delivery Service + Authenticated Negotiate (Layer C)

> Status: COMPLETE · Mode: Serverless (`Microsoft.Azure.SignalR.Management` 1.33.1) per FR-01 spike · 2026-07-21

## Placement Justification (CLAUDE.md §10 — for the PR)

The Layer-C SignalR delivery service (`SignalRDeliveryService`) and the `POST /api/notifications/negotiate`
endpoint STAY IN THE BFF. The spine is the sole real-time policy/token point per spec §8: the negotiate
endpoint derives the caller's identity from the validated JWT and mints a per-user-scoped SignalR token
server-side, and the delivery leg is the ONE platform push channel (no per-consumer hub — root CLAUDE.md
"one spine"). Both are BFF-native concerns (authenticated Minimal API + server-held connection-string
secret) with no place outside it. FR-01 spike confirmed no publish-size breach (Serverless max 47.38 MB,
~12.6 MB under the 60 MB ceiling), so the >60 MB out-of-BFF escalation did not fire.

Three-question reuse check (§11): (1) Existing — zero SignalR/live-push transport in the repo. (2) Extension
— nothing extendable into a push channel without becoming this service. (3) Cost-of-doing-nothing — no live
push; messaging-r3 badges (task 045) + Daily-Briefing cards wait for the next poll, and each consumer would
re-fork its own channel (the forked-consumer failure §11 forbids).

## Hot-path / hygiene results

- Build (Release): 0 errors.
- CVE (`dotnet list package --vulnerable --include-transitive`): 0 NEW HIGH. Only the pre-existing baseline
  finding `System.Security.Cryptography.Xml 8.0.3` (4 HIGH advisories, no fix version) — NOT introduced by
  SignalR; no SignalR package appears in the vulnerable list. (Handoff: that Xml pin is worth a separate
  defer ticket per the spike, out of scope here.)
- Publish size: **47.39 MB incl-PDB** (4 PDBs; `Compress-Archive -CompressionLevel Optimal`, the NFR-01
  convention). Delta **+0.01 MB** vs the spike's Serverless baseline (47.38) — noise, expected, since only
  `.cs` was added (the package was already counted by the spike). Well under the 55 MB review band.

## Design decisions

- **Serverless, no hosted hub.** Management SDK `ServiceManagerBuilder` → `ServiceHubContext` (transient
  transport), built lazily on first ping/negotiate — no boot-time service connection.
- **Write-before-ping is STRUCTURAL.** `PingUserAsync(Guid outboxRowId, string userOid, NotificationKind, ct)`
  / `PingGroupAsync(Guid outboxRowId, string groupName, …)` require `outboxRowId` (a value that only exists
  post-outbox-write) and throw `ArgumentException` on `Guid.Empty` — a producer cannot ping without writing.
- **Signal-only payload.** `NotificationSignal(Guid OutboxRowId, NotificationKind Kind)` — exactly two routing
  fields (pinned by a reflection test). No body/snippet/content/action-token (NFR-02/03). Envelope shape stays
  in task 013.
- **Group primitive only.** `PingGroupAsync` is a `Clients.Group(...)` primitive; task 023 owns fan-out logic.
- **Negotiate derives oid server-side.** Reads the JWT `oid` claim (with the objectidentifier fallback used
  across the BFF); accepts NO body/query param → a spoofed target-user field cannot bind and is ignored.
  `.RequireAuthorization()` → unauthenticated = 401.
- **ADR-032 Null-Object (concrete-class-subclass, preferred).** `SignalRDeliveryService` is unsealed with a
  `protected` logger-only ctor + `virtual` entrypoints; `NullSignalRDeliveryService` subclasses it. NO interface
  introduced solely for the null-object. Ping = P2 quiet no-op (`Task.CompletedTask`); negotiate = P3 fail-fast
  (`FeatureDisabledException("notifications.signalr.disabled")` → 503) so clients fall back to poll (FR-06).
- **Unconditional registration + mapping.** `AddNotificationsModule` registers `SignalRDeliveryService`
  unconditionally (real when `Notifications:SignalR` configured, Null-Object otherwise); `MapNotificationsEndpoints`
  is mapped unconditionally in `MapSpaarkeEndpoints`. So the endpoint handler's service param always resolves →
  minimal-API metadata-gen succeeds at startup with SignalR OFF (ADR-032; §F.1 scan = symmetric, no asymmetric
  registration).

## Deviations

1. `CreateHubContextAsync` — used the 2-arg `(hubName, CancellationToken)` overload (the documented Serverless
   surface); the 3-arg `(string, ILoggerFactory, CancellationToken)` in the XML docs is not accessible on the
   built `IServiceManager` instance. Directional step-mode.
2. Seam test scenario (a) "reaches a connected client" — real Serverless delivery needs a PROVISIONED Azure
   SignalR resource (not in local/CI). Per ADR-038 (no `Mock<HttpMessageHandler>`), it is proven against a
   doubled `IServiceHubContext` MODULE BOUNDARY (real service logic; SDK network glue is the only thing not
   exercised). A true end-to-end variant is gated on `SPAARKE_SIGNALR_TEST_CONNSTRING` (documented no-op skip
   when absent). Scenario (b) Null-Object degrade runs fully live locally.
3. Endpoint-level auth behaviors (401 / oid-server-side-derivation / spoofed-target-ignored) are guaranteed by
   construction (`.RequireAuthorization()` + oid read only from `context.User`, no target param). A full
   WebApplicationFactory contract test was not added (out of task scope; would need booting the BFF with test
   config) — candidate follow-up.

## HUMAN-ATTENTION / runtime provisioning

- **Azure SignalR resource + connection string required at runtime (ADR-027).** Set `Notifications:SignalR:ConnectionString`
  (Key Vault reference, ADR-028 — never plaintext) to activate real delivery; absent ⇒ Null-Object (poll only).
- **Producer identity contract (task 024+):** `PingUserAsync` targets by **Azure AD oid** (matching what
  negotiate registers), NOT Dataverse systemuserid (the outbox `OwnerId`). Producers must resolve the recipient
  systemuserid→oid before pinging. Called out for task 024.
- **CSP (from the spike):** verify `wss://*.service.signalr.net` in the target Power Platform environment CSP
  before relying on live push, else clients silently fall back to poll.
