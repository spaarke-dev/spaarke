# Task 025 — FR-C4 cross-path reconciliation — CODE COMPLETE (2026-08-06)

Rigor FULL · sonnet·high (ran on Opus) · parallel-safe:false. A mailbox-captured email becomes a
`sprk_communication`; a user "Save to Spaarke" of the SAME email's `.eml` archive lands as a `sprk_document`. FR-C4
LINKS the two representations (keyed on the shared internet-message-id, the canonical identity task 021 establishes)
so the reconciliation/review surface (Pillar E) resolves them to **ONE email** — not two rows.

## The correct model (owner: "what is the correct functional + technical solution?")

The bridge is a **new single-valued lookup ON THE DOCUMENT**: `sprk_document.sprk_linkedcommunication` → `sprk_communication`
(N:1). The document is the dependent representation; the **communication stays the canonical reconciliation unit**
(ADR-045 — extend, not fork; NOT a new `sprk_reconciliation` entity, NOT overloading an existing lookup).

**Why a new column, not reuse (§11 gate):**
- *Existing*: no lookup on `sprk_document` targets `sprk_communication`. `sprk_email` → the Dataverse **email activity**
  (wrong target type + different semantics); `sprk_parentdocument` → document; `sprk_matter`/`_project`/`_invoice` →
  regarding records.
- *Extension*: impossible — a Dataverse lookup is single-target-typed; none of the above can point at `sprk_communication`.
- *Cost-of-doing-nothing*: the captured communication and the user-saved `.eml` archive surface as **two separate rows**
  in Pillar E (the exact FR-C4 failure); no queryable relationship exists to fold them.
- New **COLUMN**, not a new **ENTITY** → the escalation trigger (new reconciliation entity) does NOT fire; no §6 stop.

**Why the lookup lives on the document (N:1):** it answers "is this document already represented as a communication?"
directly (Pillar E folds a document that has the lookup set), and a communication finds its archive via the reverse
relationship. A single write target from BOTH arrival paths → naturally idempotent (single-valued lookup).

## What shipped (code — non-gated)

- **`Services/Communication/CrossPathLink.cs`** (new, static; mirrors `DeliveryContextMerge`):
  - `LinkDocumentToCommunicationAsync(dataverse, documentId, communicationId, …)` — reads the document's current
    lookup; writes only when not already linked to this communication (idempotent); non-fatal (any failure → log +
    return false). Written via the generic seam (`IGenericEntityService.UpdateAsync` with an `EntityReference`), NOT
    the atomic document build → **contract-first-safe** (degrades until the gated column exists).
  - `FindAndLinkArchiveDocumentsAsync(dataverse, internetMessageId, communicationId, …)` — capture-side entry: queries
    email-archive `sprk_document`s (`sprk_emailmessageid == messageId AND sprk_isemailarchive == true`, `TopCount 20`)
    and links each; idempotent + non-fatal.
- **`Services/Office/OfficeDocumentPersistence.cs`** (office side, capture-then-upload): the 022 uploader-merge method
  became `MergeUploaderAndResolveCanonicalAsync` — resolves the canonical **once** and returns its id (reused for both
  the FR-C2 saver-merge AND the FR-C4 link; **no added Dataverse read**). After the document is created,
  `LinkDocumentToCanonicalCommunicationAsync` links the new archive document to that canonical.
- **`Services/Communication/IncomingCommunicationProcessor.cs`** (capture side, upload-then-capture): after the
  successful first-create + delivering-mailbox seed merge, `CrossPathLink.FindAndLinkArchiveDocumentsAsync` links any
  `.eml` archive uploaded BEFORE capture. Reuses the already-injected `IGenericEntityService` — **no new ctor deps**.

**Both arrival orders converge on the same document-side write** — every interleaving is covered by exactly one path:
| Order | Linked by |
|---|---|
| capture-then-upload (canonical exists at save) | office side (resolve canonical → link new doc) |
| upload-then-capture (archive exists at capture) | capture side (find archive by message-id → link) |
| upload, then canonical created, then duplicate delivery | already linked at canonical first-create (capture find); the duplicate-delivery early-returns harmlessly |

## Tests (12; ADR-038 module boundaries)
- `CrossPathLinkTests` (9): Link — writes-when-unlinked / idempotent-skip-when-already-linked / non-fatal-on-throw /
  empty-ids-no-op; FindAndLink — finds-and-links / no-archive-returns-zero / already-linked-skips / non-fatal-on-query-throw /
  blank-message-id-no-op. Asserts the query keys on message-id + archive flag and the lookup points at the canonical.
- `OfficeDocumentPersistenceDedupTests` (+2): email-save with a captured canonical → the NEW document is linked;
  email-save never-captured → NO cross-path link attempted (strict mock) and the save still succeeds.
- `CommunicationIntegrationTests.InboundPipeline_UploadThenCapture_LinksExistingArchiveDocumentToCommunication` (seam):
  a pre-uploaded archive document + capture of the same email → the archive is linked to the new communication.
- **1139 Communication+Office green** (0 failed, +12); build 0-err; publish **48.30 MB compressed (Δ0.00)**; CVE clean; no package delta.

## Acceptance criteria status
| Criterion | Status |
|---|---|
| Captured comm + later archive of same email → linked (not duplicated) | ✅ office side + tests |
| Reverse order (archive first, capture later) → still linked | ✅ capture side + upload-then-capture seam test |
| Review surface resolves the pair to one email | ✅ single lookup folds them (Pillar E consumes; live end-to-end pending column) |
| Re-processed pair → no duplicate link (idempotent) | ✅ single-valued lookup + guard; tested |
| Non-fatal: link throws → capture/upload not failed | ✅ both helpers non-fatal; tested |
| Build green; ≤60 MB + delta; no new HIGH CVE | ✅ 0-err; 48.30 MB (Δ0.00); CVE clean |

## Step 9.5 (inline)
- **adr-check**: CLEAN. ADR-045 (extend — one lookup, comm stays the unit), ADR-010 (static helper, no new
  interface/DI), ADR-024/027 (gated schema task 029, managed solution), ADR-013 (no AI), ADR-038 (module-boundary
  mocks, behavioral assertions), §10 (Placement Justification; 48.30 MB Δ0.00; CVE clean; no package/endpoint/DI change).
- **code-review**: CLEAN. 0 Critical / 0 Warning. 2 documented non-blocking suggestions: (1) `LinkDocument…` overwrites
  if linked to a *different* comm — cannot occur (message-id → one canonical per task 021); (2) the content-dedup
  early-return (R-3) does not re-link — forward-only-consistent.

## GATED tail (operator go-ahead) = task 029
New lookup `sprk_document.sprk_linkedcommunication` → `sprk_communication` via `dataverse-create-schema` → managed
solution → `spaarkedev1`. Code ships safely behind it (every link is non-fatal via the generic seam → degrades until
the column exists). Sibling of gated schema tasks 023/027/028. Tracked in TASK-INDEX.
