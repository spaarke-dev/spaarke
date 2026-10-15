# Playbook engine defect sweep: engine, executors, the 7 notification playbooks and the AI playbooks

> **Status:** investigation only. I made no code change, no commit and no Dataverse write. I ran no playbook and did not trigger the scheduler.
> **Date:** 2026-10-08/09. **Code basis:** `origin/master` @ `e9f08b764` (PR #1461 merged). I read it through a scratch worktree at `C:\wtinvpb`, which I removed at the end.
> **Live basis (spaarkedev1, read-only):**
> - MCP `describe` and SQL reads of `appnotification`, `sprk_analysisplaybook`, `sprk_playbooknode`, `systemuser`, `sprk_document`, `sprk_event`, `sprk_communication` and `sprk_workassignment`;
> - read-only Log Analytics queries on workspace `74b7349a-f88d-45a7-b180-8728807a85d7` (App Insights for `spaarke-bff-dev`).
>
> **Dry-run mode:** none exists. The orchestrator, executors and scheduler have no simulate or no-write switch (I searched `Services/Ai/**` for `dryRun|simulate`). So nothing was executed. Every runtime behaviour below is read from code, or from the D-89 run telemetry already in App Insights.
> **Marking:**
> - **INFERRED** = reasoned from code or evidence but not observed.
> - **VERIFIED-LIVE** = observed in Dataverse or telemetry.
> - **VERIFIED-CODE** = read directly in the code, with the line given.

---

## 0. Headline

1. **Nothing delivers correctly end to end today, even with D-89's two defects fixed.** Fixing the Condition gate (PB-01) and the priorities (PB-03) alone would still leave five problems:
   - every notification is duplicated on every run, because the dedup query names a column `appnotification` does not have (**PB-05**, VERIFIED-LIVE);
   - the Daily Briefing files every playbook notification under the "system" channel, because the writer never puts `category` into `customData` (**PB-06**, VERIFIED-LIVE);
   - New Documents notifies users about their own uploads (**PB-11**, VERIFIED-LIVE data);
   - titles and bodies can exceed the column lengths (**PB-10**);
   - date-only values render as `2026-10-10T00:00:00.0000000` (**PB-09**).
2. **The engine's skip semantics are wrong in two places, not one.**
   - A false Condition with no `falseBranch` gates nothing (PB-01, known).
   - In addition, a skipped node's output is never stored, so **skipping never propagates**: a node that depends on a skipped node runs anyway, with an empty input (**PB-02**, VERIFIED-CODE).
3. **A single Playbook Designer save destroys a repo-deployed playbook** (**PB-08**, VERIFIED-CODE, the effect is INFERRED). `NodeService.SyncCanvasToNodesAsync` deletes every node without `__canvasNodeId`, which is every node the deploy script writes. It then recreates the nodes as stubs. It maps the canvas types `control`/`aiAnalysis`, used by 6 of the 7 notification canvases, to `AiAnalysis(0)`. This is the mechanism behind ISS-018c. The D-80 audit records such a save but does not prevent it.
4. **The "ownerid" membership is a bug, not junction lag** (**PB-07**). `MembershipResolverService.MaterializeResults` credits a row to every role whose column is *populated*, not to the roles that actually matched the user. Live notifications show `memberships: [createdBy, ownerid, assignedattorney, assignedparalegal]` for one user on one matter. There is no junction: the resolver queries Dataverse directly with a 2-minute cache. This belongs to uac-r2.
5. **The scheduler runs "daily" playbooks every 25 hours, drifting through the clock** (11:00 → 18:00 over 7 days, VERIFIED-LIVE). With a fixed 24-hour lookback, about 1 hour in 25 of new documents, emails, events and work assignments is never notified (**PB-14**).
6. **The D-78 alert has a blind spot.** A playbook that fails for 14 of 15 users is a "PartialFailure": its `lastrundate` advances, nothing retries and no alert fires. D-89's Due Soon and Work Assignments were exactly that (**PB-15**).
7. **The same option-set defect exists outside playbooks**, in code that has not fired yet:
   - `IncomingCommunicationJobHandler` (the admin alert for a held email) always sends priority `200000002`;
   - `GrantExpiryReminderJob` sends `200000002` when one day is left.

   Both are invalid, so both alerts will fail when needed (**PB-03b**).
8. **AI side (§9).** Of 27 live non-notification playbooks, only U.S. Patent Office Action Review is likely to finish with useful output.
   - `matter-health-single`, the only node-engine playbook in production (insights-ask), **always** ends in the fallback decline: wrong `from` names, no synthesis prompt, wrong output path, PB-02 on the decline path (AI-01).
   - The workspace ai-summary always falls back (AI-04).
   - `universal-ingest@v1` is not deployed (AI-03).
   - Several executors type config fields as `string`, which repeats the ISS-018b class (AI-EX1/EX2).
   - CreateTask reports success when nothing was created (AI-EX3).
   - AiAnalysis is labelled read-only yet can call write tools (AI-EX7, a security review for uac-r2).

---

## 1. Orchestration semantics: intended vs actual

Code: `PlaybookOrchestrationService.cs` (PO), `ExecutionGraph.cs` (EG), `PlaybookRunContext.cs` (RC), `NodeService.cs` (NS).

| # | Topic | Intended semantics (what an author would assume, and what the docs say) | Actual (master e9f08b764) | Deviation |
|---|---|---|---|---|
| 1.1 | Load | Only active nodes, ordered, of an active playbook. | NS:77-106 loads every row for the playbook (any `statecode`), ordered by `sprk_executionorder`. EG:26 filters on `sprk_isactive`. Node `statecode` is ignored, and so is the playbook `statecode` (the scheduler filters playbooks itself; other entry points do not, INFERRED). | Minor: a deactivated node row (`statecode=1`, `isactive=true`) still runs. |
| 1.2 | Ordering | Topological order; `executionorder` breaks ties. | EG:136-193 runs Kahn batches; within a batch the nodes are sorted by `ExecutionOrder` but **run in parallel** (PO:718-734, throttle 3). | `executionorder` is not a sequencing guarantee. Designer playbooks without edges run fully in parallel. |
| 1.3 | Cycles | Rejected. | EG throws `InvalidOperationException` → RunFailed. Correct. | — |
| 1.4 | Inactive dependency | Either block the dependent or report a config error. | EG:44-52 **silently drops** the edge, so the dependent runs in an earlier batch without the input. | PB-21 |
| 1.5 | Condition true/false | The node named by the selected branch runs; the other branch is skipped; **no branch selected → no dependent runs**. | PO:983-1018 counts a dependency as a "branching gate" only when `selectedBranch` is a non-null string. A false Condition with no `falseBranch` has `SelectedBranch=null`, so it is not counted and the dependent runs. | **PB-01** (D-89 #1) |
| 1.6 | Branch target | Names a node. ConditionNodeExecutor's schema says "Node **OutputVariable** name" (:87, :93, :576-582). | PO:997 compares it with `node.Name` (case-insensitive). The repo JSONs use the node name ("Create Notification"), which works. An author who follows the schema gets the TRUE branch skipped. | PB-20 |
| 1.7 | Transitive skip | A node whose upstream was skipped is skipped too, unless another path selects it. | The skip paths return `NodeOutput.Ok(null)` **without `runContext.StoreNodeOutput`** (PO:1004-1017, 1030-1042; StoreNodeOutput appears only at 1080/1097/1126/1164/1212/1246/1287/1302/1376). Downstream, `GetOutput(dep)` is null, so the branch loop `continue`s (:991) and the failure loop `continue`s (:1027). **The dependent runs**, with `{{skipped.*}}` rendering empty. | **PB-02** |
| 1.8 | Nested conditions | `and`/`or`/`not` compose. | ConditionNodeExecutor:315-326 handles them correctly. Validation recurses (:149-209). | — |
| 1.9 | Dependency failure | A failed node fails the run; continue-on-error when configured. | PO:737-757 stops at the first failed node *of a batch*, after its siblings have already run (their side effects stay). The "Dependency failed → skip" branch (:1021-1045) is effectively unreachable. `sprk_continueonerror` is not implemented (GitHub #233). `sprk_retrycount` / `sprk_timeoutseconds` are loaded (NS:592-593) and never enforced: only `NodeExecutionContext.cs:145,151` exposes them, and nothing calls them. | PB-27 |
| 1.10 | Partial vs total | Per user: the run fails on any node failure. Per playbook: see §5. | As intended per user. A CreateNotification failure on item *k* aborts after *k−1* creates (CreateNotification:502-574 has no per-item try/catch); the next run re-creates them (dedup is broken, PB-05). | PB-05 amplifies it |
| 1.11 | Zero items | Nothing to notify → run **Succeeded**. | Without a gate (PB-01), CreateNotification still runs. Its `Validate` (:232-236) requires a top-level `body`, which is `{{#each q.output.items}}…{{/each}}` and renders `""`, so the run fails "Notification body is required". In iterate mode the executor itself would return Ok(created=0) (:484-496) if validation let it through. | PB-01 + PB-23 |
| 1.12 | Output binding | Unique `outputVariable`; downstream reads `{{var.x}}` or `{{var.output.x}}`. | Layer 1 context offers both shapes (PlaybookTemplateContextBuilder:287-343). The **executor-side** contexts of Condition (:520-561) and CreateNotification (:700-738) offer **only** `{{var.output.x}}`, with no Parameters and a reduced `run`. Duplicate `outputVariable`s overwrite silently at runtime (RC:238); only `ValidateAsync` checks them. | PB-24 |
| 1.13 | Rendering phases | Layer 1 renders config once, then executors render executor-scoped roots (`item`). | As intended after #1461 (PO:2466-2478 leaves `item` leaves verbatim for CreateNotification). Two leftovers: (a) executors **re-render** already-rendered text (CreateNotification:288-289; Condition ResolveOperand :376-384), so record data containing `{{…}}` is evaluated as a template; (b) Handlebars parse errors return the raw template silently (TemplateEngine:556-564), as `templateParameters` `{{x ?? 'y'}}` does today. | PB-25, PB-27 |
| 1.14 | Fan-out (`iteration`) | Iterate a collection; a bad expression is an error. | A render or parse failure becomes **zero iterations, Ok** (PO:2061-2075, 2196-2218). | PB-26 |
| 1.15 | `IsActive` / node type | — | `NodeType` is always `AIAnalysis`: `sprk_nodetype` was dropped from the schema and `$select`, but `NodeEntity` still maps it, so it is null and defaults (NS:579). Every node therefore runs `ResolveNodeScopesAsync` (PO:1110; 3 N:N GETs) on top of MapToDto's 3 N:N GETs. `ValidatePromptSchemasAsync` (PO:1718) treats every node with an Action FK as an AI node. | PB-18 |
| 1.16 | Validate endpoint | Validates structure. | `ValidateAsync` errors on every node without an Action FK (PO:327-331 → `GetActionAsync(Guid.Empty)` → 404 → null), so every notification playbook "fails validation". | PB-19 |

---

## 2. Executor input contracts vs live Dataverse metadata (notification executors)

Live `appnotification` (`describe`, VERIFIED-LIVE):
- `title` NVARCHAR(256); `body` NVARCHAR(**500**); `data` multiline;
- `priority` {Normal **200000000**, High **200000001**};
- `toasttype` {Timed **200000000**, Hidden **200000001**};
- `icontype` {100000000-100000005};
- `sprk_category` / `sprk_regardingid` / `sprk_regardingtype` / `sprk_source` / `sprk_playbookrunid` NVARCHAR(100);
- `sprk_isread` BIT; `sprk_briefingstate` {0,1,2};
- **no `statecode`/`statuscode`** (a SQL select of `statecode` fails: "doesn't contain attribute with Name = 'statecode'").

| Executor (type) | Required config | What it writes / reads in Dataverse | Typing of rendered values | Mismatches vs live |
|---|---|---|---|---|
| **QueryDataverse (51)** | `entityLogicalName`, `fetchXml` (:103-106). Optional `parameters.{dueSoonDays,timeWindowHours}` (defaults **7**/24, :27-28; the scheduler passes 3/24, an inconsistent default). | Read only. `RetrieveMultiple(FetchExpression)` runs as the BFF MI; `eq-userid`/`ne-userid` are rewritten to the run user (:272-280); the shape validator runs first (:175). | `ConvertAttributeValue` (:307-322): EntityReference → GUID string (**name dropped**); OptionSet → int (**label dropped**); Money → decimal; **DateTime → `ToString("o")` for date-only and date-time alike** (date-only → `2026-10-10T00:00:00.0000000`, user-local → UTC `…Z`). `FormattedValues` are discarded. AliasedValue keys keep `m.sprk_mattername`. `top=50`, no paging. | PB-09, PB-29. Lookup display names come only from link-entity aliases. |
| **LookupUserMembership (52)** | `entityType`; `targeting` ∈ {`people`, omitted}; `roles` optional; `includeRelated` is a no-op (:293-300). | Read only (MembershipResolverService; 2-minute cache; `CachesEntityType` exceptions). | Outputs `{entityType,count,ids,byRole,continuationToken,cacheExpiresAt}`. `ids` truncated at `DefaultLimit`; the continuation token is ignored by the node (INFERRED: matters beyond the limit are silently left out). | **PB-07** (`byRole` over-attribution) |
| **Condition (30)** | `condition{operator,left,right?}`; at least one of `trueBranch`/`falseBranch` (:130-133). `left` is a JsonElement and must be present and non-blank except for `exists` (:196-205). | None. | Template operands are re-rendered and parsed with culture-sensitive `double.TryParse` (:418, INFERRED to be harmless on an invariant-culture host). | PB-01, PB-20, PB-24 |
| **CreateNotification (50)** | Top-level `title` + `body` non-blank **even when `iterateItems`** (:232-236). `itemNotification.title/body` are **not** validated. | `appnotification` via NotificationActionCore: `title`, `body`, `priority` (int passthrough), `toasttype` (int passthrough, default 200000000), `ownerid` (systemuser), `ttlinseconds=604800`, `sprk_category`, `sprk_regardingid/type`, `sprk_source`, `sprk_playbookrunid`, `data` (customData: actionUrl, dueDate, regardingName, regardingEntityType, regardingId, viaMatter, source). Dedup read: `ownerid`+`sprk_category`+`sprk_regardingid`+**`statecode=0`**. | `item` values are `JsonElement`s; the date strings are whatever QueryDataverse produced. No truncation. | **PB-03** priority (no validation; schema/docs advertise 100000000/300000000, :31/:115/:756). **PB-04** toast (schema advertises 100000000/300000000; `ToastTypeHidden = 100_000_000` in NotificationActionCore is wrong: Hidden is 200000001). **PB-05** dedup reads a nonexistent `statecode` and throws every time; caught, then it creates anyway. **PB-10** lengths: `title` ≤256 vs `sprk_eventname`/`sprk_name` 850, `sprk_subject` 2000; `body` ≤500 vs `sprk_from` 1000 + `sprk_subject` 2000, `sprk_regardingrecordname` 1000. **PB-06** `customData.category` is never written. `icontype` is never set (default). |
| **Start (33)** | Config optional (`inputContract` only if `validateOnExecute`). | None. | — | The repo `scope`/`resolveUserId` keys are unread (dead config, PB-27). |

AI and other executors (AiAnalysis, AiCompletion, AgentService, CreateTask, SendEmail, UpdateRecord, Deliver*, LoadKnowledge, ReturnResponse, EntityNameValidator, GroundingVerify, Insights nodes, Sanitizer, ObservationEmitter) are covered in §9.

---

## 3. Notification playbooks: per-node validation and run verdict

Live state (VERIFIED-LIVE, 2026-10-09):
- **Active:** Tasks Due Soon and New Work Assignments (`statecode=0`).
- **Paused (D-91):** the other 5, at `statecode=1`.
- **Synced from the repo at D-89 02:28Z and unchanged since** (node `modifiedon` 22:28 local): Overdue, Due Soon, Work Assignments, Matter Activity.
- **Not synced, and drifted:** Docs, Emails, Events. None has a Lookup node; their queries use the `joinIds` comma form, and each Create node uses the wrong fields:
  - Emails: OOB `email` fields (`sender`, `subject`, `activityid`);
  - Events: OOB `appointment` fields;
  - Docs: `{{item.matterName}}`, which no query returns.
- **Canvas layouts:** all 7 playbook rows carry a `sprk_canvaslayoutjson`. 6 of the 7 use canvas types `control`/`aiAnalysis`; Due Soon uses the real types (PB-08).
- **Stale `nodes` copy:** the playbook-level `sprk_configjson` of Emails, Docs and Events still holds a stale `nodes` copy, with `joinIds` and roles.

Defects common to all 7 repo JSONs:
- PB-01: gate. Every user with zero items fails at the top-level body.
- PB-05: dedup. Duplicates on every run.
- PB-06: briefing channel = system.
- PB-09: date text.
- PB-14: cadence drift.
- `deduplication`, `templateParameters`, `parameters`, `schedule.time` and `cronExpression` are dead config (PB-27).

| Playbook | Repo JSON (master), per node | Live today | Verdict today (BFF e9f08b764) |
|---|---|---|---|
| **Tasks Overdue** | Lookup OK (`targeting: people`). Query OK (lint C passes). Check Results OK. **Create: `priority` 300000000 (top and item) INVALID.** Item title `"Overdue: {{item.sprk_eventname}}"` can exceed 256 (name 850). Body includes `sprk_regardingrecordname` (1000) and can exceed 500. `dueDate`/body show the ISO timestamp. | = repo. Paused. | **Fails for every user.** Users with 0 tasks fail at Create "body is required" (PB-01). Users with ≥1 fail at "priority 300000000 outside the valid range" (VERIFIED-LIVE, D-89 run 4d79e284). |
| **Tasks Due Soon** | As Overdue, but priority 200000000 is valid. The top-level title uses `{{dueWithinDays}}` (scheduler parameter "3", consistent). | = repo. **Active.** | **Partial.** 0-item users fail (PB-01). Users with items get notifications, VERIFIED-LIVE: 6 real ones for the operator, each with `customData` lacking `category`, `dueDate` `…T00:00:00.0000000`, and `memberships` over-attributed. Each later run re-creates them (PB-05) for as long as the task stays in the 3-day window, so about 3 duplicates per task. Reported as PartialFailure, so no alert (PB-15). |
| **New Work Assignments** | Priority 200000000 valid. Query = active WAs created **or modified** in 24 h on matters in the user's scope; there is no assignee filter (by design per task 120; title "New assignment" is misleading on modified rows, PB-31). Body shows `sprk_responseduedate` (date-only) as ISO and "(due )" when null. | = repo. **Active.** | **Partial**, as Due Soon. Delivered 1 (zz-120) on D-89. Duplicates on every run. Every modification of a WA re-notifies, because dedup is broken. |
| **Matter/Project Activity** | **Priority 100000000 INVALID.** No top-level `actionUrl`. Covers matters only: the Start scope and name say projects, but there is no project membership or filter (PB-30). Body `{{item.sprk_eventname}}` only. | = repo. Paused. | **Fails for every user**: 0-item users at PB-01; others at priority (VERIFIED-LIVE D-89). |
| **New Documents** | Priority 200000000 valid. **`createdby ne-userid` never excludes the user's own uploads:** documents are created by the BFF MI (`createdby=8793f4b0…`) and the person is in `sprk_createdbyperson` (15/15 recent rows, VERIFIED-LIVE). So users are notified of their own uploads (PB-11). Item body `"… was added to {{lookup item 'm.sprk_mattername'}}"` renders "was added to " for documents with no matter, which match through the `ownerid` branch (7/15 recent docs have a null `sprk_matter`) (PB-12). | **Drifted:** no Lookup node; the query has no membership branch; Create uses `{{item.matterName}}` (always blank) and has no `viaMatterName`. Paused. | Live: users with docs **deliver with a blank matter name** (D-89's "blank matter name" explained: template, plus docs with no matter); 0-doc users fail (PB-01); own uploads included. Repo version: delivers, apart from PB-01/05/06/11/12. |
| **New Emails** | **Priority 100000000 INVALID.** Item body `"From {{item.sprk_from}}: {{item.sprk_subject}}"` can reach 3,000 chars against `body` ≤500. Title `"New email on {{item.sprk_regardingrecordname}}"` is 100 max here, OK. `createdby ne-userid` is likely ineffective (INFERRED, same as Docs; `sprk_createdbyperson` exists). Ignores `sprk_isprivate` / `sprk_privilegeclassification` / `sprk_accesspermission` (PB-28). | **Drifted:** OOB email field names, so title "New email on ", body "From : ", no `regardingId`. `joinIds` query; priority 100000000. Paused. | **Fails** for every user with emails (priority); 0-email users fail (PB-01). Even once the priority is fixed, the live node would deliver empty text. |
| **New Events** | **Priority 100000000 INVALID.** Body uses `sprk_meetingdate` (date-only, null for Action/Deadline/Milestone/Reminder types), so the body reads "X — " (PB-13). `createdby ne-userid` is likely ineffective (INFERRED). | **Drifted:** appointment fields; Condition `{{newEvents.count}}` (raw shape, renders OK); `joinIds` query; priority 100000000. Paused. | **Fails** (priority / PB-01). |

**Would any notification playbook run end to end today, correctly? No.**
- Due Soon and Work Assignments *deliver* for users who have items. They also fail for everyone else, duplicate on every run and land in the wrong briefing channel.
- After PR-A (§12), all 7 repo JSONs would run end to end. Docs/Emails/Events also need the live sync, which is blocked on uac-r2 #1355.

---

## 4. Rendering and formatting

- **Date-only (PB-09, VERIFIED-LIVE).** QueryDataverse renders every `DateTime` with `"o"`. Date-only columns arrive from the SDK as midnight with an unspecified Kind, so they render `2026-10-10T00:00:00.0000000`, and that string goes into user-visible titles and bodies and into `customData.dueDate`.
  - The briefing parses `customData.dueDate` with `parseDueDate` (`dateLocal.ts:42-51`). That only treats `YYYY-MM-DD` as a local date; the 7-fraction-digit string falls back to `new Date()`. It works in Chromium because there is no offset, so the string is read as local time (INFERRED; non-spec, so Safari or WebKit may return Invalid Date).
  - D-25 wants the user's local day. Fix: format date-only attributes as `yyyy-MM-dd` (decide via attribute metadata, or `DateTimeBehavior`, using `FormattedValues` when present). For display, add a `formatDate` helper; the owner decides on ISO vs a localized date, and a localized date needs the user's settings.
- **Date-time.** `createdon`/`modifiedon` render UTC `…Z`. They appear only in `customData.source.modifiedOn` (machine data, acceptable) and in the Docs top-level body (unused in iterate mode).
- **Numbers.** Count is an int; the Condition compares doubles. `Money` and `decimal` would render with invariant `ToString` (INFERRED). No playbook uses them.
- **Escaping.** Query text is escaped (task 164). Notification text uses `NoEscape`; appnotification is plain text, so that is fine. Double rendering (PB-25) lets `{{` inside record data be evaluated (low; same-user context).
- **Lookups to names (PB-12).** EntityReference names are dropped (QueryDataverse:313). Names exist only through link aliases (`m.sprk_mattername`). The "blank matter name" has two causes:
  1. the live Docs template uses a nonexistent `item.matterName`;
  2. records matched through `ownerid` with no matter.

  Templates need `{{#if (lookup item 'm.sprk_mattername')}}…{{/if}}` or a fallback, and Docs should also read `sprk_matter`'s name.
- **Option-set labels.** Discarded (`statuscode`, `sprk_priority`). No current template needs them.

---

## 5. Scheduler (`PlaybookSchedulerJob.cs`)

| Item | Finding | Evidence |
|---|---|---|
| Cadence (PB-14) | `IsPlaybookDue` (:617-634) checks `elapsed ≥ 24h` against a `lastrundate` stamped at the **end** of the fan-out (:267-268), on an hourly cron tick. Each run lands about 40 s after :00, so the next day's tick at :00 is 23:59:21 later, not due. **Daily = every 25 h**, drifting +1 h per day around the clock. `schedule.time` ("06:00") is never used (:733-735). `cronExpression` (WA, MA) is ignored, so both run daily (task 131). | VERIFIED-LIVE: Tasks Overdue `fan-out complete` at 10-01 11:00, 10-02 12:00 … 10-08 18:00. ISS-018: 86 runs in 89 days. |
| Window gap (PB-14) | `timeWindowHours` is fixed at "24" (:515), so with a 25-hour cadence about 1 h per cycle (≈4%) of new docs, emails, events, WAs and activity is never in any window. Each pause or outage loses its whole span, because nothing catches up. | INFERRED (arithmetic from the verified cadence) |
| Total failure (D-78) | Works: an Error trace, `Status=Failed`, `lastrundate` held, a retry next hour. Seen in D-89 (MA, Overdue). | VERIFIED-LIVE (deploy-log D-89) |
| Partial failure (PB-15) | `failureCount < userCount` → "PartialFailure"; `lastrundate` advances (:263-279); **no alert, no retry of the failed users**; per-user errors are Warning traces only. D-89 Due Soon and WA (1/15 success) would have counted as healthy. The alert KQL only matches "failed for every user". | VERIFIED-CODE + D-89 |
| Retry semantics | Retrying after a total failure re-runs every user, including users whose runs created notifications before failing. With dedup broken (PB-05) that duplicates. A held `lastrundate` reruns hourly while a defect persists. That is bounded, because total failure means no creates (INFERRED). | VERIFIED-CODE |
| Per-user isolation | One DI scope and one run per user, parallelism 5 (:459-553). Each run executes as the MI; user scoping is FetchXML only. A user's failure does not affect other users. **There is no record-level access check:** Restricted/Limited events, secure WAs and private emails reach matter members (PB-28, INFERRED; owner/uac-r2). | VERIFIED-CODE |
| User population (PB-17) | `isdisabled=0 AND accessmode<>4` gives 15 users in dev. It includes **Support User (accessmode 3), Delegated Admin (5) and an unlicensed Administrative user (1)**. Wasted runs; their "failures" count towards PartialFailure. | VERIFIED-LIVE |
| Paused playbook (PB-16) | "Paused" in D-91 = `statecode=1`. The scheduler queries `statecode=0` (:391-392), so a paused playbook is invisible: no "Skipped" child and no telemetry. On reactivation: Overdue and MA have `lastrundate=null`, so they run on the next tick. Docs/Emails/Events resume with a 24-hour window, so everything created while paused is never notified. Other run paths (Designer "Run", PlaybookRunEndpoints) do not check the playbook's state (INFERRED). | VERIFIED-LIVE (playbook rows) + code |
| Run history | `sprk_backgroundjobrun` is in-memory (ISS-018 §2.1). Children's `ResultJson` is lost on restart. | ISS-018 |
| Cost (PB-18) | Per user and playbook: `GetNodesAsync` (1 GET + 3 N:N GETs per node) plus `ResolveNodeScopesAsync` (3 GETs per node, because NodeType is always AIAnalysis) is about 31 GETs for 5 nodes, before any real query. 7 playbooks × 15 users gives about 3,250 calls per tick. That grows linearly with users; Dataverse service-protection throttling for the single MI identity is likely at tenant scale (INFERRED). | VERIFIED-CODE |

Task 131 already covers cadence and window parameters and re-notify after retry. **Extend it with:** the +1 h/day drift and its root cause (stamp the *scheduled* slot, or compare against `time`); the window gap and catch-up from `lastrundate` instead of a fixed 24 h; the partial-failure alert threshold; population filtering; and the paused-playbook semantics.

---

## 6. Membership: the "ownerid" role on a team-owned matter

**Verdict: a bug in `MembershipResolverService.MaterializeResults`, not junction lag and not by design.** There is no junction on this path: the resolver builds FetchXML and queries Dataverse live, with a 2-minute per-user cache (`CacheTtl`, :129).

The cause is in `RowMatchesDescriptor` (:1323-1336). It credits a row to a role whenever that role's column is **non-empty** (comment at :1320: "we don't re-compare values to the identity here"). The row matched the OR filter through *one* term, but every other populated person column is credited too.

For the zz-120 matter:
- it matched through `createdBy` (the operator created it);
- its `ownerid` holds the team, which is populated, so `ownerid` was credited as well.

VERIFIED-LIVE: the operator's real Due Soon notifications carry `memberships: [{createdBy},{ownerid},{assignedattorney},{assignedparalegal}]` ("Test New Matter via Workspace") and `[createdBy, ownerid]` on three other matters. Those are every populated column, not the operator's roles.

Impact:
- `customData.viaMatter.memberships` is wrong on every notification. No client reads it today (grep: no `viaMatter`/`byRole` consumer in `src/client`; `membership.ts` uses `ids` only).
- Any future consumer that uses `byRole` to decide something (for example "matters I own") gets a superset.
- `ids` is correct.

**Tell uac-r2:**
1. `MaterializeResults` must compare each descriptor's value with the caller's identity values: the SystemUser id, the linked contact id, and team ids where that surface admits them. Only matching roles should be credited.
2. The `byRole` contract (per-role ids for *this* user) is violated on every surface (`ResolveAsync` and `ResolveByContactAsync` :581).
3. Add a regression test: a row matched through `createdBy` that also has another user's `ownerid` and another contact's `assignedAttorney` must yield `byRole = {createdBy:[row]}` only.
4. Bump `CacheVersion` (the cached value's semantics change; see :100-127).
5. The F3 question on Restricted/secure records in notifications (PB-28) is still open.

---

## 7. Consumer contract: the Daily Briefing (PB-06)

- **Reader.** `notificationService.ts:214-229` takes `category` from `customData.category`, falling back to `customData.channel`, then `'system'`. It takes `priority` from `customData.priority` (`'high'`/`'normal'`). `groupByCategory` (:383-404) groups on that value.
- **Writer.** `NotificationActionCore.BuildNotificationEntity` writes the category only to the `sprk_category` **column**, never into `customData`, and never writes a priority label.
- **Live (VERIFIED).** All 6 delivered Due Soon rows have `customData` without `category`. The non-playbook rows (`{"category":"communication"}`) have it.
- **Effect.** Every playbook notification shows under the "system" channel with normal priority. The per-channel unread counts and channel ordering are wrong. Channel-disable filtering still works because it uses the column.

**Fix (pick one; the owner decides):** add `category` and the `priority` label to `customData` in the core (one place; it covers playbook, ActionSeam and dispatch), or have the reader fall back to `sprk_category` (it must also add `sprk_category` to `NOTIFICATION_SELECT`). I recommend the writer: it is a single owner, and the briefing doc already names `customData.category` as the contract.

---

## 8. Adjacent defects in the same class (not playbook-engine, found while checking every appnotification writer)

- **PB-03b.** All three call sites below go through `NotificationService.CreateNotificationAsync`, whose comment at :89 ("200000000=Informational, 200000001=Warning, 200000002=Critical") is wrong. No telemetry hit in 60 days (they have not fired), so the failures are INFERRED.
  - `IncomingCommunicationJobHandler.cs:209` — `priority: 200000002 // Critical`. Every "Inbound email held" admin alert will throw; it is caught and logged at :213, so the alert is silently lost.
  - `GrantExpiryReminderJob.cs:99,453` — `PriorityCritical = 200000002` when `DaysLeft ≤ 1`, so the last-day reminder fails.
- **PB-03c.** Dispatch / OutputRouter (:487-491) passes an LLM-produced `priority`/`toastType` straight through. The seam test `DispositionRoutabilityNotificationSeamTests.cs:78,106` **pins the invalid 300000000** as the expected value. Nothing validates the value against the option set.
- **Out of scope, noted.** `sprk_emailartifact.sprk_priority = 192350002` "outside the valid range" (21 failures, last 2026-08-31). It is another option-value defect class, last seen before this window. Check whether it is fixed.

---

## 9. AI executors and AI playbooks

The same read-only basis, done by a parallel sweep agent:
- live reads Q1-Q12: playbooks, nodes, `configjson`, canvas layouts, consumers, tool links, actions, and `describe` of the affected tables;
- `invoke_api` was not used, because it cannot be guaranteed to be a GET.

Paths are relative to `src/server/api/Sprk.Bff.Api/`.

**Production reach of the node engine.** Most AI work does not use the node graph. Document Profile on upload, Matter/Project Pre-Fill and Summarize File all call one Action directly (`AppOnlyAnalysisService.cs:625-634`, `MatterPreFillService.cs:196-205`, `FileSummarizeAi.cs:60`). The node engine runs only:
- `matter-health-single` (insights-ask);
- Document Profile through the workspace ai-summary;
- manual runs through `/api/ai/analysis/execute`.

**Registration.**
- 25 executors, one type each, no collisions.
- Eight enum values have no executor: AiEmbedding (2), RuleEngine (10), Calculation (11), DataTransform (12), CallWebhook (23), SendTeamsMessage (24), Parallel (31) and Wait (32). They are still offered by the live choice column and the canvas mapping. No live node uses them, so a node of those types would fail "No executor registered" (PO:1159-1172).

**Dataverse writes vs live metadata.**
- CreateTask → `sprk_event`: all columns exist, the Task type row `124f5fc9…` is active, and Open = 659490001 matches.
- UpdateRecord: a Web API PATCH, with lookups as `{field}@odata.bind` through a home-made pluraliser (`UpdateRecordActionCore.cs:493-506`).
- ObservationEmit: writes the index and mirrors to Dataverse.
- SendEmail and DeliverToIndex: no Dataverse writes.

### 9.1 AI playbook defects

| ID | Sev | Playbook / where | Evidence | Impact | Fix |
|---|---|---|---|---|---|
| AI-01 | H | `matter-health-single` (bound to insights-ask "default" and "matter-health-single") | Live config (Q4). (a) `retrieveObservations` filters on `matterId eq '…'`, but the repo index schema `infrastructure/ai-search/spaarke-insights-index.json` has only `scope/matterId`, so a 400 at `IndexRetrieveNode.cs:~316` (INFERRED). (b) The `synthesize` AgentService node has no `prompt`, so Validate rejects it (`AgentServiceNodeExecutor.cs:108-111`); the executor never reads the Action prompt. (c) `from` fields name *nodes* (`synthesize`, `retrieveObservations`, `checkSufficiency`, `groundCitations`); lookup is by outputVariable (`NodeExecutionContext.cs:166`), so they should be `synthesis` / `observations` / `sufficiency` / `groundedSynthesis`. (d) `sourceChunksJsonPath:"documents"`, but IndexRetrieve emits `artifacts` (`IndexRetrieveNode.cs:534`). (e) On the decline path `groundCitations` still runs, because a branch-skipped dependency is not propagated (**PB-02**), so RunFailed. (f) `persistEnvelope` has a malformed template (`{{{groundCitations.output.*}}}`, `{{run.startedAtIso}}` which does not exist, `body` unquoted). (g) The repo JSON `Services/Ai/Insights/Playbooks/matter-health-single.playbook.json` has the same errors (not drift). | The Insights card always gets the "no-artifact-produced" fallback decline (`InsightsOrchestrator.cs:~517-531`). DeclineToFind shows a literal `{have}`/`{need}`. | Output-variable names; `artifacts`; `subjectScope:"matter:{{matterId}}"`; resolve the AgentService prompt from the Action (or author `prompt`); fix the `persistEnvelope` template; PB-02. |
| AI-02 | H (unbound) | `predict-matter-cost@v1` | No consumer row (Q8). `checkSufficiency` reads `from:"retrieveCohortObservations"`, so the verdict is always insufficient. No `sufficientBranch`/`insufficientBranch`, so `selectedBranch` is the literal "insufficient" and **both** `synthesize` and `declineInsufficient` are skipped (PO:1001-1018), then `groundCitations` fails. The live `synthesize.prompt` is the literal `"$ref:Services/Ai/Insights/Prompts/…txt"` (unresolved). The repo has no `prompt` (drift, both wrong). Same name and `documents` errors as AI-01. | Unusable when bound. | As AI-01, plus branch names, and resolve `$ref:` at deploy or load. |
| AI-03 | H | `universal-ingest@v1` | Not deployed in dev: no playbook row (Q1), no consumer (Q8), so `RunIngestAsync` throws (`InsightsOrchestrator.cs:640-656`). The repo definition makes `layer1Classify`/`layer2Extract` AiAnalysis, which needs a Tool and a Document (`AiAnalysisNodeExecutor.cs:119-154`); ingest passes no Document (`InsightsOrchestrator.cs:660-666`) (INFERRED fail at node 2). | Insights ingest does not exist in dev. | Deploy; switch the classify/extract nodes to AiCompletion with input bindings, or pass a Document. |
| AI-04 | M | Workspace ai-summary → Document Profile | `WorkspaceAiService.cs:378-389` runs it with empty `DocumentIds` and no Document. Node 1 (AiAnalysis) fails "requires document context", so `BuildFallbackResponse`. | The workspace AI summary is always the fallback template. | Bind ai-summary to an entity-summary playbook or Action. |
| AI-05 | M | Document Profile graph (manual, with a document) | The JPS asks for `sprk_filetype` as a category (≤200 chars). Live `sprk_document.sprk_filetype` is NVARCHAR(**10**) (Q5) and holds extensions (Q6). The AI value `"nda"` matches no option label ("Non-Disclosure Agreement") and passes through raw (`UpdateRecordActionCore.cs:337-341`). | PATCH rejected (INFERRED). The upload path is unaffected (direct-Action). | Fix the field mapping and the enum labels; lint the output schema against the metadata snapshot. |
| AI-06 | M | Manual playbooks | Analyze Agreement Financial Terms, Finance Invoice Processing and Quick Document Review: the AiAnalysis node has no tool and no Action, so "requires a tool". Real Estate Lease Review: 6 nodes, no `dependsOn` (all in batch 1), CreateTask has no `subject`, AiCompletion has no Action and is given a Document, AiAnalysis has no tool. Patent Claims: only a Deliver Output node (the canvas AI node is missing), so it completes with empty output. | Fail at the first node, or empty output. | Rebuild from the repo, or retire. |
| AI-07 | L | `summarize-document-for-chat@v1` / `-for-workspace@v1` | Orphaned (no binding). AiCompletion forbids a Document (`AiCompletionNodeExecutor.cs:195-199`) and has no `inputBinding`. Repo actionType 0 vs live 1 (drift). | None today. | Retire, or fix and bind. |
| AI-08 | L | Document Summary (compose-summarize binding) | The binding has no `sprk_action`, so a 422 at `SessionDispatchOrchestrator.cs:248-256` (INFERRED reachability). A manual run produces a *profile* (wrong action). | Compose-summarize fails. | Set the action, or rebind. |
| AI-09 | L | Daily Briefing Narrate | Inactive. Engine dispatch removed (`DailyBriefingEndpoints.cs:615-672`). BRIEF-NARRATE-CHANNEL action inactive. Would hit AI-EX1. | Dead row. | Remove. |
| AI-10 | L | Junk / data | 5 "New Playbook" stubs plus one unnamed playbook `4369cab2-…-7c1e520aa4df`. `sprk_matter.sprk_performancesummary` holds Daily Briefing narrative text (Q12; the writer was not traced). | Noise; possibly a wrong-column writer. | Clean up; trace the writer. |

### 9.2 Executor contract defects (AI and other executors)

| ID | Sev | Where | Defect | Fix |
|---|---|---|---|---|
| AI-EX1 | M (latent) | `LoadKnowledgeNodeExecutor.cs:342-343` | `passthroughBinding` is `Dictionary<string,string>`. Layer 1 turns a single-expression value into native JSON (PO:2478-2499), so deserialization throws, the error is swallowed, and `{}` is bound silently. | `JsonElement` values, as ReturnResponse does. |
| AI-EX2 | M (latent) | CreateTask, SendEmail `to[]`, UpdateRecord `fieldMappings[].value`, DeliverOutput `template`, AgentService, EntityNameValidator `candidateText` | `string`-typed fields break when a pure template resolves to a number, bool, array or object (Option D auto-wrap). UpdateRecord fails "Failed to parse"; DeliverOutput silently auto-assembles. `{{textNode}}` renders the `{output,text,success}` wrapper. This is the same class as ISS-018b. | Type the fields as `JsonElement` and coerce; a Layer 1 contract test per executor schema. |
| AI-EX3 | M | `CreateTaskNodeExecutor.cs:211-239`; `TaskActionCore.cs:216-227, 258-266, 301-311` | Returns OK when the core returns `Guid.Empty` (owner refusal, stamping failure, Dataverse rejection). Subject not truncated to 850. `sprk_regardingagreement` missing from the regarding map. | Fail on `Guid.Empty`; truncate; add the mapping. |
| AI-EX4 | L/M | `DeliverToIndexNodeExecutor.cs:160, 208-223` | `indexName` rendered but never sent; `source`/`contentVariable` ignored. | Wire them or reject them. |
| AI-EX5 | L | `UpdateRecordActionCore.cs:306-308, 357-360, 413-417` | An empty rendered value clears the column. `lookup` passes a raw GUID. A multiselect gets a single int. Navigation-property names are guessed. The PATCH has no If-Match, so it is an upsert (INFERRED). | Skip empty unless explicit; build `@odata.bind` from metadata; `If-Match: *`. |
| AI-EX6 | L | `SendEmailNodeExecutor.cs:222-231` | Requires a user HttpContext, so it fails in app-only or scheduled runs. | App-only mail path, or reject at Validate in app-only. |
| AI-EX7 | M (INFERRED, security) | `INodeExecutor.cs:~314` `ExecutorSideEffects`; `ToolFrameworkExtensions.cs` | AiAnalysis is classified read-only but can dispatch any registered tool handler, including Dataverse create/update/delete, MemoryWrite and SendWorkspaceArtifact. | uac-r2 / security review. Classify by the resolved tool set. |

**Engine items the AI sweep confirmed independently:**
- dependsOn to an inactive or missing node is dropped (PB-21);
- a malformed `dependsOnJson` becomes "no dependencies" (`NodeService.cs:562-571`; add to PB-21);
- node `statecode` is not filtered (§1.1);
- `sprk_nodetype` was dropped, so everything is AiAnalysis (PB-18);
- `$choices` reads `__actionType` instead of `sprk_executortype` (PO:1414-1417; add to PB-18);
- Layer 2 re-rendering (PB-25).

### 9.3 Canvas sync on AI playbooks (PB-08 extended)

- **Wiped entirely on the next sync.** Every node lacks `__canvasNodeId`:
  - Daily Briefing Narrate (6/6);
  - **matter-health-single (9/9)**;
  - predict-matter-cost@v1 (8/8);
  - summarize-document-for-workspace@v1 (5/5).
- **Canvas-only nodes that a sync would create:**
  - Matter Pre-Fill: `entityNameValidator`, which becomes AiAnalysis;
  - Document Profile: "Save Profile";
  - Patent Claims: "AI Analysis";
  - Real Estate Lease: a second CreateTask;
  - Summarize NDA and Email Analysis: "AI Analysis", each currently with 0 nodes.
- **Type mapping.** `control` and `output` are unknown types, so they map to AiAnalysis (0). summarize-for-workspace's deliverComposite node is saved on the canvas as `aiAnalysis`. Daily Briefing's canvas has an array-valued `type`, so the sync parse probably fails (INFERRED).
- **Triggers:**
  - Designer save (`PlaybookEndpoints.cs:716`);
  - `ExecuteLegacyModeAsync` → `AnalysisOrchestrationService.ExecutePlaybookAsync` (PO:612), only for playbooks with 0 nodes;
  - `AnalysisExecutionHandler.cs:289` (chat re-analysis, any playbook in context; INFERRED reachability).

  The Insights path (`InsightsOrchestrator` → `ExecuteAsync`) does not sync, so matter-health's wipe risk is a Designer save or a re-analysis that targets it.

### 9.4 AI per-playbook verdicts (27 live, non-notification)

| Playbook | Runs end to end? | Fails at |
|---|---|---|
| matter-health-single | No | `retrieveObservations` (INFERRED), else `synthesize` / `groundCitations` |
| predict-matter-cost@v1 | No (unbound) | `groundCitations` |
| universal-ingest@v1 | No (not deployed) | binding lookup, then `layer1Classify` |
| Document Profile (ai-summary) | No | Profile Document (no document) |
| Document Profile (manual, with doc) | No (INFERRED) | Update Record (filetype length, "nda") |
| Document Summary | Graph yes; compose-summarize 422 (INFERRED) | — |
| Summarize File / Matter Pre-Fill / Project Pre-Fill | Graph likely yes (production uses direct-Action) | — |
| U.S. Patent Office Action Review | Likely yes (INFERRED) | — |
| Patent Claims Analysis | Completes, empty | AI node missing |
| Quick Document Review / Agreement Financial Terms / Finance Invoice Processing | No | AI Analysis (no tool) |
| Real Estate Lease Agreement Review | No | batch 1 |
| summarize-document-for-chat / -workspace @v1 | No (orphaned) | AiCompletion |
| Daily Briefing Narrate | Inactive, dead | LoadKnowledge (AI-EX1) |
| Email Analysis, Risk Scan, Full Contract Analysis, SprkChat Document Assistant, Summarize NDA | No node graph (legacy tool path) | not node-engine checked |

**Not checked:** the deployed insights index schema (AI-01a remains INFERRED), the AgentService kill switch, and whether compose-summarize and re-analysis reach these playbooks in production.

---

## 10. Why none of this was caught (test gaps)

1. **Every executor test stubs `IGenericEntityService`** with Moq, which accepts any option value, any length and any attribute.
   - The dedup seam test (`CreateNotificationNodeExecutorSeamTests.cs:125`) *returns a row* for the dedup query, so the test "proves" dedup while the real query throws on `statecode`.
   - The seam test at `DispositionRoutabilityNotificationSeamTests.cs:106` pins an invalid priority.
2. **No test runs the real orchestrator with real executors over a real playbook graph.**
   - `Issue1452_NotificationPlaybookFetchXmlShapeTests` checks nodes in isolation (render, shape, Condition, Create with items). It never runs Lookup → Query → Condition(false) → Create, so PB-01 and PB-02 are invisible.
   - The orchestrator tests use mock executors and assert events, not semantics.
3. **No metadata contract.**
   - Nothing compares written option values, lengths or attribute names with Dataverse metadata.
   - Lint C checks FetchXML shape, canvas stubs and Condition `left` only.
   - No test pins `appnotification`'s option sets.
4. **No consumer contract test.** Nothing checks that a playbook-produced `data` payload parses into the briefing's expected shape (category, priority, dueDate format).
5. **Live checks covered only queries.** `NotificationPlaybookFetchXmlLiveTests` is read-only and query-only. The first-ever write path ran in production-like dev on D-89.
6. **The canvas sync** has an integration test for type-mapping drift (task 065) but none for "deploy, then save Designer, then the playbook still runs".
7. **Monitoring** alerts only on total failure, so a 93% failure rate looks healthy.

### 10.1 The end-to-end harness that would have caught them (recommended)

**`PlaybookE2EHarness`** (tests/integration/seam/Ai/Playbooks):
- It runs the **real** `PlaybookOrchestrationService`, the real executors, the real `TemplateEngine` and the real `NotificationActionCore`, against a **metadata-faithful fake `IGenericEntityService`** loaded from a checked-in snapshot of live metadata: option sets, MaxLength, attribute existence and required level, for `appnotification`, `sprk_event`, `sprk_document`, `sprk_communication`, `sprk_workassignment`, `sprk_matter` and `sprk_analysisplaybook`.
- The fake **throws exactly as Dataverse does**:
  - an unknown attribute in a QueryExpression or FetchXML (catches PB-05);
  - an option value outside the set (PB-03, PB-04);
  - a string over MaxLength (PB-10);
  - an `in` with no values.
- RetrieveMultiple serves fixture rows. The fixture SDK shapes must match the real ones: date-only = Unspecified midnight, AliasedValue for links, EntityReference without a name, a BFF-app `createdby` plus `sprk_createdbyperson`.
- For **each repo playbook JSON** × fixtures it asserts:
  - the run state: **zero items → Succeeded with no create**;
  - the exact created rows, including `customData.category` and `dueDate` = `yyyy-MM-dd`, title ≤256 and body ≤500;
  - that a second run creates **zero** new rows (dedup);
  - that `viaMatter.memberships` = only the matched roles.
- The fixtures: 0, 1 and many items; an item with no matter; an over-long name; the user's own upload; a team-owned matter created by the user.
- A **snapshot-refresh test** (live-gated, read-only `EntityDefinitions` GETs) fails when live metadata drifts from the checked-in snapshot.
- Graph-semantics unit tests:
  - a false condition with no falseBranch skips its dependents;
  - a skip propagates transitively;
  - a dependency on an inactive node is reported;
  - an OutputVariable branch target is rejected with a message.
- A Designer round-trip test: deploy the repo JSON, then `SyncCanvasToNodesAsync` with its canvas, then assert the nodes are unchanged (catches PB-08).
- A consumer-contract test: a playbook-produced `data` string goes through the TypeScript `parseNotificationData` fixture. The test is generated JSON shared by the .NET and Jest suites.

### 10.2 Safe automated pre-deploy check

The fix plan builds an extended `Deploy-NotificationPlaybooks.ps1 -DryRun`. It adds three steps, all read-only.

1. **Static lint D** (repo test + deploy), using the same validator classes as the harness:
   - written option values against the live `EntityDefinitions` option sets (GET only);
   - branch targets resolve to a node *name* in the same playbook;
   - every template root resolves to an upstream outputVariable, a scheduler parameter, `run`, or `item` (CreateNotification only);
   - helper names exist;
   - a length budget: the max column length of each template variable plus the literal text, against the target MaxLength; warn or fail;
   - no `joinIds` in FetchXML, no canvas stubs, no Designer-hostile canvas (`control`/`aiAnalysis` types for non-AI nodes);
   - a priority that is not 200000000/200000001 fails.
2. **Shadow run.** A `CapturingEntityService` decorator lets reads through and **throws on any Create/Update/Delete/Associate/Execute**. It writes nothing, and a unit test asserts that the decorator overrides every mutating member of `IGenericEntityService`. The orchestrator runs each playbook for N real users through the decorator, and the would-be creates are validated against the metadata snapshot.
   - This is the provably write-free "dry run" the engine lacks today.
   - It needs a small BFF seam (a scoped `IGenericEntityService` override for the run). The admin endpoint (`/api/admin/playbooks/{id}/shadow-run`) must also be auth-gated.
3. **Diff**: the shadow-run output against the last real run's notifications, by category and per-user counts.

---

## 11. Defect table

Severity: **C** = blocks delivery for every user. **H** = wrong or failed delivery for many users, or silent data loss. **M** = wrong content, partial loss, or latent and likely. **L** = latent, cosmetic or hygiene.

| ID | Sev | Where | Evidence | User impact | Fix |
|---|---|---|---|---|---|
| PB-01 | C | PO:983-1018 (`TryExtractSelectedBranch` null → not a gate) | D-89: 14 users × 4 playbooks "body is required" | Every user with nothing to notify fails; masks real failures | Treat any dependency whose output is a `ConditionResult` (has `trueBranch`/`falseBranch`) as a gate; null `SelectedBranch` → skip all its dependents. Test. |
| PB-02 | H | PO:1004-1017, 1030-1042 (skip output not stored) | Code | Nodes after a skipped node run with empty inputs (e.g. Condition → A → B: B runs) | Store a `Skipped` output; skip a node whose every dependency is skipped (or whose gate excluded it). Test the chain. |
| PB-03 | C | Repo JSONs: Overdue 300000000; MA, Emails, Events 100000000; live Emails/Events. Executor schema/docs CreateNotification:31,115,756 | Live metadata {200000000,200000001}; D-89 errors | Overdue, MA, Emails, Events never deliver | Overdue → 200000001, others → 200000000; executor `Validate` rejects other values; fix schema/docs; lint D |
| PB-03b | H (latent) | IncomingCommunicationJobHandler:209; GrantExpiryReminderJob:99,453; NotificationService:89 comment | Live metadata | Held-email admin alerts and last-day grant reminders silently lost | Use 200000001 for High; central `AppNotificationPriority` constants used by every writer |
| PB-03c | M | OutputRouter:487-491; seam test :78,106 | Code | LLM- or binding-supplied priority fails the create | Clamp or validate in ActionSeam/core; fix the test |
| PB-04 | M (latent) | NotificationActionCore `ToastTypeHidden=100_000_000`; CreateNotification schema :121, :762-763 | Live: Hidden=200000001 | An authored Hidden toast fails the create; the FR-18 "no actions when hidden" rule never fires | Constants 200000000/200000001; validate |
| PB-05 | H | NotificationActionCore `CheckForDuplicateNotificationAsync` (`statecode` condition) | Live: no `statecode`; 10 exceptions in run 4d79e284; swallowed → create | Duplicates on every run (Due Soon about 3 per task; Overdue daily until done; WA on every modification) | Use a real column (`sprk_isread=false` and/or `sprk_briefingstate≠2`; the owner defines "duplicate"). A failed check should not silently create (owner decides). Test against the metadata fake. |
| PB-06 | H | NotificationActionCore `BuildNotificationEntity` vs `notificationService.ts:219-220` | Live `data` rows | Every playbook notification lands in the "system" briefing channel | Write `customData.category` and a priority label (or the reader falls back to `sprk_category`); contract test |
| PB-07 | M | MembershipResolverService:1323-1336 `RowMatchesDescriptor` | Live `memberships` lists 4 roles | Wrong "via your role" data; any future `byRole` consumer is wrong | uac-r2: compare with identity values; `CacheVersion` bump; test |
| PB-08 | H | NS:381-516 (deletes nodes without `__canvasNodeId`; stubs); NS:986-1019 (`control`, `queryDataverse`, … → AiAnalysis); AnalysisOrchestrationService:198-210 JIT sync | Code + live canvas types; ISS-018c precedent. Effect INFERRED | One Designer save silently kills a playbook (nodes deleted, AI stubs created) | Refuse canvas sync for system/notification playbooks, or make the sync preserve non-canvas nodes and map every type. Round-trip test. Owner decision. |
| PB-09 | M | QueryDataverse:318-319 | Live `dueDate` / titles | "due on 2026-10-10T00:00:00.0000000" | Date-only → `yyyy-MM-dd`; display helper; D-25 |
| PB-10 | M | NotificationActionCore (no truncation) | Live metadata lengths | A long name or subject throws and aborts the user's run mid-iteration | Truncate title to 256 and body to 500 with an ellipsis in the core; lint D budget |
| PB-11 | M | Docs/Emails/Events `createdby ne-userid` | Live: docs `createdby` = MI, person in `sprk_createdbyperson` | Users notified of their own uploads | `<filter type="or">` on `sprk_createdbyperson` ne user (null-safe) AND `createdby` ne user; owner decision |
| PB-12 | M | Live Docs Create `{{item.matterName}}`; repo template has no fallback | Live node config; 7/15 docs have no matter | "was added to " blank | Sync the repo (after uac-r2); `{{#if}}` fallback |
| PB-13 | M | Events body `sprk_meetingdate` | Metadata; type list | "X — " for non-meeting events | Per-type date (due, planned start) with a fallback |
| PB-14 | H | Scheduler :267, :617-634; fixed window :515 | Live cadence 11:00 → 18:00 | Notifications at random hours; about 4% of new items never notified; paused spans lost | Task 131: anchor to the schedule slot; window = since last successful run |
| PB-15 | H | Scheduler :243-279; alert KQL | D-89 PartialFailure 1/15 | 93% failure looks healthy; failed users never retried | Alert on failure ratio (e.g. ≥20%) and on per-error-class counts; record per-user failures; optional per-user retry |
| PB-16 | L | Scheduler :391-392 | Live statecode | Pauses invisible in telemetry; no catch-up on resume | Log "Skipped (inactive)"; decide catch-up |
| PB-17 | L | Scheduler :405-424 | Live users (Support User, Delegated Admin, unlicensed admin) | Wasted runs; skews failure ratio | Filter to licensed interactive users (`accessmode` 0, `islicensed`) or a security role |
| PB-18 | M | NS:579 (`sprk_nodetype` gone → AIAnalysis); PO:1110, :1718 | Code | About 3,250 extra Dataverse GETs per tick in dev; throttling risk at scale (INFERRED) | Derive the category from `sprk_executortype`; cache nodes per playbook per tick |
| PB-19 | L | PO:327-331 | Code | The Validate endpoint fails every structural-node playbook | Skip the action check when `ActionId == Guid.Empty` |
| PB-20 | M | ConditionNodeExecutor schema :87,:93; PO:997 | Code | Schema-following authors get their TRUE branch skipped | Accept a name OR outputVariable; validate targets exist (lint D, ValidateAsync) |
| PB-21 | L | EG:44-52 | Code | A deactivated node's dependents run without input | Treat as a config error (fail) or skip the dependents |
| PB-22 | L | CreateNotification:469-482 | Code (ConcurrentDictionary order) | Two `items` producers → nondeterministic source | `itemsFrom` config; default = the direct upstream query |
| PB-23 | L | CreateNotification:232-236 | Code | A top-level body is required even in iterate mode; item title/body are not validated | Validate the active mode's templates only |
| PB-24 | L | Condition:520-561; CreateNotification:700-738 | Code | Item templates that mix `item` with raw `{{q.count}}` or parameters render blank | Build executor contexts with `PlaybookTemplateContextBuilder.Build(nodeContext)` |
| PB-25 | L | CreateNotification:288-289; Condition:376-384 | Code | Record text containing `{{…}}` is evaluated | Don't re-render Layer-1-rendered strings (mark as rendered) |
| PB-26 | M | PO:2061-2075, 2196-2218 | Code | A broken fan-out expression silently does nothing | Fail the node (or warn plus emit an event) on render or parse failure |
| PB-27 | L | Repo JSON `deduplication`, `templateParameters` (`??`), `parameters`, `schedule.time`, `cronExpression`, Start `scope`/`resolveUserId`; node `sprk_retrycount`/`timeoutseconds`/`conditionjson` | Code | Authors believe config works that doesn't | Implement or delete; lint D warns on unknown keys |
| PB-28 | M (INFERRED) | Notification queries ignore `sprk_accesspermission`, `sprk_issecure`, `sprk_isprivate`, privilege | Live metadata | Restricted or private record titles reach matter members | uac-r2 F3 decision |
| PB-29 | L | `top="50"`, no paging | Code | Counts cap at 50 | Accept or page |
| PB-30 | L | MA is matters-only | JSON | Project activity never notified | Product decision |
| PB-31 | L | WA query created OR modified | JSON | "New assignment" re-notifies on every edit (worse with PB-05) | Created-only, or a "Updated assignment" title; task 131 dedup semantics |

(AI-side IDs AI-xx: §9.)

---

## 12. Recommended fix plan

### PR-A: one follow-up PR, "notification delivery correctness" (BFF + the 7 repo JSONs + lint)

Everything needed for a correct end-to-end delivery, plus the guard for each class.

1. **Engine:** PB-01, PB-02 (gate + transitive skip), PB-20 (branch target accepts a name or outputVariable; ValidateAsync and lint check it exists), PB-23.
2. **Notification core:**
   - PB-03, PB-03b, PB-03c and PB-04: one `AppNotificationOptions` constants class, used by every writer (NotificationService, NotificationActionCore, IncomingCommunicationJobHandler, GrantExpiryReminderJob, OutputRouter). Validation rejects other values with a clear message.
   - PB-05 (the dedup column; the owner decides the semantics).
   - PB-06 (`customData.category` + priority label).
   - PB-10 (truncation).
3. **QueryDataverse:** PB-09 (date-only → `yyyy-MM-dd`).
4. **The 7 repo JSONs:**
   - priorities (Overdue 200000001; the rest 200000000);
   - PB-11 creator filter, PB-12 matter fallback, PB-13 event date;
   - delete dead `deduplication` and `templateParameters` (PB-27; owner OK).
5. **Tests:** `PlaybookE2EHarness` with the metadata-faithful fake and fixtures (§10.1); graph-semantic unit tests; the consumer contract test; fix the two seam tests that pin wrong behaviour.
6. **Lint D** in `scripts/common/` plus the repo test (§10.2 step 1).
7. **BFF hygiene:** a hot-path declaration and a publish-size delta (CLAUDE.md §10).

### Separate tasks

- **T-canvas (PB-08 + §9.3):** canvas sync protection. It must cover the notification playbooks AND matter-health-single, predict-matter-cost, summarize-for-workspace and Daily Briefing.
  - **Options:** refuse the sync when a playbook has nodes without `__canvasNodeId`; make the sync preserve non-canvas nodes; map every canvas type; reject an unknown type instead of falling back to AiAnalysis.
  - **Tests:** a deploy → Designer save → still-runs round-trip test, plus the JIT-sync callers (`AnalysisExecutionHandler.cs:289`, legacy mode).
  - This is the highest-risk standalone item, and the owner decides the approach. **Until it ships, add a standing directive (main session; CLAUDE.md is not writable here): never open and save a system or repo-deployed playbook in the Playbook Designer.**
- **T-shadow-run (§10.2 step 2):** the write-free shadow-run seam and admin endpoint. It is the dry-run the engine lacks, and it serves both the notification and the AI playbooks.
- **T-ai-playbooks (AI-01…AI-10):**
  - **First:** fix matter-health-single, the only production node-engine consumer. Correct its repo JSON and resync, resolve the AgentService prompt from the Action, apply PB-02, and verify the live index field (`matterId` vs `scope/matterId`).
  - **Then** predict-matter-cost (branch names, `$ref:` resolution).
  - **Then** universal-ingest (deploy plus node types).
  - **Then** rebind ai-summary (AI-04), fix Document Profile's mappings (AI-05), and the compose-summarize binding (AI-08).
  - **Finally** decide whether to rebuild or retire the manual demo playbooks (AI-06/07/09) and the junk rows (AI-10).
  - Each fixed playbook gets a `PlaybookE2EHarness` case (§10.1), with LLM and Search faked.
- **T-executor-contracts (AI-EX1…EX6, plus PB-20, PB-22, PB-23, PB-24, PB-26):**
  - type every config field that Layer 1 can turn into native JSON as `JsonElement`;
  - add a per-executor schema-vs-Layer-1 contract test (the general guard against the ISS-018b class);
  - CreateTask fails on `Guid.Empty`;
  - DeliverToIndex wiring;
  - UpdateRecord edge cases;
  - SendEmail app-only.
- **T-engine-hardening:** PB-18 (NodeType/perf, plus `$choices` via `sprk_executortype`), PB-19, PB-21 (plus a malformed `dependsOnJson` should fail), PB-25, node `statecode` filtering, and the unregistered executor types (remove them from the canvas mapping or register them).
- **uac-r2 (security review):**
  - PB-07 (`byRole` over-attribution);
  - PB-28 (Restricted/secure/private records in notifications);
  - AI-EX7 (AiAnalysis can dispatch write tools while classified read-only);
  - the open F3 / #1355 Docs/Emails/Events question.

### Task 131 (extend)

PB-14 (drift, window gap, catch-up), PB-15 (partial-failure alert and retry), PB-16 (pause semantics), PB-17 (population), PB-27 schedule params / cron, PB-31, and the re-notify-after-read dedup semantics.

### Live re-verification (spaarkedev1), in order, after PR-A merges

1. Deploy the BFF from merged master; check `/healthz` and the InformationalVersion.
2. Snapshot-refresh test (read-only metadata), so the harness snapshot equals live.
3. `Deploy-NotificationPlaybooks.ps1 -DryRun` with lint D for the 4 synced playbooks. Then a real run with `-RecordPath`, and the read-back diff.
4. **Before reactivating anything:** delete nothing, but record the operator's current appnotification count per category (baseline).
5. Reactivate Overdue and MA (`statecode=0`). Leave Docs/Emails/Events paused until uac-r2 answers and their sync is done.
6. Create zz rows:
   - user A: an overdue task, a due-soon task, a WA, and an activity on a matter where A is a member, plus an over-long event name (900 chars);
   - user B: none.
7. Clear `sprk_lastrundate` on the 4 playbooks; trigger the scheduler once.
8. **Assert in App Insights:**
   - `fan-out complete … failures=0` for all 4;
   - 0 "outside the valid range";
   - 0 "Idempotency check failed";
   - 0 "body is required".
9. **Assert the rows:**
   - A's rows have priority ∈ {200000000, 200000001}, `customData.category` set, `dueDate` `yyyy-MM-dd`, a truncated long title, and `memberships` = the true roles only (after the uac-r2 fix);
   - B has none.
10. Clear `lastrundate` and trigger again; **assert zero new rows** (dedup).
11. Open the Daily Briefing as A; check each item appears under its channel.
12. Delete the zz rows and their notifications; confirm by query.
13. After uac-r2 answers: sync Docs/Emails/Events, then repeat steps 6-12 with docs, emails and events, including an upload *by A* (A must not be notified) and a doc with no matter.
14. Watch two natural ticks; confirm the cadence and that there are no PartialFailure children (or that the new alert fires correctly).

---

## 13. Decisions the owner must make

1. **Dedup semantics (PB-05).** What counts as a duplicate: unread (`sprk_isread`), not removed from the briefing (`sprk_briefingstate≠2`), or within a time window? If the dedup check itself fails: create anyway (today) or skip (fail closed)?
2. **Priority mapping (PB-03).** Overdue = High (200000001) and the rest Normal? Should Due Soon at ≤1 day be High?
3. **Date display (PB-09).** ISO `yyyy-MM-dd`, or a localized date (needs the user's locale settings per run)?
4. **Self-notification (PB-11).** Exclude records the user created themselves (`sprk_createdbyperson`)?
5. **Docs with no matter (PB-12).** Notify the owner without a matter line, or drop them?
6. **Designer vs repo (PB-08).** Make system/notification playbooks Designer-read-only, or fix the sync to preserve non-canvas nodes? Interim standing directive?
7. **Briefing channel (PB-06).** Fix the writer (recommended) or the reader?
8. **Partial-failure alerting (PB-15).** Threshold and severity; retry failed users or not.
9. **Scheduler cadence (PB-14, with task 131).** Honour `schedule.time` and the crons, and catch up from `lastrundate`?
10. **Fan-out population (PB-17).** Licensed interactive users only?
11. **Restricted/secure/private records in notifications (PB-28).** Route to uac-r2 F3.
12. **Scope of PR-A.** Include the adjacent non-playbook priority fixes (PB-03b/03c)? Recommended: yes; they are the same class and one constant fixes them.
13. **Dead config (PB-27).** Delete `deduplication`/`templateParameters`/`parameters`, or implement them?
14. **AI playbooks (§9).**
    - Fix matter-health-single now (the Insights card is permanently in fallback), or hide the card until it is fixed?
    - Rebind ai-summary to which playbook or Action?
    - Retire or rebuild the demo playbooks (Quick Document Review, Agreement Financial Terms, Finance Invoice Processing, Real Estate Lease, Patent Claims, summarize-for-chat/-workspace, Daily Briefing Narrate) and the junk rows?
    - Deploy universal-ingest to dev?
15. **AgentService prompts (AI-01/02).** Executor reads the Action's system prompt (one source of truth), or every node authors its own `prompt`?
16. **Which project owns PR-A** (ontology vs notification-spine vs daily-update-service), and whether to file GitHub issues per defect. I have filed none.

---

## Appendix A: read-only queries used

- MCP `describe`: `tables/appnotification`, `tables/sprk_playbooknode`, `tables/sprk_event`, `tables/sprk_communication`, `tables/sprk_workassignment`.
- MCP SQL:
  - `appnotification` TOP 20 by `createdon` (data, priority, toasttype, `sprk_*`);
  - `SELECT statecode FROM appnotification` (error: attribute does not exist);
  - `systemuser WHERE isdisabled=0 AND accessmode<>4`;
  - `sprk_analysisplaybook WHERE sprk_playbooktype=2` (state, `lastrundate`, `configjson`, `canvaslayoutjson`);
  - `sprk_playbooknode` JOIN playbook (type 2; executor type, `isactive`, `statecode`, `dependsonjson`, `modifiedon`, action);
  - `configjson` of the unsynced Docs/Emails/Events Create/Condition/Start nodes;
  - `sprk_document` TOP 15 (`createdby`, `sprk_createdbyperson`, `sprk_matter`).
- Log Analytics:
  - `AppExceptions has 'statecode'` (10, last 2026-10-09 02:34:26Z);
  - `fan-out complete` for 4369cab2 over 8 days (cadence);
  - `union AppTraces, AppExceptions` for `200000002` / `outside the valid range` / `Could not alert administrator` over 60 days.

## Appendix B: AI-side read-only queries (parallel sweep)

- **Q1:** `sprk_analysisplaybook` (id, name, code, type, mode, trigger, statecode, system).
- **Q2:** `sprk_playbooknode` (id, playbook, name, executortype, outputvariable, dependsonjson, isactive, executionorder, action, statecode).
- **Q3:** playbook count.
- **Q4:** `sprk_playbooknode` `configjson`.
- **Q5:** `describe` of `sprk_playbooknode`, `sprk_analysisplaybook`, `sprk_analysisaction`, `sprk_document`, `sprk_event`, `sprk_matter` and `sprk_playbookconsumer`.
- **Q6:** TOP 15 `sprk_document` with a TL;DR (`filetype`, `documenttype`, summary status).
- **Q7:** `sprk_analysisplaybook.sprk_canvaslayoutjson`.
- **Q8:** `sprk_playbookconsumer` with a playbook.
- **Q9:** `sprk_playbooknode_tool`, plus tools by id.
- **Q10:** `sprk_analysisaction` by id.
- **Q11:** `sprk_eventtype_ref` `124f5fc9…`.
- **Q12:** `sprk_matter.sprk_performancesummary` TOP 3.

`invoke_api` was not used.
