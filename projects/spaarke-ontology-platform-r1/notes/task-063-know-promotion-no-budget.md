# Task 063 - first Know-promotion rule: "this matter has no budget"

Branch `task/ontology-063` (PR into `docs/ontology-platform-design`). Rigor STANDARD, raised in practice to FULL checks because the task edits `.cs` and `tests/**`.
Sonnet 5.5. Owner decision 2026-10-17 resolved the POML's earlier escalation: add `(sprk_matter, sprk_budget)` to `PredicateCompiler.VerifiedJoins` inside this task.

## What shipped

| Piece | File |
|---|---|
| The join `(sprk_matter, sprk_budget)` -> `sprk_matter` | `src/server/api/Sprk.Bff.Api/Services/Signals/PredicateCompiler.cs` (`VerifiedJoins`, doc comment) |
| Policy and version definition (one policy, one version) | `config/know-promotion.policies.json` |
| The apply script now accepts the definition kind `know-promotion-policies` (Decide lane, Decide-lane plan codes); the Do-lane behaviour is unchanged | `scripts/Apply-DoLanePolicies.ps1` |
| Unit tests: the join accepted, wrong paths refused | `tests/unit/domain/Signals/PredicateCompilerTests.cs` |
| Unit tests: the definition passes the evaluator's own gates, section 0.3, plan offers assign-work | `tests/unit/domain/Signals/KnowPromotionPolicyDefinitionTests.cs` |
| Live read-only proof | `tests/integration/seam/Signals/KnowPromotionPolicySeamTests.cs` |

## The join, confirmed against the live schema (read-only, 2026-10-16)

`GET EntityDefinitions(LogicalName='sprk_budget')/ManyToOneRelationships?$filter=ReferencedEntity eq 'sprk_matter'` on spaarkedev1 returned exactly one relationship:

```
SchemaName: sprk_Matter_Budget_1n   ReferencingAttribute: sprk_matter   ReferencedEntity: sprk_matter
```

`sprk_budget`'s Lookup attributes are createdby, createdonbehalfby, modifiedby, modifiedonbehalfby, owningbusinessunit, owningteam, owninguser, sprk_assignedtoexternal, sprk_assignedtointernal, sprk_createdbyperson, `sprk_matter`, sprk_project, transactioncurrencyid. `sprk_matter` is the only one that targets `sprk_matter` (`sprk_project` targets the project; a budget row can belong to a project instead of a matter). The existing live seam `SignalPredicateTests.CompilerAllowLists_MatchTheLiveSchemaAndTheCallersReadDepth` iterates `VerifiedJoins`, so it now checks the new entry too: it passed live.

`sprk_budget` was already in `EvaluatorGlobalReadableEntities`; `notes/security-roles.md` records `prvReadsprk_Budget` (Global) on Spaarke Ontology Service since setup (lines ~147, 726). The proof below ran with `SIGNALS_LIVE_CALLER_ID=3121bf1b-...` (the writer) set; `WhoAmI` printed the operator `1d02f31c-1872-f011-b4cb-7c1e52671ad0`, so this note does not claim impersonation took effect. The query saw all 62 matters and all 4 budgets, the same counts as the admin Web API read, so no row was trimmed. The writer's Global read on `sprk_budget` rests on the role record above; a run that proves impersonation (task 030's method, notes/security-roles.md section 9.2) is the evaluator's own run-time check.

`sprk_budgetrevision` was NOT substituted (section 0.3): the message says "no active budget record", and the body reads `sprk_budget`.

## The policy

| Field | Value |
|---|---|
| Code / ids | `POL-MATTER-NOBUDGET`, policy `00000000-0000-4000-8000-000000006301`, version `...006311` (fixed ids: idempotent upsert) |
| Lane / subject / work type / severity | Decide / Matter / Approve or rebudget / Info (all labels exist on the live columns) |
| Enabled | **No** (created Off; the script never changes it) |
| Body | `{"type":"Existence","subject":"sprk_matter","when":{"createdon":{"<=":"now-5d"}},"all":[{"notExists":"sprk_budget","path":"sprk_matter","filter":{"statecode":0}}]}` |
| Message template | "This matter was created more than 5 days ago and has no active budget record." (literal, no tokens) |
| Headline | "Matter created more than 5 days ago with no active budget" |
| Decision plan (D-51) | `{"actions":["assign-work"],"nextSteps":[]}` |
| Proposed action | "assign-work: assign someone to set up this matter's budget." |

### Section 0.3, claim by claim

| Claim in the sentence | Read by the body |
|---|---|
| "created more than 5 days ago" | `when.createdon <= now-5d` |
| "no ... budget record" | `notExists sprk_budget via sprk_matter` |
| "active" | the clause filter `statecode = 0`; an inactive budget row is not counted, so the sentence says "active" |

The sentence makes no claim about revisions, amounts, spend, documents or communications. `KnowPromotionPolicyDefinitionTests.TheMessageTemplate_AssertsOnlyWhatTheBodyReads` pins this (and fails if a banned word enters the sentence or headline).

### Why `statecode = 0` is in the clause

The Existence schema requires a clause `filter` with at least one condition (`minProperties: 1`), so a bare "any sprk_budget row" cannot be written. `statecode = 0` is the natural condition ("active budget"). Dev holds 4 budgets, all active, so it changes nothing today.

## Real-data proof (spaarkedev1, read-only, 2026-10-17 03:22Z)

`KnowPromotionPolicySeamTests` compiles the definition with the production `PredicateCompiler` and runs the one FetchXML through the SDK (env `SIGNALS_LIVE_DATAVERSE_URL`, `AZURE_TOKEN_CREDENTIALS=AzureCliCredential`, `SIGNALS_LIVE_CALLER_ID` as above). Nothing is written.

| Matter | Has a `sprk_budget` row? | Compiled rule | Result |
|---|---|---|---|
| `2444af6d-e1f2-f011-8406-7ced8d1dc988` "Real estate transaction analysis" (created 2026-01-16; budget `e3cbe1fc-0f09-f111-8406-7c1e525abd8b`) | yes | per-matter `Compile(body, id)` | **0 rows** (the notExists is false); also absent from the full result |
| `3a785f76-c773-f011-b4cb-6045bdd8b757` "Commercial matter" (created 2025-08-07) | no | per-matter `Compile(body, id)` | **1 row** (the notExists is true); also present in the full result |

Whole set: **58 matters** returned = the 62 matters older than the cutoff minus the 4 matters named by an active budget, asserted equal to an independent computation from two plain queries. A wrong join column would return all 62 (notExists true for everyone); a missing one is refused at compile time.

Five-day gate on a real row, moving the clock instead of writing data (matter `ced2c3d9-47bf-f111-aaaf-0022482913fc`, created 2026-10-03, no budget): anchored 2026-10-05 (2 days old) the compiled query returns **0 rows**; anchored 2026-10-16 (13 days old) it returns **1 row**.

Compiled FetchXML (cutoff for the run at 2026-10-17T03:22:52Z):

```xml
<fetch distinct="true"><entity name="sprk_matter"><attribute name="sprk_matterid" /><order attribute="sprk_matterid" />
  <link-entity name="sprk_budget" from="sprk_matter" to="sprk_matterid" link-type="outer" alias="c0"><filter type="and"><condition attribute="statecode" operator="eq" value="0" /></filter></link-entity>
  <filter type="and"><condition attribute="createdon" operator="le" value="2026-10-12T03:22:52Z" /><condition entityname="c0" attribute="sprk_budgetid" operator="null" /></filter>
</entity></fetch>
```

## Seeding proof

With the `(sprk_matter, sprk_budget)` entry removed from `VerifiedJoins`: 12 failures across `PredicateCompilerTests` and `KnowPromotionPolicyDefinitionTests`, including `Compile_NotExistsBudgetViaSprkMatter_IsAcceptedWithTheVerifiedPath_AsAnOuterJoinWithItsOwnNullTest` (the acceptance test) and the definition's `ValidateForSave`. Entry restored; both files green.

## What the owner must know before enabling

1. **58 of the 62 dev matters would raise a Signal on the first nightly run** once `sprk_enabled` is turned on: the rule has no upper age bound (the POML says "created more than five days ago"), so every older matter without a budget qualifies, including test matters ("Test New Matter via Workspace", etc.). A tighter population (an upper age bound, `statecode`/status of the matter, or a matter-type filter) is a new policy version, not a code change. The Restricted/Limited skip (D-15) and the Secure rules are the evaluator's and apply as for any other policy.
2. The rule says "no ACTIVE budget". A matter whose only budget is deactivated qualifies. A budget that belongs to a project rather than a matter (`sprk_project` set, `sprk_matter` null) does not count for a matter.
3. Work type "Approve or rebudget" and severity Info are my choices from the live option sets; the POML names neither. Both are policy-row fields, changeable without code.

## Live write prepared, NOT run (the main session runs it)

`sprk_policy` + `sprk_policyversion` rows. `-WhatIf` and `-Verify` ran read-only against spaarkedev1:

```
Definition OK: config/know-promotion.policies.json (1 policies).
What if: Performing the operation "CREATE POL-MATTER-NOBUDGET (sprk_enabled = No)" on target "https://spaarkedev1.crm.dynamics.com sprk_policy 00000000-0000-4000-8000-000000006301".
What if: Performing the operation "CREATE POL-MATTER-NOBUDGET v1" on target "https://spaarkedev1.crm.dynamics.com sprk_policyversion 00000000-0000-4000-8000-000000006311".
  would write sprk_rulebody = {"type":"Existence","subject":"sprk_matter","when":{"createdon":{"<=":"now-5d"}},"all":[{"notExists":"sprk_budget","path":"sprk_matter","filter":{"statecode":0}}]}
  would write sprk_decisionplan = {"actions":["assign-work"],"nextSteps":[]}
  would set current version of POL-MATTER-NOBUDGET -> v1 once the policy exists
```

Commands (from the repo root, after this PR is merged and the checkout has the new script, or from this branch):

```
pwsh scripts/Apply-DoLanePolicies.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -DefinitionPath config/know-promotion.policies.json -WhatIf
pwsh scripts/Apply-DoLanePolicies.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -DefinitionPath config/know-promotion.policies.json
pwsh scripts/Apply-DoLanePolicies.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -DefinitionPath config/know-promotion.policies.json -Verify -RequireRows
```

The second command creates exactly two rows (no delete, no change to any existing row, `sprk_enabled = No`). Undo: delete the two rows by id. The default-definition `-Verify` of the four Do-lane policies still reports IN SYNC (regression check of the script change).

The acceptance criterion "when evaluated, then a Signal is produced" is met by the compiled-query proof above and by `KnowPromotionPolicyDefinitionTests`; the full evaluator path (`SignalEvaluator.EvaluateAsync` over the applied row) needs the row, so after the apply the main session can run it by adding the row's version id to a seam like `DoLanePolicySeamTests`. The policy stays Off, so no Signal is written until the owner enables it.

## Deviations from the POML

- The POML's escalation trigger (the join must be added) fired earlier; the owner decided "add it inside 063" (2026-10-17). This is that change.
- Step 4 ("confirm it fires on the seeded new matter with no budget") was proved on existing dev matters, not a newly seeded row: dev already has 58 matters with no budget and 4 with one, which is better evidence than a `zz-063-` row and needs no live write.
- Step 5 (TASK-INDEX, POML status) is not done here: the main session owns those files.
- The apply script was extended (kind -> lane and plan codes) instead of adding a second script (CLAUDE.md section 11: extension). The script's file name still says "DoLane".

## BFF hygiene (CLAUDE.md section 10, bff-hygiene items 1-6)

The BFF change is one dictionary entry and its doc comment in `PredicateCompiler`: no endpoint, service, DI registration, package, background work or AI type (items 1-3: pure modification of existing surface; no new route, so no ledger entry). Publish size (item 4), both sides from short-path trees (`C:\w63m` = `a956b1d54`, this branch's base, so the delta isolates this task; `C:\wts-063` = this branch), `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/ -r linux-x64 --no-self-contained`, `Compress-Archive -CompressionLevel Optimal`: base 37.292 MB, branch 37.292 MB, **delta 0.000 MB**, 192 files each (counts match). Item 5: `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages. `dotnet build -warnaserror` is clean; `dotnet format --verify-no-changes` is clean on the four changed C# files.

## Tests

| Suite | Result |
|---|---|
| `PredicateCompilerTests` + `KnowPromotionPolicyDefinitionTests` + `DoLanePolicyDefinitionTests` | 141 passed after the fix to my own expectations (first run: 2 of my new assertions were wrong about `TemplateEligibleFields` and the describer's two clauses; corrected, not the code) |
| `--filter` on the Signals and Ontology namespaces (unit, incl. the catalog test that equals `VerifiedJoins`) | 2260 passed, 0 failed |
| `dotnet test tests/Spaarke.ArchTests` (full) | 1072 passed, 0 failed |
| Live (read-only) `KnowPromotionPolicySeamTests` x2 + `SignalPredicateTests.CompilerAllowLists_MatchTheLiveSchemaAndTheCallersReadDepth` | 3 passed against spaarkedev1 |
| Seeding proof (join entry removed) | 12 failures, including the acceptance test; restored |

## ADR tensions

ADR-039 path A (Policy decides what is true; Binding what executes) applies as for every policy row; no other ADR tension. ADR-002: no plugin, no write path added. Review: self-review of the diff (no sub-agents per the lane brief): no F-class finding; the fail-closed property (a wrong path for the pair is refused, and the message names the verified path) is pinned by 5 theory cases.

## Review round (2026-10-17)

Opus review F1: the policy `shortName` becomes the Signal title (worklist row text) and said "Matter has no budget" while the rule only reads active budgets. Now "Matter has no active budget". `TheMessageTemplate_AssertsOnlyWhatTheBodyReads` asserts the claim on `shortName` and `name` too and runs the banned-word loop over both. Seeding proof: with the old short name the test fails ("Expected shortName ... to contain \"no active budget\""), restored 9/9 pass. Known limits (K2/K4) recorded in the PR review: no `sprk_domain` set by the apply script (empty reads as Ontology), no cross-file id check, a new budget clears the Signal only at the nightly pass, live seam returns early when unconfigured.
