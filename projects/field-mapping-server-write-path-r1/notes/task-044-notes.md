# Task 044 — the Signal / Decision Record core pair is written whole, only by the writers

> Branches: `task/fm-044` (design), `task/fm-044-build` (the first build: field security), `task/fm-044-fix` (this
> scope). Rigor FULL. Step mode prescriptive.
> **Status (2026-11-06): re-scoped and built on `task/fm-044-fix`.** Nothing was written to any environment; the dev
> checks were read-only.

## 0. Scope (owner ruling 2026-11-06; ontology D-210)

The core pair (`sprk_corerecordtype` + `sprk_corerecordid`) on `sprk_signal` and `sprk_decisionrecord` gets **no field
security**. This revises 042c's "task 044 covers the paths outside the server: field security plus a repair-pass
backstop" (decisions.md, "Owner rulings: 044 scope, ..."). Ontology-r1 and UAC-r2 agreed.

The pair is written whole by the server writers only (D-204: the general create refuses these tables; the writers
write both halves or neither, `CorePairTypeImpliesIdGuardTests`). Outside the server, an edit is detected and traced,
not blocked:

| Part | Where |
|---|---|
| An exact allow-list of roles that may create or write Signals and Decision Records, and of who may hold the service roles (D-204, D-210) | `tests/Spaarke.ArchTests/SignalWriterRoleGuardTests.cs` (role files); the live check is read-only from the operator's session (§4) |
| Column auditing on both pair columns of both tables | confirmed read-only on dev (§5) |
| Task 052 reports a half pair and a pair changed after create, and writes nothing | `tasks/052-repair-pass-scheduled-job.poml` (§6) |
| Signal access fails closed when the pair cannot be confirmed | `SignalCoreRecordAccess`, `SignalReadService` (§2) |
| The pair is not field-secured, and no profile names it | `tests/integration/data-mutation/Signals/CorePairFieldSecurityAgreementTests.cs` (§3) |

The first build (`task/fm-044-build`: a field-security script, profile rows, `IsSecured` in both `Entity.xml` files, a
Pester suite and its CI entry) is reverted on `task/fm-044-fix` with a new commit; no history was rewritten. It was never
applied to any environment, and dev reads back `IsSecured` false on all four columns (§5).

## 1. Commits on `task/fm-044-fix`

1. The Signal access check confirms an absent core pair app-only (§2).
2. Revert of the field-security build.
3. The agreement test pins the unsecured state; the role allow-list guard (§3, §4).
4. This note, the 044 POML and 052's 044(d) line.

## 2. Signal access fails closed when the pair cannot be confirmed (ontology D-210)

The access check reads the pair app-only when the caller's read returns neither half, and denies when a pair exists.

- **Per-Signal routes** (`SignalCoreRecordAccess.AuthorizeAsync`; the decision-plan, suppression and commit routes use it).
  When the caller's read returns neither half, the BFF reads the two pair columns of the Signal through the existing
  app-only `IGenericEntityService`. A pair, whole or half, is the same denial as an unreadable core
  (`SignalAccessDecision.NotFound`, the uniform 404): the route is not an existence oracle. An app-only read that fails
  (fault, throttle, no row) also denies. Only an app-only read that succeeds with neither half takes the no-core path.
  Cancellation propagates.
- **Worklist** (`SignalReadService`, step 6b). The Do-lane rows whose caller-read row has neither half are confirmed by
  one batched app-only query: an `in` filter on `sprk_signalid`, selecting the two pair columns, `IdChunk` ids per query,
  never one read per row. A row with a stored pair, or a row the read does not return, is left out, as an unreadable
  core's row is. When the query fails, every no-core row is left out and core rows stay (fail closed). Decide-lane no-core
  rows were never listed (D-35), so they are not re-read.
- **Events.** `SignalAccessEvents.CorePairPresentAppOnly` (50330) and `CorePairAppOnlyReadFailed` (50331), Warning. Both
  are logged in a scope whose only fields are `SignalId` and `Lane`; the message text carries no id and no column value.
- **Shape of the denial.** Per-Signal routes: the uniform 404 (Decide and Do alike). Worklist: the row is not listed,
  which is the worklist's existing shape for a Signal it cannot show (an unreadable core is not listed either).

Tests (failed first, then pass):

| Test | Before the change | After |
|---|---|---|
| `tests/unit/domain/Signals/CorePairAppOnlyReadTests.cs` (10) | 8 failed (re-read disabled) | 10 pass |
| `SignalReadRouteTests`: one batched read, a hidden pair (whole / type / id) left out, a failed read leaves out the no-core rows | 4 of 5 failed (drop disabled) | 5 pass |

The existing no-core harnesses (`DoLanePrivacyTests`, `DecisionActionCatalogTests`, `SignalSuppressionTests`,
`DecisionCommitServiceTests`) answer the app-only read with "no stored pair" (`CorePairAppOnlyFakes`), so their
behaviour and assertions are unchanged.

The inventory of every read of the pair done as the signed-in user, with a verdict for each, went to ontology-r1 and
UAC-r2 with the task report; it is not repeated here.

## 3. The pair is not field-secured (agreement test)

`CorePairFieldSecurityAgreementTests` now pins the ruling: every `Entity.xml` of the two tables keeps `IsSecured` 0 on
both columns, and no field-security profile in any solution names either column. It fires on each of 8 seeded drifts
(each cell secured; each cell named by a profile) and on a missing column; it passes on the committed files. Against the
reverted secured XML, 9 of its 10 tests failed.

## 4. Role allow-list (D-204, D-210)

`SignalWriterRoleGuardTests` checks four sets separately — Create and Write on each table — at any depth, over every role
file in any `Roles` folder of any solution (a role file does not say whether users or teams hold it, so both are covered).
Each set must EQUAL its allow-list: an extra role fails, and an allow-listed role that the repo defines and that no longer
holds its privilege fails. The allow-lists are constants in the test, and role names appear only there. Must-fire
controls: an extra role gains Create or Write on either table (several roles and depths), a role in another solution's
`Roles` folder, the writer gaining Write on Decision Records (append-only depends on its absence), and the writer losing a
privilege it must hold. Must-not-fire: today's role files.

It complements 046b's `MustNotHold` (person roles only, with the cleanup role excepted): this guard also covers the app
users' role and excepts no one.

Only roles the repo defines are in its role files; the platform roles on the allow-lists and the holders of the service
roles are live state. They are checked read-only from the operator's session against the same allow-lists.

## 5. Column auditing and field security on dev (read-only, 2026-11-06)

Read with the operator's `az` identity, GETs only:

| Read | Result |
|---|---|
| `organizations.isauditenabled` | true |
| `sprk_signal` and `sprk_decisionrecord` entity `IsAuditEnabled` | true, true |
| `IsAuditEnabled` on the four pair columns | true on all four |
| `IsSecured` on the four pair columns | false on all four |

Nothing was turned on or changed.

## 6. Task 052's 044(d) line (report only)

052's background and acceptance criteria now say: for Signal and Decision Record rows created after go-live, the pass
reports, and never writes, any row that holds one half of the pair only and any row whose pair changed after create (from
the column audit history of the two pair columns); it writes nothing to these tables (D-204, D-210). The wording goes to
ontology-r1 for agreement with this branch.

## 7. Grep proof — no client writes either column (from the design step, unchanged)

Run from the repo root on `cb61ddf317`:

```
grep -rn -i "corerecordtype\|corerecordid" src/client src/solutions \
  --include=*.ts --include=*.tsx --include=*.js --include=*.jsx | grep -v -E "node_modules|/dist/|/out/|/bin/|/obj/"
```
Result: 5 files, all types, comments or labels; none is a write. A second search for creates or updates of
`sprk_signal` / `sprk_decisionrecord` in client code found none: the client surfaces that show Signals or Decision
Records read them through the BFF. No form or view of either table in the solution source names either column.

## 8. Placement (CLAUDE.md §10)

A modification of two existing Signals services in the BFF: one app-only read each, through the existing
`IGenericEntityService`. No new endpoint, service, DI registration, package or background work; publish delta ≈ 0 (not
measured in this fix round, per the brief). The new event ids live in the existing `SignalCoreRecordAccess.cs`.

## 9. Known limits

- **Live holders and platform roles** are not in the repo, so the repo guard cannot see them; they are checked read-only
  from the operator's session (§4).
- **052's report** is specified, not built; it is task 052's work.
- **Model-driven grids** show the pair as stored; nothing there writes it under the allow-list.
