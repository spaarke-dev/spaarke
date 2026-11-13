# Contract baseline: signal worklist, decision-plan, suppression and decision routes

> Task 011 of `spaarkeai-mcp-bff-api-r1` (FR-09, success criterion 6). Recorded from the code **before** the decision-capability extraction (012-017). No file under `src/` was changed by this task.
> **Start commit:** `16e5bf1ba7a6239e3ddb82ff541573aea431e394` (`work/spaarkeai-mcp-bff-api-r1`; `git diff --stat -- src` is empty at record time).

## What is frozen

Each case is one file `tests/integration/contract/Api/Signals/Baselines/{case}.json` holding the HTTP **status**, the **Content-Type** and the response **body bytes** (`body`; `bodyPretty` is for the reader and never compared). 63 cases.

| Routes | Test file | Assembly | Harness |
|---|---|---|---|
| decision-plan, suppression, decision | `tests/integration/contract/Api/Signals/SignalRoutesContractBaselineTests.cs` (a nested class of `DecisionCommitServiceTests`, so it reuses that class's private `Rig`) | `Sprk.Bff.Api.Tests` | the REAL `MapDecisionPlanEndpoints` / `MapDecisionCommitEndpoints` on a minimal TestServer host (routing, `RequireAuthorization`, binding, handler, JSON writer), the real `SignalCoreRecordAccess`, `DecisionPlanService`, `DecisionCommitService`, `SignalSuppressionService` over the Rig's module-boundary fakes |
| worklist | `tests/integration/contract/Api/Signals/SignalWorklistRouteContractBaselineTests.cs` (a part of `SignalReadRouteTests`, reuses its `World`) | `Sprk.Bff.Api.IntegrationTests` (csproj link; removed from the unit project's contract glob) | the full `Program` pipeline of `SignalReadRouteTests` |
| both | `GoldenContract.cs` | compiled into both | the golden-file harness |

**Golden-file rules.** A missing golden file FAILS the test; nothing writes a file in a normal or CI run. A baseline is recorded only with `SPAARKE_GOLDEN_RECORD=1` on a developer machine (ignored when `CI`, `TF_BUILD` or `GITHUB_ACTIONS` is set). **Later tasks never edit a golden file to make a refactor pass (FAILURE-MODES AP-9); a REST change that needs a different golden is an escalation to the owner.** Run without `--no-build` after editing tests (G-12).

**Normalisation (ADR-019: only values that change per run), one list in `GoldenContract.cs`:**
1. `VolatileFields` = `correlationId`, `traceId` (value replaced by `<volatile>`; the request's trace id).
2. Generated GUIDs, **worklist cases only** (`generatedGuids: true`): the `World` fixture mints ids with `Guid.NewGuid()`, so each GUID becomes `<guid-N>` (N = order of first appearance; two references to one id stay two references to one id). The decision-plan, suppression and decision cases use fixed ids and normalise nothing else.
Nothing else is normalised: key order, casing, nulls and whitespace are compared as written.

## Reason codes and baseline cases

Used by task 017 for its MCP mapping tests. "reasonCode on the wire" is the ProblemDetails `reasonCode` extension.

### GET /api/v1/signals/worklist

| HTTP | reasonCode on the wire | Baseline case (`Baselines/{case}.json`) |
|---|---|---|
| 200 | (none) | `worklist.200.closed-earlier` |
| 200 | (none) | `worklist.200.closed-today` |
| 200 | (none) | `worklist.200.decided-empty` |
| 200 | (none) | `worklist.200.lanes` |
| 200 | (none) | `worklist.200.suppressed` |
| 400 | (none) | `worklist.400.unknown-set` |
| 401 | (none) | `worklist.401.anonymous` |
| 403 | `sdap.access.deny.caller_unresolved` | `worklist.403.caller-unresolved` |
| 503 | `ontology.signal.worklist_unavailable` | `worklist.503.too-many-signals` |
| 503 | `ontology.signal.worklist_unavailable` | `worklist.503.unavailable` |

### GET /api/v1/signals/{id}/decision-plan

| HTTP | reasonCode on the wire | Baseline case (`Baselines/{case}.json`) |
|---|---|---|
| 200 | (none) | `plan.200.create-budget` |
| 200 | (none) | `plan.200.do-lane` |
| 200 | (none) | `plan.200.path-b` |
| 401 | (none) | `plan.401.anonymous` |
| 403 | `sdap.access.deny.caller_unresolved` | `plan.403.caller-unresolved` |
| 404 | `sdap.access.deny.record_unavailable` | `plan.404.uniform` |
| 422 | `ontology.decisionplan.refused` | `plan.422.refused` |
| 503 | `ontology.signal.access_unavailable` | `plan.503.access-unavailable` |

### GET /api/v1/signals/{id}/suppression

| HTTP | reasonCode on the wire | Baseline case (`Baselines/{case}.json`) |
|---|---|---|
| 401 | (none) | `suppression.401.anonymous` |
| 403 | `sdap.access.deny.caller_unresolved` | `suppression.403.caller-unresolved` |
| 404 | `sdap.access.deny.record_unavailable` | `suppression.404.uniform` |
| 503 | `ontology.signal.access_unavailable` | `suppression.503.access-unavailable` |
| 503 | `ontology.suppression.unavailable` | `suppression.503.unavailable` |

### POST /api/v1/signals/{id}/decision

| HTTP | reasonCode on the wire | Baseline case (`Baselines/{case}.json`) |
|---|---|---|
| 200 | (none) | `decision.200.dismissal` |
| 200 | (none) | `decision.200.recorded` |
| 200 | (none) | `decision.200.replayed` |
| 401 | (none) | `decision.401.anonymous` |
| 403 | `decision.action.not_authorized` | `decision.403.action-not-authorized` |
| 403 | `ontology.decision.not_authorized` | `decision.403.not-authorized` |
| 403 | `ontology.decision.signal_subject_missing` | `decision.403.signal-subject-missing` |
| 403 | `sdap.access.deny.caller_unresolved` | `decision.403.caller-unresolved` |
| 404 | `sdap.access.deny.record_unavailable` | `decision.404.uniform` |
| 409 | `inquiry.disposition_required` | `decision.409.inquiry-disposition-required` |
| 409 | `ontology.decision.decided_in_invoice_review` | `decision.409.decided-in-invoice-review` |
| 409 | `ontology.decision.review_in_progress` | `decision.409.review-in-progress` |
| 409 | `ontology.decision.signal_not_open` | `decision.409.signal-not-open` |
| 422 | `ontology.decision.actions_do_not_match_plan` | `decision.422.actions-do-not-match-plan` |
| 422 | `ontology.decision.actions_exclude` | `decision.422.actions-exclude` |
| 422 | `ontology.decision.also_resolve_not_allowed` | `decision.422.also-resolve-not-allowed` |
| 422 | `ontology.decision.core_changed` | `decision.422.core-changed` |
| 422 | `ontology.decision.core_unsupported` | `decision.422.core-unsupported` |
| 422 | `ontology.decision.dismissal_detail_required` | `decision.422.dismissal-detail-required` |
| 422 | `ontology.decision.dismissal_reason_required` | `decision.422.dismissal-reason-required` |
| 422 | `ontology.decision.dismissal_reason_unknown` | `decision.422.dismissal-reason-unknown` |
| 422 | `ontology.decision.dismissal_with_actions` | `decision.422.dismissal-with-actions` |
| 422 | `ontology.decision.invalid_parameter` | `decision.422.invalid-parameter` |
| 422 | `ontology.decision.item_owner_unsupported` | `decision.422.item-owner-unsupported` |
| 422 | `ontology.decision.not_confirmed` | `decision.422.not-confirmed` |
| 422 | `ontology.decision.record_refused` | `decision.422.record-refused` |
| 422 | `ontology.decision.review_changed` | `decision.422.review-changed` |
| 422 | `ontology.decision.review_invalid` | `decision.422.review-invalid` |
| 422 | `ontology.decision.unresolved_action` | `decision.422.unresolved-action` |
| 422 | `ontology.decisionplan.refused` | `decision.422.plan-refused` |
| 500 | `decision.action.write_failed` | `decision.500.step-failed` |
| 500 | `ontology.decision.not_resumable` | `decision.500.not-resumable` |
| 500 | `ontology.decision.record_write_failed` | `decision.500.record-write-failed` |
| 503 | `ontology.decision.core_unsupported` | `decision.503.core-unsupported` |
| 503 | `ontology.decision.idempotency_unavailable` | `decision.503.idempotency-unavailable` |
| 503 | `ontology.decision.idempotency_unavailable` | `decision.503.idempotency-unavailable-writer-identity` |
| 503 | `ontology.decision.item_owner_unsupported` | `decision.503.item-owner-unsupported` |
| 503 | `ontology.decision.record_refused` | `decision.503.record-refused` |
| 503 | `ontology.decision.secure_family_unavailable` | `decision.503.secure-family-unavailable` |
| 503 | `ontology.decision.signal_unreadable` | `decision.503.signal-unreadable` |

## Observations recorded as they are today (the baselines freeze them)

- **Worklist 503 `too_many_signals` is not on the wire.** `SignalReadService` returns the internal reason `too_many_signals` (more than `MaxPages` pages), but `SignalReadEndpoints.GetWorklistAsync` answers every `Unavailable` with the same 503 body, `ontology.signal.worklist_unavailable` (the internal reason is never returned). `worklist.503.too-many-signals` pins that: no truncated list, and no new reason code on the wire. A change that surfaces `too_many_signals` as a wire code is a REST change and an escalation.
- **A core-record read fault on a Signal that has a core record is the uniform 404**, not 503 (`SignalCoreRecordAccess` fails closed). The 503 `ontology.signal.access_unavailable` of the plan and suppression routes is reached through a no-core Do item whose subject read faults (`plan.503.access-unavailable`, `suppression.503.access-unavailable`).
- **The uniform 404 carries `reasonCode` `sdap.access.deny.record_unavailable`** and is byte-identical (after normalisation) for an unreadable Signal and an unreadable core record: asserted in `plan.404.uniform` and `decision.404.uniform`.
- **`ontology.decision.not_resumable` is a 500** (status stays `Failed`, `replayed` true), not a 422.
- **The writer-identity (WhoAmI) fault answers `ontology.decision.idempotency_unavailable` (503)**, not `record_refused`: pinned in `decision.503.idempotency-unavailable-writer-identity`. `record_refused` 503 is the Decision Record owner resolver faulting (`decision.503.record-refused`).
- **`ontology.decision.core_unsupported` is 422 when the catalog row is gone and 503 when the read faults**: two cases.
- **A taken action's refusal or failure carries the action's own code** (`decision.action.not_authorized` 403, `decision.action.write_failed` 500, with `failedStep`, `landed`, `steps`...), not an `ontology.decision.*` code. One representative of each is baselined; the per-executor codes are pinned in `DecisionActionExecutorsTests`.
- The success body of the decision route serialises `status` as the enum number (`0` = Recorded).

## Not baselined (and why)

| Item | Reason |
|---|---|
| `ontology.decision.gate_undeclared` (422) | Not reachable through the route: every action in the closed catalog declares a gate, so reaching it needs a production change (escalation trigger 1). |
| `GET .../suppression` 200 and 503 `ontology.signal.partition_undetermined` | The suppression count needs the Signal-grain store of `SignalSuppressionTests` (private to that class: caller-scoped grain read plus app-only Decision Record facts). Only the fault answers reachable from the Rig are baselined (`suppression.503.unavailable`). Filed as a follow-up for the operator: the 200 shape of the suppression route has no byte-level baseline, so a change to `SuppressionResponse` is not guarded by this task. |
| Executor-specific refusal codes (`DecisionActionReasons.*` other than `not_authorized` and `write_failed`) | Per-executor, pinned by `DecisionActionExecutorsTests`; the route maps them through the same `Problem(...)` path as the two baselined. |
| `ontology.decision.review_invalid` for an empty review id | Same code and status as the baselined AlsoResolve-cap case; only the `detail` text differs. |
| Framework errors: malformed JSON body (400), non-GUID `{id}` (404), unsupported media type (415), `dataverse-query` rate limit (429), the global exception handler (500) | Produced by ASP.NET Core and `MiddlewarePipelineExtensions`, not by the four handlers; the minimal host registers no limiter. |
| Worklist `decided` set with rows | `worklist.200.decided-empty` pins the envelope; populated `decided` rows share the row type frozen by `worklist.200.lanes` plus the `decision` member (covered by `SignalReadRouteTests`). |

## Seeding proof (acceptance criterion 4)

Changed one character of a ProblemDetails title in `DecisionCommitEndpoints.ToHttp` (`"Conflict"` -> `"Conflicd"`), rebuilt, ran `SignalRoutesContractBaselineTests`: **4 failed** (`decision.409.review-in-progress`, `.signal-not-open`, `.inquiry-disposition-required`, `.decided-in-invoice-review`), 48 passed. The file was restored with `git checkout`; `git diff --stat -- src` is empty and the suite is green again (53/53). A missing golden file (`plan.200.path-b.json` moved away) fails with "Golden file missing ..." and writes nothing.

