/**
 * Jest config for the Spaarke search-results widget (spaarkeai-mcp-bff-api-r2 task 035).
 *
 * Same shape as the briefing widget's config (ts-jest, jsdom, React forced onto this package's copy). The MCP Apps SDK is ESM-only and is
 * imported by `../shared/mcp/mcpHost.ts` alone; a test provides the host connection as a plain object through `mcpHostContext`.
 * Pure-logic tests opt into the node environment with a docblock.
 *
 * Run: `npm install --legacy-peer-deps --no-audit --no-fund && npm test` from this folder.
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  testTimeout: 20000,
  // `../shared` holds the shared widget MCP transport and its tests (task 021); the Decisions and briefing widgets run them, so this one need not.
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  setupFilesAfterEnv: ['<rootDir>/jest.setup.cjs'],
  // A shared file resolves a bare name from ITS folder, and CI has no node_modules there, so names not mapped below are also searched here.
  modulePaths: ['<rootDir>/node_modules'],
  moduleNameMapper: {
    '^@fluentui/react-components$': '<rootDir>/node_modules/@fluentui/react-components',
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
