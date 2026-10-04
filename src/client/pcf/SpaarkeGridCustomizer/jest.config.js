/**
 * Jest configuration for the SpaarkeGridCustomizer PCF (unified-access-control-r2 task 168 f1).
 *
 * The tests drive the real override functions the grid receives (customizers/*) and the control's
 * init through its real entry module. `generated/ManifestTypes` is emitted by the PCF build
 * (gitignored); it is mapped to a stub so the tests compile without a prior build.
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>'],
  testMatch: ['**/__tests__/**/*.test.{ts,tsx}'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': '<rootDir>/__tests__/__mocks__/styleMock.ts',
    'generated/ManifestTypes$': '<rootDir>/__tests__/__mocks__/manifestTypes.ts',
  },
  transform: {
    '^.+\\.tsx?$': ['ts-jest', { tsconfig: { jsx: 'react', esModuleInterop: true, strict: true, target: 'ES2017', module: 'commonjs', skipLibCheck: true, types: ['jest', 'node', 'powerapps-component-framework'] } }],
  },
  moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
};
