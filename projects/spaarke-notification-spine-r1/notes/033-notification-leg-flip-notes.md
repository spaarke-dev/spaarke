# Task 033 — Notification Leg Flip (FR-14): Notes

> **Status**: ✅ Completed 2026-07-21. `DispositionRoutability.Notification` flipped `Routable=false→true` and the `OutputRouter` routing leg added in the SAME change (ADR-043 Path C). Gated on the 032 audit (signed off). Both quality gates clean; full BFF suite green.

## What shipped

| Artifact | Change |
|---|---|
| `Services/Ai/DispositionRoutability.cs` | `Notification` entry `Routable=false→true`, `NotRoutableReason` removed. Now admissible on the dispatch path (ADR-043 §3: admission = routability). Comment cites the 032 audit. |
| `Services/Ai/OutputRouter.cs` | (1) ctor gains `IActionSeam? actionSeam = null` (last, optional — DI auto-injects the unconditionally-registered Singleton; bare test construction still compiles). (2) `case BindingDisposition.Notification:` → `CreateNotificationViaSeamAsync`. (3) `CreateNotificationViaSeamAsync`: validates the `notification` envelope STRUCTURE, maps → `CreateNotificationRequest`, calls `IActionSeam.CreateNotificationAsync`, throws loud on missing seam / missing envelope / `!Success`; `Skipped` (idempotency duplicate) is a successful no-op. (4) two doc comments updated (overlay/record/notification → overlay/record). |
| `tests/integration/seam/Ai/DispositionRoutabilityNotificationSeamTests.cs` (NEW, 3 tests) | admit⇔route⇔store happy path (asserts the seam is called with the parsed title/body/recipientId/category/actionUrl/priority/toastType/regarding + `Source="dispatch"` + `CorrelationId==ledgerKey`, and the ledger entry disposition=="notification"); seam-rejects-content → loud-after-store; missing-envelope → loud-before-seam. |
| `tests/integration/seam/Ai/DispositionRoutabilitySeamTests.cs` | Removed `Notification` from the not-yet-routable `[Theory]`; added it to `Registry_RoutableSet_IsExactlyTheRealizedLegs` (now six realized legs). |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/OutputRouterTests.cs` | Removed `Notification` from `RouteAsync_..._ThrowsLoudNotSupported` `[Theory]` (it now routes, no longer throws NotSupported). |

## Acceptance — all 8 criteria met

1. ✅ `DispositionRoutability.For(Notification)` → `Routable == true`, `NotRoutableReason == null`.
2. ✅ `OutputRouter.RouteAsync` with `Disposition=Notification` does NOT hit the `default:` drift-guard (`ArgumentOutOfRangeException`) — a real switch leg handles it (seam test green).
3. ✅ Valid Notification dispatch → `IActionSeam.CreateNotificationAsync` called, appnotification created with the stored-payload fields — verified by a **seam test**, not a mock-only unit.
4. ✅ Malformed payload (seam-rejected content OR missing envelope) → loud failure AFTER the ledger write — mirrors the Email leg's contract (store precedes the throw; entry stays addressable).
5. ✅ Every other disposition unchanged — full BFF suite **8781/0**; dispatch contract tests 81/0.
6. ✅ 032 audit's sequencing recommendation followed — "immediate flip, no remediation" (zero Notification-disposition Bindings to remediate). Audit signed off before the flip.
7. ✅ No new Layer-B outbox `kind` value and no new outbox row — the leg calls `IActionSeam.CreateNotificationAsync` (the durable `appnotification` write), not the outbox. FR-10 taxonomy unchanged.
8. ✅ Publish **46.06 MB compressed incl-PDB** ≤60 MB (~0 delta vs 031's 46.05; no package added); **0 new HIGH CVE** (`System.Security.Cryptography.Xml 8.0.3` is pre-existing — identical vulnerable set to HEAD); Placement Justification stated (existing-leg realization, facade-consumed).

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api` (Debug): 0 errors.
- Targeted: 46 disposition/router tests + 81 dispatch-contract/ActionSeam/NotificationService tests — all green.
- **Full BFF suite: 8781 passed / 0 failed** (101 pre-existing skips) — behavior neutrality across the system incl. the real DI container (`WebApplicationFactory<Program>`), confirming the Scoped `OutputRouter` → Singleton `IActionSeam` injection has no captive dependency.
- Step 9.5: code-review CLEAN (0 Critical / 0 Warning / 1 informational — OutputRouter.cs 523 lines, cohesive Email-mirror leg, don't split); adr-check 0 violations.

## Design decisions

- **`Source = "dispatch"`** on the router-originated notification (vs the node executor's `"playbook"`) — distinguishes chat/dispatch-triggered notifications in `appnotification.sprk_source`. **`CorrelationId = entry.Key`** ties the appnotification to the ledger key (mirrors the Email leg's `CorrelationId`).
- **Division of labor mirrors `DeliverEmailAsync`**: the router validates the `notification` envelope STRUCTURE (present + object); the seam validates CONTENT (title/body/recipientId required). Both failure modes are loud. Not duplicated.
- **Optional ctor param, not required**: `IActionSeam?` mirrors `emailSender`/`workProductPersister` — DI injects the real Singleton in production; bare test harnesses that don't exercise the notification leg keep compiling. No DI-registration edit needed (single ctor; container resolves the optional param from the registered Singleton).
- **Escalation triggers did NOT fire**: no unanticipated capability lit up (032 found zero Bindings); the leg realized via the existing durable `appnotification` write (no new Layer-B `kind`, no SignalR push) — squarely within FR-10 + "realize the Layer-A leg through the registry".

## For downstream (Phase 4/5)

Notification is now a first-class routable disposition. A future `sprk_playbookconsumer` Binding authored with `disposition=notification` will emit an `appnotification` from the chat/dispatch surface with **no further registry gate** (the registry is all-or-nothing per ADR-043 §3). Per the 032 audit's forward-looking guard: **authoring the first such Binding is the moment to re-run the NFR-02/03 content/privacy check** on its rendered title/body/actionUrl. The `admit⇔route⇔store` seam test is the standing structural guard.
