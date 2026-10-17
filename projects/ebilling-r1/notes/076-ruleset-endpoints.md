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
6. **Clause visibility - one decision.** `RuleSetClauseVisibility.ForFirm(visibleFlag, quote, location)` (pure) and `RuleSetService.GetFirmClauseAsync(policyVersionId)` (reads the flag, fails closed: no text unless the version is or was in force, its policy is billing, and the rule set is Published). Flag off or unknown = `FirmClauseText.None` (no quote, no section reference). The result type has two members and no document link. 130 / 134 / 135 call `GetFirmClauseAsync`; the Procedure Library document is never returned to firms (D-66, D-79). Turning the flag off takes effect on the next call.
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
- **K: not atomic across transactions.** Publish is versions -> policies -> rule set; each chunk is atomic, the whole is not. A failure before the pointer move leaves the old rules in force; a failure between the pointer move and the rule set write leaves the rules in force and the set still Draft/Published-as-before - publishing again completes it (idempotent: in-force rules are skipped).
- **K: no concurrency token.** Two admins editing one draft rule race last-write-wins; two simultaneous publishes of one set can both pass validation and write the same values.
- **K: no retire route.** `ruleset.retired` is enforced for sets retired on the form; there is no endpoint to retire a set.
- **K: no delete.** A rejected rule stays; there is no endpoint to remove a policy.
- **K: more than 5,000 rules per set** are refused with 422 `ruleset.result_truncated` (`IGenericEntityService` does not page).
- **K: no UI entry point** for a billing admin until 077 (Rule Review page) lands; admins call the routes.
