# Document-creation paths and their `ComposeOrigin` stamp

> Task 044, FR-A08 acceptance criterion 1 — *"Every document-creation path is enumerated and stamps the
> correct `ComposeOrigin` — recorded in the task notes."*
> **Measured 2026-08-23**, through the wire, not read off the source.

---

## Why this note exists at all

FR-A08 shipped on 2026-08-23 (`95ab4d24f`) with the *suppression* half built and this enumeration
skipped. The consequence was not cosmetic. The suppression reads the durable `sprk_composeorigin`
marker — and the class the requirement names first, PDF-sourced documents, was being stamped
**Imported**. So the feature could not fire for it.

Measured before the fix, through the create-on-save route:

```
PDF load → edit → create-on-save → sprk_composeorigin = 100000001 (Imported)
                                   expected           = 100000000 (Authored)
```

The lesson is the one this project keeps re-learning: a requirement with two halves is not done when
the interesting half is. Reading the origin computation would have shown this in a minute; nobody read
it, because the suppression was the part that felt like the work.

---

## The enumeration

`origin` in `ComposeService.SaveAsync` serves two masters, and 044 separated them:

- **routing origin** (`origin`) — drives clean-apply and the returned result. Deliberately
  Imported-biased: any save carrying a baseline *source* is Imported. This can never mis-stamp a
  genuinely imported document Authored and force it onto the clean branch (the SEV-1 UAT regression).
- **persisted origin** (`originToPersist`) — what gets written to `sprk_composeorigin`. This is what
  the document *is*.

| # | Creation path | Entry | Routing | **Persisted** | Status |
|---|---|---|---|---|---|
| 1 | Born-in-editor (blank / AI-drafted) | `create-on-save`, model only, no carrier | Authored | Authored | ✅ correct before 044 |
| 2 | **PDF from SPE** (Load a `.pdf`) | `create-on-save`, model + synthesized carrier | Imported | **Authored** | ✅ **fixed in 044** |
| 3 | **PDF from Assistant upload** | `/api/compose/upload` → `create-on-save` | Imported | **Authored** | ✅ **fixed in 044** |
| 4 | **PDF from Browse (local file)** | `/api/compose/project` → `create-on-save` | Imported | Imported | ⚠️ **residual — see below** |
| 5 | Imported `.docx` from SPE | `{speId}/save` replace | Imported | *(no new row)* | ✅ correct |
| 6 | Browse/upload of a native `.docx` | `create-on-save`, model + original carrier | Imported | Imported | ✅ correct — there IS an original |
| 7 | Save-New fork (`ForkNew`) | `create-on-save`, fresh transient key | inherits | inherits | ✅ correct by construction |
| 8 | Task 043 "Edit a copy" fork | — | — | — | ⛔ 043 not started; must stamp Authored when built |

### The discriminant

Not the client's word, and not a content match (NFR-02 / I-7). The server determines PDF-ness itself
at load — `IsPdfSource`, bytes-first — and **carries that determination forward** on the session
(`sdap:compose:pdf-session:{sessionId}`, `IDistributedCache` per ADR-009). The save reads it back by
the session id it minted. This is project CLAUDE.md invariant 7: *deterministic information available
at capture time MUST be carried, not re-derived.*

Because the discriminant is the server's own PDF detection, it cannot fire on a `.docx` load. The
SEV-1 vector stays closed.

### Downstream effect, deliberate

Reading `Authored` back on a later save also puts a PDF-sourced document's own edits on the
**clean-apply** branch. That is correct and is more than a warning fix: there are no redlines to drop,
because there was never an original to redline against.

---

## Path 4 — the residual, and why

`/api/compose/project` is contracted to leave **zero server-side state** (`ComposeEndpoints.cs`: *"a
call leaves zero server-side state"*). It is the Browse-local-file door: the user picks a PDF off
their disk, and there is no session and no server-side identity to key a marker on.

Closing it would mean either giving a deliberately stateless endpoint state, or trusting a
client-supplied "this was a PDF" claim for the persisted marker. Both are worse than the symptom.

**Cost of leaving it**: a PDF opened via Browse and saved shows "some formatting was simplified when
saving" once, on its first save, describing a loss relative to an original that never existed. Its
*second* save is correct — by then the record exists and the marker is read from it.

Carried to the task-045 residual list.

---

## What is covered by tests

`tests/integration/seam/Compose/ComposePdfRefreshBaselineSeamTests.cs`

| Test | Asserts |
|---|---|
| `PdfSourcedCreateOnSave_StampsTheRecordAuthored_…` | path 2 stamps `Authored` (100000000) |
| `PdfSourced_SecondSaveAfterRefresh_…` | FR-A09 end to end (see `pdf-refresh-baseline.md`) |
| `PdfSourced_WhenTheDerivedDocumentWasDeleted_…` | the mapping degrades, never traps the user |

Path 3 (Assistant upload) shares the identical mechanism and code path as path 2 — same
`SetPdfSourceMarkerAsync` call, same read at save. It is **not separately tested end-to-end**, and is
recorded here as such rather than counted as covered.
