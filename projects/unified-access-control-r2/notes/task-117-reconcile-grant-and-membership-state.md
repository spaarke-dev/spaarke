# Task 117 — D-1 option B: ONE scheduled reconciliation job over external grants and org memberships

> **Date**: 2026-09-21 · **Register**: ISS-026 (#1006) write half · ISS-020 (#999) part 4 ·
> **Owner decisions**: D-1 (2026-09-19) option B · D-2 part 3 (ship disabled + report-only)
> **Siblings**: task 107 = D-1 option A (shipped 2026-09-21, `b0e59bd86`) · task 118 = D-1 option C

---

## 1. What shipped

One `IScheduledJob` — `ExternalAccessReconciliationJob` — that makes a row's OWN `statecode` /
`sprk_expiresdate` the truth about whether it confers access, so nothing has to re-derive that from a
parent or a date on every read.

| Rule | Selects | Writes |
|---|---|---|
| **R1** | ACTIVE `sprk_externalrecordaccess` with no `sprk_expiresdate` | `sprk_expiresdate = ExternalGrantLifecycle.DefaultExpiry(today)` |
| **R2** | ACTIVE grant whose `sprk_organization` points at an INACTIVE `sprk_organization` | `statecode=1, statuscode=2` |
| **R3** | ACTIVE `sprk_contactorganization` whose `sprk_enddate` has PASSED | `statecode=1, statuscode=2` |

**R2 beats R1 on the same row**, and that precedence is *structural* rather than a dedupe step: ONE scan
serves both rules, so a row is classified once and cannot land in two change sets — which is also what
stops two updates for one id entering one transaction.

## 2. Coherence with task 107, which shipped first (the thing that changed the design)

Task 107 inverted the READ default: a null `sprk_expiresdate` now confers **nothing**. That inverts R1's
character. Under the old fail-open default R1 was a security fix (bounding an unbounded grant). Under
107's fail-closed default the exposure is *already closed at read time*, and R1 is a **data repair that
turns a row conferring nothing into a row conferring access for 90 more days**.

So R1 is the one rule here that **grants**. It is therefore gated by the same owner switch as R2 and R3
rather than treated as harmless — which is a deliberate departure from reading R1 as "just cleanup". An
operator who decides an undated row *should* stay dead simply never enables writes, and 107's read
default keeps it dead.

## 3. Step 0 — the two UNVERIFIED claims, now verified (Dataverse MCP `describe`, 2026-09-21)

| Claim | Result | Escalation trigger |
|---|---|---|
| `sprk_enddate` type/behaviour | **DATE ONLY** | Trigger 1 **did not fire** — R3 ships as specified, compared as a bare `yyyy-MM-dd` with strict `lt` |
| `sprk_startdate` exists? | **YES — it exists** (`DATE ONLY`) | Trigger 2 **FIRED** — see §4 |

Also confirmed on both tables: `statecode` Active(0)/Inactive(1), `statuscode` Active(1)/Inactive(2) —
so the deactivation payload's `statuscode=2` is right for `sprk_contactorganization` as well as for
`sprk_externalrecordaccess`.

## 4. 🔔 ESCALATION — `sprk_startdate` exists and NOTHING consults it (trigger 2, fired)

This task's own escalation trigger: *"If `sprk_startdate` exists, a not-yet-STARTED membership is a second
fail direction this task never scoped. STOP and escalate rather than extending scope silently."*

It exists, and it is unread on the server:

```
grep "sprk_startdate" src/**   →  server-side hits: ZERO
                                  (4 hits, all client: a Calendar widget date-field option
                                   + RecordHeader test fixtures)
```

**Consequence**: a `sprk_contactorganization` row with `sprk_startdate` in the FUTURE and `statecode=0`
confers access **today**. That is the mirror image of R3 and it is not in this task's scope.

**Deliberately NOT implemented.** Adding an R4 would be the silent scope extension the trigger forbids —
and it is not obviously a no-op: "not yet started" plausibly means the row should not exist yet rather
than that it should be deactivated, and deactivating it would make the start date unreachable. That is an
owner decision, not an implementation detail. Live blast radius today: **1 of 2** active junction rows has
a start date and it is in the past (2026-08-12), so nothing is currently affected either way.

**Recommendation**: file as a register issue alongside ISS-020/ISS-026 and decide whether the read guard
(tasks 109+110, which own the junction read) should bound on `sprk_startdate` as well as `sprk_enddate` —
that is probably the right home for it, not this writer.

## 5. Step 1 — the before-state, measured against live dev (2026-09-21)

The owner's D-2 part 3 precondition. Measured by direct query, not by the job:

| Rule | Rows it would change | Denominator |
|---|---|---|
| R1 | **0** | 28 active `sprk_externalrecordaccess` rows — every one already carries an expiry |
| R2 | **0** | all **3** `sprk_organization` rows are Active, so no grant references an inactive one |
| R3 | **0** | 2 active `sprk_contactorganization` rows, both with a NULL `sprk_enddate` |

Sanity-checked in both directions — the tables are non-empty (28 / 2 / 3 rows), so these zeros mean
"nothing to reconcile", not "the query was wrong".

**Escalation trigger 3 did NOT fire** (it fires on "more than a handful"). Zero is not owner sign-off for
enabling the job — it is the statement that enabling it *today* would remove nothing. The environment is
dev and its records are test records; the counts must be re-taken against any real environment before the
switch is thrown.

## 6. The two switches, and why there are two

Both must be thrown before a single row is written.

1. **`AddScheduledJob<ExternalAccessReconciliationJob>(cron, enabled: false)`** — the scheduler never
   ticks it.
2. **`ExternalAccess:Reconciliation:WritesEnabled`**, default **false** — so even a manual
   `POST /api/admin/jobs/external-access-reconciliation/trigger` of the disabled job produces a REPORT and
   writes nothing.

The flag is named **positively** on purpose: `bool.TryParse(…) && enabled`. Absent, empty, `"1"`, `"no"`,
`"TRUE-ish"` — every way of getting the configuration wrong resolves to `false`. Pinned by a `[Theory]`
over exactly those values.

**The before-state is recorded for every row the run would change, in both modes**, and — after a
code-review fix — it is emitted *after the scan completes*, not per row as pages arrive. A scan that fails
on page 3 returns before that point, so the report never contains "this row would change" lines for a run
that then reconciled nothing. What the operator reads and what the write pass would act on are the same
set.

## 7. ADR positions taken deliberately

**ADR-036 A1 rule 4 (retry) — this job does NOT throw from `ExecuteAsync`.** The rule is "throw only when
a retry THIS tick could complete work the tick would otherwise lose". Nothing here is lost: reconciliation
is idempotent by construction, so a rule that failed today is picked up whole by tomorrow's tick — which
is also why **A1.1's lease-outage case needs no catch-up mechanism**, exactly as the task's constraint
asked to be stated rather than built. Against that, the retry policy would re-run an *access-removal*
write path three times in quick succession for no gain. The failure is reported loudly instead
(`Success=false` + `ErrorMessage` + Error log + heartbeat), satisfying ADR-036's "MUST NOT swallow
exceptions". This differs from `GrantExpiryReminderJob`, which *does* throw — correctly, because a
last-day reminder genuinely is lost if today's tick does not send it. The difference is in the work, not
in the discipline.

**ADR-003 — the fail direction is inverted relative to the read path, and that is the point.** For a
READER an empty result on a fault is the ISS-019 hazard: it reads as "no access" and is acted on. For this
WRITER the same empty result is fail-SAFE — it writes nothing. So the requirement is not that a failure
still produces writes; it is that the run is recorded **FAILED** and never reported as "nothing to
reconcile". `ScanFailed` per rule + `Success=false` + heartbeat `status=error` carry that.

**ADR-036 A1 rules 1/2/7 — nothing was written for the lease or the slot guard.** Task 103's
`ScheduledJobHost` + `RedisScheduledJobLease` + slot guard are reused as-is; the job references none of
`ScheduledJobHost` / `IBackgroundJobStore` / `ScheduledJobRegistry` / `IScheduledJobLease` (grep: zero
hits). `JobRunResult.Skipped` is never set by the job — it belongs to the host.

**ADR-010 — Singleton job, `IServiceScopeFactory.CreateScope()` once per `ExecuteAsync`**, mirroring
`GrantExpiryReminderJob`. No new lifetime pattern, no new interface (the job adds zero interfaces).

**ADR-032 — registration is UNCONDITIONAL.** Every dependency is itself unconditional, so there is no
`if (flag)` around the registration and no Null-Object is needed. The job's disabled state is carried by
the *registration data*, not by an `if` around the registration — which is what §F.1's
asymmetric-registration anti-pattern asks for.

## 8. One correctness detail the task's verified-facts list did not cover

The task recorded that deactivation "writes BOTH fields together: `["statecode"]=1, ["statuscode"]=2`
(`ProjectClosureEndpoint.cs:405-406`)". That is true — **on the Web API path**, where the payload is JSON
and bare ints are correct.

`BulkUpdateAsync` is the **SDK** path: `DataverseServiceClientImpl.BuildBulkUpdateTransaction` assigns each
value straight onto an `Entity` (`:2404`), so a state/status attribute must be an **`OptionSetValue`**, not
an `int`. `DeactivationFields()` uses `new OptionSetValue(1)` / `new OptionSetValue(2)` accordingly. Copying
the endpoint's ints would have been a runtime serialization failure, not a compile error.

## 9. Tests — the closed set, and what each safety property costs to break

`tests/integration/auth/UnifiedAccessControl/ExternalAccessReconciliationTests.cs` — **37 tests**
(ADR-038 KEEP path #2, authorization behaviour).

**Why the fake does not evaluate the FetchXML filter** (deliberate; it makes the tests stronger). The fake
is a small ROW STORE: it serves every row of the scanned table, pages it, and applies `BulkUpdateAsync`
back into itself. Each rule is therefore proven by the **in-code** decision with the server-side filter
removed as a variable — a row the filter would have excluded is fed in anyway and must still be left
alone. The filter is a bound on volume, not on correctness, and it is pinned separately by direct
assertions on the emitted FetchXML. Applying writes back into the store is also what makes the idempotence
test real: the second run sees what the first one wrote.

### Perturbation (mandatory, review-2026-08-24) — 11 properties, both directions

Files were backed up to a scratchpad copy and restored from it (never `git checkout` / `git stash`).
Baseline before every perturbation: **37/37 pass**. Restored state re-verified byte-identical by md5.

| # | Property broken | Result |
|---|---|---|
| 1 | Per-chunk **atomic claim** removed | **1 fail** — `S3_AChunkWhoseClaimIsAlreadyHeld…` |
| 2 | **Completion marker** never consulted | **1 fail** — `S3_AChunkWhoseCompletionMarkerIsAlreadyPresent…` ⚠️ see note |
| 3 | `BulkUpdateAsync` → **per-row writes** (`ChunkSize=1`) | **1 fail** — `S4_AChunkWhoseTransactionFails…` |
| 4 | **`statuscode` dropped** from the deactivation | **2 fail** — R2 and R3 deactivation tests |
| 5 | **Report-only default inverted** | **6 fail** — all `S1_*` |
| 6 | Report-only no longer **suppresses the write pass** | **6 fail** — all `S1_*` |
| 7 | Job **ships enabled** | **1 fail** — `S2_TheJobShipsDisabled…` |
| 8 | **Null statecode** no longer ACTIVE | **1 fail** — `ARowWhoseStatecodeIsNull…` |
| 9 | Failed scan **not recorded FAILED** | **1 fail** — `AScanThatFails…` |
| 10 | **Truncation hidden** (`truncated` starts false) | **1 fail** — `AScanThatExceedsThePageCeiling…` |
| 11 | R3 **boundary moved** (`>= today` → `> today`) | **1 fail** — and only the `offsetDays: 0` case, which is exactly the boundary |
| — | **All restored** | **37/37 pass** |

⚠️ **Property 2 took two attempts, and the first attempt is the interesting one.** Removing *one* of the
two `IsEventProcessedAsync` checks left the suite **green**, because the second check — the one under the
claim — still caught it. That is correct defence-in-depth (ADR-036 A1 rule 3's re-check exists precisely
because the claim is check-then-set and fails open, #984), and the perturbation was simply too weak to
reach the property. Removing **both** checks reddens the test. Recorded because a green result from a
too-weak perturbation is indistinguishable from an untested property unless you say which one it was.

Two perturbations initially failed to *compile* (`CS0162`, unreachable code, under
`TreatWarningsAsErrors`); both were reissued in compiling form. A perturbation that does not build has not
tested anything.

## 10. Exact numbers

| Measurement | Before | After |
|---|---|---|
| `Sprk.Bff.Api` build | — | **0 errors, 0 warnings** (`TreatWarningsAsErrors=true`) |
| `Sprk.Bff.Api.Tests` | 12,538 passed / 0 failed / 58 skipped / 12,596 | **12,575 passed / 0 failed / 58 skipped / 12,633** (+37 = exactly this task's tests) |
| `Spaarke.ArchTests` | — | **323 passed / 0 failed** |
| Vulnerable packages | — | **none** (`dotnet list package --vulnerable --include-transitive`) |
| New package references | — | **zero** |

## 11. 🔴 Publish size — NOT MEASURED, and the reason is structural

CLAUDE.md §10 requires the delta against a **fresh worktree of `origin/master`**, with the branch side
also published from a **fresh** worktree at a **short path**, file counts compared on both sides
(hazards 3 and 4).

**That measurement is not available to me**: a `git worktree` is created from a *commit*, and this task's
changes are uncommitted — committing is outside this session's remit. A fresh-master vs
iterated-branch-worktree comparison is precisely the hazard-3 measurement CLAUDE.md documents as
untrustworthy (a measured **+4.95 MB** delta that did not exist), so producing it would be worse than
producing nothing.

**No number is asserted.** What can be stated without measuring: the change adds **one** `.cs` file
(849 lines, 227 of them XML doc) and **zero** package references, project references or transitive
dependencies — so there is no mechanism for a material delta. **Obligation**: run the §10 procedure on
both sides after this work is committed, before merge.

## 12. §10 Placement Justification + §11 Component Justification

**§10 Placement** (per [`.claude/constraints/bff-extensions.md`](../../../.claude/constraints/bff-extensions.md)
§A + §D). The workload runs **in the BFF**, on the in-process `Spaarke.Scheduling` host. ADR-052 signals:
it is BFF domain code over BFF-owned tables (`sprk_externalrecordaccess` grant semantics live in
`ExternalGrantLifecycle`), it uses BFF identity, it follows BFF release cadence, and it is two queries a
day (ADR-052 B2/B3). The one signal that leans out-of-process — *one dispatch per schedule* (F3) — is
already met **in place** by task 103's lease. Moving it to Functions or a Container Apps Job would cost a
deployable per stamp plus the extraction of `ExternalGrantLifecycle`, for two queries a day; ADR-052's
tie-breaker is *fewer moving parts*. Inside the BFF this is schedule-driven, so ADR-036 `IScheduledJob` via
`AddScheduledJob<TJob>` — **not** a hand-rolled timer `BackgroundService`, and not ADR-004 (nothing queues
it). §D checklist: no new endpoint, no new package, no AI coupling, feature-module DI (registered in
`ExternalAccessModule`, not `Program.cs`).

**§11 Component Justification** — the job is new surface, so the three questions fire:

1. **Existing** — four neighbours, each checked. `GrantExpiryReminderJob` (same module, same table, daily
   paged scan — but it only READS and notifies, and its window is `today..+30`, so it cannot see an undated
   or a stale row). `MembershipReconciliationJob` (the recon *shape*, but a different junction,
   `sprk_userentityassociation`, against different sources of truth). `ExternalOrganizationMembership`
   (`RevokeExternalAccessEndpoint.cs:748`) and `ExternalParticipationService.QueryActiveOrgIdsAsync` —
   both org-scoped junction readers.
2. **Extension** — no, for a mechanical reason rather than an aesthetic one. Both junction readers sit on
   `DataverseWebApiClient.QueryAsync`, which issues **one GET** and never reads the `@odata.nextLink` it
   declares (`:227-246` vs `:296-297`), under a 200-row bound; a reconciliation scan is unbounded by
   nature, so that client cannot serve it, and its `UpdateAsync` (`:204-214`) is one untransacted PATCH per
   row. `GrantExpiryReminderJob` has no write path and the wrong window. **What IS reused rather than
   rebuilt**: `IGenericEntityService` paging, `BulkUpdateAsync`, `ExternalGrantLifecycle`'s date rule
   (including the single `DefaultExpiryDays`), `IIdempotencyService`, and the `AddScheduledJob` host. No
   new package, client, store, scheduler or date constant.
3. **Cost of doing nothing** — three concrete, named failures. (a) Deactivating an organization record —
   the obvious operator gesture for "this firm is no longer engaged" — is a silent no-op: its grant rows
   stay active and every member keeps inherited access, with no error and no surface that shows it.
   (b) A membership ended by date keeps conferring indefinitely, because nothing in the repo writes that
   junction at all. (c) An externally-created grant row with no `sprk_expiresdate` is invisible to task
   100's reminders (which select only `today..+30`), so nobody is ever told it is dead — which, post-107,
   is now a *silent denial* rather than a silent grant.

**§11.5 cohesion** — three rules in one job is ONE responsibility ("make the row's own state the truth"),
one reason to change. 849 lines / 525 code lines, comparable to the canonical `GrantExpiryReminderJob`
(688 / 490); 4 ctor params (ADR-010 threshold 5); zero new interfaces.

## 13. Step 9.5 quality-gate findings (all fixed in-session)

| # | Gate | Finding | Disposition |
|---|---|---|---|
| 1 | code-review (AI smell) | Five `#pragma warning disable CA1031` blocks. **CA1031 is not enabled anywhere in this repo** (zero other uses; the sibling `GrantExpiryReminderJob` catches `Exception` bare and builds clean). Suppressing a rule that is off is defensive noise. | **Fixed** — pragmas removed, the *reasons* kept as plain comments. Build still 0/0. |
| 2 | code-review (honest reporting) | Before-state lines were logged per row as pages arrived, so a scan that failed on page 3 emitted "this row would change" claims for rows the run then never touched. | **Fixed** — logging moved to after the scan completes; a failed scan now emits none. |
| 3 | code-review (§11.5 cohesion) | `ExecuteAsync` was 131 lines, most of it a 24-argument heartbeat call — a second reason-to-change (log format) inside the orchestrator. | **Fixed** — extracted `LogHeartbeat(...)`. |
| 4 | adr-check (**ADR-038 ban B3**) | My own `S2` registration test asserted `Job.Should().BeOfType<…>()` and the cron — wiring assertions, and `BeOfType` is redundant since `Single(r => r.Job.JobId == …)` already selects by id. | **Fixed** — reduced to the single safety assertion (`Enabled == false`), renamed to `S2_TheJobShipsDisabled_SoEnablingItIsAnOwnerAction`, with a remark explaining why it is not a B3 wiring test. Perturbation 7 confirms it still reddens. |

adr-check otherwise **clean**: ADR-001/052 (no Functions, no Durable Task, no hand-rolled timer
`BackgroundService`), ADR-036 A1-6/A1-7 (registered via `AddScheduledJob`, zero host-type references),
ADR-007 (no Graph), ADR-009 (no `IMemoryCache`), ADR-010 (zero new interfaces, 4 ctor params),
ADR-013 (no AI types), ADR-028/A4 (no secrets, no bearer handling), ADR-038 (no `Mock<HttpMessageHandler>`,
no ctor null-check tests, no reflection).

## 14. Single definition of the 90-day default — grep evidence

```
grep -rn "DefaultExpiryDays" src/ tests/
  src/…/Infrastructure/ExternalAccess/ExternalGrantLifecycle.cs:160   internal const int DefaultExpiryDays = 90;   ← THE definition
  …plus 8 reference sites (GrantExternalAccessEndpoint ×4, ExternalGrantLifecycle ×3 doc/usage, tests ×5)
```

This task adds **no** literal `90` and **no** second constant: R1 calls
`ExternalGrantLifecycle.DefaultExpiry(today)` and `ExternalGrantLifecycle.ToSdkDateOnly(...)`. The
behavioural half is pinned by `R1_AnActiveUndatedGrant_IsStampedWithTheOneDefinedDefaultExpiry`, which
asserts equality against `ExternalGrantLifecycle.DefaultExpiry(Today)` rather than a literal date — so a
second constant that drifted would redden it.

## 15. Deliberately NOT done

- **No R4 for `sprk_startdate`** — escalation trigger 2, see §4.
- **No READ guard for ISS-026** — the task scopes this out explicitly: it touches the same
  `ExternalParticipationService` query that tasks 109+110 are rewriting as a single snapshot, and splitting
  one query's correctness across two tasks would re-introduce the two-snapshot hazard task 043 removed.
- **No Dataverse plugin / business rule / schema default** — ruled out repo-wide by D-1. `src/dataverse/**`
  is untouched (`git status --porcelain -- src/dataverse/` → empty).
- **No enabling of the job and no write to live data** — owner action, gated by two switches (§6).
- **No publish-size number** — §11.

## 16. Follow-on obligations

| # | Obligation | Owner |
|---|---|---|
| 1 | Decide the `sprk_startdate` fail direction (§4); most likely home is the 109+110 read guard, not this writer | owner + tasks 109/110 |
| 2 | Re-take the §5 before-state counts against any non-dev environment **before** enabling writes | operator, pre-enable |
| 3 | Run the CLAUDE.md §10 fresh-worktree publish-size comparison once this work is committed | reviewer, pre-merge |
| 4 | Enabling is TWO actions, not one: `enabled: true` at the registration **and** `ExternalAccess:Reconciliation:WritesEnabled=true`. Run report-only first (admin `trigger`) and read the before-state lines | owner |
