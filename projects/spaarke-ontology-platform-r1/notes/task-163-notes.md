# Task 163 notes: the worklist reads policy columns as the BFF (D-185, #2285)

Branch `task/163-policy-server-read`, PR into `docs/ontology-platform-design`. Rigor FULL, security-tagged. Live roles and the grid configuration row on dev are NOT changed by this PR.

## What changed

| File | Change |
|---|---|
| `Services/Signals/SignalReadService.cs` | `SignalRow` gains `PolicyId` (from `_sprk_policy_value`). New step (11) `ApplyPolicyColumnsAsync`: one app-only `sprk_policy` read (`sprk_policyid In (distinct ids of the returned rows)`, chunked at `IdChunk` = 100) for the short name, work type and severity, applied just before `Shape`. The `Aliased(...)` parse stays, so rows from the old configuration still parse. |
| `config/worklist.gridconfiguration.json` | The `policy` link-entity is removed from all five fetches; `sprk_policy` stays selected on the Signal. |
| `Roles/Spaarke Basic User.xml` | Drops `prvReadsprk_Policy`. |
| `Roles/Spaarke Console User.xml` | Drops `prvReadsprk_Policy` and `prvReadsprk_PolicyVersion`. |
| Tests | `SignalReadRouteTests` (7 new, fake extended), `WorklistGridConfigurationTests` (guard flipped: no policy link allowed), ledger sentence in `RouteAuthorizationGuardTests.Ledger.cs`. |
| `OntologyAdminAuthoringService.cs` | One comment corrected (it said Console User reads `sprk_policy`; true only until D-185). No logic change. |

## Behaviour decisions

- **Either deploy order works.** The BFF reads the three columns whether or not the alias is present and, when it finds the policy, it is authoritative (it overwrites an alias value, which is identical anyway). A new BFF with the old config, an old BFF with the new config is NOT supported (the old BFF reads `policy.*` only, so it would show no names and rank everything last): deploy the BFF first, then apply the config, then change the live roles.
- **Premise pin for the order:** `OldConfiguration_WithNoPolicyReadForTheCaller_IsTheEmptyListToday...` shows that with the old configuration and no caller Read on `sprk_policy`, Dataverse refuses the Signal query and the route treats it as "no read on `sprk_signal`" (empty list, Warning). So the live roles must lose the Reads only AFTER the new configuration is applied on that environment.
- **Visible rows only.** The policy read runs after every caller-scoped filter, on the rows that will be returned. A policy named only by rows the caller cannot see is never read (`ThePolicyIsReadOnlyForTheRowsTheCallerCanSee`). Nothing beyond the three columns the worklist already shows is returned.
- **Fault = unavailable.** The read throws into the existing catch in `ReadAsync` (reason `fault`, 503 `ontology.signal.worklist_unavailable`), like the record-type catalog read. Never rows with blank severity (`APolicyReadFault_Is503_NeverRowsWithBlankSeverity`).
- **A row whose policy no longer exists** (the Signal's `sprk_policy` points at a deleted row, or is empty) keeps today's missing-alias behaviour: no rule short name, no work type, severity unknown (rank 3, last). Pinned by `APolicyThatNoLongerExists_...`. An inactive or retired policy still resolves (no statecode filter), as the outer link did.
- **Placement (CLAUDE.md section 10):** in the BFF, inside the existing read service. No new service, endpoint, DI registration, package or background work.
- **Component justification (section 11):** Existing: `DecisionPlanService` and the catalog/configuration reads already read configuration as the BFF. Extension: one more batched read in `SignalReadService`. Cost of doing nothing: every internal user keeps direct Read on billing policies (#2285), and removing the Read without this empties the worklist.

## Caller-run reads of `sprk_policy` and `sprk_policyversion` (escalation grep)

Searched: all of `src/server` (BFF, `Spaarke.Core`, `Spaarke.Dataverse`), `src/solutions/SpaarkeAi`, `src/client` (including `Spaarke.UI.Components`, `Spaarke.AI.Widgets`, PCF) and `src/dataverse` (non-role files).

| # | Reader | How | Verdict |
|---|---|---|---|
| 1 | Worklist FetchXML `policy` link-entity (`config/worklist.gridconfiguration.json`, run by `SignalReadService` as the caller) | outer link on `sprk_policy` | REMOVED by this task. Replaced by the BFF read. |
| 2 | `OntologyAdminAuthoringService` (catalog, dry-run, validate, save, publish: `sprk_policies`, `sprk_policyversions` via `IDataverseUserClient`) | caller | admin-only. Every route is refused 403 unless the caller holds `prvCreatesprk_PolicyVersion`; the Console tab is registered only on that answer. Ontology Administrator keeps both Reads. |
| 3 | `OntologyAdminRulesService` (rule list, rule page, On/Off, Retire: `sprk_policies`, `sprk_policyversions` via `IDataverseUserClient`) | caller | admin-only by design (the tab is hidden from non-admins; writes are refused by Dataverse). The two GET routes carry no BFF capability gate, so a Console User who calls them directly now gets a clean 403 Forbidden (Dataverse refuses the read: `DataverseUserClient.cs:312` maps 401/403 to AccessDenied, `OntologyAdminRulesService.cs:492` maps that to Forbidden, `OntologyAdminRulesEndpoints.cs:125` returns 403) instead of the Ontology rules. That is the intended effect of #2285; the only Console caller is the admin-gated Rules page. |
| 4 | `OntologyAdminClassificationService` (policies and versions that read a triage category; read-coverage check of the caller's `prvReadsprk_Policy` / `prvReadsprk_PolicyVersion`) | caller | admin-only (Ontology admin Classification area). A caller without the Reads gets the route's existing refusal or unverifiable answer; not changed here. |
| - | Console code (`src/solutions/SpaarkeAi/**`, `Spaarke.UI.Components`, `Spaarke.AI.Widgets`, PCFs) | none | No `Xrm.WebApi` or FetchXML read of either table. Only comments and the `workType` field of the BFF's worklist response mention `sprk_policy`. |
| - | Evaluator, closure, suppression, decision commit, re-evaluation job, `PolicyScopeResolver`, `DecisionPlanService`, `DecisionCommitFollowUps` | BFF (`IGenericEntityService`) | not caller reads. Unaffected. |

No non-admin surface reads either table as the caller other than the worklist join, so the POML escalation trigger did not fire. uac-r2 (2026-10-23) independently confirmed no UAC surface or guard reads these tables as the caller.

## Role-pinning and role-scan tests

- No test asserts that `Spaarke Basic User` or `Spaarke Console User` holds `prvReadsprk_Policy` / `prvReadsprk_PolicyVersion`. `OntologyAdminAuthoringEndpointTests` (:940) and `OntologyAdminClassificationEndpointTests` (:1255) fake the privilege lookup of a caller; they do not read the role XML, so they did not change.
- `RouteAuthorizationGuardTests.Ledger.cs`: the worklist `HandlerDecision` text (the ~:1903 entry; the ledger line cited in the POML as ~:2008 has moved) gained one sentence naming the BFF policy read. The guard passed unchanged otherwise.
- Role-XML scan tests on this branch: `SpaarkeRoleAuditReadGuardTests` and `SpaarkeRoleLedgerPrivilegeGuardTests` (they check `sprk_signal` / audit privileges only); both pass. **ebilling 081's role-XML scan test is not on this branch** (`git grep` finds none), so ebilling should re-run it after merging this PR: the two role files lose exactly the three privileges above, nothing else changes.
- The seam test `WorklistGridConfigurationLiveTests` still reads `sprk_policy` column metadata; it does not depend on the link.

## Evidence

Failing before (the old `SignalReadService` against the new fake and the new config): 6 of the 8 new D-185 route tests fail (the two that pass are the must-not-fire old-configuration tests). Against the old config row, `WorklistGridConfigurationTests` fails 2 (`TheShippedDefinition_HasNoProblems`, `EveryQuery_..._WithNoPolicyLink`).

Passing after: SignalReadRouteTests + Signals/Ontology (integration project, filtered) 93 passed / 3 skipped (live legs) / 0 failed; `Sprk.Bff.Api.Tests` filtered Signals|Ontology|Worklist 2519 passed / 2 skipped (live) / 0 failed; `WorklistGridConfigurationTests` 18 passed; `Spaarke.ArchTests` full 1093 passed / 0 failed. BFF build 0 warnings, 0 errors. `dotnet list package --vulnerable --include-transitive`: none.

Publish size (Release, framework-dependent linux-x64, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included, 4 PDBs): origin/docs/ontology-platform-design `5911710acd` 39,155,527 bytes (37.342 MB), 192 files; this branch `accfc2d2f7` 39,158,991 bytes (37.345 MB), 192 files. Delta +3,464 bytes (+0.003 MB). Both from short-path worktrees (`C:\w163b`, `C:\w163n`), removed afterwards with `Remove-WorktreeSafe.ps1`. The branch is cut at `0a8bf12d8c`; the commits between it and the tip changed client code only.

## Fix round 1 (review pass 1; no F-class defect)

**A. Deploy order: how the worklist row reaches customer environments.** It does not, automatically. `scripts/Deploy-Release.ps1` has no step that applies configuration rows (Phase 2 BFF at :574, Phase 3 SpaarkeMaster import, Phase 4 web resources, Phase 5 validation). The provisioning seeder H12b (`DataverseWebApiDataGridSeeder.cs:87-105`) seeds only the four reconciliation grids (ids 5001-5003 and a name-only row), from embedded resources; the worklist row (id 5010) is in no seeder, workflow or release script. The only applier is `scripts/Apply-WorklistGridConfiguration.ps1`, run by hand. So it is a release-time manual step: (1) BFF, (2) apply the config, (3) roles. Recorded in the script `.DESCRIPTION` and in `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` section 7.8 ("Ordering requirement (D-185)"). The doc comment at `SignalReadService.cs` ApplyPolicyColumnsAsync is corrected: the new BFF works with either config; an old BFF with the new config loses names and severity; roles go last. The "either deploy order works" bullet above means new BFF with either config only.

**B.** Section 3 row 3 corrected (403, not empty list or 404); PR body corrected. `security-roles.md` section 19 says "two role files".

**D. Drift.** Until the live role change, `check-dev-drift.py` and `Export-SpaarkeMasterSource.ps1` see dev holding the Reads the repo dropped, and a fresh export would put them back. The dev role change must follow the config apply promptly; nobody exports the roles from dev before it (also in `security-roles.md` section 19).

**E. Known limit K4.** The app-only read shows the three columns for any Signal the caller can read, including a policy outside the caller's business-unit reach that the old caller-run link blanked. Intended under D-185. The lookup's formatted value already exposes the policy name; the rule body, version and code are not exposed.

**C. Fault on the second chunk.** `AFaultOnTheSecondPolicyChunk_Is503_NoRowsReturned` (150 visible Signals, 150 distinct policies = reads of 100 + 50; the fake throws on the second `sprk_policy` read only; expects 503 `ontology.signal.worklist_unavailable` and no `lanes`). It passes on the unchanged code (the reviewer was right). Proof it can fail: with the chunk loop's `RetrieveMultipleAsync` wrapped in `try { ... } catch (TimeoutException) { continue; }`, both it and `APolicyReadFault_Is503_NeverRowsWithBlankSeverity` FAILED (2 failed); restored, both pass.

**F. External or unknown callers are not enriched (uac-r2 condition for D-185).** The worklist resolves the caller as a systemuser, and a B2B guest flagged `sprk_isexternal` is one; such a guest holds Spaarke Basic User by default (`H11UserProvisioningOptions.cs:51`; there is no separate External Collaborator role in the repo), and the caller-run link returned only what their own read returned. The app-only read would have given them more than that. The app-only read would have undone that. Now `ApplyPolicyColumnsAsync`, once per read and only when a returned row names a policy, calls `SignalReadService.IsExternalOrUnknownCallerAsync` (mirrors master's `InternalPlaneDocumentNarrowing.IsExternalOrUnknownCallerAsync`, lines 202-230; `// Keep in step with ...` comment; that file is NOT brought over, so no Api/Filters dependency in Services): `ICallerSystemUserResolver.ResolveAsync(user)` not resolved or non-GUID id = external; `ISystemUserIdentityResolver.IsExternalAsync(id)` true = external; `OperationCanceledException` with the token cancelled is rethrown; any other fault = external (SUPERSEDED in fix round 2: a fault is now a 503). External = no BFF enrichment and no `sprk_policy` query: external or unknown callers get only what their own read returned (rows keep blank name and work type, unknown severity, ranked last). Wiring: `SignalReadService` takes the two resolvers as constructor dependencies and `ReadAsync(set, ClaimsPrincipal? user, ct)` takes the caller's principal from `httpContext.User` (the endpoint is its only caller). The method is `internal static` (not private) so the cancellation leg can be unit-tested; it is still a member of `SignalReadService`.
- Section 11: Existing - `ICallerSystemUserResolver` and `ISystemUserIdentityResolver`, both already registered in DI (`AnalysisServicesModule.cs:995`, `NotificationsModule.cs:33`); no new registration, type or package. Extension - reuse of those two as they are, no new service. Cost of doing nothing - an external B2B guest reads the policy short name, work type and severity through the BFF, a D-185 bypass.
- Tests (`SignalReadRouteTests`): must-fire `AnExternalCaller_GetsNoPolicyColumns_AndNoPolicyQueryIsIssued`; must-not-fire `AnInternalCaller_IsEnrichedAsBefore_WithOnePolicyQuery`; `AnUnresolvedOrNonGuidCaller_IsNotEnriched`; `AResolverFault_IsNotEnriched_ButTheRowsAreStillReturned` (caller resolver throws, and external-flag read throws: 200, no policy read); `TheExternalRule_RethrowsCancellation_AndTreatsAnyOtherFaultAsExternal`. With the guard disabled (`if (ids.Length < 0)`), 3 of the 4 route tests FAILED (external, unresolved/non-GUID, resolver fault); the internal caller test passed as it must. Restored: all pass.


**Publish size re-measured after fix round 1** (same method; `C:\w163b` = origin/docs/ontology-platform-design `f949b7267c`, `C:\w163n` = this branch `08a45136c8` merged with it, merge clean, both removed with `Remove-WorktreeSafe.ps1`): 39,161,521 -> 39,165,793 bytes, delta +4,272 bytes (+0.004 MB), 192 files each. No new package. Final test counts: SignalRead integration 97 passed / 2 skipped (live) / 0 failed; unit filtered 124 passed; `Spaarke.ArchTests` 1093 passed / 0 failed; BFF build 0 warnings, 0 errors.

## Fix round 2 (adversarial pass 2)

**F2. A resolver fault is a 503, not a silently blank worklist.** `CallerSystemUserResolver` turns a Dataverse fault into `Unresolved("lookup-failed")` (`CallerSystemUserResolver.cs:143`). Round 1 treated that, and any exception, as external, so an internal user got 200 with every rule name, work type and severity blank (ranking every row last), against the file's own rule that rows are never returned without severity. Now `IsExternalOrUnknownCallerAsync` (a) throws on `UnresolvedReason == "lookup-failed"` (`SignalReadService.CallerLookupFailedReason`), (b) lets any exception from either resolver propagate (the generic catch is removed), (c) still rethrows cancellation. All of them reach `ReadAsync`'s outer catch: 503 `ontology.signal.worklist_unavailable`. uac-r2 accepted 503 as an option.

Unresolved reasons the resolver can return (read from `CallerSystemUserResolver.cs`) and their treatment:

| Case | Source | Treatment |
|---|---|---|
| `no-claims-principal` | `:102`, principal null | external/unknown: 200, not enriched |
| `no-oid-claim` | `:112`, no usable oid | external/unknown: 200, not enriched |
| `no-matching-systemuser` | `:126`, no active systemuser for the oid | external/unknown: 200, not enriched |
| `resolver-not-registered` | `:161`, the ADR-032 null-object default | external/unknown: 200, not enriched (a registration default, not a Dataverse fault) |
| `lookup-failed` | `:143`, Dataverse fault | FAULT: 503 |
| resolved, id not a GUID | caller side | external/unknown: 200, not enriched |
| `IsExternalAsync == true` | `ISystemUserIdentityResolver` | external: 200, not enriched |
| either resolver throws | | FAULT: 503 |

**Deliberate deviation from master's rule (d)** (`InternalPlaneDocumentNarrowing.IsExternalOrUnknownCallerAsync`: any fault = external, fail closed). There a fault only narrows the documents returned. Here a fault would change the ranking and labelling of EVERY row for an internal user while looking like success, so the fail-closed answer is unavailability, not a degraded 200. Externality itself (unresolved / non-GUID / `IsExternalAsync`) is still kept in step with master; the comment on the method names the deviation.

Tests (`SignalReadRouteTests`): `AResolverFault_IsNotEnriched_ButTheRowsAreStillReturned` replaced by `AResolverFault_Is503_NotARowListWithBlankSeverity` (caller resolver throws; external-flag read throws; resolver returns `lookup-failed`: all 503, no `lanes`, no policy read). `AnUnresolvedOrNonGuidCaller_IsNotEnriched_ButTheRowsAreStillReturned` now covers non-GUID plus the four no-answer reasons (200, not enriched, no policy query). `TheExternalRule_RethrowsCancellation_AndAnyOtherFault_ButNotAnAnswer` is the unit leg (cancellation and `TimeoutException` propagate; `lookup-failed` throws; the other reasons answer true). The external and internal route tests are unchanged.

**Proof (before/after).** With the pre-fix `SignalReadService.cs` (plus only the `CallerLookupFailedReason` constant so the tests compile), `SignalReadRouteTests` ran 97: 2 FAILED (`AResolverFault_Is503_NotARowListWithBlankSeverity`, `TheExternalRule_RethrowsCancellation_AndAnyOtherFault_ButNotAnAnswer`), 95 passed. With the fix restored all pass (counts under Verify below).

**F1 (docs).** The D-185 ordering paragraph in `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` (7.8) and the `.DESCRIPTION` of `scripts/Apply-WorklistGridConfiguration.ps1` now say: (a) upgrades: BFF, config (`-Verify -RequireRow`), roles; `Deploy-Release.ps1` does it once the release-config phase ships (task 164, PR #2299), until then `-SkipPhase Solutions`, the config script, then the Solutions phase; (b) fresh environments: the order does not matter, apply the row after the BFF is provisioned (H9); (c) on dev the role change is `RemovePrivilegesRole` (security-roles.md section 19), an unmanaged import does not remove privileges. The guide change is confined to that one paragraph to avoid a conflict with #2299.

**K1.** The comment at `ApplyPolicyColumnsAsync` and section F above are reworded: no BFF enrichment for external or unknown callers; they get only what their own read returned. Guests hold Spaarke Basic User by default; no External Collaborator role exists in the repo.

**K3.** Line references in the `RouteAuthorizationGuardTests.Ledger.cs` worklist entry refreshed to `SignalReadService.cs:310` (ResolveMineCallerAsync call), `:1035` (WhoAmI), `:333` (worklist FetchXML as the caller), `:900` (core batched read), `:954-955` (Do-lane subject read). CRLF and no-BOM preserved byte for byte; `git diff --stat` shows 4 lines changed.

**K2. Self-contained issue text (the main session files it).**

> **Title:** One shared externality rule for the Services layer (or a source-scan guard keeping three copies in step)
>
> **Problem.** The rule "is the caller external or unknown?" (resolve the caller's systemuser via `ICallerSystemUserResolver`; unresolved or non-GUID id = external; `ISystemUserIdentityResolver.IsExternalAsync(id)` true = external) exists in three copies: `InternalPlaneDocumentNarrowing.cs:202` (Api/Filters), `RetrievalAccessTrim.cs:321`, and `SignalReadService.cs:800` (`IsExternalOrUnknownCallerAsync`, from task 163 / D-185). They differ on one point by design (master's helper treats a Dataverse fault as external; the worklist treats it as a 503), but the externality test itself must not drift: a change to what counts as external (a new unresolved reason, a new flag) would have to be made three times and nothing fails if one is missed. Drift means either a policy-read bypass for external guests or a blanked worklist for internal users.
>
> **Existing:** the three methods above. **Extension:** a Services-layer helper (for example `CallerExternality.ClassifyAsync` returning `Internal | External | Unknown | Fault`) that each caller maps to its own fail-closed behaviour, so Api/Filters and Services share one definition without Services depending on Api. **Cost of doing nothing:** silent divergence of a security-relevant rule across the document-narrowing, retrieval-trim and worklist paths.
>
> **Acceptance:** either (1) the three call sites use the shared helper and keep their own fault mapping, with tests for each mapping, or (2) a source-scan ArchTest asserts the three bodies make the same resolver calls and the same `IsResolved` / `Guid.TryParse` / `IsExternalAsync` sequence, with must-fire and must-not-fire controls. Include the `lookup-failed` reason in the helper's contract.

## Next (main session)

1. Review and merge, deploy the BFF to dev.
2. Apply `config/worklist.gridconfiguration.json` to dev (`scripts/Apply-WorklistGridConfiguration.ps1`) and run the worklist as a Basic + Core user.
3. Only then remove `prvReadsprk_Policy` from the live Spaarke Basic User and `prvReadsprk_Policy` + `prvReadsprk_PolicyVersion` from the live Spaarke Console User (owner standing authority, D-185). Read back the role privilege counts. Other environments follow the same order.
4. Re-run `SignalReadRouteLiveTests` as the non-Console user.

rework: none task 163
