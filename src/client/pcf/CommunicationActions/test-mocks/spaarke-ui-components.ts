/**
 * Test-local stand-in for `@spaarke/ui-components`, mapped in `jest.config.js`
 * ONLY for this PCF's test run.
 *
 * `@spaarke/ui-components`'s published `dist/index.js` uses ESM `export *`
 * syntax; this PCF's `ts-jest` transform (scoped to `.tsx?` under
 * `transform`) does not touch `node_modules/@spaarke/ui-components`, so a
 * runtime `import` of the real package throws `SyntaxError: Unexpected
 * token 'export'` here (task 081 / C-8 finding — `launchCreate.ts` added a
 * VALUE import of `getXrm`, where the module previously had none; the
 * sibling `attachmentsSource.ts`'s `IAttachmentItem` import is `import
 * type`, which TypeScript erases before this would ever matter).
 *
 * Re-exports straight from the real package's TypeScript SOURCE (not dist)
 * — mirrors this same `jest.config.js`'s existing `@spaarke/communication-
 * components/logic/actions` → source mapping, one level further down the
 * dependency chain.
 */
export { getXrm } from '../../../shared/Spaarke.UI.Components/src/utils/xrmContext';
export type { IAttachmentItem } from '../../../shared/Spaarke.UI.Components/src/components/EmailComposer/EmailComposer.types';
