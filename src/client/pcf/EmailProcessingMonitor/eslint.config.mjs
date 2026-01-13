import eslint from "@eslint/js";
import tseslint from "typescript-eslint";
import globals from "globals";
import powerAppsPlugin from "@microsoft/eslint-plugin-power-apps";
import promisePlugin from "eslint-plugin-promise";

export default tseslint.config(
  eslint.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ["control/**/*.ts", "control/**/*.tsx"],
    languageOptions: {
      parserOptions: {
        ecmaVersion: 2020,
        sourceType: "module",
        ecmaFeatures: {
          jsx: true,
        },
      },
      globals: {
        ...globals.browser,
        ComponentFramework: "readonly",
      },
    },
    plugins: {
      "@microsoft/power-apps": powerAppsPlugin,
      promise: promisePlugin,
    },
    rules: {
      "@microsoft/power-apps/avoid-2d-context": "warn",
      "@microsoft/power-apps/avoid-browser-specific-api": "warn",
      "@microsoft/power-apps/avoid-dom-form-elements": "warn",
      "@microsoft/power-apps/avoid-id-elements": "warn",
      "@microsoft/power-apps/avoid-iframe": "warn",
      "@microsoft/power-apps/avoid-unpub-api": "warn",
      "@microsoft/power-apps/avoid-window-top": "warn",
      "@microsoft/power-apps/do-not-make-parent-assumption": "warn",
      "@microsoft/power-apps/use-cached-webapi": "warn",
      "promise/catch-or-return": "warn",
      "promise/param-names": "error",
      "@typescript-eslint/no-explicit-any": "warn",
      "@typescript-eslint/no-unused-vars": ["warn", { argsIgnorePattern: "^_" }],
    },
  },
  {
    ignores: ["**/generated/**", "**/node_modules/**", "**/out/**"],
  }
);
