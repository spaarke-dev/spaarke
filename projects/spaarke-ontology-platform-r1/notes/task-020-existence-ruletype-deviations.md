# Task 020 (`Existence` rule type + JSON Schema) — deviations from the POML, recorded per step 7

> spec FR-05 / FR-08; design.md CM-7 and section 8.0.1(a).

## 1. The "canonical-reference" file does not exist at the path the POML names, and is not actually about rule types

The POML's `<relevant-files>` cites `src/server/api/Sprk.Bff.Api/Services/Communications/CommunicationRuleGate.cs`
(plural `Communications`). The real path is `Services/Communication/CommunicationRuleGate.cs` (singular). More
importantly, having read it in full: it is a confidence-threshold gate over a *different* table
(`sprk_communicationrule` — tenant/matter scope + confidence threshold), and contains **no rule-type
enumeration or JSON-Schema validation of any kind**. It is relevant to task 023 (FR-09: "copy
`CommunicationRuleGate` scope semantics verbatim"), not to this task. I read it, confirmed it has nothing to
mirror for a closed rule-type/schema set, and did not base anything in this task's design on it beyond
confirming it is NOT the pattern to copy here.

## 2. The POML's own `<justification>` ("Threshold and Switch exist in the same closed set with the same
validation path") is inaccurate — nothing existed in code

Searched the whole repo (`.cs` files) for `RuleType`, `ThresholdRule`, `SwitchRule`, `RuleBodyValidator`,
`sprk_ruletype` — zero matches before this task. `Services/Finance/SignalEvaluationService.cs` has an
unrelated `ISignalRule`/"Threshold"-named concept (`sprk_spendsignal`/`sprk_spendsnapshot`), which project
CLAUDE.md §3.4 already flags as a trap ("its XML doc claims a strategy pattern the code does not deliver") —
it is not, and was not treated as, prior art for `sprk_policyversion.sprk_ruletype`.

This task is therefore the **first** code to establish the `RuleType` enum and any rule-body schema
validation for the ontology policy model. I built the full closed-set mechanism from design.md section
8.0.1(a) / `notes/mvp-technical-spec.md` §3.3–3.5, which already fully specify the Threshold, Switch and
Existence body shapes and the "validated per rule type, invalid body cannot be saved" contract — nothing was
invented.

## 3. Threshold and Switch get enum membership and Dataverse-value parity, but no JSON Schema in this task

`RuleType` has all three members (`Threshold = 100000000`, `Switch = 100000001`, `Existence = 100000002`,
confirmed against live Dataverse via `DESCRIBE TABLE sprk_policyversion`). `RuleBodySchemaValidator` dispatches
on a closed `switch`; for `Threshold`/`Switch` it throws `NotSupportedException` with an explicit message
rather than silently accepting an unvalidated body. Scoped this way because:

- The POML's own `<outputs>` names only `existence-rule.schema.json` as new.
- No `sprk_policyversion` row of type `Threshold` or `Switch` exists yet (seeding is task 004/005, not yet
  run), so there is no live body to validate against and no acceptance criterion requires it.
- §11's three-question justification test does not support authoring two more schemas "for completeness"
  with no concrete failure mode they fix today.

This is a known, intentional gap for whichever future task first seeds a `Threshold` or `Switch` row —
`RuleBodySchemaValidator.ResolveSchema` throwing loudly (rather than falling through to a default) is the
guard that makes the gap visible instead of silent.

## 4. Package / placement — no new BFF surface beyond the files listed

No new endpoint, DI registration, package reference, or background work. `JsonSchema.Net` (`Json.Schema`
namespace) is already a direct `Sprk.Bff.Api.csproj` dependency (pinned 7.3.4, used by
`AnalysisToolService.MapJsonSchema`) — reused as-is, no version bump, no new package. Publish-size impact is
therefore limited to one new ~3 KB embedded JSON resource plus two small `.cs` files; not independently
measured against a fresh master build given the near-zero expected delta (see main report for the
reasoning; a from-scratch fresh-master-vs-branch measurement was judged disproportionate for a change this
small, and is flagged in the final report rather than silently skipped).

## 5. Test location follows the POML's explicit (legacy) path, not the aspirational `tests/unit/domain/**` reorg target

`tests/CLAUDE.md` describes a target structure of `tests/unit/domain/**` for pure domain logic. The POML's
own `<outputs>` names `tests/unit/Sprk.Bff.Api.Tests/Services/Signals` explicitly, and every existing sibling
service (`Services/Finance`, `Services/Communication`, etc.) still lives at that legacy path — the reorg has
not happened repo-wide. Followed the POML + existing convention rather than unilaterally relocating a single
new test folder to a structure no other service test uses yet.
