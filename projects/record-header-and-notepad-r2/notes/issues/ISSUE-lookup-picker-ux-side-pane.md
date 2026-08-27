# ISSUE — the lookup opens the side-pane picker, not the OOB inline dropdown

> **Raised**: 2026-08-27 by owner, UAT round 5 of RecordHeader v1.1.8
> **Status**: 🟠 open — **cosmetic/UX, not a defect.** The picker works; it is the wrong *kind* of picker.
> **Severity**: low-medium — it is a visible departure from OOB behaviour on every lookup cell, on every entity.

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
