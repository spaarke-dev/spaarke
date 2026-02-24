/**
 * Jest configuration for ScopeConfigEditor PCF
 *
 * @see https://jestjs.io/docs/configuration
 */
module.exports = {
    preset: "ts-jest",
    testEnvironment: "jsdom",
    roots: ["<rootDir>/ScopeConfigEditor"],
    testMatch: [
        "**/__tests__/**/*.test.{ts,tsx}",
        "**/*.test.{ts,tsx}",
    ],
    moduleNameMapper: {
        // Handle CSS imports
        "\\.(css|less|scss|sass)$": "identity-obj-proxy",
        // Mock CodeMirror modules (DOM-heavy, not suitable for unit tests)
        "@codemirror/(.*)": "<rootDir>/__mocks__/codemirrorMock.js",
    },
    setupFilesAfterEnv: ["<rootDir>/jest.setup.ts"],
    transform: {
        "^.+\\.tsx?$": ["ts-jest", {
            tsconfig: "tsconfig.json",
        }],
    },
    moduleFileExtensions: ["ts", "tsx", "js", "jsx", "json"],
    collectCoverageFrom: [
        "ScopeConfigEditor/**/*.{ts,tsx}",
        "!ScopeConfigEditor/**/*.d.ts",
        "!ScopeConfigEditor/generated/**",
        "!ScopeConfigEditor/index.ts",
    ],
    coverageThreshold: {
        global: {
            branches: 50,
            functions: 50,
            lines: 50,
            statements: 50,
        },
    },
    globals: {
        Xrm: {},
    },
};
