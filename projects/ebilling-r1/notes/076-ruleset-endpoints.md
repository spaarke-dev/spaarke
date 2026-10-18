# Task 076 - Rule set endpoints: proposals, edit, publish, visibility (FR-28)

Rigor: FULL. Model: Sonnet 5.5 @ high. Base: `43451b9b2`.

## 1. What shipped

Nine routes under `/api/finance/rulesets`, all `RequireAuthorization()`, each with its OWN `AddFinanceAuthorizationFilter(BillingAdminAuthorization.X())` then `.RequireEbillingEnabled()` (the flag never replaces authorization; ADR-008, ADR-018).

| Route | Billing-admin check (caller's own Dataverse privilege, D-76, fail closed) | Result |
|---|---|---|
| `POST /?name&sourceKind&scope&firmId` (multipart `file`) | Create + Write `sprk_ruleset` | 201 Draft; 400 `ruleset.invalid`; 409 owner reason; 413 `ruleset.file_too_large`; 500 `ruleset.write_failed`; 503 `ruleset.container_not_configured` |
| `GET /` | Write `sprk_ruleset` | 200 summaries |
| `GET /{id}` | Write `sprk_ruleset` | 200 detail (rules beside clause quotes); 404 `ruleset.not_found` |
| `PATCH /{id}/proposals/{policyId}` | Write `sprk_ruleset`, Create + Write `sprk_PolicyVersion` | 200 `{proposal, createdNewVersion}`; 400; 404 `ruleset.proposal_not_found`; 409 `ruleset.retired` |
| `POST /{id}/proposals/{policyId}/accept` and `/reject` | Write `sprk_ruleset`, Write `sprk_PolicyVersion` | 200 proposal; accept validates (400 with errors); 409 `ruleset.proposal_in_force` |
| `POST /{id}/publish` body `{effectiveDate}` | Write `sprk_ruleset`, `sprk_PolicyVersion`, `sprk_Policy` | 200 with `clauseQuotesVisibleToFirms` + `clauseVisibilityNotice`; 400 `ruleset.publish_invalid` + per-rule `errors`; 400 `ruleset.nothing_to_publish`; 409; 422 `ruleset.result_truncated` |
| `PUT /{id}/assignment` body `{scope, firmId}` | Write `sprk_ruleset` | 200 summary; 400; 409 retired |
| `PUT /{id}/visibility` body `{visibleToFirms}` | Write `sprk_ruleset` | 200 summary; 409 retired |

400 shape: `errorCode` plus `errors:[{policyId, rule, field, message, reason}]`. 403 carries `reasonCode` `sdap.access.deny.insufficient_privilege`; 503 `ebilling.disabled` with the flag off.

Files: `Api/Finance/RuleSetEndpoints.cs`, `Api/Finance/BillingAdminAuthorization.cs` (the reusable admin helper - 075 calls it), `Services/Finance/RuleSetService.cs` (concrete, `AddScoped` in `FinanceModule`, no interface, ADR-010), `Services/Signals/RuleDomain.cs` (two members added to `RuleDomains`: `AddBillingFilter`, `IsBillingRow`). Shared append points: `FinanceModule.cs` (one line), `EndpointMappingExtensions.cs` (one line).

## 2. Decisions (read before 075, 077, 130, 134, 135)

1. **"Billing admin" = Write on `sprk_ruleset`** (every route), plus the privilege of every table the route writes. It cannot be expressed by `sprk_Policy` / `sprk_PolicyVersion` alone: the Ontology Administrator role holds every privilege on those shared tables and would become a billing admin (over-grant); and Core / Basic User hold Read on `sprk_ruleset`, so Read would expose rule bodies and clause text to every internal user. **No default solution role carries Create / Write on `sprk_ruleset`**, so today only System Administrator (or a custom role someone assigns) is a billing admin. This is the intended "named people" behaviour but it is a provisioning fact to tell the owner; no role was changed (read-only schema check only). Tested: an Ontology-administrator-like caller (all policy privileges, no rule set Write) gets 403 on every route.
2. **Schema was sufficient** (no escalation): 019 carries `sprk_proposalstatus` (Proposed / Accepted / Rejected) on the version, `sprk_consequence`, `sprk_clausereference` + `sprk_clauselocation`, the rule set status / scope / firm / `sprk_visibletofirms` / `sprk_notconverted`. The predecessor link is the sibling version with the next-lower version number (no extra column).
3. **Version in force = the policy's `sprk_currentversion`.** A working version is a Draft when it is not the pointer's target and has no `sprk_inforceto`. A Draft version is edited in place; a version that is or was in force is NEVER updated (an edit creates version N+1, owner team copied, state Proposed). Publish writes versions (new `sprk_inforcefrom`, predecessor `sprk_inforceto`) then policies (pointer move + enabled) then the rule set (Published, effective-from), so the set is not Published until the last write and a second publish completes a partial one. Each write is chunked at 1,000 per transaction.
4. **Validation** is `PolicyVersionValidator.ValidateForSave(ruleType, body, template, RuleDomain.Billing)` at accept, edit and publish; `ValidateForAuthoring` is not used. Publish validates every candidate (accepted, not yet in force) BEFORE the first write; one failure refuses the whole publish with per-rule errors. An Ontology rule type on a billing policy is refused as `rule_type_wrong_domain`.
5. **Domain isolation, two layers.** Every `sprk_policy` and `sprk_ruleset` query carries a top-level `sprk_domain = Billing` condition (`RuleDomains.AddBillingFilter`) AND each returned row is re-checked (`RuleDomains.IsBillingRow`, missing or other value = not billing, fail closed). A billing policy that points at a billing rule set is the only thing ever listed or changed; an Ontology (or empty-domain, or Docketing) policy is a 404 here and is never written. The service never writes `sprk_domain` on a policy; it writes Billing only on the rule set it creates. The other direction is asserted too: the Ontology allow-list refuses every row this service touches.
6. **Clause visibility - one decision.** `RuleSetClauseVisibility.ForFirm(visibleFlag, quote, location)` (pure) and `RuleSetService.GetFirmClauseAsync(policyVersionId, invoiceFirmId)` (reads the flag, fails closed: no text unless the version is or was in force, its policy is billing, the rule set is Published, and the rule set is the client default or assigned to `invoiceFirmId` - FR-49; review round 1 added the firm, section 7; round 2 named it and fixed its caller contract, section 8 item 5). Flag off or unknown = `FirmClauseText.None` (no quote, no section reference). The result type has two members and no document link. 130 / 134 / 135 call `GetFirmClauseAsync`; the Procedure Library document is never returned to firms (D-66, D-79). Turning the flag off takes effect on the next call.
7. **Flag default per source kind:** Guideline and RateCard start on; InternalProcedure starts off. The publish response states the outcome: "Clause quotes will be visible to firms." / "Clause quotes will not be visible to firms.".
8. **Create stores the source document** in the Procedure Library through `SpeFileStore.UploadSmallAsync` (drive from `FinanceOptions.ProcedureLibraryContainerId`, never from the request), document row created unlinked (DEF-015, as 078), owner team through `IRecordOwnershipResolver`. Any later failure, including a client cancel, deletes the created rows and the file.
9. **Metadata in the query string** (name, sourceKind, scope, firmId), file in multipart - as 078.
10. **Guards touched** (additive entries): `RouteAuthorizationGuardTests.Ledger.cs` (new GovernedFile `Api/Finance/RuleSetEndpoints.cs`, `ExpectedEndpointFileCount` 136 -> 137 with history line; ADR-010 ceiling 166 untouched), `SpeWriteSinkContainerProvenanceGuardTests.cs` (two `ServerDerivedConfig` entries for `UploadSmallAsync` and the compensating `DeleteFileAsync`, same as 078's).
11. **Not touched:** 074's `DecisionActionCatalog` / `DecisionRecordWriter`, task 075's drafting Action.

### Row shape for 075 (the drafting Action must write rows this service can read)

A proposed rule = one `sprk_policy` (domain Billing = 100000000, `sprk_ruleset` lookup to the Draft set, name, policycode, lane, priority, subjecttype, `sprk_enabled` false) with one `sprk_policyversion` (version number 1, rule type + body that pass `ValidateForSave(..., Billing)`, message template, short headline, `sprk_proposalstatus` Proposed, `sprk_consequence`, `sprk_clausereference` = the verbatim quote, `sprk_clauselocation` = section / page, `sprk_inforcefrom`). The policy's `sprk_currentversion` stays EMPTY until publish. A policy with an unset domain is invisible to this service.

## 3. Section 11 - component justification

| New surface | Existing overlap | Why not extend | Cost of doing nothing |
|---|---|---|---|
| `RuleSetEndpoints` (9 routes) | `OntologyAdminAuthoringService` / its endpoints edit the same policy tables for the Ontology platform | Those are Ontology-scoped (domain allow-list, Ontology Administrator role, Ontology rule types) and `ValidateForAuthoring`; reusing them would over-grant billing admin to Ontology admins and let one domain edit the other's rows | FR-28 has no way to review, edit, publish, assign or hide a billing rule set; no billing rule can ever be in force |
| `RuleSetService` | `RateCardImportService` (078) is the pattern; no rule-set service exists | Different tables and lifecycle (versioned, immutable, all-or-nothing publish) | The same |
| `BillingAdminAuthorization` | `RateCardImportEndpoints` declares its privileges inline | 076 and 075 (and later admin routes) share one definition of "billing admin"; two copies drift | 075 re-derives the admin check and the two disagree about who may draft vs publish |
| `RuleDomains.AddBillingFilter` / `IsBillingRow` | `AddOntologyPolicyAllowlist` / `IsOntologyPolicy` (the Ontology side) | The billing side was missing; same file and idiom | Billing reads rely on one hand-written condition per query; a lost filter shows an Ontology policy to billing admins and lets billing edit it |
| `GetFirmClauseAsync` / `ForFirm` | none | One place decides what a firm may see of a clause | Each of 130 / 134 / 135 re-implements the flag check; one forgets and leaks clause text of a flag-off set |
| Two SPE sink entries | 078's two entries | A guard entry is per file/site | The ArchTest (task 083) fails the build |

## 4. Verification

- Build: `dotnet build src/server/api/Sprk.Bff.Api/ -m:1` 0 warnings, 0 errors.
- Tests (this task): `RuleSetServiceTests` 60 + `RuleSetEndpointsTests` 78 = **138 pass / 0 fail**. Filter `RuleSet|Billing|PolicyVersion|Finance|Ontology` on `tests/unit/Sprk.Bff.Api.Tests`: **1887 pass / 0 fail / 4 skipped (pre-existing)**. `tests/Spaarke.ArchTests`: **1126 pass / 0 fail** (route ledger, ADR-010, SPE write-sink provenance).
- Domain isolation, both directions: `EveryPolicyAndRuleSetQuery_CarriesATopLevelBillingDomainCondition` (all eight operations plus the firm-clause read), `EvenWhenAQueryLosesItsDomainFilter_AnOntologyPolicyIsNeverActedOn_ByAnyOperation` (a harness flag drops the filter; null-domain and Ontology policies are 404 on edit / accept / reject, absent from get, no text from the clause helper, zero writes), `Get_ListsOnlyBillingPolicies...`, `List_ReturnsOnlyBillingRuleSets...`, `Get_ARuleSetOfAnotherDomain_IsNotFound` (Ontology, empty, Docketing), `Publish_OfARetiredRuleSet_Is409_AndAnUnknownOrForeignDomainOneIs404`, `BillingOperations_NeverWriteTheDomainColumn_AndTheirRowsAreInvisibleToTheOntologyReaders`.
- Mutation proofs (source mutated, 135-139 tests rerun, each mutant killed, source restored with `git checkout`): billing filter no-op (1 fail), `IsBillingRow` always true (1), both layers removed (8), version in force edited in place (3), `ForFirm` treats unknown as visible (1), firm clause ignores the rule set flag (2), publish skips validation (3), publish puts Proposed rules in force (2), predecessor never stamped (2), validation with the Ontology domain (20), publish route without its auth filter (6), read routes gated on a policy privilege - the over-grant (8), publish writes the rule set before the policies (1), `MaxPerTransaction` unbounded (1), `MaxInValues` unbounded (1), cancel without compensation (1).
- Publish size (Release, `-r linux-x64 --self-contained false`, `Compress-Archive` Optimal, short-path copies from `git archive`, 192 files each): base `43451b9b2` **39,467,636 B** -> head **39,512,769 B**, delta **+45,133 B** (+0.043 MB; 37.68 MB total). Under the +5 MB and 55 MB thresholds. No PackageReference change; `dotnet list package --vulnerable --include-transitive`: none.
- Placement (root section 10): BFF, B1 caller identity.

## 5. Review (self-run; the independent reviewer is left to the caller - concurrent-agent cap)

- ADR-001 (Minimal API group), 008 (per-route filters), 010 (concrete DI, no interface, ceiling unchanged), 019 (ProblemDetails `errorCode`), 002 (no plugin), 003 / 028 (SPE container server-derived, caller's privilege asked as the caller, fail closed), 007 (no Graph type referenced by the service).
- Class-grep: no unguarded `Single` / `First` / `Guid.Parse` on external data (the one `Guid.Parse` parses `CreateDocumentAsync`'s own result, as 078); the `OperationCanceledException` catches carry `when (ct.IsCancellationRequested)`; `In` lists are batched at 500, transactions at 1,000 (the 078 lessons).

## 6. Known limits (none blocks the task; owner decision where marked)

- **K: audit.** Edits, accepts and publishes are written as the application identity (the service writes as the app after the caller check), so Dataverse auditing shows the app, not the person. The caller's object id is used for the owner team only. Decision for the owner: whether the Decision Record (074) should carry the actor for rule set changes.
- **K: not atomic across transactions.** Publish is versions -> policies -> rule set; each chunk is atomic, the whole is not. A failure before the pointer move leaves the old rules in force and (review round 1) takes back the end dates it wrote; a failure of the rule set write leaves the rules in force and the set not Published - publishing again finishes it by writing the set. Residual: if the process dies, or the take-back itself fails, between the version write and the take-back, a version in force keeps its end date until a publish is retried (a retry re-stamps it) or someone clears it; the take-back failure is logged as an Error with the version ids. See section 7.
- **K: concurrency (K2, section 7; narrowed by review round 2, section 8).** Closed in round 2: two edits of an in-force rule (the second is 409), two in-place edits of one draft (the second is 409), an accept / reject against content that changed (409), an edit against a draft that was decided or publish-stamped after it was read (409), and publish writes over rows that changed (409). Residuals: section 8, "Known limits".
- **K: no retire route.** `ruleset.retired` is enforced for sets retired on the form; there is no endpoint to retire a set.
- **K: no delete.** A rejected rule stays; there is no endpoint to remove a policy.
- **K: more than 5,000 rules per set** are refused with 422 `ruleset.result_truncated` (`IGenericEntityService` does not page).
- **K: no UI entry point** for a billing admin until 077 (Rule Review page) lands; admins call the routes.

## 7. Review round 1 (adversarial pass 1, all findings confirmed; base `7c0daf8c9`)

Probe tests from the reviewer (`ReviewProbeTests.cs`: probes 1-4) were ported into `RuleSetServiceTests.cs` as must-fire tests; each failed against the unfixed service before its fix (red counts below), then passed.

| # | Finding | Fix (`RuleSetService.cs`) | Commit | Must-fire tests (red before / green after) |
|---|---|---|---|---|
| 1 | F1: a retry after a failed rule set write is refused as `ruleset.nothing_to_publish`, so the set stays Draft | With no draft left, a set that is not Published while a rule's current version is its working version is finished: only the rule set is written (`MarkPublishedAsync`). The set shows the date the first attempt stamped on the versions, not the retry's date | `01a697d3c` | `Publish_WhenTheLastWriteFails_ARetryMarksTheSetPublished_AndWritesNothingElse` (2 rows), `Publish_OfADraftSetWithARuleInForceAndAProposedOne_FinishesTheSet_AndCountsTheProposedOne`; control `Publish_OfAPublishedSetWithEverythingInForce_HasNothingToPublish_AndWritesNothing`. 3 red / 71 total green |
| 2 | F1: the old version's end date is written before the pointer moves and nothing takes it back (D-14); a disconnect abandons the writes; a retry with another date leaves a gap | The write phase (`WritePublishAsync`) runs with `CancellationToken.None`; the caller going away stops a publish only before the first write. A failure in the version or policy writes runs `TakeBackEndDatesAsync`: from a fresh read, it puts back each old version's prior `sprk_inforceto` only where the policy's pointer did NOT move (so a partly applied chunk keeps the moved rules' dates); a version it cannot put back is logged as an Error with its id. An old version whose end date differs from the new effective date is re-stamped | `298991ade` | `Publish_WhenThePolicyTransactionFails_...` (now asserts the end dates), `..._AfterAFailedPointerMove_ARetryWithAnotherDate_PutsTheNewDateOnBothVersions_LeavingNoGap`, `..._WhenTheEndDateCannotBeTakenBack_TheFailureIsLoggedAsAnError_AndARetryWithAnotherDateRestampsIt`, `..._AfterAFailedPointerMove_RejectingTheDraftLeavesNoEndDateOnTheVersionInForce`, `..._WhenTheVersionWritesFailPartWay_...`, `..._WhenOnlySomePolicyChunksMove_...`, `..._WhenTheCallerDisconnectsDuringTheWrites_StillFinishesAllThreeWrites`; control `..._CancelledBeforeAnyWrite_WritesNothing_AndRethrows`. 7 red / 67 total green |
| 3 | F1: editing an Accepted draft keeps it Accepted, so publish puts the edit in force | An in-place edit that CHANGES the type, body, template or consequence of an Accepted or Rejected draft sets `sprk_proposalstatus = Proposed`. A headline or clause edit, or one that sets the same value, keeps the decision (decision: the brief names the four members; the headline and clause are display text, the decision was about what the rule does) | `1eb3806d6` | `Edit_OfADecidedDraft_ThatChangesTheTypeBodyTemplateOrConsequence_ReturnsItToProposed` (8 rows), `EditThenPublish_WithoutAcceptingAgain_DoesNotPutTheEditInForce`; controls `Edit_OfAnAcceptedDraft_ThatChangesNothingTheRuleDoes_KeepsItAccepted` (5 rows). 9 red / 85 total green. `BillingOperations_NeverWriteTheDomainColumn_...` is unchanged (its headline edit does not demote). **Superseded by D-109 / round 2 item 3:** a clause text, section reference or location change also returns the draft to Proposed, and every content change writes Proposed whatever state was read |
| 4 | F4: the firm clause helper ignores the asking firm | `GetFirmClauseAsync(policyVersionId, firmId, ct)`, `firmId` required: None unless the set is the client default or `sprk_firm == firmId`; None for an unassigned set, a firm set without its firm, and an empty `firmId` (the caller has no firm). The flag still decides on top (D-79) | `2ab2d0645` | `GetFirmClause_ShowsAFirmsOwnRuleSetToThatFirmOnly_AndTheClientDefaultToEveryFirm`, `GetFirmClause_FailsClosedWhenTheAssignmentIsIncompleteOrNoFirmIsAsking`; control `GetFirmClause_WithTheFlagOff_ShowsNothingToAnyFirm_EvenTheOneTheRuleSetIsAssignedTo`. 2 red / 88 total green |

**For 130 / 134 / 135 (callers of `GetFirmClauseAsync`):** superseded by review round 2 (section 8, item 5). The parameter is now `invoiceFirmId`: the invoice's `sprk_vendororg`, never the SPA identity's firm.

### K1 - D-108 (owner accepted): billing admin = Write on `sprk_ruleset` at ANY depth

No change to the check. D-108 is recorded in the `BillingAdminAuthorization` remarks (commit below): the check asks whether the caller holds the privilege (`FinanceAuthorizationCheck.CallerPrivilege`, "any depth"), not at what depth; the role the owner grants carries Org depth in practice.

### K2 - concurrent edits of an in-force version both create version N+1: recorded as a known limit, no code change (CLOSED in round 2, section 8 item 1, option (b))

Looked for an existing optimistic-concurrency mechanism:
- Alternate key on (policy, version number): none. `SpaarkeMaster/Entities/sprk_PolicyVersion/Entity.xml` has no `EntityKeys`; `sprk_Policy` has only `sprk_PolicyCodeKey`.
- ETag on the policy row: the platform has one (`IFieldMappingDataverseService.UpdateRecordFieldsIfUnchangedAsync`, `If-Match` on `versionnumber`, used by `InvoiceLifecycleService` / `InvoiceReviewService`), but not on `IGenericEntityService`, the seam this service uses, and the in-force edit path writes no policy row, so there is no write to make conditional. A deliberate "touch" write to the shared policy row would work only if Dataverse advances `versionnumber` for an update that changes no value, which I could not verify without a live write (none allowed), and it would add a second Dataverse seam to the service.

Scenario: admins A and B both open a rule whose working version is v2 (in force). Both PATCH. Both read the highest number as 2 and create v3; both get 200 with `createdNewVersion = true`. The working version is the highest number, so one v3 is shown and can be accepted and published; the other is never shown. One admin's edit is silently lost; nothing wrong goes into force.

If the owner wants it closed: (a) an alternate key (policy, version number) on `sprk_PolicyVersion` (a schema change in 019's area, shared with the Ontology platform, whose version writer must not collide), with `DataverseServiceClientImpl.IsAlternateKeyDuplicate` mapped to a 409; (b) a version id derived from (policy, number), so the primary key is the uniqueness constraint (needs a live check of the fault shape); (c) inject `IFieldMappingDataverseService` and make a policy-row write conditional on its `versionnumber`.

### K3 - constraint for task 072 (copy into 072's POML)

> **In force = the policy's current version, OR a version that has an end date. A version that is neither is a draft and is never in force, whatever its `sprk_inforcefrom` says.** 076 creates the new version of an edit with `sprk_inforcefrom = UtcNow` (the column is required; the value is provisional) and no `sprk_inforceto`, so a plain date-range test (`inforcefrom <= date AND (inforceto IS NULL OR date < inforceto)`) would treat every unpublished draft as in force from the day it was drafted. 072 selects the policy's current version (the `sprk_currentversion` pointer) for dates on or after its `sprk_inforcefrom`, and ended versions (`sprk_inforceto` not null) for earlier dates inside their `[inforcefrom, inforceto)` range; it never selects a version that is not current and has no end date. Publish sets the new version's `sprk_inforcefrom` to the effective date and stamps the superseded version's `sprk_inforceto` with the same instant, so the ranges are contiguous. The pointer alone decides the current version, even when it carries an end date left by a failed publish (the take-back in section 7 clears it, but a crash can leave one).

### Other decisions and limits from this round

- **Take-back decides from a fresh read, not from which write failed** (a partly applied chunk, or a write whose outcome is unknown, is handled the same way). ~~It restores each old version's value from before the publish~~ - superseded by round 2 (section 8, item 4): it clears the end date, because a version whose pointer did not move is still in force.
- **Residual (known limit, rare):** a process kill between the version write and the take-back, or a take-back that itself fails (logged as an Error with the version ids), leaves an end date on a version still in force until a publish is retried. A retry re-stamps it. Rejecting the successor instead leaves it. It fails safe for 072 (the pointer decides, K3) and for the Ontology job, which refuses to evaluate that policy rather than closing its Signals (`SignalReevaluationJob`).
- **Class search (task-execute 9.5 rule 6):** other writers of `sprk_inforceto` in `src`: `Services/Ontology/OntologyAdminAuthoringService.cs` (the Ontology publish; another project's code, not opened or verified here, listed for the operator: it may have the same stamp-before-pointer exposure). Other readers of `sprk_visibletofirms`: none outside `RuleSetService.cs`. Multi-step writes in `RuleSetService` that use the request token: none (every other write is a single request).
- **Not changed:** the `RuleSetClauseVisibility.ForFirm` flag decision; the endpoints; DI; packages.

### Verification (round 1)

- Build `dotnet build tests/unit/Sprk.Bff.Api.Tests` 0 warnings, 0 errors.
- `RuleSetServiceTests`: 60 -> **88** (28 added incl. theory rows); 88 pass / 0 fail.
- Filter `RuleSet|BillingAdmin|Finance` on `tests/unit/Sprk.Bff.Api.Tests`: **1197 pass / 0 fail / 3 skipped**.
- ArchTests (`tests/Spaarke.ArchTests`): **1160 pass / 0 fail / 0 skipped**. No new route (route ledger stays 137, ADR-010 ceiling 166); no DI change.
- Mutation proof: the new tests were red against the unfixed service, with the red counts in the table above. Each red run was taken before its fix was written (the F4 run kept the new parameter ignored, so the tests failed on behaviour, not on a missing member).
- `dotnet format` ran in the pre-commit hook (lint-staged) on the staged files. The hook uses a transient backup stash; none was left behind (G-18).
- Not run: `dotnet list package --vulnerable --include-transitive` (no csproj or package change in this round).
- Publish size (BFF hygiene, root section 10; Release, `-r linux-x64 --self-contained false`, `Compress-Archive` Optimal over `deploy/api-publish/*`, PDBs included, short-path copies from `git archive`, 192 files each): base `7c0daf8c9` **39,554,732 B**, head **39,558,392 B**, round delta **+3,660 B**; fresh master `5f1593aca` **39,126,814 B**, head vs master **+431,578 B (+0.41 MB; 37.73 MB total)** - the branch carries other tasks ahead of master. Under the +5 MB and 55 MB thresholds. No PackageReference change.

## 8. Review round 2 (fix list from the main session; base `be1c60e38`)

Commits:
- `05b8c75aa`: conditional bulk update on the app identity.
- `1713054e4`: items 1-6 in the service.
- `df0b8bb5b`: the Step 9.5 finding, a conditional in-place edit.

Writes stay on the app identity (`IGenericEntityService`, D-76); `IDataverseUserClient` is not used. There is no schema change, and option (a), the alternate key, is not used. Each red count was taken by running the new tests against the previous service file (fail-before); all of them pass after the fix.

| # | Finding | Fix (`RuleSetService.cs` unless named) | Must-fire tests |
|---|---|---|---|
| 1 | F2: two edits of an in-force version both create N+1 | The new version's id is an RFC 4122 v5 id of (policy, number), built by `VersionIdFor` from the name `sprk_policyversion:{policyId:D}:{number}`. The primary key is therefore the uniqueness constraint. On any create failure other than cancellation, the service re-reads with `CancellationToken.None`. If N+1 exists, the result is 409 `ruleset.concurrent_edit`; otherwise the error is rethrown as a 500. The version query is ordered by `sprk_versionnumber`, `createdon`, `sprk_policyversionid` | Red before: `Edit_TwoConcurrentEditsOfAVersionInForce_..._TheOtherIs409_...`, `TheVersionsOfARuleAreReadInAStableOrder_...`. Also added: `VersionIdFor_IsAVersion5IdOfThePolicyAndTheNumber` and the control `Edit_WhenTheNewVersionCannotBeCreated_AndNoOtherEditCreatedIt_Is500`, which was green before because the outcome is unchanged |
| 2 | F1: the publish writes are not conditional | `versionnumber` is now read on policies and versions. `DataverseServiceClientImpl.BuildBulkUpdateTransaction` takes an optional row version per update (`IfRowVersionMatches`). `BulkUpdateIfRowVersionsMatchAsync` maps a mismatch to `System.Data.DBConcurrencyException`: code `0x80060882`, found in the fault chain or in a transaction fault's `InnerFault`. Steps 1 (versions) and 2 (policies) are conditional. A refused step-1 chunk did not land, so it is not taken back. A refused step-2 chunk means another publish moved those pointers, so its step-1 stamps stand. Either refusal returns 409 `ruleset.concurrent_edit`; any other failure is still a 500. Every update and its row version are built before the first write | Red before: `Publish_WhenAnOldVersionWasStampedByAConcurrentPublish_...409_...`, `Publish_WhenThePointerWasMovedByAConcurrentPublish_...409_...`, `Publish_WhenALaterVersionChunkIsRefused_TakesBackOnlyTheChunkThatLanded_Conditionally`, `Publish_TheTakeBackIsConditionalOnItsFreshRead_...`. `BulkUpdateTransactionTests` gained 3 tests; before the fix they failed to compile rather than on behaviour, because the members did not exist |
| 3 | F1: the edit / accept race, and D-109 | Every content change now writes `Proposed`; the `!= Proposed` condition is gone. Content means type, body, template, consequence, clause text, section reference and clause location (D-109); a headline-only change keeps the decision. Accept and reject are conditional on the row version they read, and a mismatch is a 409. The Step 9.5 commit `df0b8bb5b` makes the in-place edit conditional too (409). So an accept, another edit or a publish's step-1 stamp landing between an edit's read and its write refuses the edit | Red before: `Accept_ThatReadTheOldContent_IsRefused409_WhenAnEditLandedFirst`, `Edit_ThatReadProposed_IsRefused409_WhenAnAcceptLandedBeforeItsWrite_SoTheEditIsNeverAccepted`, `Edit_OfADraftAPublishHasStartedToPutInForce_IsRefused409_...`, and the clause and Proposed rows of `Edit_OfADraft_ThatChangesItsContent_WritesProposed_WhateverStateItWasReadIn`. `Edit_OfADraft_UpdatesItInPlace_...` and `Accept_And_Reject_...` were updated for the conditional write |
| 4 | F1: the take-back restores `old.InForceTo` | The take-back now writes `DBNull`, because a version whose pointer did not move is still in force. It touches only versions that the pointer still names and that carry an end date. Each write is conditional on the fresh read's row version, and a refusal is logged as an Error with the ids | Red before: `Publish_TheTakeBackLeavesNoEndDate_EvenWhenTheOldVersionCarriedAStaleOneBeforeThePublish` (v1 carries E1, step 2 fails, and v1 must end with no end date) |
| 5 | F4: the `GetFirmClauseAsync` caller contract | The parameter is renamed `invoiceFirmId`, and the XML doc carries the contract below. Behaviour is unchanged, so there is no new test; the round-1 tests cover the behaviour | None |
| 6 | F4: the retry branch can report a Rejected draft that was never in force | The superseded version reported is now the highest-numbered other version that carries an end date, or null | Red before: `Publish_FinishingASet_ReportsTheVersionThatEnded_NeverARejectedDraftThatWasNeverInForce` |

Fail-before totals:
- Items 1-4 and 6: 17 of the 138 `RuleSetServiceTests` failed against the round-1 service.
- The Step 9.5 change: 3 tests failed against `1713054e4`'s service.

All of them pass after the fixes.

### Caller contract for 130 / 131 / 133 / 134 / 135 (copy into those POMLs)

> `GetFirmClauseAsync(policyVersionId, invoiceFirmId, ct)`: `invoiceFirmId` is the firm of the INVOICE the finding belongs to. That is the invoice's `sprk_vendororg`, read server-side. Pass it only after the route's own firm-membership check has established that the caller may see that invoice. Never pass a value from the request (body, query or route), and never pass the SPA identity's firm. A CIAM caller may belong to several firms (130), so "the caller's firm" is not one value, and internal callers (131, 133) have no SPA identity. `Guid.Empty` (the invoice has no firm) returns no text.

### Section 11 - new surface this round

- **`IGenericEntityService.BulkUpdateIfRowVersionsMatchAsync`, its `DataverseServiceClientImpl` implementation, `IsRowVersionMismatch` and `ConcurrencyVersionMismatch`.**
  - Existing: `BulkUpdateAsync`, which is unconditional, and `IFieldMappingDataverseService.UpdateRecordFieldsIfUnchangedAsync`, which uses Web API `If-Match` on one row through another seam.
  - Extension: the change extends `BuildBulkUpdateTransaction` with an optional list of row versions. A new member is still needed because `BulkUpdateAsync` must stay unconditional for its existing callers. The member is a default interface member that throws, so the hand-written test fakes do not break.
  - Cost of doing nothing: a publish overwrites a pointer or an end date that another publish wrote (item 2).
- **`RuleSetService.VersionIdFor`.**
  - Existing: `InquiryDispositionService.TodoIdFor` and `SignalSuppressionService.ProbeId`. Both are ontology-owned and private, with fixed namespaces.
  - Extension: neither can be extended here, because ontology-owned files are out of this task's scope.
  - Cost of doing nothing: two edits both create N+1 (item 1).
  - Three copies of the v5 derivation now exist; a shared helper is a follow-up for the operator.
- **`CodeConcurrentEdit` (`ruleset.concurrent_edit`).**
  - Existing: the service had no 409 code.
  - Extension: none is needed outside the service. The 409 response metadata was already declared on edit, accept, reject and publish, and `RuleSetEndpoints.cs` maps refusals generically, so it is unchanged.
  - Cost of doing nothing: the conflicts in items 1-3 would have no error code to return.

### Known limits (round 2)

- **An edit whose read and write both fall between a publish's step 1 and step 2** still goes through: its read already sees the stamp, so its row version matches. Closing this needs a multi-table transaction. The window is one round trip.
- **A same-date retry over a stale end date:** an old version that already carries the effective date is not re-stamped, so a take-back does not clear it. This fails safe, because the pointer decides what is in force (K3), and the retry completes.
- **A take-back chunk refused on one row** rolls back the whole chunk (it is one transaction), and the chunk's ids are logged as an Error.
- **Mismatch detection** reads the error code, or the text `0x80060882` / `ConcurrencyVersionMismatch`. A fault from a concurrent request surfacing in the same call (#971) could be misreported as a mismatch.
- **A create that succeeded but lost its response** is reported as a 409. The message says another edit created the next version, which in this case is wrong (the caller's own edit did). Its advice still holds: reload the rule, and make the change again only if it is not there.
- **Not checked live:** the fault shape of a duplicate primary key, and of `IfRowVersionMatches` inside `ExecuteTransaction`. No live writes were allowed this round; the unit tests use the documented code.

### Verification (round 2)

- Build: `dotnet build tests/unit/Sprk.Bff.Api.Tests` has 0 errors.
- `RuleSetServiceTests`, including the harness shared with `RuleSetServiceDraftTests`: the RuleSet filter gives **234 pass / 0 fail**.
- The filter `RuleSet|BillingAdmin|Finance` on `tests/unit/Sprk.Bff.Api.Tests` gives **1381 pass / 0 fail / 3 skipped**, up from 1363 pass before this round.
- `BulkUpdateTransactionTests`: the 3 added tests pass.
- ArchTests (`tests/Spaarke.ArchTests`) at `df0b8bb5b`: **1170 pass / 0 fail / 0 skipped**.
- This round adds no route and no DI registration, so the route ledger stays at 137 and the ADR-010 ceiling at 166.
- There is no csproj or package change, so `dotnet list package --vulnerable` was not needed.
- Publish size, measured as in round 1 (192 files each):

  | Build | Size |
  |---|---|
  | Base `be1c60e38` | **39,587,664 B** |
  | Head `df0b8bb5b` | **39,592,059 B** |
  | Fresh master `a82578cdc` | **39,190,807 B** |

  The round delta (head vs base) is **+4,395 B**. Head vs master is **+401,252 B (+0.38 MB; 37.76 MB total)**. Both are under the +5 MB and 55 MB thresholds.
- Class search: the other writers of `sprk_inforceto` are in `Services/Ontology/**` and `Services/Signals/**`. They are ontology-owned, so they are listed for the operator and not changed.

## 9. Review round 3 (fix list from the main session; base `229643893`)

Commits:
- `e1501dbbc`: item 2, `MapConditionalBulkFailure`.
- `9a6bbfe01`: item 1, `stampsStand` removed.
- `f9cbfa486`: item 3, the publish claim.

No schema change, no new registration, no package change. Each red count was taken by running the new tests against the code without the fix (for a one-line fix, a mutation that undoes it).

| # | Finding | Fix | Must-fire tests and fail-before evidence |
|---|---|---|---|
| 1 | F3: the `stampsStand` test proved nothing; one unrelated edit of a policy in a chunk left every old version of the chunk current but carrying an end date | The exclusion is removed. A refused step 2 chunk is taken back like any other failure. The take-back already decides from a fresh read, so a policy whose pointer another writer moved keeps its stamp, and a policy that changed without its pointer moving has its stamp cleared. The rule now stated in the code: a version that is still the policy's current version never ends with an end date, unless the same publish moves its pointer | `Publish_WhenThePolicyChangedWithoutItsPointerMoving_...` (2 rows: `sprk_name` renamed, `sprk_enabled` toggled; the end state is v1 current with no end date, and a retry completes) and `Publish_WhenOneChunkOfPolicyWritesIsRefusedWithoutAPointerMove_...` (1,001 rules; the chunk that landed keeps its stamps, the refused chunk's are cleared). **3 of 3 failed** with the exclusion in place (found `2026-11-01`, expected null). The round 2 test `...TheStampsOfThatChunkStand` still passes: it covers a moved pointer, which the fresh read handles |
| 2 | F3: the catch in `BulkUpdateIfRowVersionsMatchAsync` was untested | The decision moved to the pure `DataverseServiceClientImpl.MapConditionalBulkFailure(entity, updates, failure)`: a mismatch is a `DBConcurrencyException`; every other failure is the `InvalidOperationException` that `BulkUpdateAsync` throws (worded by `DescribeBulkUpdateFailure`). The catch only logs and throws what it returns. A non-fault exception (a timeout) keeps today's behaviour: an `InvalidOperationException` that says the outcome is unknown. It is not rethrown bare, because that would change the 500 path | 5 tests in `BulkUpdateTransactionTests`: the code on the fault, on an inner fault (also wrapped), as message text, an unrelated fault, a non-fault exception. The helper did not exist before, so the red count is by mutation (the mismatch test made always false): **3 of 5 failed** |
| 3 | Real defect (filed as K3 by the reviewer): the take-back race | **Option chosen: a per-rule-set publish claim over the existing `IScheduledJobLease`**, not a column on `sprk_ruleset`. See below | `Publish_WhileAnotherPublishOfTheSetHoldsTheClaim_...`, `Publish_WhenTheClaimStoreIsUnreachable_FailsClosed_...`, `Publish_WhenASecondPublishStartsWhileTheFirstFailsAtItsPointerMove_...` (two real threads; P2 is parked just before its pointer move while P1 fails and takes back), and the must-not-fire `Publish_ReleasesItsClaim_HoweverItEnds_...` (done, refused, failed, throws). With the claim skipped, **3 failed** (the race ends with the pointer on v2 and v1 without an end date, the exact defect). With the release removed, the 4 release rows and every test that publishes twice failed (14 in the `Publish` filter) |

### Item 3: why a lease and not the options in the brief

- **Why the take-back race cannot be closed by conditional writes alone.**
  - A take-back's clear is conditional on the row version of its own fresh read. That read happens after the second publish's stamp, so the condition holds and the clear lands. The second publish's pointer move is conditional on a policy row version the first publish never touched, so it also lands.
  - "P2 re-verifies after moving its pointer" has a hole: P1 can read, P2 can move and verify, and then P1 clears with the row version it read. Closing that needs a multi-table transaction (a clear of v1 and a touch of the policy row in one request), whose no-op-update row version behaviour is unverified live.
  - A claim marker on `sprk_ruleset` needs a new column, which the brief rules out.
- **What shipped.**
  - `PublishAsync` takes the lease `ruleset-publish:{ruleSetId}` (`RuleSetService.PublishClaimId`) before its first read, for ten minutes (`PublishClaimLifetime`, the stale-claim timeout), and releases it in a `finally` with `CancellationToken.None`, however the publish ends.
  - A second publish of the same set finds it held and gets 409 `ruleset.publish_in_progress`, having read and written nothing. Publishes of other sets are not affected.
  - An unreachable claim store fails closed: 500 `ruleset.write_failed`, nothing read or written.
  - The body of the publish is unchanged (`PublishClaimedAsync`).
- **Distribution.** In the BFF the lease is the Redis one (`SET NX PX`, token-checked release), so the claim holds across instances and slots. With Redis off (single-instance development) it is process-local.
- **Precedent.** `DecisionCommitService` already uses `IScheduledJobLease` as a mutex outside the scheduler. The ArchTest that keeps jobs host-neutral concerns `IScheduledJob` types only, so it does not apply.

### Section 11 - new surface this round

- **`RuleSetService` depends on `IScheduledJobLease`; new code `ruleset.publish_in_progress`; `DataverseServiceClientImpl.MapConditionalBulkFailure`.**
  - Existing: `IScheduledJobLease` (the registration already exists in `SchedulingModule`; `DecisionCommitService` is the precedent). `MapConditionalBulkFailure` only moves code that was inside the catch.
  - Extension: the lease is the extension. A `sprk_ruleset` marker column would be schema; an in-process lock would not hold across instances.
  - Cost of doing nothing: a superseded version can end with no end date, and 072 never evaluates it for invoices dated in its window (item 3).
- No route, no DI registration, no package. The route ledger stays at 137.

### Known limits (round 3)

Carried from round 2 and earlier, as stated:
- **`IfRowVersionMatches` inside `ExecuteTransactionRequest` is unverified live.** It is a pre-deploy dev probe item for the main session: a conditional update inside a transaction, with the row changed in between, must fault with `0x80060882` and roll the transaction back. The unit tests use the documented code.
- **A retry after a lost response can be misreported as a mismatch.** The SDK may retry the transaction on throttling. If the first attempt landed but its response was lost, the retry finds the row versions changed and reads as a 409, although the publish's own write is what changed them.
- **The edit / publish window for sets above 1,000 rules.** The writes are several chunk transactions, so an edit can land between two chunks. Each chunk is conditional, so the edit or the publish is refused, never both applied half way.
- **Accept / reject staleness from the page load.** The client sends no version, so the service reads the row version when the request arrives. An accept can therefore decide on content the admin did not see if an edit landed after the page loaded. Note for task 077: the page should send the version it showed (or the proposal's content hash) and the service should compare it.
- **A create that succeeded but lost its response is reported as a 409** (round 2).

New or restated this round:
- **The claim is not renewed.** A publish that outlived `PublishClaimLifetime` (ten minutes; a few thousand rows take seconds) would let a second publish in. The conditional writes still catch every case except the take-back race the claim exists to close.
- **The claim covers publishes of one set only.** Edits, decisions and the Ontology publish (`Services/Ontology/**`, another project's code) are not serialised with it; the conditional writes and the fresh-read take-back cover them.
- **A refused step 2 chunk caused by a rename or toggle** now takes back the stamps. If that take-back itself fails, the Error log names the version ids, as before; a retry of the publish re-stamps.

### Verification (round 3)

- Build: `dotnet build src/server/api/Sprk.Bff.Api/` 0 warnings, 0 errors.
- Filter `RuleSet|BulkUpdate|BillingAdmin` on `tests/unit/Sprk.Bff.Api.Tests`: **261 pass / 0 fail**. `RuleSet` alone: **244 pass** (round 2: 234; +3 item 1, +7 item 3). `BulkUpdateTransactionTests`: **15 pass** (+5 item 2).
- The wider filter `RuleSet|BulkUpdate|BillingAdmin|Finance`: **1408 pass / 0 fail / 3 skipped** (round 2: 1381 pass, without `BulkUpdate`).
- ArchTests (`tests/Spaarke.ArchTests`): **1175 pass / 0 fail / 0 skipped**.
- Publish size, measured as in round 1 (Release, `-r linux-x64 --self-contained false`, `Compress-Archive` Optimal, PDBs included, short-path copies from `git archive`, 192 files each):

  | Build | Size |
  |---|---|
  | Base `229643893` | **39,594,409 B** |
  | Head `f9cbfa486` | **39,595,681 B** |
  | Fresh master `a82578cdc` | **39,190,712 B** |

  The round delta is **+1,272 B**. Head vs master is **+404,969 B (+0.39 MB; 37.76 MB total)**. Both are under the +5 MB and 55 MB thresholds.
- Class search: no code constructs `RuleSetService` outside the DI registration and the three test harnesses, which were updated.
