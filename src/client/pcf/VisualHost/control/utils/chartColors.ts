/**
 * Chart Color Utilities
 * Provides themed color palettes for chart components using Fluent UI v9 tokens
 * Per ADR-021: MUST NOT hard-code colors; MUST use Fluent design tokens
 */

import { tokens } from "@fluentui/react-components";

/**
 * Chart color palette type
 */
export interface IChartColorPalette {
  /** Primary colors for data series */
  series: string[];
  /** Semantic colors for status */
  status: {
    success: string;
    warning: string;
    error: string;
    info: string;
    neutral: string;
  };
  /** Axis and grid colors */
  axis: {
    line: string;
    text: string;
    gridLine: string;
  };
  /** Background colors */
  background: {
    chart: string;
    tooltip: string;
    legend: string;
  };
  /** Text colors */
  text: {
    primary: string;
    secondary: string;
    onBrand: string;
  };
}

/**
 * Get standard chart color palette using Fluent design tokens
 * These tokens automatically adapt to light/dark/high-contrast themes
 */
export function getChartColorPalette(): IChartColorPalette {
  return {
    // Series colors for categorical data (8 distinct colors)
    series: [
      tokens.colorBrandBackground,
      tokens.colorPaletteBlueBorderActive,
      tokens.colorPaletteTealBorderActive,
      tokens.colorPaletteGreenBorderActive,
      tokens.colorPaletteYellowBorderActive,
      tokens.colorPaletteDarkOrangeBorderActive,
      tokens.colorPaletteRedBorderActive,
      tokens.colorPalettePurpleBorderActive,
    ],

    // Semantic status colors
    status: {
      success: tokens.colorPaletteGreenBackground3,
      warning: tokens.colorPaletteYellowBackground3,
      error: tokens.colorPaletteRedBackground3,
      info: tokens.colorPaletteBlueBorderActive,
      neutral: tokens.colorNeutralBackground4,
    },

    // Axis styling
    axis: {
      line: tokens.colorNeutralStroke1,
      text: tokens.colorNeutralForeground2,
      gridLine: tokens.colorNeutralStroke2,
    },

    // Background styling
    background: {
      chart: tokens.colorNeutralBackground1,
      tooltip: tokens.colorNeutralBackground1,
      legend: tokens.colorNeutralBackground2,
    },

    // Text styling
    text: {
      primary: tokens.colorNeutralForeground1,
      secondary: tokens.colorNeutralForeground2,
      onBrand: tokens.colorNeutralForegroundOnBrand,
    },
  };
}

/**
 * Get extended color palette for larger data sets (16 colors)
 */
export function getExtendedChartColorPalette(): string[] {
  return [
    // Primary 8 colors
    tokens.colorBrandBackground,
    tokens.colorPaletteBlueBorderActive,
    tokens.colorPaletteTealBorderActive,
    tokens.colorPaletteGreenBorderActive,
    tokens.colorPaletteYellowBorderActive,
    tokens.colorPaletteDarkOrangeBorderActive,
    tokens.colorPaletteRedBorderActive,
    tokens.colorPalettePurpleBorderActive,
    // Extended 8 colors (foreground variants for contrast)
    tokens.colorBrandForeground1,
    tokens.colorPaletteBlueForeground2,
    tokens.colorPaletteTealForeground2,
    tokens.colorPaletteGreenForeground2,
    tokens.colorPaletteYellowForeground2,
    tokens.colorPaletteDarkOrangeForeground2,
    tokens.colorPaletteRedForeground2,
    tokens.colorPalettePurpleForeground2,
  ];
}

/**
 * Get status-specific color palette for status-based charts
 */
export function getStatusColorPalette(): Record<string, string> {
  return {
    // Active/Success states
    active: tokens.colorPaletteGreenBackground3,
    success: tokens.colorPaletteGreenBackground3,
    complete: tokens.colorPaletteGreenBackground3,
    approved: tokens.colorPaletteGreenBackground3,

    // Warning/Pending states
    pending: tokens.colorPaletteYellowBackground3,
    warning: tokens.colorPaletteYellowBackground3,
    inProgress: tokens.colorPaletteYellowBackground3,
    review: tokens.colorPaletteYellowBackground3,

    // Error/Critical states
    error: tokens.colorPaletteRedBackground3,
    failed: tokens.colorPaletteRedBackground3,
    rejected: tokens.colorPaletteRedBackground3,
    cancelled: tokens.colorPaletteRedBackground3,

    // Info/Neutral states
    info: tokens.colorPaletteBlueBorderActive,
    new: tokens.colorPaletteBlueBorderActive,
    draft: tokens.colorNeutralBackground4,
    inactive: tokens.colorNeutralBackground4,
    closed: tokens.colorNeutralBackground4,

    // Default fallback
    default: tokens.colorNeutralBackground4,
  };
}

/**
 * Get a color for a specific status value
 * Matches common status field values to appropriate colors
 */
export function getStatusColor(status: string | number | undefined): string {
  if (status === undefined || status === null) {
    return tokens.colorNeutralBackground4;
  }

  const normalizedStatus = String(status).toLowerCase().trim();
  const palette = getStatusColorPalette();

  return palette[normalizedStatus] || palette.default;
}

/**
 * Get color by index from the series palette (cycles through)
 */
export function getSeriesColor(index: number): string {
  const palette = getChartColorPalette().series;
  return palette[index % palette.length];
}

/**
 * Apply color to data points if not already specified
 */
export function applyChartColors<T extends { color?: string }>(
  dataPoints: T[],
  getIndex?: (item: T, index: number) => number
): T[] {
  return dataPoints.map((point, index) => ({
    ...point,
    color: point.color || getSeriesColor(getIndex ? getIndex(point, index) : index),
  }));
}
