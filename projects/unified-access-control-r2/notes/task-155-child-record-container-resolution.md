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

> **Superseded by task 156 (2026-10-02, owner round 4 item 5 — option b).** The held position below
> (`container_ancestor_unverifiable` for every child filed under another record) is REPLACED: the record a child is filed
> under is read LIVE and its root compared with the child's copy — equal resolves exactly like a direct root link,
> different refuses **`container_ancestor_stale`** (409) and enqueues a re-stamp, unreadable refuses 503. Every BFF re-file
> path re-stamps the copies in the same operation (`CoreAncestorRestamper`), and `CoreAncestorStampReconciliationJob`
> repairs out-of-band staleness every 5 minutes. Still held: a service request, and an intermediate on a row that carries
> no copy (work assignment, project, contact, invoice / document / agreement filed under another record). Interpretation
> (viii) below is superseded too: the communication's invoice is compared, not followed. See
> [task-156-stamp-freshness.md](task-156-stamp-freshness.md).

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

> **Superseded by the consolidated list in "Round f4" → "AC7 manual live gates (all rounds)".** Items 1–4 below are
> kept as written in round r0; the consolidated list carries them forward with every later round's additions.
> **Do not use the container id below (f5 correction):** the task-144 cutover on 2026-10-02 re-provisioned project
> `65a3fab2`, so `b!HBRbo…` is now an orphaned, empty drive. Its live `sprk_containerid` is
> `b!MVasATu_GE6Lqs6JOGaeghG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN` (read-only, 2026-10-02); the consolidated list
> names it.

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
2. **Interpretations the owner may reverse** — now recorded, each with the one-line change that reverses it, under
   "Owner-reversible interpretations" at the end of this note (f4 item 4). As written in f3:
   - **(i)** A pair naming a PARTY adds nothing, rather than being held. This matches the typed party columns. The
     brief's literal "non-root → intermediate" would refuse every to-do whose pair names a person.
   - **(ii)** Agreement by identity does not read the pair's type (event `a6d00177` above).
   - **(iii)** A non-secure WORK ASSIGNMENT / PROJECT whose row names a secure root stores its content in that root's
     container. This is C10 part 2 read as "filed regarding = child". The ACCESS taxonomy (`CoreAncestorResolver`
     rule 1: a core record does not inherit from another) is untouched.
   - **(iv)** A contact's `sprk_invoice` is held. This reverses the f2 reading, per the f3 brief.
   - **(v)** `sprk_servicerequest` is an intermediate for CONTAINER purposes only. It stays CORE for access.
3. **AC7 live gate** (consolidated, with every round's additions, in "Round f4" → "AC7 manual live gates (all
   rounds)"). It now also covers: an upload to event `80164675-0311-f111-8342-7c1e520aa4df` (pair-only,
   dangling matter) must answer 409 `container_ancestor_unresolved` with nothing written.
4. **The SPE-membership 403 observation** (unchanged).

## Round f4: fourth adversarial-verifier findings (branch `task/uac-r2-155-f4`)

The f3 verifier judged NEEDS-FIXES. Items 1–4 are closed below. Escalation trigger 2 options (a)/(b) are still NOT
implemented here (task 156 does (b), owner round 4 item 5). `OfficeService` is untouched.

### Item 1 (fail-open): the root walk was one hop

**The defect.** A root link was followed ONE hop. f3 interpretation (iii) stores a NON-secure work assignment's own files
in the SECURE matter / project its row names (and a project's, through its pair). But a to-do, event or invoice under
that work assignment — by `sprk_regardingworkassignment`, or a pair naming a work assignment or project — read only the
work assignment's own flag ("not secure") and resolved the shared business-unit container. The chain disagreed with
itself, and the disagreement was a fail-open. Live example: event `a30254d0-7f1e-f111-88b3-7ced8d1dc988` under work
assignment `9c0254d0-7f1e-f111-88b3-7ced8d1dc988`.

**The fix** (`RecordContainerResolver.ResolveSecureAncestorAsync`, now a walk). Every row the walk reads names the records
above IT, through the same `ChildAncestorLinks` table the record's own row uses, and those are read in turn.

- **One read per row.** A root above the record is read with its flag, its container AND its own links. A project's
  links are its pair; a matter has none; a work assignment has its typed roots, its held columns and its pair.
- **Any secure root in the chain decides.** The content goes to that root's own container, or the resolver refuses
  (`secure_record_container_missing`). Never a shared container while a secure root is anywhere above.
- **Two DIFFERENT secure roots anywhere → `container_ancestor_ambiguous`.** That covers two branches, or two secure roots
  stacked one above the other. The walk continues PAST a secure root to find a second one (interpretation vi). The same
  record reached twice (a diamond) is one root.
- **Failures.** An unreadable row → `container_ancestor_unresolved` 503. A missing row, a null row or an unknown type → 409.
- **Held rows.** A row above the record that names an intermediate is the held path (`container_ancestor_unverifiable`).
  The response then names that row ("File the sprk_workassignment directly against …").
- **The pair rules hold on every row.** Agreement by identity, disagreement → ambiguous, type read when it alone names a
  record.
- **Bounded and cycle-guarded.** `MaxRootChainDepth` = 4 hops deep and `MaxRootReads` = 8 rows across all branches.
  Anything past either bound is REFUSED (409), never truncated into "no secure root". A record already on the walk —
  including the record being resolved — is not read again.
- **A root type that cannot carry `sprk_issecure` but names roots above it is still followed.** Its links are read; its
  flag is not, because reading it would fault. A root type that can do neither (a matter in such an org) is still never
  read.

Interpretation (iii) is kept exactly as implemented. Whether such a work assignment should itself become secure for
OWNERSHIP / ACCESS is a separate owner question, not this task.

**Live (read-only simulation, below):** exactly one record changes on the record path — event `a30254d0` goes from the
shared BU container to `container_ancestor_unresolved` 409. Its work assignment's pair names matter `c4ef17ed-…`, which no
longer exists. No live record moves INTO a secure container, because the one secure root (project `65a3fab2`) has no
work assignment under it.

### Item 2 (fail-open): the communication path

**The defect.** `sprk_communication` carries the polymorphic pair (161 of 276 live rows) and non-securable intermediates
(service request, event, analysis, budget, report card). `CommunicationContainerResolver` read only its SECURABLE typed
regardings, so a communication linked to a secure matter only by its pair, or filed under a service request, went to
the shared archive container.

**The fix.** The communication ITSELF is now the record: one call into the same child resolution as the record path.

- **The entry.** `ChildAncestorLinks` gains a `sprk_communication` entry, from a read-only live sweep of its 24 lookups
  (2026-10-02).
- **What the resolution covers.** The pair rules, the held path for intermediates and the transitive root walk are the
  record path's own code.
- **Fallback.** `CommunicationContainerResolver.ResolveContainerAsync` now calls
  `RecordContainerResolver.ResolveForRecordWithFixedFallbackAsync("sprk_communication", id, archive)`. The archive fallback
  stays ONLY when no root can be involved. When the archive is unconfigured the answer is still "none — skip"; the
  communication's business unit is never derived, which the public four-argument overload would have done.
- **What is preserved.**
  - The empty-securable-set refusal (`securable_entities_unknown`, before any read) moved with the adapter.
  - The inbound processor's permanent/transient split (`IsPermanentContainerRefusal`) is unchanged and pinned. The new
    refusals are 409 ancestor codes (permanent) or 503 (transient).
  - An unreadable communication row is now the typed 503, still transient. Before it was a raw fault, also transient.
  - A missing communication row is `container_record_not_found` 404, non-permanent. Before it was
    `communication_regarding_unknown` 409, also non-permanent.
- **No longer raised: `communication_secure_container_ambiguous`.** Two secure records are now the record path's
  `container_ancestor_ambiguous`, which is also permanent. Two DIFFERENT secure records now refuse even if they share one
  container. That is co-mingling, and `ResolveOwningRecordAsync` already treats it as ambiguous. The old code stays in
  the predicate for compatibility.
- The adapter's unused `IGenericEntityService` constructor parameter was removed. DI is constructor-injected, so only the
  four test constructions changed.

**`sprk_communication`'s 24 lookups, classified (live, read-only, 2026-10-02):**

| Kind | Columns |
|---|---|
| Root, FOLLOWED | `sprk_regardingproject`, `sprk_regardingmatter`, `sprk_regardingworkassignment` |
| Securable record, FOLLOWED live (interpretation viii) | `sprk_regardinginvoice` → sprk_invoice (its own flag, container, typed `sprk_project` / `sprk_matter`, held `sprk_regardingagreement`, pair) |
| Intermediate, HELD (`container_ancestor_unverifiable`) | `sprk_regardingservicerequest`, `sprk_regardingevent`, `sprk_regardinganalysis`, `sprk_regardingbudget`, `sprk_regardingreportcard` |
| Party (not read) | `sprk_regardingperson` → contact, `sprk_regardingorganization` → sprk_organization, `sprk_regardingaccount` → account |
| Grouping (not read; interpretation vii) | `sprk_communicationthread` → sprk_communicationthread |
| Reference / principal / system (not read) | `sprk_triagecategory` → sprk_triagecategory (its own lookups: the four system columns + `organizationid`); `sprk_regardingrecordtype` (the pair's type); `sprk_sentby` → systemuser; `createdby` / `createdonbehalfby` / `modifiedby` / `modifiedonbehalfby` / `owninguser` → systemuser; `ownerid` → systemuser\|team; `owningteam` → team; `owningbusinessunit` → businessunit |
| Polymorphic pair | YES — `sprk_regardingrecordid` (String) + `sprk_regardingrecordtype` |

**Why the thread is not ownership.** `sprk_communicationthread` carries the same regarding lookups as a communication.
Read literally, f3's rule — "a column whose target can hang off a root is read or refused" — would hold it. It is not
held, for three reasons:

- **Its anchor is COPIED FROM its messages' regarding** (`IThreadResolver`: "when a NEW thread is created, its anchor is
  copied from the message's resolved regarding"). The thread is downstream of the communication, not its owner.
- **Live threads group messages filed under DIFFERENT matters.** Thread `1e992cc9`'s messages `14b094bb` and `dd89f19c`
  regard matter `b68299c6`.
- **Every message gets a thread** by the 3-tier ladder, and on inbound the thread is assigned before archival. Holding on
  it would refuse every threaded message: 95 of 276 live, and every new inbound email.

What this leaves: a message with no regarding of its own, inside a thread anchored to a SECURE matter, still goes to the
archive, exactly as before f4. Live: 5 such messages. Their threads regard matters `b68299c6` and `1e992cc9`, neither
secure. This is interpretation (vii), and it is an owner question.

**Live (read-only simulation):** 10 of 276 communications change outcome, all from "archive" to a permanent refusal. None
was under a secure root.

> **f5 correction.** Three of the ten below — `d3516503`, `83349fe9` and `84d04780` — were NOT refusals the brief allowed:
> each pair id is the row's OWN typed person, and `d3516503` is exactly the row the current outbound sender writes for an
> email regarding a person. Round f5 extends rule 3 to the row's typed party regarding lookups, and all three return to
> the archive. "Pair id with no type … a data fix" was also wrong for two of the six: see "Round f5" → item 1.

- Unverifiable: `3b7b5825-8a96-f111-b8db-0022482fb5a7` (regarding an event) and `a36784ef-e38e-f111-b8db-7ced8ddc4a05`
  (an analysis).
- Unresolved 409, a pair id with no type:
  - `ab302254-ab58-f111-a824-3833c5d9bcb1`
  - `d3516503-748b-f111-8076-7ced8d1dc216`
  - `4971a3c2-236b-f111-ab0d-7ced8ddc4a05`
  - `817708e9-427b-f111-ab0e-7ced8ddc4a05`
  - `70741c9f-477b-f111-ab0e-7ced8ddc4a05`
  - `a4c9b0ca-9b7b-f111-ab0e-7ced8ddc4cc6`
- Ambiguous, the typed link and the pair disagree:
  - `83349fe9-828c-f111-8077-7ced8ddc4a05`: typed invoice; pair id with no type.
  - `84d04780-2f81-f111-ab0f-7ced8ddc4a05`: typed matter; the pair names a CONTACT. See interpretation (ix).

The other 266 keep the archive.

### Item 3: bookkeeping

The consolidated lists below include work assignment `9c0254d0-7f1e-f111-88b3-7ced8d1dc988` (409
`container_ancestor_unresolved`), work assignment `b10b7dab-437b-f111-ab0e-7ced8ddc4cc6` (`container_ancestor_unverifiable`),
event `80164675-0311-f111-8342-7c1e520aa4df` and to-do `4ff4dc1f-3c9a-f111-b8de-7ced8ddc4cc6`.

### New live refusals (all rounds; read-only simulation, spaarkedev1, 2026-10-02)

**Method.** A read-only Python simulation (GET only) loaded every live to-do (50), event (73), invoice (10), work
assignment (22), project (19), matter (59), communication (276), the contacts carrying `sprk_invoice` (0) and the 14
`sprk_recordtype_ref` rows. It applied the resolver's rules in both f3 and f4 form and diffed the outcomes. Live has ONE
secure root: project `65a3fab2-77a5-f111-aaad-70a8a590c51c`. "Before" is what the record resolved before the round that
introduced the refusal: the BU container on the record path, the archive on the communication path. That was shared
either way.

| Record | Refusal | Why | Round |
|---|---|---|---|
| to-do `1432926a-0266-f111-ab0c-70a8a590c51c`, `1b32926a-0266-f111-ab0c-70a8a590c51c`, `9fb4ece2-397b-f111-ab0e-7ced8ddc4a05`, `31c9680c-037c-f111-ab0e-7ced8ddc4a05`, `33c9680c-037c-f111-ab0e-7ced8ddc4a05`, `36c9680c-037c-f111-ab0e-7ced8ddc4a05`, `b70b7dab-437b-f111-ab0e-7ced8ddc4cc6`, `9250d1b8-467b-f111-ab0e-7ced8ddc4cc6`, `6d67b203-049d-f111-b8de-7ced8ddc4cc6` | 409 `container_ancestor_unverifiable` | regarding an invoice (held, trigger 2) | r0 |
| to-do `736a6eb7-9570-f111-ab0e-7ced8ddc4cc6`, `bc19baa5-9870-f111-ab0e-7ced8ddc4cc6`, `b856b1ed-7e74-f111-ab0e-7ced8ddc4cc6`, `15d2a80b-7f74-f111-ab0e-7ced8ddc4cc6` | 409 `container_ancestor_unverifiable` | regarding an event | r0 |
| event `edfef460-43bc-f111-aaaf-0022482913fc` | 409 `container_ancestor_unverifiable` | regarding a communication | r0 |
| to-do `a01477e8-427b-f111-ab0e-7ced8ddc4cc6` | 409 `container_ancestor_unverifiable` | regarding report card `9d1477e8-…` | f2 |
| **to-do `4ff4dc1f-3c9a-f111-b8de-7ced8ddc4cc6`** | 409 `container_ancestor_unresolved` | pair id with no type | f3 |
| **event `80164675-0311-f111-8342-7c1e520aa4df`**, `8478ea81-0311-f111-8342-7c1e520aa4df`, `a67d88d8-3e01-f111-8407-7ced8d1dc988`, `094c75e0-4b01-f111-8407-7ced8d1dc988`, `338c5c1e-4c01-f111-8407-7ced8d1dc988`, `6c54b6f0-4c01-f111-8407-7ced8d1dc988`, `0f8efca9-4d01-f111-8407-7ced8d1dc988`, `088dcbd3-4e01-f111-8407-7ced8d1dc988`, `0c6e31b3-6801-f111-8407-7ced8d1dc988`, `4aca40a1-0305-f111-8407-7ced8d1dc988` | 409 `container_ancestor_unresolved` | pair-only, naming a matter that no longer exists (`c4ef17ed-…`, `050995f1-…`, `97962160-…`, `d57bc02f-…`) | f3 |
| **work assignment `9c0254d0-7f1e-f111-88b3-7ced8d1dc988`** | 409 `container_ancestor_unresolved` | its pair names matter `c4ef17ed-…`, which no longer exists | f3 |
| **work assignment `b10b7dab-437b-f111-ab0e-7ced8ddc4cc6`** | 409 `container_ancestor_unverifiable` | regarding invoice `a1652ca5-…` | f3 |
| event `a30254d0-7f1e-f111-88b3-7ced8d1dc988` | 409 `container_ancestor_unresolved` | its work assignment `9c0254d0` names the deleted matter (transitive walk) | **f4** |
| communication `3b7b5825-8a96-f111-b8db-0022482fb5a7` / `a36784ef-e38e-f111-b8db-7ced8ddc4a05` | 409 `container_ancestor_unverifiable` (inbound: permanent skip) | regarding an event / an analysis | **f4** |
| communication `4971a3c2-236b-f111-ab0d-7ced8ddc4a05` (to-do "Test To Do Item"), `ab302254-ab58-f111-a824-3833c5d9bcb1` (document "Discovery Status Report — January 2026.docx") | 409 `container_ancestor_unresolved` (permanent) | a pair id the CURRENT outbound sender writes for a primary it does not map (`sprk_todo`, `sprk_document`): no typed column, no type — and a to-do or a document can belong to a secure matter. **Recurs** (see "Round f5") | **f4** |
| communication `817708e9-427b-f111-ab0e-7ced8ddc4a05` (report card), `70741c9f-477b-f111-ab0e-7ced8ddc4a05`, `a4c9b0ca-9b7b-f111-ab0e-7ced8ddc4cc6` (events) | 409 `container_ancestor_unresolved` (permanent) | pair id with no type, written BEFORE the sender mapped those types (event 2026-07-14, report card 2026-07-29); today's sender writes the typed column, which is the held path. A data fix | **f4** |
| ~~communication `d3516503-748b-f111-8076-7ced8d1dc216`~~ | archive again in **f5** | the outbound sender's party shape: typed person + the same id in the pair, no type | f4 → reverted f5 |
| ~~communication `83349fe9-828c-f111-8077-7ced8ddc4a05`, `84d04780-2f81-f111-ab0f-7ced8ddc4a05`~~ | archive again in **f5** | each pair id is the row's own typed person (the first untyped next to a typed invoice; the second typed contact next to a typed matter); the invoice / matter above them is not secure | f4 → reverted f5 |

Totals after f5: 29 record-path refusals (15 to-dos, 12 events, 2 work assignments; 0 invoices, 0 projects, 0
contacts; unchanged by f5 — no live to-do or event carries a typed party regarding) and **7** communication refusals
(10 under f4). Of the type-less pair ids, only the three written before the sender's mappings (`817708e9`, `70741c9f`,
`a4c9b0ca`) and the record-path ones (dangling matters, to-do `4ff4dc1f`) are a data fix; `4971a3c2` and `ab302254` are
the shape the sender still writes. No data was changed, because no live writes were allowed.

### AC7 manual live gates (all rounds; NOT run — no live writes or deploys in this session)

1. **Deploy** the BFF to dev. This is owned by the main session.
2. **Secure child.** As an existing non-admin user shared on the secure test project
   `65a3fab2-77a5-f111-aaad-70a8a590c51c` (container `b!MVasATu_GE6Lqs6JOGaeghG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`
   — corrected in f5: the task-144 cutover on 2026-10-02 re-provisioned the project, and the earlier id `b!HBRbo…` is an
   orphaned, empty drive that would make this check pass or fail for the wrong reason):
   - first re-read the CURRENT id, read-only — `SELECT sprk_containerid FROM sprk_project WHERE sprk_projectid =
     '65a3fab2-77a5-f111-aaad-70a8a590c51c'` — and use it below if it differs again;
   - create a to-do with `sprk_regardingproject` set to it (a live write);
   - upload through its document control;
   - confirm read-only: `GET https://graph.microsoft.com/v1.0/drives/b!MVasATu_GE6Lqs6JOGaeghG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN/root/children`.
3. **Ordinary child.** Upload to a to-do under an ordinary project. Confirm it lands in the to-do's `owningbusinessunit`
   container (`SELECT owningbusinessunit FROM sprk_todo …`, then `SELECT sprk_containerid FROM businessunit …`).
4. **Held path (r0 / f2).** An upload to to-do `1432926a-0266-f111-ab0c-70a8a590c51c` (regarding an invoice) and to-do
   `a01477e8-427b-f111-ab0e-7ced8ddc4cc6` (regarding a report card) answers 409 `container_ancestor_unverifiable`, and
   nothing is written.
5. **f3 refusals.** Each of these answers the stated code, and nothing is written:
   - event `80164675-0311-f111-8342-7c1e520aa4df` → 409 `container_ancestor_unresolved`;
   - to-do `4ff4dc1f-3c9a-f111-b8de-7ced8ddc4cc6` → 409 `container_ancestor_unresolved`;
   - work assignment `9c0254d0-7f1e-f111-88b3-7ced8d1dc988` → 409 `container_ancestor_unresolved`;
   - work assignment `b10b7dab-437b-f111-ab0e-7ced8ddc4cc6` → 409 `container_ancestor_unverifiable`.
6. **f4 transitive.**
   - An upload to event `a30254d0-7f1e-f111-88b3-7ced8d1dc988` answers 409 `container_ancestor_unresolved`, and nothing
     is written.
   - Positive case: a live write. Set an existing non-secure work assignment's `sprk_regardingproject` to the secure test
     project, create an event regarding that work assignment, upload, and confirm read-only that the file is in the
     project's CURRENT container — `b!MVasATu_GE6Lqs6JOGaeghG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN` as of 2026-10-02,
     re-read as in gate 2 (the same GET as gate 2; NOT the orphaned `b!HBRbo…`).
7. **f4 communication.**
   - Re-run archival for communication `3b7b5825-8a96-f111-b8db-0022482fb5a7` (regarding an event). It is skipped as a
     permanent refusal (`container_ancestor_unverifiable` in the `[SECURE-CONTAINER] REFUSING` log line), and nothing
     reaches the archive container.
   - Positive case: a live write. An inbound email associated to the secure test project lands its `.eml` in the
     project's container (gate 2's id), not the archive.
8. **f5 outbound party shape.**
   - A live write. Send an outbound email from the composer (archive on, the default) whose primary association is a
     contact. Its `.eml` lands in the archive container (`Communication:ArchiveContainerId`) and the send reports no
     archival failure. Before f5 this was refused `container_ancestor_unresolved` and the `.eml` was lost.
   - On demand, a live write: archive existing communication `d3516503-748b-f111-8076-7ced8d1dc216` (typed person, pair
     id = that person, no type). It archives to the archive container; f4 answered 409.
   - Expected refusal, nothing written: archive existing communication `4971a3c2-236b-f111-ab0d-7ced8ddc4a05` (the to-do
     wizard's email: pair id only) → 409 `container_ancestor_unresolved`.
   - Precondition, read-only: neither communication has an archive document yet (`SELECT sprk_documentid FROM
     sprk_document WHERE sprk_relatedcommunication = '<id>'` → 0 rows, 2026-10-02). `ArchiveExistingAsync` returns an
     existing archive idempotently WITHOUT asking the resolver, so if a row appears the gate proves nothing.

### Read budget after f4 (two-argument overload unless stated)

| Shape | f3 | f4 |
|---|---|---|
| To-do / event / invoice under a project or matter, root not secure (the project's pair is read on the same row; live 0 projects carry it) | 3 | 3 |
| The same, root secure | 2 | 2 |
| Under a work assignment that names nothing above it | 3 (row + WA + BU) | 3 |
| Under a work assignment filed regarding a matter / project, none secure | 3 (**leak** if the matter is secure) | 4 (row + WA + matter + BU); 3 if the matter is secure |
| Under a work assignment whose pair alone names a matter | 3 (**leak**) | 5 (row + WA + type + matter + BU) |
| A pair alone naming a work assignment that names a matter | 4 (row + type + WA + BU; **leak**) | 5 / 6 |
| Communication, no regarding | 1 | 1 |
| Communication regarding a non-secure matter (adapter) | 3 (comm + matter + the matter's BU) | 2 (comm + matter; no BU — the archive is the fallback) |
| Communication regarding an invoice under a matter | 4 (comm + invoice + matter + invoice's BU) | 3 |
| Communication linked only by its pair to a matter | 1 (**leak**) | 3 (comm + type + matter) |
| Worst case | — | 1 record + 8 rows above it + one type read per row whose pair alone names a record + 1 BU; past the bounds → 409 |

Each new read decides whether a secure root is above the record. There are no extra metadata round trips: every
classification comes from the scope-memoized catalog.

### Tests (f4)

**`ChildRecordContainerResolutionTests`:**

- **Item 1:**
  - two-hop theory ×5: event/to-do under a work assignment, to-do pair → work assignment, invoice → project-pair →
    matter, to-do pair → project-pair → matter;
  - cost pin (4 reads; the work assignment's links ride on its one read);
  - dangling pair two hops up (409);
  - unreadable row two hops up (503);
  - work assignment held under an invoice;
  - two-secure-roots theory ×2 (branches, stacked);
  - diamond; two cycle tests;
  - depth-bound theory ×2 (exactly 4 resolves, 5 refuses);
  - breadth bound;
  - non-securable root followed for its links;
  - secure root two hops up without a container;
  - row-above-the-record pair disagreement.
- **Item 2:**
  - communication pair → secure matter (archive and no archive);
  - held theory ×5 (service request, event, analysis, budget, report card);
  - communication → non-secure work assignment → secure matter;
  - dangling pair;
  - no link to any root (archive kept; no archive → null; the BU never read);
  - two different secure records;
  - typed vs pair disagreement;
  - unreadable / missing communication row (503 transient, 404 non-permanent);
  - empty securable set refused before any read;
  - a `sprk_communication` row in the live-sweep pin theory.
- **Changed:**
  - `Todo_UnderARootTypeThatCannotBeSecure…` pinned "a non-securable work assignment is never read". That is exactly the
    one-hop assumption f4 removes. It now pins the surviving guarantee — a non-securable root type that ALSO names
    nothing above it (a matter) is never read — with a new test for the followed case.
  - The pin theory's `NonOwnerTargets` gained `sprk_triagecategory` and `sprk_communicationthread`, each with its
    evidence.
  - The f1/f3 communication → invoice tests are unmodified and green.

**Other suites:**

- **`RecordKeyedUploadRouteChildRecordTests`:** +2 REAL-route cases. An event under a non-secure work assignment that
  regards the secure project stores in the project's container. An event under a work assignment with a dangling pair is
  refused 409, and nothing is uploaded.
- **Fixtures (§F.2, assertions unchanged):** `SpeScopeFactoryStub`, `MessageAttachmentMaterializerTests` and
  `SpeFlatUploadPathTests` construct the adapter without the removed parameter, and their registry doubles now answer
  `ClassifyEntityAsync` through `TestEntityCatalog`. The communication is the record being classified.

**Counts:**

| Suite | Result |
|---|---|
| `ChildRecordContainerResolutionTests` | 136 cases (102 in f3) |
| `RecordKeyedUploadRouteChildRecordTests` (real mapped PUT route) | 10 (8 in f3) |
| Affected suites (resolver, child-record, route, registry, Office no-target / provenance, #1038, communication service / processor / materializer, SPE upload paths, document list, acting user, decision table, lockstep) | 364 / 364 |
| Full BFF unit suite (`dotnet test tests/unit/Sprk.Bff.Api.Tests`) | Passed 13339, Failed 0, Skipped 54 (Total 13393) |
| `Spaarke.ArchTests` | 337 / 337 |

`dotnet format` (what the pre-commit hook runs) was run on the changed files before the counts. Its only effect was to
re-order one pre-existing `using` in `SpeFlatUploadPathTests.cs`.

### Mutation proof (f4)

**How the seeds were run.** Each seed was applied by a harness script (`seed_harness.py`, in the session scratchpad):

- replace one exact anchor (asserted unique);
- rebuild;
- run the affected suites (resolver, child-record, route, registry, Office no-target, Office provenance, #1038,
  communication service / processor / materializer, SPE upload paths, document list, acting user, decision table,
  lockstep);
- restore the backup, touch it, and assert byte-identity.

Every seed turned tests red. Out of 364 affected tests:

| Seed | Tests that failed |
|---|---|
| S1 (item 1): one hop only — the rows above a root are not followed | 26, including both new route PUTs and the f1/f3 communication → invoice tests |
| S2 (item 1): a hop's own links left out of its read | 24 |
| S3 (item 1): the walk stops at the first secure root on a branch | 1 (stacked ambiguity) |
| S4 (item 1): the cycle / diamond guard removed | 3 (two cycles, diamond) |
| S5 (item 1): the depth bound removed | 1 |
| S6 (item 1): the breadth (row-read) bound removed | 1 |
| S7 (item 1): a missing hop read as an empty row | 6, including the route PUT and the communication pair |
| S8 (item 1): an unreadable hop read as an empty row | 4 |
| S9 (item 1): a held column on a row ABOVE the record ignored | 2 |
| S10 (item 1): a non-securable root type skipped even when it names roots above it | 1 |
| S11 (item 1): the pair ignored on rows above the record | 9, including the route PUT |
| S12 (item 2): the `sprk_communication` entry removed | 33, including the communication-service / materializer / flat-path suites (the communication is refused as a child with unknown links) |
| S13 (item 2): the communication's invoice no longer followed (held) | 5 (four f1 communication → invoice tests and the f4 two-secure-records test) |
| S14 (item 2): the communication's service-request / event columns dropped | 3 (pin + 2 held rows) |
| S15 (item 2): the adapter derives the business unit when the archive is unconfigured | 1 |
| S16 (item 2): the adapter's empty-securable-set refusal removed | 1 |
| S17 (item 2): the communication's pair not read | 4 |

## Round f5: fifth adversarial-verifier findings (branch `task/uac-r2-155-f5`)

The f4 verifier closed items 1 (two-hop walk) and 2's routing, and judged NEEDS-FIXES on one blocking item and three
bookkeeping / test items. All four are closed below. Escalation trigger 2 options (a)/(b) are still NOT implemented
(task 156 does (b)); `OfficeService` and `CommunicationService` are untouched.

### Item 1 (BLOCKING): f4 refused the shape the outbound sender writes for a person, organization or account

**The defect.** `CommunicationService.MapAssociationFieldsAsync` sets the primary association's typed lookup and
`sprk_regardingrecordid` = the same id. It never writes `sprk_regardingrecordtype` (only the reply-inheritance path tries
to copy one from the source message). For an email whose primary is a contact, organization or account the row is: `sprk_regardingperson` /
`…organization` / `…account` = X, pair id = X, no type, no root anywhere. The party columns were not in the pair's rule 3
(which compared only FOLLOWED links), so rule 5 refused "a record id without a type": `container_ancestor_unresolved`
409, a permanent refusal. Every ArchiveToSpe send regarding a person, organization or account lost its `.eml`
("archival failed", non-fatal), and `ArchiveExistingAsync` answered 409. Before f4 all of these went to the archive. The
brief's preservation clause ("archive fallback stays only when no root can be involved") did not hold, and the f4 note
mislabelled the live example `d3516503` as "a pair id with no type … a data fix" — the sender writes it on every send.

**The fix — rule 3 reads the row's typed PARTY regarding lookups** (`RecordContainerResolver`):

- `ChildAncestorLinks` gains `PartyRegardingColumns`, per entry, from the f3 / f4 live sweeps:
  - `sprk_communication`: `sprk_regardingperson` → contact, `sprk_regardingorganization` → sprk_organization,
    `sprk_regardingaccount` → account;
  - `sprk_todo`: `sprk_regardingcontact`, `sprk_regardingorganization`;
  - `sprk_event`: `sprk_regardingcontact`, `sprk_regardingorganization`, `sprk_regardingaccount`;
  - none for invoice, work assignment and project: they have no regarding-party lookup (their party columns are assignees,
    vendors and law firms, which no builder pairs with the pair id).
- They ride on the row's ONE read whenever the row carries the pair (`AllColumns`), so they cost no round trip.
- Rule 3 is now: the pair's id equals a followed link's id **or one of these columns' id** on the same row → the pair names
  that record. A party is not ownership, so the pair adds nothing; the row's followed links are walked as before. This is
  the rule's own reasoning (the typed lookup is referentially enforced), applied to every typed regarding on the row.
- Identity, not presence: a typed party with a DIFFERENT id leaves the pair to rules 4 and 5 (tested).
- A guard in the `ChildAncestorLinks` constructor refuses a "party" column whose target is not a party, or one on a row
  without the pair: a root there would let rule 3 swallow a pair that names that root.
- The columns were checked live, read-only (2026-10-02), by selecting each one: the SQL endpoint refuses a column that does
  not exist (a deliberately bogus column was refused), so the eight columns exist. A column missing live would fault the
  row read, which fails closed.

**Why not change the sender instead.** (1) The resolver must place the rows already written (`d3516503` and every earlier
send). (2) With rule 3 extended, a type on a party pair would only add a type read. (3) For the sender's UNMAPPED
primaries (below) a type would change only the refusal code (`unresolved` → `unverifiable`), not the outcome. The sender is
untouched; having it write `sprk_regardingrecordtype` stays a data-hygiene option for the owner.

**Live (read-only, 2026-10-02).** The rows the extension can affect are those whose pair id equals a typed party on the
same row: 67 communications (of the 87 carrying both a typed person and a pair id; none carries a typed organization or
account), 0 to-dos, 0 events (none carries a typed party regarding). Three change outcome, all from a refusal back to the
archive:

| Communication | Shape | f4 | f5 |
|---|---|---|---|
| `d3516503-748b-f111-8076-7ced8d1dc216` | outbound; typed person `8e9918a9`, pair id = it, no type | 409 unresolved | archive |
| `83349fe9-828c-f111-8077-7ced8ddc4a05` | typed invoice `55328b00` + typed person; pair id = the person, no type | 409 ambiguous | the invoice is followed → its matter `b68299c6` (pair agrees; `sprk_issecure` NULL) → archive |
| `84d04780-2f81-f111-ab0f-7ced8ddc4a05` | inbound; typed matter `375fa95a` + typed person; pair id = the person, type contact | 409 ambiguous | the matter (not secure) → archive |

The other 64 already resolved to the archive (their pair carries the contact type and nothing else names a record, so rule
5 read the type and found a party); they now skip that type read. No record-path outcome changes. Communication refusals:
10 → **7**. No row reaches a shared container while a secure root is above it: the only live secure root (project
`65a3fab2`) has no communication, to-do, event or work assignment under it.

**What stays refused, and why — corrected from f4.** The f4 note filed six communications under "pair id with no type …
a data fix". Read-only, each pair id was resolved to its record:

| Communication | Pair id names | Why it has no typed column | Outcome |
|---|---|---|---|
| `d3516503` | contact `8e9918a9` | it has one (`sprk_regardingperson`) | **archive (f5)** |
| `4971a3c2-236b-f111-ab0d-7ced8ddc4a05` | to-do `286a7bc0` "Test To Do Item" | the sender maps no `sprk_todo` (`RegardingLookupMap`), and the communication has no to-do lookup | 409 unresolved — **recurs** |
| `ab302254-ab58-f111-a824-3833c5d9bcb1` | document `25b83b0d` "Discovery Status Report — January 2026.docx" | the sender maps no `sprk_document`, and the communication has no document lookup | 409 unresolved — **recurs** |
| `817708e9-427b-f111-ab0e-7ced8ddc4a05` | report card `9d1477e8` | written 2026-07-08, before the sender mapped report cards (2026-07-29, `71ac390871`) | 409 unresolved — data fix |
| `70741c9f-477b-f111-ab0e-7ced8ddc4a05`, `a4c9b0ca-9b7b-f111-ab0e-7ced8ddc4cc6` | events `a2bd239a`, `9ac9b0ca` | written 2026-07-08/09, before the sender mapped events (2026-07-14, `bbffb4532b`) | 409 unresolved — data fix |

The to-do and document rows are the shape the CURRENT sender writes for those primaries (it logs "Unknown entity type …
Regarding lookup will not be set" and writes only the pair id). They stay refused **by design**: nothing on the row says
what the id is, and a to-do or a document can belong to a secure matter — so a root CAN be involved and the archive would
be a guess, which is the fail-open the brief closes. Pinned with the real sender's row
(`OutboundEmail_RegardingAnUnmappedType_AsTheSenderWritesIt_IsRefused`).

**AC5 after f5** ("CommunicationContainerResolver behaves as before for records without a secure ancestor"). It now holds
for every communication whose row can involve no root — no regarding, a party (typed, or the pair naming the row's own
typed party), a thread, a triage category: the archive, or "skip" when none is configured. It deviates only where a root
CAN be involved and nothing on the row says which: a held intermediate, a pair id naming a to-do / document / any record
whose type the row does not state, a pair naming a deleted record, or a pair that disagrees with a typed root. Those are the
shapes brief item 2 routes through the record path's refusals, so the deviation is the brief's, and each is listed above
with its live rows.

### 🔔 Owner: archival skips that RECUR from now on (verifier observation 11, in so many words)

Until task 156 lands, and for some shapes beyond it, these sends are permanent archival skips — the email is sent, its
`.eml` is not archived ("archival failed", non-fatal; inbound: a logged permanent skip; on demand: 409):

1. **Every email — inbound or outbound — filed to an event, analysis, budget, report card or service request**
   (`container_ancestor_unverifiable`, the held path). This is the branch task 156 (option b) is scoped to replace; until
   it lands, every such email skips archival.
2. **Every outbound email whose primary association is a to-do or a document** (`container_ancestor_unresolved`). The
   composer archives by default (`archiveToSpe ?? true`), so **"Email this document" from the file preview
   (`FilePreviewDialog`, primary `sprk_document`) loses its `.eml` on every send**. The to-do wizard's follow-on email does
   not ask to archive, so it matters only for on-demand archival. **Task 156 does NOT fix these**: the sender applies the
   core-ancestor stamp only inside its `RegardingLookupMap` branch, so an unmapped primary gets no stamp either (that is
   also an ACCESS gap for those emails under FR-26, outside this task). Placing them needs a sender change — map the type
   and stamp its ancestor — which is the owner's call.

Before f4 all of these went to the shared archive container, which was the fail-open.

### Item 2 (bookkeeping): gates 2 and 6 named an orphaned container

The consolidated AC7 gates 2 and 6 cited `b!HBRbokLX…` for the secure test project `65a3fab2`. The task-144 cutover on
2026-10-02 re-provisioned the project; read-only, its live `sprk_containerid` is
`b!MVasATu_GE6Lqs6JOGaeghG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`, and the old drive is orphaned and empty, so the gates'
GET would have checked the wrong drive. Both gates now name the current id and tell the operator to re-read
`sprk_containerid` first. The r0 gate section and the r0 POML block carry the same correction. Gate 8 (f5) is new.

### Item 3 (AC6): two branches the verifier's seeds showed unpinned

- **V4, ambiguity by distinct CONTAINERS** (the f3 adapter's behaviour). New test: two DIFFERENT secure roots that share ONE
  container refuse `container_ancestor_ambiguous` — a to-do under a secure project and a secure matter, and an email
  regarding a secure matter and a secure work assignment (permanent on the inbound path).
- **V15, a pair naming an invoice held instead of followed.** It failed closed, but the f4 note claimed the communication's
  pair follows invoices. New test: an email whose pair ALONE names an invoice under a secure matter routes to the matter's
  container.

### Verifier observations 10 and 12

- **10** (a secure record is not walked) is now interpretation (x), with its reversal.
- **12** (the thread is not ownership) stays interpretation (vii), an open owner question. Live: 5 messages, none under a
  secure root.

### Tests (f5)

**`ChildRecordContainerResolutionTests`** — 153 cases (136 in f4):

- the REAL sender's row (new helper `OutboundCommunicationRow`, beside the tests: runs `CommunicationService.SendAsync`
  with only Dataverse and Graph doubled and returns the entity handed to `CreateAsync`):
  - party theory ×3 (contact / organization / account): archive kept, no archive → null, only the communication row read;
    it also asserts the row has the pair id and NO type, so a sender change that starts writing one is noticed;
  - secure matter control: the matter's container, no type read;
  - unmapped theory ×2 (to-do, document): 409 `container_ancestor_unresolved`, permanent;
- the pair names the row's own typed person next to a typed root, with and without the type (`84d04780`) ×2: the root is
  followed, no type read;
- typed invoice + the pair naming the row's own untyped person (`83349fe9`): the invoice is followed;
- a typed person with a DIFFERENT id does not vouch for the pair: still refused;
- to-do / event untyped pair naming its own typed contact / organization / account ×5: resolves (BU), no type read;
- AC6: two different secure roots sharing one container — record path and communication path;
- AC6: a pair alone naming an invoice is followed;
- changed: the live-sweep pin theory now also expects each pair-carrying row's `sprk_regarding*` party columns (derived from
  the snapshot by name and target kind); one f4 display name dropped the two live ids that are no longer that shape.

**Counts:**

| Suite | Result |
|---|---|
| `ChildRecordContainerResolutionTests` | 153 cases (136 in f4) |
| Affected suites (resolver, child-record, route, registry, Office no-target / provenance, #1038, communication service / processor / materializer, association mapping, SPE upload paths, document list, acting user, decision table, lockstep) | 416 / 416 |
| Full BFF unit suite (`dotnet test tests/unit/Sprk.Bff.Api.Tests`) | Passed 13356, Failed 0, Skipped 54 (Total 13410; f4 13393 + 17 new) |
| `Spaarke.ArchTests` | 337 / 337 |

`dotnet format --include` (what the pre-commit hook runs) was run on the four changed / new `.cs` files before the
counts; `--verify-no-changes` on the same files then exits 0.

### Mutation proof (f5)

Same harness as f4 (`seed_harness.py`, session scratchpad, `f5/`): one exact anchor per seed (asserted unique), rebuild,
the affected suites, restore + touch + byte-identity assert.

Every seed turned tests red. Out of 416 affected tests:

| Seed | Tests that failed |
|---|---|
| P1 (item 1): rule 3 compares followed links only — the party identity removed | 11 (the three real-sender party rows, the `84d04780` and `83349fe9` shapes, the five to-do / event party rows) |
| P2 (item 1): the party regarding columns left out of the row read (`AllColumns`) | 14 (P1's eleven + three pin rows) |
| P3 (item 1): the communication's party list dropped | 7 |
| P4 (item 1): the to-do's party list dropped | 3 |
| P5 (item 1): the event's party list dropped | 4 |
| P6 (item 1): identity weakened to presence (any typed party vouches for any pair) | 1 |
| P7 (item 1): party agreement only when no followed link is set (rule 4 fires first) | 3 |
| P8 (item 1): a ROOT column put in the communication's party list (the constructor guard) | 214 (the table fails to initialize) |
| V4 (item 3, the verifier's seed): ambiguity by distinct CONTAINERS | 1 |
| V15 (item 3, the verifier's seed): a pair naming an invoice is held instead of followed | 1 |

## Owner-reversible interpretations

Each line is a reading this task chose where the brief or the data left room. The second line is the change that
reverses it. All but (vii) are in `RecordContainerResolver.cs`.

- **(i) f3 — a polymorphic pair naming a PARTY (contact / account / organization) adds nothing, rather than being
  held.** This matches the typed party columns.
  - *Reverse:* in `ResolvePolymorphicRegardingAsync`, make `case RecordKind.Party:` throw `Unverifiable(...)` like
    `Intermediate`.
- **(ii) f3 — agreement by identity (the pair's id equals a typed link's id on the same row) does not read the pair's
  type.** A mislabelled type (live event `a6d00177`) is ignored.
  - *Reverse:* in rule 3, read the type and refuse `container_ancestor_ambiguous` when `pairEntity` differs from the
    matching hop's entity.
- **(iii) f3 — a NON-secure work assignment / project whose row names a SECURE root stores its own content in that root's
  container** (C10 part 2 read as "filed regarding = child"; the ACCESS taxonomy is untouched). f4 keeps it, and makes the
  records under such a work assignment agree.
  - *Reverse:* delete the `["sprk_workassignment"]` and `["sprk_project"]` entries from `ChildAncestorLinks.ByEntity`.
    The walk then stops at a work assignment / project both for its own uploads and for its children's, so content under
    a non-secure work assignment under a secure matter goes to a shared container.
- **(iv) f3 — a contact's `sprk_invoice` is held** (reversing f2's "party → invoice reference" reading).
  - *Reverse:* delete the `["contact"]` entry from `ByEntity`. The f2 zero-read behaviour for a contact with an explicit
    fallback returns.
- **(v) f3 — `sprk_servicerequest` is an INTERMEDIATE for containers (held) and stays CORE for access.**
  - *Reverse:* set `KindByEntity["sprk_servicerequest"] = RecordKind.Root` and add a `ByEntity` entry for its
    `sprk_regarding{matter,project,workassignment}`. It is then walked live like a work assignment: never read for a
    flag (it cannot carry one), read for its links.
- **(vi) f4 — the walk continues PAST a secure root, so two different secure roots stacked on one chain refuse as
  ambiguous for the records below.** Example: a to-do under a secure work assignment that regards a different secure
  matter. The work assignment's own uploads keep its own container (a secure record is never walked).
  - *Reverse:* in `ResolveSecureAncestorAsync`, do not call `NextHopsAsync` for a hop just added to `secureRoots`. Each
    branch then stops at its first secure root, and the nearest secure root wins.
- **(vii) f4 — `sprk_communicationthread` is a GROUPING, not ownership: never read.** A message with no regarding of its
  own inside a thread anchored to a secure matter keeps the archive (live 5, none secure).
  - *Reverse:* add `("sprk_communicationthread", "sprk_communicationthread")` to the communication entry and
    `KindByEntity["sprk_communicationthread"] = RecordKind.Intermediate`. That refuses EVERY threaded message (95 live,
    all new inbound). Alternatively, follow the thread live — that is option (a), task 156's territory.
- **(viii) f4 — SUPERSEDED by task 156 (interpretation xiv there): the invoice is now compared like every other intermediate.** As written in f4: a communication's `sprk_regardinginvoice` is FOLLOWED live (the invoice's own flag, container and
  links), not held like a to-do's. This preserves what the communication path has done since task 155 r0. An invoice's
  typed `sprk_project` / `sprk_matter` are its own links, not a stamp.
  - *Reverse:* drop `followed: ["sprk_invoice"]` from the communication entry. An email regarding an invoice then refuses
    `container_ancestor_unverifiable` (3 live).
- **(ix) f4 (inherited from f3 rule 4) — a typed link and a pair that name DIFFERENT records refuse as ambiguous without
  reading the pair's type, even when the pair names a PARTY.** f4 cited communication `84d04780` (matter + a contact pair);
  f5 found its pair is its OWN typed person, which rule 3 now reads as agreement (xi), so it archives. No live row is this
  shape any more: (ix) now bites only a pair naming a party that is NOT the row's typed party.
  - *Reverse:* on disagreement, read the type and ignore the pair when `KindOf(pairEntity) == RecordKind.Party`.
- **(x) f4, recorded in f5 (verifier observation) — a record that is ITSELF secure keeps its own container and is not
  walked.** A secure work assignment filed regarding a DIFFERENT secure matter keeps the work assignment's container,
  while a to-do under that work assignment refuses as ambiguous (vi). Inconsistent, but both answers fail closed (neither
  reaches a shared container), and live has no such rows (one secure root, nothing filed under it).
  - *Reverse:* in `ResolveCoreAsync`, call `ResolveSecureAncestorAsync` for a secure record too (drop `!isSecure &&` from
    its guard) and refuse `container_ancestor_ambiguous` when it finds a secure root other than the record. The secure
    work assignment's own uploads then refuse like its to-do's.
- **(xi) f5 — rule 3 counts the row's typed PARTY REGARDING lookups as identity witnesses for the pair**
  (`sprk_regardingperson` / `…organization` / `…account` on a communication, `sprk_regardingcontact` / `…organization`
  [/ `…account`] on a to-do / event). Assignee, vendor and law-firm party columns are not counted: no builder pairs them
  with the pair id. The pair then names a party, which is not ownership (i).
  - *Reverse:* drop the `parties:` arguments from `ChildAncestorLinks.ByEntity`. Do not, unless the outbound sender is
    first changed to write `sprk_regardingrecordtype`: without it every outbound email regarding a person, organization or
    account is refused `container_ancestor_unresolved` and loses its `.eml` archive (the f4 regression).
