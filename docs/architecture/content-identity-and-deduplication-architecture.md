# Content Identity & Deduplication Architecture

> **Last Updated**: 2026-09-04
> **Last Reviewed**: 2026-09-04
> **Reviewed By**: email-communication-intelligence-r2 (Pillar C shipped the content-dedup layer + graduate-on-divergence; this doc is its first canonical home)
> **Status**: Current
> **Purpose**: The single reference for how Spaarke decides "is this the *same* thing?" across documents and communications — the three deduplication layers, their distinct mechanisms, and the invariants every save/upload/capture path must respect.
> **Code is the source of truth.** Primary code: `Services/Documents/ContentDedupDetector.cs`, `Services/Compose/ComposeService.cs`, `Services/Office/OfficeDocumentPersistence.cs`, `Services/Communication/IncomingCommunicationProcessor.cs`.

---

## Why this doc exists

Deduplication was historically undocumented and assumed to be "owned by a separate detector project" (`sdap-file-duplication-detector-r1`). **That project was absorbed into email-communication-intelligence-r2 (Pillar C) and its content-dedup design shipped** — so dedup is now a live, cross-cutting platform mechanism used by Compose save, Office/add-in save-back, email capture, and Assistant persist. Any new save path (e.g. the Word add-in) must consume it rather than reinvent or wait on it. This doc is the map.

**Dedup is not association.** Dedup answers *"is this the same record?"*; the Association Engine ([communication-intelligence-architecture.md](communication-intelligence-architecture.md)) answers *"which record does this belong to?"*. Different machinery, different docs.

---

## The three layers (each answers a different "same as?" question)

| Layer | Question | Key mechanism | Applies to |
|---|---|---|---|
| **1. Item identity** | Same SPE drive item? | `sprk_graphitemid_uk` alternate (unique) key on `sprk_document` | Every document (SPE-backed) |
| **2. Content identity** | Same *bytes*, different item? | `sprk_canonicalhash` (SPE `quickXorHash`) + `ContentDedupDetector` | Every upload/save/capture path |
| **3. Message identity** | Same *email*, different delivery? | `sprk_internetmessageid` alternate key + Service Bus idempotency + context-merge | `sprk_communication` |

A single save can be checked by more than one layer — item identity is a hard schema guard; content identity is a soft (best-effort) reconciliation; message identity applies only to communications.

---

## Layer 1 — Item identity (`sprk_graphitemid_uk`)

The **primary, structural** guard against duplicate `sprk_document` rows: a Dataverse **alternate (unique) key** on the SPE drive-item id. Two attempts to file the *same drive item* cannot create two `sprk_document` rows — Dataverse rejects the second.

- **Load-bearing** — Compose's transient-key dedup and promote-idempotency both rest on it. **Do not relax it.**
- This is *identity*, not *content*: it dedups "the exact same file object", not "a byte-identical copy uploaded as a new item". That second case is Layer 2.

---

## Layer 2 — Content identity (`sprk_canonicalhash` + `ContentDedupDetector`)

Detects a **byte-identical copy uploaded as a *different* SPE item** — the case Layer 1 can't see. Shipped in r2 (FR-C3, absorbing `sdap-file-duplication-detector-r1`).

### Mechanism — gate-AFTER-write (owner decision 2026-08-05)

1. Upload the file to SPE (a brief transient duplicate blob is **accepted** — this is the deliberate trade that retired the pre-upload-hash spikes).
2. Read the persisted item's **`quickXorHash`** via the `SpeFileStore` facade (ADR-007 — `sha256Hash` is deprecated on SPE driveItems; `quickXorHash` is the identity).
3. Reconcile against the indexed **`sprk_document.sprk_canonicalhash`** (cross-container authority, task 023).
4. On a hit → **notify (never silent)** + report the canonical `sprk_document`; on a miss → return the hash for the caller to **stamp** on the new document so future uploads dedup against it.

`ContentDedupDetector.ResolveContentIdentityAsync(driveId, itemId)` is the pure, side-effect-free core `(Hash, CanonicalId)`; `ReconcileAsync(...)` wraps it with notify + the suppress decision.

### Two caller modes — the key distinction

The same content-hash hit means different things depending on whether the document is **immutable** or **editable**:

| Mode | Caller | On a byte-identical hit | Why |
|---|---|---|---|
| **Suppress-forever** | Immutable copies — **email attachments**, Assistant persist (`OfficeDocumentPersistence`, `ReconcileAsync`) | Do **not** create a second document; point the user at the canonical | An archival copy never diverges, so one canonical is always correct |
| **Link + graduate-on-divergence** | **Editable** documents — **Compose** save (`ComposeService.PromoteIfEphemeralAsync`, `NotifyLinkedCopyAsync`) | Create the row but record it as a **hash-linked copy** (`sprk_canonicaldocument` → canonical); it **graduates to its own canonical the moment its content diverges** (first edit) | Two matters' drafts from the same template are byte-identical *right now* but must not collapse into one document |

### Graduate-on-divergence (FR-C3, editable path)

- **`sprk_document.sprk_canonicaldocument`** — a self-referential lookup (`sprk_document → sprk_document`, task 027 schema).
  - `null` ⇒ this row **is** a canonical (the dedup authority for its content).
  - non-null ⇒ a **hash-linked copy** (byte-identical *now*), carrying the same `sprk_canonicalhash`.
  - On divergence, the lookup is **cleared** and `sprk_canonicalhash` is updated to the new content hash — the copy graduates.
- **`FindCanonicalByHashAsync` excludes hash-linked copies** (`sprk_canonicaldocument IS NULL`), so a third identical upload always dedups to the *true* canonical, never to a copy that is about to graduate.
- **Distinct from `sprk_parentdocument`** (attachment → parent-email) — a document can be both an attachment and a hash-linked copy, so the two lookups are separate.

### Scope — Tier 1 only

**Tier-1 (exact `quickXorHash` equality) is what shipped.** **Tier-2 (semantic near-duplicate over the existing `documentVector3072` cosine-KNN "Find Similar" engine, at a dedup-tuned threshold) was explicitly DEFERRED out of R2** (spike-gated, NFR-08). Near-dup is a validated fast-follow, not a shipped capability — do not assume it exists.

---

## Layer 3 — Message identity (`sprk_internetmessageid`)

For `sprk_communication` — the same email arriving via multiple mailboxes, or saved by multiple users, or delivered twice by a webhook race — must yield **exactly one** row (NFR-02, FR-C1).

- **Structural key**: a Dataverse **alternate (unique) key** on `sprk_internetmessageid` (task 020) — enforced by the platform, not app-level check-then-insert.
- **Race-proof create + Service Bus idempotency** (task 021) — concurrent inserts of the same message-id race-fail gracefully to a single row; SB idempotency keyed on the message-id.
- **Context-merge on duplicate** (FR-C2, task 022/028) — a detected duplicate is **not dropped**: its delivery/uploader context merges onto the canonical row (`sprk_deliveredmailboxes`, `sprk_savedbyusers`). No delivery fact is lost.
- **Cross-path reconciliation** (FR-C4, tasks 025/029) — a captured `sprk_communication` and a user-saved `sprk_document` archive of the *same* email reconcile via message-id through the existing **`sprk_document.sprk_relatedcommunication`** (no new column) — linked, not duplicated. The review surface shows one email.

---

## Invariants (every save/upload/capture path MUST honor)

1. **Never relax `sprk_graphitemid_uk`.** Compose transient-key dedup + promote-idempotency depend on it.
2. **Content dedup is best-effort / non-fatal (NFR-04).** Any hash-read / lookup / notification failure logs and degrades to `DedupDecision.NoDedup` — the upload proceeds, no document is ever erroneously blocked. Dedup **must not** fail a capture, save, or send.
3. **Never silent.** A suppressed immutable duplicate emits a "Duplicate document detected" notification; a hash-linked editable copy emits a "Linked to an existing document" notification. The user always learns what happened.
4. **Editable ≠ immutable.** Do not apply suppress-forever to an editable document — it must link + graduate, or two distinct living drafts collapse into one (data loss).
5. **Dedup to the true canonical.** Content lookups exclude hash-linked copies (`sprk_canonicaldocument IS NULL`).
6. **Reach SPE only via `SpeFileStore`** (ADR-007) for the hash read — no direct Graph in callers.

---

## Schema

| Field / key | On | Layer | Task |
|---|---|---|---|
| `sprk_graphitemid_uk` (alternate key) | `sprk_document` | 1 (item identity) | pre-existing |
| `sprk_canonicalhash` (indexed string) | `sprk_document` | 2 (content) | 023 |
| `sprk_canonicaldocument` (self-lookup) | `sprk_document` | 2 (graduate-on-divergence) | 027 |
| `sprk_internetmessageid` (alternate key) | `sprk_communication` | 3 (message) | 020 |
| `sprk_deliveredmailboxes`, `sprk_savedbyusers` (memo) | `sprk_communication` | 3 (context-merge) | 028 |
| `sprk_relatedcommunication` (lookup) | `sprk_document` | 3 (cross-path) | reuse (029) |

---

## Code inventory

| Component | Path | Role |
|---|---|---|
| `ContentDedupDetector` | `Services/Documents/ContentDedupDetector.cs` | Content-hash dedup core: `ResolveContentIdentityAsync` (pure), `ReconcileAsync` (suppress), `NotifyLinkedCopyAsync` (link) |
| `DedupDecision` | same file | `(CanonicalHash, IsDuplicate, CanonicalDocumentId)` result record |
| Compose link + graduate | `Services/Compose/ComposeService.cs` (`PromoteIfEphemeralAsync`, `CanonicalDocumentAttribute`), `ComposeCreateOnSavePromoter.cs` | Editable-path caller — link on create, graduate on divergence |
| Email-attachment / Assistant persist | `Services/Office/OfficeDocumentPersistence.cs` | Immutable-path caller — suppress-forever |
| Message-level dedup + context-merge | `Services/Communication/IncomingCommunicationProcessor.cs` | Layer 3 on capture + user-upload |
| SPE hash read | `Infrastructure/Graph/SpeFileStore.cs` (`GetQuickXorHashAsync`) | The `quickXorHash` source (ADR-007 facade) |

---

## Related

- [communication-intelligence-architecture.md](communication-intelligence-architecture.md) — the association/triage engine dedup feeds (association ≠ dedup)
- [ADR-049](../../.claude/adr/ADR-049-compose-shadow-document.md) — Compose save contract (the editable path whose graduate-on-divergence lives here)
- [ADR-007](../../.claude/adr/ADR-007-spe-storage-seam.md) — `SpeFileStore` facade (the hash-read seam)
- `projects/email-communication-intelligence-r2/spec.md` — FR-C1..C4 + NFR-02/04/08 (the requirements this implements)
- [office-outlook-teams-integration-architecture.md](office-outlook-teams-integration-architecture.md) — the add-in save path that consumes this

---

*Last Updated: 2026-09-04 — first canonical documentation of the dedup layers, after email-communication-intelligence-r2 Pillar C.*
