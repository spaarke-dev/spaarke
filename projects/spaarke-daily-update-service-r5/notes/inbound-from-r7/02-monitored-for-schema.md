# 02 — "Monitored For" schema

> **Priority**: HIGH — data model + semantics
>
> **Source**: R7 W12 UAT feedback, 2026-07-01
>
> **Scope**: Cross-cutting schema addition (7 entities), replacing the binary `sprk_monitor` Boolean

## Verbatim operator feedback

> "NOTE: we don't currently have a set of 'monitored for' but we should create that…note it and for next project"

Given in the context of the R7 W12 HighPriority section rewrite. Operator observed that the section shows WHICH records are flagged HighPriority/Monitor, and asked what **why** they're being monitored — the current binary flag doesn't carry that information.

## The problem

Today `sprk_monitor` is a Boolean on 7 entities (Matter, Project, Invoice, Document, Workassignment, Event, Todo). The flag captures **that** a record is being watched but not **why**. The Daily Briefing HighPriority section shows a "Reason" chip that reads "Monitor" or "HighPriority + Monitor" — descriptive of the flags but useless for prioritization.

The operator wants a **semantic reason** attached to the Monitor state so:

- The HighPriority section reason chip becomes actionable ("Awaiting reply", "Budget review", "Regulatory deadline")
- Filtering/sorting by reason becomes possible (e.g., "show only records I'm monitoring for a regulatory deadline")
- Analytics can group by reason to surface systemic patterns

## Proposed schema

New Choice (global or local — see decision below) option set. Example candidate values:

| Value | LogicalName suffix | Description |
|---|---|---|
| Awaiting Reply | `awaitingreply` | Waiting for a response from external party |
| Awaiting Approval | `awaitingapproval` | Internal approval required |
| Budget Review | `budgetreview` | Financial threshold monitoring |
| Regulatory Deadline | `regulatorydeadline` | External-imposed deadline |
| Client Sensitive | `clientsensitive` | Extra visibility for client relationship |
| Escalation Watch | `escalationwatch` | Prior escalation, monitoring for recurrence |
| High Value | `highvalue` | Financial or strategic importance |
| Personal Followup | `personalfollowup` | User's own reminder |
| Other | `other` | Fallback (with optional free-text explanation) |

Operator to confirm the value list before schema deploy. The list above is a first-cut based on legal-operations context, not the operator's authored list.

## Design questions for R5

### 1. Multi-select vs single-select?

A record could legitimately be monitored for multiple reasons (Awaiting Reply AND High Value). Options:

- **Single-select Choice**: forces user to pick the primary reason. Simpler schema, harder for edge cases.
- **MultiSelect Choice**: records can carry multiple reasons. Enables richer filtering but more UI complexity.
- **Primary + Notes**: single-select `sprk_monitorreason` + `sprk_monitornotes` (Memo) for free-text elaboration.

Recommend Primary + Notes — the vast majority of records will have one dominant reason; free-text handles the edge case without complicating the schema.

### 2. Global option set vs per-entity?

The 7 entities share the same reason semantics (Awaiting Reply means the same on a Matter as on an Event). Global option set is the right call — one option set (`sprk_monitorreason`) referenced from a lookup column on each entity.

Follow the pattern used elsewhere for shared choices.

### 3. Replace `sprk_monitor` or add alongside?

Two paths:

- **Replace**: `sprk_monitor` becomes `sprk_monitorreason` — a value of `null` means "not monitored", any set value means "monitored, and here's why". Cleanest but data migration required (translate all existing `sprk_monitor = true` records to a default reason like `Other`).
- **Add alongside**: keep `sprk_monitor` Boolean + add `sprk_monitorreason` Choice. Reason is optional. No migration but leaves two overlapping fields.

Recommend Replace with default reason `Other` for existing `true` records — cleaner long-term. Data migration is a one-time script.

### 4. Impact on Daily Briefing rendering

Once the schema exists:

- **HighPrioritySection Reason chip** shows the reason label directly (`Awaiting Reply` chip color-coded by category) — replaces the current `HighPriority + Monitor` chip.
- **Collector query** filters or ranks by reason (e.g., surface `Regulatory Deadline` before `Personal Followup`).
- **TL;DR LLM input**: expose reason in the `items[]` payload; TL;DR can synthesize by reason ("5 records awaiting reply, 2 with regulatory deadlines this week").
- **Deterministic Activity Notes** (see doc 01): each row's Reason chip is a first-class column.

### 5. Change tracking

Should the reason field have change tracking (Dataverse audit) enabled? Legal-ops context suggests yes for Matter, Project, Invoice; possibly no for Event, Todo (higher change volume). Operator to decide per entity.

## Impact on R7 code

R7 shipped these that R5 will need to adjust:

- `DailyBriefingCollector.cs` — HighPriorityItemDto.Reason is a `string` derived from Boolean flags. R5 replaces derivation with a direct read of `sprk_monitorreason` choice label.
- `HighPrioritySection.tsx` — `reasonToLabel` helper collapses to a plain read of the choice label; badge color-coding based on reason category.
- `DailyBriefingEndpoints.cs` — DTO extends with reason value + label; consumer selects by reason.

## Migration plan sketch

1. **Deploy schema** (Choice option set + column on each of 7 entities) via `dataverse-deploy`
2. **Backfill**: script iterates all records where `sprk_monitor = true` and sets `sprk_monitorreason = Other`
3. **UI updates on parent forms**: expose the Choice field on Matter/Project/Invoice/Document/Workassignment/Event/Todo main form ribbons
4. **Daily Briefing changes**: rewire collector + widget as above
5. **Retire `sprk_monitor` Boolean** in a follow-up release after user validation

## Test cases

1. **Backfill correctness**: all pre-migration `sprk_monitor=true` records have `sprk_monitorreason='Other'` post-migration; no `sprk_monitor=false` records get a reason.
2. **Reason-driven ranking**: HighPriority section orders records by reason severity (Regulatory Deadline > Awaiting Reply > Budget Review > Personal Followup).
3. **Filter round-trip**: BFF query with `sprk_monitorreason eq {ID}` returns only records with that reason.
4. **Choice label localization**: labels render correctly in EN + one other language (if multi-language is in scope for R5).

## References

- Current binary flag pattern: [`src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs) `QueryHighPriorityGenericAsync` — filters on `sprk_highpriority` + `sprk_monitor`
- Reason chip rendering: [`src/client/shared/Spaarke.DailyBriefing.Components/src/components/HighPrioritySection.tsx`](../../../../src/client/shared/Spaarke.DailyBriefing.Components/src/components/HighPrioritySection.tsx) — `reasonToLabel` helper
- Schema-deploy skill: `.claude/skills/dataverse-deploy/SKILL.md`
- 7 entities schema references: [`docs/data-model/`](../../../../docs/data-model/)
