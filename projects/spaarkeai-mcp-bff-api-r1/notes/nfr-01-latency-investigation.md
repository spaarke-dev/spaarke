# NFR-01 latency: why show and plan miss 3 s (2026-11-25)

> Read-only investigation (App Insights `6a76b012-…`, dev BFF, traffic since 2026-11-24 00:22Z, ~33% sampled; whole operations are kept or dropped together, so per-call counts are reliable). No code changed. Evidence rule (ADR-015): counts and timings only.

## Measured

| Tool | n | p95 | Dataverse calls per call (Web API + SDK) | Sum of dependency time vs tool time |
|---|---|---|---|---|
| `spaarke_show_decisions` | 10 | 6,517 ms (max 8,768) | ~27 + ~13 | ≈ 98% (5,646 of 5,773 ms on one call) |
| `spaarke_get_decision_plan` | 10 | 4,674 ms | ~20 + ~18 | ≈ 99% |
| Console `GET /api/v1/signals/worklist` (REST, 14 days) | 49 | 3,822 ms (p50 1,989) | ~9 + ~8 | ≈ 100% |
| Console `GET …/decision-plan` (REST) | 9 | 1,322 ms (p50 1,036) | ~5 + ~5 | ≈ 87% |

**Every millisecond is Dataverse round trips made one after another** (about 80–110 ms each, `sprk_signals` queries ~250 ms). No CPU, Redis, OBO or serialisation cost of note. The cold-process OBO token plus MSAL instance discovery adds ~0.7 s to the first call after a restart only.

## Causes

1. **Show reads the whole lane twice.** `ShowDecisionsTool.RunAsync` calls `ListDecideAsync` (whole lane, for the category counts) and then `ListDecidePageAsync`, which calls `SignalReadService.ReadAsync(Lanes)` again for page 1. A timeline of one call (14:49:38Z 2026-11-24) shows two identical ~1.8–2.0 s bursts of the same 17 queries (signals ×2, matters ×3, projects, work assignments, events, to-dos, work assignments, matters ×2, invoices, plus SDK calls). **Cost: ~2 s per show.**
2. **Plan reads the whole lane to find one row**, after two separate access checks. `GetDecisionPlanTool.RunAsync` runs, in order: `GetPlanAsync` (its own `AuthorizeAsync`), `GetSuppressionAsync` (a second `AuthorizeAsync` on the same Signal), then `ListDecideAsync` (the whole lane) to pick the one row (task 023 decision 3, accepted as a known limit and flagged for measurement). **Cost: ~2 s for the lane read, ~0.4–0.8 s for the repeated access check.**
3. **The lane read itself is serial.** `SignalReadService.ReadAsync` issues ~17 independent queries one after another (~2 s). This also makes the Console worklist p95 3.8 s, so it is not MCP-specific. `SignalReadService` is ontology-owned.

## Fix options (estimates from the measured per-call times)

| # | Change | Owner / review | Effect |
|---|---|---|---|
| A | Show: one lane read. `ListDecidePageAsync` already holds the full `WorklistResponse`; return it (or an overload that returns lane + page) and count the cards from it | Ours (`DecisionCapability`, `ShowDecisionsTool`); ontology reviews `Services/Signals/**` | show ~5.5 → ~3.5 s |
| B | Plan: a single-Signal row read instead of the whole lane (the follow-up task 023 named) | Needs an additive method in `SignalReadService` (ontology) | plan ~4 → ~2 s |
| C | Plan: reuse the first access decision for suppression (one `AuthorizeAsync`) | Ours (`DecisionCapability`); ontology review | plan −0.4–0.8 s |
| D | Lane read: run its independent queries concurrently | Ontology (`SignalReadService`) | lane ~2 → ~0.6 s; show → ~1.5 s; Console worklist faster too |

A + B + C bring plan under 3 s; show needs A + D to be safely under 3 s at p95. None changes read rules, results or the commit path; REST contract tests must stay byte-for-byte green.

D's estimate assumes most of the lane's 17 queries can overlap. They can't: the lane read is mostly a chain where each step needs the previous step's rows (fetch → catalog → cores → subjects → witness → invoices → policy). Only the per-type and per-chunk loops and the two saved fetches can overlap. A realistic gain is ~0.8 s per lane read (~2 → ~1.2 s); the B+D PR measures it on a fixed-latency fake.

## Status (owner 2026-11-25: "Yes, fix and ask"; then "keep B and D here if this session can do them now, and have ontology check and approve")

| Fix | Where | State |
|---|---|---|
| A | `ShowDecisionsTool` + `DecidePageResult.Lane` (set by `DecisionCapability.ListDecidePageAsync`) | Built on `work/spaarkeai-mcp-bff-api-r1`; PR to master with C; ontology-r1 reviews |
| C | `IDecisionCapability.GetPlanAndSuppressionAsync` (`DecisionCapability` asks `SignalCoreRecordAccess.AuthorizeAsync` once; REST routes unchanged) + `GetDecisionPlanTool` | Built with A |
| B, D | `SignalReadService` (ontology-owned), branch `perf/signal-read-single-row-and-concurrent`, built to ontology's task 222 review contract (equivalence fixtures first, written dependency graph, bounded degree, Opus adversarial review by ontology) | PR #3152 (sent to ontology-r1 for Opus review 2026-11-25; no auto-merge). Notes: `signal-read-b-d-notes.md` |
| B wiring | `GetDecisionPlanTool` reads one row instead of the lane | After B+D merges |

Measure show, plan and the Console worklist p95 on dev after the next dev BFF build that carries them.

## Not the cause
- Token acquisition (`DefaultAzureCredential.GetToken` is cached, 0 ms in-process; an OBO token request, ~380 ms, appears on 6 of the 10 show calls and on none of the plan calls).
- Privilege derivation (task 018): one batched read at the end of the page.
- Result size or the widget: the server-side duration is the tool's own trace.
