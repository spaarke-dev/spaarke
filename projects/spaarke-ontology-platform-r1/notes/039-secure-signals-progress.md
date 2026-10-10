# 039: Signals and Decision Records as secure children: progress and completion record

> **Date**: 2026-10-07/08, live gate finished 2026-10-10 · **Rigor**: FULL · **Status**: code merged to master (#1390) and PR'd to the ontology branch (#1554, writer half). Live gate PASS on spaarkedev1 against the deployed master BFF `ebacd2e15` (section 5.2). Open items: two SPE containers for a SharePoint admin to delete, and one scheduler defect recorded as document-only (section 7).
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

### 5.1 Writer half (2026-10-07/08)

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

### 5.2 Deployed-BFF half (2026-10-10, owner decision D-67): PASS

**Setup.**
- **BFF**: spaarke-bff-dev runs master `ebacd2e15`, deployed by uac-r2, with #1390 and #1391; healthz 200. App Insights shows the new instance's first secure-child run at about 00:22Z.
- **Jobs at start** (01:12Z): both secure-child jobs enabled on `*/2`, last run Succeeded, `recentChanges.mode = write`, `rootsTotal 2`.
- **Writer**: the real `SignalWriter` was run locally through `SignalWriterSeamTests.WriteAsync_SubjectUnderASecureRecord_…`, as the writer principal `3121bf1b-…` via CallerId, using the real uac-r2 resolver. The branch was `task/ontology-039-writer` (PR #1554), with `AZURE_TOKEN_CREDENTIALS=AzureCliCredential` and `SIGNALS_LIVE_*`.
  - The seam's `DisposeAsync` sweeps its Signals at the end of the test, before the share steps could run. So for this gate only, a **local, uncommitted** line skipped that sweep when `ONTOLOGY_039_KEEP_ROWS=1`. The line was reverted afterwards, and the gate script deleted every row itself.
- **Identities**: admin (`az`). **A** = testuser1 `8d7bad7a-…` (BU1, non-admin; Spaarke Basic User, Office Add In User). **B** = uac.child `d6f8f439-…` (BU1, non-admin; Basic and Core User).
  - Root-BU users read every Secure-team-owned row in dev (accepted finding #1081, uac-r2 G3), so they cannot be sharee subjects.
  - Reads ran as each user with the admin token plus `MSCRMCallerID`, uac-r2's documented method. A was also read with testuser1's own token (`AZURE_CONFIG_DIR=C:/tmp/az-uac-child`).
- **Recipe**: uac-r2's `gate175.py` (batch-5 notes; G149-2 lineage). Throwaway roots were created by admin through the Web API. Every transition went through the BFF's own routes: `POST /api/v1/external-access/provision-project` (`transition: make-secure`), `/share-user` (View Only), `/unshare-user`, `/unsecure-project`.
- **Scripts and raw logs**: session scratchpad `gate039/`: `gate039.py`, `run1.out`, `results.json`; `gate039b.py`, `runB1.out`, `runB2.out`, `resultsB*.json`.

**Run A, 01:18:18–01:21:43Z (`20261010-0118Z`).**

Probe roots: matter **M** `335a7c78-48c4-f111-a05c-3833c5e9614d` and work assignment **W** `355a7c78-48c4-f111-a05c-3833c5e9614d`, both standalone.

Probe rows:

| Label | Row | How written |
|---|---|---|
| S1 | Signal `39337a97-48c4-f111-a05c-3833c5e9614d` | writer, subject `sprk_matter:M` |
| S2 | Signal `86337a97-48c4-f111-a05c-3833c5e9614d` | admin Web API, `sprk_matter` = M |
| S2w | Signal `42748b9c-48c4-f111-a05a-7c1e520a989f` | admin Web API, `sprk_regardingworkassignment` = W |
| D1 | Decision Record `a4337a97-48c4-f111-a05c-3833c5e9614d` | admin Web API, `sprk_matter` = M |
| D2 | Decision Record `44748b9c-48c4-f111-a05a-7c1e520a989f` | admin Web API, `sprk_workassignment` = W |
| C | communication `46748b9c-48c4-f111-a05a-7c1e520a989f` | admin, regarding the ordinary seed matter `2444af6d` AND W |
| S3 | Signal `d09eb4c7-48c4-f111-a05c-0022482913fc` | writer, subject `sprk_communication:C`; grouping matter = the ordinary seed matter |

| # | Step | Result | Evidence |
|---|---|---|---|
| 0 | Provision a Secure matter and a Secure work assignment (uac-r2 route) | PASS | 01:18:19Z M → 200 and 01:18:40Z W → 200. Both `sprk_issecure` true, owner Secure Record Owners `6eabc7f9…`, own SPE container (section 7). |
| 1 | Writer Signal on the Secure matter | PASS | S1 created 01:19:13Z, `createdby` = writer `3121bf1b…`. `owningteam` = **Secure Record Owners**, `owninguser` null, `owningbusinessunit` = Secure Record `d9ec0b6f…` (derived from the team). The seam asserts the same on the writer's result (`SecureOwnerTeamId`). That `owningbusinessunit` is not sent on the secure path is pinned by the unit test `WriteAsync_SecureOwner_SetsTheSecureTeamInTheCreate_SendsNoOwningBusinessUnit_ReadsTheTeamBack`. The live row cannot show what was sent, only the result. |
| 2 | **Re-own within ~2 min** (uac-r2 D-113 expectation): Signals and Decision Records created out of product on Secure roots | PASS | S2, S2w, D1, D2 and C were created 01:19:19–21Z by the admin user and read back owned by the admin user. All five were owned by **Secure Record Owners** **57 s** later, at the 01:20 run: `recentRowsChanged=5`, `recentRoots=2`, `changed=5`, `sharesWritten=5`, duration 24.5 s. |
| 3 | Writer Signal whose subject sits under the Secure work assignment (D-36), with an ordinary grouping matter | PASS | S3 `owningteam` = Secure Record Owners, `owninguser` null, BU Secure Record. The resolver's secure-if-any over the subject decided it. |
| 4 | Baseline: nobody shared | PASS | 01:20:40Z: A (impersonated), A (own token) and B all **403** on S1, S2, S2w, D1, D2 and S3. |
| 5 | Share M and W with A (View Only, `/share-user`): A reads within 2 min, B does not | PASS | 01:20:40Z share M → 200 `created`, mask 1. 01:20:46Z share W → 200. A **200 on all six** within **0–1 s**: `/share-user` mirrors onto the secure children inline through `SecureChildShareSynchronizer`, so the 2-minute job is not needed on this path. A's own token also gets 200 on all six. B is **403** on all six. |
| 6 | Unshare | PASS | 01:20:58Z unshare M → 200 `removed: true`. 01:21:04Z unshare W → 200. A **403 on all six** within 1 s; B 403. |
| 7 | Unsecure releases to the business unit's default team | PASS | 01:21:12Z `unsecure-project` M → 200: `sweepComplete: true`, `sharesRevoked 2`, children reowned 3 (`sprk_signal` 2, `sprk_decisionrecord` 1). 01:21:20Z W → 200: reowned 4 (`sprk_communication` 1, `sprk_decisionrecord` 1, `sprk_signal` 2). M and W are no longer Secure. All six rows and C are owned by **`09fbf21c…` "Spaarke"**, the default team of the roots' business unit (the root BU: both roots were created by the admin), within 1 s. S3 was released too, although its grouping matter is an ordinary BU1 matter: it followed C into W's release. |
| 8 | Clean-up | PASS | 01:21:34–42Z: 4 Signals, 2 Decision Records, C, W and M all DELETE 204, read-back **404**. |

**Run B: the share-JOB path (the model-driven Share dialog: Web API `GrantAccess` / `RevokeAccess` on the root, uac-r2 G149-2 step 4).**

The root is uac-r2's G149-1 probe recipe: admin create with `sprk_issecure = true`, then the owner PATCHed to Secure Record Owners. This creates **no SPE container**.

- **B-1, 01:23:57–01:29:40Z.** Matter `bc285b42-49c4-f111-a05c-0022482913fc`; writer Signal S4 `deaeb044-49c4-f111-a05a-7c1e520a989f`, owned by Secure Record Owners.
  - Before the grant, A and B were both 403.
  - `GrantAccess` (A, Read) at 01:24:14Z → 204. A read the root at once and read **S4 after 116 s**: the 01:26 share run granted it (`[SECURE-CHILD-SHARES] grant … on sprk_signal deaeb044…`). B stayed 403.
  - `RevokeAccess` at 01:26:11Z → 204. A could **still** read S4 at 204 s, so this sub-step **failed**. Cause: the 01:28 tick of both secure-child jobs never ran.
    - At 01:28:00.0026Z, `ScheduledJobHost refreshed — 15 job(s) scheduled` landed on the due tick, and no secure-child job was dispatched at 01:28.
    - This is a scheduler defect, not a lineage one. It is recorded as document-only in section 7.
  - Clean-up: 204 / 404 for both rows.
- **B-2 re-run, 01:37:09–01:40:14Z.** Matter `bb949c16-4bc4-f111-a05c-0022482913fc`; S4 `ebcfef1c-4bc4-f111-a05c-0022482913fc`, owned by Secure Record Owners.
  - `GrantAccess` at 01:37:24Z → A reads S4 after **44 s** (the 01:38 run). B 403.
  - `RevokeAccess` at 01:38:09Z (just after the 01:38 run) → A is **403 after 120 s** (the 01:40 run). B 403.
  - **PASS.** Clean-up: 204 / 404.

**Final sweep after both runs.**
- `startswith(…,'zz-039')` on `sprk_matters`, `sprk_workassignments`, `sprk_communications`, `sprk_signals` and `sprk_decisionrecords` → **0 each**.
- `sprk_signals` total **0**; `sprk_decisionrecords` total **0**.
- Neither the ordinary seed matter `2444af6d` nor uac-r2's fixture `65a3fab2` was modified.

**Not run, and why.**
- **The Secure project sharee case (acceptance criterion 11).** Its owner half was proven in 5.1 on `65a3fab2`. Sharing that project would modify uac-r2's fixture, which is forbidden. The share and release half uses the same table-generic lineage mechanism the matter and work-assignment cases proved above.
- **The service-request case (D-36)** is covered by the lineage (no service-request lookup) and by unit tests. No live probe was run.

## 6. Secure-sync load (scoping 5.2 item 4; uac-r2 #1355 §5)

- **Cap**: 20 pages × 5,000 = 100,000 changed rows per table per 2-minute window; past it the whole recent-changes pass throws for every table (#1378).
- **Dev now**: 0 Signals / 0 DRs. The jobs list `recentRowsChanged` avg 0.07, max 2 per run, with p95 about 5 s.
- **With D-61** the nightly pass writes only Signals whose result changed (plus new creates). The listing grows with **churn**, not with the open-Signal count, so a production pass approaches the cap only if more than 100k Signals change result in one 2-minute window. That is implausible for one nightly pass: it would mean 100k result changes landing in the same window.
- **Residual risk**: one high-volume first run of 031 (initial raise of many Signals at once). 031 should batch its first run or accept one retry window. Noted for 031; uac-r2 asked for a production open-Signal estimate to size #1378.
- **Re-measured 2026-10-10 with the lineage deployed** (master `ebacd2e15` on spaarke-bff-dev, first run on the new instance at about 00:22Z). 031 is still not built, so there is no nightly pass to measure yet.
  - Sources: Log Analytics `spe-logs-dev-67e2xz` for the 24 h before the deploy; App Insights `6a76b012…` after it. App Insights holds data only from 2026-10-10T00:00Z.
  - Calls are the Dataverse dependencies (`spaarkedev1`, SDK and Web API) between each tick and the later of the two jobs' completions. Only ticks on even minutes that are not `*/5` minutes are counted, so that no other scheduled job shares the window.
  - Run A's window (01:18–01:23Z) and run B's window (01:25–01:40Z) are excluded from the call counts. Durations and rows include them.

| Measure | 24 h before (2026-10-09 00:22Z → 10-10 00:22Z) | Since the deploy (00:22 → 01:40Z) |
|---|---|---|
| `secure-child-reconciliation` runs | 703 | 30 |
| Its duration p50 / p95 / max | 4.7 / 6.8 / 137.6 s | 4.9 / 24.5 / 29.2 s |
| Rows listed per run (`recentRowsChanged`) avg / max | 0.37 / 38 | 0.17 / 5 |
| `secure-child-share-reconciliation` runs | 704 | 30 |
| Its duration p50 / p95 / max | 3.0 / 4.7 / 117.0 s | 3.1 / 4.7 / 5.1 s |
| Share-job `inScope` avg / max | 2 / 2 | 2.03 / 3 |
| Dataverse calls per tick (both jobs) p50 / p95 / max | 16 / 24 / 274 (564 ticks) | 18 / 57 / 80 (23 ticks) |

Reading the "since the deploy" column:
- The two high durations are the new instance's first catch-up run (00:28Z, 29.2 s) and this gate's own run (01:20Z, 24.5 s), which re-owned and mirrored the 5 probe rows.
- Steady-state calls rose by about 2 per tick (p50 16 → 18). That fits the recent-changes listing now including two more child tables per run.
- Rows listed stay at churn level: 5 at most, which was the gate.
- Against the cap of 100,000 changed rows per table per window and the 2-minute window, dev is about four orders of magnitude below the row cap. Run time is about 5 s against 120 s.
- No escalation trigger fired. The D-61 reasoning above still holds; the residual risk remains 031's first high-volume run.

## 7. Escalations / owner decisions

- **Resolved: the dev BFF deploy (D-67).** #1390 was merged to master. uac-r2 deployed master `ebacd2e15` to dev, and the gate ran (section 5.2).
- **Owner action: delete two SPE containers.** These were created by provisioning in run A. Both are empty, and both of their records are deleted.
  - Matter M `335a7c78…`: `b!QPhJA-4NgU6Tx4SFIauctdNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y`.
  - Work assignment W `355a7c78…`: `b!vlLJSaMQZE6HXqqB9TzOr9NtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y`.
  - The only recorded recipe is uac-r2's `projects/unified-access-control-r2/notes/Remove-TestContainers.ps1`. It is run by a SharePoint administrator with `Connect-SPOService` (interactive), so this task did not run it. The exact commands are:
    ```powershell
    Connect-SPOService -Url 'https://spaarke-admin.sharepoint.com'
    Remove-SPOContainer -Identity 'b!QPhJA-4NgU6Tx4SFIauctdNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y'
    Remove-SPOContainer -Identity 'b!vlLJSaMQZE6HXqqB9TzOr9NtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y'
    ```
    The main session recorded the same commands as the owner script `projects/spaarke-ontology-platform-r1/notes/Remove-039TestContainers.ps1`.
  - Run B used the G149-1 recipe and created no container.
- **Document only (D-106; not 039 scope): `ScheduledJobHost` drops the tick that is due when its hourly definition refresh lands on it.**
  - The code: `src/server/shared/Spaarke.Scheduling/ScheduledJobHost.cs` `RefreshDefinitionsAsync` rebuilds every job's `nextFire` as `cron.GetNextOccurrence(now)`, which excludes `now`. A refresh at 01:28:00.0026Z therefore replaced the 01:28:00 occurrence that was still due with 01:30:00.
  - The evidence:
    - Run B-1: no secure-child dispatch at 01:28Z; the refresh trace is at 01:28:00.0026Z.
    - Log Analytics, last 3 days: 86 refreshes, 61 of them on an even minute. 42 of those 61 have **no** `secure-child-share-reconciliation` run in that minute.
  - The effect: about once an hour, every job due at the refresh moment skips one occurrence. For the two secure-child jobs, the "≤ 2 minutes" bound becomes about 4 minutes plus the run, for that one occurrence. This affects every table they cover, not just Signals.
  - Owner of the code: platform scheduling (ADR-036). Filed by the main session as **#1575** (project issue log F-52).
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
- PR #1554 (`task/ontology-039-writer` @ `626241c00`, 2026-10-10). This is the writer branch merged with `docs/ontology-platform-design` @ `2b0bea645` (which contains #1390), plus the B-1 pin test `SecurePath_DependsOn_SignalLineageEntry_AndSecureRecordOwnerRead`.
  - `--filter Signals|SecureChildLineage|Census`: 706 passed, 0 failed.
  - ArchTests (built explicitly): 927 passed, 9 failed. All 9 failures are `ExternalSpaGridViewSelectorGuardTests`, already red on the project branch and being fixed in another lane.
- PR #1390 CI: 33 pass, 5 skipping, 0 pending, 0 failing. One advisory job (Markdown Link Validator) first came back "cancelled" although every step succeeded; it passed on a rerun.
- New unit tests (4), the contract set: secure (owner in the create, no BU, `owningteam` read back, resolver parents), read-back mismatch (part of "verify by reading back owningteam"), not secure (FR-14 unchanged), refused (no write, Warning 50304, `owner_refused`).
- Publish size (#1390): fresh master `36ff14147` 36.22 MB / 192 files vs branch `0615f9781` 36.22 MB / 192 files, `Compress-Archive` Optimal from short paths. Delta 0.00 MB. No package change.

## 8a. Independent review of stream/039-signal-writer (2026-10-08): B-1, B-2, B-4 fixed in `13c002d85`

- **B-2 (F2):** two reconcile-path tests under a Secure answer.
  - Existing row owned by the Secure team → only `sprk_lastevaluated` is updated.
  - Row owned by another team → `secure_owner_mismatch`, no update.
  - Mutation-checked: with the reconcile call passing `null` instead of `secureTeamId`, both fail; restored afterwards.
  - Totals: SignalWriter tests 47 passed (44 unit; the 3 seam tests return early without the live env vars). ArchTests rebuilt: 818 passed.
- **B-1 (F2):** the `SignalWriter` remarks no longer claim the lineage lists `sprk_signal`. They now state that the lineage entry and the `prvReadsprk_Signal` config entry come from master PR #1390, and what happens without it (corrected after the re-check, `5d231194a`): whether the create works depends on the live Secure Record Owner role holding Read on `sprk_signal` (task 008's role edit, not #1390). Where the role lacks Read, Dataverse refuses with 403 `0x80040299` → `DataverseAccessDenied` (task 079 reproduced it 3/3 via Assign for `sprk_signal`; task 146 for `sprk_spendsignal`). Where it has Read but #1390 is not deployed (spaarkedev1 today; this gate wrote such a Signal on 2026-10-08), the Signal is owned by Secure Record Owners but its sharees are never mirrored, so only the BFF can read it. Shipping needs both #1390 and the role edit in the target environment.
  - **Sequence (coordinator):** #1390 → master → merge master into this branch → add the pin test (`SecureChildLineage.Children` contains `sprk_signal`; the config grants `prvReadsprk_Signal`) → merge this branch into `docs/ontology-platform-design`. The pin test is NOT added yet; it would be red here.
  - **Do not run `scripts/Set-SecureRecordOwnerRolePrivileges.ps1 -Apply` from this branch before #1390 is in it** (coordinator's rule, kept as a precaution). Factual note: uac-r2 states on #1355 (§2 item 1) that the script removes nothing, including privileges outside the config. From this branch, `-Verify` would list the two Reads as extra and `-Apply` would add nothing. The real hazard of running it from the wrong branch is a misleading Verify reading, not a stripped privilege.
- **B-4 (F4): publish size.**
  - Method: fresh detached worktrees at short paths (`C:\wt039m`, `C:\wt039o`, `C:\wt039w2`), `dotnet publish -c Release`, PowerShell `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, file counts compared.

| Side | Commit | Files | Zip |
|---|---|---|---|
| origin/master | `cf66c14c3` | 192 | 36.22 MB (37,978,964 B) |
| ontology base merged into this branch | `a157d44bd` | 192 | 36.26 MB (38,020,857 B) |
| this branch | `13c002d85` | 192 | 36.26 MB (38,022,437 B) |

  - **Branch vs master: +0.04 MB (+43,473 B).** That is the ontology branch's BFF work NET of master-only changes: the baseline `cf66c14c3` is not an ancestor of the branch (master has 27 `src/server` files since merge base `fc13ab03e` that the branch lacks). The 039-only figure (+1,580 B vs its ancestor base `a157d44bd`) is the clean one.
  - **039's own contribution (branch vs its ontology base): +1,580 B (≈ 0.00 MB).**
  - File counts are equal on all three sides. No package change. Far under every threshold (+5 MB / 55 MB / 60 MB).

## 9. Quality gates (Step 9.5)

- **code-review**: one F2 found and fixed: the new method had stolen `EnsureOwningBusinessUnitMatches`'s XML doc. One census F1 found by ArchTests and fixed: the owner write now goes through `ApplyTo`.
- **Known limits**:
  - K2: a Signal not yet re-owned up to 2 minutes after its root is made Secure escalates `secure_owner_mismatch` on re-evaluation (fails closed, loud).
  - K4: a non-access Dataverse fault from the resolver propagates unlogged, the same as the writer's other reads.
- **adr-check**: compliant. ADR-002 (I-6 single owner reused; refusal fails closed), ADR-010 (no new interface), ADR-013, ADR-028 (writer MI unchanged; path A already recorded), ADR-034/003, ADR-038 (KEEP paths, no DI/ctor tests).
