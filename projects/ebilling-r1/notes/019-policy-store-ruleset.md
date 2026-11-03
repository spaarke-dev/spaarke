# Task 019 - Policy store extensions, sprk_ruleset, visible-to-firms flag

> Project: ebilling-r1 (FR-09, FR-27, FR-28, D-26, D-66, D-79). Executed 2026-10-15 against dev (`https://spaarkedev1.crm.dynamics.com`, solution `SpaarkeMaster`).
> Script: `scripts/ebilling/Deploy-EbillingPolicyStoreSchema.ps1`. Data model: `docs/data-model/sprk_ruleset.md`.

## Result

Schema applied to dev; a second run created and changed nothing (`created : 0, changed : 0, skipped : 20`, "nothing changed - no publish"). No policy row, column, option or relationship that existed before was touched. No file under `src/dataverse/solutions/SpaarkeMaster/**` or any C# file was changed.

**Open item: ontology-r1's written review of the column list is not yet obtained** (the executing agent may not message other sessions; the main session consults ontology-r1). The message to send is at the end of this file. Acceptance criterion 5 stays open until the answer is pasted under "ontology-r1 review".

## 1. Collision search (step 1)

Dev metadata read 2026-10-15 before any write:

| Planned name | Where searched | Result |
|---|---|---|
| table `sprk_ruleset` | dev `EntityDefinitions` (404); SpaarkeMaster source; `src`, `scripts`, `docs`, `.claude`, `projects/spaarke-ontology-platform-r1` | does not exist; no reference anywhere |
| global option set `sprk_policydomain` | dev `GlobalOptionSetDefinitions` filtered on polic / domain / rule / consequ / proposal | does not exist (only `botcomponentreusepolicy`, `convertrule_channelactivity`, `recurrencerule_monthofyear` matched) |
| `sprk_domain` | dev `sprk_policy` / `sprk_policyversion` attributes; source | **not** on the two policy tables. The name exists on `account` and `sprk_organization` as an unrelated email-domain text column (used by `DataverseServiceClientImpl` account matching; owner-added 2026-07-14). Dataverse names are per table: no technical clash. See "Judgment calls" |
| `sprk_ruleset` (column) | same | not on either table |
| `sprk_clausereference`, `sprk_clauselocation`, `sprk_consequence`, `sprk_proposalstatus` | dev attributes of `sprk_policyversion`; source; ontology-r1 project notes | not present anywhere |
| `sprk_visibletofirms`, `sprk_notconverted`, `sprk_sourcedocument`, `sprk_scope`, `sprk_firm`, `sprk_effectivefrom`, `sprk_effectiveto` | all new on the new table | not present anywhere |

ontology-r1's own notes (`projects/spaarke-ontology-platform-r1/notes/*`) mention none of the planned names, so there is no sign that ontology-r1 has planned the same column under another name (their review will confirm).

Pre-change custom columns (non-virtual) were: `sprk_policy` - countstowardsuppression, currentversion, enabled, lane, matter, name, policycode, priority, retiredreason, severity, shortname, subjecttype, tenant, worktype; `sprk_policyversion` - authoredby, confidencethreshold, decisionplan, inforcefrom, inforceto, messagetemplate, name, policy, proposedaction, rulebody, ruletype, shortheadline, versionnumber. After the run only the planned columns were added; all of these are still present.

## 2. Overlap check (step 2)

**`sprk_proposedaction` does not carry the meaning of "consequence".** On `sprk_policyversion` it is a required free-text column (nvarchar 400) holding the action the rule puts to a human on a Decide-lane Signal, for example `send-budget-inquiry` (ontology notes `004-seed-policy-rows.md`, `mvp-technical-spec.md`, `schema-draft.md` row "What was put to the human"). `DecisionRecordWriter` copies the offered action labels into the Decision Record's own `sprk_proposedaction`. No code reads it to decide anything. A billing consequence is a closed three-value choice (Reject submission, Flag, Suggest adjustment) that the invoice gate and the lifecycle service branch on (FR-26, I-5), so it must be typed, not free text.

**`sprk_lane` does not carry it either.** It is Decide = 100000000 / Do = 100000001 and selects the evaluation cadence and the re-raise rule (`SignalReevaluationJob`: "Cadence and the re-raise rule both derive from the lane"), and copies onto `sprk_signal.sprk_lane`.

Consequence of the two required/validated existing columns for billing rows (for 070 / 076): a billing `sprk_policyversion` must still supply a non-empty `sprk_proposedaction` (required level), and every enabled policy needs a valid `sprk_lane` or the readers raise a policy failure.

This is a judgment that no escalation trigger fired. It is recorded here so that ontology-r1 can overrule it; if they say "reuse `sprk_proposedaction`", `sprk_consequence` is an empty new column and removing it needs the owner's OK (destructive).

## 3. Reader inventory for task 070 (step 2, trigger 3)

All Signals readers of `sprk_policy` select **by enabled/active state and lane only; none filters by subject type or domain**, so an enabled billing policy (once it exists) is loaded by:

| Reader | Selection | What would happen to an enabled, Active billing policy |
|---|---|---|
| `SignalReevaluationJob.ReadEnabledPoliciesAsync` (nightly pass; `src/server/api/Sprk.Bff.Api/Services/Signals/SignalReevaluationJob.cs` ~line 645) | `sprk_enabled = true` and `statecode = 0`, ordered by code | Loaded and evaluated like an ontology policy. With a billing `sprk_ruletype` the validator gate (`PolicyVersionValidator`, closed set Threshold / Switch / Existence until 070) refuses it as `RuleTypeUnsupported` -> `counts.InvalidPolicies++` -> the whole nightly run is reported as failed every night. Once 070 makes the types known, the nightly pass would raise Signals for billing rules against whatever subject the rule body names, which is not how billing rules run (they are evaluated per invoice by the gate). |
| `SignalReevaluationJob.EvaluateSubjectAsync` (event-trigger pass, Decide lane only) | same `ReadEnabledPoliciesAsync`, then `Lane == Decide` and the rule body's `subject` pre-filter | A billing policy whose body subject is the triggering table would be evaluated by the trigger. A billing policy with another subject is counted `NotApplicable`. |
| `SignalReevaluationJob.ReevaluatePolicyAsync(policyId)` | by id | Used by policy publish (task 101); a billing publish through that path would evaluate the policy as a Signal rule. |
| `PolicyScopeResolver.LoadEnabledPoliciesAsync` | `sprk_enabled = true` and `statecode = 0` | Returned by `ResolveAsync` for any tenant / matter. Grep finds no production caller of `ResolveAsync` outside the DI registration in `SignalsModule`, so no live effect today. |
| `SignalClosureService.PoliciesNotInForceAsync`, `SignalSuppressionService` | all policies with `sprk_enabled = false` or `statecode = 1`; by policy id | Harmless for billing (they close or redact Signals; a billing policy has none). |
| `SignalReadService`, `SignalEvaluator`, `DecisionCommitService`, `DecisionPlanService` | by Signal / policy id or by the record-type catalog | No policy-list read, so no change. |

**What 070 must decide (nothing is changed here):** make the ontology readers skip `sprk_domain = Billing` (add a condition `sprk_domain` not equal Billing, which keeps empty = Ontology) in `ReadEnabledPoliciesAsync` and `PolicyScopeResolver.LoadEnabledPoliciesAsync`, or give the billing evaluator its own reader and accept that the nightly pass ignores billing; add a seeded test that an enabled Billing policy is not evaluated by the nightly pass. Also note that `sprk_policycode` is one namespace for both domains (Signals use it in dedupe keys): give billing policy codes a `BILL-` prefix.

## 4. ontology-r1 review (step 3)

Received 2026-10-15 from the ontology-r1 session:
1. No collisions; nothing planned under these names (`sprk_domain` on `account` / `sprk_organization` is a different table and type).
2. Agreed: `sprk_consequence` is a new typed column; do not reuse `sprk_proposedaction` (still required; billing rows fill it) or `sprk_lane`.
3. Agreed with a change: ontology readers filter on an allowlist (`sprk_domain` null OR Ontology 100000001), not `!= Billing`. Four readers named (SignalReevaluationJob enabled query and retrieve-by-id, PolicyScopeResolver.LoadEnabledPoliciesAsync, SignalClosureService not-in-force query). Two tests. ontology-r1 reviews the 070 PR and wants the target branch named before it opens. Carried into task 070.
4. Agreed: no backfill; null means Ontology.
5. `BILL-` prefix OK.
Additions: (a) the schema sits in our solution (`SpaarkeMaster`, unmanaged), not OntologyPlatformSolution; (b) the Ontology admin tab lists all `sprk_policy` rows, so 070 filters it to the same allowlist; (c) Ontology Administrator holds write on `sprk_policy` and so could change `sprk_domain`. Handled by (b) (billing rows are not shown there) and by the billing write path never changing a policy's domain; a direct Dataverse edit by a privileged user remains possible, which is the same residual as any other column on that table.

## 5. Schema applied (step 5)

Sequence: `-WhatIf` (17 creations, 2 relabels, no write) -> run 1 (created the 17 items, shortened `sprk_name` to 200, relabelled statuscode 1 and 2, published; exit 0) -> run 2 (`created : 0  changed : 0  skipped : 20`, no publish; exit 0). Run 2 was the second run after fixing the empty-option read described under deviations; the run before that failed mid-way with exit 1 and wrote nothing.

### Every numeric value (for 070, 073, 075, 076, 130)

| Item | Value |
|---|---|
| global option set `sprk_policydomain` | Billing 100000000, Ontology 100000001, Docketing 100000002 |
| `sprk_policy.sprk_domain` / `sprk_ruleset.sprk_domain` | the global set above; empty on policy = Ontology |
| `sprk_ruleset.sprk_scope` (local) | Client default 100000000, Firm 100000001 |
| `sprk_ruleset.sprk_visibletofirms` | two-option, default False; Yes = 1, No = 0 |
| `sprk_ruleset` statuscode | Draft = 1 (statecode 0); Published = 659490000 (statecode 0); Retired = 2 (statecode 1) |
| `sprk_policyversion.sprk_consequence` (local) | Reject submission 100000000, Flag 100000001, Suggest adjustment 100000002 |
| `sprk_policyversion.sprk_proposalstatus` (local) | Proposed 100000000, Accepted 100000001, Rejected 100000002 |
| `sprk_policyversion.sprk_ruletype` (unchanged) | Threshold 100000000, Switch 100000001, Existence 100000002 |

Names and sizes: `sprk_ruleset` user-owned, entity set `sprk_rulesets`, primary name `sprk_name` (200); `sprk_notconverted` memo 100,000; `sprk_clausereference` memo 4,000; `sprk_clauselocation` text 200. Date columns are date-only (Format DateOnly, behavior DateOnly). Web API navigation names follow the lookup schema names: `sprk_ruleset` on policy = `sprk_RuleSet`, `sprk_sourcedocument` = `sprk_SourceDocument`, `sprk_firm` = `sprk_Firm`; lookup value properties are `_sprk_ruleset_value`, `_sprk_sourcedocument_value`, `_sprk_firm_value`.

Relationships (all delete = RemoveLink, other cascades NoCascade): `sprk_ruleset_policy_ruleset` (ruleset <- policy), `sprk_document_ruleset_sourcedocument` (document <- ruleset), `sprk_organization_ruleset_firm` (organization <- ruleset).

## 6. Verification (step 6)

- Metadata read-back of all 15 new columns (the script's Verify section, second run): types, global/local option lists, lookup targets, boolean default False, date-only format, lengths, ownership UserOwned, statuscode list. Output in `docs/data-model/sprk_ruleset.md` and the run log.
- Default test: a `sprk_ruleset` row created with only `sprk_name` read `sprk_visibletofirms = false`, `statecode = 0`, `statuscode = 1` (Draft), domain and scope empty. The test row was deleted afterwards (0 rule sets remain).
- Existing rows: all 8 `sprk_policy` rows read the same lane / subject type / statuscode as before, `sprk_domain` and `sprk_ruleset` empty; 9 `sprk_policyversion` rows, none has any new column set; `sprk_ruletype` unchanged.
- Signals unit tests (`dotnet test --filter`): **not run** by this agent: the session had a standing instruction not to run dotnet on `Sprk.Bff.Api` while other agents edit it. No C# and no solution source was changed, so no reader behaviour could change; the main session should run the Signals filter once before commit if it wants the criterion evidenced.

## Judgment calls and deviations

- **`sprk_domain` on `sprk_ruleset` / `sprk_policy` shares its name with the unrelated email-domain column on `account` and `sprk_organization`.** The spec and design.md 5.1 mandate the name, Dataverse allows it, and it is not on the tables this task extends, so the trigger "a planned name already exists with another meaning" was read as meaning the same table. Flagged for the owner in case they want `sprk_policydomain` as the column name instead (cheap to change now, destructive later).
- The statuscode value for Published is 659490000, matching the explicit values task 012 used for `sprk_invoice`.
- `sprk_clausereference` is capped at 4,000 characters (a quoted clause, not a document); `sprk_notconverted` at 100,000 (a JSON list for a whole guideline).
- Script fixes made while testing (both are the eventual-consistency pattern from task 018): a choice column's option list can read back empty right after its create, which made the first re-run fail inside `Add-LocalChoice` (`Compare-Object` on null) and made the first verify print an empty list. `Add-LocalChoice` and the verify step now re-read until the options appear (up to 60 s).
- No defect was found in existing code; the reader behaviour in section 3 is a consequence for task 070, not a bug today (no billing policy exists).

## Message for ontology-r1

> Subject: ebilling-r1 task 019 - additive columns on sprk_policy / sprk_policyversion (FR-09 review request)
>
> Applied to dev 2026-10-15, additive only, no relabel / delete / renumber, no policy row written (empty `sprk_domain` = Ontology):
> - global option set `sprk_policydomain`: Billing 100000000, Ontology 100000001, Docketing 100000002 (reserved)
> - `sprk_policy`: `sprk_domain` (that option set), `sprk_ruleset` (lookup -> new table `sprk_ruleset`, RemoveLink)
> - `sprk_policyversion`: `sprk_clausereference` (memo 4000), `sprk_clauselocation` (text 200), `sprk_consequence` (Reject submission / Flag / Suggest adjustment), `sprk_proposalstatus` (Proposed / Accepted / Rejected; empty on your rows)
> - new `sprk_ruleset` table (user-owned): domain, source document, scope (Client default / Firm), firm, visible-to-firms (default No), effective from / to, not-converted JSON, statuscode Draft / Published / Retired.
>
> Please confirm: (1) none of these names is planned by you under another name; (2) `sprk_consequence` is a new typed column and should not reuse `sprk_proposedaction` (free text, required, "what is put to the human") or `sprk_lane` (Decide / Do cadence); (3) your readers (`SignalReevaluationJob.ReadEnabledPoliciesAsync`, `PolicyScopeResolver.LoadEnabledPoliciesAsync`) may skip `sprk_domain = Billing` in task 070, which is a change in your code, so we ask you to review that PR; (4) no backfill of the existing rows is wanted.
>
> Evidence: `projects/ebilling-r1/notes/019-policy-store-ruleset.md`, `docs/data-model/sprk_ruleset.md`.

## 7. Closure (main session, 2026-10-15)

ontology-r1 review received and applied to task 070 (bb7fc112c). Signals tests with no code change: `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter FullyQualifiedName~Signals` 1622 passed, 0 failed (covers tests/unit/domain/Signals and tests/integration/seam/Signals). All acceptance criteria met.
