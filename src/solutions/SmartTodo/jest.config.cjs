/**
 * Jest configuration for the SmartTodo Code Page (R4-114, 2026-06-25).
 *
 * Wires the previously-shim test files in src/**\/__tests__/ so they actually
 * execute. CJS extension is intentional — SmartTodo is "type": "module" but
 * Jest reads .cjs files as CommonJS regardless.
 *
 * Patterned after src/client/shared/Spaarke.UI.Components/jest.config.js.
 */

module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  setupFilesAfterEach: ['<rootDir>/jest.setup.cjs'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
    // Subpath imports (e.g. `@spaarke/ui-components/utils`) — without an
    // `exports` map on the shared lib package.json, Node/Jest CJS resolution
    // can't find them. Map them to the built dist subpaths.
    '^@spaarke/ui-components/(.*)$':
      '<rootDir>/../../client/shared/Spaarke.UI.Components/dist/$1',
    '^@spaarke/auth/(.*)$':
      '<rootDir>/../../client/shared/Spaarke.Auth/dist/$1',
    '^@spaarke/sdap-client/(.*)$':
      '<rootDir>/../../client/shared/Spaarke.SdapClient/dist/$1',
    '^@spaarke/smart-todo-components/(.*)$':
      '<rootDir>/../../client/shared/Spaarke.SmartTodo.Components/dist/$1',
  },
  transformIgnorePatterns: [
    'node_modules/(?!(d3-force|d3-dispatch|d3-quadtree|d3-timer|marked|@spaarke)/)',
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
