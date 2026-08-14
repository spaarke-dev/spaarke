# Task 013 — TrackingTokenRung (read + verify the signed footer, FR-A1) — COMPLETE

> 2026-08-07. FULL rigor · opus tier @ effort high. Left dirty for the main session to commit.

## What shipped

- **NEW** `src/server/api/Sprk.Bff.Api/Services/Communication/Engine/Rungs/TrackingTokenRung.cs`
  — `IAssociationRung`, `Kind=RungKind.ExplicitReference`, `Order=0`. `EvaluateAsync`:
  - **Tier 1 (signed-valid → 1.0):** extracts token-shaped candidates (`base64url.base64url`, `{16,}` each
    side) from `BodyText` + `BodyHtml` (incl. quoted reply/forward history), calls
    `ITrackingTokenSigner.VerifyAsync` (the REAL async contract, not the POML's stale sync `TryVerify`), and on
    `IsValid && Payload != null` emits `RungMatch(RegardingFieldMap.FieldFor(payload.RecordType),
    EntityReference(payload.RecordType, payload.RecordId), 1.0, "tracking-token:signed:{type}",
    Rung=ExplicitReference)`. Verify-before-trust: a forged/tampered/malformed token → ignored (no match).
  - **Tier 2 (bare/edited → 0.65):** deterministically parses `{entityType} {guid}` references (the send-path
    disclosure fallback form) from the body; each maps via `RegardingFieldMap` → `0.65` corroborating match
    (`"tracking-token:bare:{type}"`). Sub-0.85 → never auto-files alone.
  - **Absent/deleted footer → `Array.Empty<RungMatch>()`**, no error. Best-effort/non-fatal (NFR-04): regex
    timeouts caught, defensive verify backstop, nothing throws into capture. Depends only on the signer +
    `RegardingFieldMap` (no Dataverse, no AI — ADR-013). Max-wins dedup by (field, target id); signed 1.0 beats
    bare 0.65 for the same record; a token quoted twice collapses to one match.
- **MOD** `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CommunicationModule.cs`
  — `services.AddSingleton<IAssociationRung, TrackingTokenRung>()` UNCONDITIONALLY beside the other rung-0
  rungs (after `RecipientAliasRung`, before `ThreadContinuityRung`), with a placement/why comment.
- **NEW** `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/TrackingTokenRungTests.cs`
  — 10 tests, modeled on `ExplicitReferenceRungTests.cs`, using an in-memory `FakeTrackingTokenSigner` real
  double (no `Mock<HttpMessageHandler>`/DI-registration/ctor-null — ADR-038). Covers all SIX acceptance cases:
  1. signed-valid → 1.0 signed match; **+** unmodified mapper auto-files it Resolved (core matter, kill-switch on).
  2. bare/edited (no valid sig) → 0.65 corroborating; **+** mapper does NOT auto-file 0.65 alone.
  3. deleted/absent footer → empty, no error.
  4. forged token → ignored (empty).
  5. conflicting-forward → valid token (matter A) + a second-rung (ThreadContinuity 0.90) matter B on the same
     field → the **unmodified mapper yields Ambiguous**.
  6. token only in quoted history → still extracted + verified.
  Plus two extras: token in HTML body (HtmlEncode leaves base64url intact); valid token for an unmapped
  `RecordType` → skipped (escalation-trigger #2 boundary).

## `AssociationStatusMapper.cs` — UNCHANGED (confirmed)

`RungKind.ExplicitReference` IS auto-file-eligible (`IsAutoFileEligible` returns true) and the mapper has a
two-distinct-targets-per-field conflict → Ambiguous path (`FieldWinner.EvaluateConflict`). Zero mapper change,
exactly as the spec's "reuse RungKind.ExplicitReference" note claims.

## POML-stale-API correction (as briefed)

The POML described `ITrackingTokenSigner.TryVerify(token, out payload) → bool` (sync). Task 010 actually
shipped `Task<TrackingTokenVerification> VerifyAsync(string? token, CancellationToken ct = default)` with
`record TrackingTokenVerification(bool IsValid, TrackingTokenPayload? Payload)` (+ `static readonly Invalid`)
and `record TrackingTokenPayload(string RecordType, Guid RecordId, string? TenantId, DateTimeOffset Issued)`.
Used the REAL async signature; verify-before-trust semantics unchanged (trust only when `IsValid`). Footer
format matched task 012: token wrapped in `{template}` with `{record-ref}` + `{signed-token}`, appended after
`<hr/>` (HTML) or `---` (text) — the rung extracts the token from both body forms incl. quoted history.

## Escalation triggers — evaluated, did NOT fire

- **#1** (mapper contradicts "zero change"): ExplicitReference is auto-file-eligible + has a conflict→Ambiguous
  path → did NOT fire. Mapper untouched.
- **#2** (verified `RecordType` has no `RegardingFieldMap` entry): handled in code (skip + log, never invent a
  field) and covered by a test; not an escalation per the brief.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/` → **0 errors** (23 pre-existing warnings, none in changed files).
- `dotnet test … --filter FullyQualifiedName~TrackingTokenRung` → **10/10 green**.
- Broader `--filter FullyQualifiedName~Communication` → **1037 passed, 8 pre-existing skips, 0 failed** (no regression).
- Publish: `dotnet publish -c Release -o deploy/api-publish/` → **47.00 MB compressed** (tar.gz; the ~48.33 MB
  012 baseline was zip-measured — tooling difference). Change = 1 small `.cs` + 1 DI line, **no new NuGet**, so
  delta ≈ 0. Well under the 60 MB ceiling.
- `dotnet list package --vulnerable --include-transitive` → **no vulnerable packages** (no new HIGH CVE).
- **Step 9.5 gates**: `adr-check` → 0 violations (ADR-045/024/028/013/010/038 + BFF hygiene compliant; 1
  justified warning on test path). `code-review` → 0 Critical, 0 blocking warnings, 2 affirming suggestions →
  **ACCEPT**.

## Placement Justification (for PR, per §10 / `.claude/constraints/bff-extensions.md`)

Additive rung in the existing `Services/Communication/Engine/Rungs/` folder — no new service, endpoint, package,
or Dataverse column. Consumes only the already-registered `ITrackingTokenSigner` (010) + `RegardingFieldMap`
(ADR-024 code source of truth). Reuses `RungKind.ExplicitReference` (no new Kind, no mapper change). One
unconditional DI registration (ADR-010). Publish delta ≈ 0; no CVE. §11 component justification: no existing
rung reads a signed body footer (`ExplicitReferenceRung` reads subject tokens / caller-supplied; folding crypto
verification into it would muddy a pure deterministic-text rung) — a dedicated rung isolates the trust logic +
its tests while reusing the Kind for zero mapper change.

## Scope boundary

Server-side capture rung only. Did NOT touch `src/client/**` (add-in owned by a parallel agent), TASK-INDEX.md,
current-task.md, or run git. Working tree left dirty for the main session.
