# R8 scope add — Durable session files (align file availability to the 90-day History window)

> **Added**: 2026-08-19 by operator decision during `spaarkeai-compose-r7` UAT.
> **Origin**: R7 UAT point 1b ("when a History session reopens, its uploaded files don't reload").
> **Owner ruling**: file availability on reopen MUST align with the **90-day History retention window**
> (NOT the current ~24h). A reopened conversation should be able to use its files for as long as the
> conversation itself survives in History.

## The problem (grounded)

Assistant chat-session uploads have **no durable byte copy**. They live in two places with **mismatched
retention**:

| Layer | What | Store | Lifetime |
|---|---|---|---|
| File **metadata / manifest** | fileId, fileName, contentType, sizeBytes, `searchDocumentIdsCsv`, enrichment | **Cosmos** — `StoredSession.UploadedFiles` (`sessions` container) | **90 days** (matches History) |
| File **searchable content** (chunks used for recall/RAG) | extracted text chunks | **Azure AI Search** index `spaarke-session-files` | **~24h** |
| Raw file **bytes** | — | **nowhere** (NOT SPE, no blob) | ephemeral |

So a conversation stays in History for 90 days, but the **content that makes its files usable is gone after
~24h**. The mismatch is the defect.

**Why 24h**: `SessionFilesCleanupJob` evicts every `spaarke-session-files` chunk whose session's **Redis key**
no longer exists, and the Redis session key has a **24h sliding TTL**. Cosmos (manifest) and Dataverse
(transcript) survive 90 days; the AI-Search chunks do not.

**Recall is scoped server-side by the persisted manifest** (`RecallSessionFileHandler` +
`SessionFileTextSource` read `UploadedFiles[].SearchDocumentIdsCsv`), so recall works on reopen **as long as
the chunks still exist**. Nothing client-side needs to change for recall — the gap is purely retention of the
content the manifest points at.

## What R7 already shipped (the best-effort interim — do NOT rebuild)

R7 (`feat(spaarkeai): re-attach uploaded files on History reopen (best-effort 24h)`, commit on
`work/spaarkeai-compose-r7`) delivered the **client-only** best-effort layer:
- On History reopen, `ConversationPane.handleSelectHistorySession` fetches `GET /sessions/{id}/restore`,
  stages the uploaded-files manifest, and re-renders the attachment chip for the reopened session.
- `FilesAttachedIndicator` (`AttachedFileSummary.available`) shows a dimmed **"no longer available"** chip
  when content is past the ~24h window (inferred client-side from freshest-message age).

R8 replaces the 24h ceiling with true durability so the "no longer available" state effectively disappears
within the 90-day window.

## R8 requirement

**Uploaded session files must remain fully usable (recall + re-attach) for the full 90-day History window**,
so a reopened conversation behaves the same on day 1 and day 60.

### Candidate design directions (for the R8 investigation to decide)

1. **Durable content store + rehydrate** — persist a durable copy of each upload (SPE container or blob) at
   upload time; on reopen (or lazily on first recall) **re-index** into `spaarke-session-files` from the
   durable copy if the chunks were evicted. Keeps the hot index small; content survives 90 days in the
   durable store. Likely aligns with the SPE-first architecture.
2. **Decouple eviction from the 24h Redis TTL** — retain the AI-Search chunks for 90 days (session-scoped,
   keyed off the Cosmos manifest's own 90-day life rather than the Redis key). Simpler, but grows the search
   index and its cost; no durable byte copy (can't re-open the raw file, only recall its text).
3. **Hybrid** — durable bytes (SPE/blob) for 90 days + on-demand re-index; chunks stay hot only while active.

### Open questions R8 must answer
- Where do durable bytes live — **SPE** (consistent with Compose/DMS documents) or blob? Tenant isolation +
  cost + GDPR-erasure implications (memory/GDPR already treats `memory-items` as erasable — mirror that).
- Re-index cost/latency on reopen vs. keeping chunks hot for 90 days (index size/cost tradeoff).
- Availability signal: once durable, replace the R7 client 24h heuristic with an authoritative
  `contentAvailable` (or drop it entirely if content is guaranteed for 90 days).
- Interaction with `SessionFilesCleanupJob` — it must stop evicting within the 90-day window (or evict only
  the hot index, never the durable copy).

## Code inventory (starting points for R8)
- Manifest + retention mismatch: `SessionPersistenceService.UpdateUploadedFilesAsync`, `StoredUploadedFile`
  (`SearchDocumentIdsCsv`), `StoredSession.UploadedFiles`.
- Recall (server-side scoping, already correct): `RecallSessionFileHandler`, `SessionFileTextSource`.
- 24h eviction (the thing to change): `SessionFilesCleanupJob` (evicts on Redis-key absence; Redis 24h TTL).
- Restore surface: `SessionRestoreService.RestoreSessionAsync` + `GET /sessions/{id}/restore`
  (`ChatEndpoints.cs`); DTO `SessionRestoreUploadedFileDto` (extend with authoritative availability if kept).
- R7 client interim: `ConversationPane.handleSelectHistorySession`, `restoredAttachmentFiles`,
  `ConversationPaneChrome.FilesAttachedIndicator` (`AttachedFileSummary.available`).

## Relationship to R8's fidelity mandate
This is a **retention/durability** item, distinct from the render-on-save **fidelity** work — but it belongs
to the same Compose/session surface and was surfaced by the same R7 UAT round. Treat as a parallel R8 track
(or a fast-follow), sequenced by the R8 investigation.
