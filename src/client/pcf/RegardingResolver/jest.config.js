/**
 * Jest configuration for RegardingResolver PCF.
 *
 * Tests the React component + ResolverWriteHandler in isolation. Mocks
 * `@spaarke/ui-components` for `applyResolverFields` + catalog so we can
 * assert that the PCF only delegates to the shared service (FR-21 / ADR-024).
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
