# Task 024 — SPE content dedup Tier-1 (quickXorHash, gate-after-write) — CORE + EMAIL-ATTACHMENT PATH COMPLETE (2026-08-06)

Rigor FULL · opus·high · parallel-safe:false. FR-C3 Tier-1 exact-hash dedup. All testable acceptance criteria met;
Compose-path hook deferred (coordinated follow-on); Assistant path safe by construction.

## What shipped
- **`DriveItemOperations.GetQuickXorHashAsync(driveId, itemId, ct)`** — app-only `GET …?$select=file` →
  `file.hashes.quickXorHash`. Keeps the Graph hash facet inside the ADR-007 boundary. Non-throwing (returns null
  when absent / not-yet-populated / on error).
- **`SpeFileStore.GetQuickXorHashAsync`** — `virtual` facade delegate (mockable at the module boundary — the
  codebase idiom, cf. `UploadSmallAsync`).
- **`UpdateDocumentRequest.CanonicalHash`** + mapping in BOTH impls (`DataverseServiceClientImpl` SDK +
  `DataverseWebApiService`) → `sprk_canonicalhash` (task-023 indexed column).
- **`Services/Documents/ContentDedupDetector.cs`** (+ `DedupDecision` record) — gate-after-write: read the
  persisted item's quickXorHash via the facade, reconcile against the `sprk_document.sprk_canonicalhash`
  cross-container authority index, and on a byte-identical hit NOTIFY (never silent, via `NotificationService`)
  + report the canonical document so the caller suppresses the copy. Non-fatal end-to-end (NFR-04): every failure
  degrades to `DedupDecision.NoDedup`. `ReconcileAsync` is `virtual` (hook-testable). Registered concrete/scoped
  in `OfficeModule` (ADR-010).
- **`OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync`** (the email-attachment / Office save path) —
  reconcile BEFORE create: on a duplicate, return the canonical id and create NO second document; else create +
  stamp `CanonicalHash` so future uploads dedup against it.
- **Tests (9)**: `ContentDedupDetectorTests` (7 — duplicate→canonical; first-upload→hash-to-stamp; hash-unavailable
  →NoDedup+no-lookup; hash-read-throws→non-fatal; lookup-throws→non-fatal+hash-returned; reads-via-facade ADR-007;
  duplicate-with-resolvable-owner→notification emitted) + `OfficeDocumentPersistenceDedupTests` (2 — duplicate→NO
  second `CreateDocumentAsync` + returns canonical; first→creates + stamps `CanonicalHash`).

## Acceptance criteria status
| Criterion | Status |
|---|---|
| 1. Assistant-persist → no second canonical document | ✅ **by construction** — that path creates NO `sprk_document` (SPE + Redis only, per storage-seam recon). Nothing to suppress. |
| 2. Email-attachment path (`OfficeDocumentPersistence`) deduped — R2-specific | ✅ **DONE + tested** (the explicitly-flagged spec-miss-risk path). |
| 3. On hit, NOTIFY (never silent) + open/link canonical | ✅ detector notifies via `NotificationService`; tested. |
| 4. Hash read via `SpeFileStore` (no direct Graph in callers) — ADR-007 | ✅ tested. |
| 5. Non-fatal (hashing/detect throws → upload proceeds) | ✅ tested (2 non-fatal paths). |
| 6. Tier-2 (near-dup / documentVector3072) NOT implemented | ✅ not built. |
| 7. Tests pass; build; size ≤60 MB + delta; no new HIGH CVE | ✅ build 0 err; 9/9 new + Office suite 21/21 green; publish 48.29 MB (flat, no packages); CVE clean. |

## Deferred (tracked) — the two absorbed-scope pieces beyond the R2-critical path
1. **Compose-path content-dedup hook** — `ComposeService.PromoteIfEphemeralAsync` already has SPE-drive-item-id
   idempotency (`sprk_graphitemid_uk`), so it never creates a second row for the SAME item; adding CONTENT dedup
   there needs a ctor dependency on `ContentDedupDetector` + `entity[sprk_canonicalhash]` stamp + a suppress
   branch (~12 lines). Deferred because `ComposeService` is **compose-r5 / compose-fidelity-contended** (a direct
   test-construction site `ComposeServiceCreateOnSaveTests` ripples on the ctor change) — per §10 / project
   CLAUDE.md hot-path coordination, this warrants a coordinated PR (conflict-check + ripple update), not a forced
   edit in this session. The detector is built + DI-registered so the hook is a small, low-risk follow-on.
   **→ file via /defer (FR-C3 Compose coverage).**
2. **Orphan transient blob cleanup** — on a duplicate hit the second SPE blob is uploaded (gate-after-write) but
   left unlinked (no `sprk_document`). Accepted per the owner's "a brief transient duplicate blob is ACCEPTABLE";
   a delete-orphan sweep is a fast-follow. **→ file via /defer (transient-blob cleanup).**

## Design decisions / deviations
- **Detector always-on (not config-gated).** The POML allows either ("Register unconditionally; IF config-gated,
  use Null-Object"). I chose always-on: the detector is non-fatal by construction (any failure → NoDedup →
  upload proceeds), so an operator kill-switch adds little; no Null-Object peer needed (ADR-032 not triggered).
- **App-only hash read even for OBO upload paths.** `GetQuickXorHashAsync` uses `_factory.ForApp()`; the app MI
  has container access, so reading item metadata app-only is valid regardless of the upload's OBO/app context.
- **Duplicate short-circuits the second document's downstream side-effects** in the Office path (returns canonical
  before the create + subsequent enrichment). This is correct for Tier-1 ("no second document"); any AI jobs key
  on the canonical id.
- **Live byte-identical-upload seam test deferred to a real SPE tenant.** The Graph/SPE transport is un-fakeable
  end-to-end (same class of constraint as task 021's ServiceClient); coverage is pinned at the mockable seams
  (facade `virtual` + `IGenericEntityService` + `IDocumentDataverseService`) — the detector DECISION + the Office
  suppression WIRING are both tested. The full through-SPE seam test needs the live tenant + task-023 column.

## Placement Justification (§10)
`ContentDedupDetector` extends the SPE storage seam in place — reuses `SpeFileStore` (ADR-007), `IGenericEntityService`,
and `NotificationService`; no new microservice, no new package, no new Graph surface in callers. Registered
concrete/scoped in `OfficeModule` (ADR-010). §11: `<existing>` none (no content-dedup service); `<extension>`
distinct cross-cutting responsibility no persist method owns; `<cost-of-doing-nothing>` N canonical docs for
byte-identical files.
