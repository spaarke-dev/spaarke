/**
 * Jest config for the Invoice Review code page (ebilling-r1 task 110).
 *
 * Patterned after src/solutions/InvoiceFiles/jest.config.cjs (ts-jest, jsdom, `@spaarke/auth` mapped to its source, React forced onto this
 * package's copy) and src/solutions/EventDetailSidePane/jest.config.cjs (`@spaarke/ui-components` mapped to a test seam, so the library barrel,
 * with its lexical / d3 / pdfjs graph, is never loaded). The seam re-exports the REAL shared components the page composes from their
 * source files (MetricCardRow, PanelSplitter, useTwoPanelLayout, StatusBar, StatusBadge, EvidenceLine, AiFieldTag, FormModal, RecordRow,
 * WizardShell) and stands in
 * for PdfHighlightViewer only (108 tests the viewer with pdfjs faked; here the page's highlight prop is what is checked). Fluent resolves to
 * this package's copy for the shared sources too, so every component shares one React and one Fluent.
 *
 * Run: `npm install --legacy-peer-deps --no-audit --no-fund && npm test` from this folder.
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
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
