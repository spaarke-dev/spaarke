# Task 071 — RegardingResolver PCF placement on `sprk_communicationthread` (deferred live config)

> **For**: the owner, next session with MCP/maker access. This is a **form-configuration change** — zero
> RegardingResolver code was touched (FR-22, entity-agnostic placement). Live apply is **DEFERRED** (same
> pattern as tasks 001–070: MCP was unavailable this session).
> **Depends on**: task 002's schema (`sprk_regardingrecordtype_ref` Lookup discriminator + the 11 typed
> `sprk_regarding{...}` lookups) must be **applied live** first (`scripts/Deploy-ThreadRegardingSchema.ps1`).
> Placing the PCF before that schema lands will fail nav-prop discovery (the discriminator lookup won't
> exist yet).

---

## 1. Prerequisite — verify task-002 schema is live

Before placing the control, confirm (via `describe_table('sprk_communicationthread')` or the maker portal
Table → Columns view):

- `sprk_regardingrecordtype_ref` exists, type **Lookup**, target `sprk_recordtype_ref`.
- The 11 typed lookups exist (`sprk_regardingmatter`, `sprk_regardingproject`, `sprk_regardinginvoice`,
  `sprk_regardingservicerequest`, `sprk_regardingworkassignment`, `sprk_regardingevent`,
  `sprk_regardingbudget`, `sprk_regardinganalysis`, `sprk_regardingorganization`, `sprk_regardingaccount`,
  `sprk_regardingperson`).
- The existing Text `sprk_regardingrecordtype` is **unchanged** (still String, not a Lookup).

If any is missing, run `scripts/Deploy-ThreadRegardingSchema.ps1` first (see
`notes/002-thread-regarding-schema.md`).

---

## 2. Place the control (Power Apps maker portal or classic form editor)

1. Open the solution containing `sprk_communicationthread` → **Tables** → `sprk_communicationthread` →
   **Forms** → the main form (the one the Timeline PCF / R1 messaging surface already uses).
2. Edit the form. Add a new section (or reuse an existing header section) near the top of the form —
   consistent with where RegardingResolver is placed on `sprk_communication`/`sprk_todo`/`sprk_event`
   (Row-1 "RELATED RECORD" pattern).
3. Insert component → search **"Regarding Resolver"** (`Spaarke.Controls.RegardingResolver`,
   namespace `Spaarke.Controls`, currently v1.4.6). If it's not in the component picker yet, the control
   needs to be added to the solution's component list first (it should already be present — it's already
   placed on `sprk_communication`).
4. Configure the control's properties exactly as below.

### Property configuration

| Property | Kind | Value | Notes |
|---|---|---|---|
| `entity` (Host Entity) | input, text | `sprk_communicationthread` | FR-22 lever — the only entity-specific setting. |
| `regardingRecordType` (Regarding Record Type) | **bound**, required | `sprk_regardingrecordtype_ref` | The task-002 Lookup discriminator. **This is the field binding that makes the R1 lesson satisfied** — a code component only appears in the form's Component Library if it declares a bound field, and this required bound property guarantees that. |
| `regardingRecordNameField` | bound, optional | `sprk_regardingrecordname` | **Bind this** — the field already exists (R1 denormalized quartet, populated by `ThreadResolver.CreateThreadAsync`/`FindOrCreateDefaultThreadAsync`). The resolver's standard write payload updates it on a regarding change. |
| `regardingRecordNumberField` | bound, optional | *(leave unbound)* | `sprk_regardingrecordnumber` does **not** exist on the thread (task 002 did not add it — out of scope). The PCF handles a missing/blank value gracefully (NFR-06 graceful-blank); do not add a new column just to populate this. |
| `regardingTargets` | input, optional | `sprk_matter,sprk_project,sprk_invoice,sprk_servicerequest,sprk_workassignment,sprk_event,sprk_budget,sprk_analysis,sprk_organization,account,contact` | **Set this explicitly** rather than relying on the manifest's default `TODO_REGARDING_CATALOG` (which was authored for `sprk_todo` and differs slightly — e.g. includes `sprk_document`, a target the thread does not carry per task 002). The explicit list mirrors `RegardingFieldMap.All` (ADR-024 order) exactly, so the resolver only ever offers the 11 targets the thread actually has typed lookups for. |
| `readOnly` | input, optional | *(leave default — auto-detected from form-disabled state)* | No special read-only requirement for the thread form. |
| `title` | input, optional | `RELATED RECORD` (default) or `REGARDING` | Either is fine; keep consistent with the sibling placement on `sprk_communication` if the maker wants visual parity. |
| `showVersionFooter` | input, optional | `true` (default) | Standard PCF footer requirement (root `src/client/pcf/CLAUDE.md`). |

5. Save → **Publish** the form (and `pac solution publish-all` if publishing via a managed pipeline).

---

## 3. What the resolver writes on a regarding change (for verification)

Per `ResolverWriteHandler.applyRegardingSelection` (client-side `Xrm.WebApi.updateRecord` — **no BFF call**,
see `ThreadResolver.ReDeriveThreadNameAsync` XML doc for the full trigger-wiring gap this implies):

- Clears the 10 OTHER typed regarding lookups (`@odata.bind = null`) that exist on the thread.
- Sets the CHOSEN typed lookup (e.g. `sprk_regardingmatter@odata.bind`) + `sprk_regardingrecordtype_ref@odata.bind`
  (the discriminator).
- Sets `sprk_regardingrecordid`, `sprk_regardingrecordname` (bound above), and `sprk_regardingrecordurl` if the
  target catalog entry supplies a URL builder.
- **Does NOT touch** the existing Text `sprk_regardingrecordtype` (it is not a lookup relationship, so the
  resolver's nav-prop discovery never matches it — confirmed by `notes/002-thread-regarding-schema.md` §2).
  This means after a regarding change via this PCF, `sprk_regardingrecordid`/`name` reflect the NEW record but
  the Text `sprk_regardingrecordtype` may reflect the OLD record type until the next message triggers
  `ThreadResolver`'s own logic (which does not currently overwrite it either — see the flagged limitation
  below). This is a pre-existing characteristic of the additive design (NFR-04), not something 071 fixes.

---

## 4. ⚠️ Known gap this task surfaces (not fixed here — root CLAUDE.md §6)

Two events are meant to reach `ThreadResolver.ReDeriveThreadNameAsync` (added by this task) and neither does
yet, because the write path is entirely client-side and bypasses the BFF:

1. **Regarding change via this PCF placement** → should trigger `ReDeriveThreadNameAsync(threadId)` so
   `sprk_name` re-derives (when the naming-edited marker is Auto).
2. **A user editing `sprk_name` directly on the form** → should flip `sprk_nameisautoderived` to Edited so a
   later regarding change preserves the user's chosen name.

Neither is wired by this task (BFF-only scope; no `dataverse-plugin` tag, no new endpoint/plugin file in this
task's outputs). **Recommended follow-on** (flagged for the owner / a future task): a Dataverse plugin
registered on `Update` of `sprk_communicationthread`, split into two steps:
- Filtered on the typed regarding lookups + `sprk_regardingrecordtype_ref` → calls into a thin future BFF
  endpoint that invokes `IThreadResolver.ReDeriveThreadNameAsync`.
- Filtered on `sprk_name` → compares the incoming value against what the same re-derive logic would produce
  and, if different, sets `sprk_nameisautoderived = false` (Edited).

Until that plugin (or an equivalent flow) exists, `ReDeriveThreadNameAsync` is a **tested, ready capability**
with no live trigger — exactly the class of gap root CLAUDE.md §6 says to surface explicitly rather than
silently guess a wiring mechanism into place.

---

## 5. UI-test checklist (run once schema + placement are both live)

Per the task POML `<ui-tests>`:

- [ ] RegardingResolver renders on the `sprk_communicationthread` form with **no console errors**.
- [ ] Setting/changing the thread's regarding writes the typed lookup + `sprk_regardingrecordtype_ref`
      discriminator; the Text `sprk_regardingrecordtype` remains populated (see §3 caveat on staleness).
- [ ] Naming behavior: **cannot be end-to-end verified yet** without the plugin from §4 — the BFF-side
      marker gate is unit-tested (`ThreadResolverTests.cs`), but nothing currently calls
      `ReDeriveThreadNameAsync` from a live regarding change. Once the follow-on plugin exists, re-verify:
      regarding change with marker=Auto → `sprk_name` re-derives; after a manual rename (marker→Edited), a
      subsequent regarding change preserves the user's name.
- [ ] Dark mode (ADR-021): control renders correctly in dark theme (Fluent v9 tokens) — already verified on
      the `sprk_communication`/`sprk_todo` placements of the same shared PCF; expected to hold unchanged here
      since zero PCF code changed.
