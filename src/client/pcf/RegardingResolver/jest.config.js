/**
 * Jest configuration for RegardingResolver PCF.
 *
 * Tests the React component + ResolverWriteHandler in isolation.
 *
 * # Deep-import resolution (fixed 2026-09-04, unified-access-control-r2 task 051)
 *
 * The control imports the shared library through DEEP `dist/` paths per ADR-012's
 * PCF import rule (the `dist/index` barrel re-exports SprkChat → pdfjs, which the
 * PCF webpack build cannot transform). Those `dist/*.js` files are ESM
 * (`export async function ...`), which Jest cannot parse — so BOTH suites failed
 * at module load with `SyntaxError: Unexpected token 'export'`, and the
 * `jest.mock('@spaarke/ui-components')` barrel mocks in both test files had been
 * inert since the deep-import refactor (they mock a specifier nothing imports).
 *
 * Mapping every `@spaarke/ui-components/dist/*` specifier onto the shared TS
 * SOURCE lets ts-jest transform it, and — more usefully — means the FR-26
 * core-ancestor derivation is exercised for real in `ResolverWriteHandler.test.ts`
 * rather than mocked away. A test that mocks the derivation cannot prove the
 * ancestor stamp reaches the payload, which is the whole point of FR-26.
 *
 * Suites that DO want a stub still get one: `jest.mock()` is per-file and targets
 * the deep specifier directly (see `RegardingResolverApp.test.tsx`).
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>'],
  testMatch: ['**/__tests__/**/*.test.{ts,tsx}'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
    // Deep shared-lib imports → shared TS source (see docstring above).
    '^@spaarke/ui-components/dist/(.*)$': '<rootDir>/../../shared/Spaarke.UI.Components/src/$1',
    // `generated/ManifestTypes` is emitted by the PCF build (gitignored); map it
    // to a stub so the App/index compile under Jest without a prior build.
    'generated/ManifestTypes$': '<rootDir>/__tests__/__mocks__/manifestTypes.ts',
    // The shared lib has its OWN node_modules carrying React 19 (it targets Code
    // Pages too). Left unmapped, a deep-imported shared source file resolves
    // `react` against THAT copy — a second React instance alongside this PCF's
    // React 16 — which breaks hooks ("two copies of React" / Invalid Hook Call).
    // At real PCF runtime this cannot happen: webpack externalizes react/react-dom
    // to the single Dataverse platform library (ADR-022). These mappings reproduce
    // that single-instance guarantee under Jest. Test-config only.
    '^react$': '<rootDir>/node_modules/react',
    '^react-dom$': '<rootDir>/node_modules/react-dom',
    // `react/jsx-runtime` needs its own entry — `^react$` does not cover it, and
    // it is the path Fluent's `@fluentui/react-jsx-runtime` actually takes. Left
    // out, every Fluent render inside a deep-imported shared component reaches
    // React 19 and dies on `recentlyCreatedOwnerStacks` (a React 19 internal
    // absent from React 16).
    '^react/jsx-runtime$': '<rootDir>/node_modules/react/jsx-runtime',
  },
  setupFilesAfterEnv: ['<rootDir>/jest.setup.ts'],
  transform: {
    '^.+\\.tsx?$': ['ts-jest', { tsconfig: 'tsconfig.json' }],
  },
  moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
};
