/**
 * Jest configuration for the Reporting Code Page.
 *
 * Patterned after src/solutions/Notepad/jest.config.cjs (CJS extension because the package is
 * "type": "module"). Node environment: the suites test logic, not rendering. `@spaarke/auth` maps
 * to its source (as AllDocuments / WorkspaceLayoutWizard do) so no dist build is needed.
 */

module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'node',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  moduleNameMapper: {
    '^@spaarke/auth$': '<rootDir>/../../client/shared/Spaarke.Auth/src/index.ts',
  },
  transform: {
    '^.+\\.tsx?$': [
      'ts-jest',
      {
        tsconfig: {
          jsx: 'react',
          esModuleInterop: true,
          allowSyntheticDefaultImports: true,
          module: 'commonjs',
          moduleResolution: 'node',
          strict: true,
        },
      },
    ],
  },
};
