# Matter Parity Baseline — `MatterHeaderPcf` v1.0.20

> **Seeded**: 2026-08-25 from the owner-supplied screenshot [`matter-record-header.jpg`](matter-record-header.jpg)
> **Consumed by**: task **002** (baseline capture) and task **080** (parity regression gate)
> **Status**: PARTIAL — light mode captured; **dark mode still outstanding**

---

## Why this file exists early

Task 002 was flagged as blocked: the shipped header returns HTTP 400 (RS-1), so there would be nothing left to screenshot. The owner's screenshot **predates the `sprk_mattersummary` deletion**, so it captures the header working. That substantially de-risks the ordering trap — the visual baseline no longer depends on shipping task 040 first.

**RS-1 re-confirmed live 2026-08-25** (so nobody re-litigates it):

```
GET /sprk_matters?$select=sprk_matternumber,sprk_mattername,_sprk_mattertype_value,
                          _sprk_practicearea_value,sprk_matterdescription,sprk_mattersummary
  -> HTTP 400  "Could not find a property named 'sprk_mattersummary'"

Same list with sprk_mattersummary -> sprk_recordsummary
  -> HTTP 200
```

The screenshot therefore shows a state that **can no longer be reproduced** against current schema until task 040 lands. Treat it as the authoritative visual record.

---

## Captured record

`REAL-2026-123456.02` — "Real Estate Transaction Matter" · Status Reason **Draft** · Matter main form · OVERVIEW tab.

## Layout — 3 columns, 5 fields

| Row | Cells | Spans |
|---|---|---|
| 1 | **Matter Number** `*` · **Matter Name** | 1 · 2 |
| 2 | **Matter Type** · **Practice Area** | 1 · 1 *(row not filled — third track empty)* |
| 3 | **Matter Description** | 3 |

Equivalent `layoutJson` for task 080's parity binding:

```json
{ "_version": "1.0", "title": "Matter", "columns": 3,
  "fields": [
    { "name": "sprk_matternumber",      "span": 1, "required": true },
    { "name": "sprk_mattername",        "span": 2 },
    { "name": "sprk_mattertype",        "span": 1 },
    { "name": "sprk_practicearea",      "span": 1 },
    { "name": "sprk_matterdescription", "span": 3, "maxLines": 10 } ] }
```

`summaryField` omitted — defaults to `sprk_recordsummary` via `RECORDSUMMARY_FIELD` (task 034).

## Visual details to match

| Element | Observed |
|---|---|
| Section header | "MATTER INFORMATION" — this is the **form section** header, rendered by the form, *above* the PCF. Not the control's own title. |
| Toolbar | Top-right, three icons: sparkle · checkmark **badge 5** · annotation **badge 6→2** (shown: 2). No title text rendered inline. |
| Required marker | `*` on **Matter Number only** — consistent with D-10 (marker stays TextField-only) |
| Read-mode cells | Light grey fill (`colorNeutralBackground3`), rounded, ~2em min-height — the v1.0.3 OOB-input-parity treatment |
| Labels | Above each cell, regular weight, neutral foreground — the v1.0.4 typography |
| Empty value | Matter Description renders **`—`** (em-dash) — confirms current behaviour for a null Memo |
| Lookups | **Pill style** with an inline `✕` clear affordance: "Patent ✕", "Intellectual Property Patents ✕" |
| Version footer | `v1.0.20`, bottom-**right**, inside the description cell's lower area |
| Description height | Tall — roughly 10 lines of empty space, consistent with `maxLines: 10` |

## ⚠️ Deliberate differences task 080 must NOT flag as regressions

1. **Lookup interaction changes.** The pill + inline-`✕` shown here is the *custom* control. FR-15a replaces the editable path with the OOB `Xrm.Utility.lookupObjects` picker. **Intended change** — the parity criterion explicitly excludes the lookup interaction.
2. **Sparkle source changes.** v1.0.20 read `sprk_mattersummary`; R2 reads `sprk_recordsummary` (0 populated of 55), so the popover will show the **"No summary yet"** empty state rather than content. Classified under FR-22, not a regression.
3. **Version footer** will read the new control's version (1.1.0 assumed), not `v1.0.20`. That is the *intended* in-UI check that the swap took.

## Still outstanding for task 002

- [ ] **Dark-mode capture** — the parity criterion requires light **and** dark; only light exists
- [ ] High-contrast capture (R1 live-QA behaviour)
- [ ] Confirm which field `boundField` binds on the live form (expected `sprk_matternumber`)
- [ ] Runtime behaviours that a screenshot cannot show: form-buffer dirty state with **no re-render flash**, 25%×35% Notepad modal, `openTodos` SmartTodo filter

> The four runtime behaviours are blocked until task 040 restores rendering. Everything visual/static above is now settled.
