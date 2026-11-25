/**
 * Jest config for the Spaarke decisions widget (spaarkeai-mcp-bff-api-r1 task 030).
 *
 * Patterned after src/solutions/InvoiceReview/jest.config.cjs (ts-jest, jsdom, React forced onto this package's copy). The MCP Apps SDK is
 * ESM-only and is imported by `mcp/mcpHost.ts` alone; no test loads that file, because components read the host through
 * `mcp/mcpHostContext.ts` (a test provides a plain object). Pure-logic tests (the fetch shim, the size gate) opt into the node
 * environment with a docblock, because jsdom has no `Response`.
 *
 * Run: `npm install --legacy-peer-deps --no-audit --no-fund && npm test` from this folder.
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  testTimeout: 20000,
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  setupFilesAfterEnv: ['<rootDir>/jest.setup.cjs'],
  // The wizard's email composer reaches RichTextEditor in the shared lib, which imports lexical and @lexical/* (react, rich-text, list,
  // html, link, utils, selection). A shared file resolves a bare name from ITS folder, and CI has no node_modules there, so the names
  // that are not mapped below are also searched in this package's node_modules. (A path mapper per package would bypass the packages'
  // own `exports`: `@lexical/react/ReactProviderExtension` is the file `LexicalReactProviderExtension.js`.)
  modulePaths: ['<rootDir>/node_modules'],
  moduleNameMapper: {
    '^@spaarke/ui-components/(.*)$': '<rootDir>/../../shared/Spaarke.UI.Components/src/$1',
    '^@fluentui/react-components$': '<rootDir>/node_modules/@fluentui/react-components',
    '^@fluentui/react-icons$': '<rootDir>/node_modules/@fluentui/react-icons',
    // One lexical for every package that imports it (see `modulePaths`).
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
