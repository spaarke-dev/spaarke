import eslintjs from "@eslint/js";
import microsoftPowerApps from "@microsoft/eslint-plugin-power-apps";
import pluginPromise from "eslint-plugin-promise";
import reactPlugin from "eslint-plugin-react";
import reactHooksPlugin from "eslint-plugin-react-hooks";
import globals from "globals";
import typescriptEslint from "typescript-eslint";

/** @type {import('eslint').Linter.Config[]} */
export default [
  {
    ignores: [
      "**/generated/",
      "**/Solution/",
      "**/*.mjs",
      "**/*.js",
      "**/__tests__/**",
      "**/stories/**",
      "**/.storybook/**",
      "**/jest.setup.*",
      "**/jest.config.*",
      "**/out/",
      "**/obj/",
      "**/bin/",
      "**/node_modules/",
    ],
  },
  eslintjs.configs.recommended,
  ...typescriptEslint.configs.recommended,
  ...typescriptEslint.configs.stylistic,
  pluginPromise.configs["flat/recommended"],
  microsoftPowerApps.configs.paCheckerHosted,
  reactPlugin.configs.flat.recommended,
  {
    files: ["**/*.ts", "**/*.tsx"],
    plugins: {
      "@microsoft/power-apps": microsoftPowerApps,
      "react-hooks": reactHooksPlugin,
    },

    languageOptions: {
      globals: {
        ...globals.browser,
        ComponentFramework: true,
      },
      parserOptions: {
        ecmaVersion: 2020,
        sourceType: "module",
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
        ecmaFeatures: {
          jsx: true,
        },
      },
    },

    settings: {
      react: {
        version: "16.14",
      },
    },

    rules: {
      // === TypeScript strictening ===
      "@typescript-eslint/no-explicit-any": "warn",
      "@typescript-eslint/no-unused-vars": [
        "warn",
        {
          argsIgnorePattern: "^_",
          varsIgnorePattern: "^_",
          caughtErrorsIgnorePattern: "^_",
        },
      ],
      "@typescript-eslint/no-empty-function": "warn",
      "@typescript-eslint/array-type": "warn",
      "@typescript-eslint/consistent-generic-constructors": "warn",
      "@typescript-eslint/consistent-indexed-object-style": "warn",
      "@typescript-eslint/no-inferrable-types": "warn",
      "@typescript-eslint/prefer-function-type": "warn",

      // === React rules ===
      "react/jsx-uses-react": "error",
      "react/jsx-uses-vars": "error",
      "react/prop-types": "off", // Using TypeScript for prop validation
      "react/react-in-jsx-scope": "off", // Not needed with JSX transform
      "react/no-unescaped-entities": "warn", // Warn on unescaped entities, not error

      // === React Hooks rules ===
      "react-hooks/rules-of-hooks": "error",
      "react-hooks/exhaustive-deps": "warn",

      // === Promise rules (downgrade from error to warn for gradual adoption) ===
      "promise/always-return": "warn",
      "promise/catch-or-return": "warn",

      // === Code quality ===
      "no-var": "error",
      "prefer-const": "warn",
      "no-debugger": "warn",

      // === ADR-021: No hard-coded colors in TSX (Fluent UI v9 design system) ===
      "no-restricted-syntax": [
        "warn",
        {
          selector:
            'JSXAttribute[name.name="style"] Literal[value=/(?:^|[^&])#[0-9a-fA-F]{3,8}(?:\\b|$)/]',
          message:
            "ADR-021: Avoid hard-coded hex colors. Use Fluent UI v9 design tokens instead.",
        },
      ],

      // === ADR-022: No React 18 createRoot in PCF controls ===
      "no-restricted-imports": [
        "warn",
        {
          paths: [
            {
              name: "react-dom/client",
              message:
                "ADR-022: PCF controls use React 16 (platform-provided). Do not import from react-dom/client. Use ReactDOM.render() instead.",
            },
          ],
        },
      ],
    },
  },
];
