/**
 * SprkModal — the canonical Spaarke modal system (spec FR-01).
 *
 * One Fluent v9 `Dialog`-envelope shell + six presets, all thin configs sharing
 * one named size scale, the standard header/footer, and dismiss semantics. See
 * `docs/standards/MODAL-DESIGN-SYSTEM.md` (the component layer) and ADR-050.
 */

// Base shell + public types (SprkModalProps / Dismiss / BodyScroll / Nav)
export * from './SprkModal';

// Size scale (SprkModalSize / SprkModalLayout, getSurfaceStyle, SIZE_SPEC, widthLabel)
export * from './sizes';

// Scaled Fluent theme for `--sprk-ui-scale` (scaleTheme, baseTheme)
export * from './scaledTheme';

// App-shell uiScale derivation (P0.5, FR-06/design §6.9): auto ≥2560 breakpoint
// + persisted "Display size" setting → one effective uiScale. Consumed via
// hooks/useUiScale.ts by the app shells (SpaarkeAi, LegalWorkspace).
export * from './uiScale';

// Opt-in chevron-pager body variant (`bodyScroll="arrows"`)
export * from './ModalScrollArea';

// Presets — thin configs of SprkModal
export * from './presets/ConfirmModal';
export * from './presets/ChoiceModal';
export * from './presets/FormModal';
export * from './presets/PreviewModal';
export * from './presets/BrowseModal';
export * from './presets/WizardModal';
