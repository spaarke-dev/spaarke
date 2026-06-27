# Task 020 — Matter-Context SavedQuery Column Layouts

> **Status**: Authored 2026-06-01 (UQ-04 resolved via user delegation — "use sensible defaults, refine later")
> **Source**: Entity schema introspected via Dataverse MCP + `docs/data-model/sprk_kpiassessment.md`
> **Refinement**: User will refine in Power Apps Maker after savedqueries exist

---

## Decision context

User direction: **"use sensible defaults, refine later"**. UQ-04 resolution delegated to AI per user. Column choices favor:
- Fields actually present on the entity (verified via MCP schema introspection)
- Density compatible with MDA OOB grid (widths 100–300 px, total ~700–900 px)
- Drill-through usefulness — the user opens this view AFTER clicking a chart slice, so they already know the Matter; show the Matter-scoped *content* not redundant Matter labeling.

---

## Entity 1 — sprk_kpiassessment (KPI Assessment)

**Entity type code**: 10681
**Primary name attribute**: `sprk_kpiname`
**Primary ID attribute**: `sprk_kpiassessmentid`
**Matter lookup**: `sprk_matter` (Lookup → sprk_matter)

### Selected columns (5 columns)

| # | Column | Width | Rationale |
|---|--------|-------|-----------|
| 1 | `sprk_kpiname` | 250 | Primary name — what KPI is being assessed (jump column) |
| 2 | `sprk_performancearea` | 180 | Choice field (Guideline Compliance / Budget Compliance / Outcomes Achievement) — categorizes the KPI |
| 3 | `sprk_kpigradescore` | 100 | Choice field (A+ through F + No Grade) — the assessment outcome (this is the "score/rating" the user expects) |
| 4 | `sprk_assessmentnotes` | 300 | Multiline — narrative context for the grade |
| 5 | `modifiedon` | 125 | When the assessment was last updated |

**Sort**: `modifiedon` descending (most recent assessments first)
**Filter**: `sprk_matter eq @MatterId` AND `statecode eq 0` (active only)
**Top**: 100

### Why not other fields
- `sprk_assessmentcriteria` — Multiline 10000 chars; too verbose for grid, useful in drill-through detail
- `sprk_project` — Project context is implicit when filtering by Matter; saves width
- `createdon` — `modifiedon` is more useful for "what changed recently"
- `ownerid` — Not particularly useful in a Matter-scoped drill-through (the Matter team owns these)

---

## Entity 2 — sprk_invoice (Invoice)

**Entity type code**: 10732
**Primary name attribute**: `sprk_name`
**Primary ID attribute**: `sprk_invoiceid`
**Matter lookup**: `sprk_matter` (Lookup → sprk_matter)

### Selected columns (6 columns)

| # | Column | Width | Rationale |
|---|--------|-------|-----------|
| 1 | `sprk_invoicenumber` | 180 | Invoice number — the natural identifier for an invoice (jump column) |
| 2 | `sprk_name` | 250 | Display name — usually carries the invoice subject/description |
| 3 | `sprk_invoicestatus` | 120 | Choice field — workflow state (ToReview, etc.) — important for budget performance drill-through |
| 4 | `sprk_visibilitystate` | 120 | Choice field (Invoiced, etc.) — visibility/lifecycle state |
| 5 | `sprk_invoicedate` | 125 | Invoice date — important for budget performance time-series context |
| 6 | `modifiedon` | 125 | Last update — surfaces recent activity |

**Sort**: `sprk_invoicedate` descending, then `modifiedon` descending (most recent invoices first)
**Filter**: `sprk_matter eq @MatterId` AND `statecode eq 0` (active only)
**Top**: 100

### Why not other fields
- `sprk_vendororg` — Useful but adds another lookup width; defer to refinement
- `sprk_document` — File reference; not a grid-friendly column
- `sprk_workspaceflag` — Internal flag, not user-facing
- `sprk_containerid` — Internal SPE container, not user-facing
- `createdon` — `sprk_invoicedate` and `modifiedon` cover both "business date" and "system change" use cases
- "Total amount" — **No such field exists on `sprk_invoice`**. The task description's "total amount" assumption was incorrect; the entity carries no monetary attribute. The user can add one later and update the savedquery in Maker. (This is one of the things the user will refine.)
- "Due date" / "paid date" — same: not present on `sprk_invoice`. The entity is minimal at this point; refinement step will add what's needed.

---

## FetchXML / LayoutXML pattern

Both savedqueries follow the existing OOB structure (verified against `Active KPI Assessments` and `Active Invoices`). Differences from OOB defaults:

1. **Matter filter is NOT baked into the stored FetchXML** (architectural note — see `020-deviations.md` Deviation 2):
   - Dataverse server-side validates the savedquery FetchXML at save-time and rejects `@MatterId` placeholders (parses every `condition[value]` as a typed literal against the target attribute; rejects non-GUID strings on a Guid-typed attribute).
   - The savedquery stores the BASE shape (columns + sort + `statecode=0` + `sprk_matter` attribute selected).
   - The framework (BFF + Custom Page) OVERLAYS `sprk_matter eq <matterId>` at runtime, reading the savedquery's fetchxml, parsing it, and inserting the Matter condition before submitting to Dataverse.
   - This is more flexible (one savedquery, many Matters) and is the standard pattern for Power Platform Custom Pages with parent-context filters.
2. **Sort order** changed from OOB to drill-through-appropriate (recent activity first).
3. **`isdefault = false`** — these are NOT default views; they're special drill-through views.

### KPI Assessment FetchXML (as stored — Matter filter overlaid at runtime)

```xml
<fetch version="1.0" mapping="logical" top="100">
  <entity name="sprk_kpiassessment">
    <attribute name="sprk_kpiassessmentid" />
    <attribute name="sprk_kpiname" />
    <attribute name="sprk_performancearea" />
    <attribute name="sprk_kpigradescore" />
    <attribute name="sprk_assessmentnotes" />
    <attribute name="modifiedon" />
    <attribute name="sprk_matter" />
    <order attribute="modifiedon" descending="true" />
    <filter type="and">
      <condition attribute="statecode" operator="eq" value="0" />
    </filter>
  </entity>
</fetch>
```

At runtime, the framework injects:
```xml
<condition attribute="sprk_matter" operator="eq" value="<actual-matter-guid>" />
```
into the `<filter type="and">` block before submitting to Dataverse.

### KPI Assessment LayoutXML

```xml
<grid name="resultset" jump="sprk_kpiname" select="1" icon="1" preview="1" object="10681">
  <row name="result" id="sprk_kpiassessmentid">
    <cell name="sprk_kpiname" width="250" />
    <cell name="sprk_performancearea" width="180" />
    <cell name="sprk_kpigradescore" width="100" />
    <cell name="sprk_assessmentnotes" width="300" />
    <cell name="modifiedon" width="125" />
  </row>
</grid>
```

### Invoice FetchXML (as stored — Matter filter overlaid at runtime)

```xml
<fetch version="1.0" mapping="logical" top="100">
  <entity name="sprk_invoice">
    <attribute name="sprk_invoiceid" />
    <attribute name="sprk_invoicenumber" />
    <attribute name="sprk_name" />
    <attribute name="sprk_invoicestatus" />
    <attribute name="sprk_visibilitystate" />
    <attribute name="sprk_invoicedate" />
    <attribute name="modifiedon" />
    <attribute name="sprk_matter" />
    <order attribute="sprk_invoicedate" descending="true" />
    <order attribute="modifiedon" descending="true" />
    <filter type="and">
      <condition attribute="statecode" operator="eq" value="0" />
    </filter>
  </entity>
</fetch>
```

At runtime, the framework injects:
```xml
<condition attribute="sprk_matter" operator="eq" value="<actual-matter-guid>" />
```
into the `<filter type="and">` block before submitting to Dataverse.

### Invoice LayoutXML

```xml
<grid name="resultset" jump="sprk_invoicenumber" select="1" icon="1" preview="1" object="10732">
  <row name="result" id="sprk_invoiceid">
    <cell name="sprk_invoicenumber" width="180" />
    <cell name="sprk_name" width="250" />
    <cell name="sprk_invoicestatus" width="120" />
    <cell name="sprk_visibilitystate" width="120" />
    <cell name="sprk_invoicedate" width="125" />
    <cell name="modifiedon" width="125" />
  </row>
</grid>
```

---

## Refinement hooks for the user

When the user opens these in Power Apps Maker, likely changes:

**KPI Assessment**:
- Optionally swap `sprk_assessmentnotes` (narrative) for `sprk_assessmentcriteria` (criteria) if criteria is preferred at-a-glance
- Add Project (`sprk_project`) column if multi-project-per-matter scenarios are common
- Adjust widths to taste

**Invoice**:
- **Add a monetary "Amount/Total" column once that field is added to the entity** (currently no such field exists — see "Why not other fields" above)
- **Add a "Due Date" column once that field is added**
- Add `sprk_vendororg` (vendor) column once column widths can accommodate
