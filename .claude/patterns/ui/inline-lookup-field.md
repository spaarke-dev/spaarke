# Inline Lookup Field — Pattern Pointer

> **Last Reviewed**: 2026-08-27 (`record-header-and-notepad-r2`, v1.1.9–v1.1.11)
> **Status**: Current
> **Load when**: adding or modifying ANY lookup / entity-reference / search-as-you-type field on any
> Spaarke surface — PCF, Code Page, wizard step, workspace widget.

## When

A user must pick a Dataverse record for a lookup field. **Do not build a new one** — compose
`@spaarke/ui-components`' `LookupField`. It already has ~14 consumers, and every fix below landed
for all of them at once.

## ⚠️ TWO components share the name — do not confuse them

| path | what it is | use for |
|---|---|---|
| [`components/LookupField/LookupField.tsx`](../../../src/client/shared/Spaarke.UI.Components/src/components/LookupField/LookupField.tsx) | **inline type-ahead dropdown** — THE one you want | any editable lookup |
| [`components/RecordHeader/fields/LookupField.tsx`](../../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/fields/LookupField.tsx) (barrel alias **`RecordHeaderLookupField`**) | display + navigate-to-record | read-only cells only |

The barrel aliases the second one precisely because the names collide. Importing the wrong one is
the single most likely mistake here.

## Read These Files

1. `src/client/shared/Spaarke.UI.Components/src/components/LookupField/LookupField.tsx` — the component + full prop docs
2. `src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/lookupSearch.ts` — the Dataverse half for Xrm hosts (target metadata → OData search → advanced dialog)
3. `src/client/pcf/RecordHeader/control/RecordHeaderView.tsx` (`case 'lookup'`) — the reference wiring, including the read-only fallback
4. `src/client/shared/Spaarke.UI.Components/src/components/LookupField/__tests__/LookupField.test.tsx` — what is contractually pinned

## The wiring contract

```tsx
<LookupField
  label={label} value={item} onChange={stage}
  onSearch={search}                 // YOU supply this — the component knows nothing about Dataverse
  span={1}                          // only inside a FieldGrid/CSS grid; omit elsewhere
  appearance="filled-darker"        // only to match an OOB FORM field; omit elsewhere
  onAdvanced={openAdvanced}         // opt-in; omit where Xrm.Utility.lookupObjects is absent
  minSearchLength={0} openOnFocus   // browse-without-typing
/>
```

**Xrm-hosted surfaces (PCF, MDA Code Page)** get `onSearch` + `onAdvanced` for free from
`useLookupTargetSearch(target, label, onPick)` in `lookupSearch.ts`. **Non-Xrm surfaces** (BFF-backed
Code Pages — the twelve `Create*Wizard` steps) supply their own `onSearch` and omit `onAdvanced`;
the component itself stays host-agnostic per ADR-012.

## Constraints

- **ADR-012** — the component is context-agnostic. Never put `Xrm`, BFF or entity names inside it; inject via `onSearch`.
- **ADR-021** — Fluent v9 semantic tokens only, zero hex.
- **ADR-022** — React 16/17-safe (consumed by PCF).

## Key Rules (each one cost a UAT round)

- **You CANNOT host the OOB inline lookup control.** `ComponentFramework.Factory` has exactly two members (`getPopupService`, `requestRender`); the inline lookup is a class the form runtime owns with no public constructor. `Xrm.Utility.lookupObjects` is callable only because it is a plain function opening the **advanced dialog**. Settled empirically 2026-08-27 — see `projects/record-header-and-notepad-r2/design.md` §6.5a. **Do not re-investigate.**
- **Read the target's `primaryNameAttribute` from metadata — never infer it.** `sprk_projecttype_ref` uses `sprk_name`; `sprk_mattertype_ref` uses `sprk_mattertypename`. R1's hard-coded `LOOKUP_META` is exactly what this replaced.
- **Call `xrm.Utility.lookupObjects` directly, never through a local alias** — FAILURE-MODES **G-14**. `openAdvancedLookup` is the ONLY call site library-wide; route through it.
- **Panels below the field MUST be `position: absolute`.** A `box-shadow` paints over neighbours but does not remove an element from flow — in-flow panels push the whole form down and it snaps back on commit ("the screen jumps").
- **The committed chip must match the input's height** (32px, Fluent `fieldHeights.medium`), or swapping them reflows the row.
- **Keep focus in the `<input>` while browsing.** Fluent draws the brand underline from `:focus-within` on the Input root, so it dies the moment focus moves to the button or an option row. Focus the input explicitly on browse; `preventDefault` mousedown on the button AND the option rows.
- **`appearance` defaults to `outline` — do not change the default.** `filled-darker` is the OOB form-field look (no border, `colorNeutralBackground3`, brand underline on focus) and belongs only where the field sits among OOB form fields. Wizard steps sit beside plain outline inputs.
- **No "+ New".** Owner decision 2026-08-27 — these target taxonomy tables users cannot add to. A test guards it. The per-row entity icon and entity-name group header are likewise dropped for good ("cleaner without it"). These are decisions, not gaps.
- **Use `thinScrollbarStyle`** from `theme/scrollbar.ts` for the option list — do not hand-roll scrollbar CSS. See [`thin-scrollbar.md`](thin-scrollbar.md).

## Blast radius

Shared by ~14 surfaces: the twelve `Create*Wizard` steps, `MatterHeader`, and `RecordHeader`. Any
change here ships to all of them on their next build, and **PCFs bundle `dist/`, not source** — rebuild
the shared lib first. A narrow grep under-reports this; see
`projects/record-header-and-notepad-r2/current-task.md` § "Update radius" for the verified list.

## See also

- [`record-header-composition.md`](record-header-composition.md) — the header that consumes it
- [`record-modal-selection.md`](record-modal-selection.md) — "proprietary browse + OOB escalation" is the pattern **Advanced** implements
- [`thin-scrollbar.md`](thin-scrollbar.md) · [`fluent-v9-component-authoring.md`](fluent-v9-component-authoring.md)
