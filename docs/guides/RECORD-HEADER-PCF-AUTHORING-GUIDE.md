# Record Header — Configuration Guide

> **Status**: Published · rewritten 2026-08-27 from shipped code (`record-header-and-notepad-r2`)
> **Control**: `Spaarke.Records.RecordHeader` — solution `RecordHeaderPcf`, currently **v1.1.11**
> **Source**: [`src/client/pcf/RecordHeader/`](../../src/client/pcf/RecordHeader/) · shared primitives in [`@spaarke/ui-components`](../../src/client/shared/Spaarke.UI.Components/)

---

## ⚠️ If you came here to build a per-entity header PCF — stop

**There is ONE Record Header control, and it works on every entity.** You do not write code to put a
header on a new table; you paste JSON into a form property.

The previous version of this guide taught the opposite: a `ProjectHeaderPcf` / `InvoiceHeaderPcf` /
`EventHeaderPcf` recipe, one thin PCF cloned per entity. **That approach was withdrawn on
2026-08-21** — it would have duplicated ~180 lines of identical machinery four-plus times, and each
clone would need its own build, solution, version and deployment. The re-scope is recorded in
[`projects/record-header-and-notepad-r2/design.md`](../../projects/record-header-and-notepad-r2/design.md).

If you find other docs describing per-entity header PCFs, they are stale — see the staleness table in
[the project CLAUDE.md](../../projects/record-header-and-notepad-r2/CLAUDE.md).

---

## Purpose

How to put the Spaarke Record Header on a Dataverse table's main form, and how to configure what it
shows. **This is maker work — no code, no build, no new solution.**

Adding the header to a new entity is: bind the control to a field on the form, paste a `layoutJson`
layout, move the raw fields into a hidden section, publish.

---

## What the header gives you

A card at the top of the form with:

- a **title** and a **toolbar** — identical on every entity: AI-summary sparkle, To Do launcher, Notepad launcher
- a **field grid** (2 or 3 columns) whose contents you configure
- **inline editing** on every field, staged into the form buffer so the form's own Save commits it

Field renderers are chosen automatically from each attribute's Dataverse type, and can be overridden:

| renderer | used for | notes |
|---|---|---|
| `text` | String | the only renderer that shows a required `*` marker |
| `textarea` | Memo | `maxLines` controls height before it scrolls |
| `lookup` | Lookup | inline type-ahead + **Advanced** dialog — see [`inline-lookup-field.md`](../../.claude/patterns/ui/inline-lookup-field.md) |
| `optionset` | Picklist / Status / State | dropdown fed from metadata |
| `date` / `datetime` | DateTime | mode read from the form's own `getFormat()` |
| `number` / `currency` | Integer / Decimal / Double / Money | currency symbol per record |
| `boolean` | TwoOptions | always-visible Switch when editable |

---

## Prerequisites

- The `RecordHeaderPcf` solution imported into the environment ([`/pcf-deploy`](../../.claude/skills/pcf-deploy/SKILL.md))
- Form-designer access, including the **classic** designer (the modern designer cannot edit a static `Multiple` property comfortably)
- The attributes you want to show already exist on the table

---

## Bind the header to a form

### 1. Add the control

Open the table's **main form** → **Edit** → **Edit in classic**. Click the field the header will
replace — conventionally the **primary name attribute** — then **Controls → Add Control → Spaarke
Record Header**, and tick **Web** (plus Phone/Tablet if you want it there).

### 2. Paste the layout

Set **Layout JSON** to a *static value*:

```json
{
  "_version": "1.0",
  "title": "Project Information",
  "columns": 3,
  "summaryField": "sprk_recordsummary",
  "fields": [
    { "name": "sprk_projectnumber", "span": 1, "required": true },
    { "name": "sprk_projectname", "span": 2 },
    { "name": "sprk_projecttype_ref", "span": 1 },
    { "name": "sprk_openeddate", "span": 1 },
    { "name": "sprk_highpriority", "span": 1 },
    { "name": "sprk_projectdescription", "span": 3, "renderer": "textarea" }
  ]
}
```

**Leave it blank** and the header derives a sensible layout from form metadata instead — primary name
first, then up to four more non-system fields in form order. Good for a quick trial.

### 3. 🚨 MOVE the raw fields — do NOT delete them

The fields your layout names **must stay on the form**. Put them in a collapsed section, or set their
controls to not-visible — but do not remove them.

**Why**: inline edits stage through the form buffer via `Xrm.Page.getAttribute(name).setValue(v)`,
and `getAttribute` returns `null` for a field with no control on the form. Delete the field and every
edit to it throws `Field '<name>' not on form`. This cost a full UAT round to diagnose; the shipped
R1 Matter form keeps every edited field present for exactly this reason.

### 4. Save → Publish

Then open a record and confirm the header renders.

---

## 🚨 Edit the layout in ALL THREE form factors

The classic designer stores a **separate copy of `layoutJson` per form factor** — Web, Tablet and
Phone each get their own `<customControl>` block with its own full copy.

Edit one and the others silently diverge, which presents as "the layout is wrong, but only on
tablet". Verified 2026-08-27 against the live `sprk_project` form: three copies, currently
byte-identical. Change one, change all three.

---

## Changing which fields the header shows

**This is the day-2 operation, and it is the answer to "how do I adjust the fields?"** — you edit the
`layoutJson` on the form. There is no other mechanism. The layout is the single source of truth for
what the header displays.

### ✅ No rebuild, no redeploy, no solution import

Changing the field set is **form configuration only**. You do not rebuild the PCF, bump a version,
re-pack a solution, or re-import anything. Edit the JSON in the form designer → Save → Publish → hard
refresh. The control reads its layout at render time.

The only reason to touch the PCF itself is to change *behaviour* (a new renderer type, a toolbar
change) — never to change *which fields appear*.

### The four changes you will actually make

| Goal | Edit | Extra step? |
|---|---|---|
| **Remove** a field | Delete its object from `fields[]` | None. Leave the field on the form — hidden and unused is harmless |
| **Reorder** fields | Move the objects; `fields[]` renders in array order | None |
| **Resize / relabel** | Change `span`, `label`, `renderer`, `maxLines`, `readOnly`, `required` | None |
| **Add** a field | Add an object to `fields[]` | 🚨 **Yes — see below** |

### 🚨 Adding a field: it must exist on the form first

This is the one change that breaks if you do only half of it.

1. **Add the field to the form** (any section) — then **hide it** (untick *Visible by default*, or park
   it in a collapsed section).
2. **Then** add it to `fields[]`.

**Why**: inline edits stage through the form buffer via `Xrm.Page.getAttribute(name).setValue(v)`, and
`getAttribute` returns `null` for a field with no control on the form. A field named in `layoutJson`
but absent from the form **renders fine and reads its value fine** — it only throws
`Field '<name>' not on form` the moment someone edits it. That asymmetry is what makes it worth
calling out: it passes a casual look and fails in a user's hands.

Mark the field `"readOnly": true` and you can skip the add — read-only cells never touch the form
buffer. Everything editable needs the control present.

### Getting the logical names right

Guessing logical names is the most common way to produce a broken layout, and the failure is loud:
**a `$select` is all-or-nothing**, so one wrong column blanks *every* cell in the header (G-12).

Verify against live metadata before pasting:

```bash
# read-only; lists the real logical names for an entity
pac org fetch --xml "<fetch top='1'><entity name='sprk_project'><all-attributes/></entity></fetch>"
```

Known traps on these tables: `sprk_project`'s primary name is **`sprk_projectnumber`**, not
`sprk_projectname` (both exist). Event's datetime pair is `sprk_plannedstart` / `sprk_plannedend` —
`scheduledstart`, `scheduledend`, and `sprk_location` **do not exist** despite live client code
elsewhere referencing them.

### Asking Claude Code to write the layout

This is the expected workflow — the JSON is fiddly and the traps above are exactly what an agent
should be checking. Give it:

- **the entity logical name** (`sprk_invoice`)
- **the fields you want, in display order**, and roughly how wide each should be
- **the form** you are binding (name or GUID) if it is not the default main form

It should come back with the JSON **and** a list of which named fields are not yet on that form and
therefore need the add-then-hide step. If it hands you JSON without that list, ask for it — that list
is the part that prevents the failure above.

A worked per-entity example for the current rollout lives in
[`notes/rollout-form-binding-cheatsheet.md`](../../projects/record-header-and-notepad-r2/notes/rollout-form-binding-cheatsheet.md).

### Verify the change landed

Open a record, open the browser console, and read **`[RecordHeader] form/metadata diagnostic`**.
**`notOnForm` must not list any field you intend to edit.** That single line confirms the add-then-hide
step in one look, and is faster than clicking every cell.

### ⚠️ One layout per form factor, not per record type

`layoutJson` is a property of a *form*, so every record opened on that form gets the same field set.
There is no built-in "show these fields for type A, those for type B" conditional.

This matters for tables used for several distinct record kinds on one form — `sprk_event` is the live
example, covering actions, tasks and others. The options are to give each type its own form (each with
its own layout), or to configure the union of fields and accept blanks. **A conditional/per-type layout
tier is not implemented**; the resolver is deliberately tier-shaped so one could be added without
touching renderers, but that is future work, not a current capability.

---

## Configuration reference

Full schema: [`RecordHeaderConfiguration.ts`](../../src/client/shared/Spaarke.UI.Components/src/types/RecordHeaderConfiguration.ts).

| key | type | notes |
|---|---|---|
| `_version` | `"1.0"` | required — the validity discriminator |
| `title` | string | header caption; the manifest `title` property outranks it |
| `columns` | `2 \| 3` | grid width. Default 3 |
| `summaryField` | string | column behind the sparkle. Defaults to `sprk_recordsummary` |
| `fields[]` | array | required, in render order |
| `fields[].name` | string | attribute logical name |
| `fields[].span` | `1..3` | clamped to `columns` |
| `fields[].label` | string | overrides the form label |
| `fields[].renderer` | see table above | overrides the type-derived renderer |
| `fields[].readOnly` | boolean | renders without an edit affordance |
| `fields[].required` | boolean | `*` marker — **text renderer only** |
| `fields[].maxLines` | number | `textarea` height before scrolling |

**Bad config never blanks the form.** Malformed JSON, a wrong `_version`, or an absent property each
produce a `console.warn` and fall back to derived defaults. That is a hard requirement (NFR-10), not
best-effort.

### Manifest properties

| property | type | purpose |
|---|---|---|
| `boundField` | bound | the field the control replaces |
| `title` | SingleLine.Text | title override; outranks `layoutJson.title` |
| `showVersion` | TwoOptions | version footer — useful during QA |
| `layoutJson` | **Multiple** | the layout |

`layoutJson` is `of-type="Multiple"` deliberately: the classic designer caps a static
`SingleLine.Text` at **100 characters**, and a real layout is ~400 bytes. Verified end-to-end
including solution transport — see [`spike-layoutjson-ergonomics.md`](../../projects/record-header-and-notepad-r2/notes/spike-layoutjson-ergonomics.md).

---

## The sparkle (AI summary)

Shown when `summaryField` names an attribute that **exists** — not when it is populated. An
existing-but-empty column shows the sparkle with "No summary yet"; a separate project populates
these columns. When the attribute is absent the affordance is omitted entirely rather than rendered
dead.

The refresh icon is deliberately **absent** — it needs a BFF endpoint that is out of scope (DEF-01).

---

## Notepad launch contract

Unchanged, and it is **external API** — breaking changes need a migration plan (NFR-09).

```typescript
Xrm.Navigation.navigateTo(
  { pageType: 'webresource', webresourceName: 'sprk_notepad_page',
    data: `regardingEntity=${entity}&regardingId=${recordId}` },
  { target: 2, position: 1, width: { value: 70, unit: '%' }, height: { value: 80, unit: '%' } }
);
```

Supported parents: `sprk_matter`, `sprk_project`, `sprk_event`, `sprk_invoice`, `sprk_budget`,
`sprk_workassignment`. An unsupported entity gets a warning MessageBar and no CRUD. To extend, add
the lookup + resolver fields to `sprk_memo` per [ADR-024](../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md)
and extend `SUPPORTED_MEMO_PARENTS` in `toolbarLaunchDefaults` — extend, don't fork (CLAUDE.md §11).

SmartTodo uses `action=openTodos&regardingType=…&regardingId=…`, also external API.

---

## For developers

Most changes to this control are **maker** changes. If you are editing code:

### Bundle optimization — still MANDATORY

The triad below is unchanged from R1 and still load-bearing. Without all three the bundle blows past
the 250 KB ceiling; with it, v1.1.11 measures **116 KB** (47%).

1. **`featureconfig.json`** at PCF root — `pcfReactPlatformLibraries: "on"`, `pcfAllowCustomWebpack: "on"`. Without it the `<platform-library>` declarations are ignored and React + Fluent get bundled.
2. **`webpack.config.js`** at PCF root — marks `@fluentui/react-icons` side-effect-free (~6.8 MB of icon chunks otherwise).
3. **Deep-path imports** — `@spaarke/ui-components/dist/components/RecordHeader`, never the top-level barrel, which drags `EntityCreationService` → `mammoth` (~1.6 MB vs ~40 KB).

⛔ **Two "clever" bundle fixes have already failed. Do not re-derive them.** Lazy-loading a renderer
measured *larger* (`pcf-scripts` emits one chunk, so `import()` inlines back). Externalising granular
`@fluentui/*` packages built clean, passed static verification, then **crashed at runtime with
minified React error #31** — it splits Fluent's slot machinery across two live copies. A successful
build proves nothing about a PCF's runtime.

### Rules that bit us

- **`npm run build:prod`**, never `npm run build` — the default is a dev build 10–15× larger ([AP-1](../../.claude/FAILURE-MODES.md)).
- **Rebuild the shared library first.** PCFs bundle `dist/`, not source; a stale `dist/` silently ships old code. The `ensure-dist-fresh` prebuild guard handles this for wired PCFs.
- **Version bumps hit 5 locations**, not 4 — `pack.ps1` is the fifth and it names the emitted zip.
- **Call `Xrm` methods directly on their namespace object.** `const f = xrm.Utility.lookupObjects` detaches the receiver and throws on `this._clientApiExecutor`. This has bitten twice, on two different namespaces — [G-14](../../.claude/FAILURE-MODES.md).
- **`Xrm.Utility.getEntityMetadata` returns the CLIENT API shape** — numeric `AttributeType`, string `DisplayName`, and **no `Format` or `Targets` at all**. Those two are filled from the live form via `getFormat()` / `getEntityTypes()`. Read the shipped `@types/xrm` before assuming a field exists — [G-13](../../.claude/FAILURE-MODES.md).
- **A `$select` is all-or-nothing.** One unrecognised column fails the whole retrieve and blanks every cell — [G-12](../../.claude/FAILURE-MODES.md). This is why the sparkle column is existence-gated before it joins the select.

### Two components named `LookupField`

| path | what | used for |
|---|---|---|
| `components/LookupField/LookupField.tsx` | inline type-ahead | **editable** lookups |
| `components/RecordHeader/fields/LookupField.tsx` (aliased `RecordHeaderLookupField`) | display + navigate | read-only / target-less lookups |

Importing the wrong one is the easiest mistake in this area. See [`inline-lookup-field.md`](../../.claude/patterns/ui/inline-lookup-field.md).

### Deploying

Per [`/pcf-deploy`](../../.claude/skills/pcf-deploy/SKILL.md): bump 5 locations → `npm run build:prod`
→ copy `out/controls/control/{bundle.js,ControlManifest.xml}` into
`Solution/Controls/sprk_Spaarke.Records.RecordHeader/` → `pack.ps1` → `pac solution import
--publish-changes` → hard-refresh (Ctrl+Shift+R) and check the version footer.

---

## Troubleshooting

The control logs two diagnostics to the console on every load. Read them before theorising — they
were added precisely because three UAT rounds were spent guessing at platform behaviour.

- **`[RecordHeader] form/metadata diagnostic`** — what it READ: Xrm.Page availability, control count, resolved formats/targets, and **`notOnForm`**.
- **`[RecordHeader] field decisions`** — what it DECIDED per field: renderer, attributeType, format, targets, readOnly, editable, and `picker: 'inline' | 'display'`.

| symptom | cause | fix |
|---|---|---|
| Every cell is an em-dash | The `$select` 400'd — usually one bad column | Check the console; a lookup rendered as `text` gets selected by its bare name |
| Edits throw `Field '<n>' not on form` | The field was deleted rather than hidden | Put it back on the form — see step 3 |
| A date shows a time picker | `format` didn't resolve | Check `formatsResolved` in the diagnostic; the field must be on the form |
| A lookup renders but won't open | No `targets` | `picker: 'display'` in the diagnostic means a required half is missing — read `readOnly` and `targets` on the same line |
| Layout right on desktop, wrong on tablet | Only one form factor was edited | Update all three |
| Layout truncated at 100 chars | The property is `SingleLine.Text` | It must be `Multiple` |
| Changes don't appear after import | Cached bundle | Hard-refresh; confirm the version footer |

---

## References

- **Design + spec**: [`projects/record-header-and-notepad-r2/`](../../projects/record-header-and-notepad-r2/) — `design.md` §5 (config), §6.5a (lookups)
- **Patterns**: [`record-header-composition.md`](../../.claude/patterns/ui/record-header-composition.md) · [`inline-lookup-field.md`](../../.claude/patterns/ui/inline-lookup-field.md) · [`pcf-build-scaffold.md`](../../.claude/patterns/pcf/pcf-build-scaffold.md)
- **ADRs**: [006](../adr/ADR-006-prefer-pcf-over-webresources.md), [012](../adr/ADR-012-shared-component-library.md), [021](../adr/ADR-021-fluent-ui-design-system.md), [022](../adr/ADR-022-pcf-platform-libraries.md), [024](../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md), [028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md), [038](../adr/ADR-038-testing-strategy.md)
- **Skills**: `/pcf-deploy`, `/code-page-deploy`, `/fluent-v9-component`
- **Companions**: [`SHARED-UI-COMPONENTS-GUIDE.md`](SHARED-UI-COMPONENTS-GUIDE.md) · [`PCF-DEPLOYMENT-GUIDE.md`](PCF-DEPLOYMENT-GUIDE.md)
