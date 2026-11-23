/**
 * Jest config for the AP Export code page (ebilling-r1 task 151b).
 *
 * Patterned after src/solutions/Reporting/jest.config.cjs: a node environment, because the suites test the page's
 * logic (the API client, the export refusals, the per-invoice Mark Paid outcomes, the download URL and the ribbon script), not
 * rendering. `@spaarke/auth` maps to its source (as Reporting does) so no dist build is needed.
 *
 * Run: `npm install --legacy-peer-deps --no-audit --no-fund && npm test` from this folder.
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'node',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.ts'],
  moduleNameMapper: {
    '^@spaarke/auth$': '<rootDir>/../../client/shared/Spaarke.Auth/src/index.ts',
  },
  transform: {
    '^.+\.tsx?$': [
      'ts-jest',
      {
        tsconfig: {
          jsx: 'react',
          esModuleInterop: true,
          allowSyntheticDefaultImports: true,
          module: 'commonjs',
          moduleResolution: 'node',
          strict: true,
          isolatedModules: true,
          skipLibCheck: true,
          types: ['jest'],
        },
      },
    ],
  },
};
