# Task 152 — Notifications and the Daily Briefing target PEOPLE

> **Task**: `tasks/152-notifications-briefing-people-targeting.poml` (GitHub #1073; membership half of #1044 + the
> generator half agreed with word-add-in-r1). **Branch**: `task/uac-r2-152` (from `task/uac-r2-141-f3`, which carries
> task 141's systemuser↔contact link). **Rigor**: FULL. **Date**: 2026-10-02.
> **Status**: code-complete. The owner has ALREADY answered the ADR approval and escalations (a), (b), (c), (d) — see
> §3 (round 3 **D1** "Path B for both" ADR-034 amendments; round 3 **B1** option (1); `droppedAsAnswered` **152(b)**;
> round 3 **S1** + **A7**; round 3 **A6** option (a); round 3 **B2** option (1) for the 146 deploy order). Nothing is
> re-asked of the owner. **Remaining before merge**: (1) the main session applies the concise ADR text (§8) to
> `.claude/adr/ADR-034-user-record-membership.md` (sub-agents cannot write `.claude/`) and the PR cites it; (2) the
> main session's publish-size measurement and `dotnet list package --vulnerable --include-transitive`; (3) the live
> gates in §7 (G2, G3, G4).

---

## 1. What the owner decided, and what that means in code

| Owner decision | Code |
|---|---|
| Round 2 item 9 / Q8 — "target Created By and Assigned To. A team-owned record does not fan out to team members." | New resolver surface `MembershipResolveOptions.PeopleTargeting` (ADR-034 A3). Binds a HUMAN `createdby`, the user-valued owner, and registry Contact-typed "Assigned *" columns via the linked contact. Never `owningteam`, `owningbusinessunit`, team/BU/org/account lookups. |
| Round 3 D1 — "Created By decides who a record is FOR — never who can open it; the briefing only lists records the user can access" | Every row the briefing, High Priority and the top-priority matter SHOW is read as the caller through the existing `IImpersonatedCommunicationQuery` (MSCRMCallerID). No app-only read of a shown row; no app-only fallback. |
| #1044 split agreed with word-add-in-r1 — human-only Created By; generators set Assigned To | `ApplicationUserCheck` (systemuser.applicationid); `AssignedToDefaults` precedence used by `TodoGenerationService` (5 rules) and `TaskActionCore`; external portal writes the calling contact. Office `CreateTodoAsync` untouched (word-add-in-r1 task 083). |
| Round 3 S1 (accepted) — BFF-created child rows name their person in Assigned To | `POST /api/v1/events` writes `sprk_assignedto` = the acting user's linked contact (escalation (c), events half). |
| Round 3 A7 (REVERSED) — Office quick-create defaults the matter/project internal Assigned-To to the maker | `RecordCreationService` writes `sprk_assignedtointernal` = the maker's linked contact unless a field mapping already set it (escalation (c), quick-create half). |
| Round 3 D1 (accepted as recommended, clarified) — "Path B for both" ADR-034 amendments; (2) = human Created By for briefings and notifications only | ADR-034 Amendment A3 is APPROVED. Full text in `docs/adr` (status: accepted); concise text in §8 for the main session to apply to `.claude/adr`. |
| Round 3 B1, option (1) (accepted as recommended) — keep personal user ownership as a third term (escalation (a)) | Kept: `ownerid` / `owninguser` = the caller is a person term. |
| `raw/session27-owner-questions.json` `droppedAsAnswered` "152(b)" — answered by round 2 item 9 (escalation (b)) | High Priority = the flagged records in the caller's people-targeted set that the caller can read. |
| Round 3 A6, option (a) (accepted as recommended) — a child-entity Assigned field gives no root grant (escalation (d); `droppedAsAnswered` merges 152(d) into A6) | The external-portal to-do default writes `sprk_assignedto` = the calling contact; no grant path treats it as grant-bearing. |
| Round 3 B2, option (1) (accepted as recommended) — if 152 cannot deploy with or before 146, an interim drop-out of some to-dos from the briefing is accepted, announced and recorded in the PR | Deploy-order note for the PR (not a code change). This is the round 3 consolidated B2, not round 4's "B2" (the `sprk_externalobjectid` alternate key). |

## 2. Changes (placement: every change extends an existing BFF component — §10)

| Area | File(s) | Change |
|---|---|---|
| Resolver | `Services/Ai/Membership/IMembershipResolverService.cs`, `MembershipResolverService.cs` | `PeopleTargeting` option + `MembershipResolveOptions.People`; `FilterToPeopleTargetingTermsAsync`; `ArgumentException` with `AccessConferringOnly`; `|p:1` appended to the options hash only when set (every pre-existing key byte-identical); `people_targeting_no_linked_contact` / `people_targeting_createdby_skipped` warnings. |
| Human check | `Services/Ai/Membership/ApplicationUserCheck.cs` (new, internal static) | `systemuser.applicationid` read + `EntityReference.LogicalName` → `PersonIdentityType` map. One answer for the resolver, the publishers and reconciliation. |
| Daily Briefing | `Services/Ai/Narrators/DailyBriefingCollector.cs` | All 6 channels + High Priority use `People`; documents = own person terms OR parent matter/project; events = event OR regarding matter/project; to-dos via the resolver (the `owninguser` conditions are gone); every shown row read as the caller, chunked at 50 ids; failed reads → `FailedChannels` / `HighPriorityCollection.FailedEntityTypes`; all channels (or all HP entities) failing throws. The collector no longer holds an app-only client at all. |
| DTOs / composite | `Api/Ai/DailyBriefingEndpoints.cs`, `Services/Ai/Narrators/DailyBriefingCompositeService.cs` | `failedChannels` on the request + response, `highPriorityFailedEntityTypes` on the response; carried to the ledger payload and the email (a "Some sections could not be loaded" line). |
| Widget | `src/client/shared/Spaarke.DailyBriefing.Components` (`briefingService.ts`, `useBriefingRender.ts`, `DailyBriefingApp.tsx`) | Warning bar naming the failed sections; a failure keeps the widget out of the "all caught up" empty state. |
| Workspace briefing | `Services/Workspace/BriefingService.cs`, `Api/Workspace/Models/BriefingResponse.cs` | Top-priority matter from `People`; details + overdue-task counts read as the caller; `TopPriorityMatterUnavailable` on failure. **Discovered defect fixed**: the detail query selected `sprk_name`, `sprk_overdueeventcount`, `sprk_totalspend`, `sprk_duedate` — none exist on `sprk_matter` (verified live, read-only) — so the top matter was always null. Now `sprk_mattername`, `sprk_totalspendtodate`, `sprk_totalbudget`, overdue tasks counted from `sprk_event`; `Deadline` is null (no column). |
| Playbook node | `Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs` | `targeting` config key (`"people"` or omitted; anything else fails validation); schema field documented. |
| Playbook sources | `projects/spaarke-daily-update-service/notes/playbooks/notification-*.json` (7) | `"roles": [...]` → `"targeting": "people"` (the roles filter would have dropped Created By; see §6). |
| To-do / task writers | `Services/Dataverse/AssignedToDefaults.cs` (new, internal static); `Services/Workspace/TodoGenerationService.cs`; `Services/Ai/Nodes/ActionCore/TaskActionCore.cs`, `CreateTaskNodeExecutor.cs`, `PublicContracts/ActionSeam.cs`, `IActionSeam.cs` (`ActingUserId`, `AssignedToContactId`); `Services/Communication/CommunicationCreateTaskApplyService.cs`, `CommunicationRiActionService.cs`; `Infrastructure/ExternalAccess/ExternalDataService.cs`, `Api/ExternalAccess/ExternalProjectDataEndpoints.cs` | Precedence: supplied → triggering person's linked contact → parent `sprk_assignedtointernal` → `sprk_assignedattorney1` (a parent without those columns defers to its stamped core record) → blank + `todo_unassigned`. Never a team, never an email match. |
| Scheduled job | `TodoGenerationService.cs`, `Infrastructure/DI/WorkspaceModule.cs`, `tests/Spaarke.ArchTests/WorkloadPlacementGuardTests.cs` | **ADR-052 §1 migration** (the task changes this timer service's behaviour): `BackgroundService` + `PeriodicTimer` → `IScheduledJob` via `AddScheduledJob` (cron compiled from `StartHourUtc`/`IntervalHours`); ratchet list 14 → 13. Rules 2/4/5 now read through `IDataverseService.RetrieveMultipleAsync` instead of an unwrapped `ServiceClient` (same query; testable). |
| Event writer | `Api/Events/EventEndpoints.cs`, `src/server/shared/Spaarke.Dataverse/Models.cs`, `DataverseWebApiService.cs` | `CreateEventRequest.AssignedToContactId` → `sprk_AssignedTo@odata.bind` (S1). |
| Quick-create | `Services/Office/RecordCreationService.cs` | A7: `sprk_assignedtointernal` = maker's linked contact. |
| Event semantics | `Services/Ai/Membership/Events/MembershipChangedEvent.cs` (`ForRowOwner`), `Events/MembershipOwnerEvents.cs` (new, internal static); the four publishers (`DataverseDocumentsEndpoints.cs`, `EventEndpoints.cs`, `OfficeEndpoints.cs` quick-create, `OfficeService.cs` save); `MembershipReconciliationJob.cs` | Events state the row's REAL owner (Team/User) in Dataverse ids; owner typed from `EntityReference.LogicalName`; application-user owners skipped by both writers (unknown → reconciliation keeps the existing row). The three false comments are removed. |
| Docs | `docs/adr/ADR-034-user-record-membership.md` (A3), `docs/architecture/membership-resolution-pattern.md`, `docs/guides/{MEMBERSHIP-RESOLUTION-GUIDE,PLAYBOOK-AUTHOR-GUIDE,JPS-AUTHORING-GUIDE}.md` | Third surface; consumers table; `targeting` key; event semantics. |

### CLAUDE.md §11 justification for the three new files (all internal static helpers, no DI registration)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `ApplicationUserCheck` | Nothing reads `systemuser.applicationid` (grep). `IdentityNormalizationService` reads the systemuser row but its `PersonIdentity` is cached and serialized on the public memberships endpoint. | Adding the flag to `PersonIdentity` would change that endpoint's JSON and the identity cache shape for one surface's need. | Without ONE check, the publishers and reconciliation would decide "is this a person" separately — the key-mismatch defect this task fixes — and the resolver would bind the BFF app user's Created By to everything it ever created. |
| `AssignedToDefaults` | Office `CreateTodoAsync` writes an assignee from its request only (word-add-in-r1's). No precedence rule exists. | One rule used by two writers (TodoGenerationService, TaskActionCore); inlining it twice would let the two diverge. | Server-created to-dos and tasks name nobody and reach no one's briefing (#1044). |
| `MembershipOwnerEvents` | The four publishers each built the event inline. | `MembershipChangedEvent.ForRowOwner` is the factory the POML asks for; this is the async read-back + app-user rule around it, shared by four sites. | Four copies of the read-back + app-user rule — the divergence that produced oid-vs-id keys. |

Changed constructors (existing registrations, auto-wired — no new registration): `DailyBriefingCollector`
(`IGenericEntityService` → existing `IImpersonatedCommunicationQuery`), `BriefingService` (+`IImpersonatedCommunicationQuery`),
`CreateTaskNodeExecutor` / `ActionSeam` / `TaskActionCore` / `RecordCreationService` (+existing singleton
`IIdentityNormalizationService`). `TodoGenerationService` registration changed from `AddHostedService` to
`AddScheduledJob` (ADR-036). No new endpoint, package, option class, Dataverse column, interface or plugin.

**Placement (ADR-052)**: the to-do job stays in the BFF — B2 (uses BFF domain code: `TodoRegardingBuilder`,
`CoreAncestorResolver`), B3 (once a day, low volume, same identity and cadence). Under `ScheduledJobHost` it now runs
once per schedule across instances (the old timer ran on every instance).

## 3. Escalation triggers — what fired, what did not

| Trigger | Outcome |
|---|---|
| 141's link not merged / not surfaced as `PersonIdentity.ContactId` | **Not fired.** This branch is built on `task/uac-r2-141-f3`; `IdentityNormalizationService` surfaces the link as `ContactId` (141-link-contract §5). 141 itself is `code-complete-live-gates-pending`, so live behaviour waits on 141's gates (dev today: 1 of 11 interactive users linked). |
| (a) personal ownership | **Answered by the owner**: round 3 **B1**, option (1) — keep it (accepted as recommended). Implemented. |
| (b) High Priority scope | **Answered by the owner**: round 2 item 9, recorded as `droppedAsAnswered` "152(b)" in `raw/session27-owner-questions.json` ("Show only flagged records in the user's people-targeted set that they can read"). Implemented. |
| (c) app-only creates lose the human creator | **Answered by the owner**: S1 (accepted) → events default Assigned To; A7 (reversed) → quick-create defaults the internal Assigned-To. Implemented both. Coordination note for 142 / word-add-in-r1 in §9. |
| (d) external portal Assigned To grant-bearing? | **Answered by the owner**: round 3 **A6**, option (a) — a child-entity Assigned field confers no root grant (`droppedAsAnswered` merges 152(d) into A6). Also **not fired** on the code: No grant path treats `sprk_todo.sprk_assignedto` as grant-bearing today: only the three roots are composed for access, and task 142's escalation (f) recommends child-entity registry entries create no root grant. **If 142's owner answer reverses (f), reconcile before 142 merges** (the external create would then issue grants to the calling contact). |
| ADR amendment declined / path A | **Not fired — the owner approved path B**: round 3 **D1** ("Path B for both" ADR-034 amendments; (2) is this one), accepted as recommended and clarified. Full version applied in `docs/adr` (status: accepted). The only remaining step is the main session applying the §8 concise text to `.claude/adr` and citing it in the PR. |
| Deploy order vs task 146 | **Answered by the owner**: round 3 **B2**, option (1) — an interim drop-out of some to-dos from the briefing is accepted if 152 cannot deploy with or before 146; announce it and record it in the PR. |
| Impersonated read cannot serve a channel | **Not fired.** The email leg is `POST /api/ai/daily-briefing/email` — an authenticated request, not a scheduled job — so it impersonates exactly like the render leg. Every channel's filter is expressible in OData (`Microsoft.Dynamics.CRM.NextXDays/OnOrBefore/OnOrAfter`, `eq … or …`). Prerequisite (already live per session27): `prvActOnBehalfOfAnotherUser` on the BFF app users. |
| word-add-in-r1's #1044 change implements a different rule | **Not fired.** `QueryTodosAsync` on this branch (which carries #1045) still had the `owninguser` filter; #1045 did not change it. |
| A live notification playbook intends team-wide notification | **Not fired.** The four live LookupUserMembership playbooks are per-user ("Lookup My Matters" → CreateNotification): Matter/Project Activity Summary, Tasks Due Soon, Tasks Overdue, New Work Assignments. |
| A consumer relies on the old caller-oid event semantics | **Not fired.** The only `MembershipChangedEvent` consumer is `MembershipJunctionUpdater` (writes `PersonId` verbatim); the only other `sprk_userentityassociation` reader (`DataverseServiceClientImpl` ~:3148, communication participant correlation) reads Contact rows (personidtype 2), which owner events never write. `schemaVersion` unchanged. |

## 4. Read-only live facts gathered (spaarkedev1, 2026-10-02)

- `sprk_matter` has **no** `sprk_name`, `sprk_overdueeventcount`, `sprk_totalspend` or `sprk_duedate`
  (`read_query` refused them); it has `sprk_mattername`, `sprk_totalspendtodate`, `sprk_totalbudget`,
  `sprk_assignedtointernal`, `sprk_assignedattorney1/2`, `sprk_highpriority`, `sprk_monitor`.
- LookupUserMembership nodes (`sprk_playbooknode.sprk_executortype = 52`), all active, all feeding Create Notification:

| Playbook (`sprk_analysisplaybook`) | Playbook id | Node id | Live config |
|---|---|---|---|
| Matter/Project Activity Summary | `24051c80-5f2d-f111-88b5-7ced8d1dc988` | `99f9ee9c-a171-f111-ab0d-7ced8ddc4a05` | `roles: [owner, assignedAttorney, assignedParalegal]`, no `targeting` |
| Tasks Due Soon | `77f77aa5-5f2d-f111-88b5-7ced8d1dc988` | `b1d94997-9574-f111-ab0e-7ced8ddc4a05` | **only `{"__canvasNodeId": …}` — no `entityType`; the node fails validation at run time today (pre-existing)** |
| Tasks Overdue | `4369cab2-5f2d-f111-88b5-7ced8d1dc988` | `4bd19f94-a171-f111-ab0d-7ced8ddc4cc6` | `roles: [owner, assignedAttorney, assignedParalegal]`, no `targeting` |
| New Work Assignments | `be7874be-5f2d-f111-88b5-7ced8d1dc988` | `e7b7e6a0-a171-f111-ab0d-7ced8ddc4cc6` | `roles: [owner, assignedAttorney, assignedParalegal]`, no `targeting` |

  None currently binds `owningteam` (the `roles` filter excludes it, and `owner` does not match the discovered role
  `ownerid`), so today they resolve only the Assigned contacts. With `targeting: "people"` and no `roles` they resolve
  Created By (human) + personal owner + every registry Assigned contact — the owner's rule.

## 5. Tests (ADR-038 KEEP; no `Mock<HttpMessageHandler>`, no DI-registration or ctor-null tests)

New: `MembershipResolverPeopleTargetingTests` (9), `MembershipOwnerKeyParityTests` (7), `TodoGenerationAssignedToTests`
(16 incl. theory rows), `TaskActionCoreAssignedToTests` (5), `RecordCreationAssignedInternalTests` (3),
`OfficeMembershipPublishingTests` (3), `failedSections.test.ts` (5, client). Rewritten (they asserted the old app-only /
caller-oid behaviour or rebuilt the event by hand): `DailyBriefingCollectorTests` (25 + the unchanged TL;DR facts tests),
`BriefingServiceTests` (12), `DataverseDocumentsEndpointsMembershipPublishingTests`, `EventEndpointsMembershipPublishingTests`.
Extended: `LookupUserMembershipNodeExecutorTests` (+5), `MembershipChangedEventTests` (+6), `ExternalTodoScopeTests` (+2
and the endpoint test now asserts the calling contact is passed). Adjusted fixtures only (assertions unchanged):
`MembershipReconciliationJobTests` (systemuser read now stubbed human), `DailyBriefingOverviewHandlerTests`,
`DailyBriefingCompositeServiceTests`, and the constructor-argument call sites of `TaskActionCore` / executor / seam.
One existing assertion changed on purpose: `ReadLookupAsIdentity_UnknownIdentityType_ReturnsNullPair` asserted that a
descriptor's declared type overrides the value's type — the exact behaviour criterion 15 reverses; it now pins that a
`businessunit` value yields no identity.

Existing `MembershipResolverServiceTests` (default and `AccessConferringOnly` surfaces) and
`AccessibleRecordSetServiceTests` pass **unedited** (criteria 2, 16).

Perturbations (each seeded, run, restored, file touched): see §5.1.

### 5.1 Perturbations — every new guard bites

30 seeded violations, each run against its test filter, the source restored byte-for-byte and touched. All 30 went
RED. Three seeds (C8, R1, O1) first failed to COMPILE (CS0162 under warnaserror — a constant condition), which proves
nothing; they were reshaped with non-constant conditions and re-run, and then failed on assertions.

| # | Seeded violation | File | Failing test(s) |
|---|---|---|---|
| P1 | owningteam / Team-typed descriptors admitted to the people surface | `MembershipResolverService.cs` | 4 — `People_EmitsConditionsOnlyForPersonTerms_GroupTypedDescriptorsEmitNothing`, `People_TeamOwnedRowWhoseOnlyLinkIsTheBuDefaultTeam_IsNotSelected`, `People_ReturnsRowsMatchedByCreatedByOwnerAndAssigned_UnderTheirRoles`, `People_AndDefault_NeverShareACacheEntry_InEitherOrder` |
| P2 | `createdby` bound for an application or unreadable systemuser | same | `People_SystemUserRowUnreadable_CreatedByTreatedAsNotAPerson` |
| P3 | any Contact-typed column admitted (registry ignored) | same | `People_EmitsConditionsOnlyForPersonTerms_GroupTypedDescriptorsEmitNothing` |
| P4 | options hash ignores `PeopleTargeting` | same | `People_AndDefault_NeverShareACacheEntry_InEitherOrder` |
| P5 | People + `AccessConferringOnly` not rejected | same | `People_WithAccessConferringOnly_ThrowsBeforeAnyIo` |
| C1 | a failed chunk/read swallowed (channel shrinks) | `DailyBriefingCollector.cs` | 5 (failed-vs-empty, one-channel-fails, failed-chunk, HP entity fails, all-fail throws) |
| C2 | a failed matters set reported as empty | same | 5 (incl. `CollectAsync_FailedCandidateSet_FailsEveryDependentChannel`) |
| C3 | no throw when every channel fails | same | `CollectAsync_EveryChannelFails_Throws_NeverAnEmptyBriefing` |
| C4 | no chunking at 50 ids | same | `CollectAsync_CandidateIdsAreChunked`, `CollectAsync_FailedChunk_FailsTheChannel_NeverShrinksIt` |
| C5 | default surface instead of People | same | `CollectAsync_ResolvesEveryCandidateSetThroughThePeopleTargetingSurface` |
| C6 | High Priority drops the people id term | same | 4 (incl. `CollectHighPriorityAsync_FlaggedRecordOutsideThePeopleSet_IsNeverReturned_ForAnyEntity`) |
| C7 | queries even when nothing is for the caller | same | `CollectAsync_DocumentNamingNoOneOnAParentNamingNoOne_AppearsForNoOne` |
| C8 | HP document drops the parent-matter term | same | caught after reshape |
| W1 | Workspace top matter on the default surface | `BriefingService.cs` | `GetBriefing_ResolvesCandidatesThroughThePeopleTargetingSurface` |
| W2 | caller-read failure silent | same | `GetBriefing_CallerContextReadFails_TopMatterUnavailable_NoAppOnlyFallback` |
| W3 | no chunking | same | `GetBriefing_LargeCandidateSet_IsChunked_EveryChunkReadAsTheCaller` |
| R1 | reconciliation types the owner from the descriptor | `MembershipReconciliationJob.cs` | caught after reshape |
| R2 | reconciliation keeps application-user owners | same | `ApplicationUserOwner_IsSkippedByBothWriters` |
| R3 | unknown app-user check deletes the existing row | same | `ApplicationUserCheckUnreadable_ReconciliationKeepsTheExistingRow` |
| O1 | publisher keeps application-user owners | `MembershipOwnerEvents.cs` | caught after reshape |
| E1 | owner always typed User | `MembershipChangedEvent.cs` | 6 (documents, events, Office save + quick-create publishers, factory theory, key parity) |
| A1 | a supplied assignee overwritten | `AssignedToDefaults.cs` | 2 (`SuppliedAssignee_IsNeverOverwritten` in both writers' tests) |
| A2 | triggering person ignored | same | 4 |
| A3 | attorney fallback dropped | same | 2 |
| X1 | executor drops the acting user | `CreateTaskNodeExecutor.cs` | `Executor_PassesThePlaybooksActingUser` |
| T1 | external payload drops the assignee | `ExternalDataService.cs` | `BuildTodoCreatePayload_WithTheCallingContact_BindsAssignedTo` |
| T2 | external endpoint drops the calling contact | `ExternalProjectDataEndpoints.cs` | 3 (theory rows) |
| L1 | node `targeting` ignored | `LookupUserMembershipNodeExecutor.cs` | 2 (theory rows `people` / `PEOPLE`) |
| Q1 | project A7 default removed | `RecordCreationService.cs` | `QuickCreate_NamesTheMakerInAssignedToInternal(Project)` |
| G1 | Rule 5 triggering person dropped | `TodoGenerationService.cs` | `Rule5_AssignedTask_TheSourceEventsAssigneeIsTheTriggeringPerson` |

Final runs (2026-10-02): full BFF unit suite **13,858 passed / 0 failed / 54 skipped (13,912)**; NetArchTest
**341 / 0 / 0**; `Spaarke.DailyBriefing.Components` `tsc --noEmit` clean and `failedSections.test.ts` 5 / 0; the
consuming `src/solutions/DailyBriefing` production build (`npm run build`: css-reset check, tsc surface gate with 0
surface-owned errors, vite) succeeded. No package references changed.

## 6. Notable decisions

- **`roles` removed from the notification playbooks.** Live role names are lower-case derived (`ownerid`,
  `assignedattorney`, …), so the configured `owner` never matched anything and a `roles` filter would also drop the
  synthesized `createdBy`. People targeting already restricts to person terms, so the filter is removed rather than
  extended.
- **Workspace top matter "unavailable"** is a new boolean on `BriefingResponse` rather than a silent null, mirroring
  the briefing's failed-channel marker.
- **Event priority/status filters in OData** use Dataverse query functions (`NextXDays`, `OnOrBefore`, `OnOrAfter`) so
  the impersonated reads keep the QueryExpression semantics they replace.
- **Cache key**: `|p:1` appended only when set — no key change, no cold cache and no `CacheVersion` bump for any
  existing caller.
- **Rule 1/3 parent**: the event's `sprk_assignedtointernal` / `sprk_assignedattorney1` (the constraint's rule 2 of the
  precedence); only Rule 5 has a named triggering person (the source event's `sprk_assignedto`). Observation for the
  owner: an overdue event's own `sprk_assignedto` may be the more natural target for Rules 1/3 — not changed here.

## 7. Pending manual gates (live writes — the main session runs approved ones)

| # | Gate | Command / action | Verify |
|---|---|---|---|
| G1 | Deploy the BFF with this branch (after merge approval) | `scripts/Deploy-BffApi.ps1` per `docs/guides` (main session) | `/healthz` |
| G2 | Switch the four live notification playbooks to `targeting: "people"` | Dry run first: `pwsh scripts/Deploy-Playbook.ps1 -PlaybookDefinitionPath projects/spaarke-daily-update-service/notes/playbooks/notification-matter-activity.json -DryRun` (repeat for `notification-tasks-due-soon.json`, `notification-tasks-overdue.json`, `notification-work-assignments.json`), then without `-DryRun`. Alternative (narrower, Dataverse MCP `update_record` on `sprk_playbooknodes`): set `sprk_configjson` of nodes `99f9ee9c-a171-f111-ab0d-7ced8ddc4a05`, `b1d94997-9574-f111-ab0e-7ced8ddc4a05`, `4bd19f94-a171-f111-ab0d-7ced8ddc4cc6`, `e7b7e6a0-a171-f111-ab0d-7ced8ddc4cc6` to `{"__actionType":52,"entityType":"sprk_matter","targeting":"people","includeRelated":false}` (keep `__canvasNodeId` on `b1d94997…`). ⚠️ The three playbooks not live in dev (new-documents/emails/events) are NOT deployed by this gate. | Re-query: `SELECT sprk_playbooknodeid, sprk_configjson FROM sprk_playbooknode WHERE sprk_executortype = 52` → all four carry `"targeting":"people"`; zero on the default. |
| G3 | Task 141's gates (schema → deploy → report-only job run → writes enabled) | per `task-141-identity-binding.md` §8 | ≥ 9 of 11 interactive users linked (141-link-contract §6) — the Assigned term is dark for an unlinked user |
| G4 | Criterion 18 manual live gate (dev; existing non-admin test users in the correct BU; **no user relocation**; per owner round 4 item 1 ask the owner to create users at specific access levels if needed) | (1) user A creates a to-do in MDA, owned by the BU default team, `sprk_assignedto` = user B's linked contact → shows for A and B, not for C (same BU, not named). (2) team-owned matter with B's contact in `sprk_assignedattorney1` → B yes, C no. (3) flagged record naming nobody → in no one's High Priority. (4) a record naming B that B cannot open in MDA → absent from B's briefing. (5) a TodoGenerationService to-do on a matter whose `sprk_assignedtointernal` = B's contact → B yes, C no (needs `TodoGeneration` Rule 2 run: trigger job `todo-generation`). (6) a user with no linked contact sees what they created/personally own, nothing via Assigned. (7) B's email leg contains the same rows as B's render. | Record users, record ids and what each saw in §10 of this file. |

## 8. Concise ADR-034 text for the MAIN SESSION (`.claude/adr/ADR-034-user-record-membership.md`)

Sub-agents cannot write `.claude/` (root CLAUDE.md §3). The owner has ALREADY approved the amendment (round 3 **D1**,
"Path B for both"), so the main session applies this text with, or before, the task 152 PR and cites it there:

**(i) Header — add after the A1 block (line ~12):**

```markdown
> ⚠️ **Amendment A3 (2026-10-02, ACCEPTED — owner round 3 D1, `unified-access-control-r2` task 152, path B)**: a THIRD consumption
> surface — **people targeting** ("which records are FOR this person": briefing, notifications). Binds a **human**
> `createdby`, the user-valued owner, and registry Contact-typed "Assigned *" columns through the caller's linked
> contact; **never** team / business-unit / organization ownership. Admits `createdby` on that surface only. Owner
> events name the row's REAL owner (team or user) in Dataverse ids. Full text:
> [full ADR](../../docs/adr/ADR-034-user-record-membership.md#amendment-a3-2026-10-02-accepted-the-people-targeting-surface--who-a-record-is-for).
```

**(ii) MUST — amend the global-exclusions bullet (line 63) to:**

```markdown
- **MUST** apply the 4 global field exclusions (`createdby`, `modifiedby`, `createdonbehalfby`, `modifiedonbehalfby`) — these are touch-history, not association. **Exception (A3):** the people-targeting surface admits a HUMAN `createdby` (caller's `applicationid` null) and nothing else of the four; the exclusion stands for AI scoping and authorization.
```

**(iii) MUST — add after the A1 rules (after line 77):**

```markdown
- **MUST** (**A3**, task 152) select records for a person's briefing, notifications and attention surfaces through the **people-targeting surface** (`MembershipResolveOptions.PeopleTargeting` / `.People`): human `createdby` + user-valued owner + registry Contact-typed "Assigned *" columns via the linked contact (`PersonIdentity.ContactId`). It **MUST NOT** bind `owningteam`, `owningbusinessunit`, a team-valued `ownerid`, or any Team/BusinessUnit/Organization/Account-typed descriptor; a team-owned record never fans out to the team. **MUST NOT** fall back to email/UPN/name matching. **MUST** be rejected together with `AccessConferringOnly`, and **MUST** be part of the options hash. Consumers **MUST** read the rows they show under the caller's Dataverse security — selecting is not authorizing. The Daily Briefing and Workspace consumers (`DailyBriefingCollector`, `PortfolioService` → `BriefingService`) **MUST** read the people-targeted set to completion (`PeopleTargetedSet`); a set larger than the resolver's ceiling is reported failed, never silently truncated. (The `LookupUserMembership` node keeps its pre-existing single 500-row page + `continuationToken`: its ids feed a FetchXML `in` list, which SQL Server caps at 2,100 parameters.)
- **MUST** (**A3**) make each `MembershipChangedEvent` describe the row's **actual owner after the write** (`Team`/teamid or `User`/systemuserid), typed from `EntityReference.LogicalName`, in the same identity space as `MembershipReconciliationJob`; both writers skip an application-user owner. No `createdby` events.
```

**(iv) Key Types — add to `MembershipResolveOptions`:**

```csharp
    // A3 (task 152): the people-targeting surface. Mutually exclusive with AccessConferringOnly.
    bool PeopleTargeting = false);
```

## 9. Coordination

- **word-add-in-r1**: (1) `QueryTodosAsync` now selects to-dos FOR the user through the resolver (human Created By,
  `sprk_assignedto` via the linked contact, personal ownership) — task 083's Office default of `sprk_assignedto` is what
  makes Office to-dos visible; nothing here touches `OfficeService.CreateTodoAsync`. (2) A7 is implemented in
  `RecordCreationService` (their file) — a small, additive change; please review. (3) Comment for #1044: membership half
  and generator half done on `task/uac-r2-152`, merge gated as above.
- **Task 142**: A7 writes a grant-bearing root column (`sprk_assignedtointernal`) on quick-create; when 142's
  materializer lands it will issue the maker a Collaborate share (they already have access via the BU team). 142's
  escalation (f) recommendation keeps child entries (incl. `sprk_todo.sprk_assignedto`) non-granting — if that changes,
  revisit the external to-do default (escalation (d)).
- **Task 036 / 142 ADR numbering**: 036 holds "Amendment 2"; this is A3 provisionally — renumber at merge if 142 lands
  first.

## 10. Read-only findings (not fixed here — outside this task's surfaces)

- ~~`PortfolioService.GetPortfolioSummaryAsync` (the Workspace briefing's metrics) selects the same non-existent
  `sprk_matter` columns (`sprk_name`, `sprk_totalspend`, `sprk_overdueeventcount`) and filters app-only by
  `ownerid`; its metrics are therefore likely always zero, and it never counts team-owned matters. Suggest a follow-up.~~
  **Withdrawn — fixed in this task** (verifier round 1 item 5; the no-deferral constraint puts discovered scope in this
  task, and the `ownerid` condition was the ad-hoc owner condition ADR-034 A3 forbids on an attention surface). See §12.
- The live "Tasks Due Soon" LookupUserMembership node has no `entityType` (config `{"__canvasNodeId": …}`) and fails
  validation on every run; gate G2 redeploys it from source.

## 11. Live gate results

_Not run — live writes are the main session's (§7)._

## 12. Verifier round 1 (2026-10-02, branch `task/uac-r2-152-r1`)

| # | Finding | Outcome |
|---|---|---|
| 1 | Reproduction of counts | Acknowledged. No change needed. |
| 2 / 14 | S1 (`POST /api/v1/events` → `sprk_assignedto`) untested; the Web API bind line was untested | **Closed.** `EventEndpoints.CreateEventAsync` is now `internal`; `EventEndpointsMembershipPublishingTests` RUNS the handler and covers three cases: linked contact → written, no link → blank, caller unresolved → blank and still created. The payload builder was extracted as `DataverseWebApiService.BuildCreateEventPayload` (internal, InternalsVisibleTo). The tests assert the `sprk_AssignedTo@odata.bind` = `/contacts(id)` bind, and that it is absent for null or an empty Guid. No `Mock<HttpMessageHandler>` is used. Seeds S1 and S2 both went red. |
| 3 / 13 | Failure markers untested past the collector | **Closed.** Five new `DailyBriefingCompositeServiceTests` cover the render response plus the ledger `sections.failedChannels` / `highPriorityFailedEntityTypes`, the render `EmptyResponse`, the email "Some sections could not be loaded" line (and its absence when nothing failed), and the email-leg `EmptyResponse`. Seeds S3a (ExecuteAsync blanked), S3b (EmptyResponse blanked) and S3c (email line suppressed) all went red. |
| 4 / 13 | 500-row candidate cap silently shrinks sets | **Closed.** New `Services/Ai/Membership/PeopleTargetedSet` (internal static) does one read at `MembershipResolveOptions.MaxLimit` (5,000). When that page comes back full, one confirmation read follows, following the `AccessibleRecordSetService.WalkMembershipPagesAsync` precedent. A set larger than the ceiling returns `Complete=false` and a `people_targeting_set_incomplete` warning. `DailyBriefingCollector` reports it as a FAILED set: its channels go into `failedChannels` and its High Priority entity into `highPriorityFailedEntityTypes`. `PortfolioService` (and through it the top-priority matter) reports it as Unavailable. Seeds S4a (incomplete treated as complete), S4b (default 500 page) and S4c/S5b (consumer ignores `Complete`) all went red. |
| 5 / 17 | PortfolioService deferred (non-existent columns, app-only ad-hoc `ownerid` filter) | **Closed in this task** (no-deferral). Details follow this table. |
| 6 / 16 | Malformed `membership-resolution-pattern.md` BriefingService row; stale text | **Closed.** The row is rewritten to 4 cells with the people-surface and `topPriorityMatterUnavailable` semantics. A `PortfolioService` consumer row was added, and so was a "Read the whole set" rule. The surfaces table names `PortfolioService`. `docs/adr` A3 gains a MUST to read the people-targeted set to completion, plus a `PortfolioService` row in its live-consumer table. §8 (iii) concise text was updated to match. |
| 7 / 15 | Publishing tests do not run the sites | **Closed for three sites, narrowed for the fourth.** Documents: the inline lambda was extracted to `internal static DataverseDocumentsEndpoints.CreateDocumentAsync`, and the tests run it (team resolved → Team event with that id and no read-back; no team → refused and nothing published). Events: the tests run `CreateEventAsync` with an app-user owner (nothing published), a team owner (Team event for the created id, read back), a human owner (systemuserid) and a throwing publisher (still 201). Quick-create: `OfficeEndpoints.QuickCreateAsync` is now `internal`, and the tests run it for a matter with a team owner (read back), a matter with an app-user owner (nothing) and a project (nothing). **Office save:** `SaveAsync` has a dozen concrete collaborators (SPE upload, job store, persistence) and no unit harness anywhere in the repo. The site's own decisions (table, team reference, read-back fallback) were extracted to `internal static OfficeService.PublishSavedDocumentOwnerAsync`, which the tests run. One whitespace-INSENSITIVE guard pins that `SaveAsync` calls it with `(documentId, owningTeamId)`. That one argument binding is the only part still pinned by text. The whitespace-exact grep is gone. Seeds S7a–S7d all went red. |
| 8 | Create-task apply names the CONFIRMER even when another assignee was chosen | **Closed.** `ActingUserId = request.AssignedTo ?? callerSystemUserId` on both the applied-proposal and ad-hoc paths. The chosen assignee's linked contact now names the task, and the confirmer is used only when no assignee was chosen. `CreateTaskApplySeamTests` asserts both cases on both paths. Seed S8 went red. |
| 9 | No resolver-level `sprk_todo` test | **Closed.** `People_Todo_TeamOwnedToDo_IsForItsHumanCreatorAndItsAssignee_NotForAnotherTeamMember` runs the resolver on `sprk_todo` descriptors. The FetchXML binds `createdby`, `ownerid` and `sprk_assignedto` (the linked contact) and never `owningteam` or the team id. The creator/assignee gets both team-owned to-dos. Another member of the same BU default team gets nothing. The harness gained optional `entity` / `callerId`. Seed S9 (the registry ignored for `sprk_todo`) went red. |
| 10 | Observations | No change (see "Owner observations" below). (4) The ADR-052 `TodoGenerationService` migration (AddHostedService → AddScheduledJob, ratchet 14 → 13) **must be called out in the PR**. |
| 11 | Verified correct | Acknowledged. |
| 12, 18, 19, 20 | Pending gates (A3 approval, escalations (a)/(b), G2, G3, G4, publish size, CVE) | G2, G3, G4, publish size and CVE still pending (main session). **Corrected in verifier round 2 (§13):** the A3 approval and escalations (a)/(b) were never pending — round 3 D1, B1 and `droppedAsAnswered` 152(b) already answer them. This round adds no package. |

**Item 5 in detail.** `PortfolioService` now selects the matters FOR the user through the people surface (`PeopleTargetedSet`). It reads them AS THE CALLER through the existing `IImpersonatedCommunicationQuery`, chunked at 50 ids, using the live-verified columns `sprk_mattername`, `sprk_totalspendtodate` and `sprk_totalbudget`. Overdue open Task events are counted from `sprk_event`. The ad-hoc `ownerid` condition and the app-only `IGenericEntityService` are gone. The read is exposed as `internal ReadMattersForSystemUserAsync` and shared with `BriefingService`: its top-priority matter now ranks the same matter set the metrics aggregate. `BriefingService` lost its duplicate detail query and its resolver / caller-query constructor arguments.

A failed read keeps the endpoint's PRE-EXISTING graceful empty portfolio (logged, never app-only). Making a failed portfolio distinguishable would change the `/portfolio` and `/health` contracts, so it is left as an owner observation. Seeds S5a (old column names), S5b (incomplete set aggregated) and S5c (overdue counts dropped) all went red. The `WorkspaceTestFixture` serves the same three matters through the two seams.

### Placement and justification (CLAUDE.md §10 / §11), this round

Everything stays in existing BFF components. There is **no new endpoint, DI registration, package, option, job, interface or Dataverse column**. Three existing endpoint handlers changed from `private` (or an inline lambda) to `internal static` so tests can run them; the routes, filters and authorization are unchanged. Constructors changed on existing auto-wired registrations:
- `PortfolioService`: `IGenericEntityService` → `IMembershipResolverService` + `IImpersonatedCommunicationQuery`.
- `BriefingService`: loses `IMembershipResolverService` + `IImpersonatedCommunicationQuery`.

New surface:
- **`PeopleTargetedSet`** (internal static helper).
  - Existing: `AccessibleRecordSetService.WalkMembershipPagesAsync`, which is private to the authorization composer in `Infrastructure/ExternalAccess`, coupled to its `AccessConferringOnly` / org-id page options and its `[WF-AUTHZ]` capped policy.
  - Extension: reusing it would make `Services/Workspace` and `Services/Ai/Narrators` depend on the authorization composer and would merge the authorization and attention surfaces that ADR-034 A3 keeps apart. A ~20-line people-surface helper beside the resolver is the smaller change.
  - Cost of doing nothing: the briefing, the portfolio and the top matter silently serve an arbitrary GUID-ordered 500-row subset to anyone with more than 500 people-targeted rows (item 4).
- **`PortfolioMatterRead`** (internal record in `PortfolioService.cs`): the "unavailable vs empty" result of the shared read. Without it, the top matter cannot distinguish "could not be determined" from "no matters".
- **`DataverseWebApiService.BuildCreateEventPayload`** and **`OfficeService.PublishSavedDocumentOwnerAsync`** (internal static extractions of existing code): without them, the S1 bind and the save site's owner decision are testable only by intercepting HTTP (banned by ADR-038 B1) or by text.

### Owner observations (item 10). No change made; raise in the PR.

1. A caller with no Read privilege on an entity (e.g. `sprk_invoice`) whose people set names rows there will see a permanent "High priority (invoices) could not be loaded", because a 403 is classed as FAILED.
2. A failed top-priority-matter determination (`TopPriorityMatterUnavailable`) is cached for the 10-minute briefing TTL.
3. A people-surface result built while the application-user check was "unknown" (createdby dropped) is cached for 5 minutes.
4. The ADR-052 migration of `TodoGenerationService` exceeds the POML's "no DI registration change" placement statement. It is justified by ADR-052 §1 (ratchet 14 → 13) and must be called out in the PR.
5. NEW this round: a failed portfolio read still returns the pre-existing zero portfolio (and caches it for 5 minutes), indistinguishable from "no matters". A marker would be an additive contract change to `/portfolio` and `/health`; this is owner's call.

**Test runs this round (2026-10-02):**
- Full BFF unit suite: **13,887 passed / 0 failed / 54 skipped (13,941)**.
- NetArchTest: **341 / 0 / 0**.

### Perturbations this round. 17 seeded, all RED, all restored and touched.

| # | Seed | Red test(s) |
|---|---|---|
| S1 | `CreateEventAsync` passes `(Guid?)null` for the contact | `CreateEvent_ActingUserWithALinkedContact_WritesThatContactToAssignedTo` |
| S2 | payload binds the wrong key | `CreateEventPayload_WithAnAssignee_BindsSprkAssignedToTheContact` |
| S3a | composite `ExecuteAsync` blanks both markers | `RenderAsync_FailureMarkers_ReachTheResponse_AndTheLedgerEntry`, `EmailAsync_FailureMarkers_…` |
| S3b | `EmptyResponse` blanks both markers | `RenderAsync_NothingToNarrateButSectionsFailed_…`, `EmailAsync_NothingToSendButSectionsFailed_…` |
| S3c | email failure line suppressed | `EmailAsync_FailureMarkers_TheEmailSaysWhichSectionsCouldNotBeLoaded` |
| S4a | incomplete set reported complete | collector `…LargerThanTheResolverCeiling…`, portfolio `…LargerThanTheResolverCeiling_Unavailable` |
| S4b | default 500-row page | collector `…LargerThanTheResolverCeiling…` (Limit verify), portfolio `…ReadAsTheCaller_WithRealColumns` |
| S4c | collector ignores `Complete` | collector `…LargerThanTheResolverCeiling_FailsItsChannel…` |
| S5a | portfolio selects `sprk_name` / `sprk_totalspend` | `GetPortfolioSummaryAsync_MattersComeFromThePeopleSurface_ReadAsTheCaller_WithRealColumns` |
| S5b | portfolio ignores `Complete` | `ReadMattersForSystemUserAsync_PeopleSetLargerThanTheResolverCeiling_Unavailable` |
| S5c | overdue counts dropped | `…ReadAsTheCaller_WithRealColumns` |
| S7a | documents site passes no owner | `DocumentCreate_PublishesTheTeamItResolvedAndWrote_NotTheCallersOid` |
| S7b | events site publishes for the wrong record | `CreateEvent_TeamOwnedRow_PublishesTheTeam_ReadBackFromTheRowItCreated` |
| S7c | quick-create publishes for non-matters | `QuickCreateMatter_ReadsTheOwnerBack…`, `QuickCreateProject_PublishesNoOwnerEvent` |
| S7d | Office save drops the resolved team | `OfficeSave_PublishesTheTeamTheSaveResolved_WithoutAReadBack` |
| S8 | create-task apply names the confirmer again | `ApplyAsync_WhenConfirmedCreateTaskProposal_…` |
| S9 | registry ignored for `sprk_todo` | `People_Todo_TeamOwnedToDo_IsForItsHumanCreatorAndItsAssignee_NotForAnotherTeamMember` |

## 13. Verifier round 2 (2026-10-02, branch `task/uac-r2-152-r1-r2`)

| # | Finding | Outcome |
|---|---|---|
| 1–4 | Reproduction + confirmed-correct areas | Acknowledged. No change. |
| 5 / 18 | `RecordCreationService.ApplyMakerAssignedInternalAsync` "a field-mapped `sprk_assignedtointernal` is never overwritten" (A7 / R3; criterion 12's "a supplied value is never overwritten") was untested — a seeded removal survived 458 tests | **Closed.** `RecordCreationAssignedInternalTests.QuickCreate_AFieldMappedAssignedToInternal_IsKept_NotOverwrittenByTheMaker` (theory: Matter ← `sprk_project`, Project ← `sprk_matter`) runs the real Field Mapping path (a profile with a Copy rule `sprk_assignedtointernal` → `sprk_assignedtointernal`, source row naming a different contact) and asserts the mapped contact is what is created, and that the maker's contact is never looked up. Seed **V5** (the verifier's own: early return `supplied.Id == Guid.NewGuid()`) → both rows RED; restored, touched. |
| 6 | `MembershipOwnerEvents.PublishOwnerAddedAsync` "Unknown (unreadable) is skipped too" untested at the publisher | **Closed.** `MembershipOwnerKeyParityTests.ApplicationUserCheckUnreadable_PublisherPublishesNothing` (theory: the systemuser read THROWS / returns no row) asserts a null result, nothing published, and exactly one systemuser read. Seed **V6** (the verifier's own: `isApplicationUser != false` → `== true`) → both rows RED; restored, touched. |
| 7 / 14 | Record said A3 approval and escalations (a)/(b) were "PENDING / CONFIRM BEFORE MERGE" | **Closed.** Header, §1, §3, §8 and §12 now cite the binding answers: round 3 **D1** ("Path B for both" ADR-034 amendments, accepted as recommended and clarified), round 3 **B1** option (1) (escalation (a)), `droppedAsAnswered` **152(b)** (escalation (b), answered by round 2 item 9), round 3 **A6** option (a) (escalation (d)), round 3 **S1** + **A7** (escalation (c)), and round 3 **B2** option (1) (interim to-do drop-out accepted if 146 deploys first — the round 3 consolidated B2, not round 4's alternate-key B2). `docs/adr/ADR-034` A3 status is now **Accepted** (heading anchor changed to `#amendment-a3-2026-10-02-accepted-…`; the in-file link and the §8 (i) link updated), and its "Open owner confirmations" section is now "Owner confirmations (all answered)". The only remaining criterion-17 step is the main session applying §8 to `.claude/adr` and citing it in the PR. |
| 8 | Drafted A3 MUST "read the people-targeted set to completion" contradicted by `LookupUserMembershipNodeExecutor` (one 500-row page) | **Closed by scoping the MUST** (the verifier's second option), in `docs/adr` A3, `membership-resolution-pattern.md` and §8 (iii). The rule binds the Daily Briefing and Workspace consumers. The node keeps its pre-existing single `DefaultLimit` page + `continuationToken`, documented with the reason it is NOT routed through `PeopleTargetedSet`: the notification playbooks interpolate `myMatters.ids` into a downstream FetchXML `in` condition (`QueryDataverseNodeExecutor` → `FetchExpression`), and a 5,000-value `in` list exceeds SQL Server's 2,100-parameter request limit. **Known limitation (pre-existing, recorded):** a person with more than 500 people-targeted matters gets notifications for a subset; closing it needs the downstream query to page/chunk its `in` list — a node/query contract change outside this task's scope. No code change. |
| 9 | High Priority capped at 50 rows per id chunk, unordered | **Recorded as PRE-EXISTING** (owner observation 6 below). Not a regression: before task 152 the HP query had `TopCount = PerChannelMaxRows` (50) per entity; the new code returns at least as many. |
| 10 | No client reads `BriefingResponse.TopPriorityMatterUnavailable`; a failed portfolio read returns + caches a zero portfolio | **Kept for the PR** (owner observations 2 and 5 below, plus 7). Fail-closed on data; the reader is not told. |
| 11 | Source-text tests are ADR-038 scaffolding candidates | **Flagged for the 090 `/test-diet`** (not deleted here): `OfficeMembershipPublishingTests.QuickCreate_FalseOwnerCommentIsGone`, `DataverseDocumentsEndpointsMembershipPublishingTests.DocumentCreateEndpoint_FalseOwnerCommentIsGone`, `EventEndpointsMembershipPublishingTests.EventCreateEndpoint_FalseOwnerCommentIsGone` (comment-absence pins), and `OfficeMembershipPublishingTests.OfficeSave_CallsItsPublishWithTheSavedDocumentAndTheResolvedTeam` (whitespace-insensitive source pin — the Office save argument binding is still pinned only by text, as reported in round 1). |
| 12 | The 7 notification playbooks still described "owner + assigned attorney + assigned paralegal" | **Closed.** Every `Lookup My Matters` node `description` in `projects/spaarke-daily-update-service/notes/playbooks/notification-*.json` (7) now says the node resolves the matters FOR the user via the people-targeting surface (human Created By, Assigned To via the linked contact, personal ownership; never team/BU/organization ownership). All 7 still parse as JSON; one line changed per file. |
| 13 | Escalations correctly stopped | Acknowledged. |
| 15 | Criterion 19: publish size + CVE not run | **Still pending — main session** (orchestration instruction: the main session measures). This round adds no package and no `src/` change (tests and docs only). |
| 16 | Criterion 13: re-query after G2 | **Still pending — live gate G2** (§7). |
| 17 | Criterion 18: manual live gate | **Still pending — G4**, which depends on G3 (task 141 link coverage). Per owner round 4 item 1, ask the owner for test users at specific access levels rather than reusing the root-BU users. |

**Owner observations to raise in the PR (additions this round):**

6. High Priority returns at most 50 rows per id chunk with no ordering (probe: 60 flagged documents on one matter FOR the caller → 50 returned, no failure marker). PRE-EXISTING: the pre-task HP query had the same `TopCount = 50` per entity.
7. No client reads `topPriorityMatterUnavailable`; on a failed portfolio read the Workspace briefing shows zero metrics and "No specific top-priority matter identified" — fail-closed on data, silent to the reader.
8. Notifications for a person with more than 500 people-targeted matters cover a subset (item 8 above; pre-existing node paging).
9. Deploy order with task 146: round 3 B2 (1) accepts an interim to-do drop-out — announce it in the PR if 152 does not deploy with or before 146.

**Placement / justification (CLAUDE.md §10 / §11), this round:** no new service, DI registration, endpoint, option,
job, package, interface or Dataverse column; no `src/` file changed. Two test methods added to existing test classes;
documentation and playbook-source description strings only.

**Test runs this round (2026-10-02):**
- Affected classes (`RecordCreationAssignedInternalTests` + `MembershipOwnerKeyParityTests`): **14 / 0**; with seeds V5 and V6 applied: **4 failed / 10 passed**.
- Full BFF unit suite: **13,891 passed / 0 failed / 54 skipped (13,945)**, which is +4 against round 1.
- NetArchTest: **341 / 0 / 0**.
