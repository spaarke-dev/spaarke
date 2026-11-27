/**
 * Jest config for the Spaarke briefing widget (spaarkeai-mcp-bff-api-r2 task 034).
 *
 * Same shape as the Decisions widget's config (ts-jest, jsdom, React forced onto this package's copy). The MCP Apps SDK is ESM-only and is
 * imported by `../shared/mcp/mcpHost.ts` alone; a test provides the host connection as a plain object through `mcpHostContext`.
 * `@spaarke/auth` and the `@spaarke/ui-components` barrel are mapped to the same build-time shims the bundle uses, so a test exercises the
 * code that ships. Pure-logic tests opt into the node environment with a docblock, because jsdom has no `Response`.
 *
 * Run: `npm install --legacy-peer-deps --no-audit --no-fund && npm test` from this folder.
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  testTimeout: 20000,
  // `../shared` holds the shared widget MCP transport and its tests (task 021); this widget runs them alongside the Decisions widget.
  roots: ['<rootDir>/src', '<rootDir>/../shared'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  setupFilesAfterEnv: ['<rootDir>/jest.setup.cjs'],
  // A shared file resolves a bare name from ITS folder, and CI has no node_modules there, so names not mapped below are also searched here.
  modulePaths: ['<rootDir>/node_modules'],
  moduleNameMapper: {
    '^@spaarke/auth$': '<rootDir>/src/shims/spaarkeAuth.ts',
    '^@spaarke/ui-components$': '<rootDir>/src/shims/uiComponents.ts',
    '^@spaarke/ui-components/services$': '<rootDir>/src/shims/uiServices.ts',
    '^@spaarke/ui-components/(.*)$': '<rootDir>/../../shared/Spaarke.UI.Components/src/$1',
    '^@spaarke/daily-briefing-components$': '<rootDir>/../../shared/Spaarke.DailyBriefing.Components/src/index.ts',
    '^@spaarke/daily-briefing-components/(.*)$': '<rootDir>/../../shared/Spaarke.DailyBriefing.Components/src/$1',
    '^@fluentui/react-components$': '<rootDir>/node_modules/@fluentui/react-components',
    '^@fluentui/react-icons$': '<rootDir>/node_modules/@fluentui/react-icons',
    '^lexical$': '<rootDir>/node_modules/lexical',
    '^react$': '<rootDir>/node_modules/react',
    '^react-dom$': '<rootDir>/node_modules/react-dom',
    '^react-dom/client$': '<rootDir>/node_modules/react-dom/client',
    '^react/jsx-runtime$': '<rootDir>/node_modules/react/jsx-runtime',
  },
  transform: {
    '^.+\\.tsx?$': [
      'ts-jest',
      {
        tsconfig: {
          jsx: 'react-jsx',
          esModuleInterop: true,
          allowSyntheticDefaultImports: true,
          module: 'commonjs',
          moduleResolution: 'node',
          strict: true,
          isolatedModules: true,
          skipLibCheck: true,
          types: ['jest', '@testing-library/jest-dom'],
        },
      },
    ],
  },
};
