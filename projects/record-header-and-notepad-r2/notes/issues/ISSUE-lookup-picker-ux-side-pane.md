# ISSUE — the lookup opens the side-pane picker, not the OOB inline dropdown

> **Raised**: 2026-08-27 by owner, UAT round 5 of RecordHeader v1.1.8
> **Status**: ✅ **CLOSED — UAT PASSED (owner, 2026-08-27, v1.1.11)**: "the PCF is working - looks good".
> Shipped across v1.1.9 (swap) → v1.1.10 (OOB input styling) → v1.1.11 (overlay + focus underline).
> **Severity**: low-medium — a visible departure from OOB on every lookup cell, on every entity.

---

## ✅ Resolution (v1.1.9, 2026-08-27)

Option **2** — switch to the inline component — implemented in three parts, all in the shared
library so every future header consumer inherits them:

| part | what |
|---|---|
| [`components/LookupField`](../../../../src/client/shared/Spaarke.UI.Components/src/components/LookupField/LookupField.tsx) | OOB affordances (right-side browse button · modern thin scrollbar · pinned right-aligned **Advanced** · deliberately **no "+ New"**) **+ a new `span` prop**, so it drops into `FieldGrid` without R1's hand-rolled wrapper `div` |
| [`RecordHeader/lookupSearch.ts`](../../../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/lookupSearch.ts) **(new)** | the Dataverse half — target metadata resolution, the OData search builder, and the **single library-wide `Xrm.Utility.lookupObjects` call site** |
| `RecordHeaderView` | picks the surface per cell: inline when editable **and** a target resolved, display renderer otherwise |

**The three costed items from the work spec, as they actually landed:**

1. **`onSearch`** — needed the target's `primaryNameAttribute`, so it is a real second
   `retrieveEntityMetadata` call (page-session cached). Read from metadata, never inferred; both
   conventions (`sprk_name` **and** `sprk_mattertypename`) are pinned by test.
2. **`span`** — added as a **prop** on the shared component (the preferred option), not a wrapper in
   the view. Omitting it emits no `gridColumn` at all, so the twelve flex-laid-out wizard consumers
   are unchanged — guarded by a test.
3. **`onAdvanced`** — routed through `openAdvancedLookup`, which is now the only place
   `lookupObjects` is called. `RecordHeaderLookupField` was refactored to delegate to it rather than
   keep a second copy of the G-14 `this`-binding discipline.

**Docs amended in the same commit** (CLAUDE.md §6.5 path B, not a silent divergence): `spec.md`
FR-15a + FR-26 + scope + criterion 15, and `design.md` §6.5 → new §6.5a.

**Task 080 parity improved**: the "identical except lookups" caveat is **withdrawn** — both controls
now render the same shared component, so Matter parity can be compared unqualified.

---

---

## ✅ Decision (owner, 2026-08-27) — inline, reproduced not hosted

**Hosting the OOB inline control is not possible.** `ComponentFramework.Factory` has exactly two
members (`getPopupService`, `requestRender`) — no `createComponent`. `lookupObjects` is a callable
*function* (the advanced **dialog**); the inline lookup is a control class the **form runtime owns**,
with no public constructor. `MscrmControls.AdvancedLookupWrapper` wraps the dialog, not the inline
control. `MscrmTools/PCF-Controls` is **GPL-3.0** (unusable as a dependency) and its own lookup
renders a custom dropdown rather than hosting the platform control.

So: reproduce the OOB shape with supported primitives, escalate to the real dialog for **Advanced**.

`components/LookupField` now does exactly that — right-side icon button that opens the full list,
independently-scrolling options with a modern thin scrollbar, and a pinned right-aligned **Advanced**
footer (opt-in `onAdvanced`). **No "+ New"** by owner decision, guarded by a test.

Option 3 (per-field `"picker"` config) remains **unbuilt and unjustified** — CLAUDE.md §11 wants a
concrete failure named first.

---

## Original analysis (retained)

---

## What was observed

Clicking **Project Type** in the header opens the **"Lookup Records" side pane** ("Select record",
with a search box and Add/Cancel). The OOB lookup on the same form instead drops an **inline
type-ahead list** directly under the field, showing matching records with icons and timestamps and an
*Advanced* link.

Both select the same record and stage the same value. The difference is purely the surface.

---

## Why it does this

By design, and the design predates the observation. FR-15/FR-15a specified the **OOB
`Xrm.Utility.lookupObjects` picker** for the editable lookup, and that API's UX *is* the side pane —
there is no option on it to render inline. `RecordHeaderLookupField` calls it with
`{ entityTypes: [targets[0]], allowMultiSelect: false }` and nothing about that call chooses a
surface.

The rationale recorded in task 023 was reuse: the platform picker handles security trimming, recent
items, "New", and Advanced Find for free, and it needs no search implementation of our own.

---

## The option, if we want inline

**A second `LookupField` already exists in the repo and already does this.**

| component | behaviour | used by |
|---|---|---|
| `components/RecordHeader/fields/LookupField.tsx` (barrel alias `RecordHeaderLookupField`) | OOB `lookupObjects` side pane — **what R2 ships** | RecordHeader |
| `components/LookupField/LookupField.tsx` | **inline search-as-you-type dropdown** | R1's `MatterHeaderView` |

R1's Matter header renders the *second* one — which is why the shipped Matter header feels closer to
OOB than R2's does. The project CLAUDE.md already warns these two are easy to confuse; this is the
first time the difference has had a user-visible consequence.

Switching would mean:

- the cell supplies its own `onSearch` (an OData `contains()` query against the target's primary name),
  which needs `primaryNameAttribute` for the TARGET entity — R2 currently resolves metadata for the
  HOST entity only, so this is a real second metadata read, not a prop change;
- losing the platform picker's Recent / New / Advanced Find affordances;
- `LookupField` (inline) has **no `span` prop** — R1 hand-rolled a `<div style={{ gridColumn }}>`
  wrapper around it, so `FieldGrid` integration is not free either.

Non-trivial, and it trades platform behaviour for visual parity.

---

## Recommendation

**Defer to a follow-up, decided deliberately.** Three options, in ascending cost:

1. **Keep the side pane.** It is the platform's own picker and behaves identically everywhere. Zero work.
2. **Switch to the inline component** for visual parity with OOB, accepting the search/metadata/span
   work above.
3. **Make it configurable** per field (`"picker": "inline" | "dialog"`) — the most work, and only
   worth it if different entities genuinely want different behaviour. CLAUDE.md §11 would want a
   concrete failure named before adding the knob.

Not blocking rollout: every lookup is functional today. Worth settling before task **080** (Matter
migration), because Matter is the parity regression test against R1 — and R1 uses the inline
component, so a strict parity read WILL flag this.
