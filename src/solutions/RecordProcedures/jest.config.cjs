/**
 * Jest config for the Record Procedures code page (ebilling-r1 task 084a).
 *
 * Patterned after src/solutions/InvoiceReview/jest.config.cjs (ts-jest, jsdom, `@spaarke/auth` mapped to its source, React forced onto this
 * package's copy, and `@spaarke/ui-components` mapped to a test seam so the library barrel, with its lexical / d3 / pdfjs graph, is never
 * loaded). The seam re-exports the REAL shared components the page composes from their source files (MetricCardRow, StatusBadge). Fluent
 * resolves to this package's copy for the shared sources too, so every component shares one React and one Fluent.
 *
 * Run: `npm install --legacy-peer-deps --no-audit --no-fund && npm test` from this folder.
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  // The panel tests mount Fluent tabs, dropdowns and tables; on a cold ts-jest cache under load one can pass 5 s.
  testTimeout: 20000,
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  setupFilesAfterEnv: ['<rootDir>/jest.setup.cjs'],
  moduleNameMapper: {
    '^@spaarke/auth$': '<rootDir>/../../client/shared/Spaarke.Auth/src/index.ts',
    '^@spaarke/ui-components$': '<rootDir>/src/__tests__/uiComponentsTestSeam.ts',
    '^@fluentui/react-components$': '<rootDir>/node_modules/@fluentui/react-components',
    '^@fluentui/react-icons$': '<rootDir>/node_modules/@fluentui/react-icons',
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
