/**
 * Jest configuration for the Notepad Code Page.
 *
 * Patterned after src/solutions/SmartTodo/jest.config.cjs. Wires
 * src/**\/__tests__/ so Vitest-agnostic tests execute. CJS extension is
 * intentional — Notepad is "type": "module" but Jest reads .cjs files as
 * CommonJS regardless.
 */

module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  setupFilesAfterEnv: ['<rootDir>/jest.setup.cjs'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
    // Subpath imports (e.g. `@spaarke/ui-components/utils`) — without an
    // `exports` map on the shared lib package.json, Node/Jest CJS resolution
    // can't find them. Map them to the built dist subpaths.
    '^@spaarke/ui-components/(.*)$':
      '<rootDir>/../../client/shared/Spaarke.UI.Components/dist/$1',
  },
  transformIgnorePatterns: [
    'node_modules/(?!(@spaarke)/)',
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
