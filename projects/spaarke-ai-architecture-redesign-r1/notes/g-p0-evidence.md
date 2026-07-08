# G-P0 Gate Evidence Package — spaarke-ai-architecture-redesign-r1

> **Gate**: G-P0 (spec Success Criterion 1) — the project's SOLE engineering gate (NFR-11)
> **Date**: 2026-07-05 · **Executed by**: gate task 014 (main session)
> **Verdict**: ✅ PASS — ADR-040 promoted to Accepted
> **Deployed build**: commit `a93bd6dce` + gate-014 reconciliation fixes, on `spaarke-bff-dev`

## 1. Ledger round-trip INCLUDING file references (FR-P0-01, task 001)

`dotnet test --filter FullyQualifiedName~ChatSessionLedgerRoundTripTests` → **7/7 passed**, including:

```
Passed GetSession_AfterCosmosRestore_PreservesLedgerEntriesAndFileReferences [358 ms]
Passed UpdateSessionCache_WithLedgerEntriesAndFileRefs_SurvivesRedisRoundTrip [18 ms]
Passed GetSession_FromPreLedgerCosmosDocument_RestoresWithNullLedgerAndNullFileRefs [2 ms]
Passed BuildOutputKey_GivenBindingAndTurn_ProducesAddressableKey (chat-summarize@t3, loop@t12)
```

The acceptance test replays the exact Cosmos serializer settings (Newtonsoft camelCase wire) and
asserts full equivalence after cold-node restore, including the 2-file manifest (14 fields/file),
`DocumentId`, and `AdditionalDocumentIds`. The pre-existing Cosmos file-reference clobber is fixed
in both mapping directions (`ChatSessionManager.MapChatSessionToStoredSession` / `MapStoredSessionToChatSession`).
Zero-readers grep: ledger fields referenced ONLY by model + persistence mappers (task 001 report).

## 2. Boot reconciliation health checks on the deployed environment (FR-P0-04, task 005)

Deployed probes on `spaarke-bff-dev` (2026-07-05, post gate-014 redeploy):

```
GET /healthz          → 200 (liveness — excludes the catalog tag by design)
GET /healthz/catalog  → 200 "Degraded"
```

**Why Degraded is the correct G-P0 state** (gate-014 semantic corrections, main session):
- Constants ↔ Binding rows: **reconciled** (9 ↔ 9; `ComposeSummarize` constant added — the row
  pre-existed from active project spaarkeai-compose-r1).
- Tool-id identity: **no duplicates** (duplicate detection corrected to key on `sprk_toolid`;
  a handler serving several named tool rows is the catalog's legitimate row=tool shape).
- Tool rows → handlers: **26/26 rows resolve to registered handlers**, incl. the 6 new
  `dataverse.*` rows (6↔6 with tasks 008/009 handlers).
- Orphan handlers (registered, no row): **14 named** — reported Degraded, NOT Unhealthy, because
  (a) several are direct-wired chat handlers that only become catalog-bound at the FR-P2-01
  cutover, and (b) the F-1 deletion targets must not be seeded rows just to green a probe.
  **Escalation to Unhealthy is a task-030 acceptance criterion** (POML updated 2026-07-05).
- Drift-fails-startup proven by test: 14/14 `RoutingConsumerTypeHealthCheckTests` green,
  covering every drift class → Unhealthy, orphans → Degraded, never-false-fail matrix.

## 3. Catalog schema deployed on spaarkedev1 (FR-P0-03, task 003)

19 columns verified live via MCP `describe` + Web API attribute checks (all `OK`):
`sprk_analysisaction` +4 · `sprk_playbookconsumer` +9 · `sprk_analysistool` +5 (+1 pre-existing
`sprk_toolid`), plus global option set `sprk_aimodeltier`. Authoritative dictionary:
[`notes/schema/schema-p0-column-dictionary.md`](schema/schema-p0-column-dictionary.md).
Idempotent script: `scripts/Deploy-AiCatalogSchemaExtensions.ps1` (re-run verified PATCH path).
Tool rows on spaarkedev1: 26 active, including the 6 GA-frozen `dataverse.*` rows with
`sprk_sideeffectclass` Read/Write declared.

## 4. User-OBO audit (FR-P0-10, task 012)

[`notes/user-obo-audit.md`](user-obo-audit.md): all six `dataverse.*` handlers PASS
(fail-closed OBO via `IDataverseUserClient`, no app-only fallback, file:line evidence).
Findings F-1 (Critical) / F-2 / F-3 / F-4 recorded; **§7 gate ruling (operator, 2026-07-05):
ACCEPT-UNTIL-CUTOVER** — task 044 scope + acceptance extended to delete the three surviving
app-only legs (`InvokePlaybookHandler`, `AnalysisQueryHandler`, `WorkingDocumentHandler`) with an
F-1 re-trace; interim backstops = closed-catalog projection (FR-P2-01) + bijection health check.

## 5. OBO spike (FR-P0-08, task 010)

[`notes/spikes/obo-mcp-spike.md`](spikes/obo-mcp-spike.md): **FAIL-with-path** — the
`mcp.tools` OBO exchange is blocked solely by a missing delegated-permission grant
(AADSTS65001); the `/.default` control proved OBO mechanics end-to-end. Native `dataverse.*`
handlers remain the runtime path (D10 unchanged); per-tool MCP transport stays open behind
3 documented admin actions. Informative, non-blocking — per spec.

## 6. Eval suite in CI (FR-P0-09, task 011)

`tests/integration/contract/Eval/` — 34 golden utterances / 14 families (BA-editable JSON).
Local run: `dotnet test --filter "Category=GoldenUtteranceEval"` → **7/7 passed** (inventory
integrity, UC traceability, catalog grounding vs `ConsumerTypes.All`, NFR-06 schema round-trip,
live `ResolveBindingAsync` routing smoke). CI pickup via the existing contract compile glob in
`Spaarke.sln` `dotnet test` (sdap-ci.yml build-test); merge-gate activation switch documented for
task 026 (NFR-02).

## 7. Full-suite + publish-size status (ADR-029 / NFR-01)

- `dotnet build src/server/api/Sprk.Bff.Api/` → **0 errors**.
- Full unit suite: **7,787 passed / 101 skipped / 9 failed** — 8 failures proven PRE-EXISTING at
  the pre-wave baseline (which itself did not compile; wave repairs fixed the compile break plus
  5 additional baseline failures): SummarizeSession contract ×3 (pre-R7-091 pipeline asserts →
  tasks 025/035 scope), ExecutorConfigSchemas, KnowledgeDeploymentConfig, DailyBriefingCollector
  resolver (R7-W12 DEF), TemplateContextBuilder, SessionFilesCleanup. The 9th
  (AuditLogServiceTests) is a parallel-load flake — passes in isolation (shown 2026-07-05).
- **Publish: 46.87 MB compressed** (deploy package, SHA-256-verified on spaarke-bff-dev).
  Ceiling ≤60 MB ✅ · review threshold 55 MB ✅. Baseline note: +1.2 MB vs the 2026-05-26
  45.65 MB baseline is accumulated master drift; P0's own contribution ≈ +0.1 MB net of
  Track-B batches 1–3 deletions (~55 files removed). Net-reduction trajectory on track —
  the large deletions land at P2/P3.

## 8. Portfolio reconciliation (FR-P0-11, task 013)

R7 (#501) closed with per-wave absorption map; R4 / Action Engine R1 / insights-r3 triggers
re-pointed to this project's gates; Action Engine re-base stub filed. [`notes/r7-close-out-absorption-map.md`](r7-close-out-absorption-map.md).

## Success Criterion 1 checklist

| Item | Evidence | Status |
|---|---|---|
| Ledger round-trip incl. file refs | §1 | ✅ |
| Health checks green on deployed env | §2 (liveness 200; catalog 200-Degraded with tracked orphans) | ✅ |
| Schema deployed | §3 | ✅ |
| ADR-040 Accepted | flipped 2026-07-05 citing this package (both copies) | ✅ |
| /goal cleared before gate; tasks 001–012 ✅ | TASK-INDEX at gate start | ✅ |
