module.exports = {
  root: true,
  env: {
    browser: true,
    es2021: true,
  },
  extends: [
    'eslint:recommended',
    'plugin:@typescript-eslint/recommended',
    'plugin:react/recommended',
    'plugin:react-hooks/recommended',
  ],
  parser: '@typescript-eslint/parser',
  parserOptions: {
    ecmaVersion: 'latest',
    sourceType: 'module',
    ecmaFeatures: {
      jsx: true,
    },
  },
  plugins: ['@typescript-eslint', 'react', 'react-hooks'],
  settings: {
    react: {
      version: 'detect',
    },
  },
  rules: {
    // React 18 doesn't require React import in JSX files
    'react/react-in-jsx-scope': 'off',
    // Allow any for Office.js interop
    '@typescript-eslint/no-explicit-any': 'warn',
    // Warn on unused vars but allow underscore prefix
    '@typescript-eslint/no-unused-vars': [
      'warn',
      { argsIgnorePattern: '^_', varsIgnorePattern: '^_' },
    ],
    // Allow empty functions for event handlers
    '@typescript-eslint/no-empty-function': 'warn',
  },
  globals: {
    Office: 'readonly',
    Word: 'readonly',
  },
};
