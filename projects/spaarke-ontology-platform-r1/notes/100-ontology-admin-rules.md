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
- List/page numbers (#44): open items, last fired (two aggregate FetchXML as the caller) and the action rate from `PolicyActionRateService` (task 071, reused). "Needs a look" = rate below 0.5 with at least 5 judged. Numbers that cannot be read degrade to `metricsAvailable: false`; the rules are still all listed. Change history (#43) from version rows plus the policy row's `modifiedon` / `_modifiedby_value`.
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

- The two aggregate FetchXML queries are capped by Dataverse at 50,000 rows; a rule with more open Signals than that would under-report its open items.
- The policy list is one page of 1000; a result of exactly that size is refused (503), not shown as the whole list.
- Counts are caller-scoped (D-123): a caller who cannot see some Signals sees smaller numbers.
- A Console User can READ policy rows (Dataverse Read); only writes are refused. The tab itself is offered only to administrators (task 104).
- The jest test for the dialog sets the reason with `fireEvent.change`: the Fluent dialog's focus trap drops focus in jsdom mid-typing (test-environment artefact).
- `Spaarke.AI.Widgets` has no ESLint 9 config (`eslint.config.js`) in the worktree, so only `tsc` and jest were run on the widget edit; `tsc` there reports the pre-existing TS2307 for `@spaarke/ui-components` (dist not built) on untouched files as well.

## Not verified live (no live writes in this lane)

The Retire PATCH writes `statecode` 1 and `statuscode` 100000000 together. Reads were verified against spaarkedev1 (policies with the domain filter, by id, versions, `systemusers`, `sprk_matters`, both aggregates). For the main session to run before relying on it (creates and deletes its own row; owner approval needed for live writes):

```
# 1. create a throwaway Ontology policy row (zz-100-): POST sprk_policies {"sprk_policycode":"zz-100-retire","sprk_name":"zz-100 retire probe","sprk_lane":100000001,"sprk_subjecttype":100000006,"sprk_severity":100000000,"sprk_domain":100000001}
# 2. PATCH sprk_policies(<id>) {"statecode":1,"statuscode":100000000,"sprk_retiredreason":"zz-100 probe"}   -> expect 204
# 3. GET sprk_policies(<id>)?$select=statecode,statuscode,sprk_retiredreason                                  -> 1, 100000000, "zz-100 probe"
# 4. DELETE sprk_policies(<id>)
```

Run each as a user holding the Spaarke Ontology Administrator role, then repeat the PATCH as a Console User and expect 403.

## Out-of-scope observations

- `InboundBodyDtoMappingGuardTests` fails on the base for `OntologyAdminCapabilities.CanAuthorPolicies` (task 104); fixed on the docs branch at `58e810405`. Not fixed here.
