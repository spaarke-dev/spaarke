# Spike — PlaybookExecutionEngine gap analysis (Wave A5 → Wave C1)

> **Status**: Open — patches scheduled for Wave C1 (task 020)
> **Source**: design-a5-universal-ingest-jps.md §7
> **Filed**: 2026-06-02 by task 014
> **Ephemeral**: yes (consumed by Wave C1 — delete after C1 ships)

---

## Gap #1 — `EvidenceSufficiencyNode` membership predicate

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/EvidenceSufficiencyNode.cs`

**Today**: Rule shapes supported = `minCount` (count-based, line 89–91) + `requireNonEmpty` (presence-based, line 90).

**Needed**: `predicate: "in"` (value-in-array membership) for the `outcomeBearingClassification` rule in universal-ingest Node 3 `checkLayer2Gate`.

**Patch scope**: ~15–20 LOC in `EvidenceSufficiencyNode.cs`. Add `Predicate`, `Value`, `ReadFrom` properties to `EvidenceSufficiencyRule`; add an `EvaluateRule` switch arm for `"in"`. No callers need changes.

**Workaround if unpatched**: precompute `outcomeBearingFlag` in Node 2 executor; use `requireNonEmpty` on the derived path. Works but distributes parameterization concerns across nodes.

**Owner**: Wave C1 (task 020).

---

## Gap #2 — Branch-aware dependency resolution

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs` (lines 838–860) + `ExecutionGraph` companion.

**Today**: `dependsOn` is AND-only. Downstream nodes execute when all upstreams are `success`, regardless of `selectedBranch`. `predict-matter-cost@v1` ships with this informal contract — the synthesis prompt itself declines on insufficient evidence (verified Wave B5 smoke 2026-06-02). For universal-ingest, this is unacceptable: `layer2Extract` (gpt-4o) must NOT execute when gate=insufficient (cost + correctness).

**Needed**:

1. Per-edge `branch` label storage in `ExecutionGraph` (parsed from `dependsOnGraph.edges[].branch` in playbook JSON).
2. Branch-aware skip check in `ExecuteNodeAsync` — before the existing dependency-failure-skip loop, check if any upstream's `selectedBranch` differs from the edge's `branch` label.
3. Helper `ExtractSelectedBranch(NodeOutput)` reading from `ConditionResult.SelectedBranch` and `EvidenceSufficiencyResult.SelectedBranch`.

**Patch scope**: ~25–40 LOC in `PlaybookOrchestrationService.ExecuteNodeAsync` + ~5–10 LOC in `ExecutionGraph`. Backward-compat: nodes whose upstreams have no `selectedBranch` (most existing playbooks) behave unchanged.

**Workaround if unpatched**: every Insights node executor short-circuits on `gate.selectedBranch != node-name` at the top of `ExecuteAsync`. Distributed routing logic — works but makes the playbook's branch semantics implicit in code.

**Owner**: Wave C1 (task 020). Must verify against `predict-matter-cost@v1` regression test before merging.

---

## Non-gaps (verified)

- Template substitution: ✅ `PlaybookOrchestrationService.ApplyConfigJsonTemplates` (added 2026-06-02 D-01 fix) handles `{{var}}` in ConfigJson.
- Parameter dictionary flow: ✅ `PlaybookRunRequest.Parameters` already reaches executors via template context.
- Action FK dispatch: ✅ `sprk_actionid` FK is canonical (D-01 fix).
- `AiAnalysisNodeExecutor` structured-output LLM calls: ✅ reuses `IOpenAiClient.GetStructuredCompletionRawAsync` — same mechanism `IngestOrchestrator` uses today.

---

## Spec Risk-1 compliance

Both gaps fit "patches stay in the engine, don't fork" — single-file changes, no parallel orchestrators, no playbook-engine variants. ✅
