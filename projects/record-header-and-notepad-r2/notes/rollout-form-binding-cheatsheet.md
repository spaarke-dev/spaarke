# Phase 5 rollout — form binding cheat sheet

> **Generated**: 2026-08-27 · **Verified against LIVE `spaarkedev1`** (read-only `pac org fetch`)
> **Covers**: tasks 050 · 051 · 060 · 061 · 070
> **Control**: Spaarke Record Header (`RecordHeaderPcf` **v1.1.11**, already imported)
> **Environment**: `spaarkedev1` only — never `spaarke-model1-prod`

Every field name below was validated by querying the real entity, not read off a spec. **All 39
fields across all five entities exist.** The risk is not missing columns — it is that many of them
are **not on the form yet**, which silently breaks editing.

---

## 🚨 Read this once — it applies to every entity

**A field the header edits MUST have a control on the form.** Inline edits stage through
`Xrm.Page.getAttribute(name).setValue(v)`, and `getAttribute` returns `null` for a field with no
control. Edit such a field and it throws `Field '<name>' not on form`.

So for each entity below:

1. **ADD** any field in the "must add" list to the form (any section), then **hide it** — uncheck
   *Visible by default* on the control, or park it in a collapsed section.
2. **MOVE** any field in the "already there" list into that same hidden section, so it does not
   render twice — once in the header and once in the body.
3. **Never DELETE** a field from the form to stop it rendering twice. Hide it.

**`sprk_recordsummary` is the one exception — leave it OFF the form.** It is read-only (the sparkle
reads it through `$select`, never through the form buffer), and it exists on all five entities, so
the sparkle will appear everywhere.

**Edit the layout in all three form factors.** The classic designer stores a separate copy of
`layoutJson` per form factor (Web / Tablet / Phone). Change one and the others silently diverge.

---

## Status at a glance

| Task | Entity | Form bound? | Fields to ADD first | Already on form |
|---|---|---|---|---|
| 050 | `sprk_project` | ✅ bound + UAT-passed | — | 6 of 6 |
| 051 | `sprk_workassignment` | ✅ bound — QA outstanding | **4** | 3 of 7 |
| 060 | `sprk_invoice` | ✅ bound — QA outstanding | **3** | 4 of 7 |
| 061 | `sprk_event` | ⏭️ **DEFERRED — do not bind** | **3** | 5 of 8 |
| 070 | `sprk_agreement` | ✅ bound — QA outstanding | **5** | 1 of 6 |

> **Updated 2026-08-28.** Bindings confirmed by the owner for 051 / 060 / 070; the per-entity renderer
> QA below has not been reported back yet. **061 is deferred** — see its section.

Agreement is the heaviest lift and also has **no records** — seed one before QA.

**To change a field set after binding** (add / remove / reorder / resize), see
[`RECORD-HEADER-PCF-AUTHORING-GUIDE.md` § "Changing which fields the header shows"](../../../docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md#changing-which-fields-the-header-shows).
No rebuild or redeploy — it is form config only. Adding a field is the one change with an extra step.

---

## 050 · Project — ✅ already done

Form: **Project main form** · `5aa00242-5212-f111-8342-7ced8d1dc988`
Bound to: `sprk_projectnumber` · Control present, layout live, **UAT-passed at v1.1.11**.

All six layout fields are on the form. Nothing to do — this is the reference implementation. The
live layout (401 bytes, verified byte-intact through a solution export) is:

```json
{"_version":"1.0","title":"Project Information","columns":3,"summaryField":"sprk_recordsummary","fields":[{"name":"sprk_projectnumber","span":1,"required":true},{"name":"sprk_projectname","span":2},{"name":"sprk_projecttype_ref","span":1},{"name":"sprk_openeddate","span":1, "renderer": "date"},{"name":"sprk_highpriority","span":1},{"name":"sprk_projectdescription","span":3,"renderer":"textarea"}]}
```

---

## 051 · Work Assignment

Form: **`7e578eef-761d-f111-88b3-7c1e520aa4df`** · Bind to: **`sprk_workassignmentnumber`**

**⚠️ ADD these to the form first, then hide them** — all four are editable, so without a control on
the form every edit throws:

| field | renderer it will get |
|---|---|
| `sprk_priority` | optionset (dropdown) |
| `sprk_assignedto` | lookup (inline type-ahead) |
| `sprk_responseduedate` | date |
| `sprk_highpriority` | boolean (toggle) |

**Already on the form — move into the hidden section**: `sprk_workassignmentnumber`, `sprk_name`,
`sprk_description`.

```json
{"_version":"1.0","title":"Work Assignment","columns":3,"summaryField":"sprk_recordsummary","fields":[{"name":"sprk_workassignmentnumber","span":1},{"name":"sprk_name","span":2,"required":true},{"name":"sprk_priority","span":1},{"name":"sprk_assignedto","span":1},{"name":"sprk_responseduedate","span":1},{"name":"sprk_highpriority","span":1},{"name":"sprk_description","span":3,"renderer":"textarea"}]}
```

---

## 060 · Invoice

Form: **`93aa1c69-0406-f111-8406-7c1e525abd8b`** · Bind to: **`sprk_invoicenumber`**

**⚠️ ADD then hide**:

| field | renderer it will get |
|---|---|
| `sprk_totalamount` | **currency** — the first real test of the Money renderer |
| `sprk_highpriority` | boolean |
| `sprk_description` | textarea |

**Already on the form — move**: `sprk_invoicenumber`, `sprk_name`, `sprk_invoicedate`,
`sprk_invoicestatus`.

```json
{"_version":"1.0","title":"Invoice","columns":3,"summaryField":"sprk_recordsummary","fields":[{"name":"sprk_invoicenumber","span":1},{"name":"sprk_name","span":2,"required":true},{"name":"sprk_totalamount","span":1},{"name":"sprk_invoicedate","span":1},{"name":"sprk_invoicestatus","span":1},{"name":"sprk_highpriority","span":1},{"name":"sprk_description","span":3,"renderer":"textarea"}]}
```

Check the currency renders with a symbol and correct precision — not a bare `12500`.

---

## 061 · Event — ⏭️ DEFERRED 2026-08-28, do not bind from this sheet

**`sprk_event` is not a like-for-like binding.** One form serves several record kinds (actions, tasks,
and others). `layoutJson` is a property of a **form**, not of a record type, so a single field set
cannot serve all of them — every kind opened on that form would get the same header.

There is no conditional/per-type layout tier today. Resolving this means an explicit choice:

- **a form per record kind**, each with its own `layoutJson` (works now, more forms to maintain), or
- **the union of fields**, accepting that irrelevant cells render as em-dashes for some kinds, or
- **build a per-type resolver tier** — the resolver is deliberately tier-shaped so this could slot in
  without touching renderers, but it does not exist and is not R2 scope.

Being addressed separately. The layout below is **retained for reference only** — it was validated
against live metadata and is correct as far as field existence goes, but it encodes the
one-size-fits-all assumption that caused the deferral.

Form: **`eaf22dcb-9aff-f011-8406-7c1e525abd8b`** · Bind to: **`sprk_eventname`**

**⚠️ ADD then hide**:

| field | renderer it will get |
|---|---|
| `sprk_eventnumber` | text |
| `sprk_eventstatus` | optionset |
| `sprk_highpriority` | boolean |

**Already on the form — move**: `sprk_eventname`, `sprk_eventtype_ref`, `sprk_plannedstart`,
`sprk_plannedend`, `sprk_description`.

```json
{"_version":"1.0","title":"Event","columns":3,"summaryField":"sprk_recordsummary","fields":[{"name":"sprk_eventnumber","span":1},{"name":"sprk_eventname","span":2,"required":true},{"name":"sprk_eventtype_ref","span":1},{"name":"sprk_eventstatus","span":1},{"name":"sprk_plannedstart","span":1},{"name":"sprk_plannedend","span":1},{"name":"sprk_highpriority","span":1},{"name":"sprk_description","span":3,"renderer":"textarea"}]}
```

This is the **datetime** test — `sprk_plannedstart` / `sprk_plannedend` must render date **and**
time, unlike Project's date-only `sprk_openeddate`. The mode is read from the form's own
`getFormat()`, which is another reason both fields must be on the form.

⚠️ `scheduledstart` / `scheduledend` / `sprk_location` **do not exist** on this entity, despite
live client code elsewhere referencing them. Do not add them to the layout.

---

## 070 · Agreement

Form: **`59d88274-a1a0-f111-aaac-000d3a99d1d7`** · Bind to: **`sprk_name`**

**Seed an Agreement record first — the entity has none, so there is nothing to QA against.**

**⚠️ ADD then hide** — five of six:

| field | renderer it will get |
|---|---|
| `sprk_agreementtype` | optionset |
| `sprk_effectivedate` | date |
| `statuscode` | optionset (Status) |
| `sprk_regardingmatter` | lookup |
| `sprk_agreementdescription` | textarea |

**Already on the form — move**: `sprk_name`.

```json
{"_version":"1.0","title":"Agreement","columns":3,"summaryField":"sprk_recordsummary","fields":[{"name":"sprk_name","span":2,"required":true},{"name":"sprk_agreementtype","span":1},{"name":"sprk_effectivedate","span":1},{"name":"statuscode","span":1},{"name":"sprk_regardingmatter","span":1},{"name":"sprk_agreementdescription","span":3,"renderer":"textarea"}]}
```

This entity also exercises the **toolbar slot auto-hide** (task 024) — check which launcher icons
appear and that they match Agreement's supported parents.

---

## After each binding — quick QA

1. Header renders the configured fields at the configured spans, no console errors.
2. **Open the console** and read `[RecordHeader] form/metadata diagnostic`. **`notOnForm` must not
   list any field you intend to edit.** That single line is the fastest confirmation you did the
   add/hide step correctly.
3. Edit one field of each renderer type and confirm the form goes dirty with no flash, then Save.
4. Click a lookup: an **inline dropdown** opens under the field (not a side pane), the right-side
   magnifier browses without typing, **Advanced** opens the OOB dialog, there is no "+ New", and
   opening the dropdown does not push the fields below it down.
5. Sparkle shows "No summary yet" — correct, the columns are unpopulated by design.
6. Check dark mode.

**If every cell shows an em-dash**, the `$select` failed — one bad column fails the whole retrieve.
The console diagnostic names what the control actually read.

---

## How this was verified

- **Field existence** — one FetchXML query per entity naming every layout field. Dataverse rejects an
  unknown attribute by name, so a clean query proves every field exists. This tests the same surface
  the control's `$select` uses.
- **On-form status** — each main form's `formxml` fetched by GUID and searched for
  `datafieldname="<field>"`.
- All queries read-only. No environment changes.
