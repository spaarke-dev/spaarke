/**
 * ESLint v9 flat config for the external SPA (@spaarke/secure-project-workspace).
 *
 * Added by unified-access-control-r2 task 140: `npm run lint` (package.json) named eslint but the package declared no
 * eslint dependency and no config, so the script could not run at all ("'eslint' is not recognized"). This mirrors the
 * shared library's flat config (src/client/shared/Spaarke.UI.Components/eslint.config.js) — typescript-eslint
 * recommended + react-hooks — so both packages lint the same way.
 *
 * Linting covers `.ts` / `.tsx` under `src/` and `tests/`. Noisy stylistic rules are warnings (as in the shared library), so the
 * gate fails on real errors — e.g. a hook called conditionally — not on style.
 */

import js from '@eslint/js';
import tseslint from 'typescript-eslint';
import reactHooks from 'eslint-plugin-react-hooks';
import globals from 'globals';

export default tseslint.config(
  {
    ignores: ['dist/**', 'node_modules/**', 'coverage/**', '**/*.js', '**/*.cjs', '**/*.mjs', 'src/**/*.d.ts'],
  },

  js.configs.recommended,
  ...tseslint.configs.recommended,

  {
    files: ['src/**/*.ts', 'src/**/*.tsx'],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: 'module',
      globals: {
        ...globals.browser,
        ...globals.es2022,
      },
      parserOptions: {
        ecmaFeatures: { jsx: true },
      },
    },
    plugins: {
      'react-hooks': reactHooks,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      'react-hooks/rules-of-hooks': 'error',

      '@typescript-eslint/no-explicit-any': 'warn',
      '@typescript-eslint/no-unused-vars': [
        'warn',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_', caughtErrorsIgnorePattern: '^_' },
      ],
      '@typescript-eslint/no-empty-object-type': 'warn',
      '@typescript-eslint/no-require-imports': 'warn',
      '@typescript-eslint/prefer-as-const': 'warn',
      'prefer-const': 'warn',
      'no-unused-vars': 'off',
    },
  },

  {
    // Tests live in tests/ (outside src/, so the SPA's source-scanning ArchTests — e.g.
    // ExternalSpaGridViewSelectorGuardTests — see only shipped code).
    files: ['tests/**/*.{ts,tsx}'],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: 'module',
      globals: {
        ...globals.browser,
        ...globals.node,
      },
      parserOptions: {
        ecmaFeatures: { jsx: true },
      },
    },
    rules: {
      '@typescript-eslint/no-explicit-any': 'off',
      '@typescript-eslint/no-unused-vars': 'off',
    },
  }
);
