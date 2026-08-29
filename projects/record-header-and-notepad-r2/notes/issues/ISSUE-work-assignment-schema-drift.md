# ISSUE — Work Assignment: create endpoint writes two columns that do not exist

> **Status**: Open, unassigned. Candidate for a focused fix project.
> **Discovered**: 2026-08-24 during `record-header-and-notepad-r2` §9 schema verification.
> **Environment verified**: `spaarkedev1` (Dataverse Web API, live).
> **Severity**: **Medium-High** — the endpoint returns 500 whenever the caller supplies a matter or a due date.
> **Owner**: unassigned — for review.

---

## Summary

`POST /api/v1/work-assignments` builds a `sprk_workassignment` entity using two column names that do not exist on the table:

| Referenced | Exists? | Real column |
|---|---|---|
| `sprk_matterid` | ❌ No | **`sprk_regardingmatter`** (Lookup → `sprk_matter`) |
| `sprk_duedate` | ❌ No | **`sprk_responseduedate`** (DateOnly — the *only* DateTime column on the table) |

`sprk_matterid` also violates the repo's own naming rule for lookups (documented in `docs/data-model/schema-corrections.md`), which is a useful tell that it was never verified against the table.

---

## Evidence

Live `sprk_workassignment` metadata from `EntityDefinitions` (2026-08-24) — complete `DateTime` and matter-lookup inventory:

```
DateTime : sprk_responseduedate        (DateOnly)   <- the ONLY DateTime column
Lookup   : sprk_regardingmatter, sprk_regardingproject, sprk_regardingevent,
           sprk_regardinginvoice, sprk_regardingcommunication, sprk_regardingrecordtype,
           sprk_assignedto, sprk_assignedattorney1/2, sprk_assignedparalegal1/2,
           sprk_assignedlawfirm1/2, sprk_assignedlawfirmattorney1, ...
```

No `sprk_duedate`. No `sprk_matterid`.

---

## Affected code

[`src/server/api/Sprk.Bff.Api/Api/WorkAssignmentEndpoints.cs`](../../../../src/server/api/Sprk.Bff.Api/Api/WorkAssignmentEndpoints.cs)

```csharp
// :78-79
if (request.MatterId.HasValue)
    entity["sprk_matterid"] = new EntityReference("sprk_matter", request.MatterId.Value);

// :81-82
if (request.DueDate.HasValue)
    entity["sprk_duedate"] = request.DueDate.Value;
```

The request-model XML doc at `:149` also documents `MatterId` as "(sprk_matterid lookup)" and needs correcting alongside.

---

## Observed behaviour

Both writes are **conditional**. So:

- `MatterId == null` **and** `DueDate == null` → the create succeeds. This is presumably why the defect has survived.
- Either value supplied → `CreateAsync` throws, the catch at [`:127-139`](../../../../src/server/api/Sprk.Bff.Api/Api/WorkAssignmentEndpoints.cs#L127) logs it and returns **HTTP 500 ProblemDetails** ("An error occurred while creating the work assignment").

So the endpoint is not wholly dead — it is dead exactly on the paths that carry the two most useful pieces of context. A caller that always omits both would never notice; a caller that passes a matter always gets a 500.

Note the notification side-effect ([`:101-108`](../../../../src/server/api/Sprk.Bff.Api/Api/WorkAssignmentEndpoints.cs#L101)) is *after* the create, so a failed create means no assignment **and** no notification — consistent, at least.

---

## Proposed fix

1. `sprk_matterid` → `sprk_regardingmatter`; `sprk_duedate` → `sprk_responseduedate`.
2. Correct the `CreateWorkAssignmentRequest` XML doc comment at `:149`.
3. **Decide the ADR-024 question below**, then implement accordingly.
4. Add a test that exercises the endpoint **with** `MatterId` and `DueDate` populated — the current coverage evidently only exercises the null path, which is why this shipped.

---

## Open questions for review

1. **ADR-024 resolver fields.** `sprk_workassignment` carries the full polymorphic resolver set — `sprk_regardingrecordtype` plus `sprk_regardingrecordid` / `sprk_regardingrecordname` / `sprk_regardingrecordnumber` / `sprk_regardingrecordurl`. Writing `sprk_regardingmatter` alone leaves those denormalised fields empty or stale, which is what ADR-024 exists to prevent. The fix should either populate them or document explicitly why not.
   - Complication: the repo's resolver helpers (`PolymorphicResolverService`, `TodoRegardingUpdateBuilder`) are **client-side TypeScript**. A server-side equivalent may need to be written or located. That is a real design question, and it is the main reason this is not a two-line change.
2. **Who calls this endpoint?** Worth confirming before prioritising — if no live caller passes `MatterId`, this is latent; if one does, it is a live 500.
3. **Should `Description` be verified too?** `entity["sprk_description"]` at `:76` is correct (`sprk_description` does exist on the table) — noted here only so the review does not re-check it.

---

## Blast radius

Single BFF file. No schema change. Question 1 may pull in a small server-side resolver helper, which is the only part with design weight.

**Rough effort**: 0.25 day for the rename + test; **0.5–1 day** if the ADR-024 resolver fields are done properly server-side.

---

## Cross-references

- Discovery context: [`../discovery-checklist.md`](../discovery-checklist.md) §F · [`../../design.md`](../../design.md) §9.1
- Sibling issues from the same sweep: [`ISSUE-event-schema-drift.md`](ISSUE-event-schema-drift.md) · [`ISSUE-daily-briefing-schema-drift.md`](ISSUE-daily-briefing-schema-drift.md)
- [ADR-024 — Polymorphic Resolver Pattern](../../../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md)
- BFF changes are governed by root `CLAUDE.md` §10 — no new endpoint/service/DI/package here, but the test-update obligation applies.
