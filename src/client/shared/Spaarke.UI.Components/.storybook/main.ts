/**
 * Storybook main configuration — addon wiring + story discovery for the
 * `@spaarke/ui-components` DataGrid framework.
 *
 * **Status**: Storybook is NOT yet installed as a runtime dependency of this
 * package. This configuration file documents the EXPECTED wiring per task 009
 * (Phase A acceptance gate). When the package adopts Storybook (separate task),
 * dropping `@storybook/react`, `@storybook/addon-a11y`, `@storybook/addon-viewport`,
 * and `@storybook/test-runner` into devDependencies makes this file load
 * verbatim. The CSF stories under `../storybook/` already conform to this
 * configuration.
 *
 * Addons required by task 009:
 * - `@storybook/addon-a11y`     — axe-core scan on every story (NFR-04)
 * - `@storybook/addon-viewport`  — zoom-level testing (NFR-01: 75/100/125/150%)
 *
 * Headless axe scan command (CI):
 *   `npx storybook test-runner --url http://localhost:6006`
 * Returns non-zero exit on serious / critical axe-core violations per
 * `@storybook/test-runner`'s default `a11y` rule severity.
 *
 * @see ../storybook/FluentV9NativeFeatures.stories.tsx
 * @see ../storybook/EdgeStates.stories.tsx
 * @see projects/spaarke-datagrid-framework-r1/notes/phase-a-acceptance-gate.md
 */

import type { StorybookConfig } from '@storybook/react-vite';

const config: StorybookConfig = {
  // Pick up every `*.stories.@(ts|tsx)` file in the sibling `storybook/` folder.
  // Stories live OUTSIDE `src/` so the library build (`tsc`) does not include them.
  stories: ['../storybook/**/*.stories.@(ts|tsx)'],

  addons: [
    '@storybook/addon-essentials', // controls, viewport, backgrounds, toolbars
    '@storybook/addon-a11y',       // axe-core scan per story (NFR-04)
    '@storybook/addon-viewport',   // zoom-level / responsive testing (NFR-01)
  ],

  framework: {
    name: '@storybook/react-vite',
    options: {},
  },

  // Required for Fluent v9 + React 16-safe code paths.
  // The DataGrid framework targets React 16.14+ (peerDep) so the runtime
  // bundling stays compatible with PCF hosts as well as React 18+ hosts.
  typescript: {
    reactDocgen: 'react-docgen-typescript',
    reactDocgenTypescriptOptions: {
      shouldExtractLiteralValuesFromEnum: true,
    },
  },

  docs: {
    autodocs: 'tag',
  },
};

export default config;
