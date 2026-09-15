# Document-link vocabulary hoist — decision record (2026-09-05)

> Standalone hoist task (not a numbered `tasks/*.poml` item — dispatched directly from the brief
> recorded in `current-task.md`'s "🔴 THERE ARE ALREADY TWO COPIES OF THIS VOCABULARY" section).
> Executed by a concurrent sub-agent alongside the main session's own work; no git commands were run
> here — the main session owns commit/push.

## 1. The problem, restated

Two independent copies of the `sprk_document` record-link vocabulary existed:

1. `AttachmentDocumentAssociationRung.DocumentLinkFields` — a private field, 6 entries.
2. `ComposeService.DocumentAssociationLookupAttributes` — a `string[]`, the SAME 6 entries, whose own
   comment conceded it was "the SAME closed set `AttachmentDocumentAssociationRung` follows."

Both were missing `sprk_relatedinvoice` and `sprk_relatedworkassignment`, plus six further
`sprk_related*` columns neither list had ever enumerated. A document linked to a matter/project/etc.
ONLY through one of the missing columns was invisible to every consumer of either list.

## 2. Where it now lives, and why

**`src/server/shared/Spaarke.Dataverse/Models.cs`**, immediately after `DocumentAssociationMap` (the
existing WRITE-side map — "caller-supplied type → which `UpdateDocumentRequest` property to set").

This was the brief's own strong recommendation, and it held up under implementation:

- Both consumers (`AttachmentDocumentAssociationRung`, `ComposeService`/`ComposeCreateOnSavePromoter`)
  live in `Sprk.Bff.Api`, which already references `Spaarke.Dataverse`. No new project reference.
- Placing the READ vocabulary textually beside the WRITE map means a future editor extending one sees
  the other in the same scroll — the actual mechanism that stops the two from drifting apart again is
  social/visual proximity plus the fact that both BFF call sites now derive from the SAME static data
  rather than each hand-writing a list.
- `DocumentAssociationMap` is `public`, and `Spaarke.Dataverse.csproj`'s `InternalsVisibleTo` list does
  NOT include `Sprk.Bff.Api` (only `Sprk.Bff.Api.Tests`) — so the new type had to be `public` too, for
  the two BFF consumers to reach it. Matches the existing map's visibility.

**Structural note on why this hoist is stronger than "put a list somewhere and point at it twice":**
`ComposeService.DocumentAssociationLookupAttributes` is now a *computed* field
(`DocumentLinkFields.LogicalNames.ToArray()`), not an independent literal, and
`AttachmentDocumentAssociationRung` has **no local field of its own any more** — it reads
`DocumentLinkFields.All` directly. There is no longer a second place to edit, which means there is no
longer a way to edit only one and produce silent drift by omission. The regression tests in §5 exist to
catch the case where someone reintroduces a local literal anyway (reverts the delegation).

The WRITE map (`DocumentAssociationMap`) and the READ vocabulary (`DocumentLinkFields`) remain two
separate data shapes on purpose — they answer different questions (`DocumentAssociationMap`: "which ONE
column does this caller-facing token map to, for a WRITE" vs `DocumentLinkFields`: "every column that
can carry a link, for a READ"). Unifying them into one structure was considered and rejected: the write
map's case list has no "related" variants at all (a caller cannot currently ask to file directly onto
`sprk_relatedmatter`), so forcing both into one shape would mean either inventing write semantics nobody
asked for or leaving half the unified structure's fields perpetually null. Co-location (same file,
adjacent) gets the anti-drift benefit without that cost.

## 3. The complete column table (16 entries, all live-verified 2026-09-05)

Verified two independent ways against `spaarkedev1`:

- **Target entities**: `describe('tables/sprk_document')` (Dataverse MCP `describe` tool) — a live
  `DESCRIBE TABLE` projection.
- **Schema-name casing**: a direct Web API metadata call —
  `GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName='sprk_document')/Attributes?$select=LogicalName,SchemaName,AttributeType&$filter=AttributeType eq 'Lookup'`
  (bearer token via `az account get-access-token --resource https://spaarkedev1.crm.dynamics.com`).
  The raw JSON response is what the table below transcribes — not a written list, not the brief's
  table taken on faith (though it turned out to match exactly).

| Logical name (always lowercase — safe for SDK `ColumnSet`/`Entity` access) | Schema name (case-sensitive — for a future `@odata.bind` consumer) | Target entity |
|---|---|---|
| `sprk_matter` | `sprk_Matter` | `sprk_matter` |
| `sprk_relatedmatter` | `sprk_relatedmatter` **(lowercase)** | `sprk_matter` |
| `sprk_project` | `sprk_Project` | `sprk_project` |
| `sprk_relatedproject` | `sprk_relatedproject` **(lowercase)** | `sprk_project` |
| `sprk_invoice` | `sprk_Invoice` | `sprk_invoice` |
| `sprk_relatedinvoice` | `sprk_RelatedInvoice` | `sprk_invoice` |
| `sprk_workassignment` | `sprk_WorkAssignment` | `sprk_workassignment` |
| `sprk_relatedworkassignment` | `sprk_RelatedWorkAssignment` | `sprk_workassignment` |
| `sprk_relatedagreement` | `sprk_RelatedAgreement` | `sprk_agreement` |
| `sprk_relatedcommunication` | `sprk_RelatedCommunication` | `sprk_communication` |
| `sprk_relatedcontact` | `sprk_RelatedContact` | `contact` |
| `sprk_relatedevent` | `sprk_RelatedEvent` | `sprk_event` |
| `sprk_relatedorganization` | `sprk_RelatedOrganization` | `sprk_organization` |
| `sprk_relatedservicerequest` | `sprk_RelatedServiceRequest` | `sprk_servicerequest` |
| `sprk_relatedtodo` | `sprk_RelatedToDo` | `sprk_todo` |
| `sprk_relatedvendororg` | `sprk_relatedvendororg` **(lowercase)** | `sprk_organization` |

Rows added by this hoist (10 of 16): every row except the original 6
(`sprk_matter`/`sprk_relatedmatter`/`sprk_project`/`sprk_relatedproject`/`sprk_invoice`/`sprk_workassignment`).

**The casing trap, confirmed exactly as the brief described it.** 9 of the 12 `related` columns are
PascalCase (`sprk_RelatedX`); 3 are lowercase (`sprk_relatedmatter`, `sprk_relatedproject`,
`sprk_relatedvendororg` — matter and project being, per the brief, "the two that matter most"). A
`$"sprk_Related{type}"` convention-derived builder would be right for 9 and silently wrong for 3, with
no compiler or runtime signal — see §5 for the perturbation that reproduces exactly this bug and shows
what catches it.

**Bonus finding, not in the original brief:** the 4 "primary" (non-`related`) columns are *also* not
plain-lowercase in schema form — they're PascalCased (`sprk_Matter`, `sprk_Project`, `sprk_Invoice`,
`sprk_WorkAssignment`) despite fully-lowercase logical names. The brief's trap section was scoped to the
`related` family; this extends the same warning to the primary family. Recorded in the `DocumentLinkField`
XML doc and in `DocumentLinkFieldsTests`, though neither current consumer exercises `SchemaName` today
(see §6).

**`sprk_relatedorganization` and `sprk_relatedvendororg` both target `sprk_organization`** — confirmed
by `describe`, not assumed. There is no separate "vendor org" entity in Spaarke's model.

## 4. What changed at each call site

### `src/server/shared/Spaarke.Dataverse/Models.cs`

Added, directly after `DocumentAssociationMap`:

- `public sealed record DocumentLinkField(string LogicalName, string SchemaName, string TargetEntityLogicalName)`
- `public static class DocumentLinkFields` with:
  - `All` — the 16-entry `IReadOnlyList<DocumentLinkField>` table above.
  - `LogicalNames` — `IReadOnlyList<string>`, `All.Select(f => f.LogicalName)`, the shape both current
    consumers need for a `ColumnSet` / `RetrieveAsync` columns argument.

`SchemaName` is carried on every entry even though neither current consumer touches it — both go
through the SDK-based `IGenericEntityService` (`ColumnSet`/`QueryExpression`/`Entity` indexer, all
logical-name-keyed), confirmed by reading `IGenericEntityService.cs` and
`DataverseServiceClientImpl.cs:905-938` (the sibling write path, which also indexes `Entity` by logical
name, e.g. `document["sprk_matter"] = new EntityReference("sprk_matter", ...)`). It is pinned anyway
because the brief's own instruction ("pin an explicit per-column map... in the code comment so the next
person cannot reintroduce it") is squarely about a FUTURE Web-API/`@odata.bind` consumer, and every value
was independently verified live — carrying it costs one string per row and removes the need for that
future consumer to re-derive (and mis-derive) it.

### `AttachmentDocumentAssociationRung.cs`

- Deleted the private `DocumentLinkFields` field (6-entry tuple list) and its XML doc comment.
- Replaced with a `//` explanatory comment (not `///`, since nothing follows it directly but the
  constructor — an XML doc comment would have attached to the wrong member) pointing at
  `Spaarke.Dataverse.DocumentLinkFields`, preserving the rung-specific "type-agnostic by design"
  rationale, and adding a new note about the `RegardingFieldMap` gap (§6).
- `QueryDocumentsAsync`: `columns.AddRange(DocumentLinkFields.Select(f => f.DocumentField))` →
  `columns.AddRange(DocumentLinkFields.LogicalNames)`.
- `BuildMatches`: `foreach (var (documentField, targetEntity) in DocumentLinkFields)` →
  `foreach (var (documentField, _, targetEntity) in DocumentLinkFields.All)` — positional-record
  deconstruction, discarding `SchemaName`. The loop body is otherwise byte-identical (same local names).

### `ComposeService.cs`

- `internal static readonly string[] DocumentAssociationLookupAttributes = { ...6 literals... };` →
  `internal static readonly string[] DocumentAssociationLookupAttributes = DocumentLinkFields.LogicalNames.ToArray();`
- Updated the preceding comment to point at the hoist and name both prior gaps.

### `ComposeCreateOnSavePromoter.cs` — deliberately NOT touched

This file has a third reference to `ComposeService.DocumentAssociationLookupAttributes` (the actual
link-inheritance read + copy loop, `:253` and `:260`). Because `ComposeService`'s field is now a
*computed* forwarding field rather than an independent literal, `ComposeCreateOnSavePromoter` keeps
compiling and behaving correctly with **zero changes** — it was reading a `string[]` before and still
reads the identical-shaped `string[]` now, just a longer one. This is the payoff of keeping the field
name and type stable while changing only its initializer.

## 5. Tests

### New: `tests/unit/domain/Dataverse/DocumentLinkFieldsTests.cs` (36 test cases)

Pure-domain tests (KEEP path `tests/unit/domain/**`) pinning: exact count (16), every logical
name/schema name/target-entity triple individually (`[Theory]` + `MemberData`, so a mismatch names the
specific column), the no-unexpected-additions inverse check, the 9-PascalCase / 3-lowercase casing split
by name, the "related targets same entity as primary" rule for the 4 pairs that have one, the
organization/vendororg dual-target fact, and `LogicalNames` ordering.

### New: `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeServiceDocumentLinkVocabularyTests.cs` (2 test cases)

The task's explicit "fails if the two consumers diverge" test:
`DocumentAssociationLookupAttributes_NeverForksFromTheSharedVocabulary` asserts
`ComposeService.DocumentAssociationLookupAttributes` equals `DocumentLinkFields.LogicalNames` exactly.
A second test independently pins the count and the two originally-missing columns.

### Modified: `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/AttachmentDocumentAssociationRungTests.cs` (+2 test cases, 8 → 10)

- `Evaluate_DocumentLinkedViaRelatedInvoiceOrRelatedWorkAssignment_SurfacesBothAsSuggestedCandidates` —
  the two columns the brief calls out by name as invisible to both prior copies.
- `Evaluate_DocumentLinkedThroughEveryVocabularyColumn_SurfacesAMatchForEveryMappedTarget` — builds one
  document with a link on every `DocumentLinkFields.All` column whose target has a `RegardingFieldMap`
  entry (13 of 16) and asserts all 13 surface. Proves the RUNG's actual runtime behavior tracks the full
  shared vocabulary, not merely that some list somewhere has 16 entries.

### Modified: `tests/integration/contract/Api/Ai/ComposeCreateOnSaveEndpointContractTests.cs` (count unchanged, 1 assertion widened)

`CreateOnSave_WithSourceDocumentRecordId_InheritsSourceRecordLinks` asserted the exact 6-column list
retrieved for link inheritance. Left as-is, this test would have gone red from OUR change (not from a
real regression) the moment `DocumentAssociationLookupAttributes` widened to 16. Updated the expected
array to the full 16-column list.

### Counts

| Scope | Before | After |
|---|---|---|
| `AttachmentDocumentAssociationRungTests.cs` | 8 | 10 |
| `DocumentLinkFieldsTests.cs` (new) | — | 36 |
| `ComposeServiceDocumentLinkVocabularyTests.cs` (new) | — | 2 |
| `ComposeCreateOnSaveEndpointContractTests.cs` | unchanged (test count) | unchanged (1 assertion widened) |
| **New test cases added by this hoist** | — | **40** |

Scoped run (`--filter` on the 4 touched classes, `dotnet test tests/unit/Sprk.Bff.Api.Tests/`):
**0 failed / 49 passed / 0 skipped / 49 total.**

Broader ripple check (full `Sprk.Bff.Api.Tests.Services.Communication` +
`Sprk.Bff.Api.Tests.Services.Compose` + `Sprk.Bff.Api.Tests.Domain.Dataverse` +
`Sprk.Bff.Api.Tests.Models` namespaces — still one project, not solution-wide):
**0 failed / 1442 passed / 5 skipped (pre-existing, unrelated) / 1447 total.**

`dotnet build src/server/api/Sprk.Bff.Api/ --no-incremental`: **0 warnings / 0 errors**, both before
writing tests and again as the final step after every perturbation below was reverted.

## 6. Perturbation evidence — the tests were proven to discriminate, not just proven to pass

Per the brief's explicit instruction, each new/modified test's discriminating power was verified by
breaking production code and watching the test go red, then reverting and confirming green again.

**Perturbation A — reintroduce the cross-consumer fork.** Temporarily reverted
`ComposeService.DocumentAssociationLookupAttributes` to the old 6-entry literal (undoing the
delegation to `DocumentLinkFields.LogicalNames`).

Result: **3 tests went red**, exactly the ones that should:
- `ComposeServiceDocumentLinkVocabularyTests.DocumentAssociationLookupAttributes_NeverForksFromTheSharedVocabulary`
  — *"but {...6 items...} contains 10 item(s) less"*
- `ComposeServiceDocumentLinkVocabularyTests.DocumentAssociationLookupAttributes_ContainsAllSixteenLiveVerifiedColumns`
  — *"Expected ... to contain 16 item(s), but found 6"*
- `ComposeCreateOnSaveEndpointContractTests.CreateOnSave_WithSourceDocumentRecordId_InheritsSourceRecordLinks`
  — *"Expected retrievedColumns to be a collection with 16 item(s) ... but {...6 items...} contains 10
  item(s) less"*

Reverted; re-ran the same filter: **0 failed / 3 passed.**

**Perturbation B — reproduce the exact casing trap.** Temporarily changed the `sprk_relatedmatter`
entry's `SchemaName` from the correct `"sprk_relatedmatter"` to the convention-derived (WRONG)
`"sprk_RelatedMatter"` — the precise bug shape `$"sprk_Related{type}"` would produce.

Result: **2 of 36 `DocumentLinkFieldsTests` went red**, both pinpointing the exact column and the exact
wrong value:
- `All_EveryEntry_HasTheLiveVerifiedLogicalSchemaAndTargetName(logicalName: "sprk_relatedmatter", ...)`
  — *"but "sprk_RelatedMatter" differs near "Rel" (index 5)"*
- `All_LowercaseRelatedSchemaNames_AreExactlyThisThreeColumnException(expectedLowercaseSchemaName:
  "sprk_relatedmatter")` — same diff.

The other 34 tests in the file stayed green (confirming the perturbation was isolated, not a
compile-level break). Reverted; re-ran: **0 failed / 36 passed.**

Final state re-verified clean: full BFF build 0/0, scoped filter 50/0/0 (including the adjacent
best-effort-failure test `CreateOnSave_WhenSourceRecordReadFails_StillCreatesTheDocumentUnassociated`),
broader namespace check 1442/0/5-skipped.

Raw metadata query artifact (LogicalName/SchemaName pairs used to build §3) was saved during
verification to the sub-agent's scratchpad
(`sprk_document_lookups.json`, not part of the repo) — the table in §3 is the transcription of that
response, not a re-typed copy of the brief's table.

## 7. Findings that extend or nuance the brief (none contradict it)

1. **`RegardingFieldMap` gap (new finding).** `AttachmentDocumentAssociationRung.BuildMatches` resolves
   a target entity to a `sprk_communication` regarding field via
   `Sprk.Bff.Api.Services.Communication.Engine.RegardingFieldMap.FieldFor(targetEntity)`, and that map
   does **not** have entries for `sprk_agreement`, `sprk_communication`, or `sprk_todo`. The rung already
   soft-skips (`continue`) a link whose target has no regarding field, so this is not an error — but it
   means 3 of the 16 vocabulary columns (`sprk_relatedagreement`, `sprk_relatedcommunication`,
   `sprk_relatedtodo`) will not yet produce a SUGGESTED candidate from this rung even though they are
   now part of the shared vocabulary and DO work correctly for `ComposeCreateOnSavePromoter`'s link
   inheritance (which never goes through `RegardingFieldMap`). Widening `RegardingFieldMap` is a
   separate decision — out of scope for this hoist, not attempted, and explicitly not declared as one of
   this task's outputs. Documented in the `DocumentLinkFields` XML remarks, the Rung's inline comment,
   and the new completeness test's exclusion rationale.
2. **The 4 primary columns have their own casing trap.** Not called out in the brief (which scoped the
   warning to the `related` family), but empirically true and now pinned: `sprk_matter` → `sprk_Matter`,
   `sprk_project` → `sprk_Project`, `sprk_invoice` → `sprk_Invoice`, `sprk_workassignment` →
   `sprk_WorkAssignment`. A convention that assumed "logical name with the first letter capitalized"
   would get 3 of these 4 right and `sprk_workassignment` → `sprk_Workassignment` wrong (real value is
   `sprk_WorkAssignment`, capital A). Recorded for completeness even though it is not this task's central
   trap.
3. **A third call site exists, but is not a third independent copy.** `ComposeCreateOnSavePromoter.cs`
   references `ComposeService.DocumentAssociationLookupAttributes` twice. It is a CONSUMER of
   `ComposeService`'s field, not a second definition — confirmed it required zero changes.
4. **Everything else in the brief checked out exactly as stated** — the PascalCase/lowercase split for
   the `related` family, the target-entity mappings (including the `sprk_relatedvendororg` →
   `sprk_organization` fact), and the "both prior copies were incomplete on the same two columns" claim.
