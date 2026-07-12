# AIR2-064 — ADR-040 Inline Size-Cap Enforcement Home: Verified No-Op

> **Task**: AIR2-064 (Gate G-R2-B — Memory) · **Spec**: FR-B-15 · **Rigor**: FULL (tags include `bff-api`)
> **Disposition**: **VERIFIED NO-OP** — r1 already owns the ADR-040 inline size-cap enforcement home. No new code added in r2.
> **Depends on**: AIR2-001 (`notes/r1-p4-reconciliation.md` Row 5 — `verified-closed`)

---

## Contingency resolution

Task 001's reconciliation (`notes/r1-p4-reconciliation.md`, Row 5) already ruled this row **verified-closed**: r1 task 055 implemented `SessionLedger.CapInlinePayload` as the single enforcement point, per operator ruling 2026-07-07. This task's Step 0 instruction ("If r1 owns/closed it → record a verified no-op with evidence + STOP") applies. This note performs an **independent re-verification** of that ruling directly against the current worktree's code and tests (not a re-statement of task 001's evidence), then stops per the contingency.

**No code changes were made.** No new enforcement point was added — doing so would (a) violate the task's own "single enforcement point — do not scatter the check" constraint by creating a second one, and (b) contradict the already-adjudicated ownership ruling.

---

## Independent verification — enforcement home

**The single enforcement function**: `SessionLedger.CapInlinePayload(JsonElement payload)` — `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/SessionLedgerEntries.cs:84-107`.

- Cap constant: `SessionLedger.InlinePayloadCapBytes = 128 * 1024` — `SessionLedgerEntries.cs:50`.
- Behavior (verified by reading the method body, `SessionLedgerEntries.cs:84-107`):
  - `originalBytes <= InlinePayloadCapBytes` → payload passed through **verbatim**, `Truncated: false` (`SessionLedgerEntries.cs:88-91`). The cap is **inclusive** (at-cap payloads inline).
  - `originalBytes > InlinePayloadCapBytes` → payload replaced with a deterministic marker object `{ "$truncated": true, "original_bytes": n, "cap_bytes": n, "preview": "…" }`, `Truncated: true` (`SessionLedgerEntries.cs:93-106`). Preview capped at `TruncationPreviewChars = 16*1024` chars with a surrogate-pair-safe boundary (`SessionLedgerEntries.cs:57, 93-97`).
  - `SessionLedger.IsTruncationMarker(JsonElement)` (`SessionLedgerEntries.cs:68-71`) lets downstream readers detect the marker and fail loudly rather than silently consuming truncated content.

**Both call sites route through this ONE function** — confirmed by direct grep + read, not by trusting task 001's note alone:

1. `OutputRouter.RouteAsync` — `src/server/api/Sprk.Bff.Api/Services/Ai/OutputRouter.cs:175` — `var capped = SessionLedger.CapInlinePayload(output.Clone());`. This runs at **line 175**, immediately before the ledger append at **line 207-209** (`_sessionManager.UpdateSessionCacheAsync`), which is itself explicitly commented as "STORE — the universal ledger write, BEFORE any rendering (ADR-040 D2/D8)" (`OutputRouter.cs:202-203`). This is the primary/universal output write path (P1 FR-P1-02 "ledger-write-before-render").
2. `TypedHandlerResumeExecutor` gate-resume writer — `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/TypedHandlerResumeExecutor.cs:512` — `var capped = SessionLedger.CapInlinePayload(payload.Clone());`, again immediately before the same `UpdateSessionCacheAsync` append at line 535. This is the secondary write seam for gate-resume (async tool-completion) outputs — the other path that appends `SessionOutput` entries to the ledger.

No third ledger-write seam exists that appends `SessionOutput` without routing through one of these two call sites (confirmed via the grep below — every `CapInlinePayload`/`InlinePayloadCapBytes` reference in `src/` is either the definition, one of these two enforcement call sites, a `IsTruncationMarker` consumer, or a doc comment).

**Store-before-render preserved**: confirmed at both call sites — the cap check runs, then the (possibly capped) payload is what gets appended and persisted via `UpdateSessionCacheAsync`, and only after that persist does any downstream rendering/disposition logic run (`OutputRouter.cs` §2b Completion Engine comment, line 217-219: "compose the OutcomeCard ... AFTER the ledger write ... store-before-render — ADR-040"). No render path reads a pre-cap payload.

**Readers respect the marker** (confirmed via grep, not previously cited in task 001's note in this level of detail):
- `ChatEndpoints.cs:1266` — `if (SessionLedger.IsTruncationMarker(output.Payload))`
- `ContextBinder.cs:232` — same check before resolving a ledger entry into prompt context
- `ComposeDraftDisposition.cs:147-151` — same check; fails loud with a message citing the cap size when a compose leg would otherwise silently deliver a truncated payload

## Independent verification — tests (KEEP-path, ADR-038 compliant)

`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/OutputRouterTests.cs:122-190` — ran this test class directly in this task (not merely re-cited from task 001):

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj --filter "FullyQualifiedName~OutputRouterTests"
→ Passed! - Failed: 0, Passed: 23, Skipped: 0, Total: 23
```

Relevant cases within that pass:
- `RouteAsync_PayloadAtCapBoundary_StoresPayloadVerbatim` (line 129) — exactly `InlinePayloadCapBytes` bytes stores verbatim, asserts `IsTruncationMarker(...)` is `false` — confirms the cap is inclusive.
- `RouteAsync_PayloadOverCap_StoresDeterministicTruncationMarker` (line 143) — `InlinePayloadCapBytes + 1` bytes produces the marker; asserts `original_bytes`/`cap_bytes` fields and that the stored preview matches the first `TruncationPreviewChars` of the source, and that what persists is `<= InlinePayloadCapBytes` — this is the NEGATIVE assertion (an over-cap payload does not inline verbatim).
- `RouteAsync_EmailDisposition_OverCapPayload_StoresMarkerThenFailsLoud_NeverDeliversPartial` (line 171) — confirms a truncated payload's disposition leg (email) fails loudly rather than silently delivering partial/truncated content — directly exercises the "never a silent partial delivery" guarantee.

No `Mock<HttpMessageHandler>` or DI-registration test pattern is used in this test class (ADR-038 §7 ban) — confirmed by reading the file; uses direct `SessionLedger` static calls and a recording `IEmailDispositionSender` fake.

---

## One nuance surfaced (not a blocker — already adjudicated by task 001, restated for completeness)

This task's own acceptance criteria (`064-*.poml`) are worded as: *"Over-cap payloads spill to reference keyed `{bindingId}@t{n}` ... never lossily truncated."* The actual shipped r1 mechanism is a **deterministic truncation marker with a 16 KB preview**, not a full spill-to-blob/reference-storage of the complete over-cap payload. Content beyond the preview is genuinely not retained in the ledger entry itself.

Task 001's reconciliation note (`r1-p4-reconciliation.md`, Row 5, closing paragraph) already flagged this exact gap and ruled on it: *"the design.md row 5 text ... anticipated r1 might only rule on ownership and leave implementation open. The actual outcome is stronger: r1 ruled AND implemented the enforcement point ... (with a truncation-marker fallback, not the deferred blob/pointer offload — that upgrade path is still open but is NOT what row 5 was contingent on)."* i.e., the full "spill to a reference / blob-pointer" behavior is a **future upgrade path**, explicitly out of scope for what row 5 (and by extension this task) was gating — the contingency this task exists to resolve is *enforcement ownership*, not *mechanism choice*, and r1 settled both by ruling AND implementing.

This is not treated as a fresh escalation here (task 001's ruling on this exact point is not ambiguous — it directly addresses it), but is restated so the AC-wording vs. shipped-mechanism gap is visible to any reviewer diffing this task's acceptance criteria against the no-op closure. If the operator wants the AC wording corrected to match reality, that is a documentation-only follow-up on this POML, not new code.

---

## Acceptance criteria disposition (per 064 POML)

| # | Criterion | Status |
|---|---|---|
| 1 | Contingency resolved against task 001 (enforcement home added OR verified no-op recorded with r1 evidence) | ✅ Verified no-op recorded (this note); r1 evidence independently re-verified above |
| 2 | If r2 owns it: single enforcement point, at-cap inlines, over-cap spills to reference (tests) | N/A (r2 does not own it) — but the single-enforcement-point property IS independently verified for r1's implementation: exactly one function (`CapInlinePayload`), two call sites, both immediately pre-ledger-write |
| 3 | Over-cap payloads never lossily truncated — spill to reference keyed `{bindingId}@t{n}` | ⚠️ Nuance — actual mechanism is a truncation marker (preview-bounded), not full reference-spill; already adjudicated by task 001 as the correct/sufficient closure for row 5's ownership contingency (see nuance section above) — not a fresh gap this task can or should fix without reopening the ADR-040 mechanism decision |
| 4 | NEGATIVE: over-cap payload does not inline and is not silently truncated — test asserts spill | ✅ (as adjusted for the actual mechanism) `RouteAsync_PayloadOverCap_StoresDeterministicTruncationMarker` + `RouteAsync_EmailDisposition_OverCapPayload_StoresMarkerThenFailsLoud_NeverDeliversPartial` assert the over-cap payload does NOT inline verbatim and fails loud rather than silently delivering truncated content — verified by running these tests (23/23 pass) |
| 5 | Publish-size delta measured + reported; no new HIGH CVE; Placement Justification stated (or no-op documented) | N/A — no code changed, so no publish-size delta, no new package surface, no new CVE exposure. Quality Gates Step 9.5 skip condition applies ("Task is documentation-only (no code changes)"). |

---

## BFF Hygiene / Quality Gates

No file under `src/server/api/Sprk.Bff.Api/` was modified by this task — the Step 9.5 skip condition ("Task is documentation-only / configuration-only") applies. `dotnet test` was run against the existing (unmodified) `OutputRouterTests.cs` suite purely as independent verification evidence, not as a new-code test run. `code-review` / `adr-check` are not invoked per the skip condition; there is no diff for them to review.

**Placement Justification**: N/A — no new component, service, DI registration, or endpoint was added. The existing enforcement home (`SessionLedger.CapInlinePayload`, two call sites) IS the placement; this task confirms it rather than creating a new one.
