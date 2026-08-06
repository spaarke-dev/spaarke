# Task 050 — Daily-Briefing `kind=suggestion` producer, grounded + gated BEFORE outbox write (FR-15): Implementation Notes

> **Status**: ✅ Completed 2026-07-22. Phase 5 start — the proactive leg of Daily Briefing. FULL rigor (opus/high). Full BFF suite green. Both Step 9.5 gates CLEAN. NFR-03 invariant (nothing ungrounded/ungated reaches the spine) enforced + tested.

## What shipped

| Artifact | Change |
|---|---|
| `Configuration/SuggestionGateOptions.cs` (NEW) | Proactive-gate policy dial: `Enabled` (default **false** — deny-by-default, NFR-03 kill-switch), `MaxPerRun` (3), `TtlHours` (24). Bound from `Notifications:Suggestions`. |
| `Services/Ai/Narrators/DailyBriefingSuggestionProducer.cs` (NEW) | The producer, a SIBLING of `DailyBriefingNarrator` (never inside it). Per candidate (from the collected high-priority items): **Gate 1 grounding (ADR-039)** — admissible iff it traces to a real item (non-empty EntityType, parseable EntityId, Name); **Gate 2 proactive gate (ADR-041, origin=proactive)** — admit iff `Enabled` AND confirm-worthy by declared reason (`HighPriority \|\| Monitor`). BOTH pass → exactly one `kind=suggestion` outbox row (task 013 `SuggestionEnvelope`) via the task-012 outbox service, then best-effort task-020 ping (outbox-before-ping). Fail EITHER → zero rows + logged. Volume-capped by `MaxPerRun`. Non-fatal outer try/catch (NFR-03/05). Concrete sealed, Scoped. |
| `Services/Ai/Narrators/DailyBriefingCompositeService.cs` | Gained an OPTIONAL trailing ctor param `DailyBriefingSuggestionProducer? suggestionProducer = null` (024 pattern → Null peer + existing tests construct unchanged). `RenderAsync` calls `ProduceAsync(systemUserId, highPriorityItems, ct)` as a sibling step after collect, BEFORE the empty-narrative short-circuit (so a briefing with high-priority items but no channel narrative still surfaces suggestions). |
| `Infrastructure/DI/AnalysisServicesModule.cs` | `Configure<SuggestionGateOptions>` + `AddScoped<DailyBriefingSuggestionProducer>` (compound-ON block, before the composite). DI auto-resolves the composite's optional param. |
| `tests/integration/seam/Notifications/DailyBriefingSuggestionProducerSeamTests.cs` (NEW, 5 tests) | Vertical-slice seam over the REAL `OutboxService` (doubling only Dataverse + SignalR): grounded+gated→1 row + full envelope shape + ping; ungrounded→0; gate-disabled→0; not-confirm-worthy→0; `MaxPerRun` cap. |

## Design decisions (documented)

### The ADR-041 gate — reuse the discipline, not the chat machinery (owner decision 2026-07-22)
`PendingPlanManager` is the platform's ONE confirmation gate but is chat/SSE-shaped (request-Scoped, Redis suspend/resume, needs a live `ChatSession`). A batch Daily-Briefing producer has no session. **This was escalated per the POML NFR-03 trigger; owner chose "reuse the gate discipline"** (the task-041 precedent): a declared-metadata admit decision tagged `origin=proactive` via `SuggestionGateOptions`, WITHOUT re-entering the chat/Redis machinery and WITHOUT forking a bespoke scoring decider. The emitted outbox row *represents* the pending confirmation; the actual user confirm happens downstream (051 renderer → 052 dispatch). §6.5 path-A deviation, documented here + in the DI comment.

### Grounding source = the collected high-priority items (no re-fetch)
Candidates derive from `HighPriorityItemDto[]` (the collector's `CollectHighPriorityAsync` output the composite already has), each carrying `EntityType` + record `EntityId` + `Name`. `PriorityItemDto` (category/title, NO record id) is **not** a valid grounding source for a regarding-bound suggestion, so it is not used. Title = "Review {Name}", RegardingRecordId = EntityId, ActionHint = "review" — every fact traces to collected data (grounded by construction; the grounding gate rejects incomplete/unparseable items).

### Deny-by-default (NFR-03)
`SuggestionGateOptions.Enabled` defaults **false** — in production the spine carries NO proactive suggestion until an operator enables it (mirrors task 041's confidence-0 posture). The seam test enables it explicitly to exercise the write path.

### Wired into `RenderAsync` only (not `EmailAsync`)
The suggestion producer runs on the interactive widget render leg (where a user sees + acts on suggestions), not the email push-out leg. Intentional; noted for downstream 051/052.

### Test shape — consolidated into the seam test (tests/CLAUDE.md)
The POML lists "unit tests" + "seam test". `tests/CLAUDE.md` is integration-first and bans mock-heavy unit tests (B7/B15). A separate mock-only unit file for the grounding/gate branches would be scaffolding-class (deleted at `/test-diet`). So the grounded / ungrounded / ungated / not-confirm-worthy / cap cases all live in the seam test against the **real** `OutboxService` — the honest shape covering acceptance criteria 1/2/3/5/6. Directional-step deviation, documented.

## Acceptance — all 8 criteria met
1. ✅ Grounded + gate-enabled → exactly one `kind=suggestion` row with `actionHint`+`expiresAt` per the 013 envelope (seam test 1).
2. ✅ Ungrounded (unparseable id) → zero rows regardless of gate (seam test 2).
3. ✅ Grounded but ungated (disabled OR not-confirm-worthy) → zero rows + logged (seam tests 3, 4).
4. ✅ `DailyBriefingNarrator` unchanged — the producer is a sibling; the narrator writes nothing (full suite passes its tests).
5. ✅ Outbox write is explicit via the task-012 service — no path writes via `DailyBriefingNarrator`.
6. ✅ `tests/integration/seam/**` exercises candidate → grounding → gate → real outbox write / no-write.
7. ✅ Publish **46.10 MB incl-PDB** ≤60 (unchanged from 042; no package added → 0 NEW HIGH CVE; the `System.Security.Cryptography.Xml` HIGH is pre-existing transitive, baseline-unchanged); Placement Justification stated.
8. ✅ All existing `DailyBriefingNarrator`/`Collector`/`CompositeService` tests pass unmodified (full suite 8869/0).

## Verification
- `dotnet build`: 0 errors. New seam tests: 5/5. Full BFF suite: **8869 passed / 0 failed / 101 skipped** (behavior-neutral; the composite resolves its new optional dep, narrator untouched).
- Step 9.5: code-review CLEAN (1 info — RenderAsync-only wiring, intentional); adr-check CLEAN (ADR-039 grounding, ADR-041 discipline-reuse path-A, ADR-013 no-AI-injection, ADR-032 deny-by-default + null-object, ADR-041/043 outbox-before-ping, ADR-038 real-outbox seam, NFR-03 enforced).
- conflict-check: CLEAN — no open PR touches BFF `Ai/Narrators/**`.

## For downstream
- **Task 051 (suggestion renderer branch)**: consumes the `kind=suggestion` outbox row this producer writes — renders it in the Assistant via the `@spaarke/notifications` client kind-router (ADR-021 dark mode); `actionHint` drives the render + the dispatch it re-enters.
- **Task 052 (suggestion dispatch parity)**: the confirm/act path — where `actionHint` re-enters dispatch and the user confirmation actually executes (the "gate" the outbox row represented). Seam-test dispatch parity there.
- **To enable in an environment**: set `Notifications:Suggestions:Enabled=true` (deny-by-default until then).
