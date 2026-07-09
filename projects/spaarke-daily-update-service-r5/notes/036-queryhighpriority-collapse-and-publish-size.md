# Task 036 — Collapse 7 `QueryHighPriority*Async` wrappers into a spec array

> **Date**: 2026-07-09 · **FR-C7** · collector chain (033→034→036→037) · depends on 034 ✅

## What changed (behavior-preserving refactor)

`src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs`:

- **Removed** the 7 near-identical wrappers `QueryHighPriority{Matter,Project,Invoice,Document,Workassignment,Event,Todo}Async` (former lines 362-460). Grep confirms **zero** remaining named per-entity wrappers.
- **Added** `private sealed record HighPriorityEntitySpec(EntityType, IdColumn, NameColumn, DescriptionColumn?, DueDateColumn?, FallbackDueDateColumn?, KindLabel, IncludeStateFilter, ScopeToOwner)` + a static `HighPriorityEntitySpecs[]` with the 7 rows (per-entity rationale comments — invoice-date, event fallback, todo owner-scope — carried across).
- **Added** one dispatch method `QueryHighPriorityAsync(spec, systemUserId, ct)` delegating to the **existing** `QueryHighPriorityGenericAsync` (unchanged). `ScopeToOwner` threads `systemUserId` into the owner filter — only the To Do row sets it, exactly as the former `QueryHighPriorityTodoAsync(systemUserId, ct)` did.
- **Rewired** `CollectHighPriorityAsync` to `Task.WhenAll(HighPriorityEntitySpecs.Select(spec => QueryHighPriorityAsync(spec, systemUserId, ct)))`. Spec order = former call order, so the positional per-entity counts in the completion log stay correct.

`QueryHighPriorityGenericAsync`'s query logic was **not** touched (constraint honored — reuse, don't rewrite).

## Per-entity equivalence (the collapse table)

| Entity | id / name / description | due (→fallback) | stateFilter | ownerScope |
|---|---|---|---|---|
| sprk_matter | matterid / mattername / matterdescription | — | ✅ | — |
| sprk_project | projectid / projectname / description | — | ✅ | — |
| sprk_invoice | invoiceid / name / description | — | ✅ | — |
| sprk_document | documentid / documentname / documentdescription | — | ✅ | — |
| sprk_workassignment | workassignmentid / name / description | responseduedate | ✅ | — |
| sprk_event | eventid / eventname / eventdescription | finalduedate → duedate | **❌ (false)** | — |
| sprk_todo | todoid / name / description | duedate | ✅ | **✅ systemUserId** |

## Guard (equivalence test)

`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Narrators/DailyBriefingCollectorTests.cs`:
`CollectHighPriorityAsync_FansOutOverSpecArray_EachEntityKeepsItsQueryIntent` — captures every `QueryExpression` the collapsed path issues (mock at the `IGenericEntityService` boundary — the allowed module mock, not `HttpMessageHandler`) and pins, per entity: projected columns, the HighPriority-OR-Monitor flag group, `statecode` present iff state-filtered (event excluded), and `owninguser` present iff owner-scoped (To Do → SystemUserId). This is maintain-class per ADR-038 (a real per-entity regression — dropped column, lost owner-scope, wrongly state-filtered event — is exactly what it catches).

## Verification

- **Grep**: 0 remaining `QueryHighPriority{Entity}Async` named wrappers. ✅
- **Build**: `dotnet build -c Release` → 0 errors. ✅
- **Tests**: `DailyBriefingCollector*` → **17/17** (all pre-existing high-priority + de-dup + TL;DR-facts tests + the new equivalence test). ✅ No behavior change.
- **Publish size (root §10 / NFR-01)**: **45.13 MB compressed incl PDBs** — identical to the pre-refactor measurement (code-shape-only change, no size delta); under the 60 MB ceiling, below the 49.63 MB baseline. ✅
- **CVE**: no `<PackageReference>` change → no new CVE surface. ✅

## Placement decision (BFF §10 / §11)

Behavior-preserving refactor **inside** the existing collector. `HighPriorityEntitySpec` is a `private sealed record` local to `DailyBriefingCollector` — not an injected service, not a new DI registration, not a new public surface. §11 does not require a `<justification>` for pure refactor of existing surface. No new endpoint/package/dependency.
