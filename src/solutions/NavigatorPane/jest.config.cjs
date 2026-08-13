/**
 * Jest configuration for the NavigatorPane Code Page.
 *
 * Patterned after src/solutions/Notepad/jest.config.cjs. Wires
 * __tests__/ so ts-jest render tests execute. CJS extension is intentional —
 * NavigatorPane's package.json has no "type": "module" declaration, but this
 * keeps parity with the sibling code-page jest configs regardless.
 */

module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/src', '<rootDir>/__tests__'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  setupFilesAfterEnv: ['<rootDir>/jest.setup.cjs'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
    // Subpath imports (e.g. `@spaarke/ui-components/utils`) — without an
    // `exports` map on the shared lib package.json, Node/Jest CJS resolution
    // can't find them. Map them to the built dist subpaths.
    '^@spaarke/ui-components/(.*)$':
      '<rootDir>/../../client/shared/Spaarke.UI.Components/dist/$1',
    // @spaarke/ui-components is a separate `file:` package with its OWN
    // node_modules/react (no npm workspace hoisting in this repo) — without
    // forcing both the test render tree AND the ui-components dist bundle
    // onto ONE react instance, hooks called from the dist bundle (e.g.
    // useUiScale) execute against a react module whose internal dispatcher
    // was never set up by ITS OWN react-dom, producing
    // "Cannot read properties of null (reading 'useState')".
    '^react$': '<rootDir>/node_modules/react',
    '^react-dom$': '<rootDir>/node_modules/react-dom',
    '^react-dom/client$': '<rootDir>/node_modules/react-dom/client',
    '^react/jsx-runtime$': '<rootDir>/node_modules/react/jsx-runtime',
  },
  // @spaarke/ui-components' dist/index.js barrel transitively pulls in a few
  // ESM-only deps (useForceSimulation -> d3-force et al) even though
  // NavigatorBody itself never touches them. Mirrors the shared-lib's OWN
  // jest.config.js allow-list (src/client/shared/Spaarke.UI.Components/jest.config.js)
  // rather than inventing a narrower one.
  transformIgnorePatterns: [
    'node_modules/(?!(@spaarke|d3-force|d3-dispatch|d3-quadtree|d3-timer|marked)/)',
  ],
  transform: {
    '^.+\\.tsx?$': [
      'ts-jest',
      {
        tsconfig: {
          jsx: 'react',
          esModuleInterop: true,
          allowSyntheticDefaultImports: true,
          module: 'commonjs',
        },
      },
    ],
    '^.+\\.jsx?$': [
      'ts-jest',
      {
        tsconfig: {
          allowJs: true,
          esModuleInterop: true,
          module: 'commonjs',
        },
      },
    ],
  },
};
