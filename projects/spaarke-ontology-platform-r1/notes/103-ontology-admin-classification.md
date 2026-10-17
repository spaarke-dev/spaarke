# Task 103: Classification admin (slice c)

Lane `task/ontology-103`, base `387e2d70e` (docs/ontology-platform-design). FULL rigor, Sonnet at high effort.

## 1. Pre-start checks

### 1.1 Reconciliation B.6 (HANDOFF @ `ae1cc9f`)

Task 103 is M-10 in the admin reconciliation (H§8, Classification). It builds the Classification area of the Ontology
admin tab 104 shipped: the triage categories with guidance, On/Off, the rules that read each, 30-day volume, and
measured recall against the D-49 floor (80% on at least 50 labelled items); create (saved Off), edit, On/Off. It builds
no other admin section (Actions, Suppression, Health, Access stay out; spec FR-57..FR-59). Where measured recall is
stored (C-16) is settled for this task by the owner's D-49 amendment: the three `sprk_triagecategory` columns task 074
fills.

### 1.2 uac-r2 re-read (owner 2026-10-07 coordination constraint)

`origin/master` at **`72b55bb6c`**, fetched 2026-10-16. Files re-read, with the last commit touching each:

| File | Last change on master |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Access/SecureChildLineage.cs` | `c1e16b784` (K-145-3, docs-only for this task) |
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordOwnershipResolver.cs` | `ba9945149` |
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/CoreAncestorResolver.cs` | `72b55bb6c` (log level only, #2029) |
| `src/server/api/Sprk.Bff.Api/Api/Filters/RecordRouteAccessAuthorizationFilter.cs` | `c5f71487f4` |
| `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/CallerRecordAccessProbe.cs` | `c5f71487f4` |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` | `b1eaecb49` (retires the dead `POST /api/documents/{id}/share-link` route, #2021) |
| `config/secure-record-owner-role.json` | `b2fc3fb781` (merge only; no content change) |

Effect on this task: none of them is a record-access path. `sprk_triagecategory` is organisation-owned reference data,
so the task makes no per-record access decision, uses no owner or lineage mechanism, and adds no secure-family child.
It reuses uac-r2's census, `IDataverseUserClient` and the single 403 reason code
(`sdap.access.deny.caller_unresolved`). The only shared file it edits is the ledger: one GovernedFile, four
HandlerDecisions, `ExpectedEndpointFileCount` 126 to 127. **Merge note:** master's #2021 also edits that ledger and
may change the count, so whoever merges the two must recompute `ExpectedEndpointFileCount` (the base branch is not
yet on master's ledger).

## 2. Escalation trigger: checked, did not fire

The trigger: "task 074's recall columns do not exist when this task runs". They exist: `sprk_measuredrecall`,
`sprk_labelledsetsize` and `sprk_recallmeasuredon` are in
`src/dataverse/solutions/SpaarkeMaster/Entities/sprk_TriageCategory/Entity.xml`, and `notes/deploy-log.md:190`
records the live write of all three on two categories on 2026-10-10. This task reads those columns only; it did not
read 074's notes file for values.

## 3. What was built

| Layer | Files |
|---|---|
| Routes | `Api/Ontology/OntologyAdminClassificationEndpoints.cs` (own group `/api/v1/ontology/admin/classification`; GET list, POST create, PATCH edit, PUT enabled); one call added at the end of `MapOntologyAdminEndpoints` |
| Services | `Services/Ontology/OntologyAdminClassificationService.cs`, `...Models.cs`, `...References.cs` |
| Ledger | `RouteAuthorizationGuardTests.Ledger.cs`: GovernedFile + 4 HandlerDecisions, count 127; `RecordOwnerAssignmentCensusTests.cs`: one `RunAsUserWritesThatFileNothing` entry |
| UI | `OntologyAdmin/Classification/*` (service, view, form, area, tests), the barrel, and the existing workspace widget |

Every read and write goes through `IDataverseUserClient` (the caller, via OBO). The service never consults the
capability answer and holds no app-identity Dataverse client, which are rules A and B of
`OntologyAdminCallerBoundaryGuardTests` (the guard covers `Api/Ontology/` and `OntologyAdmin*` files, so it covers the
new files without an edit).

Behaviour:
- **New category saves Off**, even if the body says `enabled: true` (the body has no such field; the create body is
  built with `sprk_enabled = false`).
- **Rename is refused (409 `ontology.admin.classification.rename_blocked`, with `blockedBy`)** while any policy
  version reads the category. If the policies or versions cannot be read, the rename is refused as a 503 fault, never
  allowed unchecked (fail closed).
- **Edits are version-conditional** (`If-Match`); a stale form gets 409 `stale`. A write that changes nothing is not
  sent.
- **A Console User is refused by every write route** (Dataverse refuses the caller; mapped to 403); an unresolved
  caller gets the single 403 on all four routes; no token is 401.
- **No confidence** is returned or shown anywhere. No recall is "Not measured", never zero.

## 4. Policy references (the scan that blocks a rename)

Rule bodies name a category by GUID in `filter.sprk_triagecategory`. The scan reads every policy and every policy
version and walks every JSON string with `Guid.TryParse`, so it recognises the GUID however it is written (lower,
upper, braces, no hyphens, inside an operator object). If a body is not valid JSON it falls back to a regex over the
text, so damaged JSON still blocks.

- **Billing-domain policies are counted.** No domain filter is applied: a rename that breaks any rule is blocked
  whatever its domain. (`sprk_domain` is not on this base; the ebilling work, #1911, lands it later. Even when it does,
  the scan must still count every domain, because a category rename is global.) A test pins this.
- **Any version blocks, not only the in-force one**: in force, draft and earlier versions each block, because a draft
  can be published and an earlier version can be restored.

## 5. Deviations from the POML

1. **Own route group** `/api/v1/ontology/admin/classification` instead of the shared group. A cross-file route group
   needs a pinned Aggregator in the census; an own group in its own file avoids touching 104's.
2. **Step 4 (TASK-INDEX and POML status) not done**: the lane brief forbids editing `TASK-INDEX.md`; the POML
   `<status>` is left for the main session.
3. **`sprk_priorityweight` is not exposed.** The POML asks for guidance, On/Off, readers, volume and recall; weight is
   not on the list (critical path, D-106).
4. **No domain filter** on the policy scan (section 4).

## 6. Known limits (recorded, not fixed)

1. **References and volume are "what the caller can read".** Policies and versions are user-owned and the
   administrator role reads them at Deep depth, so a policy owned outside the caller's business-unit tree is not
   visible and neither is a communication in volume. This is the same boundary every as-the-caller admin read has.
   The list says when the references or the volume could not be read at all (`referencesAvailable` /
   `volumeAvailable`), but it cannot say a row was hidden by depth. **For a rename this is no longer a silent gap:**
   section 10.1 refuses the rename unless the caller's read provably covers every business unit (row by row). The list and the
   volume still show only what the caller can read.
2. **TOCTOU.** A rule authored between the rename check and the PATCH can name the old category. The window is one
   round trip and the rule would hold the GUID, which survives a rename (evaluation does not break; only the
   constrained-decoding enum name changes, NFR-09).
3. ~~**Routing configuration keyed by category name is not scanned.**~~ Closed in fix round 1 (section 9.4).

## 7. Evidence

See the PR body for the criteria-to-evidence table, the test counts, the seeding proofs and the publish-size delta.

## 8. Out of scope, noticed

- `CategoryRoutingOptions` keyed by category name: closed in fix round 1 (section 9.4).
- Base branch lacks `58e810405`, so `InboundBodyDtoMappingGuardTests` fails on `OntologyAdminCapabilities.CanAuthorPolicies`
  on this base. Not touched here; it is fixed on the docs branch.

## 9. Fix round 1 (PR #2038 review; head before the round `2f5c0ce96`)

### 9.1 F2: the rename check no longer fails open for a caller who cannot see every policy version

The reference read runs as the caller. Live, the Spaarke Ontology Administrator role holds Read on `sprk_policy` and
`sprk_policyversion` at **Deep**, so an administrator in a child business unit does not see versions owned higher up, and the
old check let a rename through unchecked. No role change: `CheckRenameAsync` now verifies coverage first
(`OntologyAdminClassificationService.CheckReadCoverageAsync`), all AS THE CALLER:

1. `WhoAmI()` gives the caller's user id. (Round 2 dropped the caller's business unit: section 10.1.)
2. **(Superseded by section 10: the decision is now row by row.)**
   `systemusers({id})/Microsoft.Dynamics.CRM.RetrieveUserPrivilegeByPrivilegeName` for `prvReadsprk_Policy` and for
   `prvReadsprk_PolicyVersion`. The depth is the highest across the rows returned (one row per role; no row means the
   privilege is not held). The shape was read live on spaarkedev1: `"Depth":"Global"` (a string), and the root business
   unit has `_parentbusinessunitid_value: null`.
3. **(Superseded by section 10.1: the caller's business unit was the wrong unit to test; this rule failed open.)** Complete coverage was **Global on both**, or **Deep on both and the caller's business unit has no parent** (Deep at the root
   reaches every business unit). Only Deep needs the business-unit read.

Incomplete coverage is **409 `ontology.admin.classification.rename_unverifiable`** ("cannot be checked from your business
unit"), and the reference read is not even made. A fault in any of the three reads, or an answer that is not the expected
shape, is **503**; a caller Dataverse cannot name is the single 403. Guidance edits and On/Off never run this check. The code
is its own (it does not use the capability probe's members; 104 guard rule A bans them in Ontology files).

The cost is two or three small reads per rename; a rename is rare.

### 9.2 F1: the Off warning

Turning a category Off stops **new** emails being classified as it. A rule that reads it still matches emails **already**
classified as it, so the old wording ("they cannot match until it is On again") was wrong. When a rule reads the category the
area now asks for confirmation BEFORE writing ("Turn X Off? New emails will no longer be classified as X. POL-... still
matches emails already classified as X."), and the notice after the write says the same. A category no rule reads turns Off at
once. The rules are named by policy code, as in the list.

### 9.3 F3

Removed the sentence in `OntologyAdminClassificationEndpointTests.cs` that cited `OntologyAdminClassificationLiveTests.cs`,
which does not exist. The live test was not added.

### 9.4 K2: routing configuration keyed by category name

`UpdateAsync` takes `IOptionsMonitor<CategoryRoutingOptions>` (in process, no app identity) and refuses a rename with
**409 `ontology.admin.classification.rename_routed`**, naming the entry (`Communication:CategoryRouting:CategoryToTeam`, or
`...:Tenants:{tenant}:CategoryToTeam`). It checks the global map and every per-tenant map, matching keys trimmed and
case-insensitively (a superset of what `CategoryRoutingGate` matches), and **whatever the `Enabled` switch says**: a dormant
entry would break the day routing is switched on. It runs before any Dataverse read.

### 9.5 K2: duplicate names

- Turning a category **On** re-runs the name-in-use check among ACTIVE rows (excluding itself): a conflict is 409
  `name_taken`. Turning Off never checks.
- `LookupChoicesResolver.ResolveLookupAsync` removes duplicate names (`Distinct(OrdinalIgnoreCase)`, first spelling kept, order
  kept) from the choice list, so a duplicate never reaches the constrained-decoding enum. The resolver is shared by every
  `lookup:` reference; its tests and the enrichment and scope-resolver tests were run (section 9.10).
- **Awaiting the owner:** the full fix is an alternate key on `sprk_triagecategory.sprk_name`. That is a Dataverse schema
  change, not made here. Until then two creates racing, or a row made outside this area, can still produce two rows with one
  name; this round only stops the second one being turned On and keeps the enum clean.

### 9.6 K3: rule bodies that name a category by name

`OntologyAdminClassificationReferences.CategoriesNamedIn` counts a JSON string **value** equal to the category's current name
(trimmed, case-insensitive) as a reference, alongside the id scan. A property name is not a value. A body that is not valid
JSON is scanned as text for the name (substring, case-insensitive), which over-reads in the safe direction. The list's "Read
by" uses the same scan, so it shows these rules too.

### 9.7 K3: stale recall

`modifiedon` is now read. `ClassificationRecall.MeasuredBeforeLastEdit` is true when the row was modified (UTC) on a **later
day** than `sprk_recallmeasuredon`; the area shows "Measured before the last edit" under the figure. **Limit:** day
granularity on purpose. Task 074 writes the recall columns onto the same row, which moves `modifiedon` on the measuring day,
so an edit made later the same day cannot be told from the measurement. A toggle also moves `modifiedon`, so a category turned
On/Off after its measurement is marked too (the brief says "last edit").

### 9.8 K4: typed text lost on toggle

Chosen: **the switch of a row is disabled while that row's edit form is open** (not "keep the typed text"). Keeping the text
while the form's `version` moves on would let a save overwrite another administrator's concurrent change without the stale
check noticing. The form key still includes the version, which now only changes after a save (the form closes) or a reload
with the form closed.

### 9.9 Ledger

The PATCH and PUT HandlerDecisions in `RouteAuthorizationGuardTests.Ledger.cs` now describe the coverage check, the routing
check, the by-name scan and the On-time name check, with refreshed line references; the List and Create ones have refreshed
line references only. No route was added; `ExpectedEndpointFileCount` is unchanged (127).

### 9.10 Fix round 1 evidence

See the PR body (test counts, the seeding result, lint).

## 10. Review round 2 (PR #2038; head before the round `c0e3f43ae`)

### 10.1 F1: the rename coverage check still failed open (confirmed live by uac-r2 and the re-check)

Round 1 took the maximum `Depth` over all RolePrivilege rows and asked whether the CALLER's business unit is the root. That is
wrong: Deep reaches only the subtree of the business unit of the role copy that grants it (for a team role, the team's unit),
and a conditional privilege limits rows even at Global. A root-unit caller holding Basic directly and Deep through a child-unit
team's copy passed the check, Dataverse trimmed the reference read to the team's subtree, and a rename silently broke a rule
the caller could not see.

**The rule now** (`OntologyAdminClassificationService.CheckReadCoverageAsync`, `ReadGrantsAsync`, `HasNoRecordFilter`,
`ReadRootBusinessUnitAsync`), all AS THE CALLER:

1. Every RolePrivilege row for the privilege is kept (name, depth, `BusinessUnitId`, `RecordFilterId`), not just the maximum depth.
2. A privilege is **covered** when at least one row for it is unconditional (`RecordFilterId` null, missing or the all-zero GUID;
   a value that is not a GUID counts as a condition) AND is either **Global**, or **Deep with `BusinessUnitId` equal to the root
   business unit**. A row with a missing or unparseable `BusinessUnitId` never covers through the Deep branch.
3. The root business unit is read once, only when a Deep row is the deciding row (a Global caller makes no business-unit
   read): `businessunits?$select=businessunitid&$filter=_parentbusinessunitid_value eq null`. Exactly 1 row is expected;
   0 or more than 1, a failed read or a body of another shape is **503**. The caller's own business unit is no longer read or used.
4. BOTH `prvReadsprk_Policy` and `prvReadsprk_PolicyVersion` must be covered, otherwise **409 `rename_unverifiable`** and NO
   reference read is made (the check still runs before `LoadReferencesAsync` and is the only path to it).
5. Fault handling is unchanged: a fault gives 503; a 401/403 or an unresolved caller gives the uniform 403.

**Live row shape** (spaarkedev1, read-only):
`{"PrivilegeName":..,"Depth":"Global","BusinessUnitId":"06fbf21c-...","RecordFilterId":"00000000-0000-0000-0000-000000000000","RecordFilterUniqueName":""}`.
Dev has 5 child business units and teams in several of them. `sprk_policy` and `sprk_policyversion` are UserOwned.

### 10.2 Closed by the re-check

- **FLS is closed.** 0 secured attributes on `sprk_policy` and `sprk_policyversion`, checked live, so no field of the rule body
  is hidden from the reference read by field-level security.
- **Row scope is closed.** The reference read uses no `$filter` and reads every state and every page, so the only trimming left
  is the caller's own read privilege, which is exactly what 10.1 now verifies.

### 10.3 Known limits (recorded, not fixed)

- **K (create/publish race).** A policy created or published between the coverage check and the rename cannot be closed by a read
  check. The rule would hold the category GUID, which survives a rename (only the constrained-decoding enum name changes), so the
  exposure is the by-name references only.
- **K3.** A category name embedded inside a longer JSON string value, such as `"Category: Urgent"`, is not matched by
  `CategoriesNamedIn`, which matches a whole string value (trimmed, case-insensitive) or the id. The compiler grammar uses ids
  or exact values, so a rule authored through it cannot name a category that way.
- **K4.** `LookupChoicesResolver` dedupe (`Distinct(OrdinalIgnoreCase)`) collapses lookup values that differ only by case.
- **The `RecordFilterId` rule is inferred.** "A privilege with a record filter is conditional and limits rows even at Global"
  follows conditional-privilege semantics but is not confirmed on Microsoft Learn. The rule fails closed: a filtered grant never
  counts as coverage, so the worst case is an unnecessary 409 for an administrator, never a missed reference.

### 10.4 Evidence

Tests: `OntologyAdminClassificationEndpointTests` (must-fire 409, must-not-fire, 503 for the root-unit read, and the seeding
proof) and the suites listed in the PR body.
