/** @type {import('jest').Config} */
module.exports = {
    preset: 'ts-jest',
    testEnvironment: 'jsdom',
    roots: ['<rootDir>/DocumentRelationshipViewer'],
    testMatch: ['**/__tests__/**/*.test.tsx', '**/__tests__/**/*.test.ts'],
    moduleNameMapper: {
        // Handle CSS modules and static assets
        '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
        '\\.(jpg|jpeg|png|gif|svg)$': '<rootDir>/__mocks__/fileMock.js',
        // Deep shared-lib imports (`@spaarke/ui-components/dist/...`, #3093) resolve to compiled ESM that
        // ts-jest skips under node_modules; map them onto the shared TS source instead (same fix as
        // SemanticSearchControl/jest.config.js and RegardingResolver/jest.config.js).
        '^@spaarke/ui-components/dist/(.*)$': '<rootDir>/../../shared/Spaarke.UI.Components/src/$1',
        // The shared lib has its own React 19 node_modules; pin React to this PCF's React 16 so a
        // shared source file does not load a second React instance (breaks hooks).
        '^react$': '<rootDir>/node_modules/react',
        '^react-dom$': '<rootDir>/node_modules/react-dom',
        '^react/jsx-runtime$': '<rootDir>/node_modules/react/jsx-runtime',
        // Likewise one Fluent: at runtime Fluent is the platform library (a single instance); unmapped,
        // shared source would load the shared lib's own copy beside this PCF's FluentProvider.
        '^@fluentui/react-components$': '<rootDir>/node_modules/@fluentui/react-components',
    },
    setupFilesAfterEnv: ['<rootDir>/jest.setup.ts'],
    transform: {
        '^.+\\.tsx?$': ['ts-jest', {
            tsconfig: 'tsconfig.json',
            diagnostics: {
                ignoreCodes: [151001]
            }
        }],
    },
    transformIgnorePatterns: [
        '/node_modules/(?!(react-flow-renderer|d3-.*|@fluentui)/)',
    ],
    moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
    collectCoverageFrom: [
        'DocumentRelationshipViewer/components/**/*.{ts,tsx}',
        '!DocumentRelationshipViewer/components/index.ts',
        '!**/*.d.ts',
    ],
    coverageThreshold: {
        global: {
            branches: 60,
            functions: 60,
            lines: 60,
            statements: 60,
        },
    },
    testPathIgnorePatterns: ['/node_modules/', '/out/'],
    globals: {
        'Xrm': undefined,
    },
};
