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
