/**
 * Jest configuration for CommunicationAttachments PCF.
 *
 * Tests the attachment list rendering (name + type, inline-image filter,
 * .eml routing) and the row-click -> open wiring in isolation. Mocks the deep
 * `@spaarke/ui-components/dist/...` RichFilePreviewDialog import and `@spaarke/auth`
 * so we can assert the PCF only orchestrates the shared modal + preview URL fetch.
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>'],
  testMatch: ['**/__tests__/**/*.test.{ts,tsx}'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
    // Deep-path shared-lib import + @spaarke/auth are resolved to hand-written
    // mocks under __tests__/__mocks__ so tests run without building dist/.
    '^@spaarke/ui-components/dist/components/FilePreview/RichFilePreviewDialog$':
      '<rootDir>/__tests__/__mocks__/richFilePreviewDialog.tsx',
    '^@spaarke/auth$': '<rootDir>/__tests__/__mocks__/spaarkeAuth.ts',
    // Task 021: the Layer-1 attachments logic + the promoted AttachmentList
    // presentational core now live in the shared lib
    // (`@spaarke/communication-components`). Map the package specifiers to the
    // shared TS source so tests exercise the re-homed code directly (mirrors
    // the CommunicationConnections task-020 pattern — no dist/ build required).
    '^@spaarke/communication-components/logic/attachments$':
      '<rootDir>/../../shared/Spaarke.Communication.Components/src/logic/attachments/index.ts',
    '^@spaarke/communication-components/components/AttachmentList$':
      '<rootDir>/../../shared/Spaarke.Communication.Components/src/components/AttachmentList/index.ts',
    // `generated/ManifestTypes` is produced by the PCF build (gitignored); map
    // it to a stub so the App/index compile under jest without a prior build.
    'generated/ManifestTypes$': '<rootDir>/__tests__/__mocks__/manifestTypes.ts',
    // The promoted `AttachmentList` is deep-imported from
    // `Spaarke.Communication.Components/src/...`, a SIBLING package whose OWN
    // node_modules carries React 19 (it targets Code Pages too). Left
    // unmapped, Jest resolves that file's `import ... from 'react'` against
    // the shared lib's own node_modules — a SECOND React module instance
    // alongside this PCF's React 16 (used by react-dom/testing-library here) —
    // which breaks hooks (`useContext` on null: "two copies of React" /
    // Invalid Hook Call). At real PCF runtime this can't happen: webpack
    // externalizes `react`/`react-dom` to the single Dataverse platform
    // library for every module in the bundle (ADR-022). These two mappings
    // reproduce that same single-instance guarantee under Jest by forcing
    // EVERY `react`/`react-dom` import (including from the deep-imported
    // shared source) to resolve to this PCF's own copy. Test-config only —
    // no production code changed; not the ADR-022 `as React.ComponentType`
    // cast (which bridges TYPES, not module identity, and isn't needed here
    // since `tsc`/webpack both already build clean).
    '^react$': '<rootDir>/node_modules/react',
    '^react-dom$': '<rootDir>/node_modules/react-dom',
    '^react/jsx-runtime$': '<rootDir>/node_modules/react/jsx-runtime',
  },
  setupFilesAfterEnv: ['<rootDir>/jest.setup.ts'],
  transform: {
    '^.+\\.tsx?$': ['ts-jest', { tsconfig: 'tsconfig.json' }],
  },
  moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
};
