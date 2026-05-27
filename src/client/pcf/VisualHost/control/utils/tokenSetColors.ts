/**
 * Shared token-set color resolver
 *
 * Maps a `ColorTokenSet` name (brand/warning/danger/success/neutral) to a
 * common bag of Fluent UI v9 semantic tokens. All tokens auto-adapt to
 * light/dark mode via FluentProvider.
 *
 * Extracted from `MetricCardMatrix.tsx` (originally line 138) so that other
 * renderers (DonutChart, HorizontalStackedBar, GaugeVisual, …) consume one
 * shared vocabulary instead of duplicating the switch. Per project task 020 +
 * FR-VH-01: REUSE — do not duplicate.
 *
 * The return shape is the superset of all consumer needs:
 *   - cardBackground — light tinted background (used by MetricCardMatrix)
 *   - borderAccent  — strong accent stroke (used by every renderer)
 *   - valueText     — foreground for the headline number
 *   - iconColor     — foreground for icons
 *
 * Consumers may ignore any field they don't need.
 */

import { tokens } from '@fluentui/react-components';
import type { ColorTokenSet } from '../types';

/**
 * Resolved color tokens for a single token-set name.
 * All values are Fluent v9 `tokens.*` references — never hex/rgb literals.
 */
export interface ITokenSetColors {
  cardBackground?: string;
  borderAccent?: string;
  valueText?: string;
  iconColor?: string;
}

/**
 * Resolve a `ColorTokenSet` name to its semantic token bag.
 *
 * @param tokenSet - One of: brand | warning | danger | success | neutral
 * @returns Token references for background, border, value text, and icon color
 */
export function getTokenSetColors(tokenSet: ColorTokenSet): ITokenSetColors {
  switch (tokenSet) {
    case 'brand':
      return {
        cardBackground: tokens.colorBrandBackground2,
        borderAccent: tokens.colorBrandBackground,
        valueText: tokens.colorBrandForeground1,
        iconColor: tokens.colorBrandForeground2,
      };
    case 'warning':
      return {
        cardBackground: tokens.colorPaletteYellowBackground1,
        borderAccent: tokens.colorPaletteYellowBorderActive,
        valueText: tokens.colorPaletteYellowForeground2,
        iconColor: tokens.colorPaletteYellowForeground2,
      };
    case 'danger':
      return {
        cardBackground: tokens.colorPaletteRedBackground1,
        borderAccent: tokens.colorPaletteRedBorderActive,
        valueText: tokens.colorPaletteRedForeground1,
        iconColor: tokens.colorPaletteRedForeground1,
      };
    case 'success':
      return {
        cardBackground: tokens.colorPaletteGreenBackground1,
        borderAccent: tokens.colorPaletteGreenBorderActive,
        valueText: tokens.colorPaletteGreenForeground1,
        iconColor: tokens.colorPaletteGreenForeground1,
      };
    case 'neutral':
    default:
      return {
        cardBackground: tokens.colorNeutralBackground3,
        borderAccent: tokens.colorNeutralStroke1,
        valueText: tokens.colorNeutralForeground3,
        iconColor: tokens.colorNeutralForeground3,
      };
  }
}
