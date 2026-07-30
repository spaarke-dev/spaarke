/**
 * Jest configuration for CommunicationActions PCF.
 *
 * Tests the composer-prefill derivation, the create-launch seam, and the
 * source-attachment enumeration helpers in isolation (task 044/104/113; and
 * task 022's Layer-1 extraction into `@spaarke/communication-components`).
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>'],
  testMatch: ['**/__tests__/**/*.test.{ts,tsx}'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
    // Task 022: the Layer-1 action-bar / composer-prefill / suggested-create logic
    // now lives in the shared lib (`@spaarke/communication-components`). Map the
    // package specifier to the shared TS source so tests exercise the re-homed
    // code directly (mirrors the CommunicationAttachments task-021 pattern — no
    // dist/ build required).
    '^@spaarke/communication-components/logic/actions$':
      '<rootDir>/../../shared/Spaarke.Communication.Components/src/logic/actions/index.ts',
  },
  setupFilesAfterEnv: ['<rootDir>/jest.setup.ts'],
  transform: {
    '^.+\\.tsx?$': ['ts-jest', { tsconfig: 'tsconfig.json' }],
  },
  moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
};
