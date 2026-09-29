# ISSUE — Daily Briefing: two description columns do not exist, and the failure is silent

> **Status**: Open, unassigned. Candidate for a focused fix project.
> **Discovered**: 2026-08-24 during `record-header-and-notepad-r2` §9 schema verification.
> **Environment verified**: `spaarkedev1` (Dataverse Web API, live).
> **Severity**: **High** — flagged Projects and Events are silently missing from every user's briefing.
> **Owner**: unassigned — for review.

---

## Summary

`DailyBriefingCollector` drives its high-priority queries off a static spec array. Two of the seven entries name a description column that does not exist on the target table:

| Line | Entity | Referenced | Exists? | Real column |
|---|---|---|---|---|
| `408` | `sprk_project` | `sprk_description` | ❌ No | **`sprk_projectdescription`** |
| `424` | `sprk_event` | `sprk_eventdescription` | ❌ No | **`sprk_description`** |

The two are, almost comically, each other's mistake — Project was given Event's column name and Event was given Project's naming pattern.

The other five specs are correct: Matter `sprk_matterdescription` ✅, Invoice `sprk_description` ✅, Document `sprk_documentdescription` ✅, Work Assignment `sprk_description` ✅, To Do `sprk_description` ✅.

---

## Evidence (reproducible)

```
GET /api/data/v9.2/sprk_projects?$top=1&$select=sprk_description            -> HTTP 400
GET /api/data/v9.2/sprk_projects?$top=1&$select=sprk_projectdescription     -> HTTP 200
GET /api/data/v9.2/sprk_events?$top=1&$select=sprk_eventdescription         -> HTTP 400
GET /api/data/v9.2/sprk_events?$top=1&$select=sprk_description              -> HTTP 200
GET /api/data/v9.2/sprk_documents?$top=1&$select=sprk_documentdescription   -> HTTP 200
GET /api/data/v9.2/sprk_matters?$top=1&$select=sprk_matterdescription       -> HTTP 200
```

---

## Affected code

[`src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs)

```csharp
// :408  Project — sprk_description does not exist on sprk_project
new(EntityProject, "sprk_projectid", "sprk_projectname", "sprk_description",
    DueDateColumn: null, FallbackDueDateColumn: null, KindLabel: "Project",
    IncludeStateFilter: true, ScopeToOwner: false),

// :424  Event — sprk_eventdescription does not exist on sprk_event
new(EntityEvent, "sprk_eventid", "sprk_eventname", "sprk_eventdescription",
    DueDateColumn: "sprk_finalduedate", FallbackDueDateColumn: "sprk_duedate",
    KindLabel: "Task", IncludeStateFilter: false, ScopeToOwner: false),
```

`DescriptionColumn` is threaded into the per-entity query at [`:454`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs#L454).

---

## Why this is worse than it looks: the failure is silent

The collector runs one query per entity in parallel and is **deliberately failure-soft** — from its own comment at [`:351-356`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs#L351):

> *"Each query returns `HighPriorityItemDto[]` and is failure-soft: on Dataverse exception the entity contributes an empty array (logged as warning) so the digest still renders."*

That resilience is a good design choice in general, but here it converts a hard 400 into **silent omission**. Every Daily Briefing renders successfully while containing **zero flagged Projects and zero flagged Events** — with no error shown to the user and nothing obviously wrong in the output. The only trace is a warning in server logs.

Users therefore cannot tell that the briefing is incomplete. That makes this a correctness-of-output problem, not just a broken query.

---

## Proposed fix

1. `:408` `sprk_description` → `sprk_projectdescription`
2. `:424` `sprk_eventdescription` → `sprk_description`
3. Validate the remaining five specs against live metadata in the same pass (they look correct; confirm rather than assume).
4. Add a startup or test-time assertion that every column named in `HighPriorityEntitySpecs` resolves against entity metadata — this class is exactly the shape that benefits from it, since a typo degrades silently rather than loudly.

---

## Open questions for review

1. **Should failure-soft behaviour stay fully silent?** A briefing that quietly drops whole entity types is hard to trust. Consider surfacing a per-entity "could not load" marker in the digest, or at minimum raising the log level from warning to error for schema-shaped failures (a 400 is a bug; a timeout is not).
2. **How long has this been shipping?** The spec array was introduced when "R5 task 036 collapsed the former 7 named wrappers into that spec array" ([`:351-352`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs#L351)). Worth checking whether the named wrappers had the right column names and the regression came in with that refactor.
3. **Are other narrators affected?** Only `DailyBriefingCollector` was examined. Sibling collectors under `Services/Ai/Narrators/` may carry the same hard-coded column lists.

---

## Blast radius

Single BFF file, two string literals. No API contract change, no schema change, no client change. This is the lowest-risk fix of the three sibling issues and has the highest user-visible payoff.

**Rough effort**: 0.25 day including the metadata assertion; less if questions 1 and 3 are deferred.

---

## Cross-references

- Discovery context: [`../discovery-checklist.md`](../discovery-checklist.md) §F · [`../../design.md`](../../design.md) §9.1
- Sibling issues from the same sweep: [`ISSUE-event-schema-drift.md`](ISSUE-event-schema-drift.md) · [`ISSUE-work-assignment-schema-drift.md`](ISSUE-work-assignment-schema-drift.md)
- BFF changes are governed by root `CLAUDE.md` §10 (BFF Hygiene) — this is a defect fix touching no endpoints, services, DI registrations, or packages, so the placement-justification obligation does not apply, but the test-update obligation does.
