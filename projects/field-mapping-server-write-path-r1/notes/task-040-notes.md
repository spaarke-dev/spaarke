# Task 040 — Push on the canonical engine (FR-23, FR-34, FR-35)

> Branch `task/fm-040` (from `dc08e14990`). Rigor FULL (code, `bff-api`, 6+ steps). Model sonnet 5.5, effort per the brief.
> Nothing here was deployed, applied or written to a live environment. The one read of a live environment was the sync-mode
> script's dry run (read-only).

## What shipped

**Server (the push route, `POST /api/v1/field-mappings/push`)**
- A push applies Copy rules whose Sync Mode is Manual Refresh, through `CreateTimeFieldMapping.ApplyCopyOverwrite` (new, the
  overwrite twin of `ApplyFillOnly`). `ApplyMappingRule` keeps no Copy value logic: it keeps the push's own gates (source field
  returned, non-null, Copy only, the Strict type matrix, lookup bind plan) and the two translations in `FieldMappingPushValues`
  (OData JSON value to the engine's typed value and back). Other rules are counted in `skippedRuleCount`, never an error.
- `ReparentAsync`, the root filing gate, the restamp and `AfterRefileAsync` are unchanged. The #2169 / D7 T7a skip of a document's
  access columns stays in the per-rule loop; `ApplyMappingRule` does not know about it (test below).
- Per-child failure text is fixed (`ChildUpdateFailedMessage`, `RuleFailedMessage`, and the re-file refusal carries its code only);
  the detail goes to the log with a correlation id (#1437, ADR-019).
- FR-34 members: `preview`, `includeAssignedTo`, `childIds`, `kind` (request); `preview`, `changes`, `skippedNoWriteCount`,
  `truncated`, `skippedRuleCount` (response). See the design in `docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md`.
- DI-002: `AuthorizePushSourceAsync` asks the shared `MappingSourceReadProbe.CheckCallerReadOfSourceAsync` (the
  `AuthorizationGateTimingTests` push case stays green).
- `RecordFamilyReader.ReadChildrenAsync` / `HasListedEdge` (one level, caller-scoped, primary-parent rule, DetailTables skipped).

**Script, seeder, docs (FR-34 scope: sync mode; DI-003)**
- `scripts/Set-FieldMappingRuleSyncMode.ps1` is class-aware. Dry run is the default; `-Apply` was NEVER run.
- The provisioning seeder writes `sprk_syncmode` explicitly (see Decisions, item 3).
- Deployment guide section 10.6 (the upgrade step), scripts README entry, framework doc updated.

**Client (FR-35)** — new files only: `recordFamilyPushService.ts`, `RecordFamilyActionPanel.tsx` (+ exports in the folder's
`index.ts`). The panel runs preview on open (writes nothing), shows current to new with the Secure/Restricted/Limited flags,
and applies only on the confirm button, with `childIds` = exactly the previewed records.

## Decisions

1. **Staged child discovery.** An ordinary request (no `preview`, no `childIds`) still finds children through the existing
   `ResolveParentLookup` path, because the brief requires the existing push contract and re-file guard tests to pass unchanged
   and they assert that discovery. A request that sets `preview` or `childIds` finds children through the Filing edge
   (`RecordFamilyReader.ReadChildrenAsync`), as FR-34 asks. Replacing the ordinary path is a follow-up that must change those
   tests; it is not done here.
2. **Existing tests changed, and why (the only ones).** `FieldMappingPushAuthorizationContractTests.PushHost.Rule` sets
   `SyncMode = ManualRefresh` (FR-23: a rule at the default One-time is no longer pushed, so the fixture must say Manual
   Refresh). `FieldMappingRuleProjectionTests`: the "Default rule with every new field populated" case is now a skipped rule
   (FR-23). `InvoiceFamilyGenericUpdatePathTests...AnyOtherDataverseFault...` asserted the leaked exception text; it now asserts
   the fixed text (the #1437 acceptance criterion). No other existing test was edited.
3. **DI-003 vs the class-aware amendment.** DI-003 (2026-10-21) says seed Manual Refresh on seeded Copy rules. The owner's
   later class-aware amendment (2026-10-27) says Assigned-To targets stay 0. All 20 seeded rules target Assigned-To columns
   (the attorney matrix), and a push applies an Assigned-To rule only on an opt-in request whatever its Sync Mode, so the seeder
   writes `sprk_syncmode = 0` explicitly (never the column default). A seeded rule of another class would take 1: the constant
   and the test are marked for that. If the owner wants 1 anyway it is a one-line change plus the test.
4. **The #2188 caller-mask guard.** `ApplyCopyOverwrite` is a second engine entry that takes a source, which that guard forbids
   by design. The guard now scans `ApplyCopyOverwrite` as an engine call, pins it as internal, and lists the push's one call
   site (`FieldMappingEndpoints.ApplyMappingRule`) as a ratcheted, reasoned exemption: the push reads its source AS THE CALLER
   through the impersonated query, so field security is already applied. **Needs UAC-r2 sign-off** (a security guard changed).
5. **`PushMayWrite`** (the preview's "would the loop write this column" check) lives in `FieldMappingEndpoints` beside the loop,
   not in the new family file: the #2257 guard allows `WriteIdentity.Caller` only there, and the D-204 guard wants every method
   that asks the fixed-after-create check to ask the writer-only-table check too (it does).

## Section 11 answers (new surface)

| Surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| Request/response members on `/push` | The same route and DTOs | Extended in place; no new endpoint | No preview or confirmation, so a push changes records the person never saw |
| `CreateTimeFieldMapping.ApplyCopyOverwrite` | `ApplyFillOnly` (same `ApplyAll` core) | A parameter-only twin: Copy only, overwrite, no caller-supplied snapshot | The push keeps its own Copy logic and the two paths drift (the bug class FR-23 removes) |
| `FieldMappingPushValues`, `PushRuleSelection`, `FieldMappingPushFamily` | `ApplyMappingRule`, `FieldMappingPushLookups` | New files in the same folder: the translations, the rule choice and the preview each have one job; adding them to the 1,800-line endpoint file would bury the route | Without them the push keeps its second Copy implementation, rule choice by hand, and no preview |
| `RecordFamilyReader.ReadChildrenAsync` / `HasListedEdge` | `ReadAsync` (full walk) | Added to the reader that owns the Filing edge model | The push would keep naming a parent lookup per profile, which cannot find a regarding-pair child |
| Class-aware script, parity tests | The task-005 script | Same script, class decided per rule | Plain data stays at the default and never refreshes, or Assigned-To/filing rules refresh by mistake |
| Client service and panel | `RecordFamilyModal` (read-only, hands rows to a host) | New files beside it; the modal is untouched | The modal's two actions stay disabled |

BFF hygiene (CLAUDE.md section 10): no new endpoint, DI registration, package or background work. The publish-size delta and
CVE check are the main session's (`publish-delta` was not run, per the brief); no package reference changed.

## Tests

Failed first, then passed (the old behaviour is copied back to prove it):
- `PushCopyParityTests` (text, option set, date, lookup, money) plus the projection and #1437 cases: 9 failed against the old
  `ApplyMappingRule`, all pass now.
- `FieldMappingPushFamilyContractTests` (15): 9 failed against the old endpoints (preview, per-id refusal, kind, Assigned-To
  403, no-edge 409, rule skipping), the rest are controls. All 15 pass.
- ArchTests: the first full run had 3 failures caused by this task (#2188 second engine entry, #2257 `WriteIdentity.Caller`
  outside the endpoint, D-204 writer-only check). Fixed (decisions 4 and 5); second full run 1974 of 1974.
- Provisioning seeder test `SeedAsync_EveryRuleCarriesAnExplicitSyncMode_OneTimeForTheAssignedToMatrix` failed without the
  line; the class has 9 of 9.
- Client `RecordFamilyActionPanel.test.tsx` (new file, so nothing to fail before); a mutation that drops `childIds` from the
  apply fails 2 of its tests.

Final counts (after the last change):
- `Sprk.Bff.Api.Tests`, `--filter` on the push, rule-projection, family-reader, authorization-gate-timing, re-file, invoice-family,
  secure-root, create-time, search-index, core-ancestor, caller-probe, source-read-probe, route-authorization and sync-mode-class
  classes (never the whole project): 2826 passed, 0 failed.
- `Spaarke.ArchTests` (rebuilt, whole project, after the last change): 1974 passed, 0 failed.
- `Sprk.Provisioning.ControlPlane.Tests` seeder class: 9 passed. Script `-SelfTest`: PASS (37 checks).
- `Spaarke.UI.Components` `src/components/RecordFamilyModal`: 2 suites, 41 passed (26 existing, 15 new). ESLint clean; prettier
  clean; `tsc` reports no error in these files (its other errors are unresolved workspace packages in this checkout).

`CallerChecksConcurrencyTests.TheWholeCheck_TakesItsLongestChain_NotTheSumOfItsReads` (task 049) is wall-clock sensitive: it
failed once in a loaded full run and passes 18 of 18 on its own and in the final run; not touched here.

## Sync-mode dry run (read-only, `spaarkedev1`, 2026-11-06)

40 Copy rules read: 28 are Assigned-To targets currently at Manual Refresh (they would DEMOTE to One-time), 12 are plain data
already at Manual Refresh, 0 would migrate, 0 filing, 0 inactive, 0 unknown. No behaviour changes either way: an ordinary push
never applies an Assigned-To rule whatever its value. The owner reviews this before any `-Apply`; none was run.

## What task 075 must change to fit 040

- Host the panel from the modal's `onAction`: render `RecordFamilyActionPanel` with `source` = the record the ribbon was used
  on, `rows` = what `onAction` was given (keep the array identity stable: the preview runs when it changes), and the same
  `authenticatedFetch`. Without `onAction` the modal's two actions stay disabled.
- Remove any direct call to `/push` and the `openConfirmDialog` path in `sprk_fieldmapping_push.js`; the panel is the
  confirmation (the checkbox wording "Update all related 'Assigned To' contacts" is the "Update Assigned To" action).
- The server finds children one Filing level below the source. Rows deeper than that are refused per id with a fixed text, so
  offer the push actions for direct children (depth 1 of the anchor) or accept the refusals.
- Nothing is pushed on open; 075's acceptance "no push unless the person confirms rows" now means: the preview (no writes) on
  open, the apply on the confirm button.

## Known limits

- "Update Assigned To" applies the Assigned-To rules in addition to the ordinary Copy rules (the server member is
  `includeAssignedTo`, which adds). The preview shows every value, so nothing is applied that was not shown. An Assigned-To-only
  request would need a new server member.
- Ordinary requests still use the legacy parent-lookup discovery (decision 1).
- The preview reads current values in chunks of 50 children as the caller; a family at the 500 cap costs 10 reads. Push
  performance in general is #2098 (out of scope, not fixed).
- `sprk_fieldmapping_push.js` (the ribbon script) is not changed here; it is 075's.
- The class tables in the script are copies; `SyncModeMigrationClassDataTests` fail the build when they drift.

## Defects found

- None outstanding from this task. The two wall-clock sensitive tests above and the ArchTest guards are reported, not changed.

rework: 3 (the three ArchTests guards fixed after the first full run)
