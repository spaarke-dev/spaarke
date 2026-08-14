# Task 020 — unique key on sprk_communication.sprk_internetmessageid — COMPLETE (2026-08-06)

## Result
- **Alternate key**: `sprk_InternetMessageIdKey` (logical `sprk_internetmessageidkey`), UNIQUE, over the single
  attribute `sprk_internetmessageid`. **Status: Active** (verified in spaarkedev1).
- **Column change**: `sprk_internetmessageid` MaxLength reduced **1000 → 850** (operator) to fit the SQL unique-
  index 1700-byte limit (850 × 2 bytes = 1700). Longest existing value is 118 chars, so no data was truncated.

## Duplicate-key error class (for task 021 catch-and-reconcile)
A duplicate insert against the active key returns:
- **HTTP 412** (Precondition Failed)
- **error.code `0x80060892`**
- message: *"Entity Key Internet Message Id Key violated. A record with the same value for Internet Message Id
  already exists. A duplicate record cannot be created…"*

Task 021 must catch **HTTP 412 / `0x80060892`** on the create path and reconcile to the existing canonical row
(rather than surfacing an unhandled error). Nulls do NOT collide (Dataverse excludes null-valued rows from the
unique constraint) — only messages carrying an internet-message-id are deduped, which is correct.

## Pre-flight data cleanup (operator-approved 2026-08-05)
The key initially FAILED to activate: dev held **13 duplicate non-null message-id pairs** (117 non-null values,
104 distinct; 93 nulls) — duplicated R1 test emails ("Test Email with Attachments N", "PAT-942665…", "LITG-
119896…"). With operator approval, the **13 redundant rows** (the later-created of each pair) were deleted, then
the key was reactivated (`ReactivateEntityKey`) and reached **Active**. No production/non-test data mutated.

## Nulls note for 021/023
93 of 210 `sprk_communication` rows have a NULL `sprk_internetmessageid` (outbound/captured-without-header). This
is expected and permitted under the unique key.

---

## Task 023 note (sprk_document.sprk_canonicalhash)
- Column created by operator. **Backfill: forward-only** (owner decision 2026-08-05) — no historical reprocessing.
- **Index caveat**: the maker portal cannot create a plain non-unique secondary index, and `canonicalhash` can't
  be a unique key (duplicates are expected — that's the dedup signal). The column is Searchable; if the task-024
  detector's `sprk_canonicalhash eq '<hash>'` lookup is slow at scale, escalate for a platform index then.
