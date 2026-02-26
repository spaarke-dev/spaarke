module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>'],
  testMatch: ['**/*.test.ts'],
  transform: {
    '^.+\\.tsx?$': [
      'ts-jest',
      {
        diagnostics: false,
        tsconfig: {
          jsx: 'react-jsx',
          esModuleInterop: true,
          allowSyntheticDefaultImports: true,
          module: 'commonjs',
          strict: false,
          noImplicitAny: false,
          moduleResolution: 'node',
          target: 'ES2022',
          lib: ['ES2022', 'DOM'],
        },
      },
    ],
  },
  setupFilesAfterEnv: ['<rootDir>/jest.setup.js'],
  coverageThreshold: {
    global: {
      statements: 80,
      branches: 75,
      functions: 80,
      lines: 80,
    },
  },
};
