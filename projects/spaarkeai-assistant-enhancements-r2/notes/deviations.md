# Deviations Log — spaarkeai-assistant-enhancements-r2

## Task 001 (FR-E1) — retained `SuggestionCard.tsx`

**Date**: 2026-08-05
**Spec instruction**: FR-E1 "delete `useSuggestionCards.tsx` + `SuggestionCard.tsx` (+ tests)".
**Deviation**: Deleted `useSuggestionCards.tsx` (the spine controller = the banner/card-stack), but **retained `SuggestionCard.tsx`** (the presentational component) and its (trimmed) test.
**Why**: `SuggestionCard.tsx` is still imported and rendered by `useRerunFullAnalysisCard.tsx` (the client-local "Rerun a full analysis" card offered after a QUICK-depth review — declared at `ConversationPane.tsx:712`, rendered at `:2490`). That card is a **separate keep-surface**, not the spine-driven notification surface FR-E1 targets. Deleting `SuggestionCard.tsx` would have broken the build, contradicting the task's own acceptance criterion "the code page builds without type/import errors." The two acceptance criteria (delete SuggestionCard.tsx AND build passes) were internally contradictory given the shared dependency; FR-E1's actual goal (remove the spine-driven banner + card stack) is fully met without deleting the shared component.
**Verification**: typecheck surface-owned 0 errors; `SuggestionCard.test.tsx` 3/3 pass; independent code-review + adr-check gate PASS; spine / NotificationsClient / useConsumerChips / Daily Briefing confirmed untouched.
**Open question for owner**: if the intent was also to remove the "Rerun a full analysis" card, that is a separate scope item (not in FR-E1) — flag it and it can be a follow-on task.

## Task 021 (FR-B2) — Option C (new column), overriding FR-B2 "no deploy" — §6.5 Path A

**Date**: 2026-08-05 (owner-approved)
**Spec conflict**: FR-B2 says context-type tags are "analyst-editable, **no deploy**"; spec §11 table says "add a tag **column/field**". These contradict.
**Discovery**: `sprk_playbookconsumer` / `Binding.cs` has **no** context-type field today. Closest existing fields are `sprk_surfaces` (placement vocabulary — semantically wrong) and `sprk_matchconditions` (general JSON predicate, evaluated by `ConsumerRoutingService.TryMatchConditions(json, IRoutingContext)`). So *some* new carrier is required regardless; even the "reuse match-conditions" option needs a BFF line to inject `contextType` into the routing context (not truly "no deploy").
**Decision (§6.5 Path A — project-scoped exception)**: implement **Option C** — a dedicated first-class column. FR-B2's "no deploy" rested on the wrong premise that a field existed; the functional/technical need (discoverable, type-safe, first-class context-type tag aligned to the task-020 closed set) wins. This is what §11 anticipated.
**Re-scope of task 021** (was STANDARD/data-only): now **FULL rigor**, tags += `bff-api, dataverse`. Work items:
1. New Dataverse column on `sprk_playbookconsumer` (e.g. `sprk_contexttypetags`, multi-value/CSV of the closed set) via `dataverse-create-schema`.
2. `Binding.cs` reads it (new `ContextTypeTags` field) + `ConsumerRoutingService` maps it (~line 857 attribute read) + filter logic.
3. Seed tag values on the relevant Bindings + author the Reanalyze Binding (FR-D11 data).
4. BFF redeploy (publish-size check).
Downstream: task 022's proactive turn filters candidate Bindings by the active tab's `contextType` against this field.
