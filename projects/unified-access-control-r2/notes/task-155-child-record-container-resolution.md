# Task 155 (#1080): uploads to a child record resolve the right container

Residual of task 151, found by its adversarial verifier. Owner C10 part 2 (binding): every child of a secure
record is secure. This task makes a child's **content** follow its secure root; task 146 makes its
**ownership** follow it.

## The defect

`RecordContainerResolver.ResolveForRecordAsync` returned the non-secure fallback for every non-securable entity
without reading the record. That had two effects:

- **On the record-keyed upload routes** (two-argument overload, no fallback) the answer was always Unresolved.
  Every upload to a to-do, event or contact got the misleading 409 "No storage container is configured", even
  when the record's business unit had a container.
- **On the Office save path** (`OfficeService.ResolveContainerAsync`) Unresolved falls through to the configured
  default container. A to-do under a secure project therefore put that project's content in a shared container.
  This is the #1038 class, reached through the child instead of the root.

## What changed

`src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/RecordContainerResolver.cs` now classifies the entity
into one of three kinds and handles each one.

| Kind | Examples | Behaviour |
|---|---|---|
| Child with known root links | `sprk_todo`, `sprk_event`, `sprk_invoice` | One record read returns `owningbusinessunit` plus the root-link and intermediate columns. If the child is filed under another child, the request is refused with `container_ancestor_unverifiable`. Otherwise every linked root that can be secure gets one read. A secure root returns its own container. If that root has no container, the request is refused with `secure_record_container_missing`. Two secure roots refuse with `container_ancestor_ambiguous`. When no root is secure, the explicit fallback is used if there is one, else the record's own business-unit container. |
| Child with unknown links | `sprk_document`, `sprk_communication`, `sprk_analysis` | Refused with `container_ancestor_unresolved` and no read (escalation trigger 1). None of these reaches a caller of this resolver today. |
| No ancestor concept, not securable | `contact`, `account` | With an explicit fallback: unchanged, no reads. With the two-argument overload: the record's own `owningbusinessunit` container, as the overload's documentation always promised. |
| Securable, not a child | project, matter, work assignment | Unchanged. |

A child that is itself secure, such as an invoice with `sprk_issecure = true`, keeps its own container. Its root
is never consulted.

**Root-read failures:**

- A root that does not exist: `container_ancestor_unresolved`, 409.
- Any other read fault: `container_ancestor_unresolved`, 503 (retryable).
- A NULL flag on the root: treated as non-secure and logged, the same as on the record path. Live dev has 9
  projects and 18 matters with a NULL `sprk_issecure`.

**Read cost:**

- The child's links come back on its one record read.
- Each linked root that can be secure costs one read, and in practice there is one.
- The business unit is never read when a secure root decides.
- `sprk_servicerequest` cannot carry `sprk_issecure` in dev, so it is never read.
- `SecurableEntityRegistry` now keeps the catalog for the lifetime of its scope (it is registered Scoped). That
  makes the root's securability question free, even with Redis down. Without it, every child upload would pay a
  second full-org metadata round trip, which is the cost task 151's review removed.

## Live metadata verified (read-only, spaarkedev1, 2026-10-01)

> **Superseded by "Round f3" below.** The table here was hand-picked and incomplete: it missed
> `sprk_invoice.sprk_regardingagreement` and the polymorphic regarding pair, and it treated
> `sprk_regardingservicerequest` as a root link. The f3 section holds the full column-by-column sweep.

**Child link columns:**

| Entity | Root links | Intermediate (child) regarding columns |
|---|---|---|
| `sprk_todo` | `sprk_regarding{project,matter,workassignment,servicerequest}` | `sprk_regarding{analysis,communication,document,event,invoice}` |
| `sprk_event` | `sprk_regarding{project,matter,workassignment,servicerequest}` | `sprk_regarding{analysis,communication,event,invoice}` |
| `sprk_invoice` | typed `sprk_matter`, `sprk_project`; no `sprk_regarding{core}` columns | none |

**Which entities carry `sprk_issecure`:**

- Present on: project, matter, work assignment, **invoice**.
- Absent from: service request, document, communication, analysis, agreement, budget, report card, organization.

**Data:**

- 1 secure project, with its own container.
- 0 to-dos regard it.
- 0 secure invoices.
- 13 of 50 to-dos and 1 of 73 events are filed under another child record. The holding position below refuses
  uploads to these records.

## 🔔 Escalation trigger 2 FIRED: the root stamp can be stale and send bytes to the wrong root

When a to-do regards a project, `sprk_regardingproject` is the link the user chose. When it regards a
communication, event, invoice, document or analysis, its `sprk_regarding{core}` value is a denormalized copy of
that record's root. `CoreAncestorResolver` writes the copy once and nothing refreshes it:

1. **Re-filing the intermediate record does not re-stamp its children.** Derivation is one hop by design (ADR-034,
   `CoreAncestorResolver` remarks). Suppose a to-do sits under communication C, and C is re-filed from ordinary
   matter A to SECURE matter B, deliberately or through `IncomingAssociationResolver`, which only ever adds. The
   to-do still reads "root A, non-secure", so a stamp-trusting resolver sends its bytes to a shared container.
   That is the leak this task exists to close.
2. **Clearing the intermediate lookup on the form leaves the stamp in place.** This is task 051 finding F-051-6,
   still open: the copy points at the old root.

So the resolver does not trust the copy, and this part was stopped as the trigger requires. **Holding position
(fail-closed, implemented):** a child filed under another child is refused with `container_ancestor_unverifiable`
(409). The detail tells the user to file the record directly against its project, matter or work assignment. No
bytes move.

What the holding position changes:

- On the OBO routes, these records answered 409 before this task, so nothing regresses there.
- On the Office save path they used to fall through to the shared default container, which was a potential leak.
  They are now refused.

**Owner decision needed.** The current code isolates the choice to one branch,
`ResolveSecureAncestorAsync`'s intermediate check, so whichever option is chosen replaces only that branch.

- **(a) Read the intermediate live.** This costs one more read per upload and amends this task's read budget.
  The intermediate's own root can itself be a copy, so the walk must be bounded or the intermediate must be
  required to be direct.
- **(b) Re-stamp the children when the intermediate is re-filed.** This is a server-side cascade in the
  re-parent paths. It also fixes the access over-grant from the same stale copy (task 051 §1), and it closes
  F-051-6 only if form clears go through the BFF, as ADR-002 now requires.
- **(c) Accept the refusal.** Users file uploads directly against the root.

Recommendation: (b), because the same stale copy is also an access over-grant today. Keep (c) as the holding
position until (b) lands.

## Escalation trigger 1 did NOT fire for the routes

Every child type the record-keyed routes accept (`todo`, `event`, `invoice`) has a verified root link. A guard
test (`EveryRouteReachableChildType_HasKnownRootLinks`) fails if a new route type is added without one. Contact
is not a child (`CoreAncestorResolver`: unclassified), so it takes its own business-unit container.

## Behaviour changes outside the routes (deliberate)

- **`OfficeService.ResolveContainerAsync`** benefits automatically:
  - a to-do, event or invoice under a secure root now stores its content in the root's container, or is refused;
  - a contact target now uses its own business-unit container instead of the configured default.
- **`CommunicationContainerResolver`** passes only securable regardings. Live, that includes `sprk_invoice`, so
  an email that regards an invoice under a secure matter now routes to the matter's container instead of the
  archive. Records with no secure root behave as before; all of its tests are green and unmodified.

## Tests

- `ChildRecordContainerResolutionTests` (new): 29 cases covering every acceptance criterion, the cost, and the
  route-type guard.
- `RecordKeyedUploadRouteChildRecordTests` (new): drives the real mapped `PUT /api/obo/records/{entity}/{id}/files/…`
  route through filter, handler, resolver and the drive the bytes reach. This closes the task 151 verifier's
  test-shape gap. `SpeFileStore.UploadSmallAsUserAsync` (the overload that takes a conflict behaviour) became
  `virtual` for this, following the facade's existing double idiom. There is no behaviour change.
- `SecurableEntityRegistryTests`: one new test pins the scope memo, and one arrangement now models a row for the
  newly read invoice. Its assertion is unchanged.
- `RecordContainerResolverTests`: two tests pinned the defect itself.
  - The zero-read test moved from `sprk_invoice` (now a child) to `contact`, with its assertions unchanged.
  - The "no fallback leads to Unresolved" leg was replaced by a test of the documented business-unit derivation.
- Office fixtures (`OfficeEndpointsContractTests.cs`): the two world doubles now answer the resolver's
  non-securable `owningbusinessunit` read with an empty row (§F.2 Fixture-Config-FIRST), so saves still fall
  through to the configured default as before. All 411 Office tests are green, the same as the base.

**Mutation proof** (each change seeded, tests watched failing, change reverted):

| Mutation | Tests that failed |
|---|---|
| M1: secure roots ignored | 12, including both route tests |
| M2: intermediate refusal removed | 8 |
| M3: ambiguity removed | 1 |
| M4: unreadable root read as non-secure | 1 |
| M4b: missing root read as non-secure | 1 |
| M5: fail-closed skipped | 2, including the route test |
| M6: unknown child type allowed | 1 |
| M7: scope memo removed | 1 |
| M8: business-unit derivation for contact removed | 3 |
| M9: non-securable root read anyway | 1 |
| M10: `sprk_event` links removed | 6, including the guard |

## Manual live gates (NOT run: no live writes in this session)

1. **Deploy the BFF to dev.** This is a deploy, owned by the main session.
2. **Upload to a to-do under the secure test project.**
   - As an existing non-admin test user who has access to the secure test project (`65a3fab2-77a5-f111-aaad-70a8a590c51c`,
     container `b!HBRbokLXnUGzaDLSTdNFvM5RFHtaaUZCi0Jm-xs-hDQV_6QuLuKmR4jrMdC6UgMm`), create a to-do with
     `sprk_regardingproject` set to it. This is a live write. None exists today.
   - Upload a file through the to-do's document control.
   - Confirm read-only that the item is in that container:
     `GET https://graph.microsoft.com/v1.0/drives/b!HBRbokLXnUGzaDLSTdNFvM5RFHtaaUZCi0Jm-xs-hDQV_6QuLuKmR4jrMdC6UgMm/root/children`
3. **Upload to a to-do under an ordinary project.** Confirm the item lands in the to-do's `owningbusinessunit`
   container:
   `SELECT owningbusinessunit FROM sprk_todo WHERE sprk_todoid = '<id>'`, then
   `SELECT sprk_containerid FROM businessunit WHERE businessunitid = '<bu>'`.
4. **Confirm the holding position.** An upload to one of the 13 to-dos filed under another record answers 409
   `container_ancestor_unverifiable`, and nothing lands in any container.

## Round f1: adversarial-verifier findings (branch `task/uac-r2-155-f1`)

- **AC4, unreadable ancestor LINK.** A child's own row, when unreadable (any failure except not-found), now refuses
  with `container_ancestor_unresolved` **503**, the same refusal as an unreadable root. Before this round it was a
  raw fault, which the record-keyed routes rendered as a generic 500 "Upload failed".
- **Fail-closed branches the verifier proved untested** (an unknown root entity type; a null root row) now have
  tests. The verifier's exact seeds (`continue`) turn them red.
- **Inbound communication classification.** `IncomingCommunicationProcessor.IsPermanentContainerRefusal` skips
  three refusals as permanent: `container_ancestor_ambiguous`, `container_ancestor_unverifiable`, and
  `container_ancestor_unresolved` at 409. `container_ancestor_unresolved` at 503 stays transient. Before this
  round, the permanent codes that an email regarding an INVOICE can raise fell into the retry loop.
- **Communication → invoice → secure matter routing** is now tested end to end. The test runs the real adapter
  over the real resolver.
- **Route-level copy.** A test now covers the documented 409 "No storage container is configured" response.
- **CoreAncestorResolver remark.** The stale remark that `sprk_todo` lacks `sprk_regardingservicerequest` has
  been corrected. The example is now `sprk_invoice`.
- **Still open:**
  - The owner's choice for escalation trigger 2, option (a), (b) or (c).
  - The AC7 live gate (above).
  - The taxonomy observation for agreement, budget, report card and organization, which belongs to tasks 146/147.
    **Superseded in f2:** the container REFUSAL for agreement / budget / report card is now added here (below);
    only the access-taxonomy half remains with 146/147.
  - The SPE-membership 403 observation.

## Round f2: second adversarial-verifier findings (branch `task/uac-r2-155-f2`)

- **BLOCKING, closed: a to-do / event filed under an agreement, budget or report card.** These three belong to a
  matter or project (live spaarkedev1, read-only, 2026-10-01: `sprk_agreement` and `sprk_reportcard` via
  `sprk_regardingmatter` / `sprk_regardingproject`; `sprk_budget` via typed `sprk_matter` / `sprk_project`), but
  `CoreAncestorResolver` does not classify them, so the child carries **no** `sprk_regarding{core}` stamp. The
  resolver read "no root" and returned the business-unit container — shared, even under a SECURE matter. On the
  OBO routes that was a new byte path (before task 155 it was a 409). `sprk_regardingagreement`,
  `sprk_regardingbudget` and `sprk_regardingreportcard` are now in `ChildAncestorLinks.IntermediateColumns` for
  both `sprk_todo` and `sprk_event`, so they refuse with `container_ancestor_unverifiable` (409) exactly like a child
  under another child. Live dev has one record in this shape (to-do `a01477e8-…` → report card `9d1477e8-…` →
  matter `b68299c6-…`, not secure today); 0 events.
- **Every other regarding column checked live.** `sprk_todo`: agreement, analysis, budget, communication, contact,
  document, event, invoice, matter, organization, project, reportcard, servicerequest, workassignment.
  `sprk_event`: the same minus document, plus account. The remaining non-root targets — `contact`,
  `sprk_organization`, `account` — are PARTY records with no project / matter / work-assignment lookup (verified
  by describe). `contact` and `sprk_organization` carry an `sprk_invoice` lookup, but that is a party → invoice
  reference, not ownership (a person is not under an invoice), so they are not ancestors and are not refused.
  Flagged for the owner in case that reading is wrong.
- **The guard is now proven to bite.** The test double returned the whole row whatever was requested, so dropping
  the intermediate columns from the read left 101 tests green. `ChildRecordContainerResolutionTests`' double now
  returns ONLY the requested columns, as Dataverse does, and a new theory pins the read's columns against a LITERAL
  live-verified list (to-do, event, invoice). The verifier's exact seed (`AllColumns => RootLinks...`) now turns
  23 tests red.
- **Typed 503 on a Dataverse timeout.** The catch filters excluded every `OperationCanceledException`, so an HTTP
  timeout (`TaskCanceledException`, caller token live) escaped as a generic 500. Both filters now exclude only a
  CALLER cancellation (`ex is OperationCanceledException && ct.IsCancellationRequested`); a timeout is an unreadable
  row and gets `container_ancestor_unresolved` 503. Caller cancellation still propagates (pinned).
- **Office-level pins** (`OfficeSaveNoTargetContainerContractTests` §4, over the real mapped `POST /api/office/save`):
  a to-do with no secure root lands in the to-do's OWN business-unit container (not the tenant default, not the
  acting user's business unit); a to-do filed under a report card is refused (`container_ancestor_unverifiable`)
  with nothing uploaded; a target that does not exist is refused (`container_record_not_found`) with nothing
  uploaded. The Office save renders every resolver refusal in its pre-existing 400 "Save failed: {code}: …" shape
  (`OfficeService.SaveAsync`'s catch), so the code, not the status, identifies the refusal there.

**Mutation proof (f2):**

| Mutation | Tests that failed |
|---|---|
| S1 (verifier's seed): `AllColumns` drops `IntermediateColumns` | 23 |
| S2: the three new columns removed from to-do / event | 14 |
| S3: invoice's `sprk_project` link removed | 2 (column pin + two-secure-roots) |
| S4: OCE filter back to "any OperationCanceledException" | 1 (timeout → 503) |
| S4b: OCE filter never excludes | 1 (caller cancellation propagates) |
| S5a: no business-unit derivation for children | 1 (Office to-do → BU container) |
| S5b: = S2, Office suite | 1 (Office report card refused) |
| S5c: not-found read as an empty row | 1 (Office missing target refused) |

**Still open after f2:** escalation trigger 2's owner choice (a)/(b)/(c) — which now also covers the agreement /
budget / report card shape (option (b) would need `CoreAncestorResolver` to classify those three as children so
they get stamped; option (a) reads the intermediate's root live); the AC7 live gate (now also: an upload to a to-do
filed under a report card must answer 409 `container_ancestor_unverifiable`); the read-budget note (a child
carrying two root stamps costs two root reads — needed to detect the two-secure-roots ambiguity, documented as
"one in practice"); the SPE-membership 403 observation.

## Round f3: third adversarial-verifier findings (branch `task/uac-r2-155-f3`)

The f2 verifier judged NEEDS-FIXES. Every item below is closed in code; the owner decisions that remain are listed
at the end.

### Item 1 (BLOCKING fail-open): an invoice regarding an agreement

`sprk_invoice` has a live lookup `sprk_regardingagreement → sprk_agreement`, and an agreement belongs to a matter or
project (its own `sprk_regardingmatter` / `sprk_regardingproject`). The f2 table gave the invoice **no** intermediate
column. An invoice regarding an agreement of a SECURE matter therefore read "no secure root":

- with no typed `sprk_matter` / `sprk_project`, or a typed link to a different non-secure root, it resolved the
  shared business-unit container;
- on the communication path, it resolved the shared archive container.

**Fix:** `sprk_regardingagreement` is an intermediate on the invoice, so the invoice takes the same held path as every
other child-under-a-child: `container_ancestor_unverifiable` (409). On the inbound path that refusal is classified as
permanent. Options (a) and (b) of escalation trigger 2 are NOT implemented.

**Live:** 0 of 10 invoices set it today.

### Item 2: the systematic live sweep

**Method.** Read-only, against spaarkedev1, 2026-10-01:

- `GET EntityDefinitions(LogicalName='x')/Attributes/Microsoft.Dynamics.CRM.LookupAttributeMetadata?$select=LogicalName,Targets,AttributeType`
  for every Lookup, Customer and Owner column;
- `GET EntityDefinitions(LogicalName='x')/Attributes` filtered client-side, to find the polymorphic pair columns.

**Which entities were swept.** Every entity in `ChildAncestorLinks`, plus every entity the resolver can be asked
about:

- the OBO record-keyed routes (`EntityAccessFilter.EntitySetByType`): contact, project, matter, work assignment,
  invoice, event, to-do;
- the Office save path (`DocumentAssociationMap`): the same set;
- Compose: `sprk_matter`;
- the external project endpoint: `sprk_project`;
- the communication adapter: the securable regardings (project, matter, work assignment, invoice).

**How each target was classified.** Every target was then swept for its OWN lookups:

- **root**: project, matter, work assignment;
- **intermediate**: a record that has, or can have, a root;
- **party**: a person or organization, which is not ownership;
- **reference / principal**: not ownership, never followed.

Total: 188 lookup columns across 7 entities, plus 24 target entities swept for their own lookups: the 18 Spaarke and
OOB record or reference targets, and the 6 principal and system targets (systemuser, team, businessunit,
transactioncurrency, externalparty, sla).

| Entity (lookups) | Root links (READ, flag + container checked) | Intermediate (HELD: `container_ancestor_unverifiable`) | Party (not ownership, not read) | Reference / principal / system (not read) | Polymorphic pair |
|---|---|---|---|---|---|
| `sprk_todo` (25) | `sprk_regardingproject`, `sprk_regardingmatter`, `sprk_regardingworkassignment` | `sprk_regarding{analysis, communication, document, event, invoice, agreement, budget, reportcard, servicerequest}` | `sprk_assignedto`, `sprk_regardingcontact` → contact; `sprk_regardingorganization` → sprk_organization | `sprk_regardingrecordtype` (the pair's type), `sprk_relatedrecordtype` → sprk_recordtype_ref (a type with no id column, so it names no record); createdby / createdonbehalfby / modifiedby / modifiedonbehalfby / owninguser → systemuser; ownerid → systemuser\|team; owningteam → team; owningbusinessunit → businessunit (the BU-container source) | YES — `sprk_regardingrecordid` (String) + `sprk_regardingrecordtype` |
| `sprk_event` (42) | the same three | `sprk_regarding{analysis, communication, event, invoice, agreement, budget, reportcard, servicerequest}` | `sprk_approvedby`, `sprk_assignedattorney1/2`, `sprk_assignedparalegal1/2`, `sprk_assignedto`, `sprk_assignedto1/2`, `sprk_assignedtoexternal/internal`, `sprk_completedby`, `sprk_reassignedby`, `sprk_rescheduledby`, `sprk_todoassigned`, `sprk_regardingcontact` → contact; `sprk_assignedlawfirm1/2`, `sprk_regardingorganization` → sprk_organization; `sprk_regardingaccount` → account | `sprk_ai_search_index` → sprk_aisearchindex; `sprk_eventset` → sprk_eventset; `sprk_eventtype_ref`; `sprk_regardingrecordtype`; the 8 system columns | YES (it also has a `sprk_regardingrecordtypelogicalname` String, NULL on every live row, so it is not used) |
| `sprk_invoice` (22) | typed `sprk_project`, `sprk_matter` | **`sprk_regardingagreement`** → sprk_agreement (item 1) | `sprk_assignedto1/2`, `sprk_assignedtoattorney1/2`, `sprk_assignedtoparalegal1/2` → contact; `sprk_vendororg` → sprk_organization | `sprk_ai_search_index`; `sprk_regardingrecordtype`; `sprk_securitybu` → businessunit (a security BU, not a root); `transactioncurrencyid`; the 8 system columns | YES |
| `sprk_workassignment` (28) | `sprk_regardingproject`, `sprk_regardingmatter` | `sprk_regardingcommunication`, `sprk_regardingevent`, `sprk_regardinginvoice` | `sprk_assignedattorney1/2`, `sprk_assignedlawfirmattorney1`, `sprk_assignedparalegal1/2`, `sprk_assignedto`, `sprk_assignedtoexternal/internal` → contact; `sprk_assignedlawfirm1/2` → sprk_organization | `sprk_ai_search_index`; `sprk_mattertype` → sprk_mattertype_ref; `sprk_practicearea` → sprk_practicearea_ref; `sprk_regardingrecordtype`; `sprk_securitybu`; the 8 system columns | YES |
| `sprk_project` (24) | — | — | `sprk_assignedattorney1/2`, `sprk_assignedparalegal1/2`, `sprk_assignedtoexternal/internal` → contact; `sprk_assignedlawfirm1/2` → sprk_organization; `sprk_externalaccount` → account | `sprk_ai_search_index`; `sprk_mattertype`; `sprk_practicearea`; `sprk_projecttype_ref`; `sprk_regardingrecordtype`; `sprk_securitybu`; `transactioncurrencyid`; the 8 system columns | YES (0 live projects set it) |
| `sprk_matter` (24) | — | — | as project | as project, plus `sprk_chartdefinition` → sprk_chartdefinition; `sprk_regardingrecordtype` exists but there is **no** `sprk_regardingrecordid` column, so the row can name no record | NO |
| `contact` (23) | — | **`sprk_invoice`** → sprk_invoice (0 live contacts set it) | `accountid`, `msa_managingpartnerid` → account; `masterid`, `parentcontactid` → contact; `parentcustomerid` → account\|contact; `sprk_organization` → sprk_organization | `sprk_systemuser`, `preferredsystemuserid` → systemuser; `createdbyexternalparty` / `modifiedbyexternalparty` → externalparty; `slaid` / `slainvokedid` → sla; `sprk_contacttype` → sprk_contacttype_ref; `transactioncurrencyid`; the 8 system columns | NO (`contact_regardingrecordnumber` is a display string only) |

**How each target entity was classified, from its own lookups (live):**

| Target | Its own lookups that matter | Kind |
|---|---|---|
| sprk_project, sprk_matter, sprk_workassignment | carry `sprk_issecure` and `sprk_containerid` | **Root** |
| sprk_agreement | `sprk_regardingmatter`, `sprk_regardingproject`, `sprk_regardingdocument` | Intermediate |
| sprk_budget | typed `sprk_matter`, `sprk_project` | Intermediate |
| sprk_reportcard | `sprk_regardingmatter`, `sprk_regardingproject` | Intermediate |
| sprk_servicerequest | `sprk_regarding{matter, project, workassignment, communication, invoice, todo, …}`; carries **no** `sprk_issecure` | Intermediate (**reclassified in f3**, see below) |
| sprk_analysis, sprk_communication, sprk_document, sprk_event, sprk_invoice, sprk_todo | the child taxonomy: a root stamp written once and never refreshed | Intermediate (trigger 2) |
| contact, account, sprk_organization | no lookup to a root (contact and organization carry `sprk_invoice`, a reference) | Party |
| systemuser, businessunit, transactioncurrency, externalparty, sla, sprk_recordtype_ref, sprk_eventtype_ref, sprk_mattertype_ref, sprk_practicearea_ref, sprk_projecttype_ref, sprk_contacttype_ref, sprk_eventset, sprk_aisearchindex, sprk_chartdefinition | no lookup to a root or an intermediate | Reference / principal |
| team | `regardingobjectid` CAN target sprk_matter / sprk_project / sprk_document / sprk_event / sprk_invoice. That column belongs to ACCESS teams. An access team cannot own a record (only owner teams can), so `ownerid` / `owningteam` never names a record-regarding team. Live: 0 teams carry a `regardingobjectid`. | Principal |

**What the sweep changed in code** (`RecordContainerResolver.ChildAncestorLinks`). The table is now one literal
`column → target` list per entity. Root links and intermediate columns are derived from it through a single
`KindByEntity` table.

- **`sprk_invoice`**: + `sprk_regardingagreement` (intermediate); + the pair.
- **`sprk_todo` / `sprk_event`**:
  - `sprk_regardingservicerequest` moved from root link to **intermediate**. A service request cannot carry
    `sprk_issecure`, but it hangs off a matter, project or work assignment. Because it is a CORE record,
    `CoreAncestorResolver` stamps nothing above it on the child, so a to-do under a service request of a SECURE
    matter resolved the business-unit container. This is the same fail-open as the f2 agreement shape, and the f2
    test pinned it as correct.
  - Live: 0 to-dos and 0 events regard a service request, and there are 0 service requests.
  - Both entities also gain the pair.
- **`sprk_workassignment`** (NEW entry). Root links `sprk_regardingproject` / `sprk_regardingmatter`; intermediates
  `sprk_regarding{communication, event, invoice}`; and the pair.
  - Its OWN `sprk_issecure` still decides first: a secure work assignment keeps its own container.
  - A NON-secure work assignment filed regarding a SECURE matter now resolves the matter's container. This is the
    same rule as a non-secure invoice under a secure matter.
  - Live: 9 of 22 work assignments regard a matter or project, none secure, so the outcome is unchanged at +1 read.
    1 regards an invoice (`b10b7dab-…`) and is now held.
- **`sprk_project`** (NEW entry): the pair only. Live: 0 projects carry it.
- **`contact`** (NEW entry): `sprk_invoice` as an intermediate. Live: 0 contacts set it.
  - f2 judged this a party → invoice reference and flagged the reading for the owner.
  - The f3 brief's rule ("any column whose target can be or can hang off a root MUST be read and checked, or
    refused") makes it read-and-held.
  - Consequence: the four-argument overload now READS a contact (it used to read nothing). No production caller
    passes an explicit fallback, so this costs nothing live. The zero-read pins moved from contact to `account`.
- **`sprk_matter`**: no entry. Its row can name no record.

`ChildRecordRead_RequestsEveryLinkAndIntermediateColumn` now pins the swept table as data:

- It carries the LITERAL `column>target` snapshot for all 7 entities, plus the pair flag.
- It derives the expected read from that snapshot by target kind, and asserts the one record read equals it EXACTLY.
- It fails if a re-sweep brings a target nobody has classified.

The f2 wording "verified on live" was wrong: it described a hand-picked list that missed
`sprk_invoice.sprk_regardingagreement`. The test now names its source (the f3 sweep) and its method.

### Item 3: the polymorphic regarding pair (`sprk_regardingrecordid` + `sprk_regardingrecordtype`)

**The pair.**

- `sprk_regardingrecordid` is a STRING with no referential integrity. Live rows store it in mixed case.
- `sprk_regardingrecordtype` is a lookup to `sprk_recordtype_ref`, whose `sprk_recordlogicalname` names the entity.

**What changed.** The pair is now read for every entity in the table that has it: to-do, event, invoice, work
assignment and project. The rules, in order (`RecordContainerResolver.ResolvePolymorphicRegardingAsync`):

1. **No record id**: the pair names no record and adds nothing. A type with no id, which is 21 live events and 1
   live to-do, names no record either.
2. **An id that is not a GUID**: refused, `container_ancestor_unresolved` 409.
3. **The id equals a typed root link's id on the same row**: the pair names that same record. This is agreement by
   identity: nothing new, and no read.
4. **A typed root link is set and the pair names a DIFFERENT record**: refused as ambiguous,
   `container_ancestor_ambiguous` 409. Neither link is picked, and nothing more is read.
5. **The pair is the only thing naming a record**: its type decides. The type is read from `sprk_recordtype_ref`,
   which costs one read.
   - A **root** joins the same list as the typed roots, so it is flag-checked, container-checked and counted in the
     two-secure-roots ambiguity check.
   - A root that does **not exist** refuses, `container_ancestor_unresolved` 409.
   - An **intermediate** takes the held path: `container_ancestor_unverifiable`.
   - A **party** (contact, account, organization) adds nothing, exactly like the typed party columns.
   - A missing type (`container_ancestor_unresolved` 409), an unreadable type row (503), or a type the table does
     not classify (409) is refused.

**Live consequences:**

- **The verifier's 10 pair-only events** all name one of 4 matter ids (`C4EF17ED-…`, `050995F1-…`, `97962160-…`,
  `D57BC02F-…`). None of the 4 exists any more: the matters were deleted, and the string id stayed behind. These
  events now refuse uploads with `container_ancestor_unresolved` 409. Before f3 they resolved the business-unit
  container. Cleaning the dangling ids is a data fix, not done here because no live writes were allowed.
- **To-do `4ff4dc1f-…`** has a pair id with no type and no typed link. It is now refused with 409.
- **Event `a6d00177-…`** has typed `sprk_regardingproject = b12496d1` and a pair with the same id but type "Matter".
  By rule 3 it resolves through its typed project, so it is unchanged. The typed lookup is referentially enforced, so
  a mislabelled type cannot hide a second record. Owner flag 2 below covers this.
- **Typed links that disagree with the pair by id:** 0 live.

**Why rule 3 does not read the type.** A type read on rule 3 would add one Dataverse read to the commonest live shape
(the regarding builders stamp the typed column and the pair together). The only thing it could detect is a
mislabelled type, and the content goes to the right root either way. Item 5 allows extra reads only where they make
the answer fail closed.

### Item 4: comment vs code on an undefined `EntitySecurability`

**Before.** The comment said an undefined value "takes the secure path". The code computed
`isSecurable = securability == Securable`, so for an entity with no links an undefined value took the NOT-securable
branch: zero reads with a fallback, else the business-unit container.

**Now.** Any value other than `NotSecurable` / `Securable` (`NotAnEntity` was already refused) is refused with
`securable_entities_unknown` 409 before any read. That is the existing code for "securability could not be
determined". The comment states what the code does.

`UndefinedClassification_IsRefused` covers contact, account, to-do and project on both overloads, and asserts that no
read happens.

### Item 5: the read budget after f3 (two-argument overload; the four-argument overload skips the BU read)

| Shape | Before f3 | After f3 |
|---|---|---|
| To-do / event / invoice, no link set | 2 (row + BU) | 2 |
| Typed root, plus an agreeing pair (the commonest live shape), root not secure | 3 (row + root + BU) | **3**: the pair costs nothing |
| The same, root secure | 2 | 2 |
| Two typed roots | 2 root reads (needed for the ambiguity check) | unchanged |
| Pair is the only link, naming a root | 2 (row + BU: **the leak**) | 4 (row + type + root + BU); 3 if the root is secure |
| Pair is the only link, naming a party | 2 | 3 (row + type + BU) |
| Pair is the only link, naming an intermediate | 2 (**leak**) | 2 (row + type; refused) |
| Filed under a typed intermediate (now incl. service request, invoice → agreement, contact → invoice) | 1, or 2 (**leak**) for the shapes new in f3 | 1 (refused) |
| Work assignment, secure | 1 | 1 |
| Work assignment, not secure, regarding a root (+ agreeing pair) | 2 (row + BU) | 3 (row + root + BU); 2 if the root is secure |
| Project / matter, not secure, no pair | 2 | 2 |
| Contact (two-argument) | 2 | 2 |
| Contact (four-argument; no production caller) | 0 | 1 |
| Undefined classification | 0 to 2 | 0 (refused) |

Each new read is one that decides whether a secure root is involved. No extra metadata round trip is added: the root
classification still comes from the scope-memoized catalog.

### Unchanged contract, deliberately

- A read fault on the row of a ROOT or a CONTACT still propagates raw (fail-closed: nothing below runs). Only a CHILD
  row fault becomes the typed 503, as in f1.
- Giving the new entries the 503 wrap would change the pinned task 075 contract (`RecordReadFailure_Propagates`) for
  no isolation gain.

### Tests (f3)

**`ChildRecordContainerResolutionTests`:**

- The pin theory was rewritten as the live-sweep snapshot (7 rows).
- The trigger-2 theory gained 5 rows: service request ×2, and work assignment → communication / event / invoice.
- The no-stamp theory gained 4 rows: service request ×2, invoice → agreement, contact → invoice.
- New item 1 tests: a 2-row theory and the communication-path test.
- New item 3 tests: 11 tests and theories. Pair → secure matter, pair → non-secure matter (with the cost), dangling
  pair, pair → intermediate ×2, pair → party, id without a type, type without an id, unparseable id, unclassifiable
  or unreadable type ×4, agreement by identity (with the cost), disagreement.
- New work-assignment tests ×3, and a project-pair test.
- New item 4 theory ×4.
- `Todo_UnderAServiceRequest_DoesNotReadIt…` pinned the f3 fail-open as correct. It was rewritten to pin the
  surviving branch (a root type the org cannot make secure is never read), using a world where the work assignment
  is not securable.

**Other suites:**

- `RecordKeyedUploadRouteChildRecordTests`: two new REAL-route cases. A to-do linked to the secure project ONLY by the
  pair stores in the project's container. An invoice regarding an agreement is refused with 409 and nothing is
  uploaded.
- `RecordContainerResolverTests`: the two zero-read pins moved from contact to account. The assertions are unchanged.
- `SecurableEntityRegistryTests`: the cache-down cost theory's double now answers the contact read (§F.2). The
  assertion is unchanged.

**Counts:**

| Suite | Result |
|---|---|
| `ChildRecordContainerResolutionTests` | 102 cases |
| Affected suites (resolver, route, registry, Office no-target, communication adapter, document-list, acting-user, SPE upload paths) | 255 / 255 |
| Full BFF unit suite | Passed 13303, Failed 0, Skipped 54 (Total 13357) |
| `Spaarke.ArchTests` | 337 / 337 |

### Mutation proof (f3)

**How the seeds were run.** Each seed was applied to `RecordContainerResolver.cs` from a backup by a harness script.
For each seed the harness rebuilt, ran the resolver / route / registry / Office no-target suites (206 tests), and
restored the backup. The file was touched after every restore. The final restore was byte-identical (`diff -q`).

| Seed | Tests that failed |
|---|---|
| S1 (item 1): invoice's `sprk_regardingagreement` dropped from the table | 6, including the route PUT, the communication path and the pin |
| S2 (item 2): `sprk_servicerequest` reclassified back to Root | 5 |
| S3 (item 2): `sprk_workassignment` entry removed | 6 |
| S4 (item 2): `contact` entry removed | 2 |
| S5 (item 2): project's pair switched off | 2 |
| S6 (item 3): the pair ignored entirely | 14, including the route PUT |
| S7 (item 3): agreement by identity removed, so the type is always read | 1 |
| S8 (item 3): disagreement picks the typed root | 1 |
| S9 (item 3): an id with no type read as "no pair" | 1 |
| S10 (item 3): an unparseable id read as "no pair" | 1 |
| S11 (item 3): an unclassified type read as "no root" | 1 |
| S12 (item 3): a pair naming an intermediate read as "no root" | 2 |
| S13 (item 3): an unreadable type row read as a party | 1 |
| S14 (item 4): the undefined-securability refusal removed | 4 |
| S15 (item 2/3): the pair columns left out of the read (`AllColumns`) | 18 |

### Still open after f3 — owner decisions, not deferrals

1. **Escalation trigger 2, (a)/(b)/(c).** This is unchanged, but the held path now also covers a service request, an
   invoice → agreement, a work assignment → communication / event / invoice, a contact → invoice, and a pair naming
   any intermediate.
2. **Interpretations the owner may reverse** (each is one line in `KindByEntity` or `ByEntity`):
   - **(i)** A pair naming a PARTY adds nothing, rather than being held. This matches the typed party columns. The
     brief's literal "non-root → intermediate" would refuse every to-do whose pair names a person.
   - **(ii)** Agreement by identity does not read the pair's type (event `a6d00177` above).
   - **(iii)** A non-secure WORK ASSIGNMENT / PROJECT whose row names a secure root stores its content in that root's
     container. This is C10 part 2 read as "filed regarding = child". The ACCESS taxonomy (`CoreAncestorResolver`
     rule 1: a core record does not inherit from another) is untouched.
   - **(iv)** A contact's `sprk_invoice` is held. This reverses the f2 reading, per the f3 brief.
   - **(v)** `sprk_servicerequest` is an intermediate for CONTAINER purposes only. It stays CORE for access.
3. **AC7 live gate.** It now also covers: an upload to event `80164675-0311-f111-8342-7c1e520aa4df` (pair-only,
   dangling matter) must answer 409 `container_ancestor_unresolved` with nothing written.
4. **The SPE-membership 403 observation** (unchanged).
