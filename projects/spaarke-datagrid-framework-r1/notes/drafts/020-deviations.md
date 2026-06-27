# Task 020 — Deviations from POML

> **Status**: 2 deviations, both documented + accepted
> **Task outcome**: completed (both savedqueries created + verified)

---

## Deviation 1 — UQ-04 resolution method: AI-defaulted, not Ralph-interviewed

**POML steps 1 + 2 + 7 notes**: "Interview Ralph (escalation point)" / "🔔 HUMAN ESCALATION POINT — UQ-04 requires Ralph's decision on column layouts"

**Actual**: User explicitly delegated column choice to AI with directive **"use sensible defaults, refine later"**. No Ralph interview occurred. Column choices were made by AI based on:
1. Entity schema introspection via Dataverse MCP + `docs/data-model/sprk_kpiassessment.md`
2. Density compatibility with MDA OOB grid (per NFR-01 / FR-DG-10)
3. Drill-through usefulness (the Matter is already known from VisualHost; show *content* not Matter labeling)

**Refinement path**: User will iterate on the savedqueries in Power Apps Maker after they exist. The savedquery GUIDs (in `020-savedquery-ids.md`) persist across Maker edits.

**Acceptable per**: explicit user instruction in task 020 sub-agent prompt: "Do NOT escalate UQ-04 to a human — pick reasonable defaults based on entity introspection. The user will refine the savedqueries in Power Apps Maker after they exist."

---

## Deviation 2 — Matter filter NOT baked into stored FetchXML; framework overlays at runtime

**POML constraint** (line 41): "Both savedqueries MUST accept Matter ID as filter parameter via FetchXML `<filter><condition attribute='sprk_matter_lookup' operator='eq-userid' value='@MatterId'/></filter>` pattern (or appropriate dynamic-value syntax)."

**Actual**: Dataverse server-side validation of savedquery FetchXML at save-time rejects the `@MatterId` placeholder string:

```
Error : An exception System.FormatException was thrown while trying to convert
input value '[REDACTED]' to attribute 'sprk_kpiassessment.sprk_matter'.
Expected type of attribute value: System.Guid. Exception raised: Guid should
contain 32 digits with 4 dashes (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx).
(correlation ID: 5bd6a5f7-1b60-4227-bb55-a63bc347fedd)
```

Dataverse does NOT support runtime parameter placeholders in stored savedquery FetchXML. The POML constraint's `value='@MatterId'` pattern is application-layer convention; Dataverse rejects it at the SDK boundary because it parses every `condition[value]` as a typed literal against the target attribute's data type (here, `System.Guid`).

**Resolution**: The Matter filter is overlaid at runtime by the framework. The savedquery stores the BASE shape:
- Selected columns (incl. `sprk_matter` so it's available downstream)
- Sort order
- `statecode=0` filter
- `top=100`

The consuming framework code (BFF + Custom Page) reads the savedquery's fetchxml, parses it, and inserts the `sprk_matter eq <matterId>` condition before submitting to Dataverse. This is also a more flexible pattern — the same savedquery can be reused with different Matter IDs at runtime without server-side state.

**Side note** — the POML's `operator='eq-userid'` was likely a copy-paste artifact (`eq-userid` is for user-context filters); the correct operator for a Matter GUID filter is `eq`. The deviation notes the correct pattern.

**Impact on downstream tasks**:
- **Task 021** (configjson for KPI grid) — configjson should carry `matterFilterAttribute: "sprk_matter"` so the framework knows which attribute to filter on at runtime
- **Task 022** (configjson for Invoice grid) — same
- **Framework code** (Phase A tasks) — `BffDataverseClient` or equivalent must implement the fetchxml overlay logic
- **Task 023** (Custom Page hosting) — `parentContext` URL params must pass `matterId` through to the framework, which then injects into the overlay

**Acceptable per**: Dataverse server-side validation is non-negotiable; no human-side workaround exists. The application-layer overlay pattern is standard for Power Platform Custom Pages with parent-context filters.

---

## What was NOT deviated

- Column layout structure (5 KPI, 6 Invoice columns) — per `020-savedquery-column-layouts.md`
- `querytype = 0` (saved view) — per POML constraint
- `statecode = 0` (active records) — per POML constraint
- `isdefault = false` — these are special drill-through views, not entity defaults
- `returnedtypecode` per entity (10681 KPI, 10732 Invoice) — verified against existing OOB savedqueries
- Verification by retrieval — POML step 5; both records read back successfully
- Document outputs (`020-savedquery-column-layouts.md`, `020-savedquery-ids.md`) — per POML outputs section

---

## Files updated by this task

- `projects/spaarke-datagrid-framework-r1/notes/drafts/020-savedquery-column-layouts.md` (created)
- `projects/spaarke-datagrid-framework-r1/notes/drafts/020-savedquery-ids.md` (created)
- `projects/spaarke-datagrid-framework-r1/notes/drafts/020-deviations.md` (created — this file)
- `projects/spaarke-datagrid-framework-r1/tasks/020-author-matter-context-savedqueries.poml` (`status` → `completed`)

## Records created in Dataverse

- `savedquery` GUID `a3f6d045-9a5e-f111-ab0c-7c1e521545d7` — "KPI Assessment - Matter Context"
- `savedquery` GUID `b9f6d045-9a5e-f111-ab0c-7c1e521545d7` — "Invoice - Matter Context"
