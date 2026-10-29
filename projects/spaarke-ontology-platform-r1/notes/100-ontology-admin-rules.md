# Task 100 - Ontology admin slice (a): Rules read-only, On/Off, Retire

Branch `task/ontology-100` (from `origin/docs/ontology-platform-design` at `387e2d70e`). Rigor FULL (admin write routes that close live Signals, plus UI). Model Sonnet, effort high.

## Pre-start check (B.6) and uac-r2 re-read

- Reconciliation HANDOFF section 8 (L301) pre-start check done: Retire is a Dataverse write as the caller plus task 033's closure; no access rule is changed here.
- Re-read against `origin/master` = `2e4ffc65408989cfb58aac55834b1acbbfca4116`: `CoreAncestorResolver`, `RecordOwnershipResolver`, `SecureChildLineage.cs`, `RecordRouteAccessAuthorizationFilter` and `CallerRecordAccessProbe` are unchanged since the merge-base. The route-authorization ledger on master has three commits (re-annotation, one dead-route retire, #2021); no rule change. Nothing in this task depends on record-level access: the routes read and write `sprk_policy` / `sprk_policyversion` (tenant-level reference data) as the caller.

## What was built

- BFF: `Api/Ontology/OntologyAdminRulesEndpoints.cs` (GET /rules, GET /rules/{id}, PUT /rules/{id}/enabled, POST /rules/{id}/retire), wired from `OntologyAdminEndpoints.cs` by one line; `Services/Ontology/OntologyAdminRulesService.cs` and `OntologyAdminRulesModels.cs`; two registrations in `SignalsModule`.
- Every read and write runs as the caller through `IDataverseUserClient` (D-22). The capability answer is never consulted (rule A of `OntologyAdminCallerBoundaryGuardTests`, passing).
- Error mapping: `UserContextRequired` / `OboNotConfigured` / `OboExchangeFailed` -> the single 403 `sdap.access.deny.caller_unresolved`; `AccessDenied` -> 403 `ontology.admin.forbidden` (this is how a Console User's write is refused: by Dataverse); `NotFound` -> 404; anything else -> 503, never a shorter list.
- Off: PATCH `{"sprk_enabled": x}` only. Always issued, even when the value is unchanged, so Dataverse authorizes every call. A retired rule is 409.
- Retire: reason trimmed and required (400 before any I/O), at most 2000 characters. One PATCH `{"statecode":1,"statuscode":100000000,"sprk_retiredreason":...}` AS THE CALLER, and ONLY THEN `SignalClosureService.ClosePolicyRetiredNowAsync` (it does not check the policy is Retired, so order matters). Reads `outcome.IsComplete`; re-runs once only when incomplete solely because `ChangedSinceRead > 0`; a fault or a thrown closure is reported in the 200 body (`closure.unavailable`), the rule stays retired. Already retired is 409. No Decision Record is written (the fake Dataverse throws on any POST).
- List/page numbers (#44): open items, last fired (two aggregate FetchXML as the caller) and the action rate from `PolicyActionRateService` (task 071, reused). "Needs a look" = rate below 0.5 with at least 5 judged. The three are read independently (fix round 1): a number that cannot be read is null with its flag false in `metricsAvailability`, shown as "Could not be read" on its own; the other two still show and the rules are all listed. `metricsAvailable` is true only when all three were read. Last fired looks back 90 days (`createdon last-x-days 90`) and is labelled "in the last 90 days". Change history (#43) from version rows plus the policy row's `modifiedon` / `_modifiedby_value`.
- UI (`Spaarke.UI.Components/src/components/OntologyAdmin/Rules/`): service client (injected fetch, handles thrown `ApiError` and returned non-2xx), `RuleList` (All / Decide / Do / Needs a look), `RulePage` (read-only; only On/Off and Retire), `RetireRuleDialog` (SprkModal `sm`, `dismiss="alert"`), `RulesArea`. Passed to `OntologyAdminTab` as the `rules` slot from `OntologyAdminWorkspaceWidget` (`useAiSession()`); lane 103 adds `classification` beside it.

## Deviations and decisions

1. **Domain allow-list (lane brief).** ebilling's `RuleDomains` is not on the base. One private helper `OntologyOnly()` in the service: `(sprk_domain eq null or sprk_domain eq 100000001)`, comment `// Swap to RuleDomains (ebilling-r1 task 070, #1911) when it lands`. Applied to the list and to every by-id read (before toggle, before retire, before the closure). A Billing/Docketing rule is never listed and is the uniform 404 (identical body to a missing id) with zero writes and no closure. `sprk_domain` option values read live on spaarkedev1 (Billing 100000000, Ontology 100000001, Docketing 100000002); not in the exported solution source, so pinned by a constant test.
2. **POML step 4 (POML status, TASK-INDEX) not done by this lane**: the main session owns it (lane brief).
3. **`PolicyRetiredClosure` delegate (CLAUDE.md section 11).** Existing: `SignalClosureService.ClosePolicyRetiredNowAsync`. Extension: the service must call exactly that method; the delegate is a one-line registration of that method group, not a new abstraction. Cost of doing nothing: Retire's ordering (write first, closure second) and the re-run rule could not be proven without constructing the Signal writer (eight collaborators).
4. **Tests beyond the contract and authorization cases** (one-line justifications): the page-cap and metrics-unavailable cases pin the "never a shorter list" rule; the schema-agreement test pins the option-label tables to the solution source because the fake Dataverse echoes whatever the table says.
5. **Option labels are carried in the service** (lane, severity, subject type, work type), pinned to `SpaarkeMaster/Entities/sprk_Policy/Entity.xml` by `OntologyAdminRulesSchemaAgreementTests`.
6. **Kit choice.** Only `StatusBadge` from the task 057 kit. `EvidenceLine`, `StatusBar`, `RecordRow` and `AggregateCard` are Signal / Decision Record shapes and do not fit a rule.
7. **Route census.** Four routes classified (`GovernedFile` GroupGated plus four `HandlerDecision`s, seam `IDataverseUserClient`), `ExpectedEndpointFileCount` 126 -> 127. A rebase conflict on that line with tasks 101/103 is expected; the main session resolves it.
8. **Bug caught by the tests during the task**: the service read `_sprk_modifiedby_value` while the select asked for `_modifiedby_value`; fixed (the change-history "By" for the rule record was always empty).

## Known limits (not fixed; critical path only, D-106)

- Dataverse fails an aggregate FetchXML that has to scan more than 50,000 records (`AggregateQueryRecordLimit`); it does NOT return a partial or under-reported count (the first version of this note said "under-report", which was wrong). Open items scans only Open/Acknowledged Signals, the action rate is already windowed to 90 days, and last fired is now windowed to 90 days (it first scanned every Signal ever written, so it failed tenant-wide above 50,000 rows and, being all-or-nothing, blanked every rule's numbers; fixed in round 1). If a windowed or open-only scan ever exceeds the limit, that one number shows "Could not be read" and the others are unaffected.
- **Deep depth hides root-owned policies from child-BU admins (K1, out of scope, not fixed).** The tenant-level `sprk_policy` rows are read and written as the caller; an administrator in a child business unit whose role grants the privilege at a depth below Deep/Organization cannot see root-owned policies, so the list and the rule page would omit them (a 404, not an error). On spaarkedev1 the Ontology Administrator role has one member today (one user), so it does not bite yet; revisit when a child-BU administrator is introduced, and check the role's privilege depth then.
- The policy list is one page of 1000; a result of exactly that size is refused (503), not shown as the whole list.
- Counts are caller-scoped (D-123): a caller who cannot see some Signals sees smaller numbers.
- A Console User can READ policy rows (Dataverse Read); only writes are refused. The tab itself is offered only to administrators (task 104).
- The jest test for the dialog sets the reason with `fireEvent.change`: the Fluent dialog's focus trap drops focus in jsdom mid-typing (test-environment artefact).
- `Spaarke.AI.Widgets` has no ESLint 9 config (`eslint.config.js`) in the worktree, so only `tsc` and jest were run on the widget edit; `tsc` there reports the pre-existing TS2307 for `@spaarke/ui-components` (dist not built) on untouched files as well.

## Fix round 1 (review of `3774398df`)

- **Numbers (F1).** `OntologyAdminRulesService.ReadMetricsAsync` reads the three numbers independently (`TryReadAsync`), the last-fired aggregate carries a `createdon last-x-days 90` condition, the response gains `metricsAvailability` and `lastFiredWindowDays`, and each UI cell/fact shows its own "Could not be read". Tests: an `AggregateQueryRecordLimit` fault on each one number in turn still returns the other two (list and page); a tenant with more than 50,000 Signals in total but fewer in the window still reads last fired (the fake Dataverse applies the 50,000 limit to whatever the aggregate has to scan, so the unwindowed query fails it).
- **Real closure (F3).** `OntologyAdminRulesRealClosureTests` keeps the production `PolicyRetiredClosure` registration (`SignalsModule`) and substitutes only the Signal writer's `OntologyWriterDataverseClient` (over a fake organization service) and the shared `IGenericEntityService`, with one open Signal seeded. Retire closes it as PolicyRetired by a conditional write, with `sprk_decisionrecord`, `sprk_resolvedby` and `sprk_suppresseduntil` unset, and no Create reaches either client.
- **Unknown outcome (F1).** A write (On/Off, Retire) that ends in a 5xx or no answer says "The change may not have been saved. Reload to check." (`outcomeUnknown` on the failure); "Nothing was changed" is kept only for the server's 4xx refusals (403, 404) and for reads the phrase is dropped.
- **Concurrent Retire (K1).** Retire reads `versionnumber` with the policy and PATCHes with `PatchAsync(..., expectedVersion)` (If-Match). A 412 is a 409 `ontology.rule.changed_since_read` ("changed since you opened it"), no write, no closure. A policy read with no `versionnumber` writes nothing (503). `IsOptimisticConcurrencyEnabled` on `sprk_policy` was read live on spaarkedev1 on 2026-10-16: true (also `sprk_signal`, `sprk_servicerequest`). On/Off stays unconditional (it sets one boolean to a value the admin chose; last write wins is the intended behaviour).
- **Inactive, not retired (K4).** The page hides On/Off and Retire for any inactive rule (`state` Retired or Inactive), with a note; the 409 text for a switch on an inactive rule is "An inactive rule cannot be switched."
- **Accessibility (K4).** The list is a Fluent `Table` (native `table`/`tr`/`td`) instead of `<button role="row">` with `role="cell"` children. The rule code is a real `Link as="button"` (the DataGrid's primary-name pattern, `DataGrid.tsx` `primaryNameLink`), so a row opens from the keyboard and is announced as a row of cells; a click anywhere on the row opens it for the pointer.
- **Ledger (K4).** The four `HandlerDecision` line cites in `RouteAuthorizationGuardTests.Ledger.cs` were re-pointed to the current lines of `OntologyAdminRulesService.cs` and re-checked after the last edit to that file.

## Not verified live (no live writes in this lane)

The Retire PATCH writes `statecode` 1 and `statuscode` 100000000 together. Reads were verified against spaarkedev1 (policies with the domain filter, by id, versions, `systemusers`, `sprk_matters`, both aggregates). For the main session to run before relying on it (creates and deletes its own row; owner approval needed for live writes):

```
# 1. create a throwaway Ontology policy row (zz-100-): POST sprk_policies {"sprk_policycode":"zz-100-retire","sprk_name":"zz-100 retire probe","sprk_lane":100000001,"sprk_subjecttype":100000006,"sprk_severity":100000000,"sprk_domain":100000001}
# 2. PATCH sprk_policies(<id>) {"statecode":1,"statuscode":100000000,"sprk_retiredreason":"zz-100 probe"}   -> expect 204
# 3. GET sprk_policies(<id>)?$select=statecode,statuscode,sprk_retiredreason                                  -> 1, 100000000, "zz-100 probe"
# 4. DELETE sprk_policies(<id>)
```

Run each as a user holding the Spaarke Ontology Administrator role, then repeat the PATCH as a Console User and expect 403. Also repeat step 2 with a stale `If-Match: W/"<versionnumber - 1>"` and expect 412 (the BFF maps it to 409, no closure).

### Live gate (K2): who can write a policy - read-only, run before relying on "Dataverse refuses a Console User"

The 403 for a Console User holds only if no role a Console User (or any non-administrator human) holds carries `prvWritesprk_Policy`. Read-only GETs against `https://spaarkedev1.crm.dynamics.com/api/data/v9.2` (az CLI token), run as a script and ASSERT the last step is empty:

```
# 1. the privilege id
GET privileges?$select=privilegeid,name&$filter=name eq 'prvWritesprk_Policy'                       -> <privilegeid>
# 2. every role that holds it
GET roleprivilegescollection?$select=roleid&$filter=privilegeid eq <privilegeid>                      -> role ids
GET roles(<roleid>)?$select=name,_businessunitid_value                                                -> role names
# 3. for every role whose name is NOT "Spaarke Ontology Administrator" and NOT "System Administrator", who holds it
GET systemusers?$select=fullname,applicationid,isdisabled&$filter=systemuserroles_association/any(o:o/roleid eq <roleid>)
GET teams?$select=name,teamtype&$filter=teamroles_association/any(o:o/roleid eq <roleid>)
# ASSERT: every systemuser row has a non-null applicationid (an app identity, not a person) and the teams list is empty
#         (a team could carry the role to its human members). Any human row or any team is a finding: stop and report.
```

Result on spaarkedev1, 2026-10-16: the privilege is held by System Administrator, Spaarke Ontology Administrator, System Customizer and Service Writer. System Administrator is held by two enabled humans (Ralph Schroeder, Delegated Admin) and app identities; Spaarke Ontology Administrator by Ralph Schroeder. **System Customizer is held only by app identities on dev** (Microsoft Copilot Studio, github-actions-spe-infrastructure) and **Service Writer only by app identities** (Power Platform first-party applications and managed identities); neither role is assigned to any team. So no human other than a System Administrator or an Ontology Administrator can write a policy today. Re-run it whenever a role is assigned or a team is given a role; System Customizer is the one to watch (it is a standard role an admin might hand to a person).

## Re-check (2026-10-16, Sonnet, head `1dc3f082a`): merge, no F-class

Known limits (K4), not fixed:
- On/Off is unconditional (`OntologyAdminRulesService.cs:294`, `expectedVersion: null`): last write wins by intent. Against a racing Retire, Dataverse refuses the write on the inactive row or Retired still wins in display; closure unaffected. Optional: pass `versionnumber` as Retire does.
- Server Retire accepts an Inactive, non-retired rule (only checks `statuscode == Retired`, :330). Harmless: the nightly Off/Retired closure step already closes those Signals. Optional: `statecode != Active` → 409 NotActive.
- The 409 text says "changed since you opened it" but the version is read at request time, so only a read-to-write race is caught (:359).
- `rulesService.ts` `messageFor`: a 408/429 on a write reads "could not be reached" with `outcomeUnknown` false; server faults map to 503 so this is unlikely.
- After a 409 or unknown outcome the page behind the Retire dialog is not reloaded; the message tells the admin to reload.
- Earlier jest flake not reproduced in 7 runs; likely cause is the Retire dialog `findBy` calls on the default 1 s `asyncUtilTimeout` under a cold, CPU-starved first render. Hardening: `{ timeout: 5000 }` on those two calls.

## Out-of-scope observations

- `InboundBodyDtoMappingGuardTests` fails on the base for `OntologyAdminCapabilities.CanAuthorPolicies` (task 104); fixed on the docs branch at `58e810405`. Not fixed here.
