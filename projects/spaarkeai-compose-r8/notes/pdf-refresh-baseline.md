# FR-A09 — the PDF-sourced second save, across a page refresh

> Task 044. **Measured 2026-08-23** through the wire before any code was written, per the POML's
> instruction to verify across a real refresh rather than two saves in one session.

---

## What the POML said, and what was actually true

> *"PDF-sourced documents do not track their synthesized file's version coordinates, so after a page
> refresh, save two cannot resolve its baseline and falls back to a full rebuild."*

The diagnosis was right about the missing tracking. The **failure it produces** is worse than a full
rebuild, and naming it correctly changed the fix.

Reproduced (`ComposePdfRefreshBaselineSeamTests`), before any change:

```
open PDF  → synthesized docx, sourceFormat=pdf, versionId=null
edit + save → mints .docx item D1, record R1        ✔ the edit is in D1
── REFRESH ──  client loses: retained bytes · re-targeted documentRef · transientKey
re-open PDF → documentSpeId = spe-item-pdf-…        ✘ the PDF, projected a SECOND time
```

The user is shown the PDF again. Their saved work is not lost — it is **invisible**, sitting in a
Word document they have no pointer to. Their next save mints a **second** document, because the
transient-key dedup is keyed on a per-mount key that `composeIdentity.ts` deliberately never persists.

So the honest statement of the defect is: *one PDF silently becomes two Word documents, and the
user's earlier work is in the one they are not looking at.*

## Why that ruled out the smaller fix

The obvious cheap fix is save-side: derive the transient key from the source PDF instead of the mount,
and the existing dedup replaces D1 in place. One document. No load-path change.

**It is wrong, and worse than the bug.** After a refresh the client's model is the fresh PDF
projection. Rendering that into D1 overwrites the first save's edit with PDF-again content — trading a
visible duplicate for silent data loss. The user cannot even see it happen.

The fix therefore has to be at **load**: hand back the document that already exists. Once it does,
save two is an *ordinary* imported save — real version coordinates, baseline resolves, untouched
blocks clone — with no PDF-shaped special case anywhere in the save path.

---

## The mechanism

Two `IDistributedCache` keys (ADR-009 — the refresh is a different request, possibly a different
instance, which is exactly the boundary the ADR is about). Both best-effort in both directions.

| Key | Written | Read | Holds |
|---|---|---|---|
| `sdap:compose:pdf-session:{sessionId}` | PDF load | save | the source PDF's coordinates |
| `sdap:compose:pdf-derived:{driveId}:{speId}` | PDF-sourced save | next load of that PDF | the Word document it became |

TTL 30 days on both. Expiry degrades to the pre-044 behavior, never to an error.

### Deliberately NOT stored: a version id

The requirement says "track the version coordinates". The mapping stores **drive + item + record**
and no version id, and this is the tracking:the resumed load re-reads the *current* version through
the ordinary path.

A stored version id would be **read-never and wrong**. Captured at creation, it is stale the moment
anyone edits the document in Word — pointing the recovery path at a version that is no longer the
document. Storing what we would not read is how stale state becomes the next bug. Flagged here rather
than buried, because it is a deviation from the requirement's literal wording.

### The client half

`loadSucceeded` now adopts the drive-item the server **served** rather than the one the client asked
with (mirroring the existing `saveSucceeded` re-target). Without it the browser holds docx content
while still pointing at the `.pdf` item, and the save path refuses that outright — the user would get
a 422 they cannot act on.

---

## After

```
re-open PDF → documentSpeId = spe-item-docx-…       ✔ resumed on the document that exists
              versionId     = v1                     ✔ real coordinates (the .pdf item has none)
              model contains the first save's edit   ✔
edit + save → REPLACE onto D1                        ✔ not a create
── final ──   1 SPE item · 1 promotion · both edits present
```

| Measure | Before | After |
|---|---:|---:|
| SPE items minted from one PDF, two sessions | 2 | **1** |
| `sprk_document` rows | 2 | **1** |
| First save's edit present after the second save | ✘ | **✔** |

## The guard

A mapping is honored only when the derived document **still exists**. Someone who deletes the Word
document is entitled to re-open the PDF and start over; a dangling mapping would otherwise 404 their
load on an item they never asked for. The stale entry is evicted on the way past.

That test carries an explicit anti-vacuity verification — it asserts the derived item was actually
probed. Without it the test would pass identically on a build where nothing was ever mapped, which is
the pre-044 behavior and proves nothing.

---

## The marker's unsafe direction, and how far the defense actually goes

Missing the marker costs a false warning. A **stale** one costs redlines — it would stamp a real
`.docx` Authored and put its later saves on the clean-apply branch, the SEV-1 shape. So the two
directions are not symmetric and the defense is written for the dangerous one.

**Primary defense: session binding.** A ChatSession is bound to a document, so a load of a *different*
document does not resume it — it mints a new one. A marker written for a PDF is therefore only ever
read back by a save of that same document. This is asserted explicitly in
`SessionThatServedAPdfThenServesADocx_…` rather than assumed.

**Defence in depth: the clear.** A non-PDF load removes the marker for its session, so the marker
always describes what the session currently holds. Given the binding above this is belt-and-braces —
and **the test does not isolate it**, which the test says in as many words rather than implying
coverage it does not have.

**Known bound (not defended):** a client that sends a save carrying a session id belonging to a
*different* document would have that document's marker read. That is a client-contract violation — the
save's session id always comes from the load response — but it is the SEV-1 direction, so it is
recorded here rather than left implicit. Closing it needs a server-side binding check at save time;
carried to task 045 as a question, not silently assumed away.

## Accepted costs (found at the Step-9.5 review, kept deliberately)

**A redirected open downloads the PDF for nothing.** Source detection is bytes-first, so the mapping
can only be consulted *after* the PDF has been fetched. Every reopen of a converted PDF therefore
pays one wasted download for the life of the mapping.

The obvious fix — a cheap pre-check on the metadata filename, before the download — was considered
and **rejected**. It needs a second redirect call site, and because every test uses a `.pdf` filename
the early path would fire in all of them and leave the byte-detected path (the mis-named PDF)
*untested*. One correct path beats two where one is unexercised; this project has been burned by
exactly that shape. Revisit if the cost shows up in practice, with a mis-named-PDF test alongside it.

**`ComposeService.cs` grew 4,031 → 4,373 lines.** It is one of the five files Track D exists to
decompose, and this change made it bigger. The PDF-provenance code is written as one self-contained
region with its own header, depending only on `_cache` / `_logger` / `_spe`, so extracting it into a
collaborator is mechanical — the same shape `ComposeBlockMerge.cs` took in task 040. Flagged for
Track D rather than done here, because splitting the save path mid-task is how R4 and R6 went wrong.

## Two defects the review caught in the first cut

Both were in code written earlier in this same task, and both were silent.

1. **The resumed load attributed the PDF's Dataverse row to the `.docx`.** The redirect fell back to
   `request.DocumentRecordId` when the derived record id was unknown, which would have read the PDF
   record's `sprk_composeorigin` as the Word document's and re-triggered the profile against the wrong
   record. Now passes `derived.RecordId` and nothing else; a null degrades to Path B, where the
   binding contract already treats origin-null as Imported.

2. **One user's lack of access could destroy the mapping for everyone.** The existence probe runs
   under the caller's identity (OBO), so its null means "this caller cannot see it" — not "it is
   gone". The first cut evicted the entry on that signal, letting any user without access delete a
   tenant-scoped recovery path for all other users. The eviction is removed: the caller falls through,
   the entry stays, and the TTL handles genuine deletion.

## Residual

- **Browse/local-file PDF door** — no session, no server identity, and `/api/compose/project` is
  contracted stateless. FR-A09 does not apply (there is no SPE source item to re-open); the FR-A08
  stamp is the residual. See [`document-creation-paths.md`](document-creation-paths.md) path 4.
- **Redis eviction inside 30 days** degrades to a fresh projection and a duplicate on the next save.
  A durable Dataverse column would be strictly better; the POML specifies `IDistributedCache`, so
  that stays a task-045 question rather than a silent substitution.
