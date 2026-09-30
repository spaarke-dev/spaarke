# Dedup & Save-Back Identity — Context for the Add-in

> **Created**: 2026-09-04 · by `email-communication-intelligence-r2` (Pillar C built the content-dedup layer this project's §8 defers to).
> **For**: `spaarkeai-word-add-in-r1` — specifically **F-1** (conditional document identity) and **S-6** (save-as-version, don't re-create).
> **⚠️ This corrects a stale premise in `design.md §8 / §4.1 / §4.2`.** See §1 below.
> **Canonical reference**: [`docs/architecture/content-identity-and-deduplication-architecture.md`](../../../docs/architecture/content-identity-and-deduplication-architecture.md) — created 2026-09-04 (this project doc is the add-in-focused slice of it).

---

## 1. The correction: dedup is SHIPPED, not pending

Your `design.md §8` ("Duplicate handling — coordination boundary") says duplicate detection is **"owned by `sdap-file-duplication-detector-r1` (analysis complete, pre-spec)"** and treats it as a future project's timeline. `§4.1`/`§4.2` repeat "NOT owning duplicate detection… owned by `sdap-file-duplication-detector-r1`."

**That project was absorbed into `email-communication-intelligence-r2` (Pillar C, FR-C1–C4) and its content-dedup design shipped to master.** So:

- ✅ **Content dedup exists and is live** — `ContentDedupDetector` (`quickXorHash` + `sprk_canonicalhash`), gate-after-write, with a **graduate-on-divergence** model for *editable* documents.
- ✅ **Item-identity dedup exists** — the `sprk_graphitemid_uk` alternate key (your §8 already notes this correctly).
- ⏸️ **Only Tier-2 (semantic near-dup over `documentVector3072`) is still deferred** — that half is a validated fast-follow, not built.

**Net for r1**: the add-in **consumes shipped machinery** for S-6/F-1 — it does not wait on, or coordinate with, a separate detector project. Update §8 accordingly.

---

## 2. Two dedup mechanisms the add-in save-back rides

Your S-6 ("save as version, don't re-create") and F-1 ("conditional document identity") intersect **two** distinct layers. Keep them separate:

### Layer A — Item identity → the S-6 primary path
- **`sprk_graphitemid_uk`** (alternate key on the SPE drive-item id) already guarantees one `sprk_document` per SPE item. Your §8 is right that this "already prevents duplicate `sprk_document` rows" and **must not be relaxed** (Compose transient-key dedup + promote-idempotency depend on it).
- **F-1 → S-6 flow**: when F-1 resolves the open document to an existing `sprk_document` (via `Office.context.document.url` → Graph `/shares/…/driveItem` → `sprk_graphitemid_uk`, or the custom-XML-part GUID stamp), the save-back should **version the existing record**, not create a new one. `SaveRequest.ExistingDocumentId` already exists (your §8 notes it). This is the add-in-visible slice — pure identity, no hashing needed.

### Layer B — Content identity → the "same bytes, new item" safety net
- If F-1 *cannot* identify the document (a doc that left Spaarke and came back as a new SPE item, or a document opened from outside), the save-back creates a new item — and **`ContentDedupDetector` catches the byte-identical case after write**: reads the new item's `quickXorHash`, reconciles against `sprk_canonicalhash`, and on a hit **notifies + points at the canonical** (immutable path) — never silently.
- The add-in **does not call the detector directly** — it rides the shipped `/api/office/save` flow, which already invokes it. Just don't build a parallel check.

---

## 3. The one thing to get right — editable vs immutable

A Word document is **editable**, so if the save-back path ever treats it like an immutable copy (suppress-forever on a hash hit), two genuinely-different drafts that happen to be byte-identical *right now* would collapse into one record — **data loss**.

The platform already solved this for Compose with **graduate-on-divergence**: a byte-identical editable save is recorded as a **hash-linked copy** (`sprk_canonicaldocument` → canonical) and **graduates to its own canonical the moment it's edited**. When the Word save-back handles the "same bytes, editable" case, it must use the **link/graduate** mode (mirroring `ComposeService.PromoteIfEphemeralAsync`), **not** the suppress-forever mode (which is only for immutable email attachments / Assistant persist).

> Practically for r1: if you stay on the **item-identity → version-save** path (Layer A) for Spaarke-sourced documents (which L-2 scopes you to), you sidestep this entirely — version-save targets the *same* record, so there's no dedup decision to make. Layer B only matters for the "returned as a new item" edge case.

---

## 4. What maps to what (add-in ↔ dedup)

| Add-in item | Dedup layer | What to do |
|---|---|---|
| **F-1** conditional document identity | Layer A (item identity) | Resolve `sprk_document` via `sprk_graphitemid_uk` / XML-part GUID; feed `SaveRequest.ExistingDocumentId` |
| **S-6** save-as-version | Layer A | If F-1 resolved → version the existing record, don't create |
| Save-back of a **returned/new** item | Layer B (content) | Ride `/api/office/save`; `ContentDedupDetector` handles the byte-identical case; use **link/graduate**, never suppress, for editable docs |
| **G1** "duplicate prevention" UAT feedback | Layers A + B | Both already ship; the UX is "notify + offer canonical / version-save", not a new detector |

---

## 5. Pointers

| Topic | Location |
|---|---|
| **Canonical dedup architecture** (all 3 layers, invariants, schema) | [`docs/architecture/content-identity-and-deduplication-architecture.md`](../../../docs/architecture/content-identity-and-deduplication-architecture.md) |
| Content-dedup detector | `src/server/api/Sprk.Bff.Api/Services/Documents/ContentDedupDetector.cs` |
| Editable link/graduate caller (the pattern to mirror) | `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs` (`PromoteIfEphemeralAsync`) |
| Immutable suppress caller | `src/server/api/Sprk.Bff.Api/Services/Office/OfficeDocumentPersistence.cs` |
| Save endpoint the add-in rides | `src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs` (`POST /api/office/save`) |
| Schema | `sprk_graphitemid_uk`, `sprk_canonicalhash`, `sprk_canonicaldocument` on `sprk_document` |
| r2 requirements | `projects/email-communication-intelligence-r2/spec.md` FR-C1..C4, NFR-02/04/08 |

---

## 6. Suggested edits to this project's `design.md`

1. **§8** — replace "owned by `sdap-file-duplication-detector-r1` (pre-spec)" with "**shipped in email-communication-intelligence-r2 (Pillar C)**; r1 consumes `ContentDedupDetector` + `sprk_canonicalhash` + graduate-on-divergence; only Tier-2 near-dup remains deferred."
2. **§4.2 (deferred)** — "Full duplicate detection" is **half-shipped**: Tier-1 exact-hash + graduate-on-divergence are live; only Tier-2 near-dup is deferred.
3. Add a one-line invariant to §8: for editable save-back, **link/graduate, never suppress** (see §3 above).
