# Task 120 — ISS-018 fix: notification playbooks (D-77, D-78, D-79, D-80)

> Branch `fix/notification-playbooks-iss018` (worktree `C:\wts-120`), base `origin/master` @ `885d0c5f9` (includes #1413).
> Status: PR **#1461** (round 2 head `afaa63aa0`); **stopped before the dev deploy** (owner approval). Live steps below are NOT done.
> Quality: full BFF suite 18,806 pass / 0 fail / 54 skip; ArchTests 811/811; 10/10 mutations killed; one independent review pass (0 Critical; 3 Warnings + suggestions — all fix-now items fixed in `fc9960baf`, K-class noted in the PR); publish +4,925 B (38,011,616 → 38,016,541, 192 files both, Compress-Archive); no vulnerable packages.

## 1. Claims verified against code (investigation §§1–6)

| Claim | Verified | Where |
|---|---|---|
| `joinIds` writes `a,b,c` into `value=` of `in` | yes | `TemplateEngine.cs` `JoinIds` |
| Dataverse reads a list operator's values only from `<value>` children; comma form fails for real ids too | yes, LIVE (read-only) | `NotificationPlaybookFetchXmlLiveTests`: comma form with 15 real matter ids → 400 "ConditionOperator.In is empty", all 7 queries |
| Condition `Left` typed `string?`; Layer 1 renders a pure template to a JSON number | yes | `ConditionNodeExecutor.cs` `ConditionExpression`; `PlaybookOrchestrationService.WriteJsonElementWithTemplateExpansion` |
| Layer 1 renders `{{item.*}}` before the per-item loop | yes (was INFERRED) | Layer 1 had no `item` root; regression test pins the fix |
| `{{run.userId}}` renders null in Layer 1 | yes | run bag had no `userId`; fixed |
| Due Soon live nodes are canvas stubs | yes | read-only query: `{"__canvasNodeId":…}`, output variables `output_<guid>` |
| Scheduler: Warning, Success=true, lastrundate advanced on total failure | yes | `PlaybookSchedulerJob.cs` |
| `MigratedPlaybookFixture.cs` orphaned, splits commas | yes | no consumers; deleted |

**Additional defects found and fixed in this task (no parking):**
- `{{item.m_sprk_mattername}}` never resolved in all 7 (aliased key is `m.sprk_mattername`) → `{{lookup item 'm.sprk_mattername'}}`; New Documents also used non-existent `matterName`.
- New Events Create Notification read `item.modifiedon`, which the query did not select → added.
- Node descriptions misdescribed their queries ("Dedupe by…", "configurable" windows) → rewritten to state exactly what each query does (coordinator acceptance point). WA playbook description ("assigned to them") corrected.
- Repo JSON nodes had no `executorType`; `Deploy-Playbook.ps1` maps "Workflow" → 20 (CreateTask) → explicit `executorType` per node.
- `Deploy-NotificationPlaybooks.ps1` passed a parameter `Deploy-Playbook.ps1` does not have and took no `-DataverseUrl` (the release procedure passes one): it never deployed anything. Rebuilt as create-or-sync.
- `Deploy-R4-Playbook-Nodes.ps1` writes the dropped `sprk_nodetype` column and only creates rows (duplicates) → retired (throws); deletion is the owner's call.
- Live (not repo) defects the sync corrects: Emails/Events Create nodes still use OOB email/appointment fields (`activityid`, `subject`); Overdue Check/Create use `{{overdueQuery.count}}`; every live playbook-level `sprk_configjson` carries a stale `nodes` copy with `joinIds`.

## 1b. Review round 2 (coordinator, 2026-10-08)

Fixed in `d847cdd10`: truthful descriptions on all 7 playbooks (fixed 24 h / 3-day windows; task 131 makes them real); rendered `not-in`/`not-contain-values` with `Guid.Empty` refused; null/blank Condition `left` fails except for `exists`, authored null/absent `left` refused by lint C; `outputVariable` `item` reserved (runtime + ValidateAsync + lint C); `Deploy-NotificationPlaybooks.ps1 -RestoreFrom` rollback (dry-run proven) and `-Only` by playbook name. Not ours: read-notification re-notify after retry → task 131; ADR-034 amendment → coordinator; Docs/Emails/Events access (F3) → uac-r2 on #1355, live sync limited to the other four until answered: `-Only 'Tasks Overdue','Tasks Due Soon','New Work Assignments','Matter/Project Activity Summary'`.

## 2. Findings to become tasks in this project (not fixed here — reported)

1. **Scheduler ignores per-playbook parameters and cron schedules.** `dueSoonWindowUtc` is always today+3 and `timeWindowHours` always 24 (playbook `parameters.dueWithinDays` / `timeWindow` are dead config); Matter Activity / Work Assignments declare `cronExpression` schedules ("every 2 hours on weekdays") but `ParseScheduleConfig` reads only `frequency` → they run daily. Descriptions now state the real behaviour; the product decision (wire them or delete them) is the owner's.
2. **Designer saves can still clobber runtime node config** (ISS-018c). Lint C refuses deploying a stub, D-80 auditing records who/when, but nothing stops a Designer save. The canvas layout of the 7 playbooks is not rewritten by the sync.
3. **`deduplication` blocks in the repo notification JSONs are dead config**: `CreateNotificationNodeExecutor` never reads them; dedup is `NotificationActionCore`'s user + regarding + category check for unread rows. Matter Activity's key includes `modifiedon` (intent: re-notify on a new change) — not implemented. Product decision: implement or delete.
4. **Live Lookup roles** (owner/assignedAttorney/assignedParalegal on the default surface) vs repo `targeting: people` — awaiting uac-r2 on #1355 (§4).

## 3. Live steps waiting for the dev deploy (owner approval) — in order

1. Deploy BFF from the merged PR to spaarkedev1 (separate approval).
2. D-80: confirm org auditing on (`organizations?$select=isauditenabled` → true, read 2026-10-08), then `PUT EntityDefinitions(LogicalName='sprk_playbooknode')` with `IsAuditEnabled = true` (+ `IsAuditEnabled` on `sprk_configjson`), publish ONLY `sprk_playbooknode` (`PublishXml`), read back.
3. D-79: apply uac-r2's answer to the 7 Lookup nodes in the repo JSON first (if it changes `targeting`/`roles`), PR it.
4. `scripts/Deploy-NotificationPlaybooks.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -DryRun` (dry run already validated 2026-10-08: 7/7 OK; plan = Due Soon 5 nodes rewritten incl. output variables; Docs/Emails/Events gain a Lookup node; Overdue/WA/MA Lookup+Query+Create updated).
5. Same without `-DryRun`, with `-RecordPath <notes>/task-120-records` → before/after JSON per playbook; the script reads every node back and fails on any difference (the programmatic diff).
6. Deploy the alert: `az deployment group create -g spe-infrastructure-westus2 --template-file infrastructure/bicep/notification-playbook-alerts.bicep --parameters appInsightsName=spe-insights-dev-67e2xz environment=dev actionGroupResourceId=/subscriptions/484BC857-3802-427F-9EA5-CA47B43DB0F0/resourceGroups/rg-spaarke-dev/providers/microsoft.insights/actionGroups/ag-spaarke-oncall-dev` (Azure change: owner approval).
7. Live proof: zz-120- rows (one task overdue + one due soon + one document + one email + one event + one work assignment on a matter where test user A is a member; the same on a matter where user B is not), wait for / trigger a scheduler tick (or clear `sprk_lastrundate` on the 7 rows so they are due), then App Insights: `fan-out complete … failures=0` for all 7, no `failed for every user`; `appnotification` rows for A, none for B on B-excluded rows; per-item titles, `sprk_regardingid` and `viaMatter.name` populated. Delete zz-120- rows and confirm by query.
8. Re-run the read-only live test: `SPAARKE_LIVE_PLAYBOOKS_DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com dotnet test tests/integration/Sprk.Bff.Api.IntegrationTests --filter NotificationPlaybookFetchXmlLiveTests` (passed 2026-10-08 pre-deploy).

## 4. Per-environment steps (every other environment)

BFF with this fix first → `Deploy-NotificationPlaybooks.ps1 -DataverseUrl <env> -DryRun` → same with `-RecordPath` → audit on `sprk_playbooknode` (owner decision per env) → alert bicep with that env's App Insights + on-call action group → one scheduled run checked in that env's App Insights. Never run the playbook sync before the BFF: the `fetchInGuids` form fails on an old BFF (unknown helper renders empty → `in` with no values).

## 5. ADR-034 amendment text for `.claude/adr/ADR-034-user-record-membership.md` line 94 (main session applies)

Replace:

> - **MUST NOT** join through `sprk_matterteammember` or any other non-existent entity. The R2-UAT-broken `notification-new-documents.json` playbook is migrated in R3 task 050 to use the new `LookupUserMembership` node (ActionType=52) + `joinIds` Handlebars helper.

with:

> - **MUST NOT** join through `sprk_matterteammember` or any other non-existent entity. The R2-UAT-broken `notification-new-documents.json` playbook is migrated in R3 task 050 to use the new `LookupUserMembership` node (ActionType=52); the downstream FetchXML writes the ids with the `fetchInGuids` Handlebars helper: `<condition attribute="…" operator="in">{{fetchInGuids myMatters.ids}}</condition>`.
> - **MUST NOT** (corrected 2026-10-08, ISS-018 #1452) put a membership id list in the `value` attribute of a FetchXML list operator (`in`, `not-in`, `between`, …), e.g. `value="{{joinIds myMatters.ids}}"`: Dataverse reads list values only from `<value>` children, so the list is empty and the query fails for every user. `fetchInGuids` writes one `<value>` per GUID and, for an empty or invalid list, the impossible match `Guid.Empty` (selects nothing). `FetchXmlShapeValidator` enforces this in the QueryDataverse executor, deploy lint C and the repo regression test.

Suggested FAILURE-MODES entry (AP-16): "A FetchXML list operator with a `value` attribute: Dataverse ignores the attribute and reads values only from `<value>` children, so `in value="a,b"` is an empty list and fails ('ConditionOperator.In is empty'); a zero-child `in` fails too. No test executed a real list condition and the one simulator split the commas itself — 89 days of silent failure (ISS-018)."

## 6. D-89 live run (2026-10-09, spaarkedev1) - stopped at step 5

Full log: `notes/deploy-log.md` "D-89". Steps 1-4 done (BFF e9f08b764 live; sprk_playbooknode audit on; 4 playbooks synced, records in `notes/task-120-records/`; alert rule live). Step 5 stopped: run 4d79e284 Failed - MA 0/15, Overdue 0/15, Due Soon 1/15, WA 1/15.

New defects (never reached before ISS-018; fix-now, need a code PR + JSON change):
1. **False Condition with no falseBranch does not skip its trueBranch node** (`PlaybookOrchestrationService` branch gate, ~line 993: `TryExtractSelectedBranch` returns null when the condition is false and `falseBranch` is unset, and the loop `continue`s, so the gate is not counted). CreateNotification then runs with an empty body -> "Notification body is required" for every user with nothing to notify. Fix: count a ConditionResult dependency as a branching gate even when SelectedBranch is null. Test: false condition, no falseBranch -> downstream skipped.
2. **appnotification priority**: accepted values are 200000000 (Normal) and 200000001 (High). Repo JSONs use 300000000 (Overdue) and 100000000 (MA, Emails, Events) -> create fails. Fix in the 7 JSONs (Overdue 200000001; others 200000000) + a lint/validation check.
3. Cosmetic: DateOnly due dates render as "2026-10-10T00:00:00.0000000" in notification bodies (QueryDataverse converts DateTime with "o").
4. Looked wrong (K, for uac-r2): viaMatter.memberships for the team-owned zz-120 matter listed role "ownerid" (likely the async membership junction lagging the owner change).
5. Docs/Emails/Events (not synced) under the NEW BFF: Emails/Events will fail loudly at their next due tick (priority / null left / body) -> alert; Docs may deliver notifications with a blank matter name in the body. Their next ticks: Emails ~07:00Z, Docs/Events ~14:00Z Oct 9.
