# DEF-061 - the server-written publish stamp on a Procedure Version

Branch `task/ebilling-def061` (base `d8c15dceb2`). Owner approved the schema change on 2026-11-09. GitHub issue #2640. Status: code and script done and tested; nothing has been written to any environment. The live steps are the coordinator's (section 8).

## 1. What changed

| Area | Change |
|---|---|
| Column | `sprk_procedureversion.sprk_publishedon`, Date and Time, optional, no default, not field-secured. Name constant `ProcedureEntity.VersionPublishedOnColumn` (`ProcedureService.cs:53`). |
| Writer | `ProcedureService.PublishVersionAsync` adds the stamp (the server's UTC now, from `TimeProvider`) to the version's update in the same `ExecuteTransaction` that makes it Active (`ProcedureService.cs:805-808`). It is the only writer. |
| Re-publish | A stamp that is already set is never moved (decision below). |
| Generic writers | `RecordTableRules` has a new `sprk_procedureversion` entry (`RecordTableRules.cs:364-371`): the column is in `WriterOnlyColumns` and `MappingProtectedColumns`, and the table's generic create stays refused (`GenericCreateOnlyRefused`). |
| Repair | `sprk_procedureversion` joins `AppendOnlyTables` in `RecordCreationService.Repair.cs`, so adding the table to the registry does not start repairing it. |
| Resolver | `ProcedureResolver.ReadVersionsAsync` selects the column and passes it to `ProcedureVersionRow.PublishedOn`; `OpenedAWindow` reads it (section 3). |
| Solution source | `Entities/sprk_procedureversion/Entity.xml` carries the attribute. |
| Script | `scripts/ebilling/Deploy-EbillingProcedureVersionPublishedOn.ps1`: `-WhatIf`, plain, `-Verify`, `-Backfill` (with `-WhatIf`). |

### Decisions

- **UserLocal, not TimeZoneIndependent.** The value is an instant: the moment the server published, stored in UTC. UserLocal stores UTC and shows it in each viewer's zone. TimeZoneIndependent stores a wall-clock literal with no zone, which is right for a date such as an effective-from and wrong for a moment in time. Nothing converts the stamp, so there is no cost to UserLocal. `-Verify` and the agreement test pin it, and a conflicting existing column stops the script with exit 2.
- **A re-publish does not move a stamp.** `PublishVersionAsync` refuses everything but a Draft, so a published version cannot be published again. The only way to reach the writer with a stamp already set is a Draft that was stamped some other way (for example a restored row). In that case the stamp is kept: "when it was first published" is the fact the resolver needs, and the first value is the evidence. A test pins it.
- **Not on the constructor path of any other writer.** The Draft-creation, edit and retire paths do not touch the stamp (`EveryOtherVersionWrite_LeavesTheStampAlone`).
- **No form change.** No form shows Procedure Versions in this repo, so the ForceReadOnly pattern has nothing to attach to. The column is `ValidForForm`, so a future form must add it read-only; the writer-only refusal already blocks an edit through any generic write path.
- **Why the registry entry, not a one-off check.** `WriterOnlyColumns` is the existing mechanism (the AP export claim, task 165d): `InheritedAccessPermission.RefuseAppOnlyWrite` asks it for every identity, so the Update Record node, a playbook's output mapping, the field-mapping push and the chat update tool are all refused with the `changed_only_by_its_writer.column` reason. Registering the table would also have made Repair operate on it, so the table is in Repair's skip set.

## 2. An absent column is unreadable, not "not published"

The resolver reads a Dataverse `Entity`, which omits a null column, so the row alone cannot tell "the column is missing from this environment" from "the column is empty". The query can: the version select names the column, and an environment without it rejects the query. That fault is the existing fail-closed path (the resolution faults, nothing resolves as not-published). `AnAbsentStampColumn_IsUnreadable_NotNotPublished` proves it with a Dataverse double that rejects the query, with a control over an environment that has the column. A null on a returned row is a real "not stamped".

This is also why the order in section 8 matters: the BFF build that selects the column must not meet an environment that lacks it.

## 3. Resolver, before and after

**Before.** A version opened a window when it had an effective-from and was Active or Superseded, or was Retired and had at least one Published policy version under it (publish marks the Accepted rules Published in the transaction that makes the version Active). That evidence misses a Retired version that governed only rate cards, or published with no Accepted rule.

**After.** A version opened a window when it has an effective-from AND the stamp. The old policy-version evidence is kept only as the fallback for a Retired version whose stamp is empty.

| Status | Effective-from | Stamp | Policy evidence | Opened a window |
|---|---|---|---|---|
| Active or Superseded | set | set | any | yes |
| Active or Superseded | set | empty | any | no (the backfill stamps these) |
| Retired | set | set | any | yes |
| Retired | set | empty | Published policy version | yes (fallback) |
| Retired | set | empty | none | no |
| Draft | any | empty | any | no |
| any | empty | any | any | no |

Consumers are unchanged: `NotApplicableOn` (the F2-d first-opened date) and the gap-kind test in `ByDate` read `OpenedAWindow`. The D-203 semantics are not changed beyond the stamp.

Every select that feeds the resolver includes the column: the only construction site of `ProcedureVersionRow` is `ReadVersionsAsync`, and `EveryProcedureVersionRead_SelectsThePublishStamp` pins its column set. `ProcedureService.VersionColumns` also lists it, so the publish write sees the current stamp.

For ontology-r1's review: only `ProcedureVersionRow.OpenedAWindow` and its constructor changed (`ProcedureResolver.cs:1338-1362`) plus the select (`:1090`, `:1107`).

## 4. Backfill rule

`-Backfill` stamps every Active and Superseded version whose stamp is empty. In order of evidence:

1. **Audit.** The earliest Dataverse audit row of that version that set `sprk_status` to Active (the publish write). Best effort: if audit is not enabled, not readable by the operator, or holds no such row, the version falls to rule 2 and the run says so.
2. **Effective-from at 00:00 UTC.** The version has been in force from that date, so the stamp never claims a publish later than the version's own window start. It can be later than the true publish (a version published before its start date), which changes nothing: the resolver reads only "not empty".

Why not simply use the effective-from for all: the audit value is the true publish time when it exists, and it costs one query per version. The VALUE is informational, so the choice was made for honesty, not for the resolver.

Also:

- A version with no effective-from cannot have opened a window: it is reported, not stamped.
- **Retired versions are left alone.** A Retired row cannot tell a version that was published and then withdrawn from a Draft that was discarded unpublished. Stamping both would turn every discarded Draft into a gap (D-203 (9)). The resolver keeps the policy-version evidence for them.
- A status that reads empty is a masked read: the run stops with exit 1 and writes nothing.
- Each row is re-read just before its write and skipped if it was stamped meanwhile (a publish never loses to the backfill). The write is a PATCH of the stamp alone.
- Re-runnable: it stamps only empty stamps. `-Verify` fails while any Active or Superseded version is unstamped.

## 5. Dry-run output (dev, `https://spaarkedev1.crm.dynamics.com`, 2026-11-10)

`pwsh scripts/ebilling/Deploy-EbillingProcedureVersionPublishedOn.ps1 -WhatIf` (exit 0):

```
1. sprk_procedureversion.sprk_publishedon (Date and Time, user local, optional, no default)
  WOULD CREATE sprk_publishedon (DateTime)
Publish
  WhatIf: publish skipped
created : 1  sprk_procedureversion.sprk_publishedon
changed : 0
skipped : 0
WhatIf run: nothing was written.
```

`... -Backfill -WhatIf` (exit 0; the column does not exist yet, so the run counts every Active and Superseded version as unstamped):

```
versions read                      : 0
  Active                           : 0
  Superseded                       : 0
  Retired (left alone)             : 0
  Draft (nothing to stamp)         : 0
Active/Superseded already stamped  : 0
Active/Superseded with no from     : 0 (reported, not stamped)
would stamp                        : 0  (audit evidence 0, effective-from 0)
```

`... -Verify` (read only, exit 1 as expected before the plain run): `GAPS: sprk_procedureversion.sprk_publishedon does not exist`.

Dev holds no Procedure Version rows readable by this operator, so the backfill counts are zero and the audit-evidence parsing could not be exercised live; the audit query is syntactically valid (the run did not fault) but its `changedata` parsing is untested against real audit rows. The coordinator's dry run on an environment with versions is the first real exercise (section 8, step 3); read its audit-versus-effective-from split before the writing run.

## 6. Tests

| Area | Tests (all new unless noted) |
|---|---|
| Publish | `ProcedureServiceTests`: stamps with the server's UTC now in the same transaction; a failed transaction leaves no stamp; a non-Draft publish is refused and keeps its stamp (theory); two publishes in a row stamp once; a Draft that already has a stamp keeps it; every other version write leaves the stamp alone. |
| Resolver | `ProcedureResolverTests`: stamp plus From opens (truth table theory); a null stamp on Active or Superseded does not; Retired with a null stamp falls back (opened and not-opened); the version select pin; an absent column is unreadable. Existing helpers (`AddVersion`, `ProcedureKit`) stamp Active and Superseded versions by default so the older tests keep their meaning. |
| Generic-writer refusal | `ProcedureVersionPublishedOnWriterOnlyTests` (copy of `BillingExportClaimWriterOnlyTests`): refused for the app-only and caller identities in six key forms; the Update Record node, output mapping and field-mapping push refused with nothing written; must-not-fire cases (an ordinary column, a neighbouring name, the same name on other tables, a null table, an unregistered table, the AP export claim untouched); Repair skips the table with no read and no write. |
| Agreement | `ProcedureVersionPublishedOnAgreementTests` (pattern of `TrackingNumberPreviousSequenceColumnAgreementTests`): script constants, `Entity.xml` attribute and the BFF constant agree; script behaviour pins each with a seeded drift; the script never uses the tenant-wide publish; a source scan pins that only `ProcedureService` assigns the stamp. |
| Updated | `RecordTableRulesTests` (two registry pins), `BillingExportClaimWriterOnlyTests` (the writer-only table list), `BillingFactLoaderProcedureTests`, `InvoiceSubmissionHoldPrecheckTests` (seed the stamp on their Active version). |

No test beyond the required set was added except two: `Publish_WhenTheTransactionFails_LeavesNoStamp` (proves "same write" rather than a second write) and `EveryOtherVersionWrite_LeavesTheStampAlone` (proves "it is the only writer" on the service side).

Counts and the fail-before run are in section 9.

## 7. Field security: not added, and probably not needed

DEF-061 adds no field security, as scoped. The stamp is a fact the BFF writes with the application user, and the refusal above stops every generic writer for every identity, so a masked read is the only remaining risk: if a profile ever secured the column, a caller without the profile would read "empty", which the resolver treats as not published. That is why the column is created not secured and why `-Verify` prints a note if it finds the column secured. The owner can decide later whether a direct Dataverse edit by a user with table-level write is a risk worth securing; if so, the BFF application user would need the profile with read and update on the column, and the resolver's reader would need the same.

## 8. Live commands for the coordinator (in this order)

```
pwsh scripts/ebilling/Deploy-EbillingProcedureVersionPublishedOn.ps1                 # 1. creates the column, publishes the entity (writes)
pwsh scripts/ebilling/Deploy-EbillingProcedureVersionPublishedOn.ps1 -Backfill -WhatIf  # 2. read only: counts and the evidence split
pwsh scripts/ebilling/Deploy-EbillingProcedureVersionPublishedOn.ps1 -Backfill       # 3. stamps Active and Superseded versions (writes)
pwsh scripts/ebilling/Deploy-EbillingProcedureVersionPublishedOn.ps1 -Verify         # 4. must exit 0
```

Then deploy the BFF build that carries this change. Do not deploy the BFF first: a build that selects the column fails closed against an environment without it, and a build deployed before the backfill reads every unstamped Active or Superseded version as not having opened a window.

Also for the coordinator:

- ontology-r1 review of the resolver change (section 3).
- Follow-up noted, not changed here (out of scope): `ScopeLockedAsync` in `ProcedureService` still decides "locked" from its own evidence; whether it should read the stamp is a separate ruling.
- Rate-card evidence (the known gap of the old fallback for Retired versions) is out of scope; the stamp removes it for every version published after this change.

## 9. Verification

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 errors, 0 warnings |
| New tests (81 cases across publish, resolver, writer-only refusal and agreement) | 81 passed, 0 failed |
| Fail-before (the four changed production files restored to base `d8c15dceb2`, plus a three-line shim so the tests compile: the column constant, the `Clock` property, the unused `PublishedOn` record parameter) | 37 failed, 44 passed of the same 81 (the 44 are the must-not-fire controls and cases that hold with or without the change) |
| Restore check | SHA-256 of `ProcedureService.cs`, `ProcedureResolver.cs`, `RecordTableRules.cs`, `RecordCreationService.Repair.cs` equal before and after the fail-before run |
| `Sprk.Bff.Api.Tests` filtered on Procedure, Policy, RateCard, Rule, Finance, RecordTableRules, Mapping, BillingExportClaim, RecordRepair, Billing | 11772 passed, 0 failed, 6 skipped (skips pre-existing) |
| Full `Spaarke.ArchTests` | 2200 passed, 0 failed |

Fixtures that seeded an Active version without the stamp and so needed it (they read the version through the resolver): `RateProcedureWorld.Version`, `InvoiceResolutionServiceTests.VersionRow`, `BillingFactLoaderProcedureTests`, `InvoiceSubmissionHoldPrecheckTests`, `ProcedureKit`, `ProcedureResolverTests.AddVersion`. This is the intended contract change: an unstamped Active version no longer opens a window.

## 10. Component justification (root CLAUDE.md section 11)

**The column `sprk_procedureversion.sprk_publishedon`**

1. Existing: it overlaps with `sprk_effectivefrom` (a date the author chooses, not when the server published) and with the Published policy versions under a version (indirect evidence that misses a version that had no rules); `sprk_procedureversion` has no field that records the publish.
2. Extension: no existing column can carry it, because an effective-from is author-controlled and can be set in the past or future, and deriving it from policy versions is the weakness this closes; adding a column is the smallest change.
3. Cost of doing nothing: a Retired version that governed only rate cards (or had no Accepted rule) reads as never having opened a window, so work dated in its window is reported against the wrong gap kind and the F2-d first-opened date can be wrong (D-203).

**The script `Deploy-EbillingProcedureVersionPublishedOn.ps1`**

1. Existing: it follows `Deploy-EbillingSubmissionPreviousSequenceColumn.ps1` and `Deploy-EbillingSentToApDateColumn.ps1` and reuses `EbillingSchema.psm1` for every Dataverse call.
2. Extension: those scripts each own one column and have no backfill; this one adds the backfill and the verify gate that depends on row data, which a generic helper cannot know, so it is a sibling, not a flag on another script.
3. Cost of doing nothing: no environment gets the column or the backfill, and the BFF build that reads it faults on every Procedure resolution (fail closed) or reads every old Active version as not published.
