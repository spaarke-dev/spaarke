/**
 * Trend analysis utilities for Report Card
 * Calculates linear regression slope and trend direction from grade data
 */

import type { TrendDirection } from "../components/TrendCard";

/** Threshold for trend direction classification (per spec-r1.md FR-09) */
const TREND_THRESHOLD = 0.02;

/**
 * Calculate the slope of a linear regression line through the data points.
 * Uses the least squares method.
 *
 * @param values - Array of numeric values (chronological order, oldest first)
 * @returns Slope of the regression line, or 0 if insufficient data
 *
 * @example
 * calculateSlope([0.85, 0.88, 0.90, 0.92, 0.95]) // ~0.025 (positive slope)
 * calculateSlope([0.95, 0.92, 0.90, 0.88, 0.85]) // ~-0.025 (negative slope)
 */
export function calculateSlope(values: number[]): number {
  const n = values.length;
  if (n < 2) return 0;

  // x values are indices: 0, 1, 2, ..., n-1
  let sumX = 0;
  let sumY = 0;
  let sumXY = 0;
  let sumXX = 0;

  for (let i = 0; i < n; i++) {
    sumX += i;
    sumY += values[i];
    sumXY += i * values[i];
    sumXX += i * i;
  }

  const denominator = n * sumXX - sumX * sumX;
  if (denominator === 0) return 0;

  return (n * sumXY - sumX * sumY) / denominator;
}

/**
 * Determine trend direction from an array of grade values using linear regression.
 *
 * @param values - Array of grade values (chronological order, oldest first)
 * @returns TrendDirection: "up" if slope > 0.02, "down" if slope < -0.02, "flat" otherwise
 *
 * Spec reference: FR-09
 *   slope > 0.02  -> "up" (improving)
 *   slope < -0.02 -> "down" (declining)
 *   else           -> "flat" (stable)
 */
export function getTrendDirection(values: number[]): TrendDirection {
  if (values.length < 2) return "flat";

  const slope = calculateSlope(values);

  if (slope > TREND_THRESHOLD) return "up";
  if (slope < -TREND_THRESHOLD) return "down";
  return "flat";
}
