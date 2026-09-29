/** @type {import('jest').Config} */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/__tests__'],
  testMatch: ['**/__tests__/**/*.test.tsx', '**/__tests__/**/*.test.ts'],
  moduleNameMapper: {
    // Resolve `@spaarke/ui-components/dist/*` to the shared library's TypeScript
    // SOURCE rather than its compiled `dist/`.
    //
    // Why: `dist/` is emitted as ES modules, and this project's only transform
    // is ts-jest on `^.+\.tsx?$` — so a `dist/*.js` file reaches the runtime
    // untransformed and dies on `SyntaxError: Unexpected token 'export'`.
    //
    // MatterHeader solves this by `jest.mock()`ing every shared subpath. That is
    // wrong for THIS control: config resolution, span clamping and renderer
    // derivation are the behavior under test, so mocking them out would leave
    // the suite asserting its own mocks. Mapping to source keeps the real
    // `resolveHeaderConfig` + real renderers in the tree, and ts-jest compiles
    // them exactly as it compiles the control.
    //
    // The PRODUCTION build is unaffected — it still consumes `dist/` through the
    // deep-path imports the bundle-size triad requires (NFR-08).
    '^@spaarke/ui-components/dist/(.*)$': '<rootDir>/../../shared/Spaarke.UI.Components/src/$1',
    // Pin React to ONE instance. Mapping the shared library to source (above)
    // means its files resolve `react` from the shared library's own
    // node_modules, giving the tree two React copies and the classic "Invalid
    // hook call / hooks can only be called inside a function component" failure.
    // At runtime this cannot happen — the platform library supplies exactly one
    // React (ADR-022) — so this mapping restores the production invariant.
    '^react$': '<rootDir>/node_modules/react',
    '^react-dom$': '<rootDir>/node_modules/react-dom',
    '^react/(.*)$': '<rootDir>/node_modules/react/$1',
    '^react-dom/(.*)$': '<rootDir>/node_modules/react-dom/$1',
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
    '\\.(jpg|jpeg|png|gif|svg)$': '<rootDir>/__mocks__/fileMock.js',
  },
  setupFilesAfterEnv: ['<rootDir>/jest.setup.ts'],
  transform: {
    '^.+\\.tsx?$': [
      'ts-jest',
      {
        tsconfig: 'tsconfig.test.json',
        diagnostics: {
          ignoreCodes: [151001],
        },
      },
    ],
  },
  transformIgnorePatterns: [
    '/node_modules/(?!(@fluentui)/)',
  ],
  moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
  testPathIgnorePatterns: ['/node_modules/', '/out/'],
  globals: {
    Xrm: undefined,
  },
};
