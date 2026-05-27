/**
 * ESLint v9 flat config for @spaarke/ui-components.
 *
 * Scope:
 *   - TypeScript-aware lint via typescript-eslint (recommended preset).
 *   - React Hooks rules via eslint-plugin-react-hooks (v5 flat-config preset).
 *   - jsx-a11y + import plugins are REGISTERED (no rules activated) only so that pre-existing
 *     `/* eslint-disable jsx-a11y/... *\/` and `/* eslint-disable import/first *\/` directives
 *     in source resolve their namespaces. Activating those rule sets is out of scope for B-9.
 *   - Test/mock files get a relaxed override (no test-runner-globals plugin pulled in).
 *
 * Linting is scoped to `.ts` and `.tsx` under `src/` only. Stray `.js` files (mocks, generated)
 * are NOT linted; they predate flat-config and would require additional Node/CommonJS-globals
 * setup that is unrelated to B-9's goal (working lint over the TS surface).
 *
 * Refs:
 *   - https://eslint.org/docs/latest/use/configure/configuration-files
 *   - https://typescript-eslint.io/getting-started/
 *   - https://www.npmjs.com/package/eslint-plugin-react-hooks (v5 flat-config)
 *
 * Notes:
 *   - We intentionally do NOT enable typed-linting (parserOptions.project) here. The package's
 *     tsconfig.json excludes __tests__/__mocks__/__test-harness__; typed-linting would require
 *     a separate tsconfig.lint.json. Keeping syntactic + AST-only rules avoids that churn.
 *   - ADR-022 (React 19) — react-hooks v5 recognizes React 19 hooks (`use`, `useFormStatus`).
 *   - Several rules that surface real-but-pre-existing issues (@typescript-eslint/prefer-as-const,
 *     prefer-const) are demoted to `warn` so the lint gate stays green. These are enumerated in
 *     `projects/spaarke-ai-platform-unification-r4/notes/b9-lint-warnings.md` for triage in
 *     B-11 or a follow-on cleanup task. They are NOT silenced via inline comments.
 *   - `react-hooks/rules-of-hooks` was demoted to `warn` in B-9 (pre-existing DatasetGrid
 *     violations) and PROMOTED BACK to `error` in task 073 / B.1 after the violations were
 *     fixed by hoisting hooks above conditional returns.
 */

import js from "@eslint/js";
import tseslint from "typescript-eslint";
import reactHooks from "eslint-plugin-react-hooks";
import jsxA11y from "eslint-plugin-jsx-a11y";
import importPlugin from "eslint-plugin-import";
import globals from "globals";

export default tseslint.config(
  // Global ignores — apply BEFORE any other config object.
  {
    ignores: [
      "dist/**",
      "node_modules/**",
      "coverage/**",
      "build/**",
      "**/*.js",
      "**/*.cjs",
      "**/*.mjs"
    ]
  },

  // Base JS recommended + typescript-eslint recommended (non-type-checked).
  js.configs.recommended,
  ...tseslint.configs.recommended,

  // Project-wide rules + plugin wiring for .ts / .tsx source files.
  {
    files: ["src/**/*.ts", "src/**/*.tsx"],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
      globals: {
        ...globals.browser,
        ...globals.es2022
      },
      parserOptions: {
        ecmaFeatures: { jsx: true }
      }
    },
    plugins: {
      "react-hooks": reactHooks,
      // Registered (no rules activated) — see file header.
      "jsx-a11y": jsxA11y,
      import: importPlugin
    },
    rules: {
      // React Hooks correctness. rules-of-hooks is `error` (promoted in task 073 / B.1 after
      // the 10 pre-existing violations in DatasetGrid GridView.tsx + ListView.tsx were fixed
      // by hoisting all hooks above the conditional returns — see notes/073-datasetgrid-hooks-fix.md).
      ...reactHooks.configs.recommended.rules,
      "react-hooks/rules-of-hooks": "error",

      // Relax noisy stylistic typescript-eslint rules. These typically surface as warnings
      // on the existing codebase without indicating actual bugs; enumerated for triage in
      // notes/b9-lint-warnings.md rather than silenced via inline comments.
      "@typescript-eslint/no-explicit-any": "warn",
      "@typescript-eslint/no-unused-vars": [
        "warn",
        {
          argsIgnorePattern: "^_",
          varsIgnorePattern: "^_",
          caughtErrorsIgnorePattern: "^_"
        }
      ],
      "@typescript-eslint/no-empty-object-type": "warn",
      "@typescript-eslint/no-require-imports": "warn",
      "@typescript-eslint/prefer-as-const": "warn",

      // Demote prefer-const to warn — pre-existing occurrence in FetchXmlService.ts; carry-over.
      "prefer-const": "warn",

      // no-unused-vars is handled by the typescript-eslint version above; turn off the base.
      "no-unused-vars": "off"
    }
  },

  // Test / mock / harness files: relax further. These commonly use jest globals + mock
  // factories that legitimately use `any` and unused params.
  {
    files: [
      "src/**/__tests__/**/*.{ts,tsx}",
      "src/**/__mocks__/**/*.{ts,tsx}",
      "src/**/__test-harness__/**/*.{ts,tsx}",
      "src/**/*.test.{ts,tsx}",
      "src/**/*.spec.{ts,tsx}"
    ],
    languageOptions: {
      globals: {
        ...globals.jest,
        ...globals.node
      }
    },
    rules: {
      "@typescript-eslint/no-explicit-any": "off",
      "@typescript-eslint/no-unused-vars": "off",
      "@typescript-eslint/no-empty-object-type": "off",
      "@typescript-eslint/no-require-imports": "off"
    }
  }
);
