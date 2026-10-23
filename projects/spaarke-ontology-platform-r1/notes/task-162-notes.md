# Task 162 notes — wizards bind an uploaded document through the exact parent column

Lane: `task/162-doc-exact-column` (worktree C:\wts-162). Code complete; POML step 4 (deploy + dev check) is NOT done and belongs to the main session.

## Rigor / model
FULL, sonnet @ high, directional steps.

## Escalation trigger: did not fire
Live check on spaarkedev1 (read-only GETs, 2026-10-22). The model-driven main forms' Documents subgrids use the PARENT column `sprk_{parent}`, not `sprk_related{parent}`:

| Form | Subgrid relationship | Referencing column | Nav prop |
|---|---|---|---|
| Work Assignment main form | `sprk_WorkAssignment_Document_1n` | `sprk_workassignment` | `sprk_WorkAssignment` |
| Project main form | `sprk_Project_Document_1n` | `sprk_project` | `sprk_Project` |
| Invoice main form | `sprk_document_Invoice_n1` | `sprk_invoice` | `sprk_Invoice` |
| Matter main form | (no sprk_document subgrid found in the form XML; the `sprk_matter_document` relationship is the parent column `sprk_matter`, nav `sprk_Matter`) | `sprk_matter` | `sprk_Matter` |

The twin lookups (`sprk_relatedworkassignment` / `sprk_relatedinvoice` / `sprk_relatedproject` / `sprk_relatedmatter`, nav `sprk_Related*`) exist alongside. Among sprk_event, sprk_todo, sprk_workassignment, sprk_invoice, sprk_project, sprk_matter, sprk_reportcard and sprk_communication, no entity has two lookups to the same sprk_ parent entity (so the ambiguity is specific to sprk_document).

## Open owner question (not decided here, per the task constraint)
What is the role of `sprk_related{parent}` on sprk_document? The wizards now set only the parent column `sprk_{parent}`. If `sprk_related{parent}` is meant to be the polymorphic "regarding"/related pointer (as `sprk_relatedevent` and `sprk_relatedtodo` are the ONLY lookups for events and to dos), should a wizard-created document set both? Until decided, a wizard-created document has `sprk_related{parent}` empty.

## Change
- `PolymorphicResolverService.findNavProp`: with several relationships to the entity, an entry whose `columnName` equals the hint (case-insensitive) wins; then the existing substring match; then the existing fallback. A single relationship is returned as before.
- `workAssignmentService.ts`: `_resolveDocNavProp` removed; the document bind uses `findNavProp(docNavProps, 'sprk_workassignment', 'sprk_workassignment')`. The not-found fallback is now the live nav prop `sprk_WorkAssignment` (it was the bare entity name, which is not a valid nav prop).
- `invoiceService.ts`: the invoice bind passes the full column `sprk_invoice`; the host (Matter/Project) bind passes the host entity name as the full column (`sprk_matter` / `sprk_project`). The host bind previously used the fragment `matter` / `project`, which both columns contain, so a host-bound document had the same ordering bug.
- `projectService.ts`: the local `_findNavProp` copy is retired in favour of the shared `findNavProp` (one helper). It is used for the project CREATE payload lookups only (contact `attorney` / `paralegal` fragments, single-match lookups); behaviour is unchanged apart from exact-first.

## Step-3 keep/fix list (document-to-parent binds and findNavProp callers)

| Site | Bind | Verdict |
|---|---|---|
| `CreateWorkAssignmentWizard/workAssignmentService.ts` document bind | was first match by referenced entity | FIXED |
| `CreateInvoiceWizard/invoiceService.ts` invoice bind (hint `invoice`) | fragment | FIXED |
| `CreateInvoiceWizard/invoiceService.ts` host bind (hint `matter`/`project`) | fragment | FIXED (found in step 3; same bug) |
| `CreateProjectWizard/CreateProjectWizard.tsx:790` | hard-coded `'sprk_Project'` (no discovery) | KEEP: it is the live nav prop of the parent column `sprk_project`; no ordering dependence. Deviation from the POML wording "both orders" for the project wizard: there is no relationship order to test. |
| `CreateMatterWizard/matterService.ts:380` | `_resolveNavProp(map, 'sprk_matter')` exact column | KEEP (the reference) |
| `solutions/LegalWorkspace/.../CreateMatter/matterService.ts:261` | same exact-column resolution | KEEP |
| `CreateTodoWizard/todoService.ts:328` | exact column `sprk_relatedtodo`, fallback `sprk_RelatedToDo` | KEEP (todo has only the related column) |
| `CreateEventWizard/CreateEventWizard.tsx:69` | constant `sprk_RelatedEvent` | KEEP (event has only the related column) |
| `DocumentUploadWizard/uploadOrchestrator.ts:641-693` + `document-upload/DocumentRecordService.ts:261` | resolves by exact RELATIONSHIP schema name via the BFF navmap, hard-coded nav-prop fallback | KEEP (order independent). Note for the operator: the invoice entry's `relationshipSchemaName: "sprk_invoice_document"` does NOT exist on dev (the live relationship is `sprk_document_Invoice_n1`), so invoice uploads always take the hard-coded `sprk_Invoice` fallback and log a warning. Works; the schema name is wrong. Not changed (out of scope). |
| `PolymorphicResolverService.applyResolverFields` (:621) + `workAssignmentService`/`reportCardService`/`eventService`/`invoiceService` create-payload lookups with fragment hints | regarding lookups on the NEW record, not documents | KEEP: none of the checked host entities has two lookups to the same sprk_ parent, and a fragment hint is not any column's full name, so exact-first changes nothing |
| `workAssignmentService.ts:645` follow-on event → work assignment (`'workassignment'`) | event lookup | KEEP (single lookup) |
| `PolymorphicResolverService.findHostNavPropForLookup`, `TodoRegardingUpdateBuilder`, `ResolverWriteHandler` | exact column first, then entity+hint | KEEP |
| `CreateEventWizard/eventService.ts` local `_findNavProp` | event create lookups | KEEP: a third local copy remains (not in the brief's list); candidate for the same retirement later |

## Findings reported, not fixed (out of scope)
1. `CreateInvoiceWizard/invoiceService.ts:376` carries an unreachable `else if (uploadedFiles.length > 0 && !this._containerId)` arm (its `if` is `uploadedFiles.length > 0`), which fails `eslint` (`no-dupe-else-if`). Pre-existing since b026daf4891. Task 076 deleted the same arm from the matter and work-assignment services. Fix: delete the arm (and `_containerId` if nothing else reads it).
2. `DocumentUploadWizard` invoice `relationshipSchemaName` is wrong (above).
3. A third local `_findNavProp` copy in `CreateEventWizard/eventService.ts`.

## Tests
- New: `services/__tests__/PolymorphicResolverService.findNavProp.test.ts` (7), `CreateWorkAssignmentWizard/__tests__/workAssignmentService.documentBind.test.ts` (3), `CreateInvoiceWizard/__tests__/invoiceService.documentBind.test.ts` (7).
- The tests drive `createWorkAssignment` / `createInvoice` and read the nav prop handed to `EntityCreationService.createDocumentRecords` (the upload and the document create are spied; nav-prop discovery runs over a stubbed `fetch` in both relationship orders).
- Before the change (old source files restored): 7 failed / 10 passed of 17. After: 17 / 17 pass. Full Spaarke.UI.Components jest: 314 suites, 5181 tests, all pass.
- `tsc --noEmit`: clean. ESLint on changed folders: 0 new problems; the one error is the pre-existing item 1.
- Builds that bundle these wizards (local): CreateWorkAssignmentWizard, CreateInvoiceWizard, CreateProjectWizard (vite) and SpaarkeAi (check gates + vite + ribbon) all build.

## Quality gates (Step 9.5)
Small diff (3 source files + 3 tests), reviewed inline against the contract; ADR-012 respected (no Xrm added). No F-class finding. K-class: the project wizard's hard-coded `sprk_Project` is not discovery-driven (K2: correct on live metadata).

## Still to do (main session, POML step 4)
Deploy per task 114's path and, on dev, create a work assignment, an invoice and a project each with one file; confirm each file shows on the parent's Documents list; record in `notes/deploy-log.md` with the environment named; delete the zz- test records.

rework: none task 162
