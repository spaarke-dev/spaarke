# R-2 — Compose-path content-dedup: graduate-on-divergence (FR-C3) — CODE COMPLETE (2026-08-06)

Rigor FULL · opus·high · parallel-safe:false. Completes FR-C3 for the **editable** Compose document path.
Owner decision (2026-08-06): implement the spec's literal **graduate-on-divergence** model (not stamp-only,
not plan-literal-suppress). Approved plan: `.claude/plans/wild-waddling-sifakis.md`.

## Why this replaced the remediation plan's "~12-line suppress hook"

The remediation plan's literal instruction (suppress the create + return the canonical, mirroring the
email-attachment path) is **unsafe for Compose documents**, which are editable/living:
1. **Data loss** — two matters' drafts from the same template, saved before first edit, would collapse into
   one `sprk_document`.
2. **Session cross-wiring** — `PromoteIfEphemeralAsync`'s idempotent branch rebinds the session onto the
   returned row; a foreign canonical (different drive-item) leaves the session's `DocumentSpeId` pointing at
   the just-uploaded item while the record points elsewhere → later saves write to the wrong place.

Spec **FR-C3** (`spec.md:87`) already anticipates this: *"copy graduates to its own document on content
divergence."* That behavior was not implemented in the task-024 detector (suppress-forever — correct only for
immutable copies).

## Model — canonical vs. hash-linked copy

- **`sprk_document.sprk_canonicaldocument`** — self-lookup (`sprk_document → sprk_document`). `null` ⇒ this
  row IS a canonical; non-null ⇒ a hash-linked COPY (byte-identical *now*, same `sprk_canonicalhash`). On
  divergence the link is CLEARED and `sprk_canonicalhash` updated → the copy graduates to its own canonical.
  Distinct from `sprk_parentdocument` (attachment→parent-email) — a doc can be both (§11 justified).
- `sprk_canonicalhash` (task 023, indexed) is unchanged — the content identity every path stamps.

## What shipped (code — non-gated)

- **`Services/Documents/ContentDedupDetector.cs`**:
  - `ResolveContentIdentityAsync(driveId, itemId)` — pure hash-read + canonical lookup, NO notify/suppress
    (the Compose LINK path's seam). `ReconcileAsync` (immutable email-attachment path) refactored to reuse it;
    external contract unchanged.
  - `FindCanonicalByHashAsync` — now excludes hash-linked copies (`sprk_canonicaldocument IS NULL`), so dedup
    always resolves to the TRUE canonical (never links a third upload to a copy about to graduate).
  - `NotifyLinkedCopyAsync` — the never-silent linked-copy notification (distinct from the suppress notice).
- **`Services/Compose/ComposeService.cs`** (`PromoteIfEphemeralAsync`):
  - **Create branch**: reads content identity; stamps `sprk_canonicalhash`; on a canonical hit LINKS via
    `sprk_canonicaldocument` (EntityReference) + notifies — **never suppresses** (no cross-wiring).
  - **Idempotent branch**: `GraduateLinkedCopyIfDivergedAsync(existingRow, …)` — if the row is a linked copy
    whose live hash ≠ stored hash, severs the link + stamps the new hash. **No extra round-trip**: the
    alt-key lookup (`TryFindDocumentByGraphItemIdAsync`, now returns the row) was widened to also fetch the two
    dedup columns (fixes the N+1 the first draft had). Race-path caller adjusted to `?.Id`.
  - Optional trailing `ContentDedupDetector? dedupDetector = null` ctor dep → guarded no-op when absent
    (bare test ctor / DI-absent); real scoped detector injected in every host (OfficeModule, always-on).
- **`Spaarke.Dataverse` generic `UpdateAsync`** (`DataverseServiceClientImpl` + `IGenericEntityService` doc):
  added a **`DBNull.Value` clear-sentinel** — C# `null` still SKIPS a field (every existing caller unchanged),
  `DBNull.Value` explicitly CLEARS it (the only way to null/sever a lookup through the generic seam). Required
  to clear `sprk_canonicaldocument` on graduation. Additive + non-breaking.

Email-attachment path (`OfficeDocumentPersistence`) unchanged — suppress-forever (immutable copy never
diverges); it benefits automatically from the linked-copy exclusion in `FindCanonicalByHashAsync`.

## Tests (contract-first, mocked boundaries — ADR-038)
- `ContentDedupDetectorTests` (+5): `ResolveContentIdentityAsync` returns hash+canonical with no side effects;
  hash-unavailable skips the lookup; the canonical lookup query filters `sprk_canonicaldocument IS NULL`
  (linked-copy exclusion); `NotifyLinkedCopyAsync` emits / degrades non-fatally.
- `ComposeContentDedupTests` (new, 6): create no-hit → stamps hash, no link; create hit → LINKS + notifies,
  still creates (own record, session not rebound to canonical); subsequent identical → no graduation;
  subsequent diverged → graduates (clears link via `DBNull`, stamps new hash); no-detector no-op;
  dedup-throws non-fatal.
- Suites: build 0-err/0-warn; **854 Compose+ContentDedup green** (18 new/extended). Publish **48.30 MB
  compressed (+0.01 MB vs task-024 baseline)**; `dotnet list package --vulnerable` clean.

## Placement Justification (§10) + §11
Extends the existing SPE-dedup seam (`ContentDedupDetector`) + the existing `ComposeService` create-on-save;
no new microservice, package, or Graph surface in callers. AI facade N/A. §11: `sprk_canonicaldocument`
Existing = `sprk_parentdocument` (different relationship — attachment→email, can't hold copy→canonical too);
Extension = not possible (semantic conflict); Cost-of-doing-nothing = data loss (suppress) or no editable-doc
dedup at all.

## GATED TAIL (operator go-ahead — the only remaining step)
Schema column **`sprk_document.sprk_canonicaldocument`** (self-lookup) via `dataverse-create-schema` (Web API +
PowerShell), packed into the managed solution (ADR-027), verified in `spaarkedev1` — the exact mechanism task
023 used for `sprk_canonicalhash`. Tracked as **task 027** (see TASK-INDEX). The code ships safely behind it:
until the column exists, writes to it degrade non-fatally (NFR-04) — so gate the *enablement*, not the merge.

## Deviation from the remediation plan (surfaced per §6.5)
Path A (project-scoped, owner-approved): the Compose path LINKS (does not suppress) and GRADUATES on
divergence, deviating from the remediation plan's literal "suppress + return canonical" because that model is
unsafe for editable documents and contradicts FR-C3's own "graduate on divergence" clause. Owner chose the
full graduate-on-divergence model on 2026-08-06.
