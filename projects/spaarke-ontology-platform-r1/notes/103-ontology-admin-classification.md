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
   `volumeAvailable`), but it cannot say a row was hidden by depth.
2. **TOCTOU.** A rule authored between the rename check and the PATCH can name the old category. The window is one
   round trip and the rule would hold the GUID, which survives a rename (evaluation does not break; only the
   constrained-decoding enum name changes, NFR-09).
3. **Routing configuration keyed by category name is not scanned.** `CategoryRoutingOptions` (appsettings, keyed by
   name) is not a policy, so a rename is not blocked by it. Filed as out of scope below.

## 7. Evidence

See the PR body for the criteria-to-evidence table, the test counts, the seeding proofs and the publish-size delta.

## 8. Out of scope, noticed

- `CategoryRoutingOptions` keyed by category name: a rename silently orphans an entry (limit 3 above).
- Base branch lacks `58e810405`, so `InboundBodyDtoMappingGuardTests` fails on `OntologyAdminCapabilities.CanAuthorPolicies`
  on this base. Not touched here; it is fixed on the docs branch.
