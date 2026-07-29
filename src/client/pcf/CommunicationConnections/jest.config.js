/**
 * Jest configuration for CommunicationConnections PCF.
 *
 * Tests the provenance derivation helpers + the write handler in isolation.
 * Mocks `@spaarke/ui-components` for `applyResolverFields` + catalog so we can
 * assert the PCF only delegates to the shared service (FR-21 / ADR-024).
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>'],
  testMatch: ['**/__tests__/**/*.test.{ts,tsx}'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
    // Task 020: the Layer-1 connections logic now lives in the shared lib
    // (`@spaarke/communication-components`). Map the package specifiers to the
    // shared TS source so tests exercise the re-homed logic directly. The
    // `/provenance` submodule map lets the pure-provenance tests avoid loading
    // the write handler (which imports `@spaarke/ui-components`).
    '^@spaarke/communication-components/logic/connections/provenance$':
      '<rootDir>/../../shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts',
    '^@spaarke/communication-components/logic/connections$':
      '<rootDir>/../../shared/Spaarke.Communication.Components/src/logic/connections/index.ts',
  },
  setupFilesAfterEnv: ['<rootDir>/jest.setup.ts'],
  transform: {
    '^.+\\.tsx?$': ['ts-jest', { tsconfig: 'tsconfig.json' }],
  },
  moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
};
