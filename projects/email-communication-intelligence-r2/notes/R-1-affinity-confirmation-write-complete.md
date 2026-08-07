# R-1 — Affinity confirmation-write hook (FR-A4 write side) — COMPLETE (2026-08-06)

Rigor FULL · opus·high. Closes the FR-A4 affinity **learning loop**: `AffinityStore.RecordConfirmationAsync`
(built prior session, unit-tested, but never called) now accumulates rows from HUMAN confirmations, so the
deterministic `AffinityRung` (read side, already shipped) can start SUGGESTING records.

## Investigation → design decision

The confirm/change surface (`EmailConnectionsReview` → `ConnectionsWriteHandler.applyRegardingSelection`) writes
the regarding **client-side via `Xrm.WebApi`** (ADR-024 — deliberate, host-context), NOT the BFF. So the
remediation plan's "reroute the confirm to a BFF endpoint" (option A) would fight ADR-024. Correctness angle:
affinity must learn from **human confirmations only**, not the engine's deterministic auto-files (else it
self-reinforces) — which argues for triggering from the human confirm action, not a blanket DB plugin (option B,
also duplicates logic + strains ADR-002 thin-plugin limits).

**Robust design (owner: "both compose-r5 AND email-communication-solution-r5 are CLOSED — code on master, edit
directly; don't defer to r5"):** keep the client-side regarding write (ADR-024), and ADD a narrow BFF endpoint the
confirm surface calls **fire-and-forget** after the write succeeds. Server records affinity; client just signals.

## What shipped — BFF (self-contained, tested)

- **`POST /api/communications/{id}/confirm-affinity`** (`CommunicationEndpoints.cs`) — body
  `{ targetEntityType, targetRecordId }`; auth-scoped via `CommunicationAuthorizationFilter`; returns 200 with
  `{ recordedSignals }`. Thin handler → delegates to the recorder (codebase convention).
- **`AffinityConfirmationRecorder`** (`Services/Communication/Engine/`, concrete, singleton, ADR-010) —
  reconstructs the envelope via the `ICommunicationEnvelopeReader` test-seam, computes the SAME signals the read
  rung uses (`AffinityRung.ExtractSignals` — read/write canonicalization parity), and increments each
  (signal → target) via `AffinityStore.RecordConfirmationAsync`. Mirrors the read rung's guards exactly (unmapped
  target → no-op; affinity disabled for the tenant → no-op). **Best-effort/non-fatal (NFR-04): every failure
  returns 0 without throwing** — the endpoint must never fail the user's confirmation.
- **`RecordAffinityConfirmationRequest`/`Result`** DTOs (`Services/Communication/Models/`).
- DI: `AddSingleton<AffinityConfirmationRecorder>()` in `CommunicationModule` (unconditional).

## What shipped — client (host-agnostic, ADR-012)

- **`IResolverWriteContext.recordAffinity?`** (`ConnectionsWriteHandler.ts`) — optional host-injected
  fire-and-forget hook `(targetEntityType, targetRecordId) => void`.
- **`EmailConnectionsReview.confirmCandidate`** — fires `writeContext.recordAffinity?.(c.entity, c.targetId)`
  ONLY after a successful confirm (never on clear/unlink; never when the write failed). No-op when unwired.
- **`EmailWorkspace.tsx`** — wires `recordAffinity` on the review's `writeContext` to
  `authenticatedFetch('/communications/{id}/confirm-affinity', POST {target})` with `.catch(() => {})` — mirrors
  the existing archive POST (ADR-028). Best-effort; a failed learning signal never surfaces to the user.

## Tests
- **BFF**: `AffinityConfirmationRecorderTests` (6) — mapped-target records one row per signal; unmapped target
  short-circuits before the envelope round-trip; invalid guid / empty target / disabled tenant → no-op;
  reconstruction-throws → non-fatal 0. Real `AffinityStore` over a mocked `IGenericEntityService`, mocked
  `ICommunicationEnvelopeReader` (ADR-038 module boundaries).
- **Client**: `EmailAssociationsAndTracking.test.tsx` (+2) — fires `recordAffinity` with the confirmed target on
  success; does NOT fire when the confirm write fails. Full suite 18/18; `EmailWorkspace` suite 13/13.
- Build 0-err/0-warn; C# non-inbound Communication suite green; publish materially unchanged (code-only, no
  package delta); CVE clean.

## Placement Justification (§10) + §11
Endpoint added via the existing `Map{Feature}Endpoints` group (no `Program.cs` change); no new package; AI facade
N/A; tests updated. §11 (`AffinityConfirmationRecorder`): Existing — none (no human-confirmation→affinity
orchestration); Extension — not on `AffinityStore` (records ONE signal, no envelope knowledge) nor the read-only
rung; Cost-of-doing-nothing — the FR-A4 learning loop never accumulates rows, `AffinityRung` can never fire.

## Pre-existing issue surfaced (NOT R-1 — flagged, not buried)
The 4 `CommunicationIntegrationTests.InboundPipeline_*` tests FAIL on the branch **independently of R-1** (verified
by stashing R-1). They are **task-021 drift**: task 021 re-routed the inbound create from
`_genericEntityService.CreateAsync` to `CommunicationService.CreateCommunicationRaceProofAsync`, but these
integration tests still capture/assert the old `CreateAsync` path (`capturedEntity` null / `createCallCount` 0).
Being fixed as a follow-on in this session (see `notes/021-inbound-integration-test-drift-fix.md`).
