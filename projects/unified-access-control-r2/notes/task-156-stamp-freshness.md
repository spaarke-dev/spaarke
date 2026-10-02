# Task 156: keep the core-ancestor stamp fresh, and compare it before trusting it

Owner round 4 item 5 (2026-10-01) chose **option (b)** for task 155's escalation trigger 2: re-stamp the children whenever
the record they hang off is re-filed. Binding with it: ADR-003 fail closed; owner C10 part 2 (children of a secure record
are secure); round 3 R3/R4 (minutes, not hours); ADR-002 (no plugins; writes outside the BFF are fixed up by the BFF).

## The problem in one paragraph

A to-do filed under a communication carries a **copy** of the communication's project / matter / work assignment in its
own `sprk_regarding{core}` columns (`CoreAncestorResolver`, FR-26). The copy is what makes access inheritance one hop,
and what task 155's storage resolver would have read. Nothing refreshed it. Re-file the communication from ordinary
matter A to SECURE matter B and the to-do still says "A": an access over-grant to A's principals (task 051 §1) and, for a
resolver that trusted the copy, a #1038 leak into A's shared container. Task 155 therefore refused every upload to a child
filed under another record (`container_ancestor_unverifiable`). This task replaces that refusal.

## What changed

| Piece | Where | What it does |
|---|---|---|
| **Derivation extended** | `Services/Dataverse/CoreAncestorResolver.cs` | A child's stamp is the root of WHATEVER it is filed under. `IntermediateRootColumns` names each intermediate's root columns: the four `sprk_regarding{core}` for communication / event / to-do / analysis; typed `sprk_matter` / `sprk_project` for invoice and budget; typed `sprk_matter` / `sprk_project` / `sprk_workassignment` (and their `sprk_related*` twins) for document; `sprk_regardingmatter` / `sprk_regardingproject` for agreement and report card. Before, invoice and document yielded **no** stamp (they lack the four columns) and agreement / budget / report card were Unclassified. Two different roots of one type on one row (a document's `sprk_matter` vs `sprk_relatedmatter`) is a derivation ERROR — never a guess. |
| **The shared topology** | same file | `StampSourceColumns` (the four tables that carry a copy, and the columns that name its source), `PartyRegardingColumns`, and `ClassifyStampSource` — the ONE rule the cascade, the job and the resolver use to decide which record a row's copy comes from (below). |
| **The cascade** | `Services/Dataverse/CoreAncestorRestamper.cs` (new) | `AfterWriteAsync(entity, id, writtenColumns)`: after a BFF write that changed what a record is filed under (its own source or pair) or its root, re-stamp the record itself and/or every child copying it, transitively (bounded at depth 4; a cycle converges because it recurses only below a copy it just changed), in the same operation. Writes ONLY the stamp columns of the root types the source can carry. A child that fails is reported, never thrown; the caller's own write stands. A write that cannot move a stamp reads nothing (`WriteCanMoveAStamp`). |
| **The job** | `Services/Dataverse/CoreAncestorStampReconciliationJob.cs` (new) | ADR-036 `IScheduledJob`, every 5 minutes (`*/5 * * * *`), registered with `AddScheduledJob` in `AddDataverseMetadataServices`. Scans each stamped table (FetchXML, paged, bounded — and, since verifier round 1, CONTINUED by the next run when the page bound stops it), classifies every row, and repairs the stale ones through the restamper (which cascades). Also clears copies orphaned by a regarding cleared on a form (F-051-6) — found by the pair's type when it names an intermediate, or by the pair's id when it has no type (communication / agreement); a row written with NO pair cannot be found (verifier round 1 item 5, owner decision below). A failed scan is a FAILED run, never "0 stale". A claim per repair + a completion marker scoped to the run (A1 rule 3; interpretation xix), one heartbeat per attempt (A1 rule 5). Writes on by default; `CoreAncestor:StampReconciliation:WritesEnabled=false` (or an unparseable value) = report-only dry run. |
| **The enqueue** | `Services/Dataverse/CoreAncestorRestampQueue.cs` + `CoreAncestorRestampJobHandler.cs` (new) | ADR-004 `IJobHandler` (`CoreAncestorRestamp`) on the shared `sdap-jobs` queue (ADR-052: queue work → `IJobHandler`). The resolver enqueues a stale row; the user-OBO AI tool enqueues its after-write cascade. Best effort: a failed enqueue is logged and the job repairs within one cycle. Service Bus is resolved lazily, so composing the resolver never needs Service Bus configuration. |
| **The live comparison** | `Infrastructure/Dataverse/RecordContainerResolver.cs` | Task 155's held branch is replaced (next section). New reason code **`container_ancestor_stale`** (409). New links entries for analysis, document, agreement, budget and report card (swept live, below). |
| **Wiring** | the BFF paths in the inventory | Each calls `AfterWriteAsync` (or enqueues it) after its own write succeeded. |

## The rule: which record a row's copy comes from (`ClassifyStampSource`)

The row's polymorphic pair (`sprk_regardingrecordid`) says what the user filed it under; every regarding builder writes it
with the typed lookup.

1. No source column set → **not filed under an intermediate**: its root columns are its own.
2. The pair names a ROOT column set on the row → **a direct, user-chosen link** (the Office carrier to-do). Every source
   column set is a **carrier**. Nothing on the row is a copy, so nothing is ever re-stamped.
3. The pair names a source column set on the row → **that record is the source**. Other source columns are carriers.
4. The pair names nothing the row carries (or is not a GUID) → **inconsistent**: nothing is written; the resolver refuses
   (pair rule 4, ambiguous).
5. No pair (or a pair naming the row's own typed party): one source column → it is the source (`TaskActionCore` and an
   Ambiguous inbound association write no pair); two or more → **ambiguous**: nothing is written; the resolver refuses.

A service request is CORE: a to-do's `sprk_regardingservicerequest` is a root column, not a source, and the resolver still
HOLDS it (`container_ancestor_unverifiable`) because a service request cannot carry `sprk_issecure`.

## The resolver's comparison (goal point 3)

For a to-do / event / communication filed under an intermediate:

- The source is read **live, once** (its root columns, its own links, and its flag + container when its type can be
  secure — an invoice). Its root is compared with the row's copy, for the root types the source can carry.
- **Equal** → the copy is followed exactly like a direct root link (secure root → its own container; otherwise the
  record's business-unit container, or the communication archive).
- **Different** (incl. a missing copy, or a copy whose source no longer names a root) → **409 `container_ancestor_stale`**,
  nothing resolved, the stale row enqueued. A retry succeeds once the re-stamp lands.
- **Unreadable** source → 503 `container_ancestor_unresolved`; **missing** → 409. Never "no root".
- **Transitive**: the source is processed by the same rules, so if IT is filed under another record its copy is compared
  in turn; a stale copy anywhere refuses and THAT row is enqueued (an event above a to-do, say). A filing loop refuses 409.
  The walk's existing bounds (4 deep, 8 rows) now count intermediate reads too.
- **A securable source that is itself secure** (an invoice with its own flag) is a secure root in its own right.
- **Office carrier** (pair = the direct matter / project; document or email as carrier): each carrier is read live. Its root
  equal to the direct link, or naming none → resolves through the direct link. Different and ANY root on either branch
  secure → `container_ancestor_ambiguous`. Different and none secure → the direct link (the user chose it).
- The analysis's NOT NULL `sprk_documentid` (the document it analyses) and `sprk_outputfileid` (its output) are read as
  carriers when an analysis is the source.

Still HELD (unchanged from 155): a service request anywhere; an intermediate column on a row that carries no copy at all —
a work assignment, project or contact, and an invoice / document / agreement filed under another record (their typed roots
are their own links, so there is nothing to compare). A pair ALONE naming an intermediate (no typed column) stays held: the
restamper writes copies only from typed source columns, so comparing would refuse it stale forever.

`IncomingCommunicationProcessor.IsPermanentContainerRefusal` does NOT list `container_ancestor_stale`: on the inbound path it
is transient and retried, which is right — the enqueued re-stamp makes the retry succeed.

## Step 1 — inventory of BFF paths that change an intermediate's root (from code, 2026-10-02)

Children exist only for an EXISTING record, so only update paths matter; a create-time stamp is applied by the writer's own
`CoreAncestorResolver.StampAsync`, which now derives the task-156 intermediates too.

| Intermediate | BFF path | Disposition | Evidence |
|---|---|---|---|
| sprk_document | `PUT /api/v1/documents/{id}` (`UpdateDocumentRequest.MatterLookup` / `ProjectLookup` / `WorkAssignmentLookup`) | **wired** — `AfterWriteAsync("sprk_document", …, DocumentColumnsWritten(request))` | `Api/DataverseDocumentsEndpoints.cs` PUT handler |
| sprk_document | `POST /api/ai/document-intelligence/associate-record` (`DocumentAssociationMap.TryApply`) | **wired** | `Api/Ai/RecordMatchEndpoints.cs` `AssociateRecord` |
| any (generic) | UpdateRecord playbook node + `ActionSeam` (`UpdateRecordActionCore` → `UpdateRecordFieldsAsync`) | **wired** — resolves the restamper from its scope only when `WriteCanMoveAStamp` | `Services/Ai/Nodes/ActionCore/UpdateRecordActionCore.cs` |
| any (generic) | playbook output orchestrator (`DataverseUpdateHandler`, both concurrency branches) | **wired** | `Services/Dataverse/DataverseUpdateHandler.cs` |
| any (generic) | field-mapping push (`POST /api/v1/field-mappings/push`) — a rule can write a lookup on each child | **wired** per child | `Api/FieldMappings/FieldMappingEndpoints.cs` `ApplyMappingsToChildRecordsAsync` |
| any (generic) | AI tool `dataverse.update_record` (`DataverseUpdateRecordHandler`, user-OBO) | **enqueued** (`CoreAncestorRestampQueue.EnqueueAfterWriteAsync`) — see "Decisions" | `Services/Ai/Handlers/DataverseUpdateRecordHandler.cs` |
| sprk_communication | `IncomingAssociationResolver.ApplyDecisionAsync` | **create-time only — no cascade**: both callers run it on a communication created in the same operation (`IncomingCommunicationProcessor` step 4.5 after `CreateCommunicationRaceProofAsync`; `EmailUploadCaptureService` only when `wasDuplicate` is false), so no child can exist. Its stamp now also derives budget / report card targets (`IsStampSourceEntity`). | `Services/Communication/IncomingCommunicationProcessor.cs` ~345; `EmailUploadCaptureService.cs` ~85-105 |
| sprk_communication | `POST /api/communications/{id}/suggest-associations` | read-only preview (`EvaluateAsync`) | `Api/CommunicationEndpoints.cs` |
| sprk_communication | enrichment / triage (`CommunicationEnrichmentService` :601, :1662), delivery merge, cross-path link | no root column written (triage fields, owner, mailbox list, `sprk_relatedcommunication` on a document) | the three files |
| sprk_event | `PUT /api/v1/events/{id}` | writes the pair only, never a root column — the event's root is unchanged (a pair change is the job's) | `Api/Events/EventEndpoints.cs` `UpdateEventInDataverseAsync` |
| sprk_invoice | Finance `InvoiceReviewService` (creates a NEW invoice), `InvoiceExtractionJobHandler` (amount fields) | create-time / no root column | `Services/Finance/InvoiceReviewService.cs` :296; `Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs` :558 |
| sprk_document | Office save / upload finalization / email attachments / Compose (`OfficeDocumentPersistence`, `UploadFinalizationWorker`, `EmailAttachmentProcessor`, `ComposeRecordResolution`) | create-then-associate in one operation (the document id is not known to anyone else yet) or no root column (file metadata, canonical link) | the four files |
| sprk_analysis | `AnalysisEndpoints`, `AppOnlyAnalysisService`, `WorkingDocumentService` | create-time / document profile fields / working text | the three files |
| sprk_agreement / sprk_budget / sprk_reportcard | none in the BFF (client wizards only) | the job (writes outside the BFF) | grep, 2026-10-02 |
| all | client `Xrm.WebApi` re-files (`ConnectionsWriteHandler`, wizards, RegardingResolver PCF), MDA forms, flows, imports | the job (ADR-002 WP-5) | `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` §4.3 |

**Children stamped from each intermediate** (`CoreAncestorResolver.StampSourceColumns`, from the 155 f3 / f4 sweeps and
this task's `sprk_analysis` sweep):

| Intermediate | Child tables (column) |
|---|---|
| communication | to-do, event, analysis (`sprk_regardingcommunication`) |
| event | to-do, event, communication (`sprk_regardingevent`) |
| invoice | to-do, event, communication, analysis (`sprk_regardinginvoice`) |
| analysis | to-do, event, communication (`sprk_regardinganalysis`) |
| document | to-do, analysis (`sprk_regardingdocument`) |
| agreement | to-do, event (`sprk_regardingagreement`) |
| budget | to-do, event, communication, analysis (`sprk_regardingbudget`) |
| report card | to-do, event, communication (`sprk_regardingreportcard`) |

Work assignment, project, contact, invoice and document carry no `sprk_regarding{core}` stamp columns, so they are never
re-stamped (a work assignment's matter is its own filing — task 155 interpretation iii).

## Live checks (spaarkedev1, read-only, 2026-10-02)

- **Escalation trigger 2 did not fire.** The most stamped children under any one intermediate is **2** (invoices
  a1652ca5 and 9050d1b8, events b52562e5 and fad64fb5). Live totals filed under an intermediate: 14 to-dos, 1 event, 5
  communications, 0 analyses. The cascade is still bounded (`MaxChildrenPerSource` 5000 per table, depth 4) and reports
  TRUNCATED past it; the job finishes and the resolver refuses meanwhile.
- **Escalation trigger 1 did not fire.** No re-parent path needs a transaction Dataverse lacks: the intermediate's write and
  the children's PATCHes are separate, but in the window between them the resolver's live comparison REFUSES a stale copy
  (409 stale), it never misfiles.
- **Swept for this task** (Dataverse MCP `describe`, every lookup and its target): `sprk_analysis` (27 lookups),
  `sprk_document` (35), `sprk_agreement` (22), `sprk_budget` (11), `sprk_reportcard` (19). New non-owner targets:
  `sprk_analysisaction`, `sprk_agreementtype`, `sprk_analysisplaybook`, `sprk_container`, `sprk_fileversion`. New
  intermediate: the OOB `email` activity (a document's `sprk_email`). `sprk_document` has a `sprk_regardingrecordid`
  string but **no** `sprk_regardingrecordtype` (5 live rows set it; 4 equal a typed root, 1 — `25413cf3` — names the deleted
  matter c4ef17ed and would refuse 409 if a child were filed under it). 0 documents set any `sprk_related{matter,project,
  workassignment}`; 131 of 531 set an intermediate link (held when read as a source). 0 agreements set
  `sprk_regardingdocument`. All 3 report cards set the pair equal to their typed matter.
- **First job run on dev (simulated read-only from the rows above; writes ON).** 14 rows re-stamped, all to non-secure
  matters (b68299c6, 2444af6d — `sprk_issecure` NULL): 8 to-dos under invoices (1b32926a, 9fb4ece2, 31c9680c, b70b7dab,
  33c9680c, 9250d1b8, 36c9680c, 6d67b203), 2 to-dos under event fad64fb5 (b856b1ed, 15d2a80b), to-do a01477e8 under report
  card 9d1477e8, communications 3b7b5825 (event 8a6b371f), 83349fe9 and a62a5f02 (invoice 55328b00). Their uploads answer
  `container_ancestor_stale` until then and resolve the business-unit container / archive afterwards (all were
  `container_ancestor_unverifiable` under 155). The access consequence is the intended one: those children start inheriting
  their matter's access (C10 part 2). Five rows whose source names no root at all (to-dos 1432926a, 736a6eb7, bc19baa5,
  event edfef460, communication a36784ef) resolve immediately. Communication 1d43505d (direct matter + invoice carrier under
  a different, non-secure matter) keeps the archive. No live record moves into or out of a secure container: the one secure
  root (project 65a3fab2) has nothing filed under it.

## Decisions and owner-reversible interpretations

- **(xii) The client stamp mirror is not changed.** `PolymorphicResolverService.deriveCoreAncestorStamps` (TypeScript) still
  derives only the four columns, so a CLIENT-created child under an invoice / document / agreement / budget / report card is
  created without a copy (fail closed — it inherits nothing) and the job stamps it within one cycle (ADR-002 WP-2: the server
  owns the invariant; the client previews). The taxonomy literals pinned to the TypeScript side (`CORE_RECORD_ENTITIES`,
  `CHILD_RECORD_ENTITIES`) are untouched. *Reverse / follow-up:* mirror `IntermediateRootColumns` in the TS derivation.
- **(xiii) A row with a root, ONE intermediate and NO pair is a copy** (rule 5), because every writer that sets a direct root
  together with an intermediate also writes the pair (the Office save, `IncomingAssociationResolver`'s priority — matter /
  project before invoice / event — the outbound sender, the client RegardingResolver). *Reverse:* treat such rows as direct
  links in `ClassifyStampSource` (they would then never be re-stamped, and the resolver would apply the carrier rule).
  **Corrected in verifier round 1 (item 9):** the premise did not hold for an Ambiguous inbound association (no pair) once
  an operator widens `CoreWritableEntities` to an intermediate (the shipped set writes roots only). That writer now makes
  the premise true — see (xxv).
- **(xiv) The communication's invoice is compared, not followed** — task 155 f4 interpretation (viii) is superseded. An email
  under an invoice now needs its copy of the invoice's root (IncomingAssociationResolver stamps it at create; the job stamps
  older rows). A pair ALONE naming an invoice is held (f5 V15 followed it). A secure invoice still decides by its own flag.
- **(xv) An inconsistent pair is never written from** — the job skips it, the resolver refuses it (ambiguous).
- **(xvi) F-051-6 orphans are cleared only when the copy still equals the cleared record's current root.** A copy that does
  not match may be a direct choice made after the clear, so it is left alone and the resolver refuses the row (pair rule 4).
- **(xvii) The user-OBO AI update tool enqueues its cascade** instead of running it: `DataverseUpdateRecordHandler` is bound
  by its own MUST rule ("no app-only client is reachable from this class"), and the re-stamp is an app-only, server-owned
  write. The enqueue holds no Dataverse client; the job runs seconds later and the resolver refuses a stale copy meanwhile.
  This is the one inventory path not re-stamped "in the same operation" — flagged in the task result for the owner.
  **Still open after verifier round 1 (item 6): AC1 is not met for this path until the owner accepts the deviation** — the
  🔔 block is in the verifier round 1 section below.
- **(xviii) Document `sprk_canonicaldocument` is held, and an analysis's `sprk_documentid` / `sprk_outputfileid` are
  carriers**, by the 155 f3 rule (a column whose target can hang off a root is read and checked, or refused).
- **(xix) The job's completion marker (ADR-036 A1 rule 3) is scoped to the RUN** (`{job}:{entity}:{id}:{planHash}:{runId}`);
  the claim is not. Found in the Step 9.5 review: keyed by row and value alone, a row put back out of band within 30
  minutes of a repair to the same value was reported `alreadyApplied` and left stale until the marker expired — which is
  exactly the manual live gate's second step. A retry of the same run still does not re-apply; the next run repairs.
- **(xx) An after-write cascade enqueued by the AI update tool is never de-duplicated** (one message per write). A stale
  refusal still collapses per child per minute (a user retrying an upload is one repair).
- **(xxi) The document's links ARE the shared vocabulary** (`Spaarke.Dataverse.DocumentLinkFields`). Found at Step 9.5 by
  the ArchTests `DocumentLinkVocabularyGuardTests`: the first cut hard-coded the document's link columns in
  `RecordContainerResolver` (9 `sprk_related*` literals) and `CoreAncestorResolver` (3). Both now PROJECT the canonical
  declaration with their exclusions written down — the resolver drops the vocabulary's party links (related contact /
  organization / vendor organization) and adds the two document-to-document links the vocabulary does not hold (parent,
  canonical); the derivation keeps the links to a project / matter / work assignment (the related service request is
  excluded: held, and it cannot carry `sprk_issecure`). `ChildAncestorLinks` now refuses to LOAD a link to an unclassified
  type, so a column added to the shared vocabulary for a new type fails every resolver test loudly instead of being
  silently ignored (fail open).
- **(xxii) A source naming two different roots of one type is AMBIGUOUS (permanent), not stale.** A seed (S9) showed no test
  distinguished the two: without the explicit check the walk answered `container_ancestor_stale` and enqueued a re-stamp
  the restamper can never derive (`CoreAncestorResolver` refuses that row), i.e. a refresh that never comes and, on the
  inbound path, a retry loop. Now pinned by a test.

## Verifier round 1 (2026-10-02, branch `task/uac-r2-156-r1`)

An independent verifier re-ran everything on a clean worktree of `4dde700b6` and confirmed the security core (no leak path;
every refusal throws before a fallback is derived; ADR-002 / ADR-036 / ADR-052 hold) and the re-parent inventory (no
unwired BFF path). Its findings, and what this round did with each:

| # | Finding | Disposition |
|---|---|---|
| 1 | Security core holds | Confirmed; nothing to change. |
| 2 | Re-parent inventory complete | Confirmed; nothing to change. |
| 3 | AC7 overclaimed: seeds M1, M2, M11, M12, M13 survived | **Closed.** Each now bites (seed table below). `StampWorld` now honours a FetchXML filter and paging, and a paged QueryExpression, so truncation and the scan's reach are asserted, not assumed. |
| 4 | The job's scan read every regarding-filed row (F-051-6 clause = "any pair type"), and rows past 20 x 1000 were never checked | **Closed.** (a) The typed orphan clause is an `in` on the `sprk_recordtype_ref` ids that name an intermediate the table is filed under (the types are read once at the start of every run; unreadable = FAILED run). (b) A scan stopped by the page bound records where it stopped (the distributed cache, 6 h) and the next run continues after that row — paging across runs; a truncated or a continuing run is PARTIAL, never ok. See (xxiii). |
| 5 | F-051-6 undetectable for communication / agreement (no `sprk_recordtype_ref` row live) | **Closed for every row that carries a pair id; a residual is escalated.** An untyped pair (id, no type) is a candidate too: the restamper and the job look the id up in each intermediate the table is filed under (exactly one hit = the cleared record; none = gone, left alone and counted; two = not guessed). The client regarding writer and the outbound sender always write the id. **Not closable without an owner decision:** a row written with NO pair at all (`TaskActionCore`'s events — live event `edfef460` — and an Ambiguous inbound association) leaves no trace of what a form clear removed. See the 🔔 block. |
| 6 | AC1 deviation (xvii) needs owner sign-off | **Not closable here** — the owner's call. 🔔 block below. |
| 7 | AC4 non-secure half not route-tested | **Closed.** A real OBO `PUT /api/obo/records/sprk_todo/{id}/files/…` for a to-do whose copy equals its communication's live root, a NON-secure project, stores in the to-do's business-unit container. |
| 8 | Office carrier-only to-do born stale; comment false; access change unflagged | **Closed.** `OfficeService.CreateTodoAsync` stamps a carrier-only to-do from its carrier (the one classification rule decides; two carriers and no record = nothing stamped, fail closed; an unreadable carrier refuses the create). Comment corrected. Access change flagged as (xxiv). |
| 9 | (xiii) premise false for `IncomingAssociationResolver` Ambiguous writes; DirectRootLink rows carry partial copies of a carrier's other root types | **Closed.** The inbound write now follows `ClassifyStampSource` over the row exactly as written: a pair naming a root → nothing copied from a carrier; no pair + a lone intermediate beside an explicit root it can carry → the intermediate is withheld (a review candidate, provenance `written: false`). Latent: the shipped `CoreWritableEntities` writes roots only. See (xxv). |
| 10 | A generic write to a stamped intermediate's copy column cascaded the hand-written value | **Closed.** `AfterWriteAsync` re-derives a record that carries a copy from its source FIRST when its own root columns were written, so the source's root — never the hand-written one — reaches its children; if the record's own source cannot be read nothing is cascaded (the job repairs both); a row filed under nothing is taken at its word on this path (only the job clears orphans). See (xxvi). |
| 11 | AC8 manual live gate and the §10 publish-size measurement pending | AC8: still the main session's, after deploy. **Publish size: measured — +0.03 MB vs master, +0.04 MB for the whole of task 156** (section below). |

### New and changed interpretations (owner-reversible)

- **(xxiii) The job reads only copy-carrying rows and F-051-6 candidates, and continues a stopped scan next run.** Live
  (read-only, 2026-10-02): the typed clause matches 0 rows in every table; the untyped clause matches 20 communications —
  every one OUTBOUND, its pair naming the matter / project / work assignment it carries (the outbound sender writes the pair
  id without its type), recognised in code as a direct link with no extra read. That subset grows with outbound mail filed
  directly to a root; the cursor guarantees every candidate is still checked within ⌈N / 20 000⌉ runs. *Follow-up (not
  done — another writer):* have the outbound sender write `sprk_regardingrecordtype` when a ref exists, and the untyped
  clause shrinks to communication / agreement filings. A cleared record that no longer exists is counted
  (`orphanSourceGone`), never a partial run that repeats every tick.
- **(xxiv) A carrier-only Office to-do inherits its carrier's project / matter / work assignment from creation.** Before,
  it inherited nothing until the job's first run (and, before task 156, never). That is C10 part 2 (a child of a secure
  record is secure) and the same access the job would have granted minutes later; live count today 0.
- **(xxv) The inbound association write follows the one classification rule.** (a) Pair naming a root (Resolved /
  Suggested — a root outranks every intermediate in ADR-024 priority): the intermediates are carriers and contribute no
  copy (before: the carrier's OTHER root types were copied, partial copies nothing refreshed — an access over-grant once
  the carrier moved; the Office carrier to-do never copied). (b) No pair (Ambiguous) and a lone intermediate beside an
  explicit root it can carry: the intermediate is not written — the engine's explicit root stands and the intermediate
  stays a review candidate. Without this the restamper read the root as the intermediate's copy and overwrote or cleared
  it — and if that root was secure, the email's content would have followed the intermediate's root instead (fail open).
  (c) Two intermediates and no pair: nothing copied (before: the last one written won — a guess). All three are reachable
  only with `CoreWritableEntities` widened to an intermediate.
- **(xxvi) A write to the copy column of a record filed under another is reverted at once.** The reconciliation job would
  revert it a cycle later anyway; doing it in the same operation stops the hand-written value reaching the children (and
  the second cascade that undid it).

### 🔔 Human Input Required — two owner decisions

🔔 **ADR Conflict — Resolution Required** (item 6 / AC1; interpretation xvii)

- **ADR / rule in question:** the spec MUST rule on `DataverseUpdateRecordHandler` (spaarke-ai-architecture-redesign-r1,
  task-012 audit): "User-OBO ONLY … No app-only client is reachable from this class".
- **Conflict:** AC1 requires every BFF re-file path to re-stamp the children IN THE SAME OPERATION. The re-stamp is a
  server-owned invariant written app-only (ADR-002 WP-1). Calling the restamper from the AI update tool would put an
  app-only client in reach of that class.
- **Proposed path: A (project-scoped exception).** Keep the enqueue: the cascade runs on `sdap-jobs` seconds later, the
  storage resolver refuses a stale copy in that window (409, never a misfile — escalation trigger 1 did not fire), and the
  job is the backstop. Cost: a stale copy is also a short access over-grant in that window.
- **Alternatives considered:** (B) amend the MUST rule to allow one narrow, write-only facade onto the restamper (the AI
  tool would then re-stamp inline; the rule's intent — no privilege escalation of the USER's write — is untouched, but the
  audit's "no app-only client reachable" property is lost); (C) re-stamp with the user's OBO client — rejected: the stamp is
  the server's invariant (WP-1), children the user cannot write would stay stale, and a partial OBO cascade is harder to
  reason about than a complete deferred one.
- **Owner action:** accept A (AC1 is then met by documented exception) or choose B.

🔔 **Owner decision — F-051-6 for rows written with no pair** (item 5 residual)

- **Situation:** a form clear of a regarding lookup is now found by the job whenever the row carries a pair id (typed or
  not). `TaskActionCore` (the AI / communication RI "create task" seam) writes the typed regarding and the stamp but no pair,
  so a later form clear of `sprk_regardingcommunication` on such an event leaves a copy with nothing on the row saying it
  was one. Live exposure today: 1 row (event `edfef460`, under communication `99eb9b52`, which names no root — so there is
  no copy to orphan). Storage: with no pair, a cleared row reads as a DIRECT link to the old root, so its content still goes
  to that root's container (never another root's); the access over-grant to the old root's principals persists.
- **Options:** (1) `TaskActionCore` writes the ADR-024 pair fields (id, type when a ref exists, name, url) like every other
  regarding builder — new rows become detectable; nothing to backfill live. (2) Accept the residual and document it.
  (3) Let the job write the pair id on rule-5 rows it re-stamps — rejected here: the constraint says re-stamping writes ONLY
  stamp columns.
- **Recommendation:** (1). Not done in this round: it changes another writer's data contract (a UI-visible regarding on
  every RI follow-up task), outside the verifier's items.

### Seeds this round (each restored byte-identical from a backup, then touched; checksums re-verified)

| Seed | Removed guard | Bit |
|---|---|---|
| M1 | `sprk_recordtype_ref` read failure counted as a scan failure | `RecordTypesUnreadable_IsAFailedRun` |
| M2 | unverified source → partial | `UnverifiableSource_IsAPartialRun` |
| M11 | page-bound truncation → partial | `PageBound_IsAPartialRun_AndTheNextRunContinuesWhereItStopped` |
| X1 | cursor saved on truncation | same test (third row never repaired) |
| X2 | continuing run flagged | same test (second run reported ok) |
| X3 | typed clause bounded to intermediate types | `RowsFiledDirectlyToARoot_AreNeverScanned` |
| X4 | untyped orphan clause | `OrphanedCopy_WithAnUntypedPair_IsClearedByTheJob` (5 cases) + the gone test |
| X5 | gone ≠ unverified | `OrphanCandidate_WhoseRecordIsGone_IsLeftAlone_AndTheRunIsOk` |
| M12 | restamper per-source bound | `ChildrenPastThePerSourceBound_AreReportedTruncated` |
| R2 | self re-derivation before the cascade | `WriteToTheCopyColumnOfAFiledRecord_…` + `…_WhenTheSourceIsUnreadable_…` |
| R3 | no orphan clear on the after-write path | `RootWriteOnAnOrphanShapedRow_IsNotClearedByTheAfterWritePath` |
| R4 | no cascade after a failed self re-derivation | `WriteToTheCopyColumn_WhenTheSourceIsUnreadable_CascadesNothing` |
| R5 | untyped pair looked up by id | restamper + job untyped tests (7) |
| R6 | a pair naming a carried root is not an orphan | `UntypedPairNamingItsOwnRoot_IsNeverAnOrphan` |
| R7 | gone → skipped, not failed (restamper) | `OrphanCandidate_WhoseRecordIsGone_IsLeftAlone_NotAFailure` |
| M13 | `IntermediateRootsAsync` bounds | `ChainOfIntermediatesPastTheDepthBound_…` + `IntermediateReadsPastTheReadBound_…` |
| S7 | fresh copy followed (155 hold reinstated) | the new non-secure OBO PUT route test (+ 2 existing) |
| O1 | carrier-only to-do stamped | `Post_OfficeCreateTodo_CarrierOnly_…` (2) + unreadable test |
| O2 | unreadable carrier refuses the create | `Post_OfficeCreateTodo_CarrierOnly_WhenTheCarrierCannotBeRead_…` |
| I1 | withhold the unplaceable intermediate | `ApplyDecision_Ambiguous_RootAndInvoiceWithNoPair_…` |
| I2 | no carrier copies on a direct-root row | `ApplyDecision_RootAndInvoiceWritten_PairNamesTheRoot_…` |
| I3 | provenance marks the withheld write | `ApplyDecision_Ambiguous_RootAndInvoiceWithNoPair_…` |

Two seeds first failed to COMPILE (an unassigned counter field is a warning-as-error) and were re-run in a compiling form;
the seed runner now refuses to run tests on a failed build, so no result came from stale binaries.

### Publish size (CLAUDE.md §10 item 4) — measured 2026-10-02

Three FRESH worktrees at short paths (`C:\w156a`, `C:\w156b`, `C:\w156m`; removed afterwards), each published exactly as
`scripts/Deploy-BffApi.ps1` does (`dotnet restore`, then `dotnet publish -c Release -o deploy/api-publish --no-restore`)
and zipped with PowerShell `Compress-Archive`. No `MSB3030`; **212 files on every side**.

| Commit | Zip incl. PDBs | Zip excl. PDBs |
|---|---|---|
| `65e6db71f` — origin/master | 45.49 MB | 44.51 MB |
| `e74541920` — task 156 base (before task 156) | 45.48 MB | 44.49 MB |
| `dc944a9b0` — task 156 + verifier round 1 | 45.52 MB | 44.53 MB |

Delta: **+0.04 MB** for the whole of task 156 (base → round 1; 48 335 bytes incl. PDBs), **+0.03 MB** branch vs master.
Code only — no package, project reference or new assembly, so no new CVE surface. Far below the +5 MB justification and the
55 / 60 MB thresholds.

## Placement justification (CLAUDE.md §10 / §11, `bff-extensions.md`)

All four new types live in the BFF, in `Services/Dataverse/` beside the invariant's owner (`CoreAncestorResolver`):
BFF domain code over BFF-owned tables, BFF identity, low volume (ADR-052 B2/B3). No new interface, endpoint, option class,
package or plugin; every registration is unconditional (ADR-032: no Null-Object question).

| New surface | Existing (grep, 2026-10-02) | Why not extend it | Cost of doing nothing |
|---|---|---|---|
| `CoreAncestorRestamper` | `CoreAncestorResolver` derives a stamp for ONE row being written; nothing anywhere re-derives a stamp on rows already written (`grep -rn "Restamp\|ReStamp" src/server` → none before this task) | Folding the cascade into the resolver would mix a pure, per-write derivation (used inline by 8 writers) with paged queries and PATCHes of other rows; the restamper REUSES the resolver for every derivation instead of copying it | Re-filing an intermediate leaves every child's copy stale: an access over-grant (task 051 §1) and, without the comparison, the #1038 leak |
| `CoreAncestorStampReconciliationJob` | `MembershipReconciliationJob`, `ExternalAccessReconciliationJob` reconcile other tables; nothing reconciles stamps | A job per invariant is the ADR-036 shape; neither existing job's scan or rules apply | Writes outside the BFF (forms, flows, imports, the client wizards and the client stamp mirror) and form clears (F-051-6) stay stale forever; the resolver would then refuse those uploads (stale) forever |
| `CoreAncestorRestampQueue` + `CoreAncestorRestampJobHandler` | `JobSubmissionService` / `IJobHandler` (ADR-004) — reused, not duplicated: the queue is a 20-line typed submitter, the handler a thin adapter onto the restamper | The resolver must not take a Service Bus dependency at construction (it is constructed on every upload); the AI update tool must not hold an app-only client | A stale refusal would wait up to 5 minutes for the job instead of seconds; the AI tool's re-files would wait for the job |
| `container_ancestor_stale` (problem code) | `container_ancestor_unverifiable` means "cannot be compared"; `container_ancestor_unresolved` means "unknown / unreadable" | Reusing either would mis-classify the inbound retry (stale is transient — the re-stamp makes the retry succeed; unverifiable / unresolved-409 are permanent skips) | A stale email would be skipped permanently, losing its archive |

**Publish size:** code only — no package, no new assembly reference. Measured in verifier round 1 from fresh short-path
worktrees: +0.04 MB for the whole task, +0.03 MB vs master (table in that section).

## Tests

- Affected suites (resolver, restamper, job, queue handler, re-file paths, real PUT / Office routes, derivation, AI tool,
  inbound processor, document vocabulary): **527 / 527**.
- Full BFF unit suite: **Passed 13488 / Failed 0 / Skipped 54 (Total 13542)**. NetArchTest: **337 / 337** (it caught (xxi)).
- Mutation proof: **49 seeds, each turned tests red** and each restored byte-identical (then touched) — resolver S1-S16,
  derivation / classification / inbound C1-C7, restamper R1-R8, job J1-J6, queue Q1, handler H1, every wired re-file path
  W1-W6, vocabulary G1-G4. Five seeds first failed to compile (`if (false)` trips CS0162) or did not bite (S9 — fixed by
  the (xxii) test) and were re-run green-to-red. G1 shows the load-time guard firing (an unclassified vocabulary column
  fails 269 tests); G2 shows the vocabulary test catching the same column with the guard removed.
- **Verifier round 1:** 28 new tests (job 11, restamper 7, resolver bounds 2, OBO PUT route 1, Office create 4, inbound
  association 3); affected suites **537 / 537**; full BFF unit **Passed 13516 / Failed 0 / Skipped 54 (Total 13570)**;
  NetArchTest **337 / 337**; 22 seeds, each red (table in the verifier round 1 section).
- New test homes: `tests/integration/data-mutation/CoreAncestorStamping/` (StampWorld in-memory Dataverse; restamper; job;
  queue + handler; every re-file path; the real document PUT route) and
  `tests/integration/auth/UnifiedAccessControl/` (stamp freshness, topology lock-step). ADR-038: no mocked HTTP handler,
  no DI-registration or constructor null-check tests; module boundaries only (Dataverse rows, Service Bus).
