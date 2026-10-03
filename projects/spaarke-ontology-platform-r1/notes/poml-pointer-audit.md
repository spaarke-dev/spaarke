# POML file-pointer audit (2026-10-03)

> Triggered by task 083, whose `<relevant-files>` pointed at `Services/RecordMatching` while the bug was
> actually in `Services/Communication/Engine`. One wrong pointer found by accident is a reason to check all
> of them, so all **402 file pointers across the 42 POMLs** were resolved against the working tree.
> `scripts/Validate-TaskPoml.ps1` does **not** check path existence - it validates XML and the canonical
> field set - so nothing would have caught these except a task hitting one.

## Why this is worth fixing up front

A task whose reference implementation cannot be found does not stop. It **re-derives** the thing - which is
exactly what root CLAUDE.md section 11 and this project's section 3.3 reuse table exist to prevent - and it
does so while believing it is reusing the canonical utility. The failure is silent and the output looks
reasonable. Two of the broken pointers were in **task 030**, a high-risk task on the critical path.

## Paths corrected (14 POMLs)

| Wrong | Actual | Affected |
|---|---|---|
| `Services/Communications/CommunicationRuleGate.cs` | `Services/Communication/CommunicationRuleGate.cs` (singular) | 020, 021, 023 |
| `Services/Communications/RuleGatedAssessedConsumer.cs` | **no such file** - the class is declared at `Services/Communication/CommunicationRuleGate.cs:222` | 032, 040, 042 |
| `Services/Dataverse/PolymorphicResolverService.cs` | **no such C# type** - see below | 030 |
| `Services/Jobs/SecureRecordIsolationCensusJob.cs` | `Services/ExternalAccess/SecureRecordIsolationCensusJob.cs` | 031 |
| `Services/Ai/Briefing/DailyBriefingCollector.cs` | `Services/Ai/Narrators/DailyBriefingCollector.cs` | 061, 062 |
| `Services/Ai/Resolvers/LookupChoicesResolver.cs` | `Services/Ai/LookupChoicesResolver.cs` | 072 |
| `Services/Signals/SignalEvaluationService.cs` | `Services/Finance/SignalEvaluationService.cs` | 030 |
| `LegalWorkspace/src/widgets/dailyBriefing.registration.ts` | `LegalWorkspace/src/sections/dailyBriefing/dailyBriefing.registration.ts` | 010, 054, 064 |
| `SpaarkeAi/src/widgets/...` | `SpaarkeAi/src/components/workspace/...` (no `widgets/` dir exists) | 054, 064 |

The `SignalEvaluationService` line citation was **accurate** - `GenerateDeterministicId(Guid matterId, int
signalType)` really is at line 241, and it really is the matter-plus-type key that a polymorphic subject
breaks. Only the directory was wrong.

## Two content defects the wrong paths were concealing

**1. Task 030 instructed an impossible call.** `PolymorphicResolverService` is **client-side TypeScript**
(`src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts`). The two C# files
that name it only reference it in comments as the counterpart they mirror. But POML 030 carried it as a
`canonical-reference`, an `ADR-024` constraint reading *"use PolymorphicResolverService.applyResolverFields"*,
a step 2 reading *"Call PolymorphicResolverService.applyResolverFields"*, and a pattern asserting *"the audit
found this utility genuinely canonical with ZERO reimplementations, so reusing it is the default."*

A C# writer cannot call it. An agent following that literally would have written a new C# polymorphic writer
while reporting that it reused the canonical utility - a **false reuse claim attached to new scope**, which is
worse than either alone. Corrected to name `Services/Workspace/TodoRegardingBuilder.cs`, the server-side
ADR-024 implementation (smart-todo-decoupling-r3) that does this exact job for `sprk_todo`, with the
TypeScript service demoted to semantic specification.

**2. Task 042's "without touching the gate" was ambiguous in a way that matters.**
`RuleGatedAssessedConsumer` is declared in **the same file** as `CommunicationRuleGate`
(`CommunicationRuleGate.cs:222`). A literal reading of *"insert the write ... without touching the gate"*
suggests avoiding that file, which is where the work has to happen. Clarified in the prompt: do not change
the `CommunicationRuleGate` class or its decision logic - but you will be editing that file.

## Deliberately NOT changed

Five pointers remain unresolved and **should**: they name files an earlier task in the chain creates -
`SignalReevaluationJob.cs` (031 to 033), `DecisionRecordWriter.cs` (040 to 042/053), `WorkItemRow.tsx`
(051 to 053). Their `role` attributes say `modify`/`canonical-reference` rather than `new`, which is a
labelling imprecision, not a broken dependency: in each case the creating task is an upstream dependency in
`<deps>`. Left as-is rather than relabelled, because the dependency order is already correct and editing
`role` on five POMLs buys nothing.

All 42 POMLs still pass `Validate-TaskPoml.ps1`: 42 clean, 0 errors, 0 warnings.
