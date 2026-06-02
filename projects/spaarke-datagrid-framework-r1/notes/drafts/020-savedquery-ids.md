# Task 020 — SavedQuery IDs (for tasks 021, 022, 025)

> **Created**: 2026-06-01 by task 020
> **Verified**: both records retrieved from Dataverse post-create — fetchxml + layoutxml + returnedtypecode + querytype + statecode + isdefault all match the design

---

## Records created

| Entity (logical) | Display name | savedqueryid (GUID) | returnedtypecode |
|---|---|---|---|
| `sprk_kpiassessment` | KPI Assessment - Matter Context | `a3f6d045-9a5e-f111-ab0c-7c1e521545d7` | 10681 |
| `sprk_invoice` | Invoice - Matter Context | `b9f6d045-9a5e-f111-ab0c-7c1e521545d7` | 10732 |

---

## Usage in configjson (tasks 021, 022)

Tasks 021 and 022 should reference these IDs in their `source.savedQueryId` field of the configjson, e.g.:

```json
{
  "source": {
    "entityLogicalName": "sprk_kpiassessment",
    "savedQueryId": "a3f6d045-9a5e-f111-ab0c-7c1e521545d7",
    "matterFilterAttribute": "sprk_matter"
  },
  ...
}
```

```json
{
  "source": {
    "entityLogicalName": "sprk_invoice",
    "savedQueryId": "b9f6d045-9a5e-f111-ab0c-7c1e521545d7",
    "matterFilterAttribute": "sprk_matter"
  },
  ...
}
```

---

## IMPORTANT: Matter filter is NOT baked into the savedquery

See `020-deviations.md` for the full rationale, but in short:

- The savedquery's FetchXML defines the **base shape** (selected columns, sort order, `statecode=0` filter, top=100)
- The framework (BFF + Custom Page consuming code) MUST overlay the `sprk_matter eq <matterId>` filter at runtime by reading the savedquery's fetchxml, parsing it, and adding the Matter condition before submitting to Dataverse
- This is because Dataverse server-side rejects `@MatterId` placeholders during savedquery validation (`Expected type: System.Guid`)
- The `sprk_matter` attribute IS selected in the fetchxml (so it's available downstream), but it is NOT filtered in the stored fetchxml

**Tasks 021 + 022 implementation note**: The configjson should carry both `savedQueryId` AND `matterFilterAttribute: "sprk_matter"` so the framework knows which attribute to add the Matter condition on. (Or hardcode the attribute name in the framework if all Matter-context views use `sprk_matter` — which they do, per `docs/data-model/sprk_kpiassessment.md` line 33 and `docs/data-model/sprk_invoice.md` line 4.)

**Task 025 (Power Apps Maker handoff)**: User will refine column choices in Maker. The 2 GUIDs above persist; only fetchxml/layoutxml will change after Maker edits.

---

## Verification

Both savedqueries successfully retrieved via:

```sql
SELECT savedqueryid, name, returnedtypecode, querytype, statecode, isdefault, fetchxml, layoutxml
FROM savedquery
WHERE savedqueryid IN ('a3f6d045-9a5e-f111-ab0c-7c1e521545d7', 'b9f6d045-9a5e-f111-ab0c-7c1e521545d7')
```

Dataverse auto-injected `savedqueryid="..."` into the stored fetchxml (per OOB behavior); this is benign.
