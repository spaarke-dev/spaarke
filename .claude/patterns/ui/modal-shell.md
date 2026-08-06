# Modal Shell Pattern (component layer)

> **Last Reviewed**: 2026-08-01 (spaarke-modal-system FR-10)
> **Status**: Current

## When
Use whenever a task BUILDS or modifies a modal — a confirm, choice, form, preview, browse, or wizard surface — on any Spaarke client surface. This is the COMPONENT layer (the shell + presets you implement with); [`record-modal-selection.md`](record-modal-selection.md) is the DECISION layer (which family to reach for).

## Read These Files
1. `docs/standards/MODAL-DESIGN-SYSTEM.md` — the canonical component guide: the 7-size scale (exact numbers), header/footer contracts, dismiss semantics, theming, component names, wiring recipe, do/don't
2. `src/client/shared/Spaarke.UI.Components/src/components/SprkModal/SprkModal.tsx` — the shell (Dialog envelope + header + body + footer); `SprkModal.types.ts` for the prop contract
3. `src/client/shared/Spaarke.UI.Components/src/components/SprkModal/sizes.ts` — the named size scale (`getSurfaceStyle`, `SIZE_SPEC`)
4. `src/client/shared/Spaarke.UI.Components/src/components/SprkModal/presets/` — the six presets to configure or copy

## Constraints
- **ADR-050** — one canonical `SprkModal` shell + thin presets; compose `ModalWindowControls` / `RecordNavigationModalShell`; no per-surface bespoke envelope
- **ADR-021** (strengthened) — Fluent v9 semantic tokens only; ZERO hex, `'1px'` literals, or inline color in modal components
- **ADR-012** — the shell + presets live in `@spaarke/ui-components`; do not duplicate per solution
- **ADR-028** — pass callbacks / `authenticatedFetch` as functions; never snapshot tokens/auth in modal props

## Key Rules
- **Configure, don't create** — a new modal is a thin config of `SprkModal` or a preset, imported from `@spaarke/ui-components`. Never hand-roll a `position:fixed`/`createElement` overlay.
- **Keep the Fluent `Dialog` envelope** — its portal survives a transformed ancestor (transform-robust centering).
- **Scale via the scaled theme** (`scaleTheme` for `--sprk-ui-scale`), never CSS `zoom`.
- **Cancel always left** (`footerStart`); danger primary via a token class, never inline color.
- **Native thin scrollbar** for the body; the chevron pager (`bodyScroll="arrows"`) is opt-in and never disables native scroll.
