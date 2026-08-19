# Assessment — 168 Skipped Tests: Inventory, Taxonomy, and Proposed Disposition

> **Created**: 2026-08-19 · **Status**: investigation complete; **project not yet scoped**
> **Tracking**: [#794](https://github.com/spaarke-dev/spaarke/issues/794) · **Parent**: [Epic #427 — Code Quality](https://github.com/spaarke-dev/spaarke/issues/427)
> **Purpose**: give a future project-setup session everything needed to scope this without re-deriving the analysis.
> **Companion**: [`quality-followups-execution-checkpoint-2026-08-19.md`](quality-followups-execution-checkpoint-2026-08-19.md) — the tasks that do *not* need a project.

---

## 1. The finding

```
grep -rE 'Skip\s*=' tests/ --include=*.cs   →   168 occurrences across 45 files
```

**Nothing tracks this number.** It appears in no scorecard, no nightly-health report, and no gate.

This matters more than an equivalent count of *missing* tests. Coverage is observation-only by design (ADR-038, and that decision is correct) — but a **skipped** test is actively worse than no test:

- it looks like coverage in the file tree and in review
- it survives `/test-diet`, whose classifier is aimed at tests that *run*
- it encodes an intent nobody is accountable for
- it accumulates silently, because skipping is the path of least resistance when a test is inconvenient

The trigger for this count was #790 (scheduling determinism). That turned out to be **8–10 of the 168 — roughly 6%**. The determinism problem is real but it is a rounding error against the whole.

## 2. Taxonomy by skip reason

Counted from the `Skip = "…"` strings:

| Count | Reason cluster | Category |
|---|---|---|
| 13 | "Requires live Dataverse and Azure AI Search" | **A — live environment** |
| 12 | "Requires live Azure AI Search and OpenAI embedding model" | **A** |
| 8 | "Requires live Dataverse connection for IScopeResolverService" | **A** |
| 7 | "IToolHandlerRegistry depends on Dataverse for handler discovery" | **A** |
| 3 | "Requires live Dataverse connection for IPlaybookService" | **A** |
| 9 | "Graph SDK sealed classes cannot be mocked (Moq / NullReferenceException)" | **B — unworkable mocks** |
| 8 | "chat action endpoints return 404 in test factory" | **B** |
| 6 | "FakeGraphHttpHandler returns errors for content download" | **B** |
| 5 | "requires fully mocked Graph SDK upload session" | **B** |
| 5 | "requires fully mocked playbook orchestration" | **B** |
| 5 | "requires fully mocked Dataverse search services" | **B** |
| 5 | "endpoint returns 404 without proper registration" | **B** |
| 3 | "requires fully mocked Dataverse services for quick create" | **B** |
| 8 | "CI cron-tick flake — needs TimeProvider refactor (see PR #415)" | **C — determinism** |
| 6 | "WireMock.Net path matching returns 500 in this environment" | **D — test infra** |

**Approximate totals: A ≈ 43 · B ≈ 46 · C ≈ 8–10 · D ≈ 6**, remainder long-tail.

Heaviest files: `PlaybookExecutionIntegrationTests.cs` (19), `RagDedicatedDeploymentTests.cs` (13), `RagSharedDeploymentTests.cs` (12), `FileOperationsTests.cs` (11), `OfficeEndpointsContractTests.cs` (10).

## 3. Why the categories need different treatment

### Category A — "requires live environment" (~43)

These are **not broken; they are mis-filed.** They are legitimate integration tests with no way to execute — no live Dataverse, AI Search, or OpenAI in CI.

Leaving them `Skip`'d in the main suite is the worst available option: no signal, no coverage, permanent noise, and a standing invitation to skip the next one.

**Proposed disposition** — one decision, mechanically applied:
- Move to an explicitly opt-in suite (`[Trait("Category","Live")]` or a separate test project) run on demand against a real environment, **or**
- Delete them, if nobody will realistically ever run them.

Either is defensible. The current state is not.

### Category B — "requires fully mocked X" (~46) — *the important one*

These are largely the shapes **ADR-038 §5 explicitly bans**: transport-level mocking, mocking the class-under-test's collaborators, `Mock<HttpMessageHandler>`-adjacent patterns.

The reason strings are self-indicting. *"Graph SDK sealed classes cannot be mocked"* is **precisely the symptom ADR-038 cites** as the reason to test at the integration boundary instead. *"endpoint returns 404 in test factory"* says the test is asserting against a host that was never wired for it.

So Category B is mostly **not** "finish the mocks." It is **delete, or rewrite as integration tests** — exactly the build-vs-maintain judgement `/test-diet` exists to make. This is scaffolding-class debt that was *skipped* rather than *deleted*, which is how it evaded every existing control.

**Expect most of Category B to be DELETE.** That is the correct outcome, not a failure.

### Category C — determinism (~8–10)

Tracked separately as **#790**. Self-contained; needs the Cronos/`FakeTimeProvider` root-cause. **Should not wait on this project** — see the execution checkpoint.

### Category D — WireMock (~6)

A test-infrastructure defect, self-contained and small.

## 4. Scoping recommendation

**Categories A + B (~89 tests) are the project.** They require per-test judgement across many surfaces (Graph, Dataverse, playbooks, RAG, Office, upload), touch multiple test projects, and produce deletions that need review. That is program work, not a task.

**Categories C and D are tasks and should be unblocked immediately** — they are independent of the A/B decision.

Two framings worth considering for the project:

1. **As a `/test-diet` campaign** — the classifier and the vocabulary already exist (ADR-038 §7 build-vs-maintain; the 17-ban list). This is the closest existing mechanism, and Category B is squarely within it.
2. **As a cycle of the standing quality program** — `code-quality-and-assurance-r3` completed 2026-08-14 with the A+ target explicitly not reached and the lineage (r1 → r2 → r3) framed as multi-cycle. "Shared server libs" and "tests" are already enumerated surfaces there.

**No `code-quality-r4` exists today.** Whether to create one, versus running this as a focused test-diet campaign, is an open scoping decision — deliberately left open here.

## 5. Suggested sequencing if the project proceeds

1. **Publish the count as a tracked metric** — it currently appears nowhere. Cheap, and it stops the number drifting invisibly again.
2. **Category B first.** Highest debt-removed per unit effort, and the only category that is *actively misleading* about coverage. Run the `/test-diet` classifier over it.
3. **Category A next.** One decision, applied mechanically.
4. Leave C (#790) and D to run independently in parallel.

## 6. Guard against re-accumulation

Whatever is decided, the number goes back up unless something watches it. Options, cheapest first:

- Surface the skipped count in the **nightly-health** report alongside the coverage observation (no gate, just visibility).
- Require a **linked issue** in every new `Skip = "…"` string, so a skip is a tracked decision rather than a silent one.
- Add the count to `notes/SCORECARD.md` as a standing quality dimension.

The failure mode this guards against is documented in the reason strings themselves: three tests carry `"needs TimeProvider refactor (see PR #415)"` — a follow-up noted in R3 that then sat unaddressed until this session surfaced it.

## 7. Provenance

Surfaced 2026-08-19 while repairing master's red suite (PRs #787, #788, #789, #793, #796). The repair work itself removed **11 red tests** and fixed three broken CI gates; this assessment is the residue — the debt those repairs revealed but did not touch.
