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
    // `declare global { namespace Office { ... } }` — task 072.
    //
    // This is the ONLY rule option changed to clear the backlog, and it is not a
    // weakening: `no-namespace` exists to steer new code away from TS namespaces in
    // favour of ES2015 modules, and `allowDeclarations` is the rule's own supported
    // option for the case where that advice cannot apply. AMBIENT declarations are
    // that case — augmenting the global `Office` namespace to add
    // `Office.MailboxEnums.Importance` types is impossible with module syntax, which
    // is why `shared/adapters/OutlookAdapter.ts:54-56` uses it and explains itself
    // in a comment above. The declaration is erased at compile time; it emits no
    // runtime namespace, so the shape the rule guards against is not being created.
    // Non-declaration namespaces remain errors.
    '@typescript-eslint/no-namespace': ['error', { allowDeclarations: true }],
  },
  globals: {
    Office: 'readonly',
    Word: 'readonly',
  },
};
