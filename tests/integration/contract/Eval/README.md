# Golden-Utterance Eval Suite (`tests/integration/contract/Eval/`)

> **Origin**: `spaarke-ai-architecture-redesign-r1` task 011 (FR-P0-09)
> **Category authority**: [ADR-038](../../../../docs/adr/ADR-038-testing-strategy.md) — `tests/integration/contract/**` is a KEEP path
> **Governing requirements**: spec NFR-02 (merge gate from P1), NFR-06 (schema-conformance + citation-integrity assertions), FR-P2-08 (refusal/compound/prompt-injection families)

## What this is

The quality spine of the AI architecture redesign. Every case is a golden utterance:

```
{ utterance, §3 UC id, expected capability binding, expected outcome class, optional output assertions }
```

- **Seed data**: [`golden-utterances.json`](golden-utterances.json) — 59 cases across 21 families, each traceable to a §3 UC trigger in [`SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../../../../docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md)
- **Harness**: [`GoldenUtteranceEvalSuiteTests.cs`](GoldenUtteranceEvalSuiteTests.cs) (inventory integrity + P0/P1 live assertions) and [`P2LoopInjectionEvalSuiteTests.cs`](P2LoopInjectionEvalSuiteTests.cs) (P2 live assertions: full-catalog coverage, injection resilience, compound, budget, activation guard — task 037 / FR-P2-08) — compiled into `Sprk.Bff.Api.Tests` via the contract-path `Compile` glob; trait-filterable as `Category=GoldenUtteranceEval`

## Case schema

| Field | Meaning |
|---|---|
| `caseId` | Stable unique id (`GU-###`) |
| `family` | Capability family for grouping/reporting (`chat-summarize`, `refusal`, ...) |
| `ucId` | §3 UC trigger id (traceability; validated against the canonical closed set) |
| `channel` | `text` \| `click` \| `event` — the ONLY three invocation routes (redesign constraint) |
| `utterance` | The utterance; for click/event channels, the affordance/event descriptor |
| `context` | `{ surface, sessionHasDocument, recordType }` — dispatch reads session context, not just words (§3.0) |
| `expected.outcomeClass` | `dispatch` \| `clarify` \| `refuse` |
| `expected.consumerType` | Expected Binding key. `catalogStatus: existing` types are validated against `ConsumerTypes.All` at build time; `planned` types MUST cite `plannedBy` (the FR introducing them) — no invented capability names |
| `assertions.schemaConformance` | Output schema id (e.g. `SUM-CHAT@v1`) — asserted from P1/P2 (NFR-06) |
| `assertions.citationIntegrity` | Grounded-citation check — asserted from P2 (NFR-06) |
| `activation` | `{ dispatchAssertPhase: P1\|P2\|P3, activatedBy }` — pending-by-design declaration; never a silent skip |

## Adding a case (BA workflow — no code)

1. Edit `golden-utterances.json` only. Copy a sibling case in the same family; give it a fresh `caseId`.
2. Trace it: set `ucId` to the §3 trigger it derives from.
3. If it targets a capability that exists today, its `consumerType` must appear in `ConsumerTypes.All` (`src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs`); otherwise mark `catalogStatus: "planned"` and cite the FR.
4. Open the PR — CI validates inventory integrity automatically (NFR-06: every catalog/prompt change adds-or-updates eval cases).

## What runs at each phase

| Phase | Active assertions |
|---|---|
| **P0** | Inventory integrity (≥30 cases, unique ids, UC traceability, closed vocabularies); consumer-type grounding against `ConsumerTypes.All`; NFR-06 schema round-trip; routing-surface smoke driving the real `ConsumerRoutingService.ResolveBindingAsync` selection algorithm (Dataverse boundary stubbed); pending-inventory declaration |
| **P1 (ACTIVE — task 026, FR-P1-07)** | UC-A-1 families LIVE: Text-path cases resolve `chat-summarize` → Prompted SUM-CHAT@v1 Action through the real `ResolveBindingAsync` (the exact read + preconditions the summarize dispatch path enforces); Event-path cases resolve `document_uploaded` ordered members chat-classify(1) → chat-summarize(2) through the real `ResolveEventBindingsAsync`; M4 clarify policy dial pinned (behavior proven in `EventRulesServiceTests`); SUM-CHAT@v1 output-schema contract pinned (`infra/dataverse/outputschemas/sum-chat-v1.schema.json` — required fields + load-bearing declaration order). **Merge gate ACTIVE** (below). NOTE: typo-tolerant NL matching ("any phrasing of summarize") is a bounded-loop property — it activates at P2; at P1 the utterances are the traceability record and the dispatch route is the assertion. |
| **P2 (ACTIVE — task 037, FR-P2-08)** | Full-catalog coverage GENERATED from `ConsumerTypes.All` (every closed-catalog member has an eval family; the 11 namespaced `sprk_analysistool` seed rows must declare the gate contract — write tools declare `Write`); loop projection of text-projectable Bindings via the real `ListTextProjectableBindingsAsync` + `AgentToolProjection.Finalize` (NFR-04 fingerprint stability); **prompt-injection resilience (NFR-03)** — write-declared tool invocations SUSPEND into the real unified gate (`SideEffectGateAIFunction` → `PendingPlanManager`; task 037 landed this loop-boundary gate after the injection family exposed that no production code called `RequiresConfirmation` post-034-cutover), embedded approval text/args cannot bypass, the gate fails CLOSED, amplification bounded by the per-turn budget (NFR-09, default 8 pinned), hostile free text never reaches the ToolChain ledger (NFR-07); compound loop-native composition (ordered ToolChain, shared budget, drain-before-render); citation-integrity enforcement + deterministic repair (NFR-06); clarify/elicitation determinism (declared-schema validation, closed answer-vs-escape vocabulary); P2 activation guard (no new P2 case without a live selector); NFR-08 deadwood guard (no case references deleted surfaces) |
| **P3** (FR-P3-01/02/03) | Remaining consumer families: document-profile, matter/project pre-fill, workspace summarize-file, email-analysis, insights ask/search, draft-correspondence, create-task. **draft-correspondence surface LIVE since task 041 (FR-P3-02)**: Binding projection through the real `ListTextProjectableBindingsAsync` → `capability_draft-correspondence` (DRAFT-CORR@v1 schema pinned at `infra/dataverse/outputschemas/draft-corr-v1.schema.json`); the `email.draft` seed row declares `Communicate` and its invocation live-suspends into the ONE gate (`DraftCorrespondence_CommunicateDeclared*` in the P2 harness file); DRAFT-ONLY invariant unit-proven in `EmailDraftToolHandlerTests` (statuscode server-pinned to Draft). **create-task surface LIVE since task 042 (FR-P3-03)**: Binding projection → `capability_create-task` (CREATE-TASK@v1 schema pinned at `infra/dataverse/outputschemas/create-task-v1.schema.json`; elicitation contract `due_date`+`assign_to` pinned from the Action's declared `sprk_inputschema`); the **typed-handler confirm-RESUME seam is LIVE** (`TypedHandlerResumeExecutor` via `POST /gates/{gateId}/resolve`) — the `CreateTask_ConfirmedWriteInvocation_*` fact drives suspend → confirm → REAL `DataverseCreateRecordHandler` execution with the created record carrying source-document + source-analysis ledger refs, gate closed `confirmed`, SessionOutput `loop@t{n}` + ToolChain written before render (GU-051/052 suspension facts unchanged; GU-057's email.draft confirm leg activates on the same seam). NL-loop dispatch assertions activate at the G-P3 gate (task 048). |

## CI wiring and the merge gate (ACTIVE since task 026)

**Pass 1 (informational)**: this suite compiles into `Sprk.Bff.Api.Tests` (member of `Spaarke.sln`) and runs inside the root `dotnet test` of `.github/workflows/sdap-ci.yml` (`build-test` job, test pass 1) on every PR. The seed JSON is copied to test output via a `Content` include in `Sprk.Bff.Api.Tests.csproj`; `*.json` edits under `tests/**` are NOT in the workflow's `paths-ignore`, so BA-only case edits trigger a CI run.

**Merge gate (blocking — NFR-02)**: the dedicated `eval-gate` job in `sdap-ci.yml` runs

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj -c Debug --filter "Category=GoldenUtteranceEval"
```

with **no `continue-on-error`**. Placement rationale (deviation from the original task-011 sketch, which put a step inside `build-test`): the `build-test` job carries job-level `continue-on-error: true` (2026-06-24 informational posture), which swallows any step failure inside it — and branch protection is currently disabled on the repo, so the workflow-run conclusion is the only mechanical merge signal. A separate no-tolerance job is the only additive change that turns a red eval into a red workflow run. When branch protection / rulesets are re-enabled, mark **"Eval Gate (Golden Utterances)"** as a REQUIRED status check to hard-block merge.

Verified 2026-07-05 (task 026): a deliberately failing scratch case made `dotnet test --filter "Category=GoldenUtteranceEval"` exit 1; the scratch case was then removed.

## Resourcefulness eval family (D-F0(e) — r2 task 031 / FR-A1-02)

A **net-new** family joined to the SAME merge gate (via the `Category=GoldenUtteranceEval` trait — no CI-YAML change). It is the **enforcement forcing-function** for the D-F0 resourcefulness doctrine (r2 task 030): D-F0 is enforced by prompt+eval, not the gate engine, so this family IS the gate. It scores **partial-value delivery AND honesty together** — a case can be passed neither by inventing an outcome nor by refusing safely when help was possible.

- **Seed data**: [`resourcefulness-eval-family.json`](resourcefulness-eval-family.json) — 23 cases across the **5-family taxonomy** (`blocked-write` ×5, `partial-capability` ×5, `read-hesitancy` ×4, `absence-claim` ×4, `fabrication-counter` ×5), each anchored in real R1 evidence (H6, H7, R4-3, R5-E, R5-C, R3-4, R2-D). Distinct schema from the golden cases: a per-case rubric (`no_fabrication` / `verified_first` / `partial_value_delivered` / `affordance_present` / `no_unneeded_confirm`) + per-family applicability (spec §2.1).
- **Rubric thresholds** (declared as concrete integers in the JSON): `no_fabrication` = **100%, NON-adjustable, GATE-CRITICAL**; the four family dimensions = **90%**, operator-tunable upward.
- **Harness**: [`ResourcefulnessEvalSuiteTests.cs`](ResourcefulnessEvalSuiteTests.cs) + the mechanical [`ResourcefulnessFabricationOracle.cs`](ResourcefulnessFabricationOracle.cs).
- **Mechanical (CI, no live model)**: inventory integrity + per-family floors, ratified thresholds, the **fabrication oracle** — every claimed side effect is cross-checked against the ADR-040 ledger `ToolChain`; a claim with no backing tool event auto-fails `no_fabrication` (RF-018 H6 record, RF-019 R4-3 url, RF-020 R2-D ui-action, RF-021 GU-051 suspended-write all go RED; RF-022 honest reference stays GREEN). The **partial-value negative** (RF-023 passive refusal) goes RED. Net-new-coverage dedupe vs the golden suite.
- **Live eval-gate (LLM-judge)**: the 17 `llm-judge` cases' expected-behavior anchors score `verified_first` / `partial_value_delivered` / `affordance_present` / `no_unneeded_confirm`. The mechanical-vs-judge boundary is surfaced (never silently trusted) by `JudgeScoredDimensions_AreSurfacedWithMechanicalCoverageBoundary`.
- **E2E band (§4)**: 10 operator browser-UAT scenarios on spaarkedev1 backing G-R2-A/B — authored at [`projects/spaarke-ai-architecture-redesign-r2/notes/d-f0-resourcefulness-e2e-band-and-thresholds.md`](../../../../projects/spaarke-ai-architecture-redesign-r2/notes/d-f0-resourcefulness-e2e-band-and-thresholds.md) as input to the G-R2-A UAT (task 049); NOT automated (r1 browser rule).

## Origin-classification eval family (Policy v2 E-1..E-6 — r2 task 033 / FR-A1-04)

A **net-new** family joined to the SAME merge gate (via the `Category=GoldenUtteranceEval` trait — no CI-YAML change). Generated from the RULED E-1..E-6 rows in [`notes/policy-v2-origin-classification-decision-tree.md`](../../../../projects/spaarke-ai-architecture-redesign-r2/notes/policy-v2-origin-classification-decision-tree.md) §4 (design.md D-F1 adopted them). Unlike the resourcefulness family, the request-origin classifier (`RequestOriginClassifier`, task 032) and the Confirmation Policy v2 engine (`ConfirmationPolicyEngine` / `GateDecisionProjector`) are **deterministic production code**, so every case is a **hard-equality assertion against the real production types** — never an LLM judge.

- **Seed data**: [`origin-classification-eval-family.json`](origin-classification-eval-family.json) — 12 cases across the **6 ruled rows** (E-1..E-6), one positive + one negative case each. Each case declares the structural `GateOriginRequest` input, the `PolicyEvaluationContext` overlay/risk inputs, and the expected `{origin, outcome, decisive overlay}` triple.
- **Harness**: [`OriginClassificationEvalSuiteTests.cs`](OriginClassificationEvalSuiteTests.cs) — deserializes each case into the production `GateOriginRequest` / `PolicyEvaluationContext` types and asserts `RequestOriginClassifier.Classify(...)` + `ConfirmationPolicyEngine.Evaluate(...)` produce the ruled origin and gate outcome by hard equality.
- **Not-vacuous guard**: `EveryEdgeCase_PositiveAndNegativeCases_ResolveToDifferentClassifierOrGateResults` asserts each row's pos/neg pair resolves to a DIFFERENT `(origin, outcome, overlay)` result, so a negative case can never be trivially green.
- **Perturbation-verified** (2026-07-09, task 033): the E-1 negative case (`E1Negative_AffirmationAfterTwoProposals_ClassifiesInferred_NeverExplicit`) and the E-6 negative case (`E6Negative_DispatchUncertainOverExplicitRequest_ForcesDialogWithSuspicion_NeverAutoExecutes`) were each hand-verified to go RED against a deliberately perturbed production classifier/engine, then the perturbation was reverted (no net diff) — confirming the family is not a vacuous pass.
- Net-new-coverage dedupe vs the golden + resourcefulness suites (`OC-` case-id namespace; `e1`..`e6` family keys).

## Eval-suite-green merge gate — full family scope (r2 task 071 / FR-D-02, NFR-02)

The `eval-gate` job in `sdap-ci.yml` gates on the `Category=GoldenUtteranceEval` trait, which now spans **every** in-scope R2 family: the golden-utterance suite + P2 loop/injection, the D-F0(e) Resourcefulness family (task 031), the Origin-classification family (task 033), the ContextEnvelope budget-breach-fails-eval check (task 054, FR-B-05), and the memory-write capture→recall family (task 057). Any single family red fails the merge — no family is exempt, and no second gate mechanism exists.

**Memory-poisoning eval families are explicitly OUT OF SCOPE and MUST NOT be added to this gate.** They are DEFERRED to the separate memory-governance project per spec FR-B-10 (`projects/spaarke-ai-architecture-redesign-r2/spec.md` item 38 + the Memory hard-governance rules deferral). This is a deliberate scope boundary, not an oversight — do not add a `[Trait("Category", "GoldenUtteranceEval")]` memory-poisoning case here until the governance project ships one.

## Assistant-Enhancements-R1 eval family (task 051 / NFR-06)

A **net-new** family joined to the SAME merge gate (via the `Category=GoldenUtteranceEval` trait — no CI-YAML change), authored by `spaarkeai-assistant-enhancements-r1` task 051. It is the operational eval coverage every R1 catalog change owed the suite (`projects/spaarkeai-assistant-enhancements-r1/notes/owed-eval-cases.md`) PLUS the two R1-specific proofs the golden-utterance schema does not model. It deliberately does **not** touch the shared `golden-utterances.json` (whose closed §3-UC set + P1/P2 activation guards are owned by the ai-architecture-redesign project) — a net-new family is the established way a project adds gate coverage.

- **Seed data**: [`assistant-r1-eval-cases.json`](assistant-r1-eval-cases.json) — 20 cases across 6 families (`create-todo`, `create-project`, `list-tasks`, `view-vs-create`, `profile-injection`, `incoherent-combo`). Distinct schema from the golden cases (`AR1-###` ids; `catalogStatus` = existing | mirrored | live-catalog).
- **Harness**: [`AssistantEnhancementsR1EvalTests.cs`](AssistantEnhancementsR1EvalTests.cs) — 6 mechanical facts (CI, no live model):
  - inventory integrity + per-catalog-change coverage floors (AC1);
  - **honest catalog grounding** — `existing` ⇒ a `ConsumerTypes` constant; `mirrored` ⇒ a row in `infra/dataverse/sprk_playbookconsumer-rows.json`; `live-catalog` ⇒ seeded on spaarkedev1 with mirror/constant parity pending (create-todo / create-project — surface-launch capabilities with no server-side `ConsumerTypes` dependency; cite `seededBy`);
  - the **list-tasks** Binding row grounded against the real mirror: `surface_launch` disposition (100000007) + the authored **VIEW-vs-CREATE** cue naming create-task/create-todo (ADR-039 ambiguity-in-descriptions, not a classifier);
  - **FR-E4 profile-injection non-flip** proven OPERATIONALLY in-gate over the R1 capability set (`AgentToolProjection.PreFilter` — a profiled vs. unprofiled caller yields a byte-identical grounded set). Structural sibling (unit, outside the gate): `PreferenceNotPermissionInvariantTests` (task 031);
  - **AC3 incoherent practice-area × matter-type cannot commit** — CREATE-MATTER@v1 emits `practice_area_suggestion` + `matter_type_suggestion` as INDEPENDENT string LABELS (never enum/const/GUID), `additionalProperties:false`, allowstools=false, so no resolved closed-set value is ever model-emitted (each ref resolved deterministically per-field downstream — task 010 resolver).
- **Not-vacuous**: the list-tasks + incoherent-combo facts carry discriminating assertions (disposition value, cue contents, independent-labels guard); the profile fact asserts an exact grounded set.

## Assistant-Enhancements-R4 eval family (ACTIVE — task 013, gated)

`spaarkeai-assistant-enhancements-r4` task 001 (FR-10 infra / P5) seeded [`assistant-r4-eval-cases.json`](assistant-r4-eval-cases.json) with a template case; task 013 (FR-10 E1) authored the harness [`AssistantEnhancementsR4EvalTests.cs`](AssistantEnhancementsR4EvalTests.cs) (same pattern as `AssistantEnhancementsR1EvalTests.cs`: `[Trait("Category", "GoldenUtteranceEval")]`, inventory integrity + honest catalog grounding + not-vacuous structural facts) and joined the merge gate with zero CI-YAML change. E2 (task 024) + E3 (task 033) extend the SAME file + harness. Convention + paper-trail-to-register detail: `projects/spaarkeai-assistant-enhancements-r4/notes/behavior-gap-register.md` ("Eval-case harness convention" section).

**Cases** (`AR4-###`): AR4-001/002/003 (E1 task-agenda-advisory — the "today"/"plate"/"prioritize" phrasings, each grounded to the advisory `list-tasks` capability), AR4-020 (E3 preference-loop). **Structural facts**: E1 grounds the advisory tier (`ListTasksAction_DeclaresAdvisoryGroundedRecommendTier_NotAckOnly`, `ListTasksBinding_DeclaresSurfaceLaunch`, `AdvisoryGroundedTools_ExistInCatalog_AndAssertGroundingAndObo`); E3 grounds the bounded preference loop (`PreferenceLoop_BiasesARealCataloguedCapability_ConfirmedOnly_OffAllowListInert`).

### E2 (task 024) FR-04 / FR-06 coverage map (FR-10)

The E2 behaviors' regression guards were authored WITH their features (the ADR-038-preferred pattern — tests land in the feature PR, not a separate task), so task 024 does NOT duplicate them here (duplicate coverage is build-class, deleted at `/test-diet`). It adds the one guard the golden-utterance gate genuinely owed — the FR-04 no-dead-end *contract* anchor — and documents the full map:

| Behavior | Acceptance | Guarded by | A regression that fails it |
|---|---|---|---|
| **FR-04** — "prioritize my tasks" maps to a real capability (no dead-end dispatch) | golden case | **AR4-003** (this family) | routing "prioritize" to a phantom / non-mirror consumerType |
| **FR-04** — the follow-on suggester cannot form a dead-end (closed-candidate, typed two-kind, free-string generator retired) | in-gate structural fact | **`SuggestFollowupsAction_IsGroundedTypedTwoKindProposer_NoDeadEndFreeString`** (this family, task 024) | reverting to the ungrounded free-string generator / dropping the closed-candidate or typed-kind contract |
| **FR-04** — the service drops an off-catalog capability suggestion; typed kinds parse | BFF unit | `AssistantSuggestionServiceTests.SuggestForConversationAsync_DropsCapabilityWhoseBindingIdIsOffCatalog_KeepsQuestion` (+ siblings) — task 021a | the closed-catalog guard stops dropping hallucinated ids |
| **FR-04** — the client renders only typed/backed items; untyped/unbacked dropped | client unit | `useSseStream.suggestions.test.ts`, `SprkChatSuggestions.test.tsx`, `suggestionsIntegration.test.tsx` — task 021b | rendering a bare untyped string / a capability chip with no binding |
| **FR-06** — Briefing / Smart To Do cards suppressed when the tab is open, shown + launch when closed | client unit | `agendaFollowOnCards.test.tsx` — task 023 | an ungated card (that would open a duplicate tab) |

**FR-06 is a pure client UX behavior with no BFF dispatch surface** — it correctly has NO golden-utterance-gate home; forcing a BFF fact for it would be mis-shaped. It is guarded entirely by the client suite (023).

**Tracked residual** (behavior-gap register / `notes/defer-issues.md`): there is no end-to-end contract test asserting the `ChatEndpoints` SSE `suggestions` event is emitted in the TYPED shape (vs the retired free-string) — that path requires the live-agent streaming harness the deterministic eval suite deliberately avoids; the typed shape is guarded structurally at the Action-contract (this family) + service (021a) + client-parse (021b) layers instead.

## Deletion-safety

KEEP-protected per ADR-038 (`tests/integration/contract/**`). Since P1 (task 026) the suite is an ACTIVE merge gate (NFR-02); every catalog/prompt change adds or updates cases (NFR-06).
