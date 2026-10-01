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
