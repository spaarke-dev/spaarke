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
| **The enqueue** | `Services/Dataverse/CoreAncestorRestampQueue.cs` + `CoreAncestorRestampJobHandler.cs` (new) | ADR-004 `IJobHandler` (`CoreAncestorRestamp`) on the shared `sdap-jobs` queue (ADR-052: queue work → `IJobHandler`). The resolver enqueues a stale row — since owner round 8 item 1 the ONLY message type (the user-OBO AI update tool re-stamps inline, below). Best effort: a failed enqueue is logged and the job repairs within one cycle. Service Bus is resolved lazily, so composing the resolver never needs Service Bus configuration. |
| **The live comparison** | `Infrastructure/Dataverse/RecordContainerResolver.cs` | Task 155's held branch is replaced (next section). New reason code **`container_ancestor_stale`** (409). New links entries for analysis, document, agreement, budget and report card (swept live, below). |
| **Wiring** | the BFF paths in the inventory | Each calls `AfterWriteAsync` after its own write succeeded — the user-OBO AI update tool through the narrow `CoreAncestorAfterWriteRestamp` (owner round 8 item 1, §6.5 path B). |

## The rule: which record a row's copy comes from (`ClassifyStampSource`)

The row's polymorphic pair (`sprk_regardingrecordid`) says what the user filed it under; every regarding builder writes it
with the typed lookup.

1. No source column set → **not filed under an intermediate**: its root columns are its own.
2. The pair names a ROOT column set on the row → **a direct, user-chosen link** (the Office carrier to-do). Every source
   column set is a **carrier**. Nothing on the row is a copy, so nothing is ever re-stamped.
3. The pair names a source column set on the row → **that record is the source**. Other source columns are carriers.
4. The pair names nothing the row carries (or is not a GUID) → **inconsistent**: nothing is written; the resolver refuses
   (pair rule 4, ambiguous).
5. No pair (or a pair naming the row's own typed party): one source column → it is the source (an Ambiguous inbound
   association writes no pair, and neither did `TaskActionCore` before owner round 8 item 2); two or more →
   **ambiguous**: nothing is written; the resolver refuses.

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
| any (generic) | AI tool `dataverse.update_record` (`DataverseUpdateRecordHandler`, user-OBO) | **wired inline** (owner round 8 item 1, §6.5 path B) — `CoreAncestorAfterWriteRestamp.AfterWriteAsync` after the caller's own user-OBO PATCH succeeded; was enqueued until then (interpretation xvii, superseded) | `Services/Ai/Handlers/DataverseUpdateRecordHandler.cs` |
| sprk_communication | `IncomingAssociationResolver.ApplyDecisionAsync` | **create-time only — no cascade**: both callers run it on a communication created in the same operation (`IncomingCommunicationProcessor` step 4.5 after `CreateCommunicationRaceProofAsync`; `EmailUploadCaptureService` only when `wasDuplicate` is false), so no child can exist. Its stamp now also derives budget / report card targets (`IsStampSourceEntity`). | `Services/Communication/IncomingCommunicationProcessor.cs` ~345; `EmailUploadCaptureService.cs` ~85-105 |
| sprk_communication | `POST /api/communications/{id}/suggest-associations` | read-only preview (`EvaluateAsync`) | `Api/CommunicationEndpoints.cs` |
| sprk_communication | enrichment / triage (`CommunicationEnrichmentService` :601, :1662), delivery merge, cross-path link | no root column written (triage fields, owner, mailbox list, `sprk_relatedcommunication` on a document) | the three files |
| sprk_event | `PUT /api/v1/events/{id}` | writes the pair only, never a root column. **Wired in verifier round 2 (item 8)** — `AfterWriteAsync("sprk_event", …, EventColumnsWritten(request))`: on an event carrying two typed sources the pair decides which one its copy comes from, so a pair change re-stamps the event and everything filed under it in the same request (before: the job's) | `Api/Events/EventEndpoints.cs` `UpdateEventInDataverseAsync` |
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
  **Still open after verifier round 2 (item 13a)** — see that section for why it is not done in a BFF fix round.
  **CLOSED by task 169 (2026-10-04, branch `task/uac-r2-169`):** the TS derivation reads a literal 30-row
  `INTERMEDIATE_ROOT_COLUMNS` equal to `IntermediateRootColumns`, with the same two-roots-of-one-type rule; pinned by
  `CoreAncestorResolverTests.IntermediateRootColumns_MatchTheTypeScriptSide` and `CoreAncestorLookups_MatchTheTypeScriptSide`
  (`notes/task-169-ts-intermediate-root-columns.md`). The taxonomy literals are still untouched.
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
- **(xvii) — SUPERSEDED by owner round 8 item 1 (§6.5 path B; section "Owner round 8" below): the tool now re-stamps
  inline.** As shipped until then: **the user-OBO AI update tool enqueues its cascade** instead of running it: `DataverseUpdateRecordHandler` is bound
  by its own MUST rule ("no app-only client is reachable from this class"), and the re-stamp is an app-only, server-owned
  write. The enqueue holds no Dataverse client; the job runs seconds later and the resolver refuses a stale copy meanwhile.
  This is the one inventory path not re-stamped "in the same operation" — flagged in the task result for the owner.
  **Still open after verifier round 1 (item 6): AC1 is not met for this path until the owner accepts the deviation** — the
  🔔 block is in the verifier round 1 section below. Verifier round 2 (item 6) checked the block and the window (the
  resolver refuses 409 stale, including transitive chains and interleaved re-file races; it never misfiles) and confirmed
  it is the owner's to close.
- **(xviii) Document `sprk_canonicaldocument` is held, and an analysis's `sprk_documentid` / `sprk_outputfileid` are
  carriers**, by the 155 f3 rule (a column whose target can hang off a root is read and checked, or refused).
- **(xix) The job's completion marker (ADR-036 A1 rule 3) is scoped to the RUN** (`{job}:{entity}:{id}:{planHash}:{runId}`);
  the claim is not. Found in the Step 9.5 review: keyed by row and value alone, a row put back out of band within 30
  minutes of a repair to the same value was reported `alreadyApplied` and left stale until the marker expired — which is
  exactly the manual live gate's second step. A retry of the same run still does not re-apply; the next run repairs.
- **(xx) — moot since owner round 8 item 1: the tool no longer enqueues, and the after-write message type is gone.**
  As shipped until then: **an after-write cascade enqueued by the AI update tool is never de-duplicated** (one message per write). A stale
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
- **RESOLVED — owner round 8 item 1 (2026-10-03): path B.** Implemented in the "Owner round 8" section below; AC1 is
  met with no exception.
- **Correction (verifier round 3, item 4).** Path A's premise is weaker than stated above. The handler already holds
  `CoreAncestorRestampQueue`, and the queue holds the root `IServiceProvider` (it resolves `JobSubmissionService` lazily). An
  app-only `IGenericEntityService` can be resolved from that provider, so "no app-only client is reachable from this class"
  is already true only by convention, not by construction. Two consequences for the choice:
  - **Path B costs less than it looked.** A narrow, write-only facade onto the restamper changes the audit property
    about as much as the queue already has.
  - **Path A can be made true again** with a small change, if the owner prefers A: give the queue a
    `Func<JobSubmissionService>` (or `Lazy<>`) in place of the provider, so nothing in the handler's reach can resolve a
    Dataverse client. Not done here, because it is only worth doing if the owner chooses A.
  - Owner round 7 item 3 approved path B for `DataverseCreateRecordHandler` and `EmailDraftToolHandler` only. It does
    not cover this update handler, so this block stays open.
  - Verifier round 4 item 11: the handler's own remarks now say this too (the property holds by encapsulation, not by
    construction, and the path is the owner's open decision). Re-checked at that round: rounds 1-7 and the #1081 peer
    report give no answer for this handler.

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
- **RESOLVED — owner round 8 item 2 (2026-10-03): option (1).** `TaskActionCore` writes the pair (section "Owner
  round 8" below). New rows only; nothing is backfilled.
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

## Verifier round 2 (2026-10-02, branch `task/uac-r2-156-r1-r2`)

A second independent verifier re-ran round 1 (`a67263cc7`) on a clean detached worktree: affected suites and NetArchTest
green, and the full BFF unit suite green (13 516 / 0 / 54) **with its two surviving seeds applied** — the same counts as
unseeded, which is the finding. Its items, and what this round did with each:

| # | Finding | Disposition |
|---|---|---|
| 1 | Method and scope | — |
| 2 | **AC7 gap, seed V2**: `extra.AddRange(sourceHops.Extra)` removed, the whole suite stayed green. A to-do under a communication whose SECURE matter is named ONLY by the communication's pair then resolves the to-do's business-unit container (#1038 leak class) | **Closed.** Resolver test `SourceWhoseSecureRootIsNamedOnlyByItsPair_ResolvesThatRootsOwnContainer` and a REAL OBO PUT route test of the same shape (`Put_TodoUnderACommunicationWhoseSecureProjectIsNamedOnlyByItsPair_…`); both red under V2. |
| 3 | **AC7 gap, seed V3**: the analysis carriers (`sprk_documentid` / `sprk_outputfileid`, interpretation xviii) disabled, suite green — only a column-list pin covered them | **Closed.** Behavioural resolver theory (input AND output document of a different SECURE matter → ambiguous) and a real route test; red under V3. Plus an agreeing-carrier test, so a carrier is shown to be read, not refused on sight. |
| 4 | **AC7 weak, seed V1**: the Source-case `carriers.AddRange(decision.Carriers)` bit only the M13 read-count test | **Closed.** Direct resolver test and real route test of "filed under X (copy fresh), also naming Y whose root is a different SECURE record" → ambiguous; both red under V1. |
| 5 | V4, V5, V6 bit as expected | Confirmed; nothing to change. |
| 6 | AC1 deviation: the user-OBO AI update tool enqueues its cascade | **Not closable here** — the owner's (🔔 ADR block in verifier round 1, unchanged: path A proposed, B considered, C rejected). The verifier confirmed escalation trigger 1 correctly did not fire. |
| 7 | Re-parent inventory complete (independently re-grepped) | Confirmed; nothing to change. |
| 8 | `PUT /api/v1/events/{id}` writes the pair and never re-stamps; on an event with two typed sources a pair change moves its source | **Closed.** Wired (`CoreAncestorRestamper.EventColumnsWritten` mirrors exactly when `DataverseWebApiService.UpdateEventAsync` writes the pair); real-route tests (`EventRefileRestampRouteTests`: the event AND the to-do under it re-stamped in the request; a rename reads nothing); seeds E1 / E2. Observation below. |
| 9 | Design risk: a root set directly on a row filed under another record (rule 3, or rule 5) is treated as a copy and reverted / cleared — a child can leave a secure root it was placed under | **Owner decision** — 🔔 below. Not changed: each alternative partly reverses a binding owner decision, so the owner chooses. |
| 10 | ADR / constraint checks | Confirmed; nothing to change. |
| 11 | Job never reports ok on a failed scan | Confirmed; nothing to change. |
| 12 | Resolver never trusts a copy without the live comparison | Confirmed; the two guards it found untested are items 2 and 3, now pinned. |
| 13a | (xii) the TypeScript stamp mirror is unchanged | **Not closed in this round** — see below. **Closed by task 169** (2026-10-04; `notes/task-169-ts-intermediate-root-columns.md`). |
| 13b | POML `<metadata><status>` reads `completed` while AC1 and AC8 are open | **Closed.** `completed-with-escalation` plus a `status-note` — the project's convention for an implemented task with an open owner item (tasks 012, 023, 062, 071). |
| 13c | `CoreAncestorRestampJobHandler` turns a merely TRUNCATED report into Failure, then Poisoned | **Closed.** Truncated with no failure = Success plus a warning: a retry re-lists from the first page and stops at the same bound, so it can never progress; the reconciliation job finishes the rest. Failures still retry, then dead-letter. Test `TruncatedOnlyCascade_IsCompleted_NeverRetriedOrPoisoned`; seed H2. |
| 14 | Publish size not re-measured | Re-measured for this round (below). |
| 15-17 | AC1 / AC7 / AC8 | AC1: item 6 (owner). **AC7: closed** — every guard the verifier named now bites a behavioural test, five of them through a real route. AC8: the main session's live gate. |

**Item 8 — observed, not changed (pre-existing, another writer's contract).** `DataverseWebApiService.UpdateEventAsync` (and
`CreateEventAsync`) write `sprk_regardingrecordtype` as the API's integer 0-7, while on `sprk_event` that column is a LOOKUP
to `sprk_recordtype_ref` (the live lookup sweep pinned in `ChildRecordContainerResolutionTests`). A PUT that sets the
regarding type therefore most likely fails at Dataverse before the cascade is reached (the route answers 500). Not
verified live: that would be a write. The wiring is correct for the day the pair write succeeds and costs nothing until
then; fixing the pair write belongs to the event writer's owner. Also observed on the same route, and also pre-existing:
`PUT /api/v1/events/{id}` checks only that the caller is signed in (the group's `RequireAuthorization()`, no endpoint
filter), and the update runs app-only (`DataverseWebApiService`, no impersonation). There is no per-record rights check
on the event being changed. This is outside task 156 and is flagged for the endpoint's owner. The re-stamp added here
writes only values derived from the data, never a value from the caller.

**Item 13a — not closed in this round, and why.** Mirroring `IntermediateRootColumns` in
`PolymorphicResolverService.deriveCoreAncestorStamps` is a change to the shared client library (`@spaarke/ui-components`).
It ships only when the RegardingResolver PCF and the code pages that bundle the library are rebuilt and redeployed. It also
changes a pinned FR-26 client contract: budget, report card and agreement targets move from `unclassified` to derived, and
a document whose typed and related links disagree would newly block the save. That is outside the BFF diff two verifiers
have now checked, and the verifier rated it non-blocking. The owner's freshness rule (R3/R4: minutes) is met without it:
the stale refusal enqueues a re-stamp that lands in seconds, and the job runs every 5 minutes. The child is born
fail-closed, refusing uploads and inheriting nothing until then. **Recommended follow-up task:** mirror the table in
TypeScript, using the same two-roots-of-one-type rule, with jest tests and a C# lock-step test that parses the TS table the
way `CoreAncestorResolverTests.Taxonomy_MatchesTheTypeScriptSide` parses the taxonomy arrays.

### 🔔 Owner decision — a root set directly on a row filed under another record (verifier round 2 item 9)

- **Resolved by owner round 8 item 3 (option 1 + 3) and task 168:** the rule stays as shipped, and `scripts/Lock-CoreAncestorStampColumnsOnForms.ps1` makes the four root columns read-only on the child forms (checked by `-Verify`; live apply and the open form decisions are in `notes/task-168-lock-root-columns-on-forms.md`).
- **Situation.** When a row's pair names an intermediate (rule 3), or it has no pair and exactly one intermediate (rule 5),
  EVERY root column the source can carry is treated as a copy. The cascade and the job set it to the source's root, or
  clear it when the source names no root of that type. A root a person sets DIRECTLY on such a row is therefore reverted
  within one cycle. The example is a native form edit that adds a SECURE work assignment or matter beside the
  communication regarding.
  - During the window the resolver refuses (409 stale). It never misfiles.
  - After the repair, the row has left the secure root it was placed under: its access follows the source's root again,
    and so does its next upload.
- **Live exposure: none today.** The verifier's read-only query found that all 14 to-dos filed under an intermediate carry
  a pair naming exactly that source. Choosing a root through the RegardingResolver PCF writes the pair to that root, which
  makes a DIRECT link (rule 2, never re-stamped), not this shape. The shape needs a raw edit of a stamp column.
- **Options.**
  1. **Confirm the rule as shipped.** The pair decides, and a root column on a filed row is always a copy. This keeps
     option (b) in both directions: a communication moved OUT of a secure matter takes its children with it.
  2. **Stricter, fail closed.** Never overwrite or clear a copy column whose CURRENT value is a SECURE root other than the
     source's. Instead treat the row as ambiguous: the job counts it for review, the resolver answers 409 ambiguous
     (permanent) instead of stale, and nothing is enqueued.
     - *Cost:* the cascade and the job cannot tell a hand-placed secure root from a stale copy of the source's PREVIOUS
       secure root. A genuine re-file of an intermediate out of a secure root, or between two secure roots, no longer
       carries its children. They stay under the old secure root (access stays restricted) and refuse uploads until a
       person re-files each one. That partly reverses (b).
     - *Work:* one flag read per differing value in the restamper, the job and the resolver, with tests and seeds.
  3. **Remove the shape at its source.** Make the four `sprk_regarding{core}` columns read-only on the to-do, event,
     communication and analysis forms. Choosing a root then goes through the RegardingResolver, which writes the pair and
     makes a direct link the restamper never touches. This is a form change (not BFF code). Which forms expose the
     columns has to be checked first.
- **RESOLVED — owner round 8 item 3 (2026-10-03): options 1 + 3.** The rule as shipped is CONFIRMED (the pair decides; a
  root column on a filed row is a copy; option (b) stays whole in both directions) — no code change in task 156. The
  column lock (option 3: the four `sprk_regarding{core}` columns read-only on the to-do, event, communication and analysis
  forms, after checking which forms expose them) is **task 168**, not task 156.
- **Recommendation: 1 + 3.** (b) stays whole, misfiled email can be moved out of a secure matter with its follow-ups, and
  the only input that produces the risky shape is closed. Option 2 is the alternative if the owner prefers the server to
  refuse whatever the forms allow.
  - *Rejected as a recommendation:* applying option 2 to the job and the after-write self-revert (xxvi) only. A
    hand-placed secure root the job left alone would still be overwritten by the next BFF re-file of its source, so the
    outcome would depend on which path ran.

### Seeds this round (each restored byte-identical from a backup, then touched; SHA-256 re-verified)

The seed runner refuses to report on a failed build. Every seed below compiled.

| Seed | Removed guard | Bit |
|---|---|---|
| V1 | Source case: `carriers.AddRange(decision.Carriers)` | `RowFiledUnderASource_AlsoNamingAnIntermediateOfADifferentSecureRoot_IsAmbiguous` + route `Put_TodoUnderACommunicationAlsoNamingAnEventOfTheSecureProject_…` (2 red) |
| V2 | Source case: `extra.AddRange(sourceHops.Extra)` | `SourceWhoseSecureRootIsNamedOnlyByItsPair_…` + route `Put_TodoUnderACommunicationWhoseSecureProjectIsNamedOnlyByItsPair_…` (2 red) |
| V3 | `SetLinks(row, rowLinks.CarrierColumns)` → empty | `AnalysisWhoseDocumentBelongsToADifferentSecureRoot_IsAmbiguous` (both columns) + route `Put_TodoUnderAnAnalysisWhoseInputDocumentIsUnderTheSecureProject_…` (3 red) |
| E1 | the event PUT's `AfterWriteAsync` call | `Put_MovingThePairToTheEventsOtherSource_RestampsTheEventAndItsChildren` |
| E2 | `EventColumnsWritten` answers nothing | the same route test |
| H2 | truncated-only → Success | `TruncatedOnlyCascade_IsCompleted_NeverRetriedOrPoisoned` |

### Tests this round

11 new test cases:
- resolver: 5 (V1, V2, V3 ×2, V3 agreeing);
- real OBO PUT route: 3 (V1, V2, V3);
- real event PUT route: 2;
- queue handler: 1.

Results:
- Affected suites (filter: `ChildRecordContainerResolutionTests`, `RecordKeyedUpload`, `CoreAncestor`, `RefilePathRestampTests`, `DocumentRefileRestampRouteTests`, `EventRefileRestampRouteTests`, `ServerWriterAncestorStamping`, `OfficeSaveNoTargetContainer`, `OfficeTodoRegarding`, `IncomingAssociation`, `IncomingCommunicationProcessor`, `DataverseUpdateRecordHandler`, `DataverseToolNameFreeze`, `EventEndpoints`, `RecordContainerResolver`): **499 / 499**.
- Full BFF unit suite **Passed 13527 / Failed 0 / Skipped 54 (Total 13581)**; NetArchTest **337 / 337** (details and one
  caveat under "Tests" below).

### Publish size (CLAUDE.md §10 item 4) — measured 2026-10-02

Method:
- three FRESH exports (`git archive`) at short paths (`C:\w156r2m`, `C:\w156r2a`, `C:\w156r2b`), removed afterwards;
- each published exactly as `scripts/Deploy-BffApi.ps1` does (`dotnet restore`, then
  `dotnet publish -c Release -o deploy/api-publish --no-restore`);
- each zipped with PowerShell `Compress-Archive`;
- no `MSB3030`, and **212 files on every side**;
- the full test suite was not running during the publishes.

| Commit | Zip incl. PDBs | Zip excl. PDBs |
|---|---|---|
| `c726acd65` — origin/master today (it now includes batch 2, #1093, which this branch does not) | 45.54 MB (47 747 531 B) | 44.55 MB |
| `a67263cc7` — task 156 after verifier round 1 | 45.52 MB (47 731 877 B) | 44.53 MB |
| `aafe12ee7` — task 156 after verifier round 2 | 45.52 MB (47 731 757 B) | 44.53 MB |

**This round: −120 bytes (0.00 MB).** The round-1 and round-2 commits differ only by this round's diff, so that pair isolates
it. The comparison with master is not a clean isolation: master moved on (#1093), and the branch reads 0.02 MB smaller.
The whole of task 156 remains +0.04 MB (round 1's base-to-branch measurement). Code only: no package, project reference
or new assembly, so there is no new CVE surface.

## Verifier round 3 (2026-10-02, branch `task/uac-r2-156-r1-r1`)

This round first merged `work/unified-access-control-r2` (origin/master `93634db58` plus integrated batch 3: tasks 138,
139, 141, 152, 155 and the Office save fix `0ecbf09fa`). Every `.cs` file auto-merged. The two text conflicts were
resolved by hand:
- `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`: both sides' registry rows are kept. The core-ancestor row and
  I-1 keep this task's text; the record-owner row and I-2 keep the integration branch's (task 144).
- The 156 POML (add/add): this branch's version is kept, because the integration branch held only the pending stub.

Merge commit (`baseSha`): `8a98ddd5a`. It builds, and the affected suites pass.

A third verifier re-ran round 2 at `57e0bfca0` on a fresh worktree. The suites matched the round-2 report exactly. Its
findings, and what this round did with each:

| # | Finding | Disposition |
|---|---|---|
| 1 | Suites reproduced (affected 499 / 499; full BFF 13 527 / 0 / 54; NetArchTest 337 / 337); POML well-formed | Confirmed; nothing to change. |
| 2 | **AC7 overclaimed: seeds K8, K7, K5 survived** the affected suites | **Closed.** Six new tests (below); each seed now turns tests red (seed table below). |
| 3 | Seeds K1, K2, K4, K6, K11 bit; no misfile path | Confirmed; nothing to change. |
| 4 | AC1 not met: the user-OBO AI update tool enqueues its cascade. The "no app-only client reachable" argument for path A is weaker than stated | **Not closable here** (the owner's). The 🔔 ADR block in the verifier round 1 section now carries the correction: the queue already holds the root `IServiceProvider`. It also states what each path costs now, and how path A could be made true again. Owner round 7 item 3 approved path B for the two CREATE handlers only, not this one. |
| 5 | Every inventory path wired and tested through its own entry point | Confirmed; nothing to change. |
| 6 | AC2-AC6 met (AC3's "failed repair = not ok" was the K8 gap) | Confirmed. The K8 gap is closed under item 2. |
| 7 | Merge readiness: two text conflicts; after the merge, the Office save marks a `container_ancestor_stale` refusal (409) NOT retryable, although its text says "try again in a minute" | **Closed.** The merge is done; the conflicts are resolved as the verifier advised. `OfficeService.SaveAsync`'s `SdapProblemException` catch now sets `Retryable` for a 5xx **or** `container_ancestor_stale`. The refusal itself enqueued the re-stamp that makes the same save succeed, and the response cache replays only successes (`IdempotencyFilter`), so a retry reaches the resolver again. Every other 4xx refusal stays not retryable. The two stale Office-save route tests now assert `retryable: true`; the ambiguous carrier test asserts `retryable: false`, so "every 409 is retryable" fails too. Seed O1. |
| 8 | Pre-existing, not task 156: (a) `UpdateEventAsync` writes `sprk_regardingrecordtype` as an integer on a Lookup column; (b) `PUT /api/v1/events/{id}` checks sign-in only and updates app-only | **Carried forward, unchanged** (already recorded under verifier round 2, item 8). Both belong to the event endpoint's owner; (a) is unverified live, because checking it would be a write. Recommended: file both as issues against the events endpoint. |
| 9 | Hygiene: an abandoned verifier worktree `C:\wtv156` (detached at `57e0bfca0`) holds an uncommitted K8 seed | **For the main session.** Still present at this round (`git worktree list`). Not this run's to remove: `git worktree remove --force C:/wtv156`. |
| 10 | ADR-038 / ADR-036 / ADR-052 / ADR-002 / §10 checks: no finding | Confirmed; nothing to change. |
| 11 | AC1 not met | As item 4: the owner's. |
| 12 | AC7 not met (K8, K7, K5) | **Closed** under item 2. |
| 13 | AC8: the manual dev live gate is pending | The main session's, after deploy. |

### The three guards, and what now pins each

- **K8 — a failed repair makes the run partial** (`CoreAncestorStampReconciliationJob.RepairAsync`, `counts.RepairFailures++`).
  The only failing-write job test healed the write before the job ran, so nothing ran the job with a repair that fails.
  New test `RepairWhosePatchFails_IsAPartialRun_NeverOk`: the PATCH is refused for the whole run. It asserts that the repair
  was attempted, that the stale copy remains, `Success == false`, `status == partial`, `repairFailures == 1` and
  `repaired == 0`.
- **K7 — an untyped pair found in two tables is never guessed** (`CoreAncestorRestamper.FindClearedSourceAsync`, the
  multi-hit arm). New tests in the restamper (`UntypedPairFoundInTwoIntermediateTables_IsNeverGuessed`) and the job
  (`UntypedPairFoundInTwoIntermediateTables_IsUnverified_NothingCleared`). The same id is a communication AND an agreement,
  and both name the copy's matter, so any guess would "match" and clear it. They assert no PATCH, the copy intact, a
  failure naming "more than one table" (restamper), and `unverified == 1` / `partial` / `Success == false` (job).
- **K5 — a stamp column the host table lacks is never planned** (`CoreAncestorRestamper.PlanStamp`). `StampWorld` gained
  `WithoutColumn(entity, column)`: an org whose table lacks a column, so the probe stops reporting it. Three tests:
  - `PlanStamp_SkipsAStampColumnTheHostLacks` (direct, with a with-the-column control);
  - the cascade (`StampColumnTheChildTableLacks_IsNeverWrittenByTheCascade`): a communication re-filed to a work
    assignment in an org whose to-do has no work-assignment column. The PATCH clears the stale matter copy and names
    nothing else;
  - the job (`StampColumnTheChildTableLacks_IsNeverPlannedOrWritten`): a row whose ONLY difference is on the missing
    column is not stale, and the other row's repair writes the matter only.

### Seeds this round (each restored byte-identical from a backup, then touched; SHA-256 re-verified)

The seed runner refuses to report on a failed build. Every seed below compiled. Each ran against the affected-suite filter
(the round 2 filter).

| Seed | Removed guard | Bit (affected suites, 513 tests) |
|---|---|---|
| K8 | `CoreAncestorStampReconciliationJob.RepairAsync`: `counts.RepairFailures++` → `+= 0` | 1 red: `RepairWhosePatchFails_IsAPartialRun_NeverOk` |
| K7 | `CoreAncestorRestamper.FindClearedSourceAsync`: the multi-hit arm → `ClearedSource.Found(hits[0], pairId)` | 2 red: the restamper and job `UntypedPairFoundInTwoIntermediateTables_…` tests |
| K5 | `CoreAncestorRestamper.PlanStamp`: `if (!hostColumns.Contains(column))` made runtime-false | 3 red: `PlanStamp_SkipsAStampColumnTheHostLacks`, the cascade test and the job test |
| O1 | `OfficeService.SaveAsync`: the `container_ancestor_stale` retryable clause removed | 2 red: both stale Office-save route tests |

### Tests this round

Six new test cases: restamper 3 (K7, K5 cascade, K5 PlanStamp), job 3 (K8, K7, K5). Assertions were added to three
existing Office-save route tests (retryable true on both stale refusals, false on the ambiguous one). The test-only
`StampWorld.WithoutColumn` was added. No ADR-038 banned pattern.

Results, on the merged tree:
- Affected suites (the round 2 filter): **513 / 513**. That is 499 + 6 new + 8 brought in by the merge.
- Office + RecordOwnership suites: **Passed 483 / Failed 0 / Skipped 8 (Total 491)**.
- Full BFF unit suite, once at the end: **Passed 14388 / Failed 0 / Skipped 54 (Total 14442)**. It grew from 13581
  because of the merge.
- NetArchTest: **345 / 345**. It grew from 337, also because of the merge.

## Verifier round 4 (2026-10-02, branch `task/uac-r2-156-r1-r2b`)

**Branch name.** The harness asked for `task/uac-r2-156-r1-r2`, but that name already belongs to the verifier round 2
branch (`57e0bfca0`, an ancestor of this work, still checked out in another workflow worktree). Re-pointing it would have
moved a branch someone else holds. This round is therefore on `task/uac-r2-156-r1-r2b`, created from
`task/uac-r2-156-r1-r1` at `45eadf2d5`.

A fourth verifier ran the final verification that round 3 never reached. It reproduced the affected suites (513 / 513) and
NetArchTest (345 / 345), checked the merge resolution side by side, and seeded two guards of its own. It then ran the
project's new hard gate (`ea6484102`: both integration suites in full before the PR), which round 3 had not run. Its
findings, and what this round did with each:

| # | Finding | Disposition |
|---|---|---|
| 1 | Not ready to merge, for one reason only: AC1 waits on an owner decision | **Not closable here.** See items 15 and the 🔔 block (verifier round 1 section). |
| 2 | Affected suites 513 / 513 and NetArchTest 345 / 345 reproduced on a fresh worktree | Confirmed; nothing to change. |
| 3 | The hard gate is red without `8531711d6`: integration 100 passed / 2 failed (`Phase2EndToEndTests` AC1P2_6, AC1P2_3); SPE 402 passed / 1 failed / 25 skipped (`InviteExternalUser_MissingWebRoleConfig_Returns500WithProblemDetails`). None is caused by task 156 | **Closed.** `work/unified-access-control-r2` at `ea6484102` is merged (merge commit `e59c74033`, no conflicts: only that branch's four files, the two integration fixtures, their tests and the project CLAUDE.md row). Both integration suites were then run in full (results below): green. |
| 4 | The round-3 merge resolution keeps both sides' behaviour and both sides' registry rows (I-1, I-2, I-6, I-10) | Confirmed; nothing to change. |
| 5 | The Office save marks only a 5xx or `container_ancestor_stale` retryable; a retry reaches the resolver again; the ambiguous test pins "not every 4xx" | Confirmed; nothing to change. |
| 6 | The verifier's own seeds (A: the stale refusal's enqueue; B: the AI update tool's enqueue) both bite | Confirmed; nothing to change. |
| 7 | Round-3 tests K8, K7, K5 are behavioural; no ADR-038 banned pattern | Confirmed; nothing to change. |
| 8 | Fail-closed review of the resolver's walk: no path reaches a shared container while a root is unconfirmed | Confirmed; nothing to change. |
| 9 | `AfterWriteAsync` never throws except on the caller's cancellation; every call site runs after its own write (AC2) | Confirmed; nothing to change. |
| 10 | Inventory spot-check: no re-file path outside the inventory | Confirmed; nothing to change. |
| 11 | Wording: the AI update handler's remarks say re-stamping inline "would put an app-only client in reach of this class", but the queue it already holds keeps the root `IServiceProvider`, so the property holds by encapsulation, not by construction | **Closed (wording only, no behaviour change).** `DataverseUpdateRecordHandler`'s remarks now say that no dependency of the class is an app-only Dataverse client, that since task 156 this holds by encapsulation (the queue exposes only its enqueue methods but keeps the root provider privately), and that path A (optionally made true by construction by narrowing the queue's dependency) or path B is the owner's open decision. The re-stamp paragraph now says the class does not run the re-stamp because that would mean calling an app-only writer from the user-OBO tool. The queue's dependency is NOT narrowed: that is path A's optional step, and path A is not chosen. |
| 12 | The POML is well-formed; status `completed-with-escalation` plus a status-note | Confirmed. Re-parsed after this round's edit (System.Xml): well-formed. |
| 13 | Housekeeping: the stale verifier worktree `C:\wtv156` (detached at `57e0bfca0`) is still registered | **For the main session.** Still listed by `git worktree list` at this round. Not removed here: it is outside this run's worktree and not this run's. Command: `git worktree remove --force C:/wtv156`. |
| 14 | #1081 (root team holds Spaarke Basic User; the Spaarke Demo BU's team holds System Administrator): dev artifacts under rounds 5 and 6; neither changes task 156; AC8 checks the secure project's container and the repair, not root-BU read reach | Agreed; nothing to change. The peer report is already recorded in the owner decisions note ("Peer report: #1081"). AC8's checks read stamp values and the container an upload lands in, which no role placement changes. |
| 15 | AC1 not met: `DataverseUpdateRecordHandler` enqueues its cascade instead of running it in the same operation. Needs the owner's section 6.5 decision (path A, optionally made true by construction; or path B). Owner round 7 item 3 approved path B for the two CREATE handlers only | **Not closable here — an owner decision, re-checked this round.** The owner decisions note on `work/unified-access-control-r2` (rounds 1-7 and the #1081 peer report) has no answer for this handler. The 🔔 block in the verifier round 1 section stands, with round 3's correction. |
| 16 | AC7 met on its own terms, but the hard gate is red on the branch until it takes `8531711d6` | **Closed** with item 3. |
| 17 | AC8 (manual dev live gate) pending | The main session's, after deploy, per the POML `manual-live-gate`. Not a defect. |

### Tests this round

No test was added or changed: the only source change is a doc comment. No new guard, so nothing to seed.

On the merged tree (`e59c74033` plus the remark change):
- Affected suites (the round 2 filter): **513 / 513**.
- `tests/integration/Sprk.Bff.Api.IntegrationTests`, in full: **Passed 104 / Failed 0 / Skipped 0 (Total 104)**. The
  verifier's 102 plus the two tests `8531711d6` added (`ReconJob_ApplicationUserOwner_GetsNoJunctionRow_Task152`,
  `ReconJob_UnreadableOwner_KeepsExistingRow_AndCreatesNothing_Task152`); the two that failed for the verifier pass.
- `tests/integration/Spe.Integration.Tests`, in full: **Passed 403 / Failed 0 / Skipped 25 (Total 428)**. The
  verifier's one failure (`InviteExternalUser_MissingWebRoleConfig_Returns500WithProblemDetails`) passes; the 25 skips
  are the suite's own (the same 25 the verifier saw).
- Full BFF unit suite, once at the end: **Passed 14388 / Failed 0 / Skipped 54 (Total 14442)**, the same as round 3:
  the merge brought integration tests only.
- NetArchTest: **345 / 345**.

## Owner round 8 (2026-10-03, branch `task/uac-r2-156-c1`)

The owner answered task 156's three open decisions (owner decisions note on `work/unified-access-control-r2`, round 8;
each "(Recommended)" option chosen). This round implements items 1 and 2 and records item 3.

**Branch and merge.** `task/uac-r2-156-c1` was created from `task/uac-r2-156-r1-r2b` (`a827ea0dc`), then
`work/unified-access-control-r2` (`d746422f7`: owner rounds 8-9, the route authorization sweep, task 141's live gates) was
merged with no conflict. `baseSha` = `02b145cc4`.

### Item 1 — the AI update tool re-stamps INLINE (§6.5 path B). AC1 is met, with no exception.

🔔 **ADR / spec-rule resolution record (CLAUDE.md §6.5 — path B, chosen by the owner).**

- **Rule amended:** `DataverseUpdateRecordHandler`'s "User-OBO ONLY … no app-only client is reachable from this class"
  (spaarke-ai-architecture-redesign-r1 MUST rule, task-012 audit), **for one helper only**. It is the same reasoning as
  owner round 7 item 3, which amended the rule for the two AI CREATE tools (task 146).
- **What runs now:** once the caller's own PATCH has succeeded (still user-OBO, so Dataverse authorizes it), the handler
  calls `CoreAncestorAfterWriteRestamp.AfterWriteAsync` in the same operation. It re-stamps the record's own copy (when
  the write changed what it is filed under) and the copies of everything filed under it (when the write changed its
  root). A child that fails is logged and the tool still answers success, because the caller's update was written; the
  reconciliation job repairs the child within one cycle. A refused PATCH re-stamps nothing.
- **The helper** (`Services/Dataverse/CoreAncestorAfterWriteRestamp.cs`, new) is narrow by construction:
  - one public member, the after-write re-stamp;
  - it holds only the restamper — no `IServiceProvider`, so nothing can be resolved through it, and no client exposed;
  - it writes only stamp columns, with values derived from the data (never a value from the caller), so the worst a
    misuse could do is make a stamp correct;
  - it takes no cancellation token: an update that landed must not report "cancelled" with its children half
    re-stamped (the same choice as every other after-write call site).
- **Removed — the queued path for this handler:** `CoreAncestorRestampQueue.EnqueueAfterWriteAsync`, the payload's
  `WrittenColumns`, and the job handler's after-write branch. The queue and `CoreAncestorRestampJobHandler` stay, for the
  storage resolver's stale refusals. Nothing is in flight to strand: task 156 has never been deployed (dev runs
  `bca0941f6`, batches 1+2).
- **Concrete class, not a C# interface.** The harness asked for "an interface exposing only the after-write re-stamp".
  ADR-010 says MUST register concretes and MUST NOT create interfaces without a genuine seam, and there is one
  implementation. A sealed class whose single public member is that re-stamp exposes exactly the same surface. This is
  §6.5 path C for that wording (pivot to comply); the owner's own words were "a narrow, write-only re-stamp helper".
- **Recorded in:** the handler's remarks, this section, and the POML `<execution>` block.

**Not done here, for the merge of task 146 (`task/uac-r2-146-b2-r2`, not merged per the harness):**
- 146 also edits `DataverseUpdateRecordHandler`: its child re-file assigns the owner through
  `IRecordOwnershipResolver.ReparentAsync` (146's §6.5 path A, task 146 note §12c). The two edits touch the same
  constructor, remarks and PATCH block, so they must be merged by hand. Keep 146's `RefileIfFiledAsync`, and call this
  helper after whichever path wrote the caller's update (`refile.Written` or the plain PATCH).
- *(Superseded by verifier round c1, below: the spec and the audit now carry "Amendment A-UAC156" on this branch, at
  places 146 does not touch, and both files merge with 146's branch without a conflict.)* The rule's own text
  (`projects/spaarke-ai-architecture-redesign-r1/spec.md`, "MUST run user-OBO for all Dataverse tool access", and
  `notes/user-obo-audit.md`) was not edited in this round. 146 rewrites that same bullet and adds "Amendment A-UAC146",
  so an edit of that bullet here would conflict. At 146's merge, the reviewer MAY fold A-UAC156 into A-UAC146's
  "Not user-OBO" list (optional; both records are correct side by side):
  > **Not user-OBO, owner-approved (round 8 item 1, CLAUDE.md §6.5 path B) — the update tool's core-ancestor re-stamp**
  > (`dataverse.update_record`, `DataverseUpdateRecordHandler`; unified-access-control-r2 task 156). After the caller's
  > own PATCH succeeds, `CoreAncestorAfterWriteRestamp.AfterWriteAsync` re-stamps APP-ONLY the `sprk_regarding{core}`
  > copies that write moved (the record's own and its children's), in the same operation. Narrow by construction: one
  > member, stamp columns only, values derived from the data. Record: `projects/unified-access-control-r2/notes/task-156-stamp-freshness.md`, "Owner round 8".

### Item 2 — `TaskActionCore` writes the ADR-024 regarding pair (F-051-6)

- **Reuse, not a fork.** The pair-writing half of `TodoRegardingBuilder.ApplyResolverFieldsAsync` (its step 2) is now
  `TodoRegardingBuilder.ApplyResolverPairAsync(host, …)`. The to-do builder calls it, and `TaskActionCore` calls it for
  the `sprk_event` task. It writes the id (lowercase "D"), the name (empty when unknown), the relative record url, and the
  type when a `sprk_recordtype_ref` row exists.
  - The type comes from `ICommunicationDataverseService.QueryRecordTypeRefAsync`, the lookup every SDK-path builder
    uses. So `TaskActionCore`, `ActionSeam` and `CreateTaskNodeExecutor` take that interface. It is an existing,
    unconditional singleton forwarder (`GraphModule`): no new registration and no asymmetric dependency.
  - The name is the regarding record's primary name, from the shared `RegardingNameFields` map. `sprk_communication` →
    `sprk_name` was added there (verified live, read-only: `sprk_name` NVARCHAR(850)). It is inert for that map's other
    two callers, because a communication is never an association candidate (`RegardingFieldMap` does not list it).
    Report card has no entry, so its name stays empty, which is the builders' convention.
  - The name is capped at `sprk_event.sprk_regardingrecordname`'s live length, 1000, so an overlong name cannot sink the
    create (the `InvoiceReviewService` precedent).
  - The pair is skipped only for a `sprk_recordtype_ref` target, whose typed lookup IS the pair's type column.
- **Live shape (read-only, spaarkedev1, 2026-10-02/03).** `sprk_event` carries all four pair columns
  (`sprk_regardingrecordid` 100, `sprk_regardingrecordname` 1000, `sprk_regardingrecordurl` 2000,
  `sprk_regardingrecordtype` → `sprk_recordtype_ref`). Of the 14 record-type rows, none is for `sprk_communication`, so a
  task under a communication carries an **untyped** pair. The job's untyped clause finds it by id.
- **Behaviour.** The classification is unchanged. A task under a root now reads as a direct link (rule 2) instead of
  "filed under nothing" (rule 1); a task under an intermediate reads as that source (rule 3) instead of rule 5. Both give
  the same answer. The AI / communication "create task" follow-ups now show the regarding link in the UI.
- **New rows only; nothing is backfilled.** Live, one older `TaskActionCore` event sits under an intermediate with no
  pair (`edfef460`, under a communication that names no root), so no copy is orphaned.
- **For the merge of task 146.** 146 also changes `TaskActionCore`'s constructor (`IRecordOwnershipResolver`) and the same
  two call sites, which is a mechanical conflict. Its owner resolution `RecordOwnershipContext.ForChild(entity, …)` reads
  "every parent lookup now on the row". The pair adds `sprk_regardingrecordtype`, an `EntityReference` to
  `sprk_recordtype_ref`, which is not an ownership parent. Check at the merge that `ForChild` ignores it.

### Item 3 — confirmed as shipped; the column lock is task 168

Owner round 8 item 3 chose options 1 + 3:
- The rule as shipped is CONFIRMED: the pair decides, a root column on a filed row is a copy, and option (b) stays whole
  in both directions. Task 156 changes no code for it.
- The lock is **task 168**, not task 156: the four `sprk_regarding{core}` columns become read-only on the to-do, event,
  communication and analysis forms, after a check of which forms expose them.

### Also changed

- `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` I-1 row:
  - L1 now includes the AI update tool;
  - the two "owner decision pending" gaps are closed;
  - the remaining gap is the pre-round-8 `TaskActionCore` rows (nothing backfilled);
  - item 3 is recorded as confirmed, with the lock at task 168.
- Two exact-field-set seam tests (`CreateTaskNodeExecutorSeamTests`, `ActionSeamTests`) now expect the pair's id, name and
  url, because the owner's decision changes that contract.
- `CatalogToolDescriptionParityContractTests`' construction helper builds a no-op delegate for a delegate parameter
  (`CoreAncestorResolver.EntityColumnProbe`, reached through the new sealed helper). It is the class-wide fix, as
  compose-r8 task 061 did for optional concretes, and the parity assertion is unchanged.
- `CoreAncestorRestampJobHandlerTests`:
  - the after-write cases are gone with the path;
  - the H2 truncation guard is now pinned with a CHILD payload (a stale event with three to-dos under it, bound 2);
  - the idempotency test now pins the per-minute collapse and a new key after the minute.

### Seeds (each restored byte-identical from a backup, then touched; SHA-256 re-verified; a failed build reports nothing)

| Seed | Removed guard | Bit |
|---|---|---|
| U1 | the handler's inline `_restamp.AfterWriteAsync` call (an empty report instead) | 2 red: `…_UpdateThatMovesAnEventsPair_RestampsTheEventAndItsChildren_InTheSameCall`, `…_RestampOfAChildFails_TheUsersUpdateStands` |
| U2 | the helper's registration (`TryAddSingleton<CoreAncestorAfterWriteRestamp>`) | 1 red: `DiGraphValidationTests.BffDiGraph_InDevelopment_HasNoCaptiveDependenciesOrUnresolvableServices` (the whole-graph check; no DI-registration test was added — ADR-038). Survived the handler / tool-framework / data-mutation filter first (683 tests), so it is the graph check that pins it |
| T1 | `TaskActionCore`'s pair write (runtime-false) | 5 red: the pair test, both F-051-6 job tests, both exact-field-set seam tests |
| T2 | the regarding name read | 1 red: the pair test |
| T3 | the pair's type in `ApplyResolverPairAsync` | 2 red: `TodoRegardingBuilderTests.ApplyResolverFieldsAsync_TypicalCase_…` and the typed F-051-6 job test. First run: only the builder test, because the job test's type assertion used `?.` (skipped on null); fixed, re-seeded red |
| J1 | the job handler's "table carries no stamp → dead letter" guard (simplified this round) | 2 red: `PayloadThatCannotMoveAStamp_IsPoisoned` (sprk_matter, sprk_invoice) |
| H2 | truncated-only → Success (re-pinned with a child payload) | 1 red: `TruncatedOnlyCascade_IsCompleted_NeverRetriedOrPoisoned` |

### Tests this round

New:
- the AI update tool: 4 (re-stamps in the same call; a failed child leaves the update standing; a refused update
  re-stamps nothing; a write that can move nothing reads nothing). These replace the 2 enqueue tests.
- `TaskActionCore`: the pair test (`ServerWriterAncestorStampingTests`) and 2 F-051-6 job tests (untyped under a
  communication, typed under an invoice).

Changed: 2 exact-field-set seam tests; the job handler tests (the after-write test removed, H2 / round-trip / idempotency
rewritten); 16 test constructor call sites.

Results:
- Affected, the round-2 filter: **517 / 517**. That is 513, minus the removed after-write job test, plus 2 net handler
  tests, plus 2 job tests and the pair test.
- Affected, item 2 (`TaskActionCore`, the create-task executor and seam, `TodoRegardingBuilder`, `TodoGeneration`,
  communication follow-ups, the tool framework): **511 passed / 0 failed / 1 skipped (Total 512)**.
- Full BFF unit suite, once at the end: **Passed 14393 / Failed 0 / Skipped 54 (Total 14447)**.
  The first full run (made while another agent's full run shared the machine; 28 m 30 s) reported 3 failures: CatalogToolDescriptionParityContractTests.EverySingleRowHandler_MetadataDescription_MatchesAuthoredSeedRow, which was real (its handler-construction helper could not stub the delegate EntityColumnProbe reached through the new sealed helper; the helper now builds a no-op delegate; fixed), and two contention timeouts that pass on an isolated re-run (RelatedRecordCardContractTests.ResolveIdentity_ForAPaneCreatedMatterWithNoNumberYet_ReturnsANullNumber_NotAnError and SpeAdmin.SearchItemsTests.SearchItems_WithToken_ValidConfigIdNotFound_Returns400, 2 m 54 s and 2 m 50 s; isolated: 17 / 17 with the parity class). The final run below is at the committed tree.
- NetArchTest: **Passed 346 / Failed 0 (Total 346)**.
- `tests/integration/Sprk.Bff.Api.IntegrationTests`, in full: **Passed 104 / Failed 0 / Skipped 0 (Total 104)**.
- `tests/integration/Spe.Integration.Tests`, in full: **Passed 403 / Failed 0 / Skipped 25 (Total 428)** (the suite's own 25 skips).

### Still open

- **AC8**, the manual dev live gate: the main session's, after deploy (POML `manual-live-gate`).
- **Item 13a**, the TypeScript stamp mirror. Owner round 8 files it as work ("156 item 13a"). It is not in this round's
  items, so it is not done here. **Done by task 169** (2026-10-04).
- **#1098**, the events API. Not task 156's.

## Verifier round c1 (2026-10-03, branch `task/uac-r2-156-c1-r1`)

Branch created from `task/uac-r2-156-c1` at `65b702473` (baseSha). No merge was needed. No production code changed: two
test files and three documents changed.

**Items 1-6, 9-11, 13: verified by the verifier; nothing to change.** The observation in item 10 (a new
`TaskActionCore` task now classifies like a to-do, so a form edit of its typed lookup that leaves the pair alone reads as
`InconsistentPair`) matches owner round 8 item 2, "exactly as the other regarding builders do". Round 8 item 3 makes the
RegardingResolver picker the only way to set a root (task 168). No change was requested.

**Items 7 and 14 (AC7, seed N1 survived; two other `ReadRegardingNameAsync` branches untested): closed.** Three tests in
`ServerWriterAncestorStampingTests`, next to the pair test. The name is display-only, so reading it must never cost the
task:
- **Over-long name.** The communication's name is 1207 characters, and the entity-service double refuses any create whose
  `sprk_regardingrecordname` is longer than 1000, as Dataverse does. The column's length was confirmed live with a
  read-only `describe` of `sprk_event` on 2026-10-03: NVARCHAR(1000). The test asserts the task is created and that the
  name is the first 1000 characters.
- **A failed name read.** The read of the communication throws. The test asserts the task is still created with the
  pair's id and url, an empty name, its typed lookup and its stamp.
- **A type with no name column** (a report card). The test asserts the pair's id and url, an empty name, and no read of
  the report card.

**Items 8 and 14 (seed Q1 survived): closed.** One test in `DataverseUpdateRecordHandlerTests` covers the AI tool's most
common re-file: an `update_record` of a communication filed under nothing, writing its own `sprk_regardingmatter` A → B as
a lookup object, with a to-do under it. The test asserts:
- the caller's PATCH runs first, through the user's client, and nothing else does;
- the to-do is re-stamped to B in the same call, writing only `sprk_regardingmatter`;
- the communication keeps the caller's B, and the helper never PATCHes it.

**Item 12 (the spec rule's own text was stale): closed.**
- `projects/spaarke-ai-architecture-redesign-r1/spec.md` gains **"Amendment A-UAC156"**, a bullet directly under the MUST
  rules. It names the amended MUST and FR-P0-10, the path (§6.5 B, owner round 8 item 1), the scope (the re-stamp only)
  and the helper's narrowness.
- `notes/user-obo-audit.md` marks the `dataverse.update_record` row as amended and adds a dated note under §3A.
- Both edits avoid the lines task 146 changes (the MUST bullet, 146's ADR Tensions section, the audit's header). A
  three-way `git merge-file` of each file against `task/uac-r2-146-b2-r2` (merge base `ea6484102`) gives **0 conflicts**,
  and the merged text carries both amendments. So the spec is correct whichever task merges first. Folding A-UAC156 into
  A-UAC146 at 146's merge is optional (the bullet above in "Owner round 8").

**Item 15 (AC8): not closed.** The manual dev live gate belongs to the main session, after deploy (POML `manual-live-gate`).

### Seeds (each restored byte-identical from a backup, then touched; SHA-256 re-verified; every seed compiled)

Run against the affected filter (the handler, data-mutation stamping, `TaskActionCore`, `CoreAncestor*`, create-task
executor and `ActionSeam` tests: 184 tests).

| Seed | Mutation | Bit |
|---|---|---|
| Q1 | `CoreAncestorAfterWriteRestamp` calls `RestampChildAsync(entity, id, None)` instead of `AfterWriteAsync(…, writtenColumns, None)` | 1 red: `…_UpdateOfACommunicationsOwnRoot_CascadesTheNewRootToItsChildren_InTheSameCall` |
| N1 | `TaskActionCore`'s cap removed (`… ? name[..1000] : name` → `name`) | 1 red: `CreateTask_WhenTheRegardingNameIsLongerThanTheColumn_CapsItAndTheTaskIsStillCreated` |
| R1 | the failed-read catch narrowed to `when (ex is OperationCanceledException)`, so a read failure propagates | 1 red: `CreateTask_WhenTheRegardingNameReadFails_…` |
| U2 | an unmapped type guesses a column (`PrimaryNameField(type) ?? "sprk_name"`) | 1 red: `CreateTask_WhenTheRegardingTypeHasNoNameColumn_…` |
| U3 | an unmapped type yields a name (`return regardingType;`) | 1 red: the same test |

### Tests this round

New: 4 tests (handler 1, `TaskActionCore` name branches 3). Nothing else changed.
- Affected (the filter above): **184 / 184**.
- Full BFF unit suite, run once at the end: **Passed 14397 / Failed 0 / Skipped 54 (Total 14451)**. That is round 8's
  14393 plus the 4 new tests. No failures, so nothing was re-run for contention.
- NetArchTest: **Passed 346 / Failed 0 (Total 346)**.
- `tests/integration/Sprk.Bff.Api.IntegrationTests`, in full: **Passed 104 / Failed 0 / Skipped 0 (Total 104)**.
- `tests/integration/Spe.Integration.Tests`, in full: **Passed 403 / Failed 0 / Skipped 25 (Total 428)** (the suite's own
  25 skips).
- The pre-commit formatter (`dotnet format --include` on the two test files) was run before the suites and changed
  nothing.

## Verifier round c1-r2 (2026-10-03, branch `task/uac-r2-156-c1-r2`)

Branch created from `task/uac-r2-156-c1-r1` at `f2064feab` (baseSha). No merge: the only integration commit the branch
lacks is the docs-only checkpoint `b18a7d321`, and the verifier found the merge clean. No production code changed: one
test file and two documents changed.

**Item 1 (AC7: seed V1 survived the whole BFF unit suite): closed with a test. The branch is reachable, so it stays.**
- *How it is reached.* `CreateTaskNodeExecutor` passes the playbook's `config.RegardingObjectType` through as text, and
  `ActionSeam` passes `CreateTaskRequest.RegardingObjectType` through, which `CommunicationCreateTaskApplyService`
  fills from a proposal's `regardingObjectType`. Neither checks the type against a list. `TaskActionCore`'s
  `RegardingFieldByEntity` maps `sprk_recordtype_ref` → `sprk_regardingrecordtype`, and `CoreAncestorResolver` classes
  that type as `Unclassified`, which is a success. So the task is created.
- *The test.* `CreateTask_WhenRegardingARecordTypeRow_KeepsItsTypedLookupAndWritesNoPair`, in
  `ServerWriterAncestorStampingTests`, next to the other pair tests. In its world every type has a record-type row,
  `sprk_recordtype_ref` included. That means a pair write would also replace the typed lookup. It asserts:
  - the task is created;
  - `sprk_regardingrecordtype` still names the filed record-type row (entity and id);
  - no `sprk_regardingrecordid`, `sprk_regardingrecordurl` or `sprk_regardingrecordname` is written;
  - the record-type lookup is never called.
- Seed V1 (`if (regardingField.Length > 0)`): 1 red.

**Item 2 (AC7: seed V16, the rethrow in `ReadRegardingNameAsync`): pinned. It is NOT an equivalent mutant.**
- *The difference V16 makes.* Suppose every Dataverse call honours the token, as the real client does. Without the
  rethrow, the name read logs the cancellation at Debug and returns null. Next, the resolver's column probe catches its
  own cancellation as a failed derivation, which fails closed. `TaskActionCore` then returns the degraded `Guid.Empty`.
- *What the caller sees.* `ActionSeam` reports `CreateTaskResult(true, Guid.Empty, null)`, and `CreateTaskNodeExecutor`
  reports `NodeOutput.Ok` ("Task created"). So a cancelled request reads as a success.
- *With the rethrow*, the cancellation propagates: the executor returns `NodeOutput.Error`, and `ActionSeam`'s caller
  receives the `OperationCanceledException`.
- *The test.* `CreateTask_WhenCancelledDuringTheRegardingNameRead_PropagatesTheCancellationAndCreatesNothing`. The
  token is already cancelled. Every double honours it: the entity service's reads and create, and the resolver's own
  reads and column probe. The test asserts:
  - an `OperationCanceledException` is thrown;
  - nothing is created;
  - the record-type lookup after the name read never runs.
- Seed V16 (the `catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }` removed): 1 red. The
  failure was "no exception was thrown", which confirms the difference above.

**Items 3-13: verified by the verifier; nothing to change.** **Item 14** (the AC7 shortfall) is closed by items 1 and 2.
**AC8** (the manual dev live gate) remains the main session's, after deploy.

### Seeds (each restored byte-identical from a backup, then touched; SHA-256 re-verified; every seed compiled)

Run against the affected filter: the handler, data-mutation stamping, `TaskActionCore`, `CoreAncestor*`, create-task
executor, `ActionSeam` and `TodoRegardingBuilder` tests (206 tests).

| Seed | Mutation | Bit |
|---|---|---|
| V1 | `TaskActionCore`'s record-type skip → `if (regardingField.Length > 0)` | 1 red: `CreateTask_WhenRegardingARecordTypeRow_KeepsItsTypedLookupAndWritesNoPair` |
| V16 | `ReadRegardingNameAsync`'s cancellation rethrow removed | 1 red: `CreateTask_WhenCancelledDuringTheRegardingNameRead_PropagatesTheCancellationAndCreatesNothing` |

### Tests this round

New: 2 tests, both in `ServerWriterAncestorStampingTests`. Nothing else changed.
- Affected (the filter above): **206 / 206**.
- Full BFF unit suite, run once at the end: **Passed 14399 / Failed 0 / Skipped 54 (Total 14453)**. That is round c1's
  14397 plus the 2 new tests. There were no failures, so nothing was re-run for contention.
- NetArchTest: **Passed 346 / Failed 0 (Total 346)**.
- `tests/integration/Sprk.Bff.Api.IntegrationTests`, in full: **Passed 104 / Failed 0 / Skipped 0 (Total 104)**.
- `tests/integration/Spe.Integration.Tests`, in full: **Passed 403 / Failed 0 / Skipped 25 (Total 428)** (the suite's own
  25 skips).
- The pre-commit formatter (`dotnet format --include` on the test file) was run before the suites and changed nothing.

## Placement justification (CLAUDE.md §10 / §11, `bff-extensions.md`)

All five new types live in the BFF, in `Services/Dataverse/` beside the invariant's owner (`CoreAncestorResolver`):
BFF domain code over BFF-owned tables, BFF identity, low volume (ADR-052 B2/B3). The fifth, `CoreAncestorAfterWriteRestamp`,
was added by owner round 8 item 1. No new interface, endpoint, option class, package or plugin; every registration is
unconditional (ADR-032: no Null-Object question).

| New surface | Existing (grep, 2026-10-02) | Why not extend it | Cost of doing nothing |
|---|---|---|---|
| `CoreAncestorRestamper` | `CoreAncestorResolver` derives a stamp for ONE row being written; nothing anywhere re-derives a stamp on rows already written (`grep -rn "Restamp\|ReStamp" src/server` → none before this task) | Folding the cascade into the resolver would mix a pure, per-write derivation (used inline by 8 writers) with paged queries and PATCHes of other rows; the restamper REUSES the resolver for every derivation instead of copying it | Re-filing an intermediate leaves every child's copy stale: an access over-grant (task 051 §1) and, without the comparison, the #1038 leak |
| `CoreAncestorStampReconciliationJob` | `MembershipReconciliationJob`, `ExternalAccessReconciliationJob` reconcile other tables; nothing reconciles stamps | A job per invariant is the ADR-036 shape; neither existing job's scan or rules apply | Writes outside the BFF (forms, flows, imports, the client wizards and the client stamp mirror) and form clears (F-051-6) stay stale forever; the resolver would then refuse those uploads (stale) forever |
| `CoreAncestorRestampQueue` + `CoreAncestorRestampJobHandler` | `JobSubmissionService` / `IJobHandler` (ADR-004) — reused, not duplicated: the queue is a 20-line typed submitter, the handler a thin adapter onto the restamper | The resolver must not take a Service Bus dependency at construction (it is constructed on every upload) | A stale refusal would wait up to 5 minutes for the job instead of seconds. (Since owner round 8 item 1 the queue carries stale refusals only.) |
| `CoreAncestorAfterWriteRestamp` (owner round 8 item 1) | `CoreAncestorRestamper.AfterWriteAsync` (the cascade itself) and `CoreAncestorRestampQueue` (the enqueue the tool used) — grep 2026-10-03: no other narrow re-stamp entry point exists | Injecting `CoreAncestorRestamper` into the user-OBO tool would hand it three app-only entry points (any child, any intermediate) instead of the one the amendment allows; the queue is the deferred path the owner replaced. So a sealed 10-line wrapper exposing only the after-write re-stamp, holding only the restamper (no `IServiceProvider`) | AC1 unmet for the AI update tool (its re-files leave children stale until a queued job runs — the deviation the owner rejected), or the tool holds the whole restamper (wider than the amended rule) |
| `container_ancestor_stale` (problem code) | `container_ancestor_unverifiable` means "cannot be compared"; `container_ancestor_unresolved` means "unknown / unreadable" | Reusing either would mis-classify the inbound retry (stale is transient — the re-stamp makes the retry succeed; unverifiable / unresolved-409 are permanent skips) | A stale email would be skipped permanently, losing its archive |

**Publish size:** code only — no package, no new assembly reference. Measured in verifier round 1 from fresh short-path
worktrees: +0.04 MB for the whole task, +0.03 MB vs master (table in that section). Verifier round 2 added −120 bytes
(table in that section).

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
- **Verifier round 2:** 11 new test cases (resolver 5, OBO PUT route 3, event PUT route 2, queue handler 1); affected
  suites **499 / 499** (filter in the verifier round 2 section); full BFF unit **Passed 13527 / Failed 0 / Skipped 54
  (Total 13581)**, which is round 1's 13516 + 11; NetArchTest **337 / 337**; 6 seeds, each red.
  - One honest caveat: the first full run, made while NetArchTest ran in parallel on the same machine, reported 1 failure
    whose name the truncated console output did not keep. The re-run on its own was clean, and so was the run at the
    commit `aafe12ee7`, after the pre-commit formatter (13527 / 0 / 54 in 11 m 13 s; NetArchTest 337 / 337). Nothing in
    this round's diff is timing-dependent.
- **Verifier round 3** (on the tree merged with `work/unified-access-control-r2`): 6 new test cases (restamper 3, job 3)
  plus retryable assertions on 3 Office-save route tests. Affected suites **513 / 513**; full BFF unit **Passed 14388 /
  Failed 0 / Skipped 54 (Total 14442)**; NetArchTest **345 / 345**; 4 seeds (K8, K7, K5, O1), each red.
- **Verifier round 4** (on the tree merged with `work/unified-access-control-r2` at `ea6484102`): no test added or
  changed (a doc-comment change only). Affected suites **513 / 513**; both integration suites in full (the project's
  hard gate) and the full unit and arch suites: see the verifier round 4 section.
- **Owner round 8** (on the tree merged with `work/unified-access-control-r2` at `d746422f7`): 7 new test cases (AI update
  tool 4 replacing 2; `TaskActionCore` pair 1; F-051-6 job 2), the job handler tests reshaped, 2 exact-field-set seam
  tests updated. Affected **517 / 517** and **511 / 0 / 1 skipped**. Full BFF unit **Passed 14393 / Failed 0 / Skipped 54 (Total 14447)**; NetArchTest
  **Passed 346 / Failed 0 (Total 346)**; integration **Passed 104 / Failed 0 / Skipped 0 (Total 104)**; SPE **Passed 403 / Failed 0 / Skipped 25 (Total 428)**. 7 seeds, each red (section "Owner round 8").
- **Verifier round c1**: 4 new tests (the AI update tool's direct-root re-file; `TaskActionCore`'s over-long, unreadable
  and unmapped regarding name). Affected **184 / 184**; 5 seeds (Q1, N1, R1, U2, U3), each red. Full suites: see the
  verifier round c1 section.
- **Verifier round c1-r2**: 2 new tests (`TaskActionCore`: a task filed under a `sprk_recordtype_ref` row writes no pair;
  a create cancelled during the regarding-name read propagates the cancellation). Affected **206 / 206**; 2 seeds (V1,
  V16), each red. Full suites: see the verifier round c1-r2 section.
- New test homes: `tests/integration/data-mutation/CoreAncestorStamping/` (StampWorld in-memory Dataverse; restamper; job;
  queue + handler; every re-file path; the real document PUT route) and
  `tests/integration/auth/UnifiedAccessControl/` (stamp freshness, topology lock-step). ADR-038: no mocked HTTP handler,
  no DI-registration or constructor null-check tests; module boundaries only (Dataverse rows, Service Bus).
