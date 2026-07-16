# sdap-file-duplication-detector-r1

> **Status**: Investigation / analysis capture (pre-spec)
> **Created**: 2026-07-14
> **Origin**: Spun out of `spaarkeai-compose-r2` round-7 UAT item #8 ("file already in SharePoint → notify / open latest"). Determined to be outside compose-r2 scope; captured here for its own project lifecycle.

## Problem

Users get confused about the **document of truth** — when they want to edit a document, they can't tell which copy is the correct starting version. Compounding this, duplicate content pollutes the **AI Search index**, degrading retrieval accuracy. There is no de-duplication anywhere in the current upload pipeline.

## Goal (priority order)

1. **(c) Source-of-truth clarity** — a user always knows which document is authoritative before editing.
2. **(a) No duplicate records** — stop identical content from creating competing `sprk_document` rows.
3. **(b) No duplicate list entries** — the same document does not appear twice in document lists.

## Locked design decisions (owner, 2026-07-14)

- **Content-hash identity**: capture the SPE content hash at upload, persist to an indexed `sprk_document` column, backfill existing docs. **NOTE (post-research correction)**: the hash is **`quickXorHash`**, not `sha256Hash` — Microsoft deprecated `sha256Hash` on OneDrive/SharePoint driveItems ("don't use"); `quickXorHash` is the only content hash SPE returns. See `analysis.md` §3.
- **Two-tier detection (research upgrade)**: Tier 1 = exact `quickXorHash` equality; Tier 2 = **semantic near-duplicate** via the *existing* `documentVector3072` cosine-KNN "Find Similar" engine at a high threshold. Tier 2 substantially addresses goal (c)'s "similar-but-not-identical, which is current?" case that hash alone cannot. See `analysis.md` §4–5.
- **All three upload paths** covered: Assistant-upload (Redis), Assistant-persist (SPE), Compose-save (Dataverse).
- **On match → notify with actions**: (1) open the existing/canonical document; (2) proceed with the new file but reconcile via hash.
- **Reconciliation mechanic**: the "proceed anyway" copy is created hash-linked to the canonical (not re-indexed as separate content); on save it is **re-hashed**, and the moment its content diverges the duplicate-link clears and it graduates into its own indexed document. (Owner confirmed correct.)

## Documents

- [`analysis.md`](analysis.md) — investigation findings (current-state trace, SPE platform features, AI Search / find-similar capability) + solution approach.

## Explicit boundary

Content hash (Tier 1) solves **exact-duplicate** confusion + index accuracy. Semantic similarity (Tier 2, reusing the existing Find Similar engine) extends this to **near-duplicates** — "same document, slightly different bytes" and "which of two similar copies?" — which hash alone cannot detect. The remaining true follow-on is an explicit **version-lineage / "supersedes" chain** (durable authoritative-version tracking over time), a data-model concept that does not exist today — scoped separately. See `analysis.md` §Boundary.

## Next step

Investigation capture → owner review → promote to `spec.md` via `/design-to-spec` → `/project-pipeline`.
