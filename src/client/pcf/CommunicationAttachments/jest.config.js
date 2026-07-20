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
    // `generated/ManifestTypes` is produced by the PCF build (gitignored); map
    // it to a stub so the App/index compile under jest without a prior build.
    'generated/ManifestTypes$': '<rootDir>/__tests__/__mocks__/manifestTypes.ts',
  },
  setupFilesAfterEnv: ['<rootDir>/jest.setup.ts'],
  transform: {
    '^.+\\.tsx?$': ['ts-jest', { tsconfig: 'tsconfig.json' }],
  },
  moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
};
