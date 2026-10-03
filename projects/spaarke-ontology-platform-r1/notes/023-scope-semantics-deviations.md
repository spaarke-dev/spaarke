# Task 023 — Scope semantics and policy defaults: deviations from the POML

> Per POML step 7 (`projects/spaarke-ontology-platform-r1/tasks/023-scope-semantics-and-policy-defaults.poml`).

## 1. Test location: `tests/integration/seam/Signals/` instead of `tests/unit/Sprk.Bff.Api.Tests/Services/Signals`

The POML's `<outputs>` named `tests/unit/Sprk.Bff.Api.Tests/Services/Signals`. The actual test file is at
`tests/integration/seam/Signals/PolicyScopeResolverSeamTests.cs`.

**Reason**: the canonical reference, `CommunicationRuleGate`, has NO file under
`tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationRuleGateTests.cs`. Its only test is
`tests/integration/seam/Communication/CommunicationRuleGateSeamTests.cs`, whose own doc comment states the
placement rationale: *"the gate composes a table read (boundary) + branchy evaluation, so a seam test over
real rule rows through the boundary [is the right KEEP category]."* `PolicyScopeResolver` has the identical
shape (a Dataverse table read + branchy scope-match/ordering evaluation), so the same ADR-038 §2 "seam"
KEEP-path rationale applies, and I followed the precedent rather than the POML's literal path. `<steps
mode="directional">` permits this (goal + acceptance-criteria + constraints bind; the step sequence is
adaptable). All 7 acceptance criteria are covered by this file; nothing is untested.

## 2. Added DI wiring not explicitly named in the POML

The POML's steps describe the component and its tests but do not mention DI registration. I added:
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SignalsModule.cs` (new `AddSignalsModule()` extension,
  registers `PolicyScopeResolver` as a singleton)
- One line + comment in `Program.cs`: `builder.Services.AddSignalsModule();`

**Reason**: per ADR-010 (DI minimalism / feature-module pattern) and the precedent set by
`CommunicationModule.AddCommunicationModule()` registering `CommunicationRuleGate` in the SAME task that
created it. Leaving `PolicyScopeResolver` unregistered would make it dead code until task 030/031 land and
would violate the feature-module DI convention `.claude/constraints/bff-extensions.md` §A.5 requires. This is
new surface (a brand-new `Services/Signals/` namespace and a brand-new `SignalsModule.cs`), so the §11
three-question justification applies: **Existing** — no Signal/Policy DI module existed before this task;
**Extension** — nothing to extend; **Cost of doing nothing** — `PolicyScopeResolver` would be unreachable via
DI, so task 030/031 would either have to add this exact module themselves (duplicating the decision) or
`new PolicyScopeResolver(...)` ad hoc, bypassing the convention every other feature module follows.

## 3. Returns an ORDERED LIST of matches, not a single `FirstOrDefault()` winner

`CommunicationRuleGate.EvaluateAsync` picks ONE matched rule (`.FirstOrDefault()` after ordering) because
comms rules are mutually-exclusive alternatives for a single gate decision. `PolicyScopeResolver.ResolveAsync`
returns the full ordered list (`PolicyScopeResolution.MatchedPolicies`) because `sprk_policy` rows are
independent, non-exclusive predicates — design.md confirms multiple distinct policies legitimately apply to
the same matter simultaneously (e.g. a Decide-lane fee/budget policy and a Do-lane overdue-task policy are
unrelated and both fire). The FR-09 constraint text ("copy scope semantics verbatim") and all six acceptance
criteria concern tenant/matter matching, ordering and fail-closed behavior — none require collapsing to a
single winner, and the acceptance criteria themselves talk about "two policies" and ties, implying a list.
Tenant blank-means-all, matter empty-means-all, `OrderBy(priority ?? 500).ThenBy(Id)`, and the fail-closed
contract are copied verbatim and unchanged by this difference.

## 4. No `IPolicyEvaluator` interface

`PolicyScopeResolver` is a concrete class (ADR-010), per design.md §3.2's explicit note that introducing an
`IPolicyEvaluator` contract "waits for a second consumer." Task 021 (predicate compiler) and task 030 (Signal
writer) are the anticipated future consumers; neither has landed. Revisit if/when a second genuinely distinct
implementation is needed.

## 5. Escalation trigger — considered, not fired

The POML's `<escalation>` trigger concerns whether fail-closed should abort a WHOLE nightly-evaluator run on a
transient read failure, versus only the affected policy/matter. This task does not implement the nightly
evaluator (task 031, not yet landed) — it implements the primitive that evaluator will call. I deliberately
did NOT decide the run-granularity question here: `PolicyScopeResolution.ReadFailed` surfaces the failure as a
distinct, inspectable outcome (never collapsed into an empty-but-successful result), and the XML doc on
`PolicyScopeResolver` explicitly flags that the granularity decision belongs to the caller (task 031/032).
This is a deferral to the correct future owner, not a silent softening of fail-closed into fail-open, so I did
not treat it as warranting escalation now — there is no run to abort or not-abort yet. Whoever implements task
031 should re-read this note before deciding that question.

## 6. Empirical schema verification (ADR-038 pairing rule)

Per ADR-038 ("any test asserting a Dataverse column list, entity name or option-set value must be paired with
something touching the real schema"), before writing the defaults logic I created a throwaway `sprk_policy`
probe row via the Dataverse MCP against `spaarkedev1` (id `1a092e80-58bf-f111-aaaf-0022482913fc`, immediately
deleted after reading it back) with `sprk_enabled` and `sprk_priority` both omitted. Result: `sprk_enabled`
persisted as `false` (confirms the Dataverse-level default is No, per FR-10) and `sprk_priority` persisted as
`NULL` (confirms there is NO Dataverse-level default for priority — the 500 fallback is correctly an
application-level concern in `PolicyScopeResolver`, exactly mirroring `CommunicationRuleGate`'s own `?? 500`
pattern rather than relying on an assumed Dataverse default that does not exist).

## 7. Concurrent task 020 in the same worktree

Task 020 (`Existence` rule type) is running concurrently in this same worktree and also writes to
`Services/Signals/` (`RuleType.cs`, `RuleBodySchemaValidator.cs`, `Schemas/existence-rule.schema.json`) — no
file-level overlap with `PolicyScopeResolver.cs`. I did touch the shared `Program.cs` (one new line +
comment) and created `Infrastructure/DI/SignalsModule.cs`; sent a heads-up via SendMessage to the task-020
agent so it reuses `SignalsModule.AddSignalsModule()` for any DI it needs rather than creating a competing
registration.
