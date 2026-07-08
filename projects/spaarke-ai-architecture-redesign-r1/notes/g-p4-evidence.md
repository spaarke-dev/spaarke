# G-P4 Gate Evidence — Phase 4 Close (task 090 step 1)

> **Assembled**: 2026-07-08 · **Branch**: `work/spaarke-ai-architecture-redesign-r1` (HEAD ~`4e8928e15`)
> **Gate definition** (spec Success Criterion 5, FR-P4-07): *"everything reliable + telemetered; Track-B audit zero unexplained survivors; publish size reduced."*
> All numbers below are cited from existing task/gate artifacts (paths given per item); the eval suite was re-run fresh for this file.

## Verdict table

| # | Item | Evidence | Status |
|---|---|---|---|
| 1 | Eval suite green (NFR-02) | Fresh run 2026-07-08: **35/35 passed, 0 failed** (`Category=GoldenUtteranceEval`) | ✅ |
| 2 | Metering rollups (NFR-05) | Live dev acceptance query: tenant rollup **3 turns / 1 toolCall / 40,254 in / 353 out tokens / 2 capabilityInvocations**; 4 counters shipped; KQL pack at `scripts/kql/ai-metering/` — `notes/task-054-metering-evidence.md` | ✅ |
| 3 | Latency (NFR-04) | No numeric TTFB recorded at gates 027/038/048; structural evidence = prompt-cache-stable tool projection (SHA-256 fingerprint, task 030) + operator-observed streaming responsiveness across 7 browser-UAT round findings files | ✅ (qualitative — stated honestly) |
| 4 | Budgets (NFR-09) | Per-turn tool budget 8 enforced in-loop (`AgentTurnContract` + `BudgetedAIFunction`); live `tool-budget-consumption.kql` shows cap=8, 0 denials; event daily budget observed at **1/50 (2 %)** | ✅ |
| 5 | Track-B zero unexplained survivors | `notes/track-b-completion-audit.md`: **62 rows — 44 grep-verified deleted, 15 Dataverse rows retired, 9 keep-with-reason, 5 operator-decision (O-1..O-5); 0 unexplained** | ✅ |
| 6 | Publish size "reduced" | `notes/publish-size-cve-report.md`: **49.63 MB** incl. PDBs vs 45.65 MB (2026-05-26) = **+3.98 MB — NOT reduced in absolute terms**; ceiling ≤60 MB comfortably met (5.37 MB headroom); zero NuGet additions | ✅ **ACCEPTED by operator 2026-07-08** (growth = priced-in capability; ceiling honored) |
| 7 | Reliability | Full BFF suite **7,447 passed / 4 failed** — all 4 on the KNOWN pre-existing list; zero new failures; branch deployed healthy to `spaarke-bff-dev` and exercised live on all three entry paths | ✅ |

---

## 1. Eval suite green (NFR-02)

Fresh run for this gate file (2026-07-08):

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --nologo --filter "Category=GoldenUtteranceEval"
Passed!  - Failed:     0, Passed:    35, Skipped:     0, Total:    35, Duration: 313 ms - Sprk.Bff.Api.Tests.dll (net8.0)
```

**35/35 green.** Corroborated by the task-050 audit run (`notes/track-b-completion-audit.md` §9: "Eval suite … ✅ 35/35 passed"). The suite injects the FULL live catalog (task 037, `notes/task-037-eval-full-catalog-injection-notes.md`) — dispatch, refusal, and briefing-family cases run against the real `ListTextProjectableBindingsAsync` projection path.

## 2. Metering rollups (NFR-05)

Source: [`notes/task-054-metering-evidence.md`](task-054-metering-evidence.md) — captured 2026-07-08 against dev App Insights `spe-insights-dev-67e2xz` after deploying the branch to `spaarke-bff-dev` and exercising all three live entry paths (text turns, `document_uploaded` Event rule, chip-click dispatch).

**The acceptance query** (`tenant-usage-rollup.kql`):

```
tenantId                             | turns | toolCalls | tokensIn | tokensOut | capabilityInvocations
a221a95e-6abc-4434-aecc-e48338a1b2f2 | 3     | 1         | 40254    | 353       | 2
```

- **4 counters shipped** (meter `Sprk.Bff.Api.Ai`, `src/server/api/Sprk.Bff.Api/Telemetry/AiTelemetry.cs`): `ai.metering.turns` (with `tool_budget.spent/cap/denied`), `ai.metering.tool_calls`, `ai.metering.tokens` (input/output × loop/executor × entry path × model), `ai.metering.capability_invocations` (entry path × capability × outcome).
- Per-user drilldown, tokens-by-model (ambient-scope attribution across text/click/event paths), and capability-usage queries all returned live rows (see source file).
- **KQL pack**: `scripts/kql/ai-metering/` — README (counter schema + runbook) + 6 documented queries.
- NFR-07 held: dimensions are identifiers/counts only, no prompt/document text.
- Bonus fix landed with 054: the `Sprk.Bff.Api.EventRules` meter export gap closed — NFR-09 "enforced AND telemetered" is now actually true in App Insights.

## 3. Latency (NFR-04)

**Honest statement: no numeric TTFB / turn-latency figures were recorded at gates 027 (G-P1), 038 (G-P2), or 048 (G-P3).** The only millisecond figures in the gate findings are Dataverse write durations inside defect logs (e.g. `durationMs=928/849/439/353` in `notes/g-p3-uat-round2-findings.md` / `g-p3-uat-round3-findings.md`), not chat-turn latency.

What the record DOES support:

- **Structural NFR-04 evidence** — `notes/task-030-agent-turn-loop-notes.md`: prompt-cache-stable tool projection. Survivors sorted `StringComparer.Ordinal`; `ComputeProjectionFingerprint` (SHA-256 over ordered name/description/schema) logged per agent creation (`[FR-P2-01][NFR-04]`), with fingerprint-stability tests (same tools in any input order ⇒ identical fingerprint ⇒ byte-identical tool block ⇒ Azure OpenAI prefix-cache hits). Reasserted in the eval suite (`notes/task-037-eval-full-catalog-injection-notes.md`: fingerprint stability across catalog read order).
- **Qualitative gate evidence** — each of the three gates was an operator browser UAT on spaarkedev1 against deployed builds; streaming responsiveness was operator-observed across **seven recorded round-findings files**: `g-p1-uat-round1/2-findings.md`, `g-p2-uat-round1-findings.md`, `g-p3-uat-round1/2/3/4-findings.md` (the round-4 file also contains a round-5 addendum on the same build). No round raised latency or streaming sluggishness as a finding; all defects were behavioral (payload composition, honesty, gating), not performance.

If a numeric latency SLO is wanted, it is an r2 measurement item (the metering counters landed in 054 give the substrate: per-turn dimensions in App Insights).

## 4. Budgets (NFR-09)

- **Per-turn tool budget (loop)** — `notes/task-030-agent-turn-loop-notes.md`: budget default **8**, tunable `Ai:AgentTurn:ToolCallBudget`, enforced by `AgentTurnContract` + `BudgetedAIFunction` wrapper (`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/AgentTurnContract.cs`, `.../BudgetedAIFunction.cs`); over-budget calls return the grounded budget-exhausted message and the inner tool never executes; covered by the `AgentTurnLoopContractTests` budget group. `[ADR-016][agent-turn.*]` telemetry emitted.
- **Budget observability live** — `notes/task-054-metering-evidence.md` `tool-budget-consumption.kql`: `cap=8, turns=3, maxSpent=0, cappedTurns=0, turnsWithDenials=0, deniedCalls=0` against real dev traffic; `tool_budget.spent/cap/denied` dimensions ride `ai.metering.turns`.
- **Event daily budget** — same file, `event-daily-budget.kql`: `executionsToday=1, cap=50, pctOfCap=2` for the live `document_uploaded` execution — consumed-vs-cap observable per NFR-09; the event-path `capability_invocations` counter carries `budget.cap`.

## 5. Track-B completion audit — zero unexplained survivors

Source: [`notes/track-b-completion-audit.md`](track-b-completion-audit.md) (task 050, FR-P4-01; every row FRESH-grepped at audit time).

| Metric | Count |
|---|---|
| Rows audited | **62** |
| Grep-verified DELETED (incl. 39 files deleted by task 050 itself) | **44** |
| RETIRE-data executed (Dataverse rows, old→new evidence shown per row) | **15** (1 playbook + 4 tools + 10 knowledge) |
| KEEP-with-reason | **9** |
| OPERATOR-decision (registered, not improvised) | **5** |
| **Unexplained survivors** | **0** |

The 5 operator-decision items (audit §11): **O-1** `spaarke-playbook-embeddings` Azure AI Search index (zero consumers — delete on dev service); **O-2** workspace-tab tool cluster (coordinated code+row retirement, r2); **O-3** `DocumentStreamEvent` plumbing + `SprkChatBridge` + `playbook_options` client leg (one r2 "client wire diet" cutover, ADR-033 Path-B); **O-4** `AnalysisOrchestrationService` deprecated no-nodes branch (verify env data, then remove, r2); **O-5** ratified-but-unused pairs + load-bearing-but-stale seed JSONs (r2 catalog-governance / test-diet batch). Doc-drift residue is itemized in audit §10 and explicitly does not block the gate criterion (code estate + explained survivors).

## 6. Publish size — ⚠️ AMBER item, reported honestly

Source: [`notes/publish-size-cve-report.md`](publish-size-cve-report.md) (task 055, FR-P4-06; deploy-lineage measurement — same `Compress-Archive` cmdlet + glob as `Deploy-BffApi.ps1`, **incl. the 4 PDBs** that ship in every deploy package).

| Measure (2026-07-08) | Value |
|---|---:|
| Compressed zip, incl. PDBs (canonical deploy lineage) | **49.63 MB** |
| Compressed zip, excl. PDBs | 45.87 MB |
| vs 2026-05-26 ADR-029 baseline (45.65 MB) | **+3.98 MB** |
| vs project-start actual (G-P0 deploy, 46.87 MB — `notes/g-p0-evidence.md` §7) | **+2.76 MB** |
| Ceiling check | 49.63 ≤ 60 MB HARD STOP ✅ · < 55 MB review threshold ✅ (**5.37 MB headroom**) |

**The "publish size reduced" expectation is NOT met in absolute terms.** Reconciliation (full detail in the source report):

- **+1.22 MB predates this project** — accumulated master drift between 2026-05-26 and project start (G-P0 measured 46.87 MB before any project code).
- **Track-B deletions DID land and shrank what they touched**: file count 279 → 247, PlaybookBuilder canvas `wwwroot/` bundle down to ~1 MB, dispatcher stack / engine shells / 42 builder-server files grep-zero.
- **Growth = net-new P1–P3 capability compiled into `Sprk.Bff.Api.dll`** (~10 MB raw) + its PDB (~4.9 MB): agent turn loop, gate + elicitation machinery, email + work_product disposition legs, widget layer, eval suite, per-tenant metering. **Zero NuGet package additions**; CVE surface unchanged (single pre-existing accepted-risk Kiota HIGH; no new HIGHs). Growth was gradual across tasks — no task crossed the +5 MB single-task escalation threshold.
- **Named reduction levers if a number is still wanted**: PDB exclusion from the deploy package (**−3.76 MB**, debugging trade-off) and the deferred Graph SDK 6.x upgrade (shrinks the 41 MB `Microsoft.Graph.dll`).
- ADR-029 baseline reset to 49.63 MB so downstream diffs are honest.

**✅ SIGNED OFF — operator ACCEPTED 2026-07-08** (per recommendation) — the growth is priced-in P1–P3 capability, hygiene rules held (zero NuGet adds, deletions landed, no new CVEs), and the binding ≤60 MB ceiling is honored with 5.37 MB headroom.

## 7. Reliability

- **Full BFF unit suite** (source: `notes/publish-size-cve-report.md` §4, two stable runs 2026-07-08): **Failed: 4, Passed: 7,447, Skipped: 101, Total: 7,552.** All 4 failures are the KNOWN pre-existing list — `KnowledgeDeploymentConfigTests.KnowledgeDeploymentConfig_DefaultValues_AreCorrect`, `DailyBriefingCollectorTests.CollectAsync_RoutesMembershipQueriesToResolver_NotInlineFetchXml`, `PlaybookTemplateContextBuilderTests.Build_TextOnlyOutput_IsExposedAsString`, `SessionFilesCleanupJobTests.RunScheduledScanAsync_Evicts_Only_Orphans_Not_In_Active_Set`. **Zero new failures**; pass delta vs prior wave (+3) exactly matches the 3 new ADR-040 cap tests. `dotnet build src/server/api/Sprk.Bff.Api/` green, 0 errors.
- **Deployed healthy on `spaarke-bff-dev`** — the branch build was deployed and exercised live for the task-054 metering acceptance (all three entry paths — text turns with drained SSE `done` frames, `document_uploaded` event rule execution, chip-click dispatch — `notes/task-054-metering-evidence.md`), i.e. both the BFF API surface and the BFF-hosted static SPA surface (`wwwroot/playbook-builder`, kept live per the task-050 audit §1-appendix) are serving. The FR-P0-04 orphan-handler boot check keeps `GET /healthz` Healthy on that deployment (referenced in `notes/task-053-ui-test-evidence.md`). G-P0's earlier deploy was SHA-256-verified on the same App Service (`notes/g-p0-evidence.md` §7).
- **Telemetered**: items 2 and 4 above — 4 metering counters + event-rules meter export fix, all verified with live App Insights rows.

---

## Gate disposition

**G-P4 = GREEN on ALL items. Item 6 (publish size) ACCEPTED by operator 2026-07-08 — gate CLOSED.**

- Reliable ✅ (7,447/4-known suite; both surfaces live on dev) · Telemetered ✅ (NFR-05 rollups verified against live App Insights; NFR-09 budgets enforced and observable) · Track-B ✅ (62 rows, 0 unexplained survivors; O-1..O-5 registered for r2) · Eval ✅ (35/35) · Latency ✅ qualitative (stated honestly; numeric SLO = r2 with the 054 substrate).
- **Sign-off (CLOSED 2026-07-08, operator: accept)**: publish size grew +3.98 MB vs the 2026-05-26 baseline instead of shrinking. Recommendation to operator: **accept** — growth is priced-in P1–P3 capability with hygiene rules held and the 60 MB ceiling honored (5.37 MB headroom); reduction levers (PDB exclusion −3.76 MB, Graph SDK 6.x) remain available if a reduction number is required.

*Assembled by task 090 step 1 from: fresh eval run + `task-054-metering-evidence.md` · `task-030-agent-turn-loop-notes.md` · `task-037-eval-full-catalog-injection-notes.md` · `track-b-completion-audit.md` · `publish-size-cve-report.md` · `g-p0-evidence.md` · `g-p1/g-p2/g-p3` UAT round findings · `task-053-ui-test-evidence.md`.*
