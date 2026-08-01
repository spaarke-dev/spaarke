import type { CSSProperties } from 'react';

/**
 * SprkModal size scale — the canonical named sizes for the Spaarke modal system
 * (spec FR-02 / design §6.2), derived from the build-resolution target: baseline
 * 1440×900, hard floor 1280×720, upper 2560×1440.
 *
 * Technique: width = min(cap·uiScale px, N·vw); height = min(N·vh, heightMax·uiScale px).
 *  - The `vw` clamp means scaling UP can never clip on the 1280 floor.
 *  - The px width cap keeps the modal from stretching to unreadable widths on a 27".
 *  - The px HEIGHT cap (`heightMax`) holds the landscape aspect on TALL monitors —
 *    without it, `72vh` on a 2560×1440 panel grows to ~1037px and the 1040-wide `md`
 *    reads square (owner UAT 2026-07-31). The cap holds the rectangle.
 *
 * `uiScale` is the numeric `--sprk-ui-scale` factor (1 = 100%); the host owns it and
 * passes the SAME value here and to `scaleTheme` so layout and Fluent internals grow
 * together (FR-06). The px caps are PRE-MULTIPLIED (no CSS `zoom`, no `calc(var())`) —
 * per FR-02 the width is `min(cap·uiScale px, N·vw)`.
 */
export type SprkModalSize = 'xs' | 'sm' | 'md' | 'lg' | 'xl' | 'full' | 'wizard';
export type SprkModalLayout = 'portrait' | 'landscape';

interface SizeSpec {
  /** Fixed px width cap (multiplied by uiScale). Omit → pure viewport width. */
  cap?: number;
  /** Viewport-width clamp (vw). */
  widthVw: number;
  /** Height (vh string), or undefined for content-height. */
  height?: string;
  /** Px height cap (× uiScale) — holds landscape aspect on tall monitors. */
  heightMax?: number;
  layout: SprkModalLayout;
  note: string;
}

export const SIZE_SPEC: Record<SprkModalSize, SizeSpec> = {
  xs: { cap: 480, widthVw: 92, layout: 'portrait', note: 'confirms · deletes · HITL' },
  sm: { cap: 560, widthVw: 92, layout: 'portrait', note: 'simple form · single choice' },
  md: { cap: 1040, widthVw: 92, height: '72vh', heightMax: 720, layout: 'landscape', note: 'forms · compose · quick-start' },
  lg: { cap: 1280, widthVw: 94, height: '85vh', heightMax: 880, layout: 'landscape', note: 'rich content + sidebar (preview)' },
  // xl width is viewport-relative, so it grows WIDE with the viewport and stays
  // landscape on its own — no height cap needed.
  xl: { widthVw: 92, height: '88vh', layout: 'landscape', note: 'near-full iframe / app host' },
  full: { widthVw: 100, height: '100vh', layout: 'landscape', note: 'maximized state of any size' },
  wizard: { widthVw: 62, height: '74vh', heightMax: 760, layout: 'landscape', note: 'wizard (stepper + content)' },
};

export const SIZE_ORDER: SprkModalSize[] = ['xs', 'sm', 'md', 'lg', 'xl', 'wizard', 'full'];

function heightExpr(s: SizeSpec, uiScale: number): string | undefined {
  if (!s.height) return undefined;
  return s.heightMax ? `min(${s.height}, ${Math.round(s.heightMax * uiScale)}px)` : s.height;
}

/**
 * Build the surface style (width/height + viewport caps) for a named size at a
 * given `uiScale`. Consumed by the `SprkModal` base shell and every preset.
 *
 * @param size - Named size (xs/sm/md/lg/xl/full/wizard).
 * @param uiScale - The `--sprk-ui-scale` factor (1 = 100%). Defaults to 1.
 */
export function getSurfaceStyle(size: SprkModalSize, uiScale = 1): CSSProperties {
  const s = SIZE_SPEC[size];
  const width =
    s.cap !== undefined
      ? `min(${Math.round(s.cap * uiScale)}px, ${s.widthVw}vw)`
      : `${s.widthVw}vw`;
  const isFull = size === 'full';
  return {
    width,
    maxWidth: isFull ? '100vw' : '96vw',
    height: heightExpr(s, uiScale),
    maxHeight: isFull ? '100vh' : '92vh',
  };
}

/**
 * Human-readable "W × H" label for a size at a scale — used by tests/harness to
 * assert the computed caps.
 */
export function widthLabel(size: SprkModalSize, uiScale = 1): string {
  const s = SIZE_SPEC[size];
  const w =
    s.cap !== undefined
      ? `min(${Math.round(s.cap * uiScale)}px, ${s.widthVw}vw)`
      : `${s.widthVw}vw`;
  return `${w} × ${heightExpr(s, uiScale) ?? 'auto'}`;
}
