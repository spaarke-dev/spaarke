# Drive provenance — decisions

> 2026-09-01 · `work/spaarkeai-compose-r8` · closes the last server item on the R1 list
> (row 3, "was *their task 096* — **NOT deferred, ours**")

## What was wrong

Both Compose write paths into an **existing** drive item took the drive from the caller:

| Path | Where the drive came from |
|---|---|
| `ComposeService.SaveAsync` (replace branch) | `request.DriveId` — the request body |
| `ComposeService.ApplyTemplateAsync` | a route parameter |

Meanwhile the authorized `sprk_document` row already held `sprk_graphdriveid`. The server never
consulted it, so **the record could say the document lives at drive X while its bytes were written to
drive Y**, and nothing noticed.

## What this is NOT

Not the app-only container hole the neighbouring entries in
`SpeWriteSinkContainerProvenanceGuardTests` describe. Every Compose write is **OBO** — SPE authorizes
it as the acting user, so a caller can only reach drives their own token already permits and no
privilege is gained by naming a different one.

The defect is **provenance**: the record and the storage could diverge, which makes the audit trail
wrong. That is why the three census entries convert to `ServerDerivedRecord` rather than being deleted
as non-findings, and why the tests assert *where the write landed* rather than *who was allowed to
write*.

## The design decision — fail-closed vs. fall back

**Chosen: the row wins when it has a drive; the caller's value is used only when the row cannot
answer, and both the fallback and any divergence are logged.**

| Case | Behaviour | Log |
|---|---|---|
| Row records a drive, caller agrees | write to it | — |
| Row records a drive, caller named a different one | **write to the ROW's drive** | Warning naming both |
| Row records no drive (legacy) | fall back to the caller's value | Debug |
| No row carries the item | fall back to the caller's value | Debug |
| The provenance read itself throws | fall back to the caller's value | Warning + exception |

**Why not fail closed on a driveless row.** Rows predating the full-SPE-pointer stamp exist —
`PromoteIfEphemeralAsync`'s create branch documents that a row without the pointer makes downstream
readers 409 *"No file is attached"*, which is the evidence such rows are real and not hypothetical.
Refusing those saves would break real documents to close a hole OBO already closes. **An attacker
cannot make a row's drive id disappear**, so the fallback covers legacy data, not an attack path.

**Why the divergence is a Warning and not an error.** A divergence is the one observation that proves
the defect was live. Making it fatal would convert a silent inconsistency into a broken save for the
users most likely to be affected; logging it makes the population measurable before anything tightens.

## Implementation shape

- `ComposeRecordResolution.TryResolveRecordedDriveIdAsync` — routed through the existing
  `TryFindDocumentByGraphItemIdAsync` rather than issuing its own query, so it **inherits the #781
  self-heal**: a duplicated or non-Active `sprk_graphitemid_uk` still resolves to the canonical row.
  A private query would have answered "no row" during exactly the outage where the data was already
  known to be inconsistent — silently handing every write back to the caller's claim.
  Both column sets (alt-key and self-heal) were widened; a test pins each, because widening one and
  not the other leaves the fix working normally and failing precisely during a key outage.
- `ComposeService.ResolveAuthoritativeDriveIdAsync` — the policy above, in one place, used by both paths.
- **`SaveAsync` folds the result back onto the request** (`request = request with { DriveId = … }`) at
  the top of the method rather than threading a second parameter through five collaborators. The
  property that has to hold is that *no site on the path can still reach the caller's claim*; a
  threaded parameter is a site a future edit can forget.
- **`ApplyTemplateAsync` renames its parameter to `requestedDriveId`** and binds `driveId` to the
  resolved value, so the metadata read, the download and the preconditioned write all move together.
  Apply-template is a read-merge-write: converting only the write would read one drive's document and
  overwrite a different drive's — a **worse** divergence than the one being fixed.

## Cost

One keyed Dataverse retrieve per replace-path write, on a path that already does a Graph metadata read
and a cache read. Not folded into the save's post-write promote lookup (which resolves the same row)
because the value is needed **before** the write — where to write is not a decision that can be made
after writing.

## Verification

11 tests in `tests/integration/data-mutation/Compose/ComposeDriveProvenanceTests.cs`
(resolution unit-level · real save route · real `ApplyTemplateAsync`). Three negative controls, each
firing only on the test that owns it:

| Mutation | Failed |
|---|---|
| A — save resolves provenance but does not apply it | the 2 save-route tests |
| B — apply-template writes to the route's claim | the apply-template divergence test |
| C — alt-key column set narrowed back | `TryResolveRecordedDriveId_AsksForTheDriveColumn` |

Census: the three `ComposeSaveStorageCoordinator.ReplaceFileContentAsUserAsync` entries converted
`ClientSupplied` → `ServerDerivedRecord`; the apply-template deferral note is marked resolved. The
`ClientSupplied` work list drops from six to three (the header's prose count was wrong again — it said
seven; it is now recounted by machine, with the recipe written down).
