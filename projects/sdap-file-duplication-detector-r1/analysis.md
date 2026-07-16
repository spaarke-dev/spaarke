# File Duplication Detector — Investigation, Analysis & Solution Approach

> **Project**: sdap-file-duplication-detector-r1
> **Status**: Investigation capture (pre-spec)
> **Date**: 2026-07-14
> **Origin**: `spaarkeai-compose-r2` round-7 UAT item #8, determined out-of-scope and spun out.
> **Evidence base**: two grounded investigations on 2026-07-14 — (1) current-state upload-flow trace of this repo; (2) SharePoint Embedded platform research (Microsoft Learn) + repo trace of the existing AI Search / "Find Similar" capability. Researcher memory: `.claude/agent-memory/researcher/spe-dedup-content-identity-2026-07.md`.

---

## 1. Problem statement

Users cannot reliably tell **which document is the source of truth** when they go to edit. They may open a stale copy, or two near-identical copies exist and it's unclear which is authoritative. Compounding it, duplicate content pollutes the **AI Search index**, degrading retrieval accuracy (the same content answers a query from two entries; "Find Similar" surfaces a document's own duplicate as a match).

Root cause: **there is no de-duplication anywhere in the upload pipeline, and no content identity is captured** on which to build one.

### Goals (owner priority order)
1. **(c) Source-of-truth clarity** — before editing, the user knows which document is authoritative. *(highest)*
2. **(a) No duplicate records** — identical content does not spawn competing `sprk_document` rows.
3. **(b) No duplicate list entries** — the same document does not appear twice in lists.

---

## 2. Current-state investigation (what exists today)

### 2.1 Three unconnected upload paths — no shared ingest
There is no single "ingest" service. Three paths behave completely differently:

| Path | Endpoint / entry | Lands in | Creates `sprk_document`? | Notes |
|---|---|---|---|---|
| **A. Assistant upload** | `POST /api/ai/chat/sessions/{id}/documents` (`ChatDocumentEndpoints.UploadDocumentAsync`) | **Redis only** (`doc-upload-text` / `doc-upload-binary`) + AI-Search `spaarke-session-files` | ❌ No | Ephemeral, ~4h TTL. `documentId` is a chat-scoped GUID, not a Dataverse id. |
| **B. Assistant → persist** | `PersistDocumentAsync` | **SPE** (`chat-uploads/{filename}` in a **shared staging container**) | ❌ No | Bytes land in SPE; still no Dataverse row. Redis idempotency on `(sessionId, documentId)`. |
| **C. Compose save** | `POST /api/compose/documents/create-on-save` (`ComposeService.SaveAsync` → `PromoteIfEphemeralAsync`) | **SPE** (client-supplied container) **+ Dataverse** | ✅ Yes | The only path that mints a `sprk_document`. Container comes from the client (user BU); no server BU→container resolver. |

Implication: covering "all paths" (owner requirement) means detection must work at **three different layers** — before persistence (A, Redis), at SPE-write (B), and at Dataverse-create (C).

### 2.2 No content identity is captured
- **No content hash anywhere.** `sha256Hash`/`quickXorHash`/`crc32` from Graph's `file.hashes` facet are **never selected or read**; no hash is computed over uploaded bytes. `sprk_document` has **no hash column** (`Spaarke.Dataverse/Models.cs`).
- The only identity is `sprk_graphitemid` (SPE drive-item id), backed by a **unique alternate key `sprk_graphitemid_uk`**.
- SHA-256 *is* used in the codebase — but only as an **embedding-cache key** (`EmbeddingCache.cs`, keyed by chunk text to save OpenAI calls) and for audit hashing. Never for duplicate detection.

### 2.3 The only "dedup" today is drive-item-id idempotency
`TryFindDocumentByGraphItemIdAsync` (`ComposeService.cs`) does an alt-key lookup before creating a row. This **only** prevents two rows for the *same SPE item* (repeated saves). It **cannot** detect that a *different* SPE item holds identical or near-identical content — which is exactly item #8's case.

### 2.4 Index behavior
- AI-Search key = `{documentId}_{suffix}_{chunkIndex}`. Re-indexing a `documentId` deletes prior chunks then re-uploads (`MergeOrUpload`) → **one logical copy per Dataverse document**.
- **Two documents with identical content = two full sets of index entries.** No content-hash dedup at ingest. They are only detectable via vector similarity (~1.0 cosine to each other).

### 2.5 Matter/container scoping
- No per-matter folder structure. Assistant-persist uses a **generic shared staging container**; Compose-save uses a **client-supplied container** (user's BU). Matter/regarding lookups on `sprk_document` are optional and **usually unset at create time**.
- So a "does this already exist?" query has no reliable per-matter scope to hang on today; the queryable identity is `sprk_graphitemid` only (no filename or hash index).

---

## 3. SharePoint Embedded platform capability analysis

*(Requested: flesh out SPE features. Source: Microsoft Learn, 2026 docs; see §Sources.)*

### 3.1 Content hash — `quickXorHash` only (⚠️ design-critical correction)
- The `hashes` facet exposes `quickXorHash`, `crc32Hash`, `sha1Hash`, `sha256Hash`.
- **`sha256Hash` is deprecated** — Graph v1.0 `hashes` doc states verbatim *"This property isn't supported. Don't use."* **Do not design around it.** (This corrects our earlier assumption.)
- **`quickXorHash` is the only guaranteed content hash** for the OneDrive-work/school substrate SPE sits on (`crc32`/`sha1` are consumer-OneDrive only → treat as absent in SPE).
- It **is** a valid content-identity key: 160-bit, base64, deterministic (same bytes → same hash). The **algorithm is public** (`learn.microsoft.com/onedrive/developer/code-snippets/quickxorhash`), so we can **compute it ourselves client- or server-side to pre-check *before* upload**, and read it from SPE after upload.
- **Request shape**: `GET /drives/{driveId}/items/{itemId}?$select=file` → `file.hashes.quickXorHash` (it lives inside the `file` facet; there is no standalone `hashes` select).
- **⚠️ Unconfirmed gap**: Microsoft does not document, for SPE specifically, *when* quickXorHash is populated (immediately vs. async for large/chunked uploads) or a size cap. **Requires a spike before we rely on "hash available synchronously on upload."** (See §9.)

### 3.2 Versioning — supported, but no per-version hash
- `/versions` **works on SPE** (`GET /drives/{driveId}/items/{itemId}/versions`, requires `FileStorageContainer.Selected` + container-type perms). Restore via `.../versions/{versionId}/restore`. **500 versions/file** cap.
- Each `driveItemVersion` has `id` ("3.0"), `size`, `lastModifiedBy/DateTime` — **no per-version hash**. To diff versions you'd download + hash yourself.
- **Relevance**: versions answer "history of *one* item," **not** "which of two *separate* items is authoritative." The source-of-truth decision across separate items is a **business decision that belongs in Dataverse**, not something SPE versioning solves.

### 3.3 Container model & scoping
- Each SPE container is a Graph **drive** → per-container is the natural, efficient dedup boundary. Spaarke already uses a tiered container model (BU + per-secure-matter container).
- **Query a container by custom metadata** (not just path): `GET /drives/{containerId}/items?$expand=listitem($expand=fields)&$filter=listitem/fields/{Column} eq 'x'` (supports `eq`, `startswith`, `$orderby`). Microsoft Search API also spans containers.
- Backfill limits: 30M files & 25 TB per container; throttling in "resource units" (~3k/min per container, 12k/min per app per tenant, 600/min per user).

### 3.4 Custom columns — the SPE-native dedup lever (**GA January 2026**)
- **fileStorageContainer column APIs went GA in v1.0 (Jan 2026).** We can define a container column (e.g. `sprk_canonicalhash` or `sprk_canonicaldocid`), stamp it per item (PATCH listItem fields), and **query by it** via the `$filter` above.
- This enables efficient **within-container** dedup lookups directly against SPE — complementary to the Dataverse authority index (which spans containers).

> **⚠️ Clarification (owner point, 2026-07-14): there is NO container-per-document structure.** The SPE "column" is a schema element defined on the **container (drive)**, but the *value* is carried **per file (driveItem)** — like a SharePoint list column. Our containers are **BU- / matter-scoped and hold many documents**, so a container-column `$filter` finds duplicates only **among files in that one container**; it cannot span containers, and it is emphatically not "one hash identifying a whole container." Because a document can also legitimately live in different containers (e.g. staging vs. matter), the container column is a **fast local pre-check only**. The **authoritative, cross-container identity is `sprk_document.sprk_canonicalhash` in Dataverse** (§5.3). If the per-item SPE column proves low-value given our container layout, it can be dropped and Dataverse used as the sole index — decide during spec.
- **Knowledge-repo correction to file**: `knowledge/sharepoint-embedded/NOTES.md` currently says driveItem metadata is "read-only." True for system fields/hashes, but **custom columns are writable + queryable** — flagged as a follow-up doc fix (§10).

### 3.5 Delta / change tracking — use for backfill + ongoing capture
- `GET /drives/{containerId}/root/delta` works on SPE. Page via `@odata.nextLink` → `@odata.deltaLink`; `?token=latest` gets a deltaLink without full enumeration. Handle `410 Gone` / `resyncRequired`.
- **Gotcha**: delta omits `ctag` and won't reliably carry hashes for OneDrive-Business drives → use delta to **enumerate what changed**, then `GET {item}?$select=file` per changed item to capture quickXorHash.

### 3.6 No native logical dedup
- **Confirmed**: two uploads of identical bytes create **two distinct logical driveItems**. SharePoint does invisible BLOB-level single-instancing at the backend but never collapses logical items. **Dedup is entirely our responsibility.**

### 3.7 Other identity/change signals
- `id` — immutable item identity (survives rename/move/content edit); not a *content* identity.
- `eTag` — changes on content **or** metadata change.
- `cTag` — changes **only** on content change → the correct "did the bytes change" token (but omitted by delta). Use `If-Match: {eTag}` for optimistic-concurrency writes (412 on race).

---

## 4. AI Search / "Find Similar" capability analysis

*(Requested: can our AI Search index / semantic find-similar help, perhaps with enhancements? Answer: **yes, substantially** — the near-duplicate engine already exists.)*

### 4.1 We already have a document-level semantic similarity engine
- **Per-document embeddings exist**: `documentVector3072` (3072-dim, `text-embedding-3-large`, HNSW **cosine**) on `spaarke-files-index`, stored `retrievable:true` specifically to seed similarity search. Computed as the L2-normalized average of chunk vectors (`RagService.ComputeDocumentVector` / `EmbeddingMigrationService.ComputeAverageVector`).
- **"Find Similar" (`VisualizationService`)** runs KNN vector search over `documentVector3072` with a **score threshold** and dedupes by document id (`GET /api/ai/visualization/related/{documentId}`). The similarity number per edge is the cosine vector score.
- **`POST /api/ai/visualization/related-from-content`** already **embeds an uploaded file on the fly and finds neighbors** — i.e. this is *essentially already* "is this incoming file a duplicate of anything indexed?" It just isn't wired to the upload flow or tuned as a duplicate gate.
- **`SemanticSearchControl`** (`POST /api/ai/search`) has reusable hybrid-search + `minScore` threshold plumbing and a `SimilarityBadge` (high/med/low at 80%/60%).

### 4.2 What's missing
- **(a)** exact/byte content-hash dedup (nothing computes/stores a content hash for dedup);
- **(b)** a **pairwise** "doc A vs doc B = X% similar" API (today it's 1-to-many KNN + threshold);
- **(c)** any **automatic gate at ingest** ("this upload duplicates an existing doc").

### 4.3 Key insight — the "near-duplicate" boundary is mostly already solvable
Earlier I framed near-duplicates (same document, slightly different bytes — the DOCX-re-export and "which of two similar copies is current?" cases) as an unsolved follow-on. **That was too pessimistic.** The `documentVector3072` KNN primitive already detects semantic near-duplicates; a duplicate gate is a **high cosine threshold** (≈0.95+) over the existing engine plus a small amount of new wiring (embed-before-persist, or compare-on-index). This is the single biggest efficiency in the whole design: **we build the exact-match tier from scratch (hash) but the near-match tier is 80% reuse.**

---

## 5. Solution approach

### 5.1 Two-tier detection
| Tier | Signal | Catches | Build |
|---|---|---|---|
| **Tier 1 — Exact** | `quickXorHash` equality | Byte-identical re-uploads (the common "I uploaded this exact file already" case) | New: compute/capture hash on all paths; store indexed on `sprk_document` + stamp SPE `sprk_canonicalhash` column |
| **Tier 2 — Near** | `documentVector3072` cosine KNN ≥ threshold (~0.95+) | Same document, non-identical bytes; "which of two similar copies?" | **Reuse** `VisualizationService` / `related-from-content`; add a high-threshold "duplicate?" mode + ingest wiring |

Tier 1 is deterministic and cheap → runs first, ideally **before** the write (compute quickXorHash client/server-side, look up). Tier 2 runs when Tier 1 misses but content may still be a semantic duplicate (needs the text/embedding, so it runs at/after index time or on-the-fly via `related-from-content`).

### 5.2 Capture (all three paths)
- Compute/read **quickXorHash** and persist to a new indexed `sprk_document.sprk_canonicalhash` (the **primary, cross-container authority key**). Optionally also stamp a **per-item** SPE container column of the same name for fast *within-container* pre-checks — but see §3.4: this is a local optimization, not the source of truth, and may be dropped given our multi-document container layout.
- **Path A (Assistant/Redis)**: compute quickXorHash over the buffered bytes at upload → store in the Redis metadata so a match is detectable **before** the file is ever persisted. (We already hold the bytes in memory here.)
- **Path B (Assistant-persist / SPE)** and **Path C (Compose-save)**: read `file.hashes.quickXorHash` via `$select=file` after write (or compute pre-write for a pre-check), persist to Dataverse + SPE column.

### 5.3 Authority index
- **Dataverse `sprk_document` is the cross-container authority** (SPE per-container queries can't span containers; "which copy is authoritative" is a business decision that belongs in Dataverse). `sprk_canonicalhash` indexed for fast lookup; a canonical/duplicate link relationship (see 5.5).
- SPE container column = fast **within-container** pre-check; Dataverse = **cross-container** truth.

### 5.4 Detect + notify + act (UX)
On a Tier-1 or Tier-2 hit, **notify** (never silently auto-open) and present the existing/canonical document with two actions:
1. **Open the existing document** — abandons the new upload, opens the authoritative copy. Directly serves goal (c).
2. **Proceed anyway (hash-linked)** — creates the new record but marks it duplicate-of the canonical; **does not** re-index identical content as a separate document (preserves index accuracy → goals a/b).

UX aggressiveness scales with tier confidence: Tier-1 exact match = high-confidence "this is a duplicate"; Tier-2 = "this looks like <canonical> (NN% similar) — is it the same?" (softer language, since near-match can be a legitimately different document).

**Tier-2 results ARE part of the duplicate-notify surface (owner requirement, 2026-07-14).** The notification does not only report an exact-hash collision — when Tier-1 misses but Tier-2 finds near-matches, the notify lists the **top near-duplicate candidates with their similarity score** (reusing the Find Similar edge score / `SimilarityBadge`), so the user can pick "open <candidate> instead" or proceed. This makes the near-match assessment actionable at the exact decision point, rather than a silent backend filter. Requires the Tier-2 engine to be robust enough that its candidate ranking is trustworthy in the notify — see §9 item 3a.

### 5.5 Reconciliation mechanic (owner-confirmed)
- The "proceed anyway" copy is created **hash-linked** to the canonical (via a duplicate-of link — reuse `sprk_parentdocument` or a dedicated "same-content-as" reference; TBD in spec).
- On save, the copy is **re-hashed**. The moment its content **diverges** from the canonical, the duplicate-link is **cleared** and it **graduates into its own indexed document**.
- Net: "proceed anyway" is never a permanent competing source of truth — it's a linked shadow that auto-promotes to a distinct document exactly when it becomes genuinely different. (Owner: "sounds correct.")

### 5.6 Index accuracy
- While hash-linked, the duplicate's content is **not** indexed as a second entry (one index entry per unique content → retrieval accuracy).
- **Backfill/cleanup**: a one-time job (SPE **delta** enumeration → `GET item ?$select=file` for hash) captures quickXorHash for existing docs, collapses existing exact dupes, and — via Tier-2 KNN — surfaces existing *near*-dupe clusters for review. Existing duplicate **index entries** are de-duplicated during this pass.

---

## 6. Boundary — what this does and does not solve

- **Solves**: exact-duplicate confusion (Tier 1) + index accuracy (both tiers) + semantic near-duplicate detection (Tier 2, reusing Find Similar). This covers the large majority of the source-of-truth confusion in goal (c), plus all of (a) and (b).
- **Does NOT solve (true follow-on)**: a durable **version-lineage / "supersedes" chain** — tracking, over time, which of several genuinely-different versions is the *authoritative current* one. The data model has no version-chain concept today (`ParentDocumentLookup` is for email-attachment parentage, not versioning; SPE `/versions` is per-item history only). Tier 2 can *surface* "these are similar," but declaring one authoritative over time is a separate business/data-model concern. **Scoped out of r1; candidate for r2.**

---

## 7. Work surface & governance (for the eventual spec)

| Surface | Change | Governance trigger |
|---|---|---|
| **Dataverse schema** | New indexed `sprk_document.sprk_canonicalhash` + duplicate-of link relationship | Data-model change; `docs/data-model/` update |
| **SPE** | New container column `sprk_canonicalhash` (GA column API); `$select=file` reads; delta backfill job | Graph/SPE integration |
| **BFF** | Hash capture on 3 upload paths; a duplicate-check service; Tier-2 threshold mode on `VisualizationService`; ingest gate | **§10 BFF Hygiene** — Placement Justification + publish-size check + hot-path conflict-check (ComposeEndpoints, ChatDocumentEndpoints, VisualizationService, RagService all touched) |
| **AI Search** | Dedup-by-hash at ingest; near-dupe threshold; existing-dupe cleanup in backfill | Index accuracy — the "accuracy in the index" half of the goal |
| **Client** | Notify dialog with 2 actions, wired into all upload entry points | UX across Assistant + Compose |
| **Backfill** | One-time delta-driven hash capture + dupe collapse (Dataverse rows + index entries) | Batch job; throttling-aware (resource units) |

Reuse-first (per CLAUDE.md §11): Tier 2 **extends** `VisualizationService` rather than adding a new similarity engine; the duplicate-of link should reuse/extend existing document-relationship fields rather than introduce a parallel concept.

---

## 8. Recommended architecture shape (one-paragraph summary)
Content-identity key = **quickXorHash** (computed pre-upload for a cheap pre-check; read from `$select=file` post-upload). Stamp it on both the **SPE container column** (fast within-container lookup) and **`sprk_document`** (cross-container authority + index). Detect in two tiers — exact hash, then existing-vector-KNN near-match at a high threshold. On a hit, **notify** with open-canonical / proceed-hash-linked; reconcile by re-hash-on-save that auto-graduates a diverged copy. Backfill via SPE **delta** enumeration. Version-lineage is explicitly deferred.

---

## 9. Open questions & required spikes
1. **quickXorHash timing/size on SPE (critical)** — confirm the hash is present + stable immediately after upload, including large (>250 MB chunked) uploads and arbitrary binary types. Determines whether Tier-1 can gate *before* vs *after* persistence. → spike: upload → immediate `GET ?$select=file`.
2. **Container column round-trip** — verify `sprk_canonicalhash` create→PATCH→`$filter` works end-to-end on **v1.0** (not beta).
3. **Tier-2 threshold calibration** — what cosine cutoff cleanly separates "duplicate/near-duplicate" from "merely related"? (Find Similar's default threshold is tuned for *relatedness*, not duplication — likely needs a higher, separately-tuned value.)
   - **3a. Tier-2 robustness + enhancement assessment (owner requirement, 2026-07-14)** — before wiring Tier-2 into the duplicate-notify (§5.4), assess how robust the *current* Find Similar engine is **for the duplicate-detection use case specifically**, and whether enhancements are warranted. Evaluate: (i) reliability of `documentVector3072` population across all ingest paths — the trace flagged that the **discovery-index path may leave `documentVector3072` unset**, and legacy docs required the `EmbeddingMigrationService` backfill, so coverage gaps would produce false-negatives (a real duplicate not surfaced); (ii) whether the document-level vector (a normalized *average* of chunk vectors) is discriminative enough at the top of the range, or whether a **pairwise** cosine / max-chunk-similarity / re-ranker pass is needed to trust "these two are ~the same"; (iii) latency of an on-upload KNN so the notify isn't sluggish; (iv) candidate-ranking quality good enough to show the user a trustworthy "top near-duplicate" list. Output: a go/no-go on reuse-as-is vs. a scoped enhancement (e.g. add a pairwise-similarity endpoint, backfill missing document vectors, add a duplicate-tuned threshold band).
4. **Reconciliation link choice** — reuse `sprk_parentdocument` vs. a dedicated "same-content-as" reference (avoid overloading email-attachment parentage).
5. **Assistant-path scope** — since Path A is Redis-ephemeral (no Dataverse row), decide whether its dedup checks against (a) other files in the same session only, or (b) the full persisted corpus (requires the hash lookup + possibly on-the-fly embedding).

## 10. Follow-ups (housekeeping)
- Update `knowledge/sharepoint-embedded/NOTES.md`: custom columns are **writable + queryable** (GA Jan 2026); `sha256Hash` deprecated → **quickXorHash only**. (Researcher already recorded this in agent memory.)

---

## 11. Next step
Owner reviews this capture → promote to `spec.md` via `/design-to-spec` → `/project-pipeline`. The spec should lead with the two spikes in §9 (items 1 & 3 are the load-bearing unknowns) before committing the schema + ingest-gate work.

---

## Sources
**SPE / Graph (Microsoft Learn, 2026):** hashes resource type (sha256 deprecation / quickXorHash) · file resource + Get driveItem (`$select=file`) · driveItem list-versions (SPE support; no per-version hash) · driveItem delta (ctag omission, tokens, 410) · What's-new in SharePoint Embedded (columns GA Jan 2026) · SPE Limits & Calling Patterns (500 versions, 30M files, throttling) · Search SPE containers + Container Metadata tutorial (custom-column `$filter`).
**Repo (this worktree):** current-state trace — `ChatDocumentEndpoints.cs`, `ComposeService.cs`, `ComposeEndpoints.cs`, `Spaarke.Dataverse/Models.cs`, `SpeFileStore.cs`. Find-Similar / index — `VisualizationService.cs`, `SemanticSearchService.cs`, `RagService.cs`, `RagIndexingPipeline.cs`, `infrastructure/ai-search/spaarke-files-index.json`, `OpenAiClient.cs`, `EmbeddingCache.cs`.
