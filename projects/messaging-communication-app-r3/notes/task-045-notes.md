# Task 045 (FR-22) — New-communication awareness: emit + consume `communication-arrived`

> Status: UNBLOCKED 2026-07-22 (notification spine landed on master). P1 gate PASSED — spine
> contract confirmed in-tree (no invented API; no degrade needed).

## STEP 1 — Confirmed notification-spine contract (evidence-cited)

The spine primitives are ALL present in this worktree. FR-22's gap is the **emit (producer)**, plus the
**client consume**. Nothing invented.

### (a) Kind + envelope — EXISTS
- `NotificationKind.CommunicationArrived` — ACTIVE kind, wire value `"communication-arrived"`
  (`src/server/api/Sprk.Bff.Api/Services/Notifications/Envelopes/NotificationKind.cs`; fail-closed
  `NotificationKindJsonConverter`).
- `CommunicationEnvelope` (9 fields, reflection-locked) —
  `Services/Notifications/Envelopes/CommunicationEnvelope.cs`. Carries IDs + minimal DISPLAY metadata
  only; `Snippet` is the ONLY optional content field and is populated by the producer ONLY for
  non-private/non-privileged records (NFR-02/03). `.Validate()` rejects a non-communication kind.
- Client mirror: `src/client/shared/Spaarke.Notifications/src/types.ts` (`CommunicationEnvelope`,
  `NotificationEvent`, `NotificationKind`).

### (b) EMIT — the GAP (NOT yet wired)
- `grep CommunicationArrived|PingUserAsync|GetEligibleRecipientsAsync` over `src/**/*.cs` returns only
  the DEFINITIONS + the fan-out service + tests. **No production code composes them to emit.** So
  task 045 adds the producer.
- Sanctioned producer building blocks (reuse — no parallel mechanism):
  - `OutboxService.WriteAsync<TEnvelope>(userId, kind, envelope, regardingRecordId?, regardingRecordType?, expiresAt?, ct)`
    → durable outbox row id (Layer B; registered UNCONDITIONALLY in `AnalysisServicesModule` B1).
  - `SignalRDeliveryService.PingUserAsync(outboxRowId, recipientSystemUserId, kind, ct)` — best-effort,
    at-most-once, **write-before-ping is structural** (empty `outboxRowId` throws). Null-Object no-op
    when SignalR unconfigured (poll fallback carries truth).
  - `CommunicationFanOutTargetingService.GetEligibleRecipientsAsync(message, thread, ct)` → deduped
    eligible `systemuserid`s. Its own XML doc states the intended producer: *"The producer (task 024)
    loops the returned ids into `SignalRDeliveryService.PingUserAsync`."* — that producer is what 045
    adds. ZERO new access logic (composes existing access filter + private-thread grant + junction).

### (c) Consumer API (client) — EXISTS
- `@spaarke/notifications` `NotificationsClient` (`src/client/shared/Spaarke.Notifications`):
  `registerHandler(kind, cb) => unregister`, `start()` (negotiate SignalR + auto poll-fallback on
  failure), `stop()`. Live push is **signal-only** (`NotificationEvent.envelope` is `undefined` on
  `source:'live'`); poll path carries the envelope. Negotiate/poll go through `@spaarke/auth`
  `authenticatedFetch` (ADR-028); the SignalR transport socket is the ONE enumerated D-AUTH-7 raw-fetch
  exception (inside the lib, not our code).

### (d) Fan-out targeting — EXISTS (`CommunicationFanOutTargetingService`)
- Candidate set = the message's `sprk_communicationparticipant` junction only (ADR-048); narrowed by
  internal-only filter + private-thread grant (both fail-closed). Requires the caller to project
  `sprk_isinternalonly` (+ `createdon`) on the message and `sprk_privacystate` on the thread.

## STEP 2 — Trigger decision (resolves the spec Unresolved Question "on capture vs on send")

**Chosen: on CAPTURE (inbound arrival).** FR-22 awareness = "a NEW communication ARRIVES → badge/toast";
the inbound-capture points are exactly where a message a recipient did not send first appears. Wired at
BOTH inbound capture paths for channel symmetry (ADR-045):
- Email inbound: `IncomingCommunicationProcessor` (after participant-index + thread-resolve).
- Chat inbound: `MessagingIngestor` (after participant-index + thread-resolve).

On-SEND is intentionally NOT wired (sender already knows; and `CommunicationService` is the highest
cross-project conflict surface). The producer is direction-agnostic, so a future task can call
`NotifyArrivalAsync` from the send path with ZERO producer change (additive extension).

## Placement Justification (root CLAUDE.md §10 / §11 + bff-extensions.md)

- **Belongs in BFF?** YES. Latency-coupled to the persist request lifecycle; composes BFF-managed
  Dataverse state + the in-process spine. Not event-driven-out-of-band → not a Function.
- **New component justification (§11):** the producer `CommunicationArrivalNotifier` is NEW but
  REUSES the three existing spine primitives (fan-out + outbox + ping) — it adds ZERO new access,
  outbox, or delivery mechanism. Existing overlap: none (grep-confirmed no emitter). Extension: N/A
  (no existing emitter to extend). Cost-of-doing-nothing: FR-22 fails — SC-10 (badge/toast) never
  fires. Home = `Services/Communication/` (ADR-010 feature module) because its inputs are all
  Communication-flavored (matches where `CommunicationFanOutTargetingService` already lives).
- **Config boundary (§G):** no new Dataverse config columns; reuses existing outbox table + envelope.
- **Publish size:** no new NuGet references → ≈0 MB delta. Verified below.
- **Snippet = null always:** the producer never populates the optional `Snippet` (fail-closed) — the
  awareness signal carries IDs + sender display + badgeDelta only. Live push is signal-only anyway.

## STEP 3 — Client consume (NFR-03 awareness-only)

- `CommunicationsWorkspaceWidget` (the host-layer seam that already imports `@spaarke/auth`) gains an
  `useCommunicationArrivals` hook: registers `communication-arrived` → increments an **unread
  CounterBadge** + raises a **Fluent v9 Toast**. It NEVER fetches message bodies from the signal.
- Content KEEPS its own ~5s polling UNCHANGED: `ConversationView` polls every `pollIntervalMs` (default
  **5000 ms**, NFR-07 — `ConversationView.types.ts` L85). The awareness path is fully independent of the
  content-poll path (proven by the hook test: signal → badge/toast while a parallel poller keeps
  ticking and the signal handler performs no fetch).

## Tests
- BFF seam: `tests/integration/seam/Communication/CommunicationArrivalNotifierSeamTests.cs` — REAL
  `OutboxService` + REAL `CommunicationFanOutTargetingService` (Dataverse boundary doubled) + recording
  `SignalRDeliveryService` double; proves emit writes outbox-before-ping to the eligible recipients and
  the envelope carries no message body (NFR-02/03).
- Client jest: `useCommunicationArrivals.test.tsx` — a consumed `communication-arrived` raises
  badge+toast WHILE a parallel content-poller keeps polling and the signal handler fetches nothing.

## Verification (STEP 5/6 — DONE)
- BFF `dotnet build -c Release`: **0 errors** (22 warnings, all pre-existing in other files).
- BFF seam test `CommunicationArrivalNotifierSeamTests`: **5/5 pass**.
- BFF Communication + Notifications suite: **742 pass / 0 fail / 8 skipped** (no regression from the two
  optional-ctor-param additions).
- Publish compressed: **47.46 MB incl PDBs / 46.64 MB excl** (ceiling 60 MB; baseline ~46–47.5 MB;
  delta ≈0 — no new packages).
- `dotnet list package --vulnerable --include-transitive`: **0 NEW HIGH** (task 045 adds no packages).
  Pre-existing transitive HIGH `System.Security.Cryptography.Xml 8.0.3` is unchanged by this task.
- Client scoped `tsc --noEmit`: **0 errors**. Client `prettier --check`: **clean**.
- Client jest: `useCommunicationArrivals.test.tsx` **3/3 pass** (badge/toast on arrival; content polls
  independently; reset). Widget file `CommunicationsWorkspaceWidget.test.ts`: 3/4 pass — the 1 failure
  (`getByPlaceholderText('Filter threads')`) is **PRE-EXISTING** (fails identically on baseline without
  this task's change; the shared `ThreadList` removed that text per task 062 UAT §B4-6). Out of scope
  for task 045; not a regression.

## Files
- BFF producer: `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationArrivalNotifier.cs` (new)
- DI: `Infrastructure/DI/CommunicationModule.cs` (register notifier)
- Emit wiring: `IncomingCommunicationProcessor.cs` (email) + `Channels/MessagingIngestor.cs` (chat)
- BFF test: `tests/integration/seam/Communication/CommunicationArrivalNotifierSeamTests.cs` (new)
- Client hook: `.../CommunicationsWorkspaceWidget/useCommunicationArrivals.ts` (+ `.test.tsx`) (new)
- Client factory: `.../CommunicationsWorkspaceWidget/createNotificationsClient.ts` (new)
- Widget: `.../CommunicationsWorkspaceWidget/CommunicationsWorkspaceWidget.tsx` (badge + toaster + hook)
- Type shim: `.../src/types/spaarke-notifications.d.ts` (force-tracked past `.gitignore`)
- jest stub: `.../src/__mocks__/notifications.ts`; jest.config.cjs mapper; tsconfig.json paths;
  package.json (`@spaarke/notifications` dep + `@microsoft/signalr` peer)
