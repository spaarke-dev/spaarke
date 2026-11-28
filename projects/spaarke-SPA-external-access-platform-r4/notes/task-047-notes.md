# Task 047 notes: foreign-environment membership 403 logs at Warning (FR-31 c, #2578)

Status: code and tests done in the working tree, NOT committed (the main session runs one review over the wave and commits).
Rigor FULL, sonnet @ xhigh. Step 9.5 (review), 9.6, 10, 11, the publish-size measurement and `/conflict-check` are the main session's.

**UAC review: APPROVED** (uac-r2, 2026-11-27; see "UAC review (FR-31)" below).

## What changed (summary)

| File | Change |
|---|---|
| `Services/ExternalAccess/IdentityLinkReconciliationJob.cs` | `ReconcileEnvironmentAsync` takes a required `bool foreignEnvironment` from its two callers (`:326` own = false, `:585` provisioning target = true). On a probe that is `Failed` in a foreign environment it asks the store whether the membership is refused; yes = `LogWarning` (`:476`), anything else = the unchanged `LogError` (`:484`). The abort (`ProbeBlocked`, `return`) and every report field are identical in both branches. |
| `Infrastructure/ExternalAccess/ContactIdentityStore.cs` | **Beyond the literal "probe-outcome type" (stated explicitly):** one additive interface member `IContactIdentityStore.IsEnvironmentMembershipRefusedAsync` (`:307`, default interface method returning `false`), its `DataverseContactIdentityStore` implementation (`:1814`), the code constant `NotAMemberErrorCode` (`:1821`) and the pure classifier `IsMembershipRefusal` (`:1828`). `BindingReadability`, `ProbeBindingReadabilityAsync`, `ClassifyProbe` and `SecuredColumnReadProof` are NOT touched. |
| `tests/integration/auth/UnifiedAccessControl/IdentityBinding/IdentityLinkReconciliationTests.cs` | 9 new test cases + a hand-written fake Dataverse (`RefusingDataverse`), a second store map on `RunAsync`/`PerEnvironmentBinderFactory`, two readability accessors. |

No new DI registration, interface type, package, timer, hosted service or schedule. No log line added or changed carries a tenant, oid, UPN, email, token or response body.

## Step 1: Error inventory of one acme pass (before the change)

Own environment first (clean), then the foreign `https://spaarke-acme.crm.dynamics.com` (registry row; the BFF's application user has no seat there). Line numbers are the pre-change tree; the POML's `:376-386` / `:382` were from an older revision (the `LogError` was at `:459`).

| # | Emitter | Level / telemetry | Reaches App Insights as Error? |
|---|---|---|---|
| 1 | Job `ReconcileEnvironmentAsync`, `LogError("[ID-LINK-RECON] {Environment}: binding column Failed — environment aborted before any decision")` (pre-change `:459`) | Error trace, severity 3 | **YES. The only one.** |
| 2 | `SecuredColumnReadProof.ProveAsync` catch, `LogWarning(ex, "[FLS-READ] Whether the BFF can read contact.<cols> could not be established…")` (`SecuredColumnReadProof.cs:169-176`) | Warning WITH an exception: the App Insights logger sends it as exception telemetry (the `exceptions` table), severity Warning. Once per pass (the binding-columns proof fails first, so the collision proof is never tried) | No (Warning severity). It is a row in `exceptions`, though; see "Found, not fixed" 2 |
| 3 | `DataverseContactIdentityStore.GetAsync`, `LogWarning("[ID-BIND] GET {Path} failed: {Status} {Body}")` for the WhoAmI read (`:2537` now) | Warning trace, includes up to 400 chars of the response body | No |
| 4 | Job `LogHeartbeat` (one line per environment per run) | Warning whenever the run status is not `ok` | No |
| 5 | `ScheduledJobHost` after the job returns `Success: false` (`ScheduledJobHost.cs:868`) | Information ("completed … success=False"); Error only for a THROWN exception or a lease fault | No |
| not emitted | `ExecuteAsync` catch (`Run failed`), `ReconcileProvisioningTargetsAsync` catch (`reconciliation failed`) | Error | Neither fires: the probe returns normally, nothing throws |

Reasoning from the logging provider's behaviour and the code; **not verified live** (no live calls were made, per the brief).

After the change an acme pass emits no Error: line 1 becomes a Warning, and the foreign Failed path adds one more Warning, the follow-up WhoAmI's `[ID-BIND] GET membership check failed: 403 0x80072560` (a labelled read: status and code only, never the body).

## Step 2: how a membership 403 is told apart (design and why)

The proof swallows the exception into a bare `ColumnReadProofOutcome.Faulted`, so the reason cannot come out of the proof without changing its contract (the escalation trigger). It does not need to.

**Chosen:** a follow-up question on the store, asked only after a Failed probe, only for a foreign environment:
`IContactIdentityStore.IsEnvironmentMembershipRefusedAsync(ct)`. The Dataverse store answers it with one more `WhoAmI` (the proof's own first read) and returns true only for HTTP 403 AND error code `0x80072560` (exact; a 403 with another code, `0x80072560` under another status, a 5xx, a throttle or an unreachable host are all false).

Why this and not the alternatives:
- **A new `BindingReadability` member (the POML's example) is fail-open, so it was rejected.** `ContactIdentityBinder.MaskedAsync` tests `readability is Masked or Failed` (`ContactIdentityBinder.cs:2529`, called from seven places: `:739`, `:1719`, `:1791`, `:2116`, `:2164`, `:2196`, `:2303`); `RegisterHomeContactAsync` tests `binding == Failed || home == Failed` (`:1029`); `LinkHomeSystemUserAsync` picks its log level by `home == Failed` (`:1610`). A fifth member would fall through the first two and let a bind, create or link proceed on an unreadable environment. Those are in `ContactIdentityBinder.cs`, outside this task's files.
- **A reason carried out of `SecuredColumnReadProof`** is exactly the shared-contract change the task says to stop for. Also the proof is shared per environment across stores (`ContactIdentityBinderFactory._readProofs`), so a per-store flag set by the proof's source would sit on the wrong store.
- **A reason captured during the probe (AsyncLocal or a field)** would miss the case where the proof is still held (10 min) and a later read is what fails.
- **The follow-up read** is stateless, adds nothing to the success path or the own environment, leaves every existing consumer's verdict byte-identical, and defaults to `false` through a default interface method, so every other store (the in-memory double, `HeaderDrivenIdentityStore`) is unchanged and keeps the Error. A store that cannot tell is loud, never quiet.
- **Warning over a cooldown** (FR-31 c): a cooldown needs per-environment state across runs. It would also remove the follow-up read and the `exceptions` rows (item 2 above); not done, see "Found, not fixed" 2.

Own environment: the job calls only `ProbeBindingReadabilityAsync`, as before. The follow-up is not asked (`foreignEnvironment` is false, short-circuit), so the own path is request-for-request identical (a test pins "one WhoAmI read").

Fail-closed is untouched: after the Warning the method returns exactly as the Error branch does (`ProbeBlocked = true`, no pass 1, no pass 2, no write), the run is still `StatusError` through `RunReport.Assess`, the target's `readability` is still `Failed`, and `ErrorMessage` still names the environment.

## Tests (`IdentityLinkReconciliationTests.cs`, KEEP path `tests/integration/auth/**`)

The membership tests run the REAL `DataverseContactIdentityStore` over a hand-written fake Web API (`RefusingDataverse`: one scripted status and body for every request; no `Mock<HttpMessageHandler>`), so the wire answer decides. Time is `FakeTimeProvider(Now)` only.

| Test | Kind | Proves |
|---|---|---|
| `AForeignEnvironment_RefusingTheBffsMembership_LogsWarningNotError_AndIsStillAbortedAndReported` | must-fire | no Error-level entry in the whole run; one Warning naming the URL and `0x80072560`, never the body; only GET `WhoAmI` reached the environment (no scan, no write); run `Success=false`, target `error`/`Failed`, `ErrorMessage` names it; the own environment is still reconciled |
| `TheOwnEnvironment_RefusingTheBffsMembership_StillLogsErrorAndIsAbortedBeforeAnyDecision` | must-not-fire | the SAME wire answer on the own environment: one Error, no Warning, exactly one request (no follow-up), aborted |
| `TheOwnEnvironment_WhoseProbeIsFailedOrMasked_...` (Theory: Failed, Masked) | must-not-fire | own Failed and own Masked: one Error, no writes, no user scan, run failed |
| `AForeignEnvironment_WhoseBindingColumnIsMasked_StillLogsError` | negative | foreign Masked keeps the Error |
| `AForeignEnvironment_FailingForAnyOtherReason_...` (Theory: 503 throttle; 403 + `0x80040220`; 500 + `0x80072560`) | negative | each stays an Error, no body in the line, still aborted |
| `AForeignEnvironment_WhoseStoreCannotTellWhyItFailed_StillLogsError` | negative (one-line justification: pins the interface default, the fail-loud direction for every store but Dataverse's) | foreign + a store with no answer = Error |

9 new cases; the class went from 59 to 68.

## Results

- `dotnet build src/server/api/Sprk.Bff.Api/`: succeeded, 0 warnings, 0 errors.
- `IdentityLinkReconciliationTests`: **68 passed, 0 failed** (59 existing + 9 new).
- Wider filter (`IdentityBinding`, `DataverseContactIdentityStoreTests`, `IdentityLinkReconciliation*`, `SecuredColumnReadProof`, `ContactIdentityBinder*`): **916 passed, 0 failed**.
- `dotnet test tests/Spaarke.ArchTests/`: **2845 passed, 0 failed** (9 m 20 s).
- The full BFF unit suite was NOT run (host-based tests time out while other builds run here), per the brief.
- Publish size: not measured here (the main session does).

## Proofs

1. **Fails before, passes after.** Old job and store restored from HEAD (`git show`), the 9 new cases run: **1 failed / 67 passed**. The one failure is `AForeignEnvironment_RefusingTheBffsMembership_…` (the old job logs Error). The must-not-fire and negative cases pass before and after, as guards should. My versions restored (byte-compared) afterwards: 68 passed.
2. **Seeding proof (own environment onto the foreign branch).** Changed the own call to `foreignEnvironment: true`: **1 failed / 67 passed**; the failure is `TheOwnEnvironment_RefusingTheBffsMembership_StillLogsErrorAndIsAbortedBeforeAnyDecision`. (The in-memory own Failed/Masked theory rows stay green under that seed: a store with no wire answer cannot be downgraded. The real-store own test is the load-bearing one.) Restored and byte-compared.
3. **Seeding proof (the classifier).** Forced `IsMembershipRefusal` to `true`: **3 failed / 65 passed**; the three failures are the three `AForeignEnvironment_FailingForAnyOtherReason_…` rows. Restored and byte-compared.
4. One test of mine failed on the first run: `AForeignEnvironment_WhoseBindingColumnIsMasked_StillLogsError`. Cause was my setup: masking only shows on a marked row, and the in-memory foreign store had none, so its probe read `NoMarkedRows`. A bound contact was added to the store; not a product defect.

## Deviations and decisions

- `ContactIdentityStore.cs` was edited beyond "the probe-outcome type": an interface member, its implementation and a classifier, all additive. The alternative (a new `BindingReadability` member) is fail-open at three consumers I may not edit; see Step 2.
- The test file's `PerEnvironmentBinderFactory`/`RunAsync` gained an optional second store map (the existing `targets` is typed to the in-memory store).
- Worktree paths in this session: the scratchpad `mine/` folder held unrelated files from earlier work; my backups live in `scratchpad/task047-backup/` (outside the repo).

## Found, not fixed

1. **NFR-04: response bodies in logs on the proof path (pre-existing, real path).** `DataverseContactIdentityStore.GetAsync` logs `Trim(body)` (400 chars) at Warning for every UNLABELLED failed read (`ContactIdentityStore.cs:2537`), and `ReadProofAsync` throws `InvalidOperationException("… did not answer (Failed, 403: {body})")` (`:2358`), whose message `SecuredColumnReadProof` logs with the exception (`SecuredColumnReadProof.cs:172`). A Dataverse 403 body can name the user or app. Scenario: every acme pass writes the 403 body into App Insights twice. Fix is two lines (give the proof read a `logLabel`, put the status and error code only in the exception) but it changes the own environment's log content and sits in files outside this task, so it is for the operator. Not filed (the brief wrote no issue step); needs `/project-defer-issue-tracking`.
2. **`exceptions`-table rows persist (needs a decision).** The proof's Warning-with-exception (item 2 of the inventory) is still emitted once per 5 minutes for acme. It is Warning severity, so FR-31 c ("logs at Warning") holds, but if task 058's gate counts "acme faults" as rows in `exceptions` regardless of severity, it will still see them. Removing them needs a cooldown (skip the environment for N minutes after a membership refusal: per-environment state in the job, on the injected `TimeProvider`) or a change in the shared proof. Say if the owner wants that.
3. **The run still reports `Success=false` every 5 minutes while acme is unfixed.** Unchanged on purpose ("the run report still records it"); the job-run record and the heartbeat Warning show it. Task 046's data fix (application user in acme, or the row's `sprk_appservicename`) removes it.

**Resolved 2026-11-28 (items 2 and 3).** Owner decision: "skip ACME until provisioning-r1 indicates it is stable". provisioning-r1's read-only checks, same day, showed no change is needed:
- acme's registry row is Ready and names its own BFF in `sprk_appservicename`, so the #2761 rule (`IsServedByThisBff`) already leaves it to that BFF.
- Fault history (provisioning-r1, App Insights with `--offset 90d`; the first check used the CLI's 1-hour default and was corrected):
  - the acme `0x80072560` faults ran from 2026-10-14 21:10Z to 2026-11-13 20:35Z: 40,910 rows in traces and exceptions;
  - the first "served by another BFF: sprk-acme-prod-api=1" skip is at 2026-11-13 20:40Z, the next run;
  - no fault has appeared since.
- **The fix is uac-r2's #2761 / #2791 (`f23dfed281`), live on dev since 2026-11-13, not a provisioning change.** The row's `sprk_appservicename` predates 2026-11-06, the start of the audit: no audit entry changes it, so it was most likely written by the October provisioning. The pre-#2761 job reconciled every Ready row, whatever App Service it named.
- provisioning-r1 considers acme stable (its run completed 2026-11-26 and set Ready). It asked that the row's setup status not be changed: L2 and the provisioning skill read it.
- No cooldown and no skip-list were added. Task 058's gate should see no acme rows.

## UAC review (FR-31)

2026-11-27, uac-r2 (cross-session message), **APPROVE** 9f819b9daa from an access-control standpoint:

> Only the log level changes. In ReconcileEnvironmentAsync both branches set ProbeBlocked first and both return before ReconcileSystemUsersAsync, so a foreign membership-refused environment still aborts before any decision or write, exactly as before.

Its non-blocking note: if the extra WhoAmI throws instead of returning a status, the foreign-loop catch records `report.Fault` and logs Error "reconciliation failed" rather than ending ProbeBlocked with an Error. It still fails closed; only the failure's wording changes. No code change.

## Main-session gates (2026-11-27)

- **Publish size:** fresh master `17bb2244fd` zips to 43,731,563 bytes; master merged with 044+047 zips to 43,732,276 bytes. Delta **+713 bytes**; 194 files each side; Compress-Archive; worktrees `C:\wt047m` / `C:\wt047b`. No package change, so no new CVE.
- **/conflict-check:** none of the 34 open PRs touches the four changed files.
- **Adversarial review (opus, one pass over 044+047):** approve, no F-class findings.
  - K1, classifier looseness: `Contains` over the whole error string. Tightened to an exact `error.code` match, carried in task 045's change set (same file).
  - K4, owner question: the registration default environment (`DATAVERSE_URL`) is also reconciled as a provisioning target, so it gets the Warning too. This is consistent with the POML wording. **Owner decision 2026-11-27: keep the Warning** for that environment as well; no code change.
  - K2, test gaps: the foreign-Masked negative test is not load-bearing on `readability == Failed` (Masked and membership cannot co-occur on a real path); the "no Error" assertion watches the job logger only, and the store/proof lines were checked by reading the code.
  - K4, task 058's gate: the proof's Warning-with-exception still writes an `exceptions` row at Warning severity every run.
  - The pre-existing body-in-logs defect (NFR-04) is fixed in task 045.
