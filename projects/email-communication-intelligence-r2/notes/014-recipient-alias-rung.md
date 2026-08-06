# Task 014 — RecipientAliasRung + Bcc plumbing (FR-A2)

**Status**: ✅ complete · **Rigor**: FULL · **Model**: sonnet/high (run on Opus session — allowed)
**Date**: 2026-08-05

## What shipped

A new deterministic rung-0-tier signal + the Bcc capture plumbing it needs.

1. **Bcc end-to-end**
   - `NormalizedMessage.Bcc` (new `IReadOnlyList<string>`, defaults empty) beside To/Cc.
   - `IncomingCommunicationProcessor` capture `$select` now requests `bccRecipients`.
   - `GraphMessageNormalizer` maps `message.BccRecipients → Bcc` (once, at the boundary — ADR-045).
2. **`RungKind.RecipientAlias = 9`** — a distinct kind (not a reuse of `ExplicitReference`) so the alias
   signal is isolated + independently testable and reinforces distinctly.
3. **`RecipientAliasRung`** (new, Order 0) — parses To/Cc/**Bcc** for the `matter-{ref}@` scheme, resolves
   each to its `sprk_matter` via the existing `QueryMatterByReferenceNumberAsync` seam (bare ref then
   `MAT-{ref}` fallback, mirroring `ExplicitReferenceRung`), and emits a confidence-1.0 `RungMatch`
   (`sprk_regardingmatter`). Distinct matters → multiple matches (mapper → Ambiguous); the same matter
   aliased across fields collapses to one.
4. **Mapper eligibility** — `RecipientAlias` added to `IsAutoFileEligible` (UNCONDITIONAL, rung-0 tier) AND
   `IsDeterministicWriteEligible`; `IncomingAssociationResolver.IsDeterministic` classifies it in the
   always-run deterministic pass.
5. **DI** — registered unconditionally in `CommunicationModule` in the rung-0 group.

## Key decisions

- **Auto-file widening (escalation trigger 2) — considered, NOT fired.** Adding `RecipientAlias` to
  `IsAutoFileEligible` widens the auto-file set beyond the C-1-narrowed rung-0/1. This does not conflict with
  C-1's misfile-avoidance intent: C-1 narrows auto-file to *explicit* deterministic signals precisely to
  avoid misfiling on weaker participant/structural inference — a per-record intake address resolved to one
  specific matter is the strongest explicit signal there is. The POML constraint explicitly directs this;
  the reasoning is inline in `AssociationStatusMapper.IsAutoFileEligible`.
- **Confidence 1.0** — a per-record alias is a deliberate, unambiguous routing instruction, as authoritative
  as a caller-supplied regarding (which is also 1.0). Clears the auto-file threshold for a core matter.
- **MVP scope = `matter-{ref}@` only** (escalation trigger 1). Other core-type schemes
  (`project-{ref}@`, `invoice-{ref}@`, …) need a record-type→address-scheme catalog no config supplies
  today — a deliberate follow-up, not a hardcoded per-tenant convention.

## Verification

- BFF build: 0 errors.
- Targeted suite (rung + normalizer + mapper + resolver + symmetry): **77/77**.
- Full Communication + seam suite: **876 passed, 5 pre-existing skips, 0 failed** — no regressions.
- Publish size: **50.91 MB compressed (incl PDBs)** — under the 60 MB ceiling; task code delta negligible
  (one small class). No new NuGet / CVE.

## Downstream

- Runtime Bcc delivery depends on a per-client Exchange **mail-flow rule** that Bcc's the `matter-*@` intake
  alias (opt-in; NOT tenant-wide plus-addressing) — a deployment/runbook concern, not code.
- Reuses the same `QueryMatterByReferenceNumberAsync` seam as `ExplicitReferenceRung`; no Dataverse schema
  change.
