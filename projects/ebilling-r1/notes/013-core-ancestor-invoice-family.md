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
filed as ISS-010.

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
