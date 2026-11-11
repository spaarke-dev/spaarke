# Task 013 — Core-ancestor derivation and one-lookup guard for the invoice family (notes)

> 2026-10-15. Rigor FULL (tags security, write-path), Opus. Reviewed by: code-review + adr-check + two adversarial passes
> (section 9). unified-access-control-r2 review: **pending** (section 10). Branch: the task worktree, based on `work/ebilling-r1`
> at `c70e6c0a0`.

## 1. What was built

- **`InvoiceFamilyCoreRecordGuard`** (new, `Services/Dataverse`, static): the one server-side owner of FR-05 for `sprk_invoice`,
  `sprk_billingevent`, `sprk_invoicesubmission`, `sprk_invoiceapproval`, `sprk_externalidentifier`, `sprk_invoicecorrection`.
  - `Check(Entity)` / `Check(entity, read)` before a write: exactly one core lookup set returns it; two or three is
    `invoice_family_multiple_core_records`; none is `invoice_family_no_core_record`; a value that is not a reference to the
    column's core table is `invoice_family_lookup_not_a_core_record` (a mis-built payload never reads as "unset"); an unknown
    table is `invoice_family_unknown_entity`. Refuses, never picks.
  - `DeriveResolverFieldsAsync` fills the five ADR-024 fields (record type, id as a lowercase-D GUID, name capped at 100,
    number, relative URL via `RegardingRecordType`). Read failure is `invoice_family_read_failed`; a missing row is
    `invoice_family_record_not_found`; a missing type row / name / number is `invoice_family_resolver_fields_incomplete`.
    A refusal passes through unchanged.
- **`CoreAncestorResolver.ResolveInvoiceFamilyCoreRecordAsync`** (stored row): probes the columns, reads only the row's own
  core lookups (plus `sprk_regardinginvoice` on an external identifier), then calls `Check`. Metadata or read failure is a
  refusal (`read_failed`), never "no core record"; NoAncestor and Error stay distinct.
- **`IntermediateRootColumns["sprk_invoice"]`** + the TypeScript `INTERMEDIATE_ROOT_COLUMNS` + `RecordContainerResolver.ChildAncestorLinks["sprk_invoice"]`
  (lockstep) now include `sprk_regardingworkassignment -> sprk_workassignment`.
- **Secure-record family**: four tables registered (section 4).
- **Creator person**: `sprk_createdbyperson` on the four tables (section 5).

Not wired into any caller (POML notes: tasks 044, 049, 100, 104 call it). The generic child-record create route is a bypass,
filed as ISS-010. **Update:** ISS-010 has since been resolved by wiring the guard into the shared write core (section 11).

## 2. Decisions taken here (technical, recorded for review)

| # | Decision | Why |
|---|---|---|
| T1 | Mixed naming maps cleanly: invoice and line use typed `sprk_matter` / `sprk_project` + `sprk_regardingworkassignment`; the four new tables use the `sprk_regarding{core}` trio (via `CoreAncestorResolver.StampColumnFor`). | Live lookups read on spaarkedev1 2026-10-15; escalation trigger 2 did not fire. |
| T2 | D-75: the line's `sprk_invoice` is never selected or read for its core record. | Tested: a line whose invoice names another matter resolves its own matter; `sprk_invoice` is not in the select. |
| T3 | D-68: an external identifier with no core lookup but `sprk_regardinginvoice` regards the **invoice**: `sprk_regardingrecordid` = invoice id, the five fields describe the invoice; never walked to the invoice's core record. A row with both a core lookup and an invoice regards the core record. | The alternate key needs a non-empty record id; walking would name a record whose lookup is empty. Note for task 044: firm invoice numbers keyed by the invoice survive resubmission (D-37 keeps the number). |
| T4 | `sprk_invoicecorrection` joins the guard (D-82 trio) and the secure family. | It holds values copied from secure invoices and lines and names one core record. |
| T5 | `sprk_billingexport` is **not** registered (the POML listed it). | Its only lookups are export documents; a batch spans many core records; the role file admits only tables a writer assigns to the Secure team, so no writer ever would. Live control: still refused for the Secure team (privilegeCount=33). The exposure question is ISS-011 (owner / uac-r2). |
| T6 | Existing data: escalation trigger 3 did not fire. | Live 2026-10-15: 10 invoices (2 with no core lookup, 8 with one, **0 with two**), 1 billing event (0 core lookups), 0 rows in the four new tables and in exports; 9 to-dos and 3 communications under invoices, none with a work-assignment stamp, so the new invoice root column changes no stored stamp. |
| T7 | The guard is static (no DI). | Nothing to swap; its one read helper takes the caller's services. ADR-010 ceiling unchanged (162). |

## 3. Files touched (uac-r2 owned files flagged **[uac-r2]**)

- New: `Services/Dataverse/InvoiceFamilyCoreRecordGuard.cs`, `tests/unit/.../InvoiceFamilyCoreRecordGuardTests.cs`, this note.
- **[uac-r2]** `Services/Dataverse/CoreAncestorResolver.cs`: + `ResolveInvoiceFamilyCoreRecordAsync`; invoice root column; two stale remarks corrected. `ChildRecordEntities`, `CoreRecordEntities`, `StampSourceColumns`, `StampedOutsideAccessTaxonomy` **unchanged**.
- **[uac-r2]** `Infrastructure/Dataverse/RecordContainerResolver.cs`: invoice WA link (lockstep with the root columns).
- **[uac-r2]** `Services/Access/SecureChildLineage.cs`: billing event + WA; invoice + WA (not `sprk_supersedesinvoice`, not `sprk_billingexport`, section 10); four new rows (submission: invoice, document, report document, resubmission-of, trio; approval: invoice, trio — **not** its Decision Record, which is stamped outside the access taxonomy; external identifier: invoice, trio; correction: invoice, line, trio).
- **[uac-r2]** `config/secure-record-owner-role.json`: four `kind: child` entries.
- **[uac-r2]** `Services/Dataverse/RecordCreatorPerson.cs` (`StampedChildTables` + 4), `scripts/Set-ChildRecordCreatorPersonSchema.ps1` (`$Tables` + 4, `$SolutionHomes` + 4 with SpaarkeMaster), `tests/scripts/Set-ChildRecordCreatorPersonSchema.Tests.ps1`.
- **[uac-r2]** `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs` (`ChildTables` + 4), `tests/unit/domain/Access/SecureChildLineageTests.cs` (+2 tests).
- **[uac-r2]** tests whose example used the invoice as "an intermediate that cannot carry a work assignment" now use the budget (same scenario): `CoreAncestorRestamperTests.RootTypeTheSourceCannotCarry_IsNeverWritten`, `ChildRecordContainerResolutionTests.StampFreshness ... RootTypeTheSourceCannotCarry_IsNotPartOfTheComparison`; + `CoreAncestorRestamperTests.InvoiceWorkAssignment_IsACopy` and `InvoiceWithoutAWorkAssignment_ClearsTheChildsWorkAssignment`; `StampWorld` invoice columns + WA; `ChildRecordContainerResolutionTests` invoice sweep + WA (live).
- `CoreAncestorResolverTests` (+1), `PolymorphicResolverService.ts` (+1 row) and its `.coreAncestor.test.ts` (+1).
- `scripts/ebilling/Deploy-Ebilling{Submission,Approval,ExternalIdentifier,Correction}Schema.ps1`: + `sprk_createdbyperson` lookup.
- Docs: `docs/data-model/sprk_{externalidentifier,invoicesubmission,invoiceapproval,invoicecorrection,billingexport}.md` (open questions answered), `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` (invariant I-19).

## 4. Secure Record Owner role (dev, spaarkedev1)

- Negative control 2026-10-15T20:37:09-10Z: POST of each table owned by team Secure Record Owners `6eabc7f9-...` returned
  HTTP 403 0x80040299 "is missing prvReadsprk_{table} privilege", `privilegeCount=29` (quotes in the role file).
- `Set-SecureRecordOwnerRolePrivileges.ps1`: dry run (29 of 33, 4 MISSING) -> `-Apply` (added at Basic, read back; platform
  re-injected the SharePoint four) -> setup guide §5.4 strip (removed `prvReadSharePointData`, `prvWriteSharePointData`,
  `prvCreateSharePointData`, `prvReadSharePointDocument`) -> `-Verify`: **PASS, 33 of 33, nothing outside the file**.
- Positive control 20:54:52Z / 20:55:19Z / 20:55:44Z: HTTP 201 for all four on 3 of 3 polls, each probe deleted at once; the
  control on `sprk_billingexport` stayed 403 with `privilegeCount=33`. Leftover check: 0 probe rows in every table.

## 5. Creator person (dev)

The four tables live only in SpaarkeMaster (created by the eBilling schema scripts), so the creator-person script lists them
with `$SolutionHomes = @('SpaarkeMaster')` (it never creates a home table's column or writes to a product solution) and the
eBilling schema scripts create the lookup (relationship `sprk_systemuser_{table}_createdbyperson`, nav `sprk_CreatedByPerson`,
delete RemoveLink, other cascades NoCascade).

- Schema scripts `-WhatIf` (1 WOULD CREATE each) -> apply: created on all four, exit 0 except the approval script's first
  verify (`does not target systemuser`: a lookup-targets read straight after the create; a re-run skipped the column and
  verified all 17, exit 0 — the module retries attribute presence but not targets; minor, same family as DEF-008 / DEF-012).
- `Set-ChildRecordCreatorPersonSchema.ps1`: dry run (4 WOULD create, as expected before the columns) -> `-Apply` (secured the
  four columns, granted both BFF profiles; column + relationship found in SpaarkeMaster via the table) -> `-Verify`:
  **PASS** (exit 0).

## 6. Tests

| Suite | Result |
|---|---|
| `Sprk.Bff.Api.Tests` (full) | see section 6a |
| Filter CoreAncestor / InvoiceFamily / ChildRecordContainerResolution / SecureRootInheritance / ParentLineage / StampedOutsideAccessTaxonomy / RecordContainerResolver / Restamp / Reconcil | 1288 passed, 0 failed |
| Filter SecureChild / RecordCreatorPerson / SecureRecordOwner / RecordOwnership / InvoiceFamily / CoreAncestor / ... | 1513 passed, 0 failed |
| `Spaarke.ArchTests` (full) | 1045 passed, 0 failed (ADR010 ceiling unchanged at 162) |
| `PolymorphicResolverService.coreAncestor.test.ts` | 52 passed |
| `Set-ChildRecordCreatorPersonSchema.Tests.ps1` (Pester) | 31 passed |

Fail-before / pass-after and seeding proofs:

- `CoreAncestorResolverTests.InvoiceFiledUnderAWorkAssignment_DerivesTheWorkAssignment` failed before the root-column change.
- TS `derivesAnInvoicesWorkAssignmentFromItsRegardingWorkAssignment`: without the TS row, 1 failed / 51 passed; restored, 52 passed.
- Seed 1, more-than-one refusal disabled (`set.Count > 99`): `InvoiceFamilyCoreRecordGuardTests` 13 failed; restored, pass.
- Seed 2, `sprk_invoicecorrection` dropped from the census: `SecureFamilyConsistencyTests` 2 failed; restored, pass.
- Seed 3, the line's WA lineage column renamed: `SecureChildLineageTests` 1 failed; restored, pass.
- Pester without the four `$SolutionHomes` rows: 7 failed (including the new 013 case); restored, 31 passed.
- `tsc --noEmit` on Spaarke.UI.Components: no error in a changed file (the pre-existing unresolved `@spaarke/auth` /
  `@spaarke/sdap-client` workspace imports only).

### 6a. Full BFF suite

Run after the last code change (the `sprk_supersedesinvoice` lineage removal): **23,346 passed, 23 failed, 20 skipped
(23,389), 48 m 58 s.** All 23 failures are WebApplicationFactory route/contract/seam tests (Chat streaming, Compose
seams, Document identity/profile, external module/project contracts, malformed multipart, drive-keyed route
retirement) under full-suite load. Their 18 classes rerun in isolation: **146 passed, 0 failed.** None touches the
invoice family, the lineage or the core-ancestor code; attributed to load, not to task 013.

An earlier full run had one real failure caused by this task:
`ExternalMultiRootListReadTests.InvoicesModule_RequiresReadOnSecureRoots_AndReadsItsAgreement`. The invoice's new
`sprk_supersedesinvoice` lineage edge reached `ExternalMultiRootRule.LiveParentLookupsOf` through
`ParentLineage.ChildFiling` and changed the external invoices module's `RootRuleAttributes`. Fixed by removing that edge
(open item 4). After the fix, the lineage-related filter (SecureChildLineage / ExternalMultiRoot / ParentLineage /
StampedOutside / InvoiceFamilyCoreRecordGuard / CoreAncestor / ChildRecordContainerResolution) gave 893 passed and
0 failed. ArchTests (full) gave 1045 passed and 0 failed.

## 7. Publish size and CVE

Short-path trees from `git archive` (`src/server`, `config`, `Directory.Build.props`, `Directory.Packages.props`,
`global.json`) of the base `c70e6c0a0` and of `a5d2516c5`; `dotnet publish -c Release -r linux-x64 --self-contained false`;
`Compress-Archive -CompressionLevel Optimal`; 192 files each (same method as task 014).

| Tree | Compressed |
|---|---|
| base (project branch before this task) | 38,939,639 B = 37.136 MB |
| head (this task) | 38,946,591 B = 37.142 MB |
| **Delta** | **+6,952 B (+0.007 MB)** |

No package reference changed (no `*.csproj` or `Directory.Packages.props` in the diff), so no new CVE source. Total stays
far below the 55 MB review threshold.

## 8. ADR / pattern text

No ADR changed. No `.claude/` edit. ADR-002 (one owner, fail closed), ADR-024 (five resolver fields), ADR-010 (no DI), ADR-034
(1-hop: the invoice WA column is a root column, the stamp stays one hop) are complied with.

## 9. Review

Two adversarial passes (security / tenant-isolation), plus a self-check against ADR-002 / 010 / 024 / 034 (section 8). The
code-review and adr-check skills were not run as separate skills; the two independent passes covered this diff.

- **Pass 1 (guard, derivation, topology).** F1: the approval's `sprk_decisionrecord` lineage edge filed a row under a Decision
  Record, which `CoreAncestorResolver.StampedOutsideAccessTaxonomy` keeps free of children (`StampedOutsideAccessTaxonomyGuardTests`:
  2 failed). **Fixed** (commit 3): the edge is dropped; a test pins its absence. K1: children of an invoice now treat their work
  assignment as a copy — a child's own WA under a matter-only invoice is cleared by the restamper and reads as stale on storage
  resolution; 0 such rows live; **pinned** by a new restamper test. K2: `sprk_supersedesinvoice` as a lineage parent also joined
  `ParentLineage.ChildFiling` and so the external invoices module's live-parent rule (`ExternalMultiRootRule.LiveParentLookupsOf`;
  the full suite caught it: `ExternalMultiRootListReadTests.InvoicesModule_RequiresReadOnSecureRoots_AndReadsItsAgreement`).
  **Resolved by removal** (commit 4): it is not a filing, and widening the external rule is uac-r2's call (section 10). K3: `Check` sees only the payload; an update must merge the stored lookups first (documented; the
  wiring tasks own it; a `CheckUpdateAsync` helper is suggested for them). K4: harmless leniencies (a reference with a blank
  `LogicalName` is accepted; the number is not length-capped; the invoice-only derivation relies on the invoice record-type
  row, which exists on dev). Clean: never picks, malformed never "unset", D-75, fail-closed reads, lowercase-D id, no access
  widening, cycle guards bounded, tests not tautological.
- **Pass 2 (secure family, creator person).** No F-class finding. K1 **deployment order**: `SecureChildShareSynchronizer`,
  `SecureChildReconciliationJob` and `SecureChildReconciler` query every lineage table and column with no presence probe, so a
  BFF build with this change in an environment whose SpaarkeMaster lacks the eBilling tables (or the invoice / line WA and
  `sprk_supersedesinvoice` columns) stops secure-child reconciliation (fail closed, no exposure). **Order rule: import the
  eBilling schema (SpaarkeMaster / the `scripts/ebilling` scripts) before deploying this BFF build to an environment**; the
  same coupling ontology task 039 introduced. A presence probe in those readers is uac-r2's call. K2: the schema scripts
  create `sprk_createdbyperson` unsecured until the creator-person script runs — **mitigated**: each script now prints the
  follow-up step. K3: not registering `sprk_billingexport` is right under the role file's rule, but the exposure is concrete
  (export CSV / JSON documents of secure invoices are ordinary-owned) — ISS-011 is a blocking input to task 150. K4: the
  census entries for the four tables are vacuous until a writer exists (the scanner lights up with 044 / 049). Clean: every
  kept lookup targets a root or codified child, none missing; no new ownership-parent table; role entries well-formed
  (lowercase privilege names match the schema names); creator-person lists consistent; no runtime stamp on these tables yet.

## 10. Open for unified-access-control-r2 / owner

1. Review every table entry (role file, lineage rows, census, creator person) and the three lockstep root-column changes.
2. ISS-011: AP export batches of secure invoices (owner decision).
3. ISS-010: the generic child-record create route should call the guard for the invoice and the line (their route).
4. Task 012 also added `sprk_invoice.sprk_supersedesinvoice -> sprk_invoice` and `sprk_billingexport -> sprk_billingexport`.
   Neither is a filing; task 013 leaves both out of the lineage, the container resolver's links and its live-sweep snapshot.
   Adding `sprk_supersedesinvoice` to the lineage would only narrow a share mirror, but it would also join `ParentLineage`
   and the external invoices module's live-parent rule (withholding more invoices from external callers) — uac-r2 to decide.
5. Deployment order (pass 2 K1): import the eBilling schema before deploying a BFF build with this change; the secure-child
   readers query every lineage table and column without a presence probe (fail closed, but reconciliation stops).

## 11. ISS-010 / #1973 resolved (unified-access-control-r2 direction, 2026-10-15)

Section 10 items 3 and 4 are answered by unified-access-control-r2. Those answers are binding, and uac-r2 reviews the write
core as code it owns. Items 1, 2 and 5 stay open (2 is ISS-011 / #1974, unchanged here; `sprk_billingexport` stays outside
the secure family).

### 11.1 What uac-r2 decided and how it was built

| uac-r2 answer | Built |
|---|---|
| Call the guard; do not refuse the tables. `sprk_invoice` and `sprk_billingevent` stay in `CreateTables` ("New Billing Event" is a ribbon create surface); `sprk_invoice` stays in `RefileTables`. | Route table sets unchanged. |
| Invoke it from the shared write core (`OwnedChildWrite` create and re-file), keyed by table, not per route. The guard owns the rule; fail closed when it cannot read what it needs. | `OwnedChildWrite.CheckInvoiceFamilyAsync`, called from `CreateAsync` (after the caller's G5 check, so AppendTo on every bound record comes first) and first in `RefileAsync` (before the re-file / ordinary-update split, so the `NotARefile` PATCH is covered too). This guards `POST /api/v1/child-records/{table}`, `PATCH /api/v1/child-records/sprk_invoice/{id}`, the chat `dataverse.create_record` / `dataverse.update_record` tools and the decision route's in-process create. The rule is `InvoiceFamilyCoreRecordGuard.CheckWriteAsync` plus `GuardedLookups`. |
| Merge stored row plus patch, once, in the write core, before any table guard. The effective lookups are the stored row's lookups overlaid with the patch. Read as the caller; fail closed on a read fault. Extend an existing reader rather than add a second one. | `OwnedChildWrite.ReadLookupsAsCallerAsync` (one `$select=_{col}_value` read as the caller) and `OwnedChildWrite.EffectiveLookups` (pure overlay: a bind replaces, a clear nulls, the rest is kept). Runs only when the patch sets or clears a guarded lookup. A read fault returns the caller's own error; a column missing from the answer is `invoice_family_read_failed`. Neither counts as "not set". |
| `sprk_supersedesinvoice` is a peer reference, not a lineage parent. On create and update the two invoices must resolve to the same core record, or the write is refused with a stable code. | `invoice_family_supersedes_core_record_mismatch`. The superseded invoice's core lookups are read as the caller. A 403 or 404 on that read gets the uniform not-found; any other fault returns the caller's own error; a superseded invoice naming no single core record is a mismatch. |

**Reader reuse (§11).** Existing stored-row readers were checked first:
- `CoreAncestorResolver.ResolveInvoiceFamilyCoreRecordAsync` reads exactly these columns, but app-only (`IGenericEntityService`). The direction says read as the caller, so it was not reused.
- The re-file core's caller reads (`CheckCallerMayRefileByPairAsync`, `KeepPairInStepAsync`) read the whole row only when the ADR-024 pair is written.

`ReadLookupsAsCallerAsync` is therefore the one caller-identity lookup reader in the write core, and it serves both reads: the stored row and the superseded invoice. Folding the pair reads into it would be a refactor of uac-r2 code, so it is left as a follow-up suggestion.

**HTTP contract** (`ChildRecordEndpoints.InvoiceFamilyRefused`):
- 400 with the guard's code as `reasonCode` (`invoice_family_no_core_record`, `_multiple_core_records`, `_lookup_not_a_core_record`, `_supersedes_core_record_mismatch`);
- 500 for `_read_failed`;
- the uniform 404 `child_record.not_found` for a record that cannot be found.

The chat tools return the guard's code as the error code.

**Ribbon (pass-1 F1).** "New Billing Event" on an invoice form posted only `sprk_Invoice@odata.bind`. FR-05 refuses that, because the line's core record is its own lookup (D-75). `sprk_secure_child_ribbon.js` (and its SpaarkeMaster mirror) now reads the invoice's three core lookups through Xrm.WebApi and adds the one bind. When the invoice is not filed under exactly one core record, it refuses client-side and sends nothing. The server still never takes a line's core record from its invoice. **The web resource must be redeployed** (SpaarkeMaster) for this to reach dev.

### 11.2 Files changed (uac-r2-owned flagged **[uac-r2]**)

- **[uac-r2]** `src/server/api/Sprk.Bff.Api/Services/Dataverse/OwnedChildWrite.cs`: `Outcome` / `RefileOutcome.InvoiceFamilyRefusal`, the two call sites, `CheckInvoiceFamilyAsync`, `EffectiveLookups`, `ReadLookupsAsCallerAsync`.
- **[uac-r2]** `src/server/api/Sprk.Bff.Api/Api/ChildRecordEndpoints.cs`: `InvoiceFamilyRefused`, plus one arm in each of the create and re-file refusal switches.
- **[uac-r2]** `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DataverseCreateRecordHandler.cs` (`OwnedCreateResult`) and `DataverseUpdateRecordHandler.cs` (`RefileRefusal`): one refusal arm each, on uac-r2 task 146's owned path.
- **[uac-r2]** `src/client/webresources/js/sprk_secure_child_ribbon.js` and `src/dataverse/solutions/SpaarkeMaster/WebResources/sprk_/scripts/secure_child_ribbon.js` (mirror, parity guard #1651): `invoiceCoreBind`.
- **[uac-r2]** `src/client/shared/Spaarke.UI.Components/src/utils/adapters/__tests__/secureChildRibbonScript.test.ts`: two invoice-host cases (3 tests) and a richer fake.
- **[uac-r2]** `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.cs`: the `ScriptedUserClient` fake answers every `$select`ed `_x_value` (null when unset, as Dataverse does), gains `GetOverride`, and gains the invoice / billing-event task 012 lookups. New partial `SecureChildOwnershipAiToolTests.InvoiceFamilyGuard.cs` (21 tests).
- ebilling-r1: `InvoiceFamilyCoreRecordGuard.cs` (`SupersedesCoreRecordMismatchCode`, `SupersedesInvoiceColumn`, `GuardedLookups`, `CheckWriteAsync`), `InvoiceFamilyCoreRecordGuardTests.cs` (+8 tests), this note, `defer-issues.md` (ISS-010), `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` (I-19).

### 11.3 Open for uac-r2 / owner

1. **Reverse supersedes (review pass 1, F1, escalated).** The forward check runs when the superseding invoice is written. Moving the SUPERSEDED invoice to another core record is not checked against the invoices that point at it. Example: Y supersedes X, both on matter A; X is re-filed to project P and accepted. Closing this needs a query for rows with `_sprk_supersedesinvoice_value eq X`. It must be app-only, because a caller-scoped query misses rows the caller cannot see, and a refusal would then reveal that an invoice the caller cannot read exists. uac-r2 / owner to choose: (a) app-only check with a non-disclosing code, (b) accept the gap and let the eBilling resubmission writer (task 044) own it, or (c) another option.
2. **Read/write race (K).** The stored lookups are read, then the PATCH goes with `If-Match: *`. Two concurrent re-files of one invoice could each pass and together leave two core records. The fix is the row's `@odata.etag` as `If-Match`, but `IDataverseUserClient.PatchAsync` has no ETag parameter today, and the same race exists for every re-file invariant.
3. **Field-secured lookups (K).** Dataverse returns a column the caller cannot read under field security as null, and the merge would read that as "not set". None of the guarded lookups is field-secured today. If one becomes so, refuse on `IsSecured` or read the stored lookups app-only after the caller's own read succeeds.
4. **Invoice wizard (K).** `CreateInvoiceWizard` can finish with no association (no host context, or a host that is not a matter or project). That is now a 400 `invoice_family_no_core_record` at Finish, which FR-05 makes correct; the wizard should require the association up front.
5. **Deployment order (K).** The invoice update read selects `_sprk_supersedesinvoice_value` and `_sprk_regardingworkassignment_value`. Import task 012's schema before deploying this BFF build, otherwise every invoice PATCH that touches a lookup fails closed with Dataverse's 400. This is the same rule as section 10 item 5.
6. **Resolver fields.** The write core does not fill the five ADR-024 fields (`DeriveResolverFieldsAsync`), because the direction did not ask for it. The eBilling writers (044 / 049 / 100 / 104) fill them.
7. **Writers outside the core.** `InvoiceReviewService` creates invoices app-only, matter-only by construction, without `OwnedChildWrite`; any new writer outside the core must call the guard itself. Existing observation, not changed: `OwnedChildWrite` treats a lookup to any ownership-parent table as a filing, so binding `sprk_supersedesinvoice` alone goes through the re-file path (`ReparentAsync`) with the superseded invoice counted as a parent for secure-if-any. That conflicts with "peer reference, not lineage"; it is uac-r2's to decide.

### 11.4 Tests, review, size

**New tests**
- 21 route and chat-tool cases in `SecureChildOwnershipAiToolTests.InvoiceFamilyGuard.cs`.
- 9 guard cases in `InvoiceFamilyCoreRecordGuardTests`.
- 3 ribbon cases in `secureChildRibbonScript.test.ts`.

**Proof each test fails before the change**
- **Seed A: guard call disabled in the write core.** 16 route and chat cases fail, every case that should refuse. The 3 positive controls pass: same-core supersedes created, matter-to-project move with the clear written, an edit that leaves the lookups alone neither read nor refused.
- **Seed B: merge does not overlay.** `EffectiveLookups` fails, and so do the refile cases, including the three census refile cases for the invoice.
- **Seed C: supersedes rule neutralised.** 6 guard cases fail, plus the route and chat supersedes cases.
- **Seed D: catch on the stored-row read disabled.** The throwing-read case fails.
- **Old ribbon script.** 3 failed, 20 passed. With the change: 23 passed.

**Suites**
- **Touched classes** (record ownership, child-record routes, invoice family, core ancestor, secure root/child, chat create/update, filing, external multi-root, parent lineage, decision route, work assignment): 2,275 passed, 0 failed.
- **`Spaarke.ArchTests`:** 1,052 passed, 0 failed. The web-resource mirror parity guard (#1651) needed the SpaarkeMaster copy updated.
- **Full `Sprk.Bff.Api.Tests`** (on the first commit, run while a review also ran tests): 23,473 passed, 65 failed, 23 skipped. Every failure is a WebApplicationFactory route, contract or seam test that timed out after 3 to 13 minutes under load. Rerun in isolation, those 46 classes gave 2,193 passed, 0 failed, 3 skipped. None touches the invoice family or the write core.

**Review: two adversarial passes (record access)**
- **Pass 1.**
  - F1 ribbon "New Billing Event" refused: **fixed** (11.1).
  - F1 reverse supersedes: **escalated** (11.3 item 1).
  - F3 five update-path branches untested: **fixed**, 5 tests.
  - K1 race (11.3 item 2), K2 field security (item 3), K4 wizard (item 4), K5 deployment order (item 5): recorded.
  - K3 exception text in a refusal: **fixed**.
- **Pass 2.** No F-class finding.
  - K1 a throwing stored-row read escaped as a generic 500: **fixed**, it now returns `read_failed` without the fault text, with a test.
  - K2 the guard's code was missing from the route warnings: **fixed**.
  - K3 `DeriveResolverFieldsAsync` still puts `ex.Message` in `Reason` (no route calls it yet): left for the eBilling writers.
  - K4 the I-19 wording called supersedes "not a filing" while the ownership core still treats it as a parent: **doc fixed** (11.3 item 7).
  - K4 an invoice may supersede itself, and A→B→A cycles are not checked: left for task 044.

**Publish size**
- Same method as section 7: `git archive` short-path trees, `dotnet publish -c Release -r linux-x64 --self-contained false`, `Compress-Archive -CompressionLevel Optimal`, 192 files each.
- Base `87ac5a821` 38,985,863 B; final head (`93949f329` code) 38,991,743 B: **+5,880 B (+0.006 MB)**. The first commit measured +5,852 B.
- No package reference changed, so there is no new CVE source. The total is about 37.2 MB, far below the 55 MB review threshold.
