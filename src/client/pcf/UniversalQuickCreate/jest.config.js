module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/control'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  setupFilesAfterEnv: ['<rootDir>/jest.setup.js'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
    // Map @spaarke/ui-components to shared library source
    '^@spaarke/ui-components/src/(.*)$': '<rootDir>/../../shared/Spaarke.UI.Components/src/$1',
    '^@spaarke/ui-components$': '<rootDir>/../../shared/Spaarke.UI.Components/src/index.ts'
  },
  transform: {
    '^.+\\.tsx?$': ['ts-jest', {
      tsconfig: {
        jsx: 'react',
        esModuleInterop: true,
        allowSyntheticDefaultImports: true
      }
    }]
  },
  transformIgnorePatterns: [
    '/node_modules/(?!(@fluentui|@spaarke)/)'
  ]
};
