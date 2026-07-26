# NEG-03 — Unauthorized-User Case (scenario descriptor, not a document)

This case is **not** a document to feed into `documentText`. It is a scenario descriptor for the closed set's
authorization dimension: an NDA-REVIEW invocation attempted by a caller who lacks the entitlement required to run
the Action.

## Scenario

- **Caller**: an authenticated user of the Compose surface who does **not** hold the role/claim gating AI-analysis
  execution (e.g., missing the entitlement checked by the `AnalysisAuthorizationFilter` endpoint filter — see
  `.claude/constraints/ai.md` "MUST use endpoint filters for AI authorization (ADR-008)").
- **Action attempted**: `POST /api/ai/analysis/execute` with `actionCode: "nda-review"` and a valid `documentText`
  (e.g., `nda-01-clean-mutual.md`).
- **Expected behavior**: the endpoint filter rejects the request with `401 Unauthorized` (no/expired token) or
  `403 Forbidden` (authenticated but not entitled) **before** `ActionRunner` is invoked. The NDA-REVIEW Action never
  executes; no Azure OpenAI call is made; no `{overallRisk, flaggedSections}` output is produced.

## Why this belongs in the closed set

Per this task's `<constraint source="project">`, the closed set must be exhaustive, not just happy-path NDAs. An
advisory capability that is powerful (full-reasoning, Reasoning-tier model) must also be provably gated — an
unauthorized caller must never reach the LLM call, both for cost/rate-limit reasons (ADR-016) and because NDA content
is sensitive client material (ADR-015).

## How this case is graded

This is the one case in the closed set that `metrics/citation_accuracy.py` does **not** grade (there is no
`documentText` and no LLM output to check citations against). It is a **documentation-and-traceability** entry: the
expected contract is declared here so that (a) the closed set is visibly complete per the task's acceptance
criteria, and (b) a future `tests/integration/auth/**` test (the ADR-038 KEEP path for authorization behavior) has a
citable, pre-agreed expected-behavior spec to assert against. Authoring or running that C#/xUnit integration test is
explicitly **out of scope** for this task (own ONLY the eval assets named in the task's HARD RULES) — it belongs
under `tests/integration/auth/**`, a different KEEP path with a different owner/task.

## Expected signal (for `legal-eval-config.yaml`)

```yaml
httpStatus: [401, 403]
actionExecuted: false
azureOpenAiCallMade: false
```
