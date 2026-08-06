# Test diet report — spaarke-notification-spine-r1

**Run date**: 2026-07-22
**Branch**: work/spaarke-notification-spine-r1 (merged to master @ 153c825ec)
**Classifier**: ADR-038 §7 build-vs-maintain (17 bans B1–B17)
**Scope**: .NET test files added/modified during the project + the two client tests (noted separately — UI tests are outside ADR-038's stated .NET scope).

## Summary

| Class | Count (methods) | Action |
|---|---|---|
| **MAINTAIN** (KEEP at canonical path) | **72** (.NET) + 10 (client) | confirmed — no change |
| SCAFFOLDING (DELETE candidate) | **0** | — |
| AMBIGUOUS (reviewer judgment) | 0 (1 sharpen-note below) | — |
| PATH-VIOLATION (wrong KEEP path) | 0 (1 path-observation below) | — |
| **Total .NET test methods touched** | **72 across 14 files** | — |

**Result: nothing to delete or move.** The project's tests are all maintain-class — vertical-slice seam tests at the KEEP path plus the envelope contract-guard unit tests. This is the expected outcome of following `tests/CLAUDE.md` (integration-first) + the ADR-038 §7 seam-DoD for dispatch-spine changes.

## Delete commands
_None — no scaffolding-class tests found._

## Path-move commands
_None._

## Classification detail (.NET)

### MAINTAIN — vertical-slice seam tests (KEEP path `tests/integration/seam/**`, ADR-038 §7 E-40 dispatch-spine DoD)

| File | Methods | Why MAINTAIN |
|---|---|---|
| Notifications/OutboxServiceSeamTests.cs | 2 | Real `OutboxService` over an interpreting boundary — write/dismiss/read-time-expiry behavior. Mock=0. |
| Notifications/PendingPollFallbackSeamTests.cs | 6 | Real pending endpoint degrade path; oid-scoped, expiry-filtered. |
| Notifications/SignalRDeliverySeamTests.cs | 9 | Exercises the **real** `SignalRDeliveryService` (Mock=15 are boundary setups, not all-mocks-trivial). |
| Notifications/DailyBriefingSuggestionProducerSeamTests.cs | 5 | Real `OutboxService`; grounded/ungrounded/gated/cap + envelope shape (incl. `regardingRecordType`, task 052). |
| Communication/CommunicationArrivedProducerSeamTests.cs | 2 | Both channels → outbox+ping, outbox-before-ping ordering, non-fatal. |
| Communication/CommsAssessedProducerSeamTests.cs | 2 | Producer seam success + producer-throws-non-fatal. |
| Communication/CommunicationRuleGateSeamTests.cs | 5 | Policy gate branches + fallback + priority (real table read path). |
| Communication/RiActionsViaSeamSeamTests.cs | 2 | E2E authorize ordering seam→outbox→ping→mirror + deny zero-side-effect. |
| Communication/FanOutTargetingSecuritySeamTests.cs | 7 | **Negative-access** cases (R-5 compliance — a leak is an incident). Highest-value security seam. |
| Ai/DispositionRoutabilityNotificationSeamTests.cs | 3 | Notification admit⇔route⇔store (ADR-043 Path-C flip, task 033). |
| Ai/Nodes/CreateNotificationNodeExecutorSeamTests.cs | 8 | Characterization net for the task-031 Layer-A extraction (behavior-neutrality DoD). |
| Ai/Nodes/CreateTaskNodeExecutorSeamTests.cs | 3 | Same (031 DoD). |
| Ai/Nodes/UpdateRecordNodeExecutorSeamTests.cs | 3 | Same (031 DoD). |

All exercise real production types across an AI/notification convergence seam, doubling only module boundaries (Dataverse / SignalR / routing) per ADR-038 — a router-unit ≠ working slice. None match B1–B17.

### MAINTAIN — envelope contract-guard unit tests

| File | Methods | Why MAINTAIN |
|---|---|---|
| unit/Sprk.Bff.Api.Tests/Services/Notifications/EnvelopeSerializationTests.cs | 15 | Guards the **wire contract** the client mirrors: kebab-case `kind` round-trip + locked wire values, camelCase field names, **closed-taxonomy fail-closed** (unknown kind → JsonException, not silent default), `Validate()` kind enforcement, and the **closed field-list / forbidden-substring guard** (NFR-02/03 — no `body`/`content`/`token` field can slip into an envelope). Each protects a concrete regression a real change would trip. |

**Path observation (not a violation):** this file sits at `tests/unit/Sprk.Bff.Api.Tests/Services/Notifications/`, the established BFF-unit-test location, not the ADR-038 aspirational `tests/unit/domain/**`. This is the whole BFF suite's convention (thousands of tests), not a project-specific drift — no `git mv` recommended.

**Sharpen-note (not a delete):** `SuggestionEnvelope_PublicProperties_MatchClosedFieldListExactly` / the Communication twin use PUBLIC-property reflection to assert the closed field set. This is a legitimate NFR-02/03 contract guard (adding a forbidden field fails the test), NOT B8 (which is NonPublic/`InternalsVisibleTo` reflection). Keep as-is.

## Client tests (outside ADR-038 .NET scope — noted, retained)

`tests/CLAUDE.md` states UI tests are out of scope for the .NET directive; the 17 bans are .NET-oriented. For completeness these two are **retained** as regression anchors:

| File | Methods | Protects |
|---|---|---|
| SpaarkeAi/…/__tests__/SuggestionCard.test.tsx | 8 | Renders-from-envelope, expired-not-rendered (pre-mount filter), re-fetch-before-act (call-order), stale-graceful, no-record-type→no-render, ADR-021 dark-mode. |
| Spaarke.UI.Components/…/xrmNavigationServiceAdapter.test.ts | 2 | The Layout-1 modal shape (`target:2`, 85%, entityrecord; `openForm` NOT used) + host-unavailable throw. |

## Count delta

- .NET test methods touched during project: **72**
- MAINTAIN: **72** · SCAFFOLDING: **0** · AMBIGUOUS: **0** · PATH-VIOLATION: **0**
- Net post-diet expected count: **72** (no reduction — nothing to remove)

## Industry citation

Build-vs-maintain per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes; DHH less-tests). Classifier B1–B17. This project's suite is maintain-class by construction — the seam tests ARE the dispatch-spine DoD, and the envelope tests anchor the NFR-02/03 wire contract.
