# Deferrals & Issues — `spaarkeai-word-add-in-r1`

> Source of truth. Every entry must ALSO have a GitHub Issue (visibility). `push-to-github` blocks on entries missing a GitHub URL.
> File via `/project-defer-issue-tracking` (alias `/defer`) — it writes both places in one step.
> Per CLAUDE.md §11, every entry names a concrete behavior or contract that fails. "Future flexibility" is not a reason.

---

## ISS-001 — `sprk_event` is written to `sprk_document` but does not exist on the entity

| Field | Value |
|---|---|
| **Type** | Issue (live defect in shipped code) |
| **Found** | 2026-09-04, during `/project-pipeline` initialization (finding **F-g**) |
| **Owner** | **`unified-access-control-r2`** — its 2026-09-03 Q4 widening added the `event` entry, and `EntityAccessFilter` is its component |
| **Severity** | Filing a document to an Event is authorized and then cannot associate |
| **GitHub Issue** | ➖ **Not filed — operator decision 2026-09-04**: no GitHub Issue needed. Routing to `unified-access-control-r2` handled out-of-band. This entry remains the record; do not treat the missing URL as an unfiled obligation or block push on it. |

### What fails

`sprk_document` has **no `sprk_event` attribute**. Verified live via Dataverse MCP 2026-09-04 and corroborated by the maker-portal Columns view:

```
SELECT sprk_event FROM sprk_document
→ 'sprk_Document' entity doesn't contain attribute with Name = 'sprk_event'
```

The entity has **four** direct lookups (`sprk_matter`, `sprk_project`, `sprk_invoice`, `sprk_workassignment`) and **twelve** `sprk_related*` lookups. The Event column is **`sprk_relatedevent`** only.

Yet three shipped layers treat a direct `sprk_event` as real:

| Layer | Behaviour |
|---|---|
| `Api/Filters/EntityAccessFilter.cs:126-127` | Accepts `event` / `sprk_event` — authorizes the record-keyed upload route |
| `Spaarke.Dataverse/Models.cs` `DocumentAssociationMap.TryApply` | Maps it → `EventLookup`, returns `true` (success) |
| `Spaarke.Dataverse/DataverseServiceClientImpl.cs:916` | Writes `document["sprk_event"]` |

Both call sites carry a comment claiming the columns were "verified against live Dataverse metadata 2026-09-03". That holds for `sprk_workassignment`; it is wrong for `sprk_event`.

### Why it matters

This is exactly the failure mode the lockstep invariant exists to prevent. [`coordination-document-association-map-from-email-r2-2026-09-04.md`](../coordination-document-association-map-from-email-r2-2026-09-04.md) §3 states the rule — *"a type belongs in **both** maps or **neither**"* — and then names `event` as compliant. `event` is in both maps with no column.

Likely mechanism: the 2026-09-03 verification checked the `sprk_related*` family (where `sprk_relatedevent` does exist) while the code writes the direct family — the same family confusion visible in that doc's §1, which describes the write target as `sprk_related{recordtype}` when the code writes direct slots.

### Two further false claims from the same source

| Claim | Reality |
|---|---|
| `sprk_todo` is "unmappable — a document cannot be associated to a to-do at all … needs a schema change first" (§3) | **`sprk_relatedtodo` exists.** True only of a column literally named `sprk_todo`. No schema change required. |
| "`sprk_document` has no account/contact lookup column" (§4.1) | **`sprk_relatedcontact` exists.** `account` is correct — no account lookup at all. |

Knock-on: §4.2 repoints `RecordKeyedUploadAuthorizationTests`' deny-path example at `sprk_todo` as "genuinely unmappable" — that example rests on the same false premise and should be re-examined.

### What r1 did (and deliberately did not do)

- ✅ Corrected tasks **026** and **035** so they do not inherit the false premise; 026 reads `sprk_relatedevent`, never `sprk_event`
- ✅ Appended a §0 correction to the coordination doc
- ✅ Recorded as finding **F-g** in `plan.md` §3, `TASK-INDEX.md`, and project `CLAUDE.md`
- ❌ **Did not change `EntityAccessFilter`, `DocumentAssociationMap`, or `DataverseServiceClientImpl`** — out of r1's scope, and UAC-r2 is live on those files (`parallel-safe:false`). Fixing them here would collide.

### Suggested fix for the owner

Either add a direct `sprk_event` column to `sprk_document`, or repoint `EventLookup` at `sprk_relatedevent` and drop `event` from `EntitySetByType` until the families are reconciled. The broader question — why two lookup families exist and which one the association map should target — is worth settling before either.

---

## Deferrals

### D-032-1 — Visualization parent HUB nodes are not authorized against their parent record

**Found by** task 032 while closing finding F-b. **Owner: `unified-access-control-r2`** (its surface).

`VisualizationService.CreateParentHubNode` builds a hub node carrying the SOURCE document's own matter /
project / invoice name (`sourceDoc.MatterName`, `BuildParentRecordUrl("sprk_matter", …)`). The caller is
authorized on the source document, but **Read on a document does not formally imply Read on its matter** —
so a matter's NAME, and a deep link to its record, can reach a caller holding no rights on the matter itself.
On a secure matter the name is frequently the sensitive fact (a matter named for a counterparty discloses the
engagement's existence), which is the same argument `RecordSearchAuthorizationFilter`'s remarks make.

**Not fixed in task 032, deliberately.** F-b is about RESULT ROWS; hub nodes are pre-existing and are the
source's own information. Widening a security task's scope silently is what CLAUDE.md §11 forbids, and the
correct check (`GetCallerRecordAccessAsync` against `sprk_matters`/`sprk_projects`/`sprk_invoices`) is
UAC-r2's mechanism, not a second one built here.

**Suggested fix**: authorize each hub against its parent record and drop the hub — and the source's edge to
it — when Read is absent. Task 032's `AuthorizeRowsAsync` already drops hubs left with no surviving
document, so the hook exists.

### D-032-2 — Rows dropped past the authorization budget are not announced to the client

**Found by** task 032. Same shape as the limitation `RecordSearchEndpoints.AuthorizeRowsAsync` recorded.

`VisualizationEndpoints.AuthorizeRowsAsync` bounds its work at `MaxDocumentAuthorizationChecks = 100` and
**drops** everything past it (fail closed). The caller sees a short graph that is indistinguishable from
"there simply are not many related documents". Cross-record document search announces exactly this with a
`PARTIAL_RESULTS` warning; `GraphMetadata` has no warnings channel.

**Not fixed in task 032, deliberately**: adding one is a response-contract change, and a response-contract
change does not belong inside a security fix — the same call `RecordSearchEndpoints` made and recorded.

**Relevant to task 033/034**, which render this surface: a short result page may mean "withheld", not
"nothing matched". See `notes/032-authorization-hardening.md` §5 for the counts that make this reachable
(five hardcoded relationship queries at `TopCount = 50` each).
