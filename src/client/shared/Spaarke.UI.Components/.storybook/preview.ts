/**
 * Storybook preview configuration — global parameters applied to every story.
 *
 * **Status**: Storybook is NOT yet installed as a runtime dependency of this
 * package. This file documents the EXPECTED preview wiring per task 009 (Phase A
 * acceptance gate, NFR-01 + NFR-04). When Storybook is adopted, this file loads
 * verbatim and applies these defaults across all `*.stories.tsx` files.
 *
 * Two cross-cutting parameter blocks:
 *
 * 1. `viewport` — zoom-level testing presets (NFR-01 visual-parity gate). The
 *    four named viewports approximate the 4 MDA zoom levels (75/100/125/150%).
 *    Per-story overrides (in `parameters.viewport.viewports`) win over these.
 *
 * 2. `a11y` — addon-a11y configuration. `manual: false` runs axe-core on
 *    every story automatically. The default rule severity treats `serious` and
 *    `critical` violations as build-breaking; `moderate` and `minor` surface
 *    as warnings.
 *
 * @see ./main.ts
 * @see ../storybook/FluentV9NativeFeatures.stories.tsx
 * @see ../storybook/EdgeStates.stories.tsx
 */

import type { Preview } from '@storybook/react';

const preview: Preview = {
  parameters: {
    actions: { argTypesRegex: '^on[A-Z].*' },
    controls: {
      matchers: {
        color: /(background|color)$/i,
        date: /Date$/i,
      },
    },
    a11y: {
      // Run axe-core automatically on every story. Per-story override available
      // via `parameters.a11y.disable = true` or per-story `manual = true`.
      manual: false,
      // Treat serious and critical violations as failures in the test runner.
      config: {
        rules: [
          // Default rule set — no overrides at the global level. Per-story
          // disables go in the per-story `parameters.a11y.config.rules`.
        ],
      },
      options: {
        // axe-core options: include all WCAG 2.1 AA tags.
        runOnly: {
          type: 'tag',
          values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'],
        },
      },
    },
    viewport: {
      // Zoom-level visual-parity presets (NFR-01 acceptance gate). The
      // four viewports below correspond to the 4 MDA zoom levels. Pair with
      // the host-browser zoom (Ctrl + / Ctrl -) during manual MDA review.
      viewports: {
        zoom75: { name: 'Zoom 75%', styles: { width: '1707px', height: '960px' } },
        zoom100: { name: 'Zoom 100%', styles: { width: '1280px', height: '720px' } },
        zoom125: { name: 'Zoom 125%', styles: { width: '1024px', height: '576px' } },
        zoom150: { name: 'Zoom 150%', styles: { width: '853px', height: '480px' } },
      },
      defaultViewport: 'zoom100',
    },
  },
};

export default preview;
