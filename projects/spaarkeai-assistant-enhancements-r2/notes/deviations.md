# Deviations Log — spaarkeai-assistant-enhancements-r2

## Task 001 (FR-E1) — retained `SuggestionCard.tsx`

**Date**: 2026-08-05
**Spec instruction**: FR-E1 "delete `useSuggestionCards.tsx` + `SuggestionCard.tsx` (+ tests)".
**Deviation**: Deleted `useSuggestionCards.tsx` (the spine controller = the banner/card-stack), but **retained `SuggestionCard.tsx`** (the presentational component) and its (trimmed) test.
**Why**: `SuggestionCard.tsx` is still imported and rendered by `useRerunFullAnalysisCard.tsx` (the client-local "Rerun a full analysis" card offered after a QUICK-depth review — declared at `ConversationPane.tsx:712`, rendered at `:2490`). That card is a **separate keep-surface**, not the spine-driven notification surface FR-E1 targets. Deleting `SuggestionCard.tsx` would have broken the build, contradicting the task's own acceptance criterion "the code page builds without type/import errors." The two acceptance criteria (delete SuggestionCard.tsx AND build passes) were internally contradictory given the shared dependency; FR-E1's actual goal (remove the spine-driven banner + card stack) is fully met without deleting the shared component.
**Verification**: typecheck surface-owned 0 errors; `SuggestionCard.test.tsx` 3/3 pass; independent code-review + adr-check gate PASS; spine / NotificationsClient / useConsumerChips / Daily Briefing confirmed untouched.
**Open question for owner**: if the intent was also to remove the "Rerun a full analysis" card, that is a separate scope item (not in FR-E1) — flag it and it can be a follow-on task.
