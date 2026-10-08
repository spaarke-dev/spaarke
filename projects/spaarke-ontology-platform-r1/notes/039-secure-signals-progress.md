# 039: Signals and Decision Records as secure children: progress and completion record

> **Date**: 2026-10-07/08 · **Rigor**: FULL · **Status**: code done, two PRs/branches pushed; the deployed-BFF part of the live gate waits on a dev deploy (owner decision, see section 7)
> **uac-r2 review**: accepted on #1355 (https://github.com/spaarke-dev/spaarke/issues/1355#issuecomment-6047438504), conditions A and B. PR linked on #1355: https://github.com/spaarke-dev/spaarke/issues/1355#issuecomment-6049869568

## 1. The split, and why

| Part | Where | Why there |
|---|---|---|
| `SecureChildLineage.cs` + `config/secure-record-owner-role.json` (uac-r2-owned) | **PR #1390 to master**, branch `feat/secure-child-signals-039`, worktree `C:\wts-039` | Pure data. Valid on master by itself: both tables and every listed column exist in spaarkedev1, the only running environment (`config/environments.json`: demo is Stopped). Once deployed, uac-r2's existing reconciler re-owns any Signal/DR filed under a Secure root and mirrors its sharees, with no ontology code involved. uac-r2 condition B: one PR for both files. |
| `SignalWriter` resolver path | **Ontology branch**, pushed as `stream/039-signal-writer` (worktree `C:\wts-039w`), already merged with `origin/docs/ontology-platform-design` @ `2a75033ad`+18. **Not yet merged into `docs/ontology-platform-design`**: that is the main session's call | `Services/Signals/**` exists only on the ontology branch |

Hazard recorded in the PR: a master BFF built after #1390 must not reach any environment that lacks the ontology schema until #1378 is fixed. uac-r2's recent-changes listing for all secure child tables sits in ONE try, so a missing table or column fails the pass for every table. Deploy order per environment: (1) schema, (2) roles (+ Assign/Share for a non-sysadmin BFF identity), (3) BFF.

## 2. uac-r2 files read (origin/master @ `0ea74d1c3`, fetched 2026-10-07)

| File | Last commit |
|---|---|
| `Services/Access/SecureChildLineage.cs` | `d254d7166` |
| `config/secure-record-owner-role.json` | `d254d7166` |
| `Services/Dataverse/RecordOwnershipResolver.cs` (I-6) | `d254d7166` |
| `Services/Dataverse/CoreAncestorResolver.cs` | `d254d7166` |
| `Api/Filters/RecordRouteAccessAuthorizationFilter.cs` | `d254d7166` |
| `Infrastructure/ExternalAccess/CallerRecordAccessProbe.cs` | `428c5bfae` |
| `Infrastructure/ExternalAccess/ExternalCallerContext.cs` | `d254d7166` |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` | `3556061e1` |
| `.claude/adr/ADR-034-…` / `docs/adr/ADR-034-…` | `d254d7166` |
| `Services/Access/SecureChildShareSynchronizer.cs` | `d437b9738` (#1342, task 114: Restricted / external users; does not change the lineage mechanism, so the plan stands) |
| `Services/Access/SecureChildReconciler.cs`, `SecureChildReconciliationJob.cs` | `d254d7166` |
| `tests/unit/domain/Access/SecureChildLineageTests.cs` | `d254d7166` |
| `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs` | `c7f016c53` |

Open PRs checked (`gh pr list --state open`): no uac-r2 PR open; none of the open PRs touch the lineage, the config or `Services/Signals` writer files. The uac-r2 worktree is clean at `8bf068828`. Its `current-task.md` names no lineage work.

## 3. What changed

**Master (#1390)**:
- Lineage, both read live 2026-10-07:
  - `sprk_signal`: `sprk_matter`, `sprk_decisionrecord` and 8 regarding lookups (matter, project, work assignment, communication, document, event, invoice, to do).
  - `sprk_decisionrecord`: `sprk_matter`, `sprk_project`, `sprk_workassignment`.
- Config: two `kind: child` Read/Basic entries quoting the task 079 refusals.

**Writer (`stream/039-signal-writer`)**:
- `SignalWriter` asks `IRecordOwnershipResolver` with the matter (target) and the subject (parent).
- **Secure answer**: `RecordOwnerResolution.ApplyTo` writes the owner in the create. That is uac-r2's censused owner write, so `RecordOwnerAssignmentCensusTests` is unchanged. No `owningbusinessunit` is sent, and `owningteam` is read back after the create and on reconcile. A mismatch escalates with the new reason `secure_owner_mismatch`.
- **Not secure**: the FR-14 path is unchanged.
- **Refused**: nothing is written. Logged at Warning with EventId `50304 WriteSkippedOwnerRefused` (50302/50303 were taken by tasks 036/026, so it was renumbered at the merge). Metered as `owner_refused`, and `SignalWriteResult.IsSkipped` is set.
- `SignalWriteResult` gains `SecureOwnerTeamId` and `SkippedRefusalCode`.

## 4. Deviations from the POML (recorded, not silent)

1. **`sprk_signal` does not list `sprk_project`** (acceptance criterion 12). D-36/D-37 replaced the D-34 typed project column on the Signal with the generic `sprk_corerecordtype` + `sprk_corerecordid` pair. Task 007 confirmed no typed core lookup was added to `sprk_signal`, and uac-r2 condition A forbids listing a column that does not exist. The POML's own D-36 constraint says "with no new typed core lookup". A Secure project or work assignment is reached through `sprk_regardingproject` / `sprk_regardingworkassignment` and through the subject's own lineage.
2. **Writer-side Secure project and work-assignment cases** (acceptance criteria 11 and 13): the writer only derives a grouping matter for matter and communication subjects (`VerifiedMatterDerivation`). Project-, work-assignment- and to-do-grouped Signals arrive with task 037 (core-record grouping), which reuses this resolver path unchanged. The lineage side for those roots is in #1390.
3. **Live gate**: done for the writer half only (section 5). The share/unshare/2-minute/unsecure-release half needs a BFF carrying the new lineage deployed to spaarke-bff-dev (section 7).
4. **Load measurement** (section 6): measured as the dev baseline plus analysis, not after a "nightly pass that stamps every open Signal". D-61 removed that pass: 031 writes `sprk_lastevaluated` only when the result changes. spaarkedev1 has 0 Signals and the evaluator (031) is not built yet.
5. **Ownership path for the writer**: the POML said `ownerid@odata.bind` (Web API shape). The writer uses the SDK, and `ApplyTo` sets `ownerid` on the SDK entity: same effect, censused path.

## 5. Live evidence (spaarkedev1)

- **Role -Verify** with the new config: `Set-SecureRecordOwnerRolePrivileges.ps1 -Verify` → **VERIFY PASS, 28 of 28** (includes `sprk_signal`, `sprk_decisionrecord`).
- **Role edits read live**:
  - Spaarke Basic User: `prvReadsprk_Signal` and `prvReadsprk_DecisionRecord` at depth 1.
  - Spaarke Ontology Service: `prvAssignsprk_DecisionRecord` and `prvAssignsprk_Signal` at depth 8, plus Create/Read/Append/AppendTo.
- **Lineage columns, live describe**: all listed lookups present; every relationship is NoCascade for share/unshare/reparent/assign.
- **uac-r2 jobs in dev**: running.
  - 24 h in App Insights: 709 runs each.
  - `secure-child-reconciliation`: p50 3.4 s, p95 5.1 s, max 119 s.
  - `secure-child-share-reconciliation`: p50 2.6 s, p95 4.3 s, max 118 s.
  - The deployed recent-changes pass re-owned this task's probe communication into Secure Record Owners about 2 minutes after create (00:36:09Z → 00:38:07Z).
- **Writer secure path (real route, real resolver, as the writer principal `3121bf1b-…` via CallerId)**: `SignalWriterSeamTests` with `ONTOLOGY_039_SECURE_SUBJECT`. The subject was probe communications filed under the ordinary seed matter `2444af6d` (REAL-2026-123456.01) AND uac-r2's Secure project fixture `65a3fab2`; the project was not modified. Run twice, the second time on the final code:
  - The Signal was **created with `owningteam` = Secure Record Owners**.
  - No `owninguser`.
  - `owningbusinessunit` = the team's BU (Secure Record).
  - The two FR-14 seam tests (ordinary matter subject and communication subject) also passed: owner = writer, BU = the matter's BU.
- **Probe rows**:
  - `zz-039-probe-comm` `d223b73d-b0c2-f111-a05a-7c1e520a989f` and `zz-039-probe-comm-2` `4cc127fd-b1c2-f111-a05a-7c1e520a989f`: deleted; the GET returns 404.
  - `startswith(sprk_name,'zz-039')` → 0.
  - The seam Signals were swept by policy code; `sprk_signals` count → **0**, `sprk_decisionrecords` → **0**.
  - No matter, project or work assignment was created, so no SPE container was created.

## 6. Secure-sync load (scoping 5.2 item 4; uac-r2 #1355 §5)

- **Cap**: 20 pages × 5,000 = 100,000 changed rows per table per 2-minute window; past it the whole recent-changes pass throws for every table (#1378).
- **Dev now**: 0 Signals / 0 DRs. The jobs list `recentRowsChanged` avg 0.07, max 2 per run, with p95 about 5 s.
- **With D-61** the nightly pass writes only Signals whose result changed (plus new creates). The listing grows with **churn**, not with the open-Signal count, so a production pass approaches the cap only if more than 100k Signals change result in one 2-minute window. That is implausible for one nightly pass: it would mean 100k result changes landing in the same window.
- **Residual risk**: one high-volume first run of 031 (initial raise of many Signals at once). 031 should batch its first run or accept one retry window. Noted for 031; uac-r2 asked for a production open-Signal estimate to size #1378.
- **Re-measure** after 031 exists and the lineage BFF is deployed.

## 7. Escalations / owner decisions

- **🔔 Dev BFF deploy for the rest of the live gate.** Steps not yet run:
  - user A (shared) reads the Signal within 2 minutes and user B does not;
  - unshare;
  - unsecure releases the Signal to the BU default team;
  - the Secure matter and Secure work-assignment cases.

  These need spaarke-bff-dev running a build that carries the #1390 lineage (and, for writer-created Signals, the stream branch). spaarke-bff-dev is deployed from worktrees many times a day (3 OneDeploys in 2 h). A deploy is an Azure change, which needs owner approval per the project manual §5, so this task did not deploy. Options:
  - (a) merge #1390, then deploy master to dev, then run the gate with a provisioned `zz-039-` Secure matter and work assignment;
  - (b) deploy `feat/secure-child-signals-039` to dev now.

  Provisioning a Secure matter creates an SPE container that the cleanup must also delete (SPE admin recycle-bin delete).
- **Do not deploy a post-#1390 master BFF to demo or any customer environment** before the ontology schema is there (#1378). The PR says so.

## 8. Tests

- Master branch:
  - uac-r2 suites (`SecureChild*`, `RecordOwnership*`, census, `SecureBuRoleDepth`, `OwnedChildWrite`, `RecordCreatorPerson`): 849 passed.
  - ArchTests: 811 passed.
  - Full `Sprk.Bff.Api.Tests`: 18,472 passed, 0 failed, 54 skipped.
- Writer branch:
  - Signals/Ontology/DecisionPlan/RuleBody tests: 514 passed.
  - ArchTests (rebuilt): 818 passed. The first run caught an unlisted owner write, fixed by routing through `ApplyTo`.
  - Full suite after the merge: 18,882 passed, 0 failed, 54 skipped.
- PR #1390 CI: 33 pass, 5 skipping, 0 pending, 0 failing. One advisory job (Markdown Link Validator) first came back "cancelled" although every step succeeded; it passed on a rerun.
- New unit tests (4), the contract set: secure (owner in the create, no BU, `owningteam` read back, resolver parents), read-back mismatch (part of "verify by reading back owningteam"), not secure (FR-14 unchanged), refused (no write, Warning 50304, `owner_refused`).
- Publish size (#1390): fresh master `36ff14147` 36.22 MB / 192 files vs branch `0615f9781` 36.22 MB / 192 files, `Compress-Archive` Optimal from short paths. Delta 0.00 MB. No package change.

## 8a. Independent review of stream/039-signal-writer (2026-10-08): B-1, B-2, B-4 fixed in `13c002d85`

- **B-2 (F2):** two reconcile-path tests under a Secure answer.
  - Existing row owned by the Secure team → only `sprk_lastevaluated` is updated.
  - Row owned by another team → `secure_owner_mismatch`, no update.
  - Mutation-checked: with the reconcile call passing `null` instead of `secureTeamId`, both fail; restored afterwards.
  - Totals: SignalWriter tests 47 passed (44 unit; the 3 seam tests return early without the live env vars). ArchTests rebuilt: 818 passed.
- **B-1 (F2):** the `SignalWriter` remarks no longer claim the lineage lists `sprk_signal`. They now state that the lineage entry and the `prvReadsprk_Signal` config entry come from master PR #1390, and that until #1390 reaches this branch a Secure-team-owned Signal is owned correctly but its sharees are not mirrored.
  - **Sequence (coordinator):** #1390 → master → merge master into this branch → add the pin test (`SecureChildLineage.Children` contains `sprk_signal`; the config grants `prvReadsprk_Signal`) → merge this branch into `docs/ontology-platform-design`. The pin test is NOT added yet; it would be red here.
  - **Do not run `scripts/Set-SecureRecordOwnerRolePrivileges.ps1 -Apply` from this branch before #1390 is in it** (coordinator's rule, kept as a precaution). Factual note: uac-r2 states on #1355 (§2 item 1) that the script removes nothing, including privileges outside the config. From this branch, `-Verify` would list the two Reads as extra and `-Apply` would add nothing. The real hazard of running it from the wrong branch is a misleading Verify reading, not a stripped privilege.
- **B-4 (F4): publish size.**
  - Method: fresh detached worktrees at short paths (`C:\wt039m`, `C:\wt039o`, `C:\wt039w2`), `dotnet publish -c Release`, PowerShell `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, file counts compared.

| Side | Commit | Files | Zip |
|---|---|---|---|
| origin/master | `cf66c14c3` | 192 | 36.22 MB (37,978,964 B) |
| ontology base merged into this branch | `a157d44bd` | 192 | 36.26 MB (38,020,857 B) |
| this branch | `13c002d85` | 192 | 36.26 MB (38,022,437 B) |

  - **Branch vs master: +0.04 MB (+43,473 B).** That is the whole ontology branch's BFF work, not only 039.
  - **039's own contribution (branch vs its ontology base): +1,580 B (≈ 0.00 MB).**
  - File counts are equal on all three sides. No package change. Far under every threshold (+5 MB / 55 MB / 60 MB).

## 9. Quality gates (Step 9.5)

- **code-review**: one F2 found and fixed: the new method had stolen `EnsureOwningBusinessUnitMatches`'s XML doc. One census F1 found by ArchTests and fixed: the owner write now goes through `ApplyTo`.
- **Known limits**:
  - K2: a Signal not yet re-owned up to 2 minutes after its root is made Secure escalates `secure_owner_mismatch` on re-evaluation (fails closed, loud).
  - K4: a non-access Dataverse fault from the resolver propagates unlogged, the same as the writer's other reads.
- **adr-check**: compliant. ADR-002 (I-6 single owner reused; refusal fails closed), ADR-010 (no new interface), ADR-013, ADR-028 (writer MI unchanged; path A already recorded), ADR-034/003, ADR-038 (KEEP paths, no DI/ctor tests).
