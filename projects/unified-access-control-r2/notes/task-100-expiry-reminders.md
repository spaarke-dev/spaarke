# Task 100 — expiry reminders at 30 / 14 / 7 / 3 / 1 days (FR-33 (d))

> **Status**: implemented `e7bd02189` (2026-09-12). Verification in §8.
> **POML**: [`tasks/100-fr33-expiry-reminder-job.poml`](../tasks/100-fr33-expiry-reminder-job.poml)
> **Code**: `src/server/api/Sprk.Bff.Api/Services/ExternalAccess/GrantExpiryReminderJob.cs` ·
> `Infrastructure/DI/ExternalAccessModule.cs` · tests
> `tests/integration/data-mutation/ExternalAccess/GrantExpiryReminderJobTests.cs`

---

## 1. What it does

Once a day (06:00 UTC), on the existing in-process `Spaarke.Scheduling` host, the job finds every **active**
external grant (`sprk_externalrecordaccess`) whose `sprk_expiresdate` is **exactly** 30, 14, 7, 3 or 1 days
away. For each one it writes one in-app notification (Dataverse `appnotification`, the model-driven app's
bell) to the internal user who can renew it. It then logs a heartbeat with its counts, even when nothing is due.

The reminder reads, for example:

> **External access expires in 7 days**
> Jane Doe will lose access to the matter "Smith v. Smith" on 2026-09-19. To keep it, set a new expiration
> date in Manage Access on the matter.

An organization grant reads "Members of {organization} will lose access…". The notification deep-links to the
record. Priority is Informational at 30 and 14 days, Warning at 7 and 3, and Critical at 1.

---

## 2. Premise corrections (the POML was wrong twice — counter now THIRTEEN)

| # | POML said | Reality (evidence) | Resolution |
|---|---|---|---|
| 1 | Step 0: "determine how the granting internal user is recorded on the grant row" — implying it is there | **20 of 28** active dev grants have **no `sprk_grantedby`** — every grant created 2026-08-11 → 08-25; grants from 2026-09-09 on have it. Every grant row's `createdby`/`ownerid` is a **BFF service identity** (`# mi-bff-api-dev`, `SDAP-BFF-SPE-API`). The spec's fallback "matter owner" is a **business-unit team** on **10 of 11** root records (`Spaarke Business Unit 1`). Read-only Web API queries, 2026-09-11. | **Escalation trigger fired** → owner decision (§3). |
| 2 | Use `OutboxService` (`sprk_notificationoutbox`) for the notification | The outbox's `kind` taxonomy is closed, and the client's `Spaarke.Notifications` `KindRouter` **skips unknown kinds** with only a `console.warn`. SpaarkeAi registers handlers for only the three active kinds. A reminder written there would reach **no screen**. Making it visible needs client work outside this task. | **Owner decision** (§3): use the existing `NotificationService` (`appnotification`). |
| — | (session 12) The job's own catch assumed a cancelled send surfaces as `OperationCanceledException` | `NotificationService.CreateNotificationAsync` catches every exception except `ArgumentNullException`, logs it, and throws `InvalidOperationException("Failed to create notification …", ex)` — cancellation included (`NotificationService.cs:119-127`). A host shutdown mid-send was therefore counted as a **failed write**. Found by the new test `ExecuteAsync_CancelledDuringASend_ReportsCancelledNotFailed`. | The job now decides by **its own token** (`ct.IsCancellationRequested`), whatever the exception type; a timeout (token not cancelled) still counts as failed. |
| — | Scheduling pattern: `SessionFilesCleanupJob` (a raw `BackgroundService` + `PeriodicTimer`) | Not wrong, but the BFF has a canonical cron framework, `Spaarke.Scheduling` (`IScheduledJob` + `ScheduledJobHost` + admin run history / trigger-now / enable-disable). `MembershipReconciliationJob` and `PlaybookSchedulerJob` use it. | Directional deviation (steps are `mode="directional"`): used `IScheduledJob`. It gives run history and an operator pause switch for free, so no feature flag is needed. |

---

## 3. Owner decisions (2026-09-11, recorded at the escalation)

| Question | Answer |
|---|---|
| When `sprk_grantedby` is empty, who gets the reminder? | **"Fall back to a person"**: `sprk_grantedby`, else the record's owner **if it is a person**, else the record's creator **if it is a person** (not an app identity). If none exists, the grant is **unroutable**: counted in the heartbeat and logged at Error, which can be alerted on. |
| Which channel? | **"MDA notification bell"**: the existing `NotificationService` (`appnotification`). It needs in-app notifications switched on for the model-driven apps (**operator step**, §7). |

"Person" in code means: the joined `systemuser` row positively reports `isdisabled = false` **and** has no
`applicationid`. If the join columns are absent, the candidate is treated as "not established" and the chain
falls through to the next one.

Live effect in dev (FetchXML run 2026-09-12, 25 grants due on 2026-12-10): **5** route to the granter and
**20** to the record's creator. **0** are unroutable.

---

## 4. Design decisions

| Decision | Why |
|---|---|
| ~~**Exact days, no catch-up**~~ → **Catch-up** (owner decision 2026-09-14, session 12 rewrite) | Superseded. The job now queries every active grant expiring **today through today + 30** and sends the **most urgent threshold whose day has come** — the smallest of 30/14/7/3/1 that is ≥ the days left — once per (grant, expiry date, threshold). A missed day is caught up the next day; several missed thresholds collapse into the most urgent one; a missed 1-day reminder still goes out **on the expiry day** (access holds through it). Why: a Redis outage now means the tick is not dispatched at all (ADR-036 A1.1), deploys and exhausted retries also skip a day, and with exact-day matching each of those silently lost a reminder. The POML criterion "a grant expiring in 20 days produces no notification" is superseded: it now holds only when that grant's 30-day reminder was already sent. A grant first created 20 days out gets a reminder the next morning, worded with the real days left. |
| **Wording by the days actually left** | Title "External access ends in N days / tomorrow / today"; body "Access for {grantee} to the {label} "{name}" ends after {date}." — access holds **through** the expiry date (read filter `ge today`), so "on {date}" was wrong. Priority from the days left. |
| **Retry rule** (ADR-036 A1 rule 4) | The run throws **after its heartbeat** when it made no progress (the query failed) or a reminder failed on the grant's **last day** — no later run can send it. Other failed reminders are counted (`partial`) and tomorrow's run catches them up. Sent reminders are skipped on a retry by their markers. |
| **Person** | Enabled ∧ no `applicationid` ∧ `accessmode` ∈ {0 Read-Write, 1 Administrative, 2 Read}. Live dev has Support (3) and Delegated Admin (5) users, who must not be the one reminded. |
| **`DateOnly`, UTC calendar** | `sprk_expiresdate` is Date Only, TimeZoneIndependent. "Today" is `ExternalGrantLifecycle.TodayUtc`, the same calendar the read filter enforces expiry against (task 007/097). |
| **ONE FetchXML query, all joins OUTER** | NFR-02. The one query returns the grant plus the granter, the contact/organization name, and each root's name, owning user and creator, each user with `isdisabled` / `applicationid`. That is 12 link-entities; Dataverse allows 15. Every join is **outer**, so grants with no granter or a team owner still come back. An inner join would silently lose exactly the grants the fallback chain exists for. The query pages only past 5,000 grants due on the same day. |
| **FetchXML, not `QueryExpression`** | The **same string** can be executed live through the Web API (`?fetchXml=`), and it was (§6). That verifies column names, aliases and the `in`-over-date operator against real Dataverse, not against our assumptions. |
| **Idempotency key = (grant, expiry date, threshold)** | Via `IIdempotencyService`: check, then lock, then send, then mark (35-day marker), then release. The expiry date is part of the key, so **renewing restarts the reminders**. Across days, exact-day matching already prevents repeats. The marker guards same-day re-runs (restart, admin trigger-now, a second instance). |
| **Unroutable ≠ run failure** | The run still reports `Success = true`, with `unroutable = N` and an Error log per grant. A **failed write** makes the run `partial` (`Success = false`). A **failed query** makes it `error`. |
| **Heartbeat** | One structured log line per run (`[GRANT-EXPIRY-REMINDER] heartbeat status= today= due= sent= alreadySent= unroutable= failed= skipped=`), at Information when `ok` and Warning otherwise, plus the same counts in `JobRunResult.ResultJson`. The scheduler keeps that JSON as run history (`/api/admin/jobs`). "Nothing due" is `status=ok due=0`; "died" is `status=error`, or no heartbeat that day. |
| **Registration: unconditional** | All dependencies (`NotificationService`, `IIdempotencyService`, `IGenericEntityService`, `TimeProvider`) are unconditional. The operator's pause is the scheduler's admin enable/disable, so ADR-032 needs no Null-Object. |

---

## 5. Known limits (accepted, stated rather than hidden)

1. **`IIdempotencyService` fails OPEN** (#984). If the distributed cache is unreachable the check says "not
   sent". In practice this job is not dispatched at all while Redis is down (ADR-036 A1.1 — the scheduler lease
   lives in the same Redis), so the window is a cache that fails *between* the lease and the send.
2. **Its lock is check-then-set, not atomic** (#984). No longer a live risk for this job: the scheduler's lease
   (task 103) runs it on one instance at a time, and the job checks "already sent" again under the claim.
3. **Without Redis** (`IDistributedCache` falls back to in-memory — Development/Testing only), the marker does
   not survive a restart. Deployed environments refuse to start without Redis (`CacheModule`).
4. ~~No catch-up~~ — catch-up is now the design (§4). A missed day costs one day's delay, not the reminder.
5. **The bell must be switched on.** `appnotification` rows exist regardless, but the model-driven app shows
   them only when in-app notifications are enabled for that app. Dev has **zero** `appnotification` rows today,
   and the per-app setting could not be read back (settingdefinition query returned nothing). **Operator step**, §7.
6. **Only BFF-written expiries are bounded** (#974). A grant created outside the BFF with no expiry is never
   reminded, because it never expires.

---

## 6. Live evidence (read-only, dev `spaarkedev1`)

| Check | Result |
|---|---|
| Active grants / with `sprk_grantedby` | 28 / 8 (2026-09-11) |
| Grant `createdby` / `ownerid` | BFF service identities only |
| Root owners (11 distinct roots) | 10 × team `Spaarke Business Unit 1`, 1 × user |
| Primary-name columns | `sprk_project` → `sprk_projectnumber` (display name used: `sprk_projectname`, exists) · `sprk_matter` → `sprk_matternumber` (`sprk_mattername` used, exists) · `sprk_workassignment` → `sprk_name` · `sprk_organization` → `sprk_organizationname` · `contact` → `fullname` |
| Columns exist | `systemuser.applicationid` (Uniqueidentifier), `systemuser.isdisabled` (Boolean), `owninguser` on all three roots |
| The job's FetchXML, run via Web API `?fetchXml=` with `in (2026-12-10, 2026-10-12)` | **25 rows**, aliased columns `gb.isdisabled`, `mt.createdby`, `mtc.isdisabled`, `ct.fullname`, `mt.sprk_mattername` returned exactly as the job reads them; human users carry no `applicationid` |

No live writes.

---

## 7. Operator / owner follow-ups

| Item | Why |
|---|---|
| **Enable in-app notifications** on the model-driven apps internal users work in (Matter Management / Spaarke Platform): app designer → Settings → Features → *In-app notifications* | Without it the reminders are written but **not shown**. |
| **Alert on** `[GRANT-EXPIRY-REMINDER] UNROUTABLE` (Error) and on **a day with no heartbeat** | These are the two failure modes the job cannot fix itself. |
| **097 still must not reach a non-dev environment without this task deployed**, and this job must be **live by 2026-11-10** (60 days after 097's backfill; first reminders for the backfilled grants fall on 2026-11-10 = 30 days before 2026-12-10) | Unchanged sequencing constraint. Deploy is task 047 / operator. |
| **Smoke on first deploy**: `POST /api/admin/jobs/external-grant-expiry-reminders/trigger`, then read the run's `ResultJson` | First live execution through the BFF. There is no Dataverse test in CI (owner directive 2026-09-10). |

---

## 8. Placement Justification (root CLAUDE.md §10, `.claude/constraints/bff-extensions.md`)

| Criterion | Answer |
|---|---|
| Latency budget against BFF state? | No. It is a daily batch. |
| Writes BFF session/audit state in the same request? | No. |
| Event-driven (timer) with no user wait → Functions per ADR-001? | Timer-driven, **but kept in the BFF**. It reuses the BFF-owned grant-table knowledge (`ExternalGrantLifecycle`), the BFF's `NotificationService` and `IIdempotencyService`, and the in-process `Spaarke.Scheduling` host that the platform adopted for exactly this class of job (R3 FR-2.x; `MembershipReconciliationJob` is the nightly precedent). A Function would duplicate Dataverse auth, configuration and the notification code for one query a day. ADR-001 *permits* Functions for narrow out-of-band work; it does not require them. |
| New packages? | **None.** |
| CRUD→AI dependency? | **None.** No AI-internal type is referenced. |
| DI convention | Registered in the existing `ExternalAccessModule`. No `Program.cs` line. |

**§11 component justification.** *Existing*: `Spaarke.Scheduling` (scheduler), `NotificationService`
(channel), `IIdempotencyService` (de-dup): all reused. Nothing scans grant expiry today (grep: no reader of
`sprk_expiresdate` outside the grant write path and the read filter). *Extension*: one job class is the
minimum; it adds no scheduler, channel or store. *Cost of doing nothing*: every external grant now expires,
and nobody is told first. The external user is locked out mid-work (spec FR-33 sequencing constraint).

**Hot path**: BFF only (`Services/ExternalAccess/**`, `Infrastructure/DI/ExternalAccessModule.cs`). No
SpaarkeAi, CI, skill or root-CLAUDE change.

---

## 9. Verification

_(filled in below as runs complete)_
