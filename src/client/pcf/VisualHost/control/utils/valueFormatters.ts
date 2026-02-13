/**
 * Value Formatters for MetricCard display
 * Centralizes value formatting logic for all card visual types.
 * Each format function converts a numeric value to a display string.
 */

import type { ValueFormatType } from "../types";
import { gradeValueToLetter } from "./gradeUtils";

/**
 * Format a short number with K/M suffixes (e.g., 1000 → "1K", 1500000 → "1.5M")
 */
export function formatShortNumber(value: number): string {
  if (value >= 1000000) {
    return `${(value / 1000000).toFixed(1)}M`;
  }
  if (value >= 1000) {
    return `${(value / 1000).toFixed(1)}K`;
  }
  return value.toLocaleString();
}

/**
 * Format a value as a percentage (value is 0-1 decimal, displayed as "85%")
 */
export function formatPercentage(value: number): string {
  return `${Math.round(value * 100)}%`;
}

/**
 * Format a value as a whole number with locale grouping (e.g., 1234 → "1,234")
 */
export function formatWholeNumber(value: number): string {
  return Math.round(value).toLocaleString();
}

/**
 * Format a value with 2 decimal places (e.g., 3.14159 → "3.14")
 */
export function formatDecimal(value: number): string {
  return value.toFixed(2);
}

/**
 * Format a value as currency (e.g., 1234 → "$1,234")
 */
export function formatCurrency(value: number): string {
  return `$${value.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 0 })}`;
}

/**
 * Format a value using the specified format type.
 * Central dispatch for all MetricCard value formatting.
 *
 * @param value - Numeric value to format (null/undefined → nullDisplay)
 * @param format - Format type string
 * @param nullDisplay - Text to show when value is null/undefined (default: "—")
 * @returns Formatted display string
 */
export function formatValue(
  value: number | null | undefined,
  format: ValueFormatType,
  nullDisplay: string = "—"
): string {
  if (value === null || value === undefined) return nullDisplay;

  switch (format) {
    case "letterGrade":
      return gradeValueToLetter(value);
    case "percentage":
      return formatPercentage(value);
    case "wholeNumber":
      return formatWholeNumber(value);
    case "decimal":
      return formatDecimal(value);
    case "currency":
      return formatCurrency(value);
    case "shortNumber":
    default:
      return formatShortNumber(value);
  }
}
