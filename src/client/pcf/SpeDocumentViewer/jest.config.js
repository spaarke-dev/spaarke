/**
 * Jest configuration for the SpeDocumentViewer PCF.
 * Tests cover the auth bootstrap (tenant-specific authority, fail-closed) and the
 * BFF client's URL construction — the parts task 124 changed. No React rendering.
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/control'],
  testMatch: ['**/__tests__/**/*.test.ts'],
  transform: {
    '^.+\.tsx?$': ['ts-jest', { tsconfig: 'tsconfig.test.json' }],
  },
  moduleFileExtensions: ['ts', 'tsx', 'js', 'json'],
};
