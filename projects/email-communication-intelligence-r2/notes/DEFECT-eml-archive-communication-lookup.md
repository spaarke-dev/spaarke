# DEFECT — `.eml` archive + email-attachment documents fail to create (wrong lookup name) — ✅ RESOLVED 2026-08-13

> Found 2026-08-13 by the **Mode C end-to-end UAT** (real send → real capture). A seeded corpus could not have surfaced this. **Severity: High** — every captured email failed to archive its `.eml`, so the reconciliation "open email as sent" reading pane had no document to render.
>
> **RESOLVED** (commit `0026af5e1`): fixed 9 sites (6 writes + 3 queries) `sprk_communication` → `sprk_relatedcommunication`, added a regression test, redeployed. **Verified live**: post-fix sends return `archivedDocumentId` with no `archivalWarning`; inbound webhook captures now create archive docs linked via `sprk_relatedcommunication` (3 confirmed today vs the prior NULL-linked orphans).

## Symptom (observed live)
`POST /api/communications/send` returned HTTP 200 but with:
> `archivalWarning: "Email sent successfully but archival failed: Failed to create sprk_document record: 'sprk_document' entity doesn't contain attribute with Name = 'sprk_communication'..."`

The subsequent real inbound capture (webhook) created the `sprk_communication` row correctly (association engine even resolved it to **Ambiguous** — the golden behavior, on the real write path) **but its `.eml` archive silently failed** (best-effort/non-fatal, NFR-04).

## Root cause
`sprk_document` has **no `sprk_communication` lookup**. The canonical lookup is **`sprk_relatedcommunication`** (N:1 → `sprk_communication`), which task 029 chose to *reuse* (see `CrossPathLink.LinkedCommunicationAttribute = "sprk_relatedcommunication"`). Some code paths were updated to that name; the **document-create + archive-query paths were not** — they still reference `sprk_communication` / `_sprk_communication_value`, which no longer exists on `sprk_document`.

Evidence it is current + not a fresh regression: the wrong write dates to 2026-02-22 (`git blame` line 1020); archive docs stop at 2026-08-05 and those have **NULL `_sprk_relatedcommunication_value`** (orphaned); `sprk_document` metadata lists `sprk_relatedcommunication` + `sprk_email` but **not** `sprk_communication`.

## Affected sites
**Writes on `sprk_document` (use `["sprk_communication"]`, should be `["sprk_relatedcommunication"]`):**
- `Services/Communication/IncomingCommunicationProcessor.cs:1020` — inbound `.eml` archive **(highest impact — every captured email)**
- `Services/Communication/IncomingCommunicationProcessor.cs:917` — inbound attachment document
- `Services/Communication/CommunicationService.cs:471`, `:2076`, `:2250` — outbound attachment + `.eml` archive
- `Services/Communication/MessageAttachmentMaterializer.cs:147` — attachment materialization

**Queries on `sprk_document` (use `_sprk_communication_value`, should be `_sprk_relatedcommunication_value`):**
- `Spaarke.Dataverse/DataverseWebApiService.cs:1003` — **R2 `GetEmailArchiveByCommunicationAsync`** (task 064 E1c ingest resolver → the `communicationId` ingest path finds nothing)
- `Spaarke.Dataverse/DataverseServiceClientImpl.cs:811` — ServiceClient variant of the same R2 method (verify filter)
- `Services/Ai/Handlers/EmailDraftToolHandler.cs:641` — email-draft archive lookup

**Correct already (reference):** `CrossPathLink.cs:44`, `OfficeDocumentPersistence.cs:163/225`, `IncomingCommunicationProcessor.cs:323`. **Not affected** (different entity — those DO have `sprk_communication`): `CommunicationThreadReadService.cs:94/107` (queries `sprk_communicationattachment`/participant), and the many `["sprk_communication"]` writes on `sprk_emailreviewlog` / participant rows.

## Proposed fix (needs direction — spans r5-owned `Services/Communication` + R2 code)
1. **Code** (canonical, matches task-029 reuse decision): swap `sprk_communication` → `sprk_relatedcommunication` (writes) and `_sprk_communication_value` → `_sprk_relatedcommunication_value` (queries) at the 9 sites above. Verify the archive-doc create's *other* fields (`sprk_emailsubject`, `sprk_emailfrom`, `sprk_emailto`, `sprk_emaildate`, `sprk_emaildirection`) also exist on `sprk_document` (the create error only reports the first unknown attribute — re-run after the lookup fix to shake out any others).
2. Add a seam/integration test that creates a real `sprk_document` archive (not a mocked `IGenericEntityService`) so this can't regress silently — the mock is exactly why R2's contract tests passed while the real field name was wrong.
3. Re-run the Mode C send → confirm `archivalWarning` is gone + the archive doc links via `sprk_relatedcommunication` + the reading pane renders.

Alternative (schema): re-create a `sprk_communication` lookup on `sprk_document` — **rejected**: contradicts task 029's consolidation and would leave two parallel lookups.

## Test-email cleanup
Two rows created by this test (`sprk_correlationid` = `uat-e2e-20260813-01-conflict`): outbound `edb9c74b-1397-…`, inbound `1841349c-1397-…`. Delete when done.
