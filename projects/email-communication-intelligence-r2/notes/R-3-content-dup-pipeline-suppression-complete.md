# R-3 — Office-path content-dedup: full pipeline suppression + transient-blob cleanup (FR-C3) — CODE COMPLETE (2026-08-06)

Rigor FULL · opus·high. Owner directive (2026-08-06): "what is the functionally robust and technically correct
solution? take that path." This note records that solution.

## The finding that reshaped R-3

The remediation plan framed R-3 as "delete the orphaned dup blob in `ReconcileAsync`." **That premise was
false.** Tracing the caller: after `OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync` suppresses the
second document on a content-dup, `OfficeService` **still queues a finalization job**
(`QueueUploadFinalizationAsync`) that consumes the same `(driveId, itemId)` to create Email/Attachment
artifacts + run AI — *even on a dup*. So the blob is NOT a clean orphan at suppress-time; deleting it in
`ReconcileAsync` would break finalization. The naive delete was a correctness regression, not a cleanup.

## The robust correct solution

A byte-identical content duplicate must suppress the **whole** duplicate pipeline, not just the document:

1. **No second `sprk_document`** — already done (task 024 / R-2).
2. **No redundant finalization** — the canonical already carries its Email/Attachment artifacts + AI on
   identical bytes; re-running would only duplicate artifacts on the canonical and re-spend AI. **Skipped.**
3. **No spurious membership event** — the dup uploader is NOT the canonical's owner; publishing an
   "Added owner" event for the canonical would be wrong. **Skipped** (falls out of the early return).
4. **Transient blob cleaned up** — now truly unreferenced (nothing downstream consumes it). **Deleted**
   (best-effort).
5. **User informed** — the detector already NOTIFIED the user of the canonical (never silent).

## What shipped

- **`OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync`** now returns
  `(Guid DocumentId, bool WasContentDuplicate)` (was `Guid`). On a dup: `WasContentDuplicate = true`,
  `DocumentId = canonical`.
- **`OfficeService`** (the save orchestrator): on `WasContentDuplicate` → delete the transient blob, mark the
  job `Completed` (phase `DeduplicatedToExisting`), and **early-return** — skipping finalization + the
  membership event. Non-dup path unchanged.
- **`OfficeStorageUploader.DeleteFromSpeAsync(driveId, itemId)`** — new best-effort SPE-facade delete (ADR-007),
  non-fatal (a failed cleanup never fails the save). Only ever called with the item THIS request just
  uploaded — never the canonical's own item (safe by construction).
- **`SpeFileStore.DeleteFileAsync`** made `virtual` (module-boundary test seam idiom — cf. `UploadSmallAsync`).
- Response: `Duplicate` is deliberately left `false` — that field means *idempotent job replay*, not content
  dedup; overloading it would confuse clients. The content-dup signal is the job phase + the notification.

## Tests
- `OfficeDocumentPersistenceDedupTests` (updated): assert the tuple — dup → `(canonical, WasContentDuplicate=true)`; first upload → `(newId, false)` + hash stamped.
- `OfficeStorageUploaderDeleteTests` (new, 2): delete routes through the facade; a throwing delete is non-fatal (returns false, never throws).
- `OfficeService` orchestration (the skip-finalization early return) is NOT unit-tested — OfficeService has no unit harness (a heavy job/storage orchestrator; integration-tested per the codebase pattern). The two testable seams (persistence tuple + uploader delete) are covered; the dup branch is a minimal, obviously-correct early return.
- Build 0-err; **1013 Office+Compose+ContentDedup green** (4 R-3 + 18 R-2). CVE clean; no package delta (publish materially unchanged from R-2's 48.30 MB).

## Boundary with FR-C2 (task 022) — explicit, not buried
R-3 makes the dup pipeline *suppress correctly* (no redundant document/artifacts/AI, blob cleaned). It does
**not** record the duplicate's delivery/recipient/uploader **context** on the canonical — that is **FR-C2
"Context-merge on duplicate" (task 022)**, additive on top of this and unchanged in scope. Today a suppressed
office-path dup records no context on the canonical; task 022 will add it. Flagged here so the gap is tracked,
not lost.

## Placement Justification (§10) + §11
Extends `OfficeStorageUploader` (adds a delete to the existing SPE-storage helper) + `OfficeService`
(orchestration) + `OfficeDocumentPersistence` (return shape) in place; no new service/package/Graph surface in
callers (delete via the `SpeFileStore` facade, ADR-007). §11: `DeleteFromSpeAsync` — Existing: `UploadToSpeAsync`
on the same helper (upload, not delete); Extension: added to the same class (the office↔SPE storage owner);
Cost-of-doing-nothing: transient duplicate blobs accumulate in SPE forever (a slow storage leak).
