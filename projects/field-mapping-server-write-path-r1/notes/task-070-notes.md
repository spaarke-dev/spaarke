# Task 070 notes: RecordFamilyEdges, the canonical Filing edge registry (FR-29)

## Rigor and scope

FULL rigor, from the POML. Executed on opus at high effort.

- Branch `task/fm-070`, cut from `1bf631c84b`, which is master `078f62bcad` plus the project docs.
- `de38296499` (UAC 194) is an ancestor.
- Seeded from the registries as they stand at that commit. No existing registry changed: the diff adds three new files
  and these notes.
- No dependency on UAC 194b. The registry has no primary-parent ordering; that belongs to the reader (071).

## What was added

| File | What |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordFamilyEdges.cs` | Static data, internal, with no DI registration and no package. |
| `tests/Spaarke.ArchTests/RecordFamilyEdgesGuardTests.cs` | The parity ratchet, the content pin and the C2 guard, each with controls. |
| `tests/unit/domain/Dataverse/RecordFamilyEdgesTests.cs` | Row shape and exclusions. |

`RecordFamilyEdges.cs` provides:

- `FamilyKind` (`Filing = 1`, with no zero member, so an uninitialised kind is never Filing).
- `EdgeKind`: `FilingCore`, `Pair`, `Stamp`, `LineageHop`.
- `Edge(FamilyKind, ChildTable, LookupColumn, ParentTable, EdgeKind, InSecureFamily, RecordNumberColumn)`.
- `All`, which holds 141 rows across 31 child tables.
- `Of(kind)`.
- `SecureLineageView()`, which has the shape of `SecureChildLineage.Children`. It is for the parity check only and is
  never a source for an isolation walk.
- `CoreAncestorChildrenView()`, which has the shape of `CoreAncestorResolver.IntermediateRootColumns`.
- `RecordNumberColumnOf(table)`, plus `PairIdColumn` and `PairTypeColumn`.
- `DetailTables`: display only, outside the access pin.

`CoreTables`, `StampColumns`, `PairIdColumn` and `PairTypeColumn` are derived from `CoreAncestorResolver`, never
restated.

## UAC-r2 review

**UAC-r2 review: content ruling 2026-10-28 on a38396afcf, OK with changes 1-3 applied.** UAC re-checks the final head.

1. Row added: `sprk_analysis.sprk_documentid → sprk_document`, a LineageHop in the Secure family. It is the analysis's
   ownership primary.
2. Row added: `sprk_communication.sprk_communicationthread → sprk_communicationthread`, a LineageHop in the Secure
   family. A message in a record thread takes the thread's filing.
3. New guard: every SecureChildLineage lookup to an ownership parent that is not a citation must be a row or baselined.

The ruling also kept the three service-request rows (Filing, `InSecureFamily = false`), added `DetailTables`, and
changed some wording; no other row changed.

`DetailTables`: `sprk_eventlog`, `sprk_emailreviewlog`, `sprk_attachmentartifact`, `sprk_emailartifact`,
`sprk_spendsignal`, `sprk_spendsnapshot`, `sprk_billingevent` and `sprk_analysisoutput`. The default family view and push
(040) skip them. `sprk_kpiassessment` is not detail, because it is user-entered.

Further candidates (notes only, not in the set): `sprk_communicationparticipant`, `sprk_communicationattachment`,
`sprk_fileversion` and `sprk_policyversion`.

### EdgeKind rule (pinned)

- On a core child table, an edge is `Pair` when its column is the pair id, and `FilingCore` otherwise.
- On any other child table, an edge is `Stamp` when its column is one of the four `sprk_regarding{core}` columns
  (checked against `CoreAncestorResolver.CoreAncestorLookups`), and `LineageHop` otherwise.

### InSecureFamily (pinned)

`InSecureFamily` is false only on an edge to `sprk_servicerequest`. Those edges are the service request's own family
(`ServiceRequestRelations`, UAC-r2 round 2), which sits outside the Secure family.

## Coordinator rules received during the task (UAC-r2 round 130, design §12.12)

1. The registry rows are `internal`. No public surface exposes lookup names or storage.
2. The task adds no family lookup column and no family table.
3. No test hard-codes a team id. C2 is pure static data over the two isolation views and makes no Secure-Record check,
   so the shared predicate (`SecureRecordOwnerTeam.IsInSecureBusinessUnit` +
   `SecureChildShareSynchronizer.ResolveSecureBusinessUnitAsync`) is not needed here.

## Seeding decisions (as reviewed by UAC-r2)

The rule I applied: an edge in an existing registry is a filing edge unless an existing registry's own code or remarks
says it is not. `SecureChildLineage` disclaims being a filing classification ("every lookup is kept, on purpose"), so
being listed there is not a filing claim. UAC-r2 then ruled on the edge cases. Every exclusion is in the baseline.

**Left out of the registry:**

- `sprk_document.sprk_currentversionid`, `sprk_document.sprk_canonicaldocument` and `sprk_analysis.sprk_outputfileid`.
  The SecureChildLineage remarks call them not filing. These are the three narrowing-only lookups `SecureLineageView`
  omits.
- `sprk_workassignment.sprk_regardingcommunication`. It is not a filing (`SecureRootInheritance.cs:426-427`); the storage
  resolver holds it.
- A service request's own regarding lookups, because it is a root.
- Isolation-only parents, because a family never decides isolation:
  - a Decision Record's resolved-Signal subjects (`sprk_steps`);
  - a frozen core that no longer matches the live stamp.

**Service-request rows (kept, UAC-r2 ruling).** All nine ServiceRequestRelations edges are Filing with
`InSecureFamily = false`. SecureChildLineage.cs:51's "not a filing parent" describes only the Secure lineage map, and UAC
rewords it in 194b. Code remark cites `RecordOwnershipResolver.cs:1157-1160` and `FilingParentOf` `:660-680`.

**Not included:**

- The work assignment's pair. It counts only by repeating a typed lookup (`SecureRootInheritance` #1616), so it is
  never an edge of its own.
- Pair edges on child tables, such as the memo. UAC confirmed none.
- `sprk_procedureassignment`, `sprk_invoicelineitem`, N:N associations, citations, people and organization lookups,
  grant rows, and `sprk_policy.sprk_currentversion`. None of them is in the source registries; the unit tests pin
  their absence.

**RecordNumberColumn** comes from the solution source (`Entities/*/Entity.xml`), for matter, project, work assignment,
service request, analysis, budget, decision record, document, event, invoice, report card and signal. Version numbers
(file version, policy version) are ordinals, so they stay null. The column is not pinned.

## Ratchet baseline (16 lines, `KnownDisagreements`)

| Registry | Edge | Direction | Reason |
|---|---|---|---|
| SecureChildLineage | document.sprk_currentversionid → fileversion | only in SCL | its current version, not filing |
| SecureChildLineage | document.sprk_canonicaldocument → document | only in SCL | its canonical copy, not filing |
| SecureChildLineage | analysis.sprk_outputfileid → document | only in SCL | its output file, not filing |
| CoreAncestorResolver.StampSourceColumns | analysis.sprk_documentid → document | only in registry | the analysis's ownership primary, not a stamp source (CoreAncestorResolver.cs:212-214) |
| CoreAncestorResolver children | decisionrecord.sprk_matter / sprk_project / sprk_workassignment → core | only in registry (3 lines) | typed decision context, not a stamp or root column (D-34) |
| CoreAncestorResolver children | signal.sprk_matter → matter | only in registry | typed core lookup, not a stamp column |
| CoreAncestorResolver children | document.sprk_relatedservicerequest → servicerequest | only in registry | service request family; the stamp topology excludes it by name |
| OwnershipParentEntities | sprk_billingevent | only in OPE | an ownership parent through sprk_invoicelineitem.sprk_billingevent, which waits for ebilling (#2348) |
| OwnershipParentEntities | sprk_kpiassessment, sprk_spendsignal, sprk_memo | only in OPE (3 lines) | an ownership parent no filing lookup names |
| OwnershipParentEntities | sprk_decisionrecord, sprk_emailartifact | only in registry (2 lines) | a lineage parent only; ownership does not list it |
| OwnershipParentEntities | sprk_communicationthread | only in registry | grouping container, never an ownership parent (RecordOwnershipResolver.cs:1150); a message takes its record thread's filing |

Every other view agrees exactly:

- TypedFilingColumns;
- IntermediateFilingColumns;
- the pair;
- ServiceRequestRelations;
- the rest of StampSourceColumns, SecureChildLineage and CoreAncestorResolver children.

## Registry rows (for UAC-r2 content review)

All rows are `FamilyKind = Filing`. This table is the same as the content pin (`PinnedAccessRows`) plus the display column.

| # | Child table | Lookup / pair | Parent | EdgeKind | InSecureFamily | RecordNumberColumn | Detail |
|---|---|---|---|---|---|---|---|
| 1 | sprk_agreement | sprk_regardingdocument | sprk_document | LineageHop | yes | — |  |
| 2 | sprk_agreement | sprk_regardingmatter | sprk_matter | Stamp | yes | — |  |
| 3 | sprk_agreement | sprk_regardingproject | sprk_project | Stamp | yes | — |  |
| 4 | sprk_analysis | sprk_documentid | sprk_document | LineageHop | yes | sprk_analysis_number |  |
| 5 | sprk_analysis | sprk_regardingbudget | sprk_budget | LineageHop | yes | sprk_analysis_number |  |
| 6 | sprk_analysis | sprk_regardingcommunication | sprk_communication | LineageHop | yes | sprk_analysis_number |  |
| 7 | sprk_analysis | sprk_regardingdocument | sprk_document | LineageHop | yes | sprk_analysis_number |  |
| 8 | sprk_analysis | sprk_regardinginvoice | sprk_invoice | LineageHop | yes | sprk_analysis_number |  |
| 9 | sprk_analysis | sprk_regardingmatter | sprk_matter | Stamp | yes | sprk_analysis_number |  |
| 10 | sprk_analysis | sprk_regardingproject | sprk_project | Stamp | yes | sprk_analysis_number |  |
| 11 | sprk_analysis | sprk_regardingservicerequest | sprk_servicerequest | Stamp | no | sprk_analysis_number |  |
| 12 | sprk_analysis | sprk_regardingworkassignment | sprk_workassignment | Stamp | yes | sprk_analysis_number |  |
| 13 | sprk_analysisoutput | sprk_analysisid | sprk_analysis | LineageHop | yes | — | detail |
| 14 | sprk_attachmentartifact | sprk_document | sprk_document | LineageHop | yes | — | detail |
| 15 | sprk_attachmentartifact | sprk_emailartifact | sprk_emailartifact | LineageHop | yes | — | detail |
| 16 | sprk_billingevent | sprk_invoice | sprk_invoice | LineageHop | yes | — | detail |
| 17 | sprk_billingevent | sprk_matter | sprk_matter | LineageHop | yes | — | detail |
| 18 | sprk_billingevent | sprk_project | sprk_project | LineageHop | yes | — | detail |
| 19 | sprk_budget | sprk_matter | sprk_matter | LineageHop | yes | sprk_budgetnumber |  |
| 20 | sprk_budget | sprk_project | sprk_project | LineageHop | yes | sprk_budgetnumber |  |
| 21 | sprk_budgetbucket | sprk_budget | sprk_budget | LineageHop | yes | — |  |
| 22 | sprk_budgetrevision | sprk_budget | sprk_budget | LineageHop | yes | — |  |
| 23 | sprk_budgetrevision | sprk_matter | sprk_matter | LineageHop | yes | — |  |
| 24 | sprk_communication | sprk_communicationthread | sprk_communicationthread | LineageHop | yes | — |  |
| 25 | sprk_communication | sprk_regardinganalysis | sprk_analysis | LineageHop | yes | — |  |
| 26 | sprk_communication | sprk_regardingbudget | sprk_budget | LineageHop | yes | — |  |
| 27 | sprk_communication | sprk_regardingevent | sprk_event | LineageHop | yes | — |  |
| 28 | sprk_communication | sprk_regardinginvoice | sprk_invoice | LineageHop | yes | — |  |
| 29 | sprk_communication | sprk_regardingmatter | sprk_matter | Stamp | yes | — |  |
| 30 | sprk_communication | sprk_regardingproject | sprk_project | Stamp | yes | — |  |
| 31 | sprk_communication | sprk_regardingreportcard | sprk_reportcard | LineageHop | yes | — |  |
| 32 | sprk_communication | sprk_regardingservicerequest | sprk_servicerequest | Stamp | no | — |  |
| 33 | sprk_communication | sprk_regardingworkassignment | sprk_workassignment | Stamp | yes | — |  |
| 34 | sprk_communicationattachment | sprk_communication | sprk_communication | LineageHop | yes | — |  |
| 35 | sprk_communicationattachment | sprk_document | sprk_document | LineageHop | yes | — |  |
| 36 | sprk_communicationparticipant | sprk_communication | sprk_communication | LineageHop | yes | — |  |
| 37 | sprk_communicationthread | sprk_regardinganalysis | sprk_analysis | LineageHop | yes | — |  |
| 38 | sprk_communicationthread | sprk_regardingbudget | sprk_budget | LineageHop | yes | — |  |
| 39 | sprk_communicationthread | sprk_regardingevent | sprk_event | LineageHop | yes | — |  |
| 40 | sprk_communicationthread | sprk_regardinginvoice | sprk_invoice | LineageHop | yes | — |  |
| 41 | sprk_communicationthread | sprk_regardingmatter | sprk_matter | Stamp | yes | — |  |
| 42 | sprk_communicationthread | sprk_regardingproject | sprk_project | Stamp | yes | — |  |
| 43 | sprk_communicationthread | sprk_regardingservicerequest | sprk_servicerequest | Stamp | no | — |  |
| 44 | sprk_communicationthread | sprk_regardingworkassignment | sprk_workassignment | Stamp | yes | — |  |
| 45 | sprk_decisionrecord | sprk_matter | sprk_matter | LineageHop | yes | sprk_decisionnumber |  |
| 46 | sprk_decisionrecord | sprk_project | sprk_project | LineageHop | yes | sprk_decisionnumber |  |
| 47 | sprk_decisionrecord | sprk_regardingcommunication | sprk_communication | LineageHop | yes | sprk_decisionnumber |  |
| 48 | sprk_decisionrecord | sprk_regardingdocument | sprk_document | LineageHop | yes | sprk_decisionnumber |  |
| 49 | sprk_decisionrecord | sprk_regardingevent | sprk_event | LineageHop | yes | sprk_decisionnumber |  |
| 50 | sprk_decisionrecord | sprk_regardinginvoice | sprk_invoice | LineageHop | yes | sprk_decisionnumber |  |
| 51 | sprk_decisionrecord | sprk_regardingmatter | sprk_matter | Stamp | yes | sprk_decisionnumber |  |
| 52 | sprk_decisionrecord | sprk_regardingproject | sprk_project | Stamp | yes | sprk_decisionnumber |  |
| 53 | sprk_decisionrecord | sprk_regardingservicerequest | sprk_servicerequest | Stamp | no | sprk_decisionnumber |  |
| 54 | sprk_decisionrecord | sprk_regardingtodo | sprk_todo | LineageHop | yes | sprk_decisionnumber |  |
| 55 | sprk_decisionrecord | sprk_regardingworkassignment | sprk_workassignment | Stamp | yes | sprk_decisionnumber |  |
| 56 | sprk_decisionrecord | sprk_workassignment | sprk_workassignment | LineageHop | yes | sprk_decisionnumber |  |
| 57 | sprk_document | sprk_invoice | sprk_invoice | LineageHop | yes | sprk_document_number |  |
| 58 | sprk_document | sprk_matter | sprk_matter | LineageHop | yes | sprk_document_number |  |
| 59 | sprk_document | sprk_parentdocument | sprk_document | LineageHop | yes | sprk_document_number |  |
| 60 | sprk_document | sprk_project | sprk_project | LineageHop | yes | sprk_document_number |  |
| 61 | sprk_document | sprk_relatedagreement | sprk_agreement | LineageHop | yes | sprk_document_number |  |
| 62 | sprk_document | sprk_relatedcommunication | sprk_communication | LineageHop | yes | sprk_document_number |  |
| 63 | sprk_document | sprk_relatedevent | sprk_event | LineageHop | yes | sprk_document_number |  |
| 64 | sprk_document | sprk_relatedinvoice | sprk_invoice | LineageHop | yes | sprk_document_number |  |
| 65 | sprk_document | sprk_relatedmatter | sprk_matter | LineageHop | yes | sprk_document_number |  |
| 66 | sprk_document | sprk_relatedproject | sprk_project | LineageHop | yes | sprk_document_number |  |
| 67 | sprk_document | sprk_relatedservicerequest | sprk_servicerequest | LineageHop | no | sprk_document_number |  |
| 68 | sprk_document | sprk_relatedtodo | sprk_todo | LineageHop | yes | sprk_document_number |  |
| 69 | sprk_document | sprk_relatedworkassignment | sprk_workassignment | LineageHop | yes | sprk_document_number |  |
| 70 | sprk_document | sprk_workassignment | sprk_workassignment | LineageHop | yes | sprk_document_number |  |
| 71 | sprk_emailartifact | sprk_document | sprk_document | LineageHop | yes | — | detail |
| 72 | sprk_emailreviewlog | sprk_communication | sprk_communication | LineageHop | yes | — | detail |
| 73 | sprk_event | sprk_regardingagreement | sprk_agreement | LineageHop | yes | sprk_eventnumber |  |
| 74 | sprk_event | sprk_regardinganalysis | sprk_analysis | LineageHop | yes | sprk_eventnumber |  |
| 75 | sprk_event | sprk_regardingbudget | sprk_budget | LineageHop | yes | sprk_eventnumber |  |
| 76 | sprk_event | sprk_regardingcommunication | sprk_communication | LineageHop | yes | sprk_eventnumber |  |
| 77 | sprk_event | sprk_regardingevent | sprk_event | LineageHop | yes | sprk_eventnumber |  |
| 78 | sprk_event | sprk_regardinginvoice | sprk_invoice | LineageHop | yes | sprk_eventnumber |  |
| 79 | sprk_event | sprk_regardingmatter | sprk_matter | Stamp | yes | sprk_eventnumber |  |
| 80 | sprk_event | sprk_regardingproject | sprk_project | Stamp | yes | sprk_eventnumber |  |
| 81 | sprk_event | sprk_regardingreportcard | sprk_reportcard | LineageHop | yes | sprk_eventnumber |  |
| 82 | sprk_event | sprk_regardingservicerequest | sprk_servicerequest | Stamp | no | sprk_eventnumber |  |
| 83 | sprk_event | sprk_regardingworkassignment | sprk_workassignment | Stamp | yes | sprk_eventnumber |  |
| 84 | sprk_eventlog | sprk_event | sprk_event | LineageHop | yes | — | detail |
| 85 | sprk_fileversion | sprk_document | sprk_document | LineageHop | yes | — |  |
| 86 | sprk_invoice | sprk_matter | sprk_matter | LineageHop | yes | sprk_invoicenumber |  |
| 87 | sprk_invoice | sprk_project | sprk_project | LineageHop | yes | sprk_invoicenumber |  |
| 88 | sprk_invoice | sprk_regardingagreement | sprk_agreement | LineageHop | yes | sprk_invoicenumber |  |
| 89 | sprk_kpiassessment | sprk_matter | sprk_matter | LineageHop | yes | — |  |
| 90 | sprk_kpiassessment | sprk_project | sprk_project | LineageHop | yes | — |  |
| 91 | sprk_kpiassessment | sprk_reportcard | sprk_reportcard | LineageHop | yes | — |  |
| 92 | sprk_memo | sprk_regardingagreement | sprk_agreement | LineageHop | yes | — |  |
| 93 | sprk_memo | sprk_regardinganalysis | sprk_analysis | LineageHop | yes | — |  |
| 94 | sprk_memo | sprk_regardingbudget | sprk_budget | LineageHop | yes | — |  |
| 95 | sprk_memo | sprk_regardingcommunication | sprk_communication | LineageHop | yes | — |  |
| 96 | sprk_memo | sprk_regardingdocument | sprk_document | LineageHop | yes | — |  |
| 97 | sprk_memo | sprk_regardingevent | sprk_event | LineageHop | yes | — |  |
| 98 | sprk_memo | sprk_regardinginvoice | sprk_invoice | LineageHop | yes | — |  |
| 99 | sprk_memo | sprk_regardingmatter | sprk_matter | Stamp | yes | — |  |
| 100 | sprk_memo | sprk_regardingproject | sprk_project | Stamp | yes | — |  |
| 101 | sprk_memo | sprk_regardingservicerequest | sprk_servicerequest | Stamp | no | — |  |
| 102 | sprk_memo | sprk_regardingworkassignment | sprk_workassignment | Stamp | yes | — |  |
| 103 | sprk_memo | sprk_reportcard | sprk_reportcard | LineageHop | yes | — |  |
| 104 | sprk_policy | sprk_matter | sprk_matter | LineageHop | yes | — |  |
| 105 | sprk_policyversion | sprk_policy | sprk_policy | LineageHop | yes | — |  |
| 106 | sprk_project | sprk_regardingrecordid | sprk_matter | Pair | yes | sprk_projectnumber |  |
| 107 | sprk_project | sprk_regardingrecordid | sprk_project | Pair | yes | sprk_projectnumber |  |
| 108 | sprk_reportcard | sprk_regardingmatter | sprk_matter | Stamp | yes | sprk_reportcardnumber |  |
| 109 | sprk_reportcard | sprk_regardingproject | sprk_project | Stamp | yes | sprk_reportcardnumber |  |
| 110 | sprk_signal | sprk_decisionrecord | sprk_decisionrecord | LineageHop | yes | sprk_signalnumber |  |
| 111 | sprk_signal | sprk_matter | sprk_matter | LineageHop | yes | sprk_signalnumber |  |
| 112 | sprk_signal | sprk_regardingcommunication | sprk_communication | LineageHop | yes | sprk_signalnumber |  |
| 113 | sprk_signal | sprk_regardingdocument | sprk_document | LineageHop | yes | sprk_signalnumber |  |
| 114 | sprk_signal | sprk_regardingevent | sprk_event | LineageHop | yes | sprk_signalnumber |  |
| 115 | sprk_signal | sprk_regardinginvoice | sprk_invoice | LineageHop | yes | sprk_signalnumber |  |
| 116 | sprk_signal | sprk_regardingmatter | sprk_matter | Stamp | yes | sprk_signalnumber |  |
| 117 | sprk_signal | sprk_regardingproject | sprk_project | Stamp | yes | sprk_signalnumber |  |
| 118 | sprk_signal | sprk_regardingservicerequest | sprk_servicerequest | Stamp | no | sprk_signalnumber |  |
| 119 | sprk_signal | sprk_regardingtodo | sprk_todo | LineageHop | yes | sprk_signalnumber |  |
| 120 | sprk_signal | sprk_regardingworkassignment | sprk_workassignment | Stamp | yes | sprk_signalnumber |  |
| 121 | sprk_spendsignal | sprk_matter | sprk_matter | LineageHop | yes | — | detail |
| 122 | sprk_spendsignal | sprk_project | sprk_project | LineageHop | yes | — | detail |
| 123 | sprk_spendsignal | sprk_snapshot | sprk_spendsnapshot | LineageHop | yes | — | detail |
| 124 | sprk_spendsnapshot | sprk_matter | sprk_matter | LineageHop | yes | — | detail |
| 125 | sprk_spendsnapshot | sprk_project | sprk_project | LineageHop | yes | — | detail |
| 126 | sprk_todo | sprk_regardingagreement | sprk_agreement | LineageHop | yes | — |  |
| 127 | sprk_todo | sprk_regardinganalysis | sprk_analysis | LineageHop | yes | — |  |
| 128 | sprk_todo | sprk_regardingbudget | sprk_budget | LineageHop | yes | — |  |
| 129 | sprk_todo | sprk_regardingcommunication | sprk_communication | LineageHop | yes | — |  |
| 130 | sprk_todo | sprk_regardingdocument | sprk_document | LineageHop | yes | — |  |
| 131 | sprk_todo | sprk_regardingevent | sprk_event | LineageHop | yes | — |  |
| 132 | sprk_todo | sprk_regardinginvoice | sprk_invoice | LineageHop | yes | — |  |
| 133 | sprk_todo | sprk_regardingmatter | sprk_matter | Stamp | yes | — |  |
| 134 | sprk_todo | sprk_regardingproject | sprk_project | Stamp | yes | — |  |
| 135 | sprk_todo | sprk_regardingreportcard | sprk_reportcard | LineageHop | yes | — |  |
| 136 | sprk_todo | sprk_regardingservicerequest | sprk_servicerequest | Stamp | no | — |  |
| 137 | sprk_todo | sprk_regardingworkassignment | sprk_workassignment | Stamp | yes | — |  |
| 138 | sprk_workassignment | sprk_regardingevent | sprk_event | FilingCore | yes | sprk_workassignmentnumber |  |
| 139 | sprk_workassignment | sprk_regardinginvoice | sprk_invoice | FilingCore | yes | sprk_workassignmentnumber |  |
| 140 | sprk_workassignment | sprk_regardingmatter | sprk_matter | FilingCore | yes | sprk_workassignmentnumber |  |
| 141 | sprk_workassignment | sprk_regardingproject | sprk_project | FilingCore | yes | sprk_workassignmentnumber |  |

## Tests

The test scope is the POML's guards and controls only. Two unit tests go one line beyond the listed exclusions:

- the grant-row table and `sprk_timekeeper`, which are in the exclusion theory under "people and organization lookups,
  grant rows" from design §12.2;
- the citation lookups, which are in spec FR-29's excluded list.

| Suite | Result |
|---|---|
| `RecordFamilyEdgesGuardTests` (ArchTests) at `43e8cf0402` | 13/13 pass |
| `RecordFamilyEdgesTests` (unit, theories expanded) at `43e8cf0402` | 41/41 pass |
| `verify-suites.sh` at `b42d0109b1` | Spaarke.sln: 0 warnings, 0 errors. Filtered Sprk.Bff.Api.Tests: 5659 pass, 7 skipped, 0 fail. ArchTests: 1586 pass, 1 fail (`DocumentLinkVocabularyGuardTests`, fixed below). |
| At `43e8cf0402` | ArchTests (full): 1590/1590 pass. Targeted ArchTests (`RecordFamilyEdges`, `DocumentLinkVocabulary`, `SecureChildLineage`, `SecureFamilyConsistency`): 22/22 pass. Targeted units (`RecordFamilyEdges`, `CoreAncestor`, `SecureChildLineage`, `DocumentLinkFields`): 279/279 pass. |

Since `a38396afcf`, two tests were added to the guard file and two changed in the unit file:

- Guard file:
  - `DetailTables_LeaveTheIsolationViewsUnchanged`;
  - the ownership-lineage guard: 1 fact plus 2 controls.
    - Must-fire: with the `sprk_analysis.sprk_documentid` row removed, the gap list is exactly
      `sprk_analysis.sprk_documentid -> sprk_document`. That is the defect UAC change 1 fixed.
    - Must-not-fire: a citation, a non-parent target and a baselined lookup produce no gap. The main fact passes on
      the current registry.
- Unit file: the Matter → Project test is reshaped, and the tautological record-number test is deleted. It was redundant: `Build()` sets every row's `RecordNumberColumn` from `RecordNumberColumns` by construction, so the test restated the code. The map's content was checked against every table's `Entity.xml` by the independent review (all record-number columns covered; version ordinals and `sprk_regardingrecordnumber` mirrors excluded on purpose). No unit test reads solution files. UAC-r2 review: OK for task 070 at 810c741e85 (2026-10-28).

The guard tests:

- 1 parity fact;
- 3 parity controls: must-fire on an edge only in the source, must-fire on an edge only in the registry, and baselined
  passes / stale fails;
- 1 pin fact;
- 2 pin controls: each of the six columns fires with the "UAC-r2 review:" text, and RecordNumberColumn does not fire;
- 1 C2 fact;
- 1 C2 control: the same row as Filing changes both views.

### Fail-before evidence (mutation runs, restored afterwards)

There is no "old file" for a new registry, so I mutated the new code instead:

- **M1: delete the `sprk_budgetbucket` row.** The parity fact failed with
  `NEW: SecureChildLineage: sprk_budgetbucket.sprk_budget -> sprk_budget — in SecureChildLineage, not in
  RecordFamilyEdges`, and the pin failed with `- Filing | sprk_budgetbucket.sprk_budget -> sprk_budget | LineageHop |
  secure`. This is the UAC-194 drift the guard exists for.
- **M2: remove the `FamilyKind == kind` filter from `Of`.** The C2 fact failed: the non-Filing row reached the
  isolation views.
- The pin, before it was filled in, failed on all 139 rows; the parity fact, before the `sprk_memo` baseline line was
  added, failed on that one NEW line.

## Publish size and CVE

- Publish delta, re-measured on the code head: `publish-delta.ps1 -MasterSha 078f62bcad -BranchSha 43e8cf0402`, zipped
  with Compress-Archive.
  - master: 39,729,337 bytes, 192 files;
  - branch: 39,732,496 bytes, 192 files;
  - **delta +3,159 bytes**, total 37.89 MB.
  - The first measurement, at `b42d0109b1`, was +2,866 bytes.
- CVE: `dotnet list package --vulnerable --include-transitive` reports that "The given project `Sprk.Bff.Api` has no
  vulnerable packages given the current sources."

## Quality gates (Step 9.5)

Code review covered the three files.

- **F2 (fixed, caught by an existing guard):** the first version spelled out the `sprk_document` link columns
  literally. `DocumentLinkVocabularyGuardTests` failed: the vocabulary is declared once, in `DocumentLinkFields`. The
  document rows are now derived from that map, minus identities and the email, exactly as `SecureChildLineage` does,
  with the service-request link's `InSecureFamily = false`. The content pin still pins every derived row, so a new
  link in the map fails the pin until UAC-r2 reviews it. The rows are unchanged (139), and the pin needed no update.
  Along the way a static-initialiser order bug appeared (the new set was declared after `All = Build()`, a
  `TypeInitializationException`). It is fixed and documented on the field.
- **F2 (fixed):** `ParityProblems_RealBaseline_DescribesOnlyRealDisagreements` duplicated the main parity fact, which
  already fails on stale lines, and went beyond the POML's test scope. I removed it.
- **Review round 1 (independent review of `a38396afcf`), fixed in `43e8cf0402`:**
  - F2, docs: the registries are "checked against" this one, not views of it. `SecureLineageView` is parity-check only:
    it omits narrowing-only lookups, so swapping it in would widen a mirror, which is fail-open.
  - F2, second copies: the core constants are now derived from `CoreAncestorResolver`, with one order comment.
  - Weak test: the Matter → Project test is reshaped, and the tautological test is deleted.
- **K1:** `RecordFamilyEdges.All` is unfiltered. A future reader could take it instead of `Of(Filing)`. Every row is
  Filing today. When a second kind lands, add a source-scan guard.
- **K1:** the "UAC-r2 review:" PR line is not mechanically enforced. The pin's failure message requires it; a reviewer
  checks the PR.
- **K3:** `CoreAncestorChildrenView`'s core-parent filter is not exercised by a mutation run. Its C2 test and control
  cover the Filing filter only.
- **K4:** The pin and parity keys are lower-cased, so a case-only change to a logical name is not caught. Dataverse
  logical names are always lower-case.
- Metrics: `RecordFamilyEdges.cs` is about 300 lines of declarative data plus two views. It is cohesive: one
  responsibility, the edge list. No interface, no constructor, no try/catch.

adr-check:

- ADR-002: no plugin, nothing on the write path.
- ADR-010: no DI registration (static data).
- ADR-013: no AI dependency.
- ADR-038: tests at KEEP paths (`tests/unit/domain/**`, `tests/Spaarke.ArchTests/**`), with FluentAssertions in the
  unit suite.
- ADR-001 / ADR-008: no endpoint.
- No violations.

## Placement Justification and §11 (for the PR)

**Placement:** in the BFF, `Services/Dataverse`, beside `CoreAncestorResolver`. It is static data that the BFF's own
access walks, family reader (071) and push (040) consume. It has no endpoint, no DI, no package and no background work.

**§11 answers:**

- **Existing:** seven registries in five files:
  - `SecureChildLineage.Children`;
  - `SecureRootInheritance.TypedFilingColumns`, `IntermediateFilingColumns` and the BFF-managed pair;
  - `CoreAncestorResolver.IntermediateRootColumns` / `StampSourceColumns`;
  - `RecordOwnershipResolver.OwnershipParentEntities`;
  - `ExternalDataService.ServiceRequestRelations`.

  Overlaps:
  - `CoreAncestorResolver`'s core set, stamp lookups and pair columns. These are now derived from it, not restated.
  - The Dataverse catalog column `sprk_recordtype_ref.sprk_regardingrecordnumberfield` (`Entities/sprk_RecordType_Ref/Entity.xml:643`).
    It is a per-record-type number field. `RecordNumberColumns` covers every family table, including those with no
    record-type row, so it stays code-side and display-only.
- **Extension:** no. Each serves one walk with its own shape. This registry is their checked union, and the parity test
  leaves them untouched.
- **Cost of doing nothing:** 071 and 040 would each declare an eighth copy of the hierarchy, and drift like the missing
  `sprk_budgetbucket` row goes undetected (mutation M1 shows the guard catching exactly that).

## Rework line

Rework:

- 2 self-review fixes: the document rows derived from `DocumentLinkFields`, and the redundant test removed.
- Review round 1: 3 F2 items fixed.
- UAC-r2 content ruling: 2 rows added, 1 guard added, `DetailTables` added, and the wording changes.

Rows went 139 → 141; the baseline stayed at 16. No escalation fired. UAC-r2 kept the service-request rows, and UAC
re-checks the final head.
