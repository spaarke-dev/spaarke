# Core-record delete: design

> **Status:** design, 2026-10-18. Docs only; nothing is built.
> **Approved inputs:** `notes/core-record-delete-inventory.md` (D1–D9), approved by the owner in round 103 item 4 (`notes/session27-owner-decisions-and-research.md:1634-1643`).
> **Owner's hard rule:** deleting a core record deletes its related records, but never another core record and never a reference record (contact, organization and the like). The UI shows a confirm message that says what will be deleted.
> **Line numbers** are from `origin/master` at `49279def1` unless marked otherwise. This worktree is 534 commits behind master, so its own line numbers differ.
> **Binding constraints:** CLAUDE.md §10 (`.claude/constraints/bff-extensions.md`), §11, §17 (write path), ADR-002, ADR-004, ADR-008, ADR-017, ADR-019, ADR-028 A5, ADR-052.

## 0. Summary

- **API.** A BFF operation does the delete, with three routes:
  - a GET preview that returns counts, blockers and a plan token;
  - a `DELETE` with `If-Match: <plan token>`, which answers 202 and queues a job;
  - a GET status route for that job.
- **Who may delete.** Only a caller who holds Dataverse Delete on the record. After a role change, only a named role carries that privilege on the four core tables.
- **The walk.** The related set comes from the descendant walk that `SecureChildReconciler` already runs over `SecureChildLineage`. The walk is extracted into a shared helper, not copied. It cannot reach a core table or a reference table, because neither appears in the lineage map.
- **Execution.** Rows are deleted from the deepest level up, and the core record itself goes last. Each document's SPE file is deleted before its row, through the existing `DocumentCheckoutService` verified-file-first policy. A partial run leaves the core record in place, and a re-run picks up whatever is left.
- **Kept, not deleted.** Decision Records, grants and the ledger are kept: Dataverse's RemoveLink unlinks them, and the existing reconcilers end the grants and mark the ledger rows. Finance rows block the delete. Rows also filed under another core record are kept: RemoveLink unlinks them.
- **D9 is already done.** `sprk_policy_matter` is `Cascade` in master XML (`sprk_Matter.xml:563`, commit `7d6f7044b`, ontology 102b, 2026-10-17). The inventory read an older master.

---

## 1. Scope

### 1.1 Core tables

The core set is `CoreAncestorResolver.CoreRecordEntities` (`Services/Dataverse/CoreAncestorResolver.cs:66-72`): `sprk_project`, `sprk_matter`, `sprk_workassignment` and `sprk_servicerequest`. No other definition exists (inventory §1.1). The operation accepts exactly these four logical names and answers 400 `unsupported_table` for anything else.

### 1.2 What "related" means

A row is **related** to core record R when the lineage walk reaches it from R. The walk is `SecureChildReconciler.Pass.LoadDescendantsAsync` (`Services/Access/SecureChildReconciler.cs:951`). It is level by level, IN-queries of at most 200 ids, at most 6 levels, and an incomplete read throws (`SecureChildShareSynchronizer.cs:270-279`). It runs over the lookups of `SecureChildLineage.Children` (`Services/Access/SecureChildLineage.cs:62`).

The walk is extended for delete in two ways:
- **Service-request lookups.** These are deliberately absent from the lineage map (`SecureChildLineage.cs` remarks: "a service request is a root of its own"). The delete walk adds them from `CoreAncestorResolver.CoreAncestorLookups` (`:116`, `sprk_regardingservicerequest`) and `sprk_document.sprk_relatedservicerequest` (inventory §2.4). This addition is for delete only. `SecureChildLineage` itself is not changed, so secure-child behaviour stays the same.
- **Tables outside the lineage map.** These are reached by one probe each on their core lookup: `sprk_communicationrule` (matter), `sprk_externalidentifier` (live only, ebilling-r1), and the kept tables `sprk_externalrecordaccess` and `sprk_assignedaccess`, which are counted for the preview only.

**By table** (the full relationship map is in inventory §2.1–2.4, with file:line evidence):

| Core table | Related (reached by the walk) | Never reached |
|---|---|---|
| `sprk_matter` | documents (`sprk_matter` / `sprk_relatedmatter`), communications, threads, Signals, events, to-dos, memos, KPI assessments, budgets, report cards, spend snapshots and signals, agreements, analyses, communication rules, invoices, billing events, budget revisions and Decision Records, plus every lineage hop below those | `sprk_workassignment` / `sprk_servicerequest` filed under it (CORE, inventory §2.1 rows 1–2), `email` (system), contacts and organizations (N:N) |
| `sprk_project` | the same set through the project lookups (inventory §2.2) | work assignments and service requests filed under it; matter (N:N `sprk_project_sprk_matter`) |
| `sprk_workassignment` | Signals, events, communications, threads, documents, memos, to-dos and analyses (inventory §2.3) | service requests filed under it |
| `sprk_servicerequest` | communications, threads, related documents, analyses, events, memos, Signals and to-dos (inventory §2.4) | — |

**Why core and reference tables are unreachable by construction.** `SecureChildLineage.Children` contains no root and no identity table. `SecureChildLineageTests` pins that every lookup targets a root or a codified child. The planner adds a second, independent check (§7): a hard deny list (§1.3) applied to every row before it can be given the Delete disposition.

### 1.3 Dispositions

The planner classifies every row it reaches as Delete, Keep, Block or Unlink. Tables are matched by logical name. A table the live metadata says does not exist (the ebilling-r1 tables on master-only environments) is skipped. A metadata read that **fails** fails the whole preview: 503, never "none", following the `HasRelatedRecordsAsync` rule at `Infrastructure/ExternalAccess/ExternalDataService.cs:2814`.

| Disposition | Tables | Decision |
|---|---|---|
| **Delete** | `sprk_document` (+ `sprk_fileversion`, `sprk_emailartifact`, `sprk_attachmentartifact`), `sprk_analysis` (+ `sprk_analysisoutput`, which cascades), `sprk_communication`, `sprk_communicationthread`, `sprk_communicationattachment`, `sprk_communicationparticipant`, `sprk_emailreviewlog`, `sprk_event`, `sprk_eventlog`, `sprk_todo`, `sprk_memo`, `sprk_signal`, `sprk_kpiassessment`, `sprk_reportcard`, `sprk_agreement`, `sprk_communicationrule` | inventory §4.1, D8; Signals per round 103; rate cards per D-120 (§10); rule sets per D-137 (§11) |
| **Keep** (RemoveLink unlinks) | `sprk_decisionrecord`, `sprk_externalrecordaccess`, `sprk_assignedaccess`, `sprk_budget` (+ `sprk_budgetbucket`, `sprk_budgetrevision`), `sprk_spendsnapshot`, `sprk_spendsignal`, R's own `sprk_ruleset` (+ its `sprk_policy` / `sprk_policyversion`) and R's own `sprk_ratecard` (+ `sprk_ratecardrate`), **always**: unlinked, cited or not, and applying to nothing afterwards (ebilling D-146, §12) | D2, D4; budgets and spend per D-120 (§10); rule sets and rate cards per D-146 (§12, superseding D-137 and D-120's card row). The operation itself clears the core lookup when the relationship is not RemoveLink. |
| **Block** (the whole delete is refused) | `sprk_invoice`, `sprk_billingevent`, `sprk_invoicesubmission`, `sprk_invoiceapproval`, `sprk_invoicecorrection`, `sprk_externalidentifier` | D3 as settled by ebilling D-120 (§10). Rate cards are now deleted with the record, and budgets (with their buckets and revisions) are kept and unlinked. Neither blocks. |
| **Platform cascade** (counted, not deleted by the BFF) | `sprk_policy` (matter; `Cascade`, D9) | — |
| **Hard deny** (never Delete, whatever the walk says) | the four core tables, `contact`, `account`, `systemuser`, `team`, `businessunit`, `sprk_organization`, `email`, every `*_ref` table, `sprk_analysisaction/knowledge/skill/tool`, `sprk_policyversion`, `sprk_ruleset`, `sprk_billingcodeset` (the one exception: R's own uncited rule set, §11) | Owner rule; inventory §1.2, §4.3 |

**Rows that name the record by text id, not by lookup** (inventory §2.6). The operation deletes `sprk_navitem` rows whose `sprk_targetid` names R, because they are pointers to the record that would otherwise go dead, and `sprk_noaccessentry` rows for R, because they are the record's own access configuration, like a policy. **Unverified:** the no-access entry's record-key column was not read. Task 2 reads it live before relying on it. `sprk_notificationoutbox` (a delivery log), `sprk_affinity` and `sprk_uploadcontext` are left alone. `sprk_userentityassociation` is already cleaned by the nightly orphan scan (`MembershipReconciliationJob`).

### 1.4 Rows also filed elsewhere (D5)

A row's **own core claims** are the core records named by its own core columns: `CoreAncestorLookups`, plus the typed root columns in `IntermediateRootColumns` (`CoreAncestorResolver.cs:169`), such as a document's `sprk_matter`, an invoice's `sprk_project` or a Signal's `sprk_matter`.

| Case | Disposition |
|---|---|
| Own claims are {R} and nothing else | the table's disposition |
| Own claims include another core record | **Unlink**: kept. RemoveLink clears its R lookups when R goes (all the relationships involved are RemoveLink, inventory §2). |
| No own claim | Delete only when **every** lineage parent it has set is itself being deleted. Otherwise Unlink. |
| A Cascade child (analysis under a document, bucket under a budget, output under an analysis) | follows its parent. The platform deletes it with the parent. |

Some dev examples (inventory §3a):
- The 5 matter+project communications are Unlink.
- The 2 communications filed to matter b68299c6 but threaded under 1e992cc9 are kept when 1e992cc9 is deleted. They are deleted when b68299c6 is.
- The 5 documents that have no core of their own, attached to a matter+project communication, are kept, because their parent is kept.

**Known limit (K):** a Cascade child that is also filed elsewhere is deleted with its parent. On dev, 0 analyses carry a `sprk_regarding{core}` (inventory §2.1). The preview counts these rows separately, so the confirm message says so.

**Stamps can be stale.** A child's `sprk_regarding{core}` stamp is a copy, refreshed within about 5 minutes by `CoreAncestorStampReconciliationJob` (`DATAVERSE-WRITE-PATH-ARCHITECTURE.md` §4.1). The preview reports what the rows say at the time it is read, and the plan token (§2.2) catches any change between the preview and the delete.

---

## 2. API

New file: `Api/CoreRecords/CoreRecordDeleteEndpoints.cs`. It is mapped through `EndpointMappingExtensions` (`Infrastructure/DI/EndpointMappingExtensions.cs:251` is where `MapChildRecordEndpoints` sits) and never in `Program.cs` (bff-extensions §C).

The group is `/api/v1/core-records` with `.RequireAuthorization()` and `.RequireRateLimiting("dataverse-query")`, as `ChildRecordEndpoints.cs:172-175` does. Errors are RFC 7807 with a `reasonCode` (ADR-019).

### 2.1 Preview

`GET /api/v1/core-records/{table}/{id}/delete-preview` returns 200 with:

```json
{
  "record": { "table": "sprk_matter", "id": "…" },
  "planToken": "sha256:…",
  "willDelete":   [ { "table": "sprk_document", "count": 12, "files": 11 }, … ],
  "willUnlink":   [ { "table": "sprk_communication", "count": 2 } ],
  "willKeep":     [ { "table": "sprk_decisionrecord", "count": 3 }, { "table": "sprk_externalrecordaccess", "count": 4 } ],
  "platformCascade": [ { "table": "sprk_policy", "count": 1 } ],
  "linksRemoved": { "contacts": 2, "organizations": 1, "coreRecords": 1 },
  "blockers": [ { "reasonCode": "finance_records", "table": "sprk_invoice", "count": 3 },
                { "reasonCode": "document_checked_out", "table": "sprk_document", "count": 1 } ],
  "deletable": false
}
```

- **Counts only.** No names and no ids of other records. The walk runs app-only and so sees rows the caller may not be able to read, and the preview must not become a read path. The filter (§2.4) already limits the preview to callers who hold Delete on R.
- **Blockers:**
  - `finance_records`: any Block-table row whose own claims include R;
  - `document_checked_out`: `DocumentCheckoutService` refuses a checked-out document (`DocumentCheckoutService.cs:781`, the `IsCheckedOut` branch);
  - `walk_truncated`: more than `MaxPages × PageSize` rows in a table. The reconciler already refuses a partial set (`SecureChildReconciler.cs`, the `ReadAllPagesAsync` throw).
- **`planToken`** is SHA-256 over a rule-version constant, R, the sorted `(table, id, disposition)` list and the blockers. It needs no secret: the server recomputes it and compares, so a forged token simply never matches. Row edits that change nothing in the plan do not change the token.

### 2.2 Delete

`DELETE /api/v1/core-records/{table}/{id}` with `If-Match: "<planToken>"`.

| Answer | When |
|---|---|
| **428** `plan_token_required` | no `If-Match` header |
| **412** `plan_changed` | the recomputed token differs (the body carries the new preview) |
| **409** `core_delete_blocked` | there are blockers |
| **409** `delete_in_progress` | the record's lease is held (§3.2) |
| **202 Accepted** | queued. Body `{ operationId }`, `Location: /api/v1/core-records/delete-operations/{operationId}` (ADR-017). |

`DELETE` with a precondition header is the HTTP-native form of "the token or etag". It avoids a body on DELETE.

### 2.3 Status

`GET /api/v1/core-records/delete-operations/{operationId}` returns the state (`Queued`, `Running`, `Completed`, `Blocked`, `PlanChanged`, `Refused`, `Partial`, `Failed`), the counts done per table, failed rows with reason codes, and the correlation id.

**Authorization is by owner comparison** (the OWNER-COMPARISON basis, round 12 item 4): the caller's `oid` must equal the operation's recorded `oid`. Unknown and not-yours get the same uniform 404, as round 12 item 4 requires for `DELETE /api/memory/pins/{pinId}` (`session27-owner-decisions-and-research.md:237`).

### 2.4 Authorization (fail closed)

- **The filter.** `AddRecordRouteAccessAuthorizationFilter("delete", entitySetName, "id")` (`Api/Filters/RecordRouteAccessAuthorizationFilter.cs:57-75`) goes on both the preview and the delete. It is mapped once per core table in a loop over `CoreRecordEntities`, so each route has a constant entity set.
  - The operation key `"delete"` already exists and means `AccessRights.Delete` (`src/server/shared/Spaarke.Core/Auth/OperationAccessPolicy.cs:279`).
  - The filter asks Dataverse AS THE CALLER through `CallerRecordAccessProbe` (`Infrastructure/ExternalAccess/CallerRecordAccessProbe.cs:70`). Any failure answers None (`:49`).
  - Denial shape: no Read gives a uniform 404, and Read without Delete gives 403 `insufficient_rights` (the filter's documented shape). This is ADR-008: an endpoint filter, not middleware.
  - **No new authorization code.**
- **The named role (D6).** The named role IS the Dataverse Delete privilege on the four core tables (option A; §9 Q1 asks the owner to confirm).
  - Today the Spaarke Core User role holds `prvDelete` on all four at Parent:Child BU depth (inventory §3e). Task 1 removes it and grants it to one new role (name: §9 Q1). From then on, "holds Delete on R" means "holds the named role, at a depth that covers R, or a platform admin role".
  - **Full Access sharees.** `RecordShareLevels.FullAccessRights` includes `DeleteAccess` (`Services/Access/RecordShareLevels.cs:86`). A sharee without the named role is expected to be denied, because a share does not stand in for a missing privilege. That is **unverified**, so the live gate (task 5) tests it.
- **The route guard.** `RouteAuthorizationGuardTests` (`tests/Spaarke.ArchTests/RouteAuthorizationGuardTests*.cs`) requires each new route to declare its basis: a record-level check for preview and delete, owner comparison for status.
- **Endpoint filters (ADR-008):** yes. The repo uses them throughout `Api/Filters/`, and this design uses the existing one. It adds none.

---

## 3. Execution model

### 3.1 A job, decided with numbers

Cost per row, read from code. Latencies were **not measured**.

| Unit | Calls |
|---|---|
| Walk | One query per (child table × parent-table chunk of ≤200 ids) per level, ≤6 levels. That is tens of queries for one record, flat in row count up to 200 parents per table per level. |
| Non-document row | 1 DELETE, or 1/100 with a chunked `ExecuteTransaction` (next to `BulkUpdateAsync`, `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs:2705`) |
| Document | about 8–9 calls in total: |
| | - 1 row read; |
| | - the pointer check (`EnsureDocumentPointerContainerAsync`, `DocumentCheckoutService.cs:926`; at least 1 read, exact count unverified); |
| | - 1 Graph DELETE; |
| | - index removal (knowledge and discovery indexes, each a search plus a delete: `RagIndexingPipeline.cs:158`); |
| | - its file versions; |
| | - 1 row DELETE. |

**The #2098 lesson.** A field-mapping push costs about 17 calls per child, so a 500-child push is about 8,500 calls, which "can exceed App Service's 230 s request limit". The caller then gets a timeout instead of counts, and a row in flight is left without its follow-up steps (#2098, measured in PR #2094).

**The same arithmetic here:**
- At 8–9 calls per document, about 1,000 documents is the same 8,500–9,000-call envelope.
- Dev is small: 157 documents across ALL core records, and at most 137 on matters (inventory §0, §2.1). So one dev record costs at most about 1,250 calls.
- Production is not: matters "with years of email" (communications plus their attachment documents) are where the platform itself switches cascades to async, at 5,000 rows (inventory §3f.4).
- The BFF's app user is shared by all BFF traffic. Dataverse service-protection limits (per Learn about 6,000 requests per 5 minutes per user per web server; **not re-checked for this note**) also argue against a burst inside a user request.

**Decision: always an ADR-004 job, never inline.**
- One path, not a size threshold plus a second, inline path.
- A persisted outcome (ADR-017), even when the client goes away.
- A timeout can no longer be the answer.

The cost is a 202 and a poll on small deletes; on dev graphs the job finishes in seconds.

- **The handler.** `CoreRecordDeleteJobHandler : IJobHandler` in `Services/Jobs/Handlers/` (not AI-coupled, bff-extensions §D), job type `core-record-delete`.
- **The payload:**
  - the root table and id;
  - the plan token;
  - the caller's `systemuserid` (from `WhoAmI` under the caller's own token, in the request) and `oid`;
  - the tenant id (for index routing);
  - the correlation id.
  The payload carries no row lists and no blobs (ADR-004).
- **The idempotency key** is `core-delete-{rootId}-{planToken}`.
- **Placement (ADR-052):** the BFF's Service Bus consumer. §6.2 has the signals.

### 3.2 Ordering

The job runs these steps in order:

0. **Lease** on `core-delete:{rootId}`, through `IIdempotencyService.TryAcquireProcessingLockAsync` (`Services/Jobs/IIdempotencyService.cs:35`) with read-back and a heartbeat. The pattern is `DocumentContainerRelocator.RelocationLease` (`Services/Documents/DocumentContainerRelocator.cs:263`), which compensates for the check-then-set lock that fails open (#984). It is extracted, not copied (§6.1). A lost lease stops the job before its next destructive step, with outcome `Partial(lock-lost)`.
1. **Re-check the caller's right.** App-only `RetrievePrincipalAccess` on R, naming the stored principal. This is a read, which ADR-028 A5 sanctions; no OBO token leaves the request (ADR-052 MUST NOT). If the caller no longer has Delete, the outcome is `Refused` and nothing is deleted. If R is gone, the outcome is `Completed(already-deleted)`.
2. **Re-walk and recompute the token.** A mismatch gives `PlanChanged` and nothing is deleted. Blockers give `Blocked`.
3. **Delete the Delete rows, deepest level first**, then the text-id rows (§1.3).
   - Documents go through `DocumentCheckoutService.DeleteAsync(id, corr, DocumentFileDeletePolicy.VerifiedFileFirstKeepRowOnFailure)` (`DocumentCheckoutService.cs:774`), in this order: the pointer check, the **SPE file** (`:929`), then (new, task 3) its index chunks and file versions, then the row. Documents run with bounded parallelism: start at 4 and measure.
   - Other rows go in chunked `ExecuteTransaction` of 100 or fewer, with a per-row fallback on a fault, so the failing row can be named.
4. **Re-walk.** If any row appeared during the run (an inbound email filed to R, say), the outcome is `Partial(graph-changed)`. **R is not deleted**: the user previews again.
5. **Re-check the right (step 1), then delete R app-only.** Dataverse then takes care of the rest:
   - it disassociates the N:N links;
   - it cascades `sprk_policy` (D9);
   - RemoveLink clears R's lookups on every kept and unlinked row.
6. **Persist the outcome** and emit one structured audit event naming the caller (`oid`, `systemuserid`), R, the counts, the token and the correlation id.

**SPE files first, per document, never last.** The row is the only pointer to its file. A row deleted before its file leaves a file nobody can find, and a re-run cannot reach it. With file first, a failed file delete keeps the row (`DeleteFileOrRefuseAsync`, `DocumentCheckoutService.cs:913`), and a re-run finds the file through the row. An item that is already gone counts as deleted (`:907`), which makes a re-run safe. The same rule puts the **core record last**: RemoveLink empties a child's lookup when its parent goes, so deleting a parent before its children would orphan rows the re-run could no longer walk to.

### 3.3 Partial failure: a defined outcome for every run

| Outcome | Meaning | What remains |
|---|---|---|
| `Completed` | everything done (or R already gone) | — |
| `Blocked` / `PlanChanged` / `Refused` | refused before any delete | everything |
| `Partial(reason, failedRows[])` | some rows deleted. Reasons: `file-not-deleted`, `index-not-cleared`, `row-delete-failed`, `graph-changed`, `lock-lost`. | R and every row not yet deleted. **R is never deleted on a Partial.** |
| `Failed` | an unexpected fault | the same as Partial; the handler reports JobOutcome Failed |

**A re-run is just a new preview and delete.** The walk sees only what is left, and the token covers only that, so a re-run needs no stored progress and no state machine. The graph in Dataverse is the checkpoint. That is the written reason ADR-004 asks for when ADR-052 §7 orchestration is not used. At-least-once delivery is safe: a duplicate message either finds the lease held or finds the work already done.

### 3.4 After the delete (existing owners; nothing new)

| Effect | Existing owner |
|---|---|
| External grants left ACTIVE with empty lookups | `ExternalAccessReconciliationJob` R4 deactivates them (`Services/ExternalAccess/ExternalAccessReconciliationJob.cs:49`) |
| Ledger rows left rootless | `AssignedAccessReconciliationJob` marks them Revoked/RootDeleted (`AssignedAccessReconciliationJob.cs:54`). This needs D4's RemoveLink on the project relationship first (task 1). |
| Stamps on unlinked rows | `CoreAncestorStampReconciliationJob` (about 5 min) |
| Unlinked rows that were Secure-team-owned | Stay isolated; the secure-child sweep reports them NeedsF3 ("never auto-unsecure", `SecureChildReconciler.cs` remarks). This fails closed, as under-access. |
| Membership junction rows | the nightly orphan scan |

---

## 4. Per-decision mapping (D1–D9)

| # | Approved | Concrete behaviour |
|---|---|---|
| D1 | BFF operation | §2–§3. Relationship metadata stays RemoveLink, so a delete that bypasses the BFF orphans rows instead of destroying them. **Exception (§13):** `sprk_relatedcommunication` and `sprk_parentdocument` are Restrict, so a delete there is refused instead. |
| D2 | ~~Decision Records kept~~ **REVERSED round 107 (2026-10-20): Decision Records are DELETED with the record, and so are the Signals that triggered them.** The text after this is the superseded design; build planning rewrites it (DRs and their Signals move to Delete; preview counts them in `willDelete`; narrow any "BFF never deletes a DR" guard to the core-record delete operation). | Superseded: Disposition Keep. RemoveLink clears the typed and regarding lookups. `sprk_corerecordid` keeps R's id as text (inventory §2.6). The BFF never deletes a DR. Signals that cite a kept DR (`sprk_signal.sprk_decisionrecord`, 4 on dev) are deleted under the Signal rule: see §9 Q5. |
| D3 | Finance rows block | Any Block-table row whose own claims include R is a `finance_records` blocker, and the delete is refused (409). Nothing is deleted or unlinked. |
| D4 | Grants and ledger kept; project→assignedaccess to RemoveLink | Not deleted; counted in `willKeep`. Task 1 changes `sprk_sprk_project_sprk_assignedaccess_project` from Cascade to RemoveLink (`src/dataverse/solutions/SpaarkeMaster/Other/Relationships/sprk_Project.xml:524`, `CascadeDelete` at `:532`) live (dry run, `-Apply`, `-Verify`) and in XML. **Task 3 must not ship before task 1:** until then, a project delete still erases ledger rows, and that table has audit OFF (inventory §3d). |
| D5 | Rows filed elsewhere are unlinked, not deleted | §1.4 classification. The preview shows `willUnlink`, and RemoveLink performs the unlink. No explicit PATCH is needed: every relationship involved is RemoveLink (inventory §2), and the one Cascade that touches a kept table (project→assignedaccess) is fixed by D4. |
| D6 | Named role only; the AI tool refuses core tables | §2.4.<br>- **Role:** Core User loses `prvDelete` on the four core tables; the named role holds it (task 1).<br>- **MDA:** the standard Delete is hidden on the four core forms, home grids and subgrids, and replaced with "Delete…", which calls the preview and then the delete (task 5).<br>- **AI tool:** `DataverseDeleteRecordHandler` refuses `tablename` ∈ `CoreRecordEntities` (task 4). |
| D7 | SPE files deleted, never containers | The per-document file delete is §3.2 step 3. The delete code reaches only `DriveItemOperations.DeleteFileAsync` (`Infrastructure/Graph/DriveItemOperations.cs:163`), which goes through the container-ownership guard. It never reaches a container API (ArchTest, §7). A Graph item DELETE goes to the container recycle bin (`OperationAccessPolicy.cs:48`, "soft delete to recycle bin"): see §9 Q2. |
| D8 | Attachments deleted with their communication (**required, not chosen**: §13) | When a communication is Delete, so are its `sprk_communicationattachment`, `sprk_communicationparticipant` and `sprk_emailreviewlog` rows, and every document whose only set lineage parent is that communication (`sprk_relatedcommunication`; 57 on dev, inventory §3c), with its file. A thread is Delete when its own claims are {R}; its communications filed elsewhere stay. |
| D9 | `sprk_policy_matter` Cascade in XML | **Already on master:** `sprk_Matter.xml:563` reads `<CascadeDelete>Cascade</CascadeDelete>` (commit `7d6f7044b`, 2026-10-17). Task 1 only verifies it live. Policy versions and Signals that cite the policy survive with emptied lookups (owner's live check B, inventory §3b). |

**Attachments in general.** Core tables are not notes- or activity-enabled (inventory §2.1, "no `annotation` … relationships"), so there are no OOB attachments on R itself. `sprk_analysis` is activity-enabled; its activities cascade from it (0 on dev).

---

## 5. Security

| Concern | Design |
|---|---|
| **Tenant isolation** | - **Dataverse** is the stamp's one org, by construction (`DataverseImpersonation.cs` remarks).<br>- **SPE file deletes** go through the container-ownership guard inside `DriveItemOperations.DeleteFileAsync`; a refusal is a 404 `spe_container_not_owned`, never a delete.<br>- **Index deletes** carry the caller's validated `tid` and filter on `tenantId` (I2, `tests/Spaarke.ArchTests/TenantIsolation/I2_AiSearchTenantIdFilterTests.cs`).<br>- **The status record** is keyed by operation id and bound to the caller's `oid`. |
| **Secure records** | - **Children:** the BFF deletes app-only, so team ownership (the named Secure Record Owners team, `SecureRecordOwnerTeam`) does not stand in the way and needs no special path.<br>- **Who may delete:** the right to delete is decided ONCE, on R, as the caller (§2.4), then re-checked in the job (§3.2 steps 1 and 5). This is the round 9 write pattern: "a rights pre-check as the user, then an app-only write only where a server invariant needs it" (`session27-owner-decisions-and-research.md:191`). The owner's delete rule is that invariant.<br>- **Unlinked rows:** a row that was isolated because of R and is kept because of another core stays isolated (fail closed). The sweep reports it.<br>- **R's own container:** a secure root has its own container (I-13). It stays, empty (§9 Q3). |
| **Why not a cascade or an impersonated write** | - A Dataverse cascade skips the child's Delete check (inventory §3e) and cannot cover more than one core type (§3f.1).<br>- An impersonated DELETE from the job would be acting for the user with a write. ADR-028 A5 sanctions impersonated **reads**, so a write would need §6.5 path A or B. It is rejected for that reason. |
| **Audit** | - **Decision Records** are kept and never touched by the BFF (D2).<br>- **Dataverse audit** is ON for the core tables and the DR table; audit rows survive a record delete (inventory §3d). App-only deletes are audited under the BFF app user, so the caller is named in the structured event (§3.2 step 6) and in the status record (7-day TTL, as `BatchJobStatusStore.cs:15`). Whether the delete should also be written as a Decision Record is §9 Q4.<br>- **Retention:** `auditretentionperiodv2` is null on dev (#2027, open), so how long the audit rows last is not settled. |
| **The AI tool** | `DataverseDeleteRecordHandler.ExecuteChatAsync` (`Services/Ai/Handlers/DataverseDeleteRecordHandler.cs:119`) gets a refusal before the metadata read (before `:147`). For `tablename` ∈ `CoreAncestorResolver.CoreRecordEntities` it answers `ValidationFailed`: "Core records are deleted from the record's Delete command, which shows what will be deleted." The description at `:65` and `infra/dataverse/sprk_analysistool-dataverse-delete-record-row.json` (byte-equal, per the comment at `:64`) drop `sprk_matter` / `sprk_project` from the entity map and say the tool refuses core records.<br>**Same defect class, fixed in the same task:** the tool also deletes `sprk_document` rows without their SPE file, which breaks D7, so it refuses `sprk_document` too and points to the document's Delete command. |
| **Bypasses that remain (known limit K)** | A named-role holder can still delete R with a raw Web API call, which is a non-product write (WP-5). Every relationship is RemoveLink, so this orphans the related rows rather than destroying them. R4 and the ledger job still end the grants and mark the ledger. Option B in §9 Q1 closes this, at the cost of no human holding Delete. |

---

## 6. Component justification (§11), placement and hot paths

### 6.1 New components

| Component | Existing (overlap) | Extension | Cost of doing nothing |
|---|---|---|---|
| **`LineageDescendantWalk`** (internal static, `Services/Access/`): the walk extracted from `SecureChildReconciler.Pass.LoadDescendantsAsync` (`:951`), taking the lookup map as input | `SecureChildReconciler` (the only walk) | IS the extension: extracted, not copied. The reconciler calls it unchanged, and its tests stay green. | A second walk would drift from the lineage map; a delete would miss rows the secure sweep finds. |
| **`CoreRecordDeletePlan`** (pure: dispositions, the §1.4 classification, the deny list, the token) and **`CoreRecordDeletePlanner`** (I/O: walk + probes), `Services/CoreRecords/` | `ExternalDataService.HasRelatedRecordsAsync` (`:2818`): a yes/no for one service request, 9 relations, 1 level. `CoreAncestorResolver` maps. | The existence probe cannot count, classify or recurse. The resolver answers "which core", not "what goes". Both are reused as inputs. | No preview counts, so the owner's confirm message cannot be built and D5 cannot be applied. |
| **`CoreRecordDeleteJobHandler`** + **`CoreRecordDeleteExecutor`** | `DocumentCheckoutService.DeleteAsync` (one document), `ExternalDataService.DeleteRecordAsync` (`:2728`, one external kind), `SecureRootInheritance` rollback (`:1200`) | Composes the document delete; none of these deletes a graph. | Deletion is left to the MDA's standard Delete, which orphans every child and leaves SPE files behind (inventory §3c). |
| **3 routes** in a new `Api/CoreRecords/CoreRecordDeleteEndpoints.cs` | `ChildRecordEndpoints` (`/api/v1/child-records`: child create and re-file) | Core records are not child records, and that family's filter and table set are child-specific. | No BFF entry point exists, so the MDA and other surfaces have nothing to call. |
| **`CoreRecordsModule.AddCoreRecordsModule()`** (ADR-010) | `ExternalAccessModule`, `DocumentsModule` | Neither owns record lifecycle, so registering there blurs both. | The registrations would land flat in `Program.cs`, which bff-extensions §A.5 forbids. |
| **+1 method on `IRelocatedFileIndexing`** (`Services/Ai/PublicContracts/IRelocatedFileIndexing.cs:18`): remove a deleted document's chunks from both indexes | The facade exists to keep "the RAG index true to a document's file" and is registered unconditionally (`AnalysisServicesModule.cs:124`; `NullRagService` when AI is off) | IS the extension; no new facade. It is called from `DocumentCheckoutService.DeleteCoreAsync`, so the single-document route gets it too. | A deleted document's chunks stay in the index (inventory §3c). CRUD code may not call `IRagService` directly (bff-extensions §A.4). |
| **File-version delete inside `DocumentCheckoutService.DeleteCoreAsync`** | The comment "cascade should handle FileVersions" (`:862`) is false live (inventory §3b; 7 rows) | A modification of existing surface | `sprk_fileversion` rows are orphaned on every document delete. |
| **`BulkDeleteAsync`** in `DataverseServiceClientImpl` | `BulkUpdateAsync` (`:2705`, `ExecuteTransaction` of 1,000 or fewer) | A sibling built the same way | One call per row: about 100× the calls for the non-document rows (§3.1). |
| **Lease helper** extracted from `RelocationLease` | `DocumentContainerRelocator.RelocationLease` (`:263`) | Extracted, used by both | Two concurrent deletes of one R race; a copy would duplicate the #984 compensation. |
| **Status record** (cache, 7-day TTL) | `BatchJobStatusStore` (email-batch shape), `IJobStatusService` (Office, `sprk_processingjob`) | Both shapes are bound to their own feature; a small record of the same kind as `BatchJobStatusStore` | ADR-017: a 202 with no status record leaves the client blind. |
| **Named Dataverse role** + Core User privilege change | Core User's `prvDelete` on the core tables | A privilege change on existing roles plus one role | D6 fails: every Core User can delete a core record. |
| **Schema script** for project→assignedaccess | `scripts/Set-DocumentAnalysisCascadeSchema.ps1` | Same pattern (dry run, `-Apply`, `-Verify`, solution-membership helper) | D4 fails: a project delete erases ledger rows, with audit OFF. |
| **`sprk_core_record_delete.js`** + RibbonDiff on 4 tables | `sprk_access_ribbon.js` (confirm helpers `:434-470`; loaded on 3 of the 4 core tables, access commands only), `sprk_DocumentOperations.js` (document delete) | Copies the `confirmFirst` / `bff_auth.js` pattern. The access ribbon is not loaded on service request, and adding delete to it mixes two responsibilities. | The MDA keeps its generic Delete, with no counts and no BFF. |

### 6.2 Placement justification (bff-extensions §A, ADR-052)

**Everything goes in the BFF.**
- **B1:** the authorization is as the caller (`CallerRecordAccessProbe`, OBO in the request).
- **B2:** the work is almost all BFF domain code: `DocumentCheckoutService`, `SpeFileStore` with its ownership guard, `RecordContainerResolver`, the lineage map, `IRelocatedFileIndexing`.
- **B3:** the volume is low. The owner says core deletes are meant to be rare.

**Against the F signals:**
- **F2** (bursty or heavy work): not material at this rarity.
- **F5** (durable orchestration): answered by the re-walk, since Dataverse holds the state (§3.3).

The decision criteria table in bff-extensions:
- **Latency budget:** the preview, yes (interactive).
- **Writes BFF audit state in the same lifecycle:** yes (the status record).
- **Streaming:** no.
- **ADR-052 places it outside:** no.

The tie-breaker is fewer moving parts.

- **Size impact:** no new package, so the delta is well under 1 MB (estimated; each BFF task measures it against a fresh master build, `.claude/rules/bff-hygiene.md` item 4).
- **Boundary:** no new CRUD→AI dependency (the index goes through `IRelocatedFileIndexing`); feature-module DI; endpoint-filter authorization.

### 6.3 Hot-path declaration

```xml
<hot-path-declaration>
  <bff-api>YES — new Api/CoreRecords/CoreRecordDeleteEndpoints.cs, Services/CoreRecords/*, Services/Jobs/Handlers/CoreRecordDeleteJobHandler.cs, Infrastructure/DI/CoreRecordsModule.cs + one line each in EndpointMappingExtensions and the module list; edits to Services/Access/SecureChildReconciler.cs (walk extracted), Services/DocumentCheckoutService.cs (DeleteCoreAsync), Services/Ai/PublicContracts/IRelocatedFileIndexing.cs + RelocatedFileIndexing.cs, Services/Ai/Handlers/DataverseDeleteRecordHandler.cs, Services/Documents/DocumentContainerRelocator.cs (lease extracted); Spaarke.Dataverse DataverseServiceClientImpl (BulkDeleteAsync)</bff-api>
  <spaarke-ai>NO — the Console is not touched; the routes serve any surface later</spaarke-ai>
  <ci-workflows>NO</ci-workflows>
  <skill-directives>NO</skill-directives>
  <root-CLAUDE-md>NO</root-CLAUDE-md>
</hot-path-declaration>
```

**Other non-BFF touches:**
- `src/client/webresources/js/sprk_core_record_delete.js`;
- the 4 core `RibbonDiff.xml` files under `src/dataverse/solutions/SpaarkeMaster/Entities/`;
- `sprk_Project.xml` (relationships);
- `infra/dataverse/sprk_analysistool-dataverse-delete-record-row.json`;
- the role scripts.

`SecureChildReconciler.cs` and `DataverseDeleteRecordHandler.cs` are shared with ontology-r1. Ontology's 042b restructures `AfterRefileAsync` (current-task.md), so task 2's extraction runs `/conflict-check` first.

---

## 7. Tests

| Kind | Cases |
|---|---|
| **Must fire** | - A matter with a document (with a file), a communication with an attachment document, a to-do, a Signal and an event: the preview counts each, and the job deletes each. The file delete, the index removal and the file-version delete are each called once per document.<br>- The text-id nav item is deleted.<br>- Each of the four core tables can be deleted. |
| **Must not fire** (the owner's rule) | - A work assignment / service request filed under the matter is **never** deleted and never even in the plan.<br>- A contact, organization or project on the far side of an N:N survives.<br>- A communication filed to the matter AND a project is kept (Unlink).<br>- A document with no core of its own, whose communication is kept, is kept.<br>- A Decision Record, an external grant and a ledger row are kept.<br>- Any finance blocker (invoice, billing event, submission, approval, correction, external identifier; D-120) makes the whole delete refuse, with nothing touched. A budget does not block, and it is kept and unlinked.<br>- A table on the hard-deny list cannot get Delete even if a future lineage edit makes it reachable (pure test over `CoreRecordDeletePlan`).<br>- A Full Access sharee without the named role gets 403 (live gate). |
| **Failure path** | - The SPE delete fails: the row is kept, the outcome is `Partial(file-not-deleted)`, R is kept, and a re-run completes.<br>- The index removal fails: the row is kept and the outcome is `Partial`.<br>- A row delete fails mid-chunk: the per-row fallback names the row.<br>- A row appears between steps 3 and 4: `Partial(graph-changed)`, R kept.<br>- The token is stale: 412, nothing deleted. No `If-Match`: 428.<br>- The lease is held: 409. The lease is lost mid-run: it stops before the next delete.<br>- The caller loses Delete before step 5: `Refused`, R kept.<br>- A metadata or table read fails: preview 503, never "none".<br>- The walk is truncated: `walk_truncated`.<br>- A duplicate Service Bus delivery is a no-op. |
| **AI tool** | It refuses each of the 4 core tables and `sprk_document` (must fire). It still deletes a `sprk_todo` (must not fire). The description literal still equals the JSON row (the existing parity test). |
| **ArchTest guard** (`CoreRecordDeleteGuardTests`, IL scan via `IlCallScan`) | 1. **Who may delete a core table.** A method that loads a core entity-set or logical-name constant (`sprk_matters`, `sprk_projects`, `sprk_workassignments`, `sprk_servicerequests`, or the logical names) and calls a delete (`IGenericEntityService.DeleteAsync`, `IDataverseUserClient.DeleteAsync`, `HttpClient.DeleteAsync`, `HttpMethod.Delete`) must be `CoreRecordDeleteExecutor`. **Ratchet baseline:** `SecureRootInheritance` (create rollback, `:1200`) and `ExternalDataService.DeleteRecordAsync` (service request, gated by `HasRelatedRecordsAsync`).<br>2. **No container delete.** `Services/CoreRecords/**` references no container-delete API.<br>**Controls:** a must-fire fixture that deletes from `sprk_matters`, and a must-not-fire fixture that deletes from `sprk_todos`. |
| **Live gate** (dev, task 5) | A seeded matter tree: the preview counts match the seed; the delete completes; the SPE item is gone (in the recycle bin); the chunks are gone; the DR is kept with its `sprk_corerecordid`; R4 deactivates the grant within its schedule; the ledger row reads RootDeleted. A project delete leaves its ledger rows in place after task 1. |

The ArchTest goes up the enforcement ladder (CLAUDE.md §16): it is the strongest mechanism that holds the rule "only the operation deletes a core record" in code. The role change holds it in Dataverse.

---

## 8. Task breakdown

Model per the UAC standing rule: Opus pinned; effort as given.

| # | Task | Files | Acceptance (closed set) | Model / effort |
|---|---|---|---|---|
| 1 | **Metadata and roles** (D4, D6, D9 verify) | New `scripts/Set-ProjectAssignedAccessDeleteBehavior.ps1` (copy `Set-DocumentAnalysisCascadeSchema.ps1`), `sprk_Project.xml:532`; a role script modelled on `scripts/Set-SecureRecordOwnerRolePrivileges.ps1` | - `-Verify` PASS: project→assignedaccess Delete = RemoveLink, live and in XML.<br>- `sprk_policy_matter` = Cascade, live and in XML.<br>- Core User holds no `prvDelete` on the 4 core tables; the named role holds it at the agreed depth.<br>- A Core User delete of a matter (Web API) fails with a privilege error.<br>- Dev only; other environments get it at provisioning. | Opus / medium |
| 2 | **Walk extraction, planner, preview route** | `Services/Access/LineageDescendantWalk.cs`, `SecureChildReconciler.cs`, `Services/CoreRecords/CoreRecordDeletePlan.cs`, `…Planner.cs`, `Api/CoreRecords/CoreRecordDeleteEndpoints.cs` (GET), `Infrastructure/DI/CoreRecordsModule.cs` | - The `SecureChildReconciler` tests are unchanged and green.<br>- The preview matches §2.1.<br>- Every must-not-fire classification in §7 passes.<br>- An unknown table answers 400.<br>- No Read answers a uniform 404; Read without Delete answers 403.<br>- A failed read answers 503.<br>- The route guard passes. | Opus / high; 2 adversarial passes (security) |
| 3 | **Executor, job, delete and status routes** | `Services/CoreRecords/CoreRecordDeleteExecutor.cs`, `Services/Jobs/Handlers/CoreRecordDeleteJobHandler.cs`, the lease helper (+ `DocumentContainerRelocator.cs`), `DocumentCheckoutService.cs` (index and file-version steps), `IRelocatedFileIndexing.cs` / `RelocatedFileIndexing.cs`, `DataverseServiceClientImpl.cs` (`BulkDeleteAsync`), the endpoints (DELETE, status) | - The §3.2 order is proven by a call-order test.<br>- Every §3.3 outcome is reachable in a test.<br>- R is never deleted on a Partial.<br>- A re-run completes.<br>- 412/428/409/202 as in §2.2.<br>- Status is by owner comparison with a uniform 404.<br>- The publish-size delta is reported.<br>- **Depends on task 1** (D4). | Opus / high; 2 adversarial passes |
| 4 | **AI tool refusal + ArchTest guard** | `DataverseDeleteRecordHandler.cs`, `infra/dataverse/sprk_analysistool-dataverse-delete-record-row.json`, `tests/Spaarke.ArchTests/CoreRecordDeleteGuardTests.cs` (+ fixtures) | - §7 AI-tool cases.<br>- The guard's must-fire and must-not-fire controls.<br>- The ratchet baseline holds exactly the 2 named sites.<br>- The JSON row is deployed to dev and read back. | Opus / medium |
| 5 | **MDA command + live gate** | `src/client/webresources/js/sprk_core_record_delete.js`, the 4 core `RibbonDiff.xml` (hide the form, home-grid and subgrid Delete; add "Delete…"), its `.data.xml` | - The confirm text lists the `willDelete` / `willUnlink` / `willKeep` counts and any blockers (Delete disabled when `deletable` is false).<br>- Progress polls status.<br>- §7 live gate PASS on dev, recorded in a gate note.<br>- `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` gets a delete-path row. | Opus / medium |

**Adjacent defect to file, not in scope:** `DELETE /api/v1/documents/{id}` deletes the row only and leaves the SPE file (`DataverseDocumentsEndpoints.cs:306`, `:337`), which breaks D7. Task 3's changes to `DeleteCoreAsync` do not reach it, because it calls `dataverseService.DeleteDocumentAsync`.

---

## 9. Open questions

**For the owner:**

- **Q1. What is the named role?**
  - **(A, recommended)** A Dataverse role that holds `prvDelete` on the 4 core tables; Core User loses it.
    - Dataverse decides the record scope, and no new authorization code is needed.
    - A role holder can still delete through the raw Web API, which orphans rows rather than destroying them.
  - **(B)** A marker role checked by the BFF; no human role keeps `prvDelete` on core tables.
    - This closes the bypass.
    - It needs a role-membership check, and record scope must then be judged by some right other than Delete.
  - With either option: the role's name, and its depth (Parent:Child BU, as Core User has today, or Organization).
- **Q2. SPE files: recycle bin or permanent?** Graph's item DELETE goes to the container recycle bin (recoverable for a period; the exact window is unverified). This pairs with Dataverse's 30-day recycle bin for matter, project and document (inventory §3d). For relocated sources, round 94 G3 chose permanent (`session27-owner-decisions-and-research.md:1372`). Recommendation: recycle bin, so an admin restore can bring back both.
- **Q3. Does a secure record's dedicated, now-empty container stay?** "Never containers" was decided for shared containers (55 matters share 2). A secure root has its own container (I-13), and its just-in-time writer grants likely survive the delete (inventory §3c, unverified). Recommendation: keep it (no container deletes from this path), and file a follow-up for empty dedicated containers.
- **Q4. Should the delete itself be recorded as a Decision Record?** The DR would name the caller, R and the counts. Today the Dataverse audit names the BFF app user for the deletes, and the caller appears only in telemetry and in a 7-day status record. Audit retention is unset (#2027). DRs are ontology-owned, so a yes goes through ontology-r1.
- **Q5. Signals that a kept Decision Record cites (4 on dev):** delete them (as approved, "Signals deleted"), or keep them as the DR's evidence?

**For ebilling-r1:** Q6 is ANSWERED by the owner as ebilling D-120 (2026-10-19). See §10.

---

## 10. ebilling D-120 (owner, 2026-10-19, relayed by ebilling-r1) — card row superseded by D-146 (§12)

This settles Q6 and replaces the earlier "rate cards and budget revisions block" default.

| Table(s) | Lookup(s) to the core record | Behaviour |
|---|---|---|
| `sprk_ratecard`, with `sprk_ratecardrate` lines (parental, cascade) | `sprk_matter` / `sprk_project` / `sprk_workassignment` (`sprk_matter_ratecard_matter`, `sprk_project_ratecard_project`, `sprk_workassignment_ratecard_workassignment`) | **Deleted with the record.** This covers the card scoped to it and its lines. A card carries at most one core scope (task 078's writer). A card with no core scope is firm-wide and untouched. |
| `sprk_budget`, with `sprk_budgetbucket` (and its revisions); `sprk_spendsnapshot`; `sprk_spendsignal` | `sprk_matter` / `sprk_project` | **Kept and unlinked.** They do NOT block. |
| `sprk_invoicesubmission`, `sprk_invoiceapproval`, `sprk_invoicecorrection`, `sprk_externalidentifier` | `sprk_regardingmatter` / `sprk_regardingproject` / `sprk_regardingworkassignment` (no service-request lookup) | **Block**, alongside `sprk_invoice` and `sprk_billingevent`: finance and audit history. |
| Billing Signals and Decision Records | only via `sprk_regardinginvoice` / `sprk_regardinginvoiceline` | Covered: the invoice blocks first. |
| `sprk_ruleset`, `sprk_policy`/`sprk_policyversion` (firm-scoped here), `sprk_billingcodeset`/`sprk_billingcode`, `sprk_timekeeper`, `sprk_billingexport`, `sprk_ebillingsetting` | none | Untouched. |

**Before relying on any platform cascade:** read the live relationship metadata (`CascadeConfiguration.Delete`) for the `sprk_invoiceapproval` and rate-card core lookups. ebilling's docs do not state them. The delete operation never depends on a cascade it has not verified. The task 1 preview reads these relationships, so it does that check.

## 11. ebilling D-137: a record's own rule set (owner, 2026-10-20, relayed by ebilling-r1) — SUPERSEDED by D-146 (§12)

This replaces §10's "untouched" for a rule set **scoped to R** (its core lookup names R). Firm-scoped and business-unit-scoped sets stay untouched. UAC recommended this rule in its review of `projects/ebilling-r1/notes/rule-set-scope-design.md` @ d42ae3c40f, and the owner chose it.

| Case | Behaviour |
|---|---|
| R's own set, and **nothing that survives the delete cites** the set, its policies or its versions | **Deleted**, deepest first: `sprk_policyversion`, then `sprk_policy`, then `sprk_ruleset`. |
| R's own set, and **a surviving row cites it** (for example Signals or Decision Records on a child record's invoice that cite one of its versions; round 107 deletes only the rows filed under R itself) | **Kept and unlinked**: the core lookup is cleared, so under S2 the set applies to nothing. |

- The delete job writes **no Retire** status. The rule-set lifecycle keeps a single writer (ebilling's `RuleSetService`).
- "Cites" is decided from the live rows that survive the planned delete. A read that **fails** fails the preview (503), never "uncited".
- The preview lists both cases with counts. Task 1's planner reads the citing lookups from ebilling's schema at build time (see ebilling D-137); no column names are guessed here.


### 11.1 Once billing rows join the secure family (ebilling 081/086; review 2026-10-20)

ebilling 081 adds `sprk_ruleset` (and the edge `sprk_policy` → `sprk_ruleset`), and 086 adds `sprk_ratecard` and `sprk_ratecardrate`, to the secure lineage. After that, the §1.2 walk reaches rule sets, their policies and versions, and rate cards and their rates. The planner must not treat them as ordinary lineage children:

- **Own claim.** §1.4's own-claim rule reads `CoreAncestorLookups` + `IntermediateRootColumns`. Neither names a core lookup on `sprk_ruleset` or `sprk_ratecard`. Read as-is, R's own set would show "no own claim" and be deleted, skipping §11's citation check. Task 1 classifies these tables explicitly, per §12 (D-146): unlink-only, never Delete.
- **Billing policies.** §1.3's `sprk_policy` row ("platform cascade", via matter) describes matter policies only. A billing policy reached through `sprk_ruleset` follows its set's §11 outcome: deleted deepest first with the set, or kept when the set is kept.
- **Sets not scoped to R.** A firm-scoped or business-unit-scoped set reached through the walk is never deleted (§10).
- **Tests.** Task 1 adds one must-fire test per case (R's own uncited set is deleted; R's own cited set is unlinked; a firm-scoped set is untouched) and a must-not-fire control: the generic own-claim path never decides a billing row.

## 12. ebilling D-146: never delete billing configuration (owner, 2026-10-20, relayed by ebilling-r1)

Supersedes §11 (D-137) and §10's `sprk_ratecard` row (D-120). The owner re-answered Q10 in ebilling's session: core-record delete **never deletes** a record-scoped rule set or rate card.

| Case | Behaviour |
|---|---|
| R's own `sprk_ruleset` (its core lookup names R), cited or not | **Kept and unlinked** (the core lookup is cleared). Under S2 it then applies to nothing. Its `sprk_policy` and `sprk_policyversion` rows stay with it, untouched. |
| R's own `sprk_ratecard` (scoped 1:1 to R) | **Kept and unlinked**. Its `sprk_ratecardrate` lines stay with it, untouched. |
| Firm-scoped or business-unit-scoped sets and cards | Untouched (as before). |

- **No citation check is needed any more.** Nothing is deleted, so §11's "a failed citation read → 503" no longer applies to these tables. The citing-lookup handoff from ebilling 081 step 10 is not needed for delete.
- **Ownership after unlinking.** The delete job **never re-owns** these rows. A set or card that was Secure-owned (under a Secure R) stays Secure-owned after the unlink, because it holds that matter's terms and rates. It never moves to an ordinary owner. This is a deliberate held state, not the #2157 race residual. Billing admins reach such rows only through the Secure team or an admin. ebilling decides whether a retire or re-scope path is needed (ebilling's lifecycle writer, not the delete job).
- **§11.1 now reads:** once 081/086 put these tables in the secure lineage, the planner classifies `sprk_ruleset`, `sprk_policy` (billing), `sprk_policyversion`, `sprk_ratecard` and `sprk_ratecardrate` reached through R as **unlink-only (the set and card) / untouched (their children)**, never Delete, and never by the generic own-claim rule.
- **Tests (task 1).** Must-fire tests: R's own uncited set is unlinked and not deleted; R's own cited set is unlinked; R's own card is unlinked and its lines are untouched; a Secure R's set stays Secure-owned after the unlink. Must-not-fire tests: a firm-scoped set or card is untouched; no `Delete` request is ever issued for these five tables (a guard over the plan).

## 13. Amendment: document links are Restrict (task 188 slice D, PR #2186; owner round 111 item 3, 2026-10-21)

D7 hides a document from partners when it comes from an internal-only or non-Open communication. Under RemoveLink, deleting the communication or the parent .eml cleared the link, and the document became visible to partners. Both relationships are therefore **Restrict** on delete:
- `sprk_document_RelatedCommunication_sprk_communication` (`sprk_relatedcommunication`)
- `sprk_document_ParentDocument` (`sprk_parentdocument`)

They are enforced by `scripts/Set-DocumentLinkDeleteRestrict.ps1 -Verify`. These parts of the design change:
- **§3 ordering.** The executor deletes documents children-first: attachments before their parent .eml, deepest first. It deletes every document linked to a communication before that communication. A Restrict refusal during a run leaves a Partial (the parent is kept), never an orphan.
- **D1 and the §5 known-limit row** ("every relationship is RemoveLink"). These two relationships are the exception: a delete that bypasses the BFF is refused there instead of orphaning the row.
- **D8.** Deleting a communication's documents first is now required.
- **The single-document delete of a parent with child documents** answers 409 `document_has_child_documents` before its file is touched (`DocumentCheckoutService.DeleteCoreAsync`, the legacy and external routes). Children are not deleted with their parent; the product deletes one document at a time.
- **Communication "delete" through the BFF only deactivates the row**, so Restrict does not affect it. A hard delete outside the BFF is refused while documents link to it.
- **Archive cascade** stays RemoveLink. Long-term retention is not enabled; see the slice D issue.


## 14. ebilling EB-D-148: Procedures (O-17; agreed with ebilling-r1, 2026-10-21)

EB-D-148 re-plans billing configuration around Procedures (ebilling `notes/procedure-and-file-model.md` §5.2, O-17, D-146 at :719). It keeps §12's rule: **the delete job retires nothing**.
- The delete only unlinks a record-scoped rule set (RemoveLink).
- ebilling's no-plugin sweep (their task 117) later retires sets whose record is gone.

So the rule-set lifecycle keeps one writer, ebilling's. ebilling's "deleting a core record … retires its record-scoped rule set" means **through that sweep**, not through the delete job.

Additions to §1.3 (proposed by ebilling from model §5.2, accepted here):

| Table | Link to R | Disposition |
|---|---|---|
| `sprk_procedureassignment` | typed `sprk_matter` / `sprk_project` / `sprk_workassignment`, exactly one set | **Delete** with R, through the planner, counted in the preview. The three core lookups are RemoveLink (ebilling 080: Dataverse allows one parental relationship per table, and `sprk_excludedpolicy` holds it). An assignment row left with no core lookup (a delete outside the planner) never applies (ebilling 082) and is removed by ebilling's O-17 sweep. |
| `sprk_ruleset`, record-scoped (`sprk_scope` Record or Firm record) | typed lookup per core type, RemoveLink | **Kept, unlinked** (§12). Retired later by ebilling's sweep, never by the delete job. |
| `sprk_policy` / `sprk_policyversion` of a record-scoped set | none (via the set) | Follow the set; untouched by the delete. Record rules live only in the record-scoped set. |
| `sprk_storedfile`, invoice purposes | none (via `sprk_invoice`) | Covered: `sprk_invoice` **blocks** first. |
| `sprk_storedfile`, export and Procedure-source purposes; `sprk_procedure`; `sprk_procedureversion` | none | Unaffected. |
| `sprk_billingfirm` / `sprk_billingresponsible` | columns on R itself | Go with R; no other effect. |

**Task 1 tests**:
- must-fire: R's assignment rows are deleted by the planner and counted in the preview;
- must-not-fire: no `Delete` is issued for `sprk_ruleset`, `sprk_policy`, `sprk_policyversion`, `sprk_procedure` or `sprk_procedureversion`, and the plan writes no rule-set status.

---

## 15. Amendment: a row that an outside row references through a Restrict lookup is kept (ontology + ebilling 088 review, 2026-10-22)

**The gap.** ebilling 088 adds `sprk_storedfile` with **Restrict** lookups `sprk_sourcedocument` → `sprk_document` and `sprk_sourcecommunication` → `sprk_communication`. §14's "Covered: `sprk_invoice` blocks first" holds only when the invoice is under R. An invoice's source .eml can be refiled to another record N after submission (D-146/#2132, ebilling model §9.1). N has no invoice, so nothing blocks. The executor deletes N's documents and then its communications, the Restrict refuses, and the run ends `Partial`. #2132 rules that outcome out for a delete the preview approved.

**Rule (metadata-driven, not a table list).** The planner reads every relationship whose referenced table is a table the plan deletes, and whose `CascadeConfiguration.Delete` is `Restrict`. For each row the plan would delete:
- **Every referencing row is itself deleted earlier in the plan:** the row is deleted, children first. This is §13's ordering for `sprk_parentdocument` and `sprk_relatedcommunication`.
- **Any referencing row is outside the plan:** the row becomes **Keep**. The preview reports it in `willKeep` with the reason `referenced:{referencing table}`; for a Stored File it reads "kept: source of an invoice file". The row's lookups to R are RemoveLink (inventory §2; ontology confirmed on dev for every document and communication lookup to a matter, project or work assignment), so the platform clears them when R goes. No explicit PATCH is needed.
- **Keep propagates as §1.4 does.** A kept .eml keeps its attachments (no own claim, parent kept). A kept communication keeps the documents whose only claim is through it. The §13 Restrict links then never point at a deleted parent.

Why this rule rather than a Stored File special case:
- It covers 088's two lookups and any Restrict added later, with no new list to keep in step.
- It turns a run-time `Partial` into a preview-time decision, which is what #2132 requires.
- **A Block was considered and rejected.** It would make the user refile a source document before deleting an unrelated record. Keeping matches D5, which keeps rows something outside R still needs.

**At run time.** The §3.2 re-walk re-reads the Restrict references. A reference that appears after the preview gives `Partial(graph-changed)` with R kept, as for any other row that appears during a run. A Restrict refusal on a row the plan deletes is still a defined `Partial`, never an orphan.

**Known limit (K).** A kept row can lose its last core claim. For example, the .eml refiled to N alone has no core record after N is deleted. Its access stays as stored: an isolated row stays isolated (fail closed, §5), and the secure-child sweep reports it as parentless. Its SPE file stays in its current container. The preview counts these rows separately, as "kept with no remaining record".

**Ownership.** This is the executor and planner (§8 task 3), which uac-r2 owns. ebilling 117 (the O-17 sweep) needs no executor change. It must not delete a Stored File's source rows either, and it doesn't, because it retires rule sets only.

**Tests (task 3):**
- **Must fire:**
  - a document or communication on R referenced by a Stored File outside the plan is kept, never `Delete`d, and listed in `willKeep` with `referenced:sprk_storedfile`;
  - an .eml kept that way keeps its attachments;
  - a Restrict reference added between the preview and the run gives `Partial(graph-changed)`, R kept.
- **Must not fire:**
  - a document whose only referencers are in the plan is deleted;
  - a Stored File under an invoice on R still blocks the whole delete (§14).
- **Pure test over the metadata reader:** every Restrict relationship onto a table with disposition Delete is handled by in-plan ordering or by Keep, so none can reach the executor as a surprise.
