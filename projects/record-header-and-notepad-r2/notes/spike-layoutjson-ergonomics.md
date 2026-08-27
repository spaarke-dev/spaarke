# Spike — `layoutJson` ergonomics: `Multiple` vs `SingleLine.Text`

> **Task**: [001](../tasks/001-layoutjson-ergonomics-spike.poml) · **Completed**: 2026-08-27
> **Decision consumer**: task 033 (`RecordHeader` manifest `of-type`)
> **Verdict**: ✅ **`of-type="Multiple"`** — shipped since v1.1.1 and confirmed here by direct
> inspection of the LIVE form XML in `spaarkedev1`.

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
the spike's own method, because it tests the thing that actually ships. One check could not be
completed this way and is recorded as **open** below rather than being claimed.

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

⚠️ **OPEN — and the investigation found something more important than the answer.**

The Project main form is a component of exactly ONE solution:

```
objectid 5aa00242-5212-f111-8342-7ced8d1dc988  (System Form)
  → Cr2b7d5 · "Common Data Services Default Solution" · Unmanaged
```

**It is in no purpose-built solution.** The Default solution is the environment's catch-all; it is
not the vehicle by which customizations move between environments. So as things stand today, **the
Project header layout would not travel anywhere** — not because `Multiple` truncates, but because
nothing is carrying the form.

This bears directly on a **binding project assumption** recorded in the project CLAUDE.md:

> "Main forms ARE transported between environments inside a solution. This is now a binding
> assumption: it makes §5.1's JSON-on-manifest portability argument real (no per-environment paste)."

The assumption is correct **as a capability** — main forms can be added to a solution and
transported. It is **not currently realised**: the form must be added to a shippable solution
(e.g. `SpaarkeCore`, or a new `SpaarkeRecordHeaderForms`) before any promotion, or the layout will
have to be re-pasted per environment — exactly the outcome §5.1 chose this design to avoid.

**Why this check was not closed**: completing it requires either adding the form to a real solution
or importing a scratch solution — both **modify** `spaarkedev1`. That is a maker decision with
consequences beyond this spike, so it was not taken unilaterally. See "Action required" below.

---

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
| 1 | **Add the Project main form to a shippable solution** before any environment promotion — it is currently only in the Default solution. Repeat for every entity as Phase 5 binds it (Work Assignment, Invoice, Event, Agreement, Matter). | maker |
| 2 | Once #1 is done, close Check 3 by exporting that solution, unzipping, and byte-comparing `layoutJson` in the packed form XML against the 401 bytes recorded here. Cheap, and no longer needs a scratch control. | either |
| 3 | When editing a layout, **edit all three form factors** or verify they still agree — the designer stores one copy per form factor. | maker |

---

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
