# Task 062 - Know becomes narrative; Critical Today retired; LLM Top action removed

Branch `task/ontology-062`. Spec section 6 ADR tensions: none new (ADR-039 path A holds: Policy decides what is true, the Binding what executes; ADR-013: no AI-internal type is injected into CRUD code).

## What changed

| Area | Change |
|---|---|
| Worklist narrative (new) | `WorklistWidget` shows the Assistant's summary of the list's COUNTS (`worklistNarrative.ts`, `useWorklistNarrative.ts`, `createWorklistService().narrate`) through the existing `POST /api/ai/daily-briefing/narrate`. The request is counts only: no record name, no date, no item. Hidden under any filter (W-10), tagged as written by the Assistant, fails closed if the entity-name scrub removed a term. Both hosts (`worklist.registration.ts`, `WorklistWorkspaceWidget.tsx`) get it through the service they already build; neither passes `narrative`. |
| Critical Today (retired) | `HighPrioritySection` deleted; the "Critical" tile, the flagged-record block of the Briefing email, `highPriorityItems` / `highPriorityFailedEntityTypes` removed from the client and from the `/render` and `/narrate` responses. |
| `sprk_highpriority` / `sprk_monitor` | `sprk_highpriority` is the second rank key in `SignalReadService` (D-42, already built and tested: `SignalReadRouteTests.LaneOrder_IsSeverityThenHighPriorityThenOldestThenRecordNumber_AndRepeatable`). `sprk_monitor` is a SUBSCRIPTION to Know: `DailyBriefingCollector.WithMonitoredSubscription` adds a "Monitored records" Know category (a count) to the Briefing. A record that is only `sprk_highpriority` adds nothing to it. |
| Top action (removed) | `TldrResult.TopAction`, the TL;DR schema field, the prompt instruction, the playbook text, the client render and the email line are gone. A deployed `BRIEF-NARRATE-TLDR` row not yet re-synced may still emit one: the narrator ignores it (test). |
| Do volume | `DailyBriefingCollector.BuildNarrateRequest` sends no `priorityItems`; `BuildTldrFacts` takes record names from Know channels and caller-supplied priority titles only. Do channels contribute counts. |
| Guards | `tests/unit/domain/Signals/WorkItemOrderAndMembershipTests.cs` (the Signal read route reaches no model/narrator/briefing code; nothing writes `sprk_rankscore`; no seeded policy raises the mere creation of a matter, project or document), with must-fire and must-not-fire controls. Client: `worklistNarrative.test.tsx` (order/filter files reach no narrative code), `criticalTodayRetired.test.tsx`. |

## Deviations from the POML

1. **Know in "the narrative or the Context pane"** (test "Know items are not rows"). Know cannot be NAMED by the worklist narrative: amended W-10 says it may name nothing absent from the list, and a Know record is not on the list. So the worklist narrative carries Do/Decide VOLUME only, and Know awareness stays in the Briefing TL;DR (counts per Know channel plus the monitored-records count) and its channel rows, which task 064 replaces. There is no Context-pane feed for Know in the code today; building one is a separate piece of work (see P-1).
2. **Narrative source.** The brief does not say where the worklist narrative is written. I used the existing `/narrate` over client-derived counts (no new route, no DI, no package), so the "Do volume" and "names nothing off the list" criteria hold by construction: the model is given no name to repeat.

## Prepared live command (NOT run; the main session applies it)

The deployed `BRIEF-NARRATE-TLDR` action row (`ce299eb4-fc70-f111-ab0e-7ced8ddc4cc6`) still carries the old output schema and prompt. Until it is re-synced the narrator ignores a returned `topAction`, so nothing breaks, but the model is still asked for one:

```
pwsh ./scripts/dataverse/Sync-BriefNarrateOutputSchemas.ps1 -IncludeSystemPrompt -DryRun
pwsh ./scripts/dataverse/Sync-BriefNarrateOutputSchemas.ps1 -IncludeSystemPrompt
```

`-IncludeSystemPrompt` (new switch) patches `sprk_systemprompt` for the TL;DR row only; without it the script behaves as before (output schemas only).

## Out-of-scope findings (D-106: reported, not fixed)

- **P-1** There is no Know feed for the Console's Context pane; "Know -> narrative plus the Context pane" has no Context-pane half to wire to. Needs its own design.
- **P-2** `src/client/shared/Spaarke.DailyBriefing.Components/test/legalWorkspaceSectionRegistry.test.ts` and `test/worklistSectionRegistration.test.tsx` import LegalWorkspace source and cannot compile in a package-only install (no `react` / `@spaarke/communication-components` resolution from `src/solutions/LegalWorkspace`). They fail identically before this change.
- **P-3** `src/client/shared/Spaarke.DailyBriefing.Components/tsconfig.test.json:25` points `@spaarke/ui-components/types/LookupTypes` at `Spaarke.UI.Components/dist/...`, so three suites (`DailyBriefingApp.openRecord`, `emailShareDraft`, `criticalTodayRetired`) need that package built first. CI builds it; a fresh local checkout does not.
- **P-4** `DailyBriefingNarrator.BuildAllowList` (`Services/Ai/Narrators/DailyBriefingNarrator.cs:345`) does not include `TldrFacts.RecordNames`, so a caller-supplied `tldrFacts` naming a record absent from `priorityItems`/`channels` would have that name scrubbed. The worklist narrative sends no names, so it is unaffected.
