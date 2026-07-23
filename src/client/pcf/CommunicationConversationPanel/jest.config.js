/**
 * Jest configuration for CommunicationConversationPanel PCF.
 *
 * Tests the pure host-context / regarding-resolution helper (`hostContext.ts`),
 * the pure preview-bounding model (`previewModel.ts`), and the compact preview
 * component (`ConversationPreview.tsx`) in isolation (mirrors
 * CommunicationTimelineRegarding/jest.config.js).
 *
 * `@spaarke/ui-components` is mapped to the built `dist/` bundle so the shared
 * ConversationView / ConversationWorkspace / MessageQuickView / read-API /
 * mapping helpers resolve at test time exactly as they do at build time — the
 * PCF never re-implements any of them (NFR-06).
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>'],
  testMatch: ['**/__tests__/**/*.test.{ts,tsx}'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
    // Force ONE copy of React + Fluent so the shared-lib source (resolved via the
    // shim below) and this PCF's test share a single React/Fluent context —
    // otherwise the shared lib's nested react copy yields "Cannot read
    // properties of null (reading 'useContext')".
    '^react$': '<rootDir>/node_modules/react',
    '^react-dom$': '<rootDir>/node_modules/react-dom',
    '^react/(.*)$': '<rootDir>/node_modules/react/$1',
    '^react-dom/(.*)$': '<rootDir>/node_modules/react-dom/$1',
    '^@fluentui/react-components$': '<rootDir>/node_modules/@fluentui/react-components',
    '^@fluentui/react-icons$': '<rootDir>/node_modules/@fluentui/react-icons',
    // The shared lib ships ESM-only dist; map to a thin TS-source shim so the
    // REAL shared code (buildTimeline / mapRegardingReadResultToGroups /
    // MessageQuickView) is exercised via ts-jest (NFR-06 — no behavioral mock).
    '^@spaarke/ui-components$':
      '<rootDir>/CommunicationConversationPanel/__tests__/uiComponentsShim.ts',
  },
  setupFilesAfterEnv: ['<rootDir>/jest.setup.ts'],
  transform: {
    '^.+\\.tsx?$': ['ts-jest', { tsconfig: 'tsconfig.json' }],
  },
  moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
};
