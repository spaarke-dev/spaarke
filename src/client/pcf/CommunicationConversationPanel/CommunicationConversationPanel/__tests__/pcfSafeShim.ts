/**
 * Test-only shim for `@spaarke/ui-components/pcf-safe`.
 *
 * Production resolves this specifier to the built `dist/pcf-safe` (per the PCF
 * `tsconfig.json` `@spaarke/ui-components/*` → `dist/*` path mapping). Jest has no
 * tsconfig-paths support, so this shim re-exports the REAL `SprkModal` straight
 * from the shared TypeScript SOURCE (which ts-jest transforms), letting the
 * transform-robust portal test exercise the ACTUAL Fluent Dialog envelope — not a
 * mock (mirrors `uiComponentsShim.ts`). Wired via `jest.config.js`
 * `moduleNameMapper`. Deliberately minimal (only what `ConversationModal`
 * imports) so tests never drag in the full pcf-safe barrel.
 */
export { SprkModal } from '../../../../shared/Spaarke.UI.Components/src/components/SprkModal/SprkModal';
export type { SprkModalProps } from '../../../../shared/Spaarke.UI.Components/src/components/SprkModal/SprkModal';
