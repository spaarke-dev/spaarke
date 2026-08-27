# Record Header Composition — Pattern Pointer

> **Last Reviewed**: 2026-07-03
> **Status**: Current (project: `record-header-and-notepad-r1`)
> **Load when**: modifying `MatterHeaderPcf`; consuming `@spaarke/ui-components` header primitives from any surface.

> ⚠️ **Do NOT author a new per-entity Record Header PCF.** That approach was withdrawn 2026-08-21.
> R2 replaces it with ONE configuration-driven `RecordHeader` control serving every entity — see
> [`projects/record-header-and-notepad-r2/design.md`](../../../projects/record-header-and-notepad-r2/design.md).
> The authoring guide below still documents the retired per-entity recipe; it is rewritten as part of R2.

> **Lookup cells**: the header's editable lookup is the shared inline `LookupField` — load
> [`inline-lookup-field.md`](inline-lookup-field.md) before touching one. It also explains the two
> same-named components (`LookupField` vs `RecordHeaderLookupField`), which is the easiest mistake
> to make in this area.

## Canonical code

- **Shared primitives**: [`src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/`](../../../src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/) · [`.../RecordHeader/`](../../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/) · [`.../hooks/`](../../../src/client/shared/Spaarke.UI.Components/src/hooks/) (`useRecordFieldValues`, `useRelatedCount`, `useRecordHeaderToolbarActions`, `toolbarLaunchDefaults`)
- **Reference PCF**: [`src/client/pcf/MatterHeader/`](../../../src/client/pcf/MatterHeader/) — v1 thin PCF (~80 LOC) composing the primitives + version footer + Popover shell owned by consumer
- **Notepad code page**: [`src/solutions/Notepad/`](../../../src/solutions/Notepad/) — standalone Vite React 18 SPA, entity-agnostic URL launch contract

## Authoring guide

Follow [`docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md`](../../../docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md) for the complete pattern: manifest anatomy, `ComponentFramework.ReactControl` class shell, view composition, ProjectHeaderPcf worked example, Notepad launch contract, and the **mandatory bundle-optimization triad** (`featureconfig.json` + `webpack.config.js` + deep-path imports from `@spaarke/ui-components/dist/components/RecordHeader` — post-Wave-8 discovery, keeps bundles at ~40 KB vs 1.6 MB without).

## Duplication pointers (in-code work triggers — NOT filed as GitHub issues)

Two files in the repo duplicate concerns that this pattern owns. Do NOT rush to
migrate them — the R1 project boundary forbade touching them, and they work.
Only migrate when someone is already editing those files for another reason;
that is the "extend existing when there anyway" trigger from CLAUDE.md §11.

- **`src/client/pcf/VisualHost/control/components/CardChrome.tsx`** (DEF-03)
  — this file re-implements the same intent as `HeaderToolbar` (title strip +
  icon slots + badges). When you next touch `CardChrome.tsx` for any reason,
  evaluate migrating callers to `@spaarke/ui-components/HeaderToolbar` to
  remove the duplication. Blocked in R1 (CLAUDE.md "MUST NOT modify
  VisualHost/**"); rescheduled to R2B when someone touches VisualHost.

- **`src/solutions/EventDetailSidePane/**/MemoSection.tsx`** (DEF-04) —
  duplicates the memo CRUD logic that `useSprkMemoRepository` (currently at
  `src/solutions/Notepad/src/hooks/useSprkMemoRepository.ts`) now handles.
  When someone next touches `MemoSection.tsx` for any reason, evaluate:
  (a) migrating it to consume `useSprkMemoRepository`, and (b) simultaneously
  promoting the hook to `@spaarke/ui-components/hooks/` (DEF-08) since
  MemoSection would be the second consumer that satisfies CLAUDE.md §11.
  Blocked in R1 (CLAUDE.md "MUST NOT modify EventDetailSidePane/**");
  rescheduled to R2B when someone touches EventDetailSidePane.

## Follow-on project

[`projects/record-header-and-notepad-r2/design.md`](../../../projects/record-header-and-notepad-r2/design.md)
— **re-scoped 2026-08-21**. Generalizes `MatterHeaderPcf` into ONE configuration-driven
`Spaarke.Records.RecordHeader` control (layout supplied as JSON on a manifest property, with
defaults derived from form metadata), rolling out to Project + Work Assignment first, then
Invoice + Event, with Matter migrated last. Adds date / number-currency / boolean renderers and
an editable option set to the shared lib.

The earlier plan — four cloned per-entity PCFs, plus the shared-lib `exports` migration (DEF-06)
and the `useSprkMemoRepository` promotion (DEF-08) — is **withdrawn**. DEF-06 and DEF-08 are both
out of R2 scope; DEF-08's trigger remains DEF-04 below.

## See also

- Spec: [`projects/record-header-and-notepad-r1/spec.md`](../../../projects/record-header-and-notepad-r1/spec.md)
- ADRs: 006, 012, 021, 022, 024, 038 (all cited in the guide)
- Pattern: [`fluent-v9-component-authoring.md`](fluent-v9-component-authoring.md) · [`fluent-v9-react-version-boundaries.md`](fluent-v9-react-version-boundaries.md) · [`record-modal-selection.md`](record-modal-selection.md)
