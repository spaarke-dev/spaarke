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
2. `Binding.cs` reads it (new `ContextTypeTags` field) + `ConsumerRoutingService` maps it in `MapBinding` (reusing `ParseSurfaces`, the generic CSV splitter) + adds `sprk_contexttypetags` to the `Columns` array. **No filter/selection logic in 021.**
3. Seed tag values on the relevant Bindings + author the Reanalyze Binding (FR-D11 data).
4. BFF redeploy (publish-size check).

**021/022 boundary (refined 2026-08-05, resume)**: 021 is the **carrier + data** only (column, `Binding.ContextTypeTags`, seed, Reanalyze row). The active-tab candidate **filter** — chips scoped to the focused tab's `contextType` against `ContextTypeTags` — lives in the proactive suggestion turn and is **task 022** (opus/xhigh). This keeps 021's acceptance criteria a closed set and respects §11. Column type = **String/CSV** mirroring `sprk_surfaces` (not a multi-select choice — avoids an option-value→token mapping layer).

## Task 022 (FR-B3/B5) — Option B (grounded suggest turn), overriding single-file scope

**Date**: 2026-08-06 (owner-approved: "yes option b … most robust, not easiest").
**Discovery** (022 exploration, verified): FR-B3 ("one grounded turn, AI selects/phrases ≤3 contextType-filtered chips") is **impossible from ConversationPane.tsx alone** — `CapabilityDto` (CapabilityDiscoveryEndpoints.cs:177-183) omits `ContextTypeTags` (client can't filter by contextType); no server path consumes `activeContext.contextType`; no client "fire a grounded turn on tab-open" handle. A client-only bridge would be an ADR-039-forbidden classifier.
**Options considered**:
- **A (rejected)** — deterministic catalog pre-filter: expose tags on CapabilityDto, client picks ≤3 catalog Bindings by contextType, renders their static authored chips. Cheaper/ADR-pure, but a **context-type-keyed STATIC menu** — same chips for every document tab, blind to content. **Structurally CANNOT satisfy FR-B5** ("no generic boilerplate chips for content-rich tabs") — the static menu IS the boilerplate.
- **B (chosen)** — true grounded suggest turn: a purpose-built BFF `POST …/{id}/suggest` that (1) deterministically pre-filters candidate Bindings by `ContextTypeTags` (ADR-039-permitted pre-filter, reuses 021's field), (2) runs ONE grounded turn reading the active-tab compact content that **selects + phrases** ≤3 content-specific chips, (3) returns them without polluting the transcript. Client fires it once per tab (Set&lt;string&gt; ref), caches, renders via `useConsumerChips.acceptChips`. Matches spec intent ("AI selects/phrases"); the only FR-B5-compliant option.
**Scope override**: 022 re-scoped STANDARD→**FULL**, single-file (ConversationPane) → **BFF (suggest endpoint + service + contextType pre-filter) + client**; tags += `bff-api`. Note: contextType filtering stays SERVER-SIDE, so CapabilityDto exposure is NOT needed (B keeps the tag server-only). ADR-039 clean: deterministic pre-filter + exactly one grounded decider; no classifier, no second dispatch protocol, no SprkChat fork.
