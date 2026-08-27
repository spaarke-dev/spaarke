# Spike — `layoutJson` ergonomics: `Multiple` vs `SingleLine.Text`

> **Task**: [001](../tasks/001-layoutjson-ergonomics-spike.poml) · **Completed**: 2026-08-27
> **Decision consumer**: task 033 (`RecordHeader` manifest `of-type`)
> **Verdict**: ✅ **`of-type="Multiple"`** — shipped since v1.1.1. All three checks PASS, confirmed
> against the LIVE form XML in `spaarkedev1` and a real `SpaarkeMaster` solution export.

---

## How this spike was actually run (deviation — read first)

**The procedure in the POML was NOT executed as written, and that was an escalated decision, not a
shortcut.** The POML is `mode="prescriptive"`, so the deviation was raised to the owner
(2026-08-27) and **option A was approved**.

**Why.** The spike was authored to decide one thing: `layoutJson`'s `of-type`. Between authoring and
execution, that decision was made, shipped, and exercised in production:

- `of-type="Multiple"` has shipped since **v1.1.1**;
- it was chosen empirically in **UAT round 1** (DEF-2) when a `SingleLine.Text` static value was
  found to be **capped at 100 characters** by the classic designer — below any real layout;
- the owner has since pasted, saved, published and re-opened a real layout in the classic designer
  across **six UAT rounds and twelve control imports**.

Running the written procedure — scaffold a throwaway PCF, import a scratch solution to `spaarkedev1`,
bind it to a dev form, request an operator designer session — would have re-derived a shipped
conclusion using a proxy control, at the cost of operator time and a scratch artifact in the dev
environment.

**What was done instead**: the same three checks were evaluated against the **real** control on the
**real** Project main form, using read-only `pac org fetch` queries. That is stronger evidence than
the spike's own method, because it tests the thing that actually ships. All three checks were closed this way; the third
required one read-only solution export.

---

## Evidence

Live form XML pulled from `spaarkedev1` (read-only):

```
pac org fetch --xml "<fetch><entity name='systemform'>…formid eq 5aa00242-5212-f111-8342-7ced8d1dc988…"
```

### Check 1 — does `Multiple` give a usable multi-line editor in the CLASSIC designer?

✅ **PASS.** Proven by use, not by demo. The owner has repeatedly pasted a multi-line JSON layout
into this property in the classic designer and it round-trips through save → publish → re-open.

The stored value confirms it was authored as multi-line — it retains a **trailing newline**
(401 bytes stored, 400 stripped), which a single-line editor could not have produced.

`SingleLine.Text` **FAILS** this payload outright: the classic designer caps a static
`SingleLine.Text` value at **100 characters**, and the live layout is **401 bytes** — 4× over.
That failure is what selected `Multiple` in the first place (DEF-2, UAT round 1).

### Check 2 — does a ~1 KB payload survive designer save + publish byte-intact?

✅ **PASS**, verified directly rather than by screenshot:

| property | value |
|---|---|
| declared type in form XML | `<layoutJson type="Multiple" static="true">` |
| stored length | **401 bytes** (400 stripped of the trailing newline) |
| valid JSON | ✅ `json.loads` succeeds |
| truncation | **none** — all six fields present through the closing `}]}` |
| form-factor copies | **3** (Web / Tablet / Phone), **byte-identical to each other** |

The three-copy detail matters and is easy to miss: the classic designer writes a **separate**
`<customControl>` block per form factor, each carrying its own full copy of `layoutJson`. A maker who
edits the layout in only one form factor will silently ship divergent layouts. All three currently
agree.

### Check 3 — does it survive a solution export/import round-trip?

✅ **PASS.** Verified by exporting the real solution and byte-comparing.

`SpaarkeMaster` (unmanaged) contains the `sprk_project` **entity** with **"Include Subcomponents"**,
so the Project main form — and its `layoutJson` — travels with it. Exported read-only:

```
pac solution export --name SpaarkeMaster --managed false     # 24.5 MB zip
```

| | |
|---|---|
| `layoutJson` blocks in the exported `customizations.xml` | **3** (one per form factor, as on the live form) |
| exported length | **402 bytes** vs **401** live |
| the ONE differing byte | offset 400 — the **trailing newline**, normalised LF to CRLF by the export serializer |
| first 400 content bytes | **byte-identical** |
| semantic comparison | `json.loads(exported) == json.loads(live)` → **True** |

No truncation, no escaping damage, no re-ordering. The CRLF normalisation is cosmetic — it is
outside the JSON payload and `JSON.parse` ignores trailing whitespace.

`sprk_project` is carried with subcomponents by six unmanaged solutions: `SpaarkeMaster`,
`SpaarkeCore`, `SPRKDOCINTELLIGENCE`, `SPRKMAINDEV1250801`, `SpaarkeMasterTest2` and `Default`.

> #### ⚠️ Correction — an earlier revision of this document got this wrong
>
> The first pass concluded the form was "in no purpose-built solution, only the Default catch-all"
> and raised a false action item to add it to one. **That was wrong**, and the owner corrected it.
>
> The error was a query that answered a narrower question than the conclusion drawn from it. Asking
> `solutioncomponent` for the **form's own** `objectid` returns only solutions where the form was
> added as an **explicit** component — which is just `Cr2b7d5`. But when an **entity** is added with
> `rootcomponentbehavior = Include Subcomponents`, its forms transport **without** getting their own
> `solutioncomponent` row. The form was in `SpaarkeMaster` the whole time, one level up.
>
> **The lesson generalises**: to ask "does this form/view/chart transport?", query the **entity's**
> component row and its `rootcomponentbehavior` — not the asset's. An asset-level query will read as
> a false negative for every entity added with subcomponents, which is the normal case.

## Decision

**`layoutJson` ships as `of-type="Multiple"`.** Already implemented; no change required.

Rationale, tied to the evidence above: `SingleLine.Text` cannot hold the payload at all (100-char
designer cap vs a 401-byte layout), so the fallback the spec named as "proven" is proven only for
short values like `title` — it is disqualified for this property. `Multiple` accepts the payload,
stores it byte-intact in form XML across all three form factors, and parses as valid JSON on read.

The spike's own note holds: this outcome "decides only the manifest of-type" and changes nothing else
in the design.

---

## Action required (not part of this task)

| # | item | owner |
|---|---|---|
| 1 | ~~Add the Project main form to a shippable solution~~ — **WITHDRAWN**: it already transports inside `SpaarkeMaster` via the entity's Include-Subcomponents behaviour. No action. | — |
| 2 | When editing a layout, **edit all three form factors** or verify they still agree — the classic designer stores one copy of `layoutJson` per form factor (Web / Tablet / Phone). All three currently match. This is the one real maker trap this spike found. | maker |

## Observations (colour, not verdict)

- The live layout sets `"renderer": "date"` explicitly on `sprk_openeddate`. Since v1.1.6 the date
  format is read from the live form (`attribute.getFormat()`), so the explicit renderer is now
  redundant — but it is a legitimate override, harmless, and was presumably added during UAT round 3
  when DateOnly was still rendering a time picker. Left as-is.
- The layout recorded in `current-task.md` (380 bytes) predates that addition. The live value is
  **401 bytes**; this document is the accurate one.
- No apostrophes appear anywhere in the stored value, so the `noAposStringType` import trap the POML
  warned about is not in play here.

---

## Cleanup

Nothing to clean up: no scratch control was built, no scratch solution was imported, and no form
binding was created. All queries were read-only `pac org fetch` calls against `spaarkedev1`
(never `spaarke-model1-prod`). `git status` shows changes only under
`projects/record-header-and-notepad-r2/`.
