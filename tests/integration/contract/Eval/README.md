# Golden-Utterance Eval Suite (`tests/integration/contract/Eval/`)

> **Origin**: `spaarke-ai-architecture-redesign-r1` task 011 (FR-P0-09)
> **Category authority**: [ADR-038](../../../../docs/adr/ADR-038-testing-strategy.md) — `tests/integration/contract/**` is a KEEP path
> **Governing requirements**: spec NFR-02 (merge gate from P1), NFR-06 (schema-conformance + citation-integrity assertions), FR-P2-08 (refusal/compound/prompt-injection families)

## What this is

The quality spine of the AI architecture redesign. Every case is a golden utterance:

```
{ utterance, §3 UC id, expected capability binding, expected outcome class, optional output assertions }
```

- **Seed data**: [`golden-utterances.json`](golden-utterances.json) — 34 cases across 14 families, each traceable to a §3 UC trigger in [`SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../../../../docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md)
- **Harness**: [`GoldenUtteranceEvalSuiteTests.cs`](GoldenUtteranceEvalSuiteTests.cs) — compiled into `Sprk.Bff.Api.Tests` via the contract-path `Compile` glob; trait-filterable as `Category=GoldenUtteranceEval`

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
| **P0 (now)** | Inventory integrity (≥30 cases, unique ids, UC traceability, closed vocabularies); consumer-type grounding against `ConsumerTypes.All`; NFR-06 schema round-trip; routing-surface smoke driving the real `ConsumerRoutingService.ResolveBindingAsync` selection algorithm (Dataverse boundary stubbed); pending-inventory declaration |
| **P1** (task 026, FR-P1-07) | UC-A-1 `chat-summarize` family dispatch assertions go live (utterance → binding via the single-hop dispatcher) + SUM-CHAT@v1 schema-conformance; **merge gate activates** (below) |
| **P2** (task 037, FR-P2-08) | Refusal + compound + prompt-injection families; clarify outcomes (loop elicitation); citation-integrity |
| **P3** (FR-P3-01/02/03) | Remaining consumer families: document-profile, matter/project pre-fill, workspace summarize-file, email-analysis, insights ask/search, draft-correspondence, create-task |

## CI wiring (P0) and merge-gate activation (P1)

**Wired now**: this suite compiles into `Sprk.Bff.Api.Tests` (member of `Spaarke.sln`) and therefore runs inside the root `dotnet test` of `.github/workflows/sdap-ci.yml` (`build-test` job, test pass 1) on every PR. The seed JSON is copied to test output via a `Content` include in `Sprk.Bff.Api.Tests.csproj`; `*.json` edits under `tests/**` are NOT in the workflow's `paths-ignore`, so BA-only case edits trigger a CI run.

**Activation switch (task 026 executes this — do not activate early)**:

1. Add a dedicated required step to the `build-test` job (after "Final test verdict"):
   ```yaml
   - name: Golden-utterance eval gate (NFR-02 — merge-blocking)
     shell: pwsh
     run: dotnet test -c ${{ matrix.configuration }} --no-build --filter "Category=GoldenUtteranceEval"
   ```
   No `continue-on-error` on the step; the trait filter exists precisely so this step is additive (no workflow restructuring).
2. The `build-test` job currently carries `continue-on-error: true` (2026-06-24 informational-CI posture). Task 026 must either remove it or mark the eval step's job as a REQUIRED status check in branch protection so a red eval suite blocks merge (NFR-02).
3. Green the UC-A-1 family: replace the pending declaration for `dispatchAssertPhase: P1` cases with live utterance→binding dispatch assertions against the P1 dispatcher.

## Deletion-safety

KEEP-protected per ADR-038 (`tests/integration/contract/**`). From P1 the suite is a merge gate (NFR-02); every catalog/prompt change adds or updates cases (NFR-06).
