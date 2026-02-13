/**
 * @deprecated v1.2.33 — Static configurations replaced by Dataverse chart definition records.
 * Use the enhanced MetricCard visual type with ICardConfig instead. See metriccard-enhancement-design.md.
 *
 * Matter Report Card - Trend Card Configurations (DEPRECATED)
 *
 * Defines the 3 trend card instances for the Report Card tab:
 * Guidelines, Budget, and Outcomes. Each configuration maps an
 * area name to its Dataverse field bindings for historical average
 * and trend data arrays returned by the calculator API.
 *
 * Used by the VisualHost PCF control to render TrendCard components
 * with the correct data bindings per performance area.
 */

import type { TrendDirection } from "../components/TrendCard";
export type { TrendDirection };

export { calculateSlope, getTrendDirection } from "../utils/trendAnalysis";

/**
 * Configuration for a single TrendCard instance on the Report Card tab.
 */
export interface ITrendCardConfig {
  /** Display name for the performance area */
  areaName: string;

  /** Dataverse field name containing the historical average grade (decimal 0.00-1.00) */
  averageField: string;

  /** Dataverse field name or API response key containing the trend data array (number[]) */
  trendDataField: string;

  /** Human-readable description of what this trend card shows */
  description: string;
}

/**
 * Guidelines compliance trend card configuration.
 * Binds to the guidelines compliance grade average and trend data
 * from the calculator API response.
 */
export const GUIDELINES_TREND_CONFIG: ITrendCardConfig = {
  areaName: "Guidelines",
  averageField: "sprk_guidelinecompliancegrade_average",
  trendDataField: "sprk_guidelinecompliancegrade_trend",
  description: "Guidelines compliance trend over time",
};

/**
 * Budget compliance trend card configuration.
 * Binds to the budget compliance grade average and trend data
 * from the calculator API response.
 */
export const BUDGET_TREND_CONFIG: ITrendCardConfig = {
  areaName: "Budget",
  averageField: "sprk_budgetcompliancegrade_average",
  trendDataField: "sprk_budgetcompliancegrade_trend",
  description: "Budget compliance trend over time",
};

/**
 * Outcomes compliance trend card configuration.
 * Binds to the outcomes compliance grade average and trend data
 * from the calculator API response.
 */
export const OUTCOMES_TREND_CONFIG: ITrendCardConfig = {
  areaName: "Outcomes",
  averageField: "sprk_outcomecompliancegrade_average",
  trendDataField: "sprk_outcomecompliancegrade_trend",
  description: "Outcomes compliance trend over time",
};

/**
 * All 3 Report Card trend card configurations in display order.
 * Iterate over this array to render one TrendCard per performance area.
 */
export const REPORT_CARD_TREND_CONFIGS: readonly ITrendCardConfig[] = [
  GUIDELINES_TREND_CONFIG,
  BUDGET_TREND_CONFIG,
  OUTCOMES_TREND_CONFIG,
] as const;
