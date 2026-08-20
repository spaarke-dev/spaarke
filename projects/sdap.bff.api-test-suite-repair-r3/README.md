# sdap.bff.api-test-suite-repair-r3

> **Last Updated**: 2026-08-19
> **Status**: 🌱 **SEED** — assessment complete, **not scoped, not execution-ready**. No worktree, no spec, no tasks.
> **Predecessors**: [`sdap-bff.api-test-suite-repair`](../sdap-bff.api-test-suite-repair/) (r1, closed 2026-06-01) → [`sdap.bff.api-test-suite-repair-r2`](../sdap.bff.api-test-suite-repair-r2/) (r2, closed 2026-06-01)
> **Portfolio**: [Issue #794](https://github.com/spaarke-dev/spaarke/issues/794) under [Epic #427 — Code Quality](https://github.com/spaarke-dev/spaarke/issues/427)
> **Tracking**: [#794](https://github.com/spaarke-dev/spaarke/issues/794) (this project) · [#790](https://github.com/spaarke-dev/spaarke/issues/790) + [#795](https://github.com/spaarke-dev/spaarke/issues/795) (siblings — **do not wait on this project**)

## One-liner

r1 repaired the test suite and surfaced a 20-entry real-bug ledger; r2 converted that ledger into production fixes. **r3 addresses what both left behind: 168 tests that are neither passing nor failing — they are `Skip`'d, tracked nowhere, and mostly should not exist.**

## Read first

**[`notes/skipped-tests-assessment-2026-08-19.md`](notes/skipped-tests-assessment-2026-08-19.md)** — the full investigation: the count, the taxonomy by skip reason, per-category disposition, scoping recommendation, and guards against re-accumulation. Everything below is a summary of it.

*(Also published at [`docs/assessments/test-suite-skipped-tests-assessment-2026-08-19.md`](../../docs/assessments/test-suite-skipped-tests-assessment-2026-08-19.md) for repo-wide discoverability.)*

## The finding

```
grep -rE 'Skip\s*=' tests/ --include=*.cs   →   168 occurrences across 45 files
```

A **skipped** test is worse than a missing one: it looks like coverage in the file tree and in review, it survives `/test-diet` (whose classifier targets tests that *run*), and it encodes an intent nobody owns. The number appears in no scorecard, no nightly-health report, and no gate.

| Category | ~Count | Disposition |
|---|---|---|
| **A** — "requires live Dataverse / AI Search / OpenAI" | 43 | **Mis-filed**, not broken. Opt-in live suite (`[Trait("Category","Live")]`) or delete |
| **B** — "requires fully mocked Graph SDK / playbook / Dataverse" | 46 | **Mostly DELETE** — see below |
| **C** — cron-tick / TimeProvider | 8–10 | **#790** — independent, do not couple |
| **D** — WireMock env | 6 | Test-infra, self-contained |

## Why Category B is the point

These are largely the shapes **ADR-038 §5 explicitly bans**. The skip reasons are self-indicting: *"Graph SDK sealed classes cannot be mocked"* is **precisely the symptom ADR-038 cites** as the reason to test at the integration boundary instead; *"endpoint returns 404 in test factory"* says the test asserts against a host never wired for it.

So Category B is mostly **not** "finish the mocks" — it is **delete, or rewrite as integration tests**. This is scaffolding-class debt that was *skipped* rather than *deleted*, which is exactly how it evaded every existing control.

**Expect most of Category B to be DELETE. That is the correct outcome, not a failure.**

## Scope boundary

**In**: Categories A + B (~89 tests) — per-test judgement across many surfaces (Graph, Dataverse, playbooks, RAG, Office, upload), multiple test projects, deletions needing review.

**Out**: Categories C and D. Both are self-contained tasks that should be unblocked immediately rather than wait on this project — see [`docs/assessments/quality-followups-execution-checkpoint-2026-08-19.md`](../../docs/assessments/quality-followups-execution-checkpoint-2026-08-19.md).

## Open scoping question (decide before `/design-to-spec`)

Two credible framings, deliberately left open:

1. **A `/test-diet` campaign** — the classifier and vocabulary already exist (ADR-038 §7 build-vs-maintain, the 17-ban list). Category B sits squarely inside it.
2. **A cycle of the standing quality program** — `code-quality-and-assurance-r3` completed 2026-08-14 with the A+ target explicitly unmet and its lineage framed as multi-cycle. "Tests" and "shared server libs" are already enumerated surfaces there.

**No `code-quality-r4` exists today.** Whether to create one, or run this as a focused campaign under the r1→r2→r3 test-suite-repair lineage, is the first decision.

## Provisional graduation criteria (finalise after scoping)

- [ ] Skipped-test count published as a tracked metric (nightly-health and/or `SCORECARD.md`).
- [ ] Category B triaged through the `/test-diet` classifier; each test deleted, or rewritten as an integration test at a KEEP path.
- [ ] Category A resolved by one mechanically-applied decision (opt-in live suite, or delete).
- [ ] Deletion safety honoured per ADR-038 §3 — anything removed from a KEEP path has same-PR replacement or a documented rationale.
- [ ] A guard against re-accumulation is in place (linked issue required in every new `Skip = "…"`, and/or the count surfaced in nightly-health).
- [ ] Final count re-measured and recorded.

## Next step

Resolve the scoping question above, then run `/design-to-spec` against a `design.md` authored from the assessment. **Do not** block #790 or #795 on this.
