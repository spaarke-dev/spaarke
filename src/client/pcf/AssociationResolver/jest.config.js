/**
 * Jest configuration for AssociationResolver PCF.
 *
 * Tests the RecordSelectionHandler thin adapter (SRFR-051) in isolation. Mocks
 * `@spaarke/ui-components` for `buildRecordUrl`, `resolveRecordType`, and
 * `resolveRecordNumberFieldName` so we can assert delegation to the shared
 * service (FR-C1-01 / ADR-024).
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>'],
  testMatch: ['**/__tests__/**/*.test.{ts,tsx}'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
  },
  setupFilesAfterEnv: ['<rootDir>/jest.setup.ts'],
  transform: {
    '^.+\\.tsx?$': ['ts-jest', { tsconfig: 'tsconfig.json' }],
  },
  moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
};
