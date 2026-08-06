# Task 042 — RI Actions via Layer-A Seam + Outbox + Ping + Appnotification Mirror (FR-13): Implementation Notes

> **Status**: ✅ Completed 2026-07-22. The Phase-4 convergence point — an AUTHORIZED assessed communication (task 041 gate) now executes an observable RI action end-to-end. FULL rigor (opus/high). Full BFF suite green. Both Step 9.5 gates CLEAN.

## What shipped

| Artifact | Change |
|---|---|
| `Services/Communication/CommunicationRiActionService.cs` (NEW) | The RI action orchestrator. On authorize it CONVERGES four existing seams in order: (1) Layer-A `IActionSeam.CreateTaskAsync` (task 031, ADR-013) creates the domain record — a follow-up `task` regarding the communication, owned by the recipient — never a direct write; (2) `OutboxService.WriteAsync` (task 012) writes a `kind=communication-assessed` row FIRST; (3) `SignalRDeliveryService.PingUserAsync` (task 020) best-effort pings AFTER the outbox write; (4) `NotificationService.CreateNotificationAsync` mirrors to `appnotification` (the ONE platform writer) for Daily-Briefing visibility. Whole path is non-fatal (NFR-05, outer try/catch-log-swallow). Concrete singleton (ADR-010). |
| `Services/Communication/CommunicationRuleGate.cs` (`RuleGatedAssessedConsumer`) | Gained a `CommunicationRiActionService` ctor dep. The authorize branch (was log-only "deferred to 042") now calls `_riAction.ExecuteAuthorizedActionsAsync(signal, decision, ct)`. The DENY path is unchanged — nothing below the `if (decision.Authorize)` runs, so no seam call/outbox/ping/appnotification (structural short-circuit). |
| `Infrastructure/DI/CommunicationModule.cs` | `AddSingleton<CommunicationRiActionService>()` registered BEFORE `RuleGatedAssessedConsumer` (which now depends on it). All deps are singletons (IActionSeam + NotificationService from AnalysisServicesModule; OutboxService + SignalRDeliveryService from NotificationsModule; IGenericEntityService) → no captive scope. |
| `tests/integration/seam/Communication/RiActionsViaSeamSeamTests.cs` (NEW, 2 tests) | E2E vertical-slice seam from the public `RuleGatedAssessedConsumer.PublishAsync` seam entry through the REAL composed chain (gate → RI service → real `ActionSeam` → real `OutboxService` → real `NotificationService`), doubling only the Dataverse (`IGenericEntityService`) + SignalR boundaries. Authorize test asserts the full ordered chain `seam-task-create → outbox-write → ping → appnotification-create` + envelope IDs-only shape; deny test asserts zero side effects. |

## Design decisions (documented)

### Seam action = a `task`; mirror = the appnotification (avoids double-appnotification)
The Layer-A seam's `CreateNotificationAsync` ALSO writes `appnotification` (via the shared node core). Using it for the domain action AND `NotificationService.CreateNotificationAsync` for the mirror would write TWO appnotifications and create a second write path. So the domain action is a **`task`** (`IActionSeam.CreateTaskAsync`) — a distinct record — and the visibility mirror is the single `NotificationService` call. Clean separation; one appnotification per authorized assessment.

### Recipient = the assessed communication's owner (`sprk_communication.ownerid`)
`CommunicationAssessedSignal` carries no recipient. This proving producer targets the communication owner (the responsible user) for the RI task + the Daily-Briefing notification. This is deliberate proving-scope — a richer matter-team fan-out (reusing task 023 `CommunicationFanOutTargetingService`, as the `communication-arrived` path does) is a **future enhancement**, out of scope for this convergence task (POML: "do not build new infrastructure"). **⚠ Product note surfaced to owner**: for system-captured inbound email, `ownerid` may be the app/service user rather than a human — recipient-derivation quality is the natural next enhancement (candidate `/defer` if the product wants matter-team targeting).

### Confidence still deny-by-default until real plumbing lands
Per task 041's documented boundary, `CommunicationAssessedSignal.Confidence` still defaults to 0 (enrichment computes no RI-confidence score yet), so in production the gate denies until a real assessment confidence is plumbed into the signal. That plumbing remains a downstream concern; **the authorize path is fully exercised by the seam test** (explicit confidences through the real chain), so the convergence is proven and ready the moment a real confidence flows.

### Ordering invariant + escalation guard (did NOT fire)
The escalation trigger (bypassing the seam with a direct write, or writing the outbox before the seam succeeds) did **not** fire: the orchestrator awaits `CreateTaskAsync` (seam) → then `WriteAsync` (outbox) → then `PingUserAsync` (ping) → then the mirror, strictly in that order. Seam degraded-success (`TaskId == Guid.Empty`) is logged but does not abort the visibility surface (the assessment still surfaces); this matches the seam's own degraded-success contract.

## Acceptance — all 9 criteria met
1. ✅ Authorize → record created via the Layer-A seam (a `task`), never a direct write (seam test asserts `LogicalName=="task"` with regarding=communication, owner=recipient).
2. ✅ `kind=communication-assessed` outbox row written BEFORE the ping (ordering log `outbox-write` precedes `ping:{owner}`).
3. ✅ Ping is best-effort (RecordingDelivery; SignalR-off already a P2 no-op, ADR-032) — never rolls back the outbox row or seam record.
4. ✅ Envelope carries IDs + minimal display metadata + `regardingRecordId` only; `Snippet==null`, `SenderDisplay`=channel label (no body/address/token).
5. ✅ Appnotification created via an explicit `NotificationService.CreateNotificationAsync` call (owned by the recipient; visible via the Daily-Briefing read path).
6. ✅ Deny → NO seam call, NO outbox row, NO ping, NO appnotification (seam test asserts empty creates/pings/events).
7. ✅ `RiActionsViaSeamSeamTests.cs` proves both paths through real production types (only Dataverse + SignalR doubled).
8. ✅ Observable end-to-end: task + appnotification exist; the appnotification is the Daily-Briefing surface (Success Criterion 2).
9. ✅ Publish **46.10 MB incl-PDB** ≤60 (below the ~49.63 baseline; no package added → CVE set unchanged, 0 new HIGH); Placement Justification stated.

## Verification
- `dotnet build`: 0 errors. New seam tests: 2/2. Full BFF suite: **8862 passed / 0 failed / 101 skipped** (baseline 8860; +2, behavior-neutral — real DI container resolves the new singleton + the extended consumer ctor).
- Step 9.5: code-review CLEAN (0 Critical/Warning; 1 informational — the assessed communication is read twice per authorize, low-frequency, documented); adr-check CLEAN (ADR-013 facade, ADR-041/043 outbox-before-ping, ADR-039 no-FireAsync/no-second-path, ADR-010/032/015/024/038 all compliant).
- conflict-check: SOFT WARN — messaging-r3 (PR #664) shares the `Services/Communication/**` hot path but a disjoint file set; task 042's files (new orchestrator + new test + branch-only gate/DI edits) have ZERO overlap.

## For downstream
- **Phase 5 (050 suggestion producer)** reuses this exact pattern (grounded+gated → outbox → renderer). 050 consumes the same `OutboxService` + `SignalRDeliveryService` + `IActionSeam`; the suggestion envelope (`SuggestionEnvelope`, task 013) is the parallel to the CommunicationAssessed envelope used here.
- **Real assessment confidence** into `CommunicationAssessedSignal` is the one remaining plumbing to make authorize fire in production (adjacent to the enrichment assessment feature).
- **Recipient targeting**: owner-target is the proving choice; matter-team fan-out via task 023 is the future enhancement.
