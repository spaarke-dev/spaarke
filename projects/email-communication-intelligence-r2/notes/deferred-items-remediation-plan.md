# Remediation plan — complete the 016/024 open items (NOT deferred, NOT r5-dependent)

> **Owner directive (2026-08-06):** do not defer work that can be done now (deferral → lost/buried work); and
> **`compose-r5` is CLOSED** — its code is merged to master, so "coordinate with r5 / r5 owns it" is NOT a valid
> reason to defer. These three items are OURS to implement directly. Schedule as the next session's first tasks.

These are **must-do follow-on tasks**, not GitHub-deferred issues. Each is small now that the substrate exists.

---

## R-1 — 016: affinity confirmation-write hook (complete FR-A4's write side)

**State:** `AffinityStore.RecordConfirmationAsync` is built + unit-tested. Nothing calls it yet. The affinity
learning loop is READ-complete but never accumulates rows.

**Why r5 is not a blocker:** the association confirm/change surface (`EmailConnectionsReview` /
`applyRegardingSelection`) is **merged into master** (r5 shipped) — it is repo code we edit directly.

**Investigation to confirm the seam (first step):**
1. Trace how a human-confirmed association is persisted today. Earlier recon said the review surface writes the
   regarding via client-side host-context `Xrm.WebApi` (bypassing the BFF). Re-verify against the MERGED code:
   grep `applyRegardingSelection` / `unlinkRegarding` in `src/client/shared/Spaarke.Communication.Components/**`
   and check whether it calls any BFF endpoint or writes `sprk_regarding*` + `sprk_associationstatus` directly.

**Implementation options (pick per the investigation):**
- **(A) BFF endpoint (preferred if a server round-trip is acceptable):** add
  `POST /api/communications/{id}/confirm-association` (caller-impersonated, ADR-024 write via `RegardingFieldMap`)
  that (i) writes the confirmed regarding + status, (ii) calls
  `AffinityStore.RecordConfirmationAsync` for each signal `AffinityRung.ExtractSignals(envelope)` produces.
  Then point the merged review surface's confirm action at it (replacing/augmenting the client Xrm.WebApi write).
- **(B) Dataverse plugin (if the confirm must stay client-side Xrm.WebApi):** a thin plugin on `sprk_communication`
  Update that fires when `sprk_associationstatus` transitions to Resolved via user edit (distinguish from the
  engine's auto-file write) and records affinity. Heavier (plugin deploy, ADR-002 thin-plugin limits) — prefer (A).
- Reuse the PUBLIC `AffinityRung.ExtractSignals` for read/write canonicalization parity (already exposed for this).

**Acceptance:** confirming an association N times from sender S → record R makes a later untagged email from S
surface R (the existing `AffinityRungTests` surfacing test, now driven by real accumulated rows). Best-effort
write (never fails the confirmation).

---

## R-2 — 024: Compose-path content-dedup hook (complete FR-C3 coverage)

**State:** `ContentDedupDetector` built + DI-registered; email-attachment path hooked + tested. Compose path
(`ComposeService.PromoteIfEphemeralAsync`) not yet hooked.

**Why r5 is not a blocker:** `spaarkeai-compose-r5` / `-fidelity` are **closed** — `ComposeService` is merged code
we edit directly (a ctor change ripples only to the in-repo `ComposeServiceCreateOnSaveTests`, which we update).

**Implementation (small, ~12 lines + ctor + 1 test update):**
1. Inject `ContentDedupDetector` into `ComposeService` (ctor param + field); update the one direct construction
   site `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeServiceCreateOnSaveTests.cs` (pass a mock/no-op).
2. In `PromoteIfEphemeralAsync`, AFTER the `sprk_graphitemid_uk` idempotency check (~L2048) and BEFORE the create
   (~L2050): `var dedup = await _dedupDetector.ReconcileAsync(request.GraphDriveId, request.DocumentSpeId, <oid
   from httpContext.User>, effectiveFileName, ct);` — if `dedup.IsDuplicate` return a
   `PromoteComposeDocumentResult { DocumentRecordId = canonical, WasCreated = false }` (mirror the idempotent
   no-op branch); else add a `CanonicalHashAttribute = "sprk_canonicalhash"` const and
   `entity[CanonicalHashAttribute] = dedup.CanonicalHash` (when non-null) before `CreateAsync`.
3. Test: extend `ComposeServiceCreateOnSaveTests` (or a sibling) — duplicate → no `CreateAsync`, returns canonical;
   first → creates + stamps hash. `/conflict-check` before PR.

**Acceptance:** byte-identical content saved twice via Compose creates no second `sprk_document`.

---

## R-3 — 024: orphan transient-blob cleanup

**State:** on a duplicate hit the second SPE blob is uploaded (gate-after-write) but left unlinked (no
`sprk_document`). Accepted transiently by the owner decision, but leaving it forever is a slow SPE leak.

**Implementation:** when `ContentDedupDetector.ReconcileAsync` returns `IsDuplicate`, delete the duplicate drive
item via a `SpeFileStore` facade delete (add/read the app-only delete method; `DriveItemOperations` already has
delete plumbing). Best-effort/non-fatal — a failed cleanup logs, never fails the request. Add a detector test
asserting the delete is invoked on a duplicate. Consider a small guard so we never delete the CANONICAL item
(only the just-uploaded duplicate `itemId`).

**Acceptance:** after a duplicate is suppressed, the transient blob is removed (or a cleanup is enqueued); no
unreferenced SPE item accumulates per duplicate.

---

## Suggested sequencing (next session)
R-2 (Compose hook — smallest, substrate ready) → R-3 (orphan cleanup — same detector) → R-1 (affinity write —
needs the confirm-path investigation first). All three are FULL-rigor BFF tasks (code-review + adr-check at 9.5),
`/conflict-check` before each PR. None depend on any closed project's team — only on merged code.
