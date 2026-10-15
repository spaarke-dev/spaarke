# ISS-018 (#1452) — FetchXML list rendering (`joinIds` → `in value="a,b"`): full investigation and fix design

> **Status:** investigation only. No code, commit, push or Dataverse write was made.
> **Date:** 2026-10-08. **Code basis:** `origin/master` @ `73d3b970e` (read through a scratch worktree, removed afterwards).
> **Live basis:** spaarkedev1, using read-only MCP SQL reads, one read-only Web API GET (audit metadata) and read-only Log Analytics queries on workspace `74b7349a-…` (App Insights for `spaarke-bff-dev`, 90-day retention, oldest row 2026-07-10 17:23 UTC).
> **Owner domain:** notification spine / daily-update-service (`spaarke-notification-spine-r1`, `spaarke-daily-update-service-r5`), not the ontology project.
> Statements marked **INFERRED** are reasoned from evidence but were not observed directly.

---

## 0. Headline (read this first)

1. **All 7 notification playbooks in dev have produced nothing for every user for at least 89 days.** Each of them fails on every run, for every user, and the failure is silent: users see no error, the scheduler job reports success, and `sprk_lastrundate` still advances. Evidence: App Insights `fan-out complete` logs from 2026-07-11 to 2026-10-08 show **0 successful user-runs out of 6,694** (§2).
2. **ISS-018 explains only 2 of the 7.** These are *New Work Assignments* and *Matter/Project Activity Summary*. The other 5 fail on two further defects:
   - **ISS-018b:** every **Condition node** fails. The R7 Wave 11 "Option D" auto-wrap turns `"left": "{{x.count}}"` into a JSON number, and `ConditionExpression.Left` is typed `string`. This hits 4 playbooks now, plus the 2 ISS-018 playbooks once their query is fixed.
   - **ISS-018c:** in *Tasks Due Soon*, the live node rows were overwritten with canvas stubs (`{"__canvasNodeId":…}`).

   **Fixing `joinIds` alone restores zero notifications.**
3. **Root cause of ISS-018.** FetchXML multi-value operators take their values only from child `<value>` elements. A comma list in the `value` attribute produces a condition with no values. The `joinIds` helper was designed in R3 (2026-06-23) on the false premise that `value="a,b,c"` is "exactly the shape FetchXML `operator='in'` wants". No test ever executed a real `in` condition, and the one fixture that simulated Dataverse split the commas itself.
4. **The SDK path behaves exactly like the Web API.** The BFF's `FetchExpression` → `RetrieveMultiple` path fails with the same error, `The value passed for ConditionOperator.In is empty. Attribute Name: sprk_RegardingMatter`. It fails for **empty lists and for real lists of 14–19 matter ids** (§3).
5. **Recommended fix: option (d).** It has four parts:
   - a GUID-only `<value>`-emitting helper that fails closed (an empty list emits an impossible match);
   - a fail-loud FetchXML arity validator in the executor (not a silent rewrite);
   - a repo test plus a deploy lint;
   - the Condition-node fix, the Due Soon rebuild and a scheduler failure signal.

   These ship together. D-76 is restored only after that BFF change is live (§6).

---

## 1. Blast radius

### 1.1 Live playbook nodes (spaarkedev1)

I queried `sprk_playbooknode` for every node whose `sprk_configjson` contains `{{`, every `QueryDataverse` (51) node, every `IndexRetrieve` (90) node, and every node matching `joinIds`, `operator=…in…value=`, `search.in` or `#each`. Notification playbooks are the 7 `sprk_analysisplaybook` rows with `sprk_playbooktype = 2`.

| Live node (id) | Playbook | List / helper in a query attribute | Operator | Fails? | Evidence |
|---|---|---|---|---|---|
| **Query New Work Assignments** `30371fa5-a171-…` | New Work Assignments `be7874be` | `value="{{joinIds myMatters.ids}}"` on `sprk_regardingmatter` | `in` (value attr) | **YES — every run** | 946/946 user-runs fail with "ConditionOperator.In is empty", 2026-07-11 → 2026-10-08 |
| **Query Matter Activity** `dfb7e6a0-a171-…` | Matter/Project Activity Summary `24051c80` | same | `in` (value attr) | **YES — every run** | 946/946 user-runs fail, same error |
| Query Overdue Tasks `348b6395-a171-…` | Tasks Overdue `4369cab2` | none (membership branch absent; scalar `{{todayUtc}}` only) | — | query OK; **playbook fails at Condition** (ISS-018b) | query ran 964× (86 runs had results, 821 rows); Check Results failed 964/964 |
| Query New Events `2d2fdd9e-…` | New Events `a4bc529c` | none; its eventtype `in` uses correct `<value>` children | `in` (children) | query OK; **fails at Condition** | 964/964 |
| Query New Documents `2b50e587-…` | New Documents `29051c80` | none (no Lookup node at all) | — | query OK; **fails at Condition** | 964/964 |
| Query New Emails `d9468c8e-…` | New Emails `2f46208e` | none (no Lookup node at all) | — | query OK; **fails at Condition** | 946/946 |
| Query Tasks Due Soon `b2d94997-…` (+ Lookup `b1d94997`, Check `b3d94997`) | Tasks Due Soon `77f77aa5` | config is a canvas stub `{"__canvasNodeId":…}` | — | **fails at Lookup validation** ("requires 'entityType'") | 964/964 |
| queryKpiAssessments `669c5310-…` | (insights, non-notification) | `value='{{matterId}}'`, a scalar | `eq` | no | scalar GUID, FetchXML-escaped |
| retrieveObservations `c4284512-…` | (insights) | OData `matterId eq '{{matterId}}'` | OData `eq` | no | scalar, OData-escaped (task 164) |
| retrievePrecedents / retrieveCohortObservations | (insights) | no templates in `filter` | — | no | — |

No live node uses `not-in`, `between`, `under`, `above`, `eq-or-*`, `contain-values`, OData `Microsoft.Dynamics.CRM.In`, or AI Search `search.in` with a template. The `#each` matches are all in Create Notification `body` text, which is not a query.

### 1.2 Repo playbook definitions

I searched all `*.json` holding `fetchXml`, plus `*.playbook.json`, under `src/`, `scripts/`, `infrastructure/`, `infra/` and `projects/`.

| Repo file | Condition | Operator | Would fail? |
|---|---|---|---|
| `projects/spaarke-daily-update-service/notes/playbooks/notification-tasks-overdue.json` (node 2) | `sprk_regardingmatter` `value="{{joinIds myMatters.ids}}"` | `in` (value attr) | **YES** |
| `…/notification-tasks-due-soon.json` | same | `in` | **YES** |
| `…/notification-work-assignments.json` | same | `in` | **YES** |
| `…/notification-matter-activity.json` | same | `in` | **YES** |
| `…/notification-new-events.json` | same; the eventtype list in the same query correctly uses `<value>` children | `in` | **YES** |
| `…/notification-new-emails.json` | same | `in` | **YES** |
| `…/notification-new-documents.json` | `sprk_matter` `value="{{joinIds myMatters.ids}}"` | `in` | **YES** |
| all 7 above, node 3 | Condition `"left": "{{<query>.output.count}}"` | — | **YES (ISS-018b)** |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/matter-health-single.playbook.json` | `{{matterId}}` scalar | `eq` | no |
| `infra/dataverse/sprk_analysistool-grid-overview-row.json` | `{{today}}`, `{{today±N}}` scalar dates (GridOverviewHandler) | `lt`/`ge` | no (D-25 issue is #1447) |
| `src/client/shared/Spaarke.Communication.Components/.../*.gridconfiguration.json`, `infrastructure/dataverse/charts/upcoming-todos-*.json` | no templates | — | no |

Other places carry the defective pattern as documentation or tests, not as runtime:
- `docs/guides/PLAYBOOK-AUTHOR-GUIDE.md:475, 490, 587-616, 859`
- `docs/adr/ADR-034-user-record-membership.md:146-149, 222`
- `.claude/adr/ADR-034-user-record-membership.md:94`
- `docs/architecture/ai-architecture-playbook-runtime.md:186`
- `docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md:88`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/TemplateEngineTests.cs:600-629` (asserts the defective shape)
- `tests/unit/domain/Ai/PlaybookQueryTextEscapingTests.cs:70-89` (uses it as a "legitimate" fixture)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PlaybookOrchestrationServiceTests.cs:225-238`
- `tests/integration/Sprk.Bff.Api.IntegrationTests/Playbooks/MigratedPlaybookFixture.cs`

### 1.3 Every helper, by position

Helpers are registered in `TemplateEngine.cs:47-130`: `safe`, `default`, `joinIds`, `json`, `map`, `flatten`, `distinct`, `concat`, `join`, `flatMap`.
- **`joinIds`** is the only list helper used inside a query. It is used in FetchXML `in` everywhere, and it is wrong there.
- `join` and `concat` would be equally wrong in FetchXML; there are no uses.
- `json` is applied **implicitly** by the Option D auto-wrap (`PlaybookOrchestrationService.cs:2444-2460, AutoWrapWithJsonHelper`) to every pure-template string. That produces ISS-018b, where a JSON number lands in a `string` property.
- `default` and `safe` are used only with scalars; fine.
- The `{{x ?? 'y'}}` syntax still appears in live `templateParameters.timeWindow`. Handlebars cannot parse it, the engine returns the raw template, and the property is unused (harmless; it violates R3 AC-H1.1).

### 1.4 Repo vs live drift (important for the fix)

| Playbook | Repo (master) | Live |
|---|---|---|
| New Documents / New Emails | Lookup node (`targeting: people`, uac-r2 152) + joinIds branch | **no Lookup node**; owner + matter-owner only |
| New Events | Lookup + joinIds branch | **no Lookup node**; owner + matter-owner only |
| Tasks Overdue | Lookup + joinIds branch; still the `sprk_finalduedate` OR (#1413 not merged) | Lookup node (roles, not targeting); **no joinIds branch** (D-76 reverted); `sprk_duedate` only |
| Work Assignments / Matter Activity | Lookup (`targeting: people`) + joinIds | Lookup (explicit roles) + joinIds |
| Tasks Due Soon | full definition | **canvas stubs** (overwritten 2026-06-30 11:08:52 UTC) |

The playbook-level `sprk_analysisplaybook.sprk_configjson` for Overdue and Due Soon holds the full R4 payload (written by R4 smoke 022 on 2026-06-25), **including the joinIds branch**. The orchestrator executes the `sprk_playbooknode` rows, not that blob. So R4's "deployed ✅" checks verified a copy that never runs.

---

## 2. Is it failing now, and since when?

### 2.1 Where run records live

- **`sprk_backgroundjobrun` / `sprk_backgroundjob`:** 0 rows. `SchedulingModule.cs:30-32` uses `InMemoryBackgroundJobStore`, so run history is process-local and lost on restart. The per-child `ResultJson` (`PlaybookSchedulerJob.cs:648-684`) is not persisted.
- **`appnotification`:** only 2 rows exist in dev, both 2026-10-04 and owned by `# mi-bff-api-dev` (communication-assessed, not playbook). The TTL is 7 days (`NotificationActionCore.cs:193`). No notification-playbook rows exist for the last 7 days, which corroborates that nothing is produced.
- **Entity audit:** `EntityDefinitions(sprk_playbooknode).IsAuditEnabled = false`; the org has audit enabled. There is no change history for nodes.
- **App Insights is the primary evidence.** The scheduler logs `fan-out complete — success= failures= total=` per playbook per tick (`PlaybookSchedulerJob.cs:511-513`), and `failed for user … {Error}` per user (`:481-483`).

### 2.2 Per-playbook results (AppTraces, 2026-07-11 → 2026-10-08)

| Playbook | Runs | User-runs | Success | First failing node / error |
|---|---|---|---|---|
| New Work Assignments `be7874be` | 84 | 946 | **0** | Query New Work Assignments — `ConditionOperator.In is empty` (946) |
| Matter/Project Activity `24051c80` | 84 | 946 | **0** | Query Matter Activity — `ConditionOperator.In is empty` (946) |
| Tasks Overdue `4369cab2` | 86 | 964 | **0** | Check Results — `Invalid condition configuration JSON … $.condition.left` (910 at byte 56, 54 at byte 57) |
| New Events `a4bc529c` | 86 | 964 | **0** | Has New Events? — same Condition error (964) |
| New Documents `29051c80` | 86 | 964 | **0** | Check Results — same (963 + 1) |
| New Emails `2f46208e` | 84 | 946 | **0** | Has New Emails? — same (945 + 1) |
| Tasks Due Soon `77f77aa5` | 86 | 964 | **0** | Lookup My Matters — `requires 'entityType'` (964) |

Total: **0 / 6,694**. The latest failures are at 2026-10-08 11:00 UTC, so the failure is current. About 11 active users are processed per run.

**Byte 57 vs 56 corroborates ISS-018b.** `{"__actionType":30,"condition":{"operator":"gt","left":` puts the value at byte 56. A two-digit count shifts the error to byte 57. That happened on 54 Overdue runs, meaning the query found ≥10 overdue tasks and Layer 1 rendered the count as a number.

### 2.3 What users lost (records found by the query but never notified)

QueryDataverse completion logs, per node:
- **Query Overdue Tasks:** 86 runs with results, **821 overdue-task rows** (max 10 per run).
- **Query New Emails:** 16 runs, 49 rows.
- **Query New Documents:** 6 runs, 26 rows.
- **Query New Events:** 0.
- **Work Assignments / Matter Activity:** unknown, because the query itself fails.

The Daily Briefing reads these categories from `appnotification` (`src/client/shared/Spaarke.DailyBriefing.Components/src/services/notificationService.ts`), so its notification-backed channels are empty too. **INFERRED** from the client code; not checked in the UI.

### 2.4 Since when

- **Observed:** since 2026-07-11, the retention floor (2026-07-10 17:23). The first scheduler tick in retention was 2026-07-10 18:00.
- **INFERRED start:**
  - *Work Assignments* and *Matter Activity* never succeeded. Their nodes were created 2026-06-26 16:57 carrying `joinIds`. Before Option D (commit `967fbfdde`, 2026-06-29) the orchestrator rendered only `{{paramName}}`. So the literal `{{joinIds …}}` reached Dataverse inside `value=`, where the attribute is ignored for `in` anyway.
  - The Condition-node failure (5 playbooks) starts with the first dev BFF deploy that contains `967fbfdde` (2026-06-29/30).
  - Due Soon starts 2026-06-30 11:08 UTC, when its nodes were overwritten.

### 2.5 Does a failing node fail the run, or is it swallowed?

- **The run fails.** `QueryDataverseNodeExecutor.cs:198-207` turns the SDK fault into `NodeOutput.Error`. `PlaybookOrchestrationService.cs:731-751` stops the run at the failed batch and emits `RunFailed`.
- **The scheduler swallows it.** `PlaybookSchedulerJob.cs:479-486` counts a failure and logs a **Warning**. The job then returns `Success: true` (`:286-294`), and `sprk_lastrundate` is persisted whatever the outcome (`:229-231`), so nothing retries. The child status is `"PartialFailure"` even at 100% failure (`:237`), and that status lives only in memory.

**Verdict: the worst case, a silent total failure.** No user-visible error appears, no alert fires, the admin history shows the job succeeded, and only Warning traces exist.

### 2.6 Who is affected

Every active interactive user in spaarkedev1, for all 7 notification types. Only `spaarke-bff-dev` sends telemetry to this workspace. Other environments were not checked. **INFERRED:** any environment that received these node rows has the same defects, because the BFF code is identical.

---

## 3. The SDK path and empty lists

- **The BFF path matches the Web API (primary evidence).** The App Insights message from the BFF reads `Failed to execute Dataverse query: The value passed for ConditionOperator.In is empty. Attribute Name: sprk_RegardingMatter, Attribute Id: 0a58ac18-…`. The BFF path is `QueryDataverseNodeExecutor.cs:265-270`, `new FetchExpression(fetchXml)` through `IGenericEntityService.RetrieveMultipleAsync`.
- **Lists of real ids fail too.** LookupUserMembership logs show `count=` values of 14, 15, 16, 17 and 19 on 83 of the 919 lookups for these two playbooks. Every run of those playbooks failed. So it is not "empty list only": **non-empty lists fail on the SDK path.**
- **One id:** the parent session proved the single-id case on the Web API. It was not observed on the SDK path, but the same cause applies.
- **Microsoft Learn ("condition operator values", fetchxml/reference/operators):** multi-value operators are written with child elements. Its `between` example is `<condition attribute="numberofemployees" operator="between"><value>6</value><value>20</value></condition>`, and `in-fiscal-period-and-year` likewise uses two `<value>` children. `in` is "The value exists in a list of values." **INFERRED** from the docs and the observed error: for a multi-value operator the `value` attribute does not populate the value list, which leaves zero values.
- **Zero `<value>` children** gives the same error. "Is empty" means zero values. `Tier2ScopeFilterInjector.cs:72-91` already documents "an empty `IN ()` is invalid" and short-circuits it.
  - **Consequence:** emitting `in` with no values for a user with no matters makes the run *error*. It does **not** "match zero rows". `PLAYBOOK-AUTHOR-GUIDE.md:490, 610-612` is wrong on that point.
  - In dev, 836 of the 919 lookups for these two playbooks returned 0 matters, so empty lists are the common case.
- **Escaping vs the SDK.** `FetchExpression` takes XML text, so XML escaping is what matters. Task 164 already escapes values (§5.1).

---

## 4. Root cause and history

### 4.1 Introduction

| When | Commit | Project / task | What |
|---|---|---|---|
| 2026-06-23 | `1e8c95b8e` (#415) | `spaarke-platform-foundations-r3` task 002 (FR-1B.2, FR-3H1.2) | Registered `joinIds`, specified as "comma-separated list suitable for FetchXML `operator='in'`". Its acceptance criterion was only "produces valid XML" (`002-…poml:62`). Tasks 050-052 migrated New Docs, Emails and Events to `in value="{{joinIds …}}"`. |
| 2026-06-25 | `7897277515` | `spaarke-daily-update-service-r4` task 015 | Added a Lookup node and the `joinIds` branch to all 7 repo JSONs. Smoke 022 checked the configjson blob statically. |
| 2026-06-26 16:57 | (live) | R4 tasks 024/025 deploy | Node rows created. Work Assignments and Matter Activity carry `joinIds`. |
| 2026-06-29 | `967fbfdde` | `spaarke-ai-platform-unification-r7` Wave 11 T116 | Option D: Layer 1 renders every config through Handlebars (so `joinIds` now executes) and **auto-wraps pure templates in `json`** (ISS-018b). |
| 2026-06-30 08:34–08:41 UTC | (live) | unknown | New Events query rewritten (eventtype `<value>` children, no membership branch). Overdue Check and Create nodes edited. |
| 2026-06-30 11:08 UTC | (live) | Designer save, **INFERRED** | Tasks Due Soon nodes replaced by canvas stubs (`__canvasNodeId` ids match `sprk_canvaslayoutjson`). `Deploy-Playbook.ps1:443-455` warns that the Designer clobbers configjson. |
| 2026-10-02 | `0a96dd3e1` | `unified-access-control-r2` task 152 | Repo Lookup nodes moved to `targeting: people`; `joinIds` unchanged. |
| 2026-10-08 13:14 UTC | (live) | ontology 068 | D-76 restore attempted and reverted (modifiedon now). |

### 4.2 Was `joinIds` ever correct?

**Not for FetchXML.** The design premise in `docs/adr/ADR-034:146` and `PLAYBOOK-AUTHOR-GUIDE.md:595` is false. The output shape *would* be right for Azure AI Search `search.in(field, 'a,b', ',')`, which `docs/guides/RAG-ARCHITECTURE.md:496` documents, but no playbook uses it that way. It is also wrong for Dataverse OData `Microsoft.Dynamics.CRM.In(...)`, which needs a quoted JSON-style array.

### 4.3 Who dropped the membership branch from the live overdue node?

**Not determinable.** Entity audit is off for `sprk_playbooknode`, and `modifiedon` was overwritten today. All edits run as `1d02f31c-…` (Ralph Schroeder), which is the shared deploy identity.

**Bounded by evidence.** The overdue query *succeeded* on every run from 2026-07-11, so the branch was gone by then. The node was created 2026-06-26 16:57, apparently in the same batch that gave the sibling nodes `joinIds`.

**INFERRED:** it was dropped in the 2026-06-30 08:34–08:41 edit session. That session's New Events query is the only live query that uses `<value>` children and lacks the membership branch, which suggests someone hit the `in` error and stripped it.

The live New Docs and Emails nodes **never** had the branch. **INFERRED:** they were created 2026-03-31, and R4 wrote only the playbook-level blob.

### 4.4 Why no test caught it

1. `TemplateEngineTests.cs:600-629` checks that the output is *well-formed XML* and **asserts the comma shape**.
2. `MigratedPlaybookFixture.cs:688-722` is the only Dataverse simulator for these playbooks. It **splits the comma list itself** and treats an empty list as "zero rows" (`:730-737`), encoding the wrong semantics in both directions. It is also orphaned: it has no consumers, and it stubs the pre-2026-06-23 HttpClient path that the executor no longer uses.
3. `PlaybookQueryTextEscapingTests.cs:70-89` uses the defective condition as its "legitimate" baseline.
4. Every executor test stubs `IGenericEntityService`. No test, and no FetchXML XSD, checks operator arity. The XSD allows a `value` attribute on any condition (**INFERRED** from the schema's shape), so schema validation alone would not catch this.
5. Deploy checks (R4 smoke 022/025) were static string checks against the playbook-level blob, and no post-deploy run was asserted. The scheduler's failure signal (§2.5) is invisible.

---

## 5. Related defect classes in the same renderer

### 5.1 XML / OData injection — handled

- Task 164 (uac-r2) escapes **every context value** before Layer 1 renders a query-text position. FetchXML uses `SecurityElement.Escape` (`& < > " '`); OData doubles `'`. See `PlaybookTemplateContextBuilder.cs:74-97` and `PlaybookOrchestrationService.cs:2334-2345, 2404-2418`.
- This covers helper output, because values are escaped *before* the helpers run. `PlaybookQueryTextEscapingTests` pins it: a record named `O'Brien & Sons <LLP>` cannot alter a query.
- The executor's own substitutions (`QueryDataverseNodeExecutor.cs:239-263`) use only server values (date, int, user GUID).
- **Residual risk:** the new `<value>`-emitting helper writes markup, so it must not echo arbitrary strings. GUID-only emission removes that risk (§6).
- `sprk_noaccessentry_postsave.js:402-406` concatenates config constants into `<value>`, unescaped. Constants only; low.

### 5.2 Date tokens vs D-25 — OK on this path

`{{todayUtc}}` and `{{dueSoonWindowUtc}}` are the recipient's local date:
- `PlaybookSchedulerJob.cs:449-458`, rendered by Layer 1;
- the executor fallback at `QueryDataverseNodeExecutor.cs:151-167`.

The UTC-day defect is in `GridOverviewHandler` only (#1447, ISS-017). Its `InjectToday` (`GridOverviewHandler.cs:515-530`) is scalar, so there is no list issue. A minor point: the scheduler hard-codes `dueWithinDays=3` and `AddDays(3)`, ignoring the playbook's `parameters.dueWithinDays`.

### 5.3 Other helper output invalid where it is used

- **ISS-018b, confirmed, HIGH.** The Option D auto-wrap turns `"left": "{{q.count}}"` into a JSON number. `ConditionExpression.Left` is `string?` (`ConditionNodeExecutor.cs:575`), so validation throws (`:138`). `ResolveOperand` (`:347-370`) already handles a `JsonElement`, so typing `Left` as `object?` (like `Right`) is the fix. This blocks all 7 playbooks.
- **Latent, INFERRED, HIGH likelihood.** Layer 1 also renders Create Notification's per-item templates (`itemNotification.title "Overdue: {{item.sprk_eventname}}"`, `regardingId "{{item.sprk_eventid}}"`) before the executor iterates. `item` is not in the Layer 1 context, so these render empty or null. The likely result is blank per-item titles, a null `regardingId` (which defeats dedup), and an empty `viaMatter`.
  - `recipientId: "{{run.userId}}"` also renders null in Layer 1, because the Layer 1 `run` bag has no `userId` (`PlaybookTemplateContextBuilder.cs:359-379`). It is rescued by the executor fallback (`CreateNotificationNodeExecutor.cs:600-610`).
  - This was never reached since 2026-07-11. **It must be verified before declaring notifications restored.**
- **Tasks Due Soon canvas stubs (ISS-018c).** A Designer save overwrote the runtime config. Nothing stops this, and the lint comment at `Deploy-Playbook.ps1:443-455` acknowledges it.

### 5.4 Other FetchXML/OData template renderers across the BFF and clients

| Renderer | Pattern | Verdict |
|---|---|---|
| `MembershipResolverService.cs:1494-1546` | builds `in` with `<value>` children; empty guarded upstream (`:1455-1466`); capped at MaxLimit | correct; **reference implementation** |
| `Tier2ScopeFilterInjector.cs` | `<value>` children, chunked, empty rejected (`:72-91`) | correct; reference for chunking |
| `GridOverviewHandler.InjectToday` | scalar date | no list issue; D-25 only (#1447) |
| `LookupChoicesResolver` | `$choices` metadata refs, not query text | not affected |
| Insights `MatterLiveFactResolver` / `SubjectParser` (the LiveFact resolvers) | `Guid.TryParse` → `RetrieveAsync` | not affected |
| Ontology "Policy predicates" | no FetchXML/OData predicate renderer exists on master (searched) | n/a; apply the guard when added |
| Client `fetchXmlOverlay.ts` (DataGrid membership) | DOM-built `<value>` children; empty list emits an **impossible match** (`operator='null'` on the primary id) | correct; fail-closed precedent |
| Client `chipFetchXml.ts`, `LookupMultiFilterChip.tsx` | `<value>` children; `like` value escaped | correct |
| VisualHost `fetchXmlBuilders.ts` / `DataAggregationService.ts` / `DueDateCardList.tsx` | `eq` with a cleaned GUID | correct |
| IndexRetrieve `filter` (live) | scalar `{{matterId}}`, OData-escaped | correct |

The defect is confined to playbook templates that use `joinIds`.

---

## 6. Fix design

### 6.1 Options compared

| | (a) `<value>` helper + template updates | (b) executor normalises comma `value` into children | (c) both | **(d) recommended** |
|---|---|---|---|---|
| Fixes current nodes without re-authoring | no | yes | yes | no; but every live node needs editing anyway for ISS-018b/c |
| Empty-list semantics | explicit, in the helper | must guess intent | both | explicit, in the helper |
| Hides authoring errors | no | **yes** (silent rewrite; the executed XML differs from the authored XML) | partly | no; the executor *rejects* bad arity loudly |
| Ambiguity | none (GUID-only) | a comma inside a string value | — | none |
| Protects against Designer-authored nodes (no repo test sees them) | no | yes | yes | yes (runtime validator) |

**Recommendation: (d).**
- **(d1)** A new helper `fetchInGuids` (name open), used as `<condition attribute="sprk_regardingmatter" operator="in">{{fetchInGuids myMatters.ids}}</condition>`.
  - Emits one `<value>{guid:D}</value>` per element that parses as a GUID; deduplicated and ordered.
  - Placed next to `joinIds` in `TemplateEngine.cs`.
- **(d2)** A shared `FetchXmlArityValidator` in Spaarke.Dataverse or `Services/Ai`, called by `QueryDataverseNodeExecutor` after variable resolution and before `RetrieveMultiple`.
  - It parses with `XDocument` and rejects: a multi-value operator (`in`, `not-in`, `between`, `not-between`, `in-fiscal-period-and-year`, `in-or-after-…`, `in-or-before-…`, `contain-values`, `not-contain-values`) with a `value` attribute; zero `<value>` children; `between` without exactly 2; and any leftover `{{`.
  - It returns `NodeErrorCodes.InvalidConfiguration`, naming the attribute and operator. It fails loud and does **not** rewrite.
  - The same validator backs the repo test and the deploy lint, so there is one owner for the rule (§11).
- **(d3)** Keep `joinIds`, but document it as AI-Search-`search.in`-only. Lint forbids it inside `fetchXml`.
- **(d4)** Fix ISS-018b: `ConditionExpression.Left` becomes `object?`.
- **(d5)** Fix the latent Layer 1 per-item rendering. Leave executor-scoped roots (`item`) unrendered in Layer 1, or exempt `itemNotification`.
- **(d6)** Scheduler signal. Log at **Error** and mark the child `Status="Failed"` when `failureCount == userCount > 0`, and surface the failure rate in `ResultJson`. Optionally hold back `sprk_lastrundate` on total failure (owner decision). Add an App Insights alert on `failed for user`.

**Component justification (§11):**
- *Existing:* `joinIds`, `MembershipResolverService.BuildTransitiveFetchXml`, `Tier2ScopeFilterInjector`.
- *Extension:* the helper extends `TemplateEngine`'s helper set (no new service). The validator could extend `QueryDataverseNodeExecutor.Validate`, but it must run on the *rendered* XML, so it is a static helper called from `ExecuteAsync`.
- *Cost of doing nothing:* 2 of 7 notification playbooks stay broken, and any author can reintroduce the defect silently.

Hot-path: BFF changes need a `<hot-path-declaration>` and a publish-size delta (§10).

### 6.2 Empty lists: fail closed, selecting nothing

When the list is empty, null or unresolved, the helper emits `<value>00000000-0000-0000-0000-000000000000</value>`. This is an **impossible match**: the condition stays valid and the OR branch selects nothing, so owner and matter-owner branches still work. It must **not** be an error, because 91% of dev lookups are empty and an error would fail the run for most users.

If any element is not a GUID, the helper emits only the impossible match and logs a Warning. It never broadens the query. This follows the client precedent in `fetchXmlOverlay.ts` (impossible match, not everyone's records).

The validator still rejects a hand-written `in` with zero values.

### 6.3 Escaping

GUID-only canonical `D` output contains nothing to escape, and it is identical whether or not Layer 1 already XML-escaped the context (task 164 leaves GUID strings unchanged). The helper must never write raw input text.

### 6.4 Tests

1. **Unit, helper:** `[]`, null, undefined, one GUID, many GUIDs, duplicates, a non-GUID, and an injection string (`a"/><condition`) → exact output.
2. **Unit, validator:** the current defective `value="a,b"` is rejected; zero children are rejected; good XML passes; `between` arity; leftover `{{`.
3. **Semantic render test** over every repo playbook JSON:
   - Render through `PlaybookOrchestrationService.RenderConfigJsonStructurally` plus `ResolveFetchXmlVariables`, with contexts holding 0, 1 and many ids and hostile names.
   - Then `XDocument.Parse`, the validator, and a check that each `in` has N `<value>` children.
   - Each test must fail on today's JSON.
4. **Condition node:** feed it a Layer-1-rendered config (`"left":0`, `"left":12`) and assert it evaluates. This fails today.
5. **Live-gated, opt-in** (the repo's skip-via-return convention, as in `EventRoutesLiveTests.cs`):
   - Render each repo notification query for a seeded user with 0, 1 and many memberships.
   - Execute it through `IGenericEntityService.RetrieveMultipleAsync(new FetchExpression(xml))` with `top=1` against spaarkedev1. Read-only.
   - Assert no fault, and use a `FetchXmlToQueryExpressionRequest` round-trip to assert `Values.Count == N`.
   - This is the only test that proves Dataverse's own semantics.
6. **Tidy up (ADR-038):**
   - delete orphaned `MigratedPlaybookFixture.cs`;
   - rewrite `TemplateEngineTests.cs:600-629`;
   - change the baseline in `PlaybookQueryTextEscapingTests.cs:70-89` and in `PlaybookOrchestrationServiceTests.cs:225-238` to the new helper form.

### 6.5 Guard against reintroduction

- **Repo test** (ArchTest-style, `tests/Spaarke.ArchTests` or `tests/unit/domain/Ai`): scan `projects/**/playbooks/*.json`, `src/**/*.playbook.json` and `infra*/dataverse/*.json`. Fail on any multi-value operator carrying `value=` inside a fetchXml string, and on `joinIds` inside fetchXml. Use the shared validator (render with fixture contexts) rather than a regex alone.
- **Deploy lint "C"** in `scripts/Deploy-Playbook.ps1`, next to lints A and B: the same rule. Also refuse to deploy a node whose config is a canvas stub (`__canvasNodeId` only).
- **Runtime validator (d2):** the backstop for live and Designer-authored nodes, which no repo check sees.
- **Main session** (sub-agents cannot write `.claude/`): update `.claude/adr/ADR-034…md:94`, and add a FAILURE-MODES entry such as "FetchXML multi-value operators need `<value>` children; the `value` attribute is ignored".

### 6.6 Files and live nodes to change

**BFF and code:**
- `src/server/api/Sprk.Bff.Api/Services/Ai/TemplateEngine.cs` (helper)
- `Services/Ai/Nodes/QueryDataverseNodeExecutor.cs` (validator call)
- `Services/Ai/Nodes/ConditionNodeExecutor.cs:575`
- `Services/Ai/PlaybookOrchestrationService.cs` (Layer 1 item scope)
- `Services/Ai/PlaybookSchedulerJob.cs` (signal)
- `scripts/Deploy-Playbook.ps1` (lint C)

**Repo playbooks:** all 7 `projects/spaarke-daily-update-service/notes/playbooks/notification-*.json`. Replace the `joinIds` condition and keep the eventtype `<value>` list. **PR #1413 touches `notification-tasks-overdue.json` and `notification-tasks-due-soon.json`; merge #1413 first, then rebase.**

**Docs:**
- `docs/guides/PLAYBOOK-AUTHOR-GUIDE.md` (§§ at 475-616, 859)
- `docs/adr/ADR-034…md:146-149`
- `docs/architecture/membership-resolution-pattern.md`
- `docs/architecture/ai-architecture-playbook-runtime.md:186`
- `docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md:88`

**Live nodes (spaarkedev1), after the BFF is deployed:**
- `30371fa5-a171-f111-ab0d-7ced8ddc4a05` (Work Assignments query) and `dfb7e6a0-a171-f111-ab0d-7ced8ddc4cc6` (Matter Activity query): replace the `joinIds` condition.
- `348b6395-a171-f111-ab0d-7ced8ddc4a05` (Overdue query): D-76, add the branch in helper form.
- Tasks Due Soon `b0d94997…`–`b4d94997…`: redeploy from the repo.
- Condition nodes (`a6041587`, `2e2fdd9e`, `15999f92`, `358b6395`, `e0b7e6a0`, `e8b7e6a0`, plus Due Soon's `b3d94997`): no config change if (d4) ships.
- Owner decision for New Docs, Emails and Events (`2b50e587`, `d9468c8e`, `2d2fdd9e`): add the Lookup node and membership branch per the repo design, or keep owner-only.

**Per environment:**
1. Deploy the BFF.
2. Run `scripts/Deploy-NotificationPlaybooks.ps1` (first confirm it writes `sprk_playbooknode` rows, not only the playbook blob; see §1.4).
3. Read back each node.
4. Trigger one run per playbook for a test user with matters and one without.
5. Confirm in App Insights that `fan-out complete success>0` and that `appnotification` rows exist.

Only spaarkedev1 telemetry was checked. Other environments need their own check.

### 6.7 Restoring D-76 safely

D-76 says overdue covers tasks the user owns OR on matters they are a member of. Restore it **only after** (d1), (d2) and (d4) are live in that environment. Before that, the playbook fails at Check Results whatever the query says, so restoring early gains nothing:
- the comma form fails for every user;
- a bare `{{#each}}<value>…` fails for the roughly 91% of users with no matters.

Then add `<condition attribute="sprk_regardingmatter" operator="in">{{fetchInGuids myMatters.ids}}</condition>` to the existing OR group, read it back, and run it live for a member user and a non-member user (as 068 did with zz-068 rows).

There is an interim option with no BFF change: `{{#each myMatters.ids}}<value>{{this}}</value>{{else}}<value>00000000-0000-0000-0000-000000000000</value>{{/each}}`. Handlebars.Net 2.4.3 supports `{{else}}` in `#each`, **but this is INFERRED and needs a unit test**. It is not recommended as the end state.

Also reconcile Lookup roles vs `targeting: people` with uac-r2, per the standing memory directive.

### 6.8 Rollout order

0. **#1413 can ship decoupled.** Its live change (`sprk_duedate`, D-73) is correct, and it has no user effect until (d4) lands.
1. **BFF PR:** (d1) helper, (d2) validator, (d4) Condition fix, (d5) Layer 1 item scope, (d6) scheduler signal, plus tests. Deploy to dev.
2. **Repo playbook JSON PR:** helper form, the repo guard and deploy lint C (after #1413 merges).
3. **Dev deploy of the playbooks:** Due Soon rebuild; Work Assignments and Matter Activity; Overdue D-76. Verify per §6.6, including that per-item titles and `regardingId` are populated (§5.3 latent).
4. **Owner decision** on the New Docs/Emails/Events membership scope, then deploy.
5. Promote to other environments; add the App Insights alert.

### 6.9 Ownership and coordination

- This belongs to the notification spine / daily-update-service, not the ontology project. Candidate homes are `spaarke-notification-spine-r1` and `spaarke-daily-update-service-r5`.
- `Services/Ai/**` edits need a hot-path declaration.
- Coordinate with uac-r2 (`targeting: people`, the task 164 escaping tests).
- **Open PRs:** of 35, only **#1413** (`fix/finalduedate-readers-068`) touches affected files, namely `notification-tasks-overdue.json` and `notification-tasks-due-soon.json`. None touches TemplateEngine, the executor, the orchestrator, the Condition node, the scheduler or the deploy scripts. Caveat: `gh` lists at most 100 files per PR.

---

## 7. Severity and owner decisions

**User impact: HIGH (P1 in dev).**
- 100% of scheduled notifications, all 7 types, all users, at least 89 days, silent.
- 821 overdue-task rows, 49 email rows and 26 document rows were found but never notified.
- Daily Briefing notification channels are empty.

**Data impact: LOW.**
- No wrong writes and no over-disclosure: the failures fail closed by accident.
- Escaping holds (task 164).
- Production exposure is unknown (not observed).

**Process impact: HIGH.**
- Three independent breakages went unnoticed because the scheduler reports success, run history is in memory, entity audit is off and "deployed ✅" checks read a blob that never executes.

**Owner must decide:**
1. Adopt fix (d), or choose (a), (b) or (c).
2. Empty-list semantics: impossible match / select nothing (recommended), or error.
3. Membership scope for New Docs, Emails and Events: restore the repo design (Lookup + branch), or keep live owner-only. This also covers reconciling `targeting: people` (repo) vs explicit roles (live).
4. The ISS-018b fix location: executor `Left: object?` (recommended), or exempt `condition.left` from the Layer 1 auto-wrap.
5. The scheduler's behaviour on total failure: Error log and alert only, or also hold back `sprk_lastrundate` so the run retries.
6. Whether to ship #1413 now, decoupled (recommended: yes).
7. Which project owns the fix (notification-spine-r1 vs daily-update-service-r5), and whether to file ISS-018b, ISS-018c and the Layer 1 item-scope issue as separate GitHub issues. I have not filed them.
8. Whether to enable entity audit on `sprk_playbooknode`, and to protect runtime configs from Designer saves.

---

## Appendix — Queries used (read-only)

- MCP SQL:
  - `sprk_analysisplaybook WHERE sprk_playbooktype=2`
  - `sprk_playbooknode` by executor type, by `LIKE '%{{%'` and by `joinIds` / `in value=` / `search.in` / `#each`
  - Condition and Lookup configs
  - playbook-level configjson for Overdue and Due Soon
  - `sprk_backgroundjob(run)` (empty)
  - `appnotification`
  - `systemuser` for modifier identities
  - `audit` (empty)
- Web API GET: `EntityDefinitions(LogicalName='sprk_playbooknode')?$select=IsAuditEnabled` → false; `organizations?$select=isauditenabled` → true.
- Log Analytics (`az monitor log-analytics query -w 74b7349a-…`), on `AppTraces` and `AppExceptions`:
  - `has 'ConditionOperator.In'`
  - `'fan-out complete'` summarised by PlaybookId
  - `'failed for user'` summarised by Error
  - `LookupUserMembership node … count=`
  - `QueryDataverse node … completed … results:`
  - retention floor
