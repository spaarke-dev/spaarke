# G-M Maker Gate — DEFERRED by Operator Ruling (Graduation Amendment)

> **Date**: 2026-07-08 (recorded at task 090 step 2)
> **Ruling**: Operator, 2026-07-07 final close session — "let's skip this for now until we're ready to really work with modifying actions."
> **Status**: **DEFERRED post-r2** — Success Criterion 6 graduates on partial evidence + a scheduled live walkthrough.

---

## What G-M required (spec Success Criterion 6 / FR-P4-07)

The operator observes a business analyst authoring a brand-new small capability ENTIRELY as data (JPS prompt + schema + Binding + chips + eval case — ZERO deploys), then a user invokes it in the Spaarke UI and sees a rendered result. Browser gate (NFR-11).

## Why it is deferred, not failed

The gate's *mechanism* is shipped and evidenced; only the *live observed session* is postponed:

| G-M component | Shipped evidence |
|---|---|
| BA authoring surface | Task 053 ✅ — PlaybookBuilder de-scoped to BA catalog editor: Actions + Bindings authoring tabs, chipTransitions + onEventBindings structured editors, client twin of `OpenAiFunctionSchemaValidator` (the outage-class schema is UNAUTHORABLE in the editor), direct Dataverse Web API saves. UI-test evidence: `notes/task-053-ui-test-evidence.md`; jest 103/103 + 53/53. |
| Capability-as-data proven end-to-end | Tasks 041/042 authored DRAFT-CORR@v1 and CREATE-TASK@v1 as catalog rows (Action JPS + schema + Binding + chips + eval cases) and both passed six rounds of operator browser UAT at G-P3 — the identical data-only pipeline G-M exercises, executed by the engineering session rather than an observed BA. |
| Eval-case obligation (NFR-02/NFR-06) | Golden-utterance suite 35/35 green including the capability cases added with each catalog row. |
| Zero-deploy invariant | Both capabilities above reached the UI via catalog rows only; the BFF deploys during P3 were fix waves, not capability wiring. |

What is NOT yet evidenced: a non-engineer (business analyst) performing the authoring unassisted under operator observation. That is precisely the piece the operator chose to defer until the team is "ready to really work with modifying actions" (post-r2, when the r2 judgment/memory core changes settle the Action-authoring surface).

## Graduation amendment

- Success Criterion 6 closes as **DEFERRED-WITH-EVIDENCE** rather than PASSED.
- The live maker walkthrough is scheduled as a post-r2 checkpoint; filed in the wrap-up /defer set (see `notes/defer-issues.md` — "G-M live maker walkthrough" entry) so it lands on the portfolio board and cannot silently evaporate.
- Prerequisite retained: `docs/guides/ai-guide-consumer-wiring.md` (task 052 rewrite) is the BA-facing tutorial the walkthrough will follow.

## Sign-off

- Operator ruling recorded verbatim in `current-task.md` (2026-07-07) and this file.
- Wrap-up PR description cites this amendment (Success Criterion 6 disposition).
