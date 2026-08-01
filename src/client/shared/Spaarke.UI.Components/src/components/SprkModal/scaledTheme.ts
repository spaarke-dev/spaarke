import { webLightTheme, webDarkTheme, type Theme } from '@fluentui/react-components';

/**
 * scaleTheme — the "scaled Fluent theme" arm of the `--sprk-ui-scale` mechanism
 * (spec FR-06 / design §6.9). Clones a Fluent `Theme` and multiplies every px-valued
 * token in the size-driving FAMILIES (font sizes, line heights, spacing, stroke
 * widths, border radii) by `scale`, rounding to 2 decimals. Colors and every
 * non-px / non-size token are left byte-identical.
 *
 * This is what makes Fluent's OWN internals (button padding, input height, text)
 * grow at 2K/4K — a root `font-size`/rem trick cannot, because Fluent v9 tokens are
 * fixed px, not rem. CSS `zoom` (the rejected alternative) under-scales a portaled
 * `position:fixed` dialog at 4K, so it is NOT used.
 *
 * Pure function (ADR-028): the host owns which base theme + scale to pass; this
 * snapshots no host/auth state. `scale === 1` short-circuits to the base identity.
 */
const SCALED_TOKEN_PREFIXES = ['fontSize', 'lineHeight', 'spacing', 'strokeWidth', 'borderRadius'];

export function scaleTheme(base: Theme, scale: number): Theme {
  if (scale === 1) return base;
  const out: Record<string, unknown> = { ...base };
  for (const [key, value] of Object.entries(base)) {
    if (
      typeof value === 'string' &&
      value.endsWith('px') &&
      SCALED_TOKEN_PREFIXES.some((p) => key.startsWith(p))
    ) {
      const n = parseFloat(value);
      if (!Number.isNaN(n)) {
        out[key] = `${Math.round(n * scale * 100) / 100}px`;
      }
    }
  }
  return out as Theme;
}

/** The base (unscaled) Fluent theme for a surface — dark or light. */
export function baseTheme(dark: boolean): Theme {
  return dark ? webDarkTheme : webLightTheme;
}
