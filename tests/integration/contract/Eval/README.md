# Golden-Utterance Eval Suite (`tests/integration/contract/Eval/`)

> **Origin**: `spaarke-ai-architecture-redesign-r1` task 011 (FR-P0-09)
> **Category authority**: [ADR-038](../../../../docs/adr/ADR-038-testing-strategy.md) — `tests/integration/contract/**` is a KEEP path
> **Governing requirements**: spec NFR-02 (merge gate from P1), NFR-06 (schema-conformance + citation-integrity assertions), FR-P2-08 (refusal/compound/prompt-injection families)

## What this is

The quality spine of the AI architecture redesign. Every case is a golden utterance:

```
{ utterance, §3 UC id, expected capability binding, expected outcome class, optional output assertions }
```

- **Seed data**: [`golden-utterances.json`](golden-utterances.json) — 57 cases across 21 families, each traceable to a §3 UC trigger in [`SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../../../../docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md)
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
| **P1 (ACTIVE — task 026, FR-P1-07)** | UC-A-1 families LIVE: Text-path cases resolve `chat-summarize` → Prompted SUM-CHAT@v1 Action through the real `ResolveBindingAsync` (the exact read + preconditions `SessionSummarizeOrchestrator` enforces); Event-path cases resolve `document_uploaded` ordered members chat-classify(1) → chat-summarize(2) through the real `ResolveEventBindingsAsync`; M4 clarify policy dial pinned (behavior proven in `EventRulesServiceTests`); SUM-CHAT@v1 output-schema contract pinned (`infra/dataverse/outputschemas/sum-chat-v1.schema.json` — required fields + load-bearing declaration order). **Merge gate ACTIVE** (below). NOTE: typo-tolerant NL matching ("any phrasing of summarize") is a bounded-loop property — it activates at P2; at P1 the utterances are the traceability record and the dispatch route is the assertion. |
| **P2 (ACTIVE — task 037, FR-P2-08)** | Full-catalog coverage GENERATED from `ConsumerTypes.All` (every closed-catalog member has an eval family; the 11 namespaced `sprk_analysistool` seed rows must declare the gate contract — write tools declare `Write`); loop projection of text-projectable Bindings via the real `ListTextProjectableBindingsAsync` + `AgentToolProjection.Finalize` (NFR-04 fingerprint stability); **prompt-injection resilience (NFR-03)** — write-declared tool invocations SUSPEND into the real unified gate (`SideEffectGateAIFunction` → `PendingPlanManager`; task 037 landed this loop-boundary gate after the injection family exposed that no production code called `RequiresConfirmation` post-034-cutover), embedded approval text/args cannot bypass, the gate fails CLOSED, amplification bounded by the per-turn budget (NFR-09, default 8 pinned), hostile free text never reaches the ToolChain ledger (NFR-07); compound loop-native composition (ordered ToolChain, shared budget, drain-before-render); citation-integrity enforcement + deterministic repair (NFR-06); clarify/elicitation determinism (declared-schema validation, closed answer-vs-escape vocabulary); P2 activation guard (no new P2 case without a live selector); NFR-08 deadwood guard (no case references deleted surfaces) |
| **P3** (FR-P3-01/02/03) | Remaining consumer families: document-profile, matter/project pre-fill, workspace summarize-file, email-analysis, insights ask/search, draft-correspondence, create-task. **draft-correspondence surface LIVE since task 041 (FR-P3-02)**: Binding projection through the real `ListTextProjectableBindingsAsync` → `capability_draft-correspondence` (DRAFT-CORR@v1 schema pinned at `infra/dataverse/outputschemas/draft-corr-v1.schema.json`); the `email.draft` seed row declares `Communicate` and its invocation live-suspends into the ONE gate (`DraftCorrespondence_CommunicateDeclared*` in the P2 harness file); DRAFT-ONLY invariant unit-proven in `EmailDraftToolHandlerTests` (statuscode server-pinned to Draft). NL-loop dispatch assertions activate at the G-P3 gate (task 048). |

## CI wiring and the merge gate (ACTIVE since task 026)

**Pass 1 (informational)**: this suite compiles into `Sprk.Bff.Api.Tests` (member of `Spaarke.sln`) and runs inside the root `dotnet test` of `.github/workflows/sdap-ci.yml` (`build-test` job, test pass 1) on every PR. The seed JSON is copied to test output via a `Content` include in `Sprk.Bff.Api.Tests.csproj`; `*.json` edits under `tests/**` are NOT in the workflow's `paths-ignore`, so BA-only case edits trigger a CI run.

**Merge gate (blocking — NFR-02)**: the dedicated `eval-gate` job in `sdap-ci.yml` runs

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj -c Debug --filter "Category=GoldenUtteranceEval"
```

with **no `continue-on-error`**. Placement rationale (deviation from the original task-011 sketch, which put a step inside `build-test`): the `build-test` job carries job-level `continue-on-error: true` (2026-06-24 informational posture), which swallows any step failure inside it — and branch protection is currently disabled on the repo, so the workflow-run conclusion is the only mechanical merge signal. A separate no-tolerance job is the only additive change that turns a red eval into a red workflow run. When branch protection / rulesets are re-enabled, mark **"Eval Gate (Golden Utterances)"** as a REQUIRED status check to hard-block merge.

Verified 2026-07-05 (task 026): a deliberately failing scratch case made `dotnet test --filter "Category=GoldenUtteranceEval"` exit 1; the scratch case was then removed.

## Deletion-safety

KEEP-protected per ADR-038 (`tests/integration/contract/**`). Since P1 (task 026) the suite is an ACTIVE merge gate (NFR-02); every catalog/prompt change adds or updates cases (NFR-06).
